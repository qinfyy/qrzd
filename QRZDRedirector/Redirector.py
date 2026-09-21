"""通过 spawn 和双 Realm 启动 NeoX，启用本地测试账号登录。"""

from __future__ import annotations

import configparser
import base64
import ipaddress
import json
import signal
import sys
import threading
import time
from pathlib import Path
from urllib.parse import urlsplit

import frida


# NeoX 引擎游戏包名：com.netease.qrzd
PACKAGE_NAME = "com.netease.qrzd"
PROJECT_DIRECTORY = Path(__file__).resolve().parent
CONFIG_PATH = PROJECT_DIRECTORY / "Config.ini"
SCRIPT_PATH = PROJECT_DIRECTORY / "dist" / "FridaScript.js"
EMULATED_REALM_ATTACH_DELAY_SECONDS = 8.0
NATIVE_BRIDGE_SETUP_WAIT_SECONDS = 5.0
SCRIPT_CONFIG_WAIT_SECONDS = 10.0
EMULATED_AGENT_PATH = "/data/local/tmp/re.frida.server/frida-agent-arm64.so"

FRIDA_ERRORS = (
    frida.AddressInUseError,
    frida.ExecutableNotFoundError,
    frida.ExecutableNotSupportedError,
    frida.InvalidArgumentError,
    frida.InvalidOperationError,
    frida.NotSupportedError,
    frida.OperationCancelledError,
    frida.PermissionDeniedError,
    frida.ProcessNotFoundError,
    frida.ProcessNotRespondingError,
    frida.ProtocolError,
    frida.ServerNotRunningError,
    frida.TimedOutError,
    frida.TransportError,
)


def is_local_ipv4(value: str) -> bool:
    """调试登录只接入回环或 RFC1918 测试服务，不解析公共域名。"""
    try:
        address = ipaddress.IPv4Address(value)
    except ipaddress.AddressValueError:
        return False
    return any(address in ipaddress.IPv4Network(network) for network in (
        "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
    ))


def load_config() -> dict[str, object]:
    parser = configparser.ConfigParser(interpolation=None)
    if not parser.read(CONFIG_PATH, encoding="utf-8-sig"):
        raise FileNotFoundError(f"找不到配置文件：{CONFIG_PATH}")
    if "Settings" not in parser:
        raise ValueError("Config.ini 缺少 [Settings] 节")

    settings = parser["Settings"]
    values: dict[str, str] = {}
    for name in ("HTTP_URL_BASE", "GATEWAY_HOST", "GATEWAY_IPV6", "GATEWAY_PUBLIC_KEY_FILE"):
        value = settings.get(name, "").strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1].strip()
        values[name] = value

    parsed_url = urlsplit(values["HTTP_URL_BASE"])
    if (
        parsed_url.scheme != "http"
        or not parsed_url.netloc
        or parsed_url.path not in ("", "/")
        or parsed_url.query
        or parsed_url.fragment
        or parsed_url.username is not None
        or parsed_url.password is not None
    ):
        raise ValueError("HTTP_URL_BASE 必须是 HTTP 根地址，例如 http://192.168.0.108:21000")

    gateway_host = values["GATEWAY_HOST"]
    if (
        not gateway_host
        or any(character.isspace() for character in gateway_host)
        or any(character in gateway_host for character in ":/@?#")
    ):
        raise ValueError("GATEWAY_HOST 必须是不带协议和端口的 IPv4 地址或主机名")

    try:
        gateway_port = settings.getint("GATEWAY_PORT")
    except (TypeError, ValueError) as error:
        raise ValueError("GATEWAY_PORT 必须是整数") from error
    if gateway_port is None or not 1 <= gateway_port <= 65535:
        raise ValueError("GATEWAY_PORT 必须在 1 到 65535 之间")

    try:
        emulator = settings.getboolean("EMULATOR", fallback=False)
        use_neox_sdk_bypass = settings.getboolean("USE_NEOX_SDK_BYPASS", fallback=True)
    except ValueError as error:
        raise ValueError(
            "EMULATOR、USE_NEOX_SDK_BYPASS 必须是布尔值"
        ) from error

    client_arch = settings.get("CLIENT_ARCH", "arm64-v8a").strip().lower()
    if (
        len(client_arch) >= 2
        and client_arch[0] == client_arch[-1]
        and client_arch[0] in "\"'"
    ):
        client_arch = client_arch[1:-1].strip()
    if client_arch != "arm64-v8a":
        raise ValueError("当前 NeoX 样本只验证了 ARM64，CLIENT_ARCH 必须为 arm64-v8a")
    if use_neox_sdk_bypass and (
        not is_local_ipv4(gateway_host)
        or not is_local_ipv4(parsed_url.hostname or "")
        or values["GATEWAY_IPV6"]
    ):
        raise ValueError("NeoX SDK Bypass 要求 HTTP/Gateway 使用本地 IPv4 测试地址，IPv6 留空")
    # 提前触发 urlsplit 的非法端口检查，不能等注入后才失败。
    http_port = parsed_url.port if parsed_url.port is not None else 80
    if not 1 <= http_port <= 65535:
        raise ValueError("HTTP_URL_BASE 的端口必须在 1 到 65535 之间")

    gateway_public_key = settings.get("GATEWAY_PUBLIC_KEY", "").strip()
    if not gateway_public_key and values.get("GATEWAY_PUBLIC_KEY_FILE"):
        key_path = (PROJECT_DIRECTORY / values["GATEWAY_PUBLIC_KEY_FILE"]).resolve()
        gateway_public_key = key_path.read_text(encoding="ascii").strip()

    if gateway_public_key:
        lines = [line.strip() for line in gateway_public_key.splitlines() if line.strip()]
        if len(lines) < 3 or lines[0] != "-----BEGIN PUBLIC KEY-----" or lines[-1] != "-----END PUBLIC KEY-----":
            raise ValueError("GATEWAY_PUBLIC_KEY / GATEWAY_PUBLIC_KEY_FILE 必须是本地 Gateway 的 PEM 公钥，不能填写私钥")
        try:
            decoded = base64.b64decode("".join(lines[1:-1]), validate=True)
        except ValueError as error:
            raise ValueError("Gateway PEM 公钥编码无效") from error
        if not 128 <= len(decoded) <= 8192:
            raise ValueError("Gateway PEM 公钥长度无效")
        gateway_public_key = "\n".join(lines)

    return {
        "httpUrlBase": f"http://{parsed_url.netloc}",
        "httpHost": parsed_url.hostname,
        "httpPort": http_port,
        "gatewayHost": gateway_host,
        "gatewayPort": gateway_port,
        "gatewayIpv6": values["GATEWAY_IPV6"],
        "gatewayPublicKey": gateway_public_key,
        "clientArch": client_arch,
        "emulator": emulator,
        "engine": "neox",
        "useNeoXSdkBypass": use_neox_sdk_bypass,
    }


def main() -> int:
    config = load_config()
    if not SCRIPT_PATH.is_file():
        raise RuntimeError("缺少 Frida 编译产物，请先在 BH2Redirector 执行 npm run build")
    if SCRIPT_PATH.stat().st_mtime_ns < (PROJECT_DIRECTORY / "FridaScript.js").stat().st_mtime_ns:
        raise RuntimeError("Frida 编译产物已过期，请先执行 npm run build")
    source = SCRIPT_PATH.read_text(encoding="utf-8")

    stopped = threading.Event()
    native_bridge_ready = threading.Event()
    configured: dict[str, threading.Event] = {}
    failure = threading.Event()
    bypass_ready = threading.Event()
    failure_messages: list[str] = []
    device = frida.get_usb_device(timeout=10)
    pid: int | None = None
    sessions: list[frida.Session] = []
    scripts: list[frida.core.Script] = []
    attached_realms: list[str] = []
    resumed = False

    def on_message(
        message: dict[str, object],
        _data: bytes | None,
        realm_label: str,
    ) -> None:
        prefix = f"[{realm_label}] " if len(realm_specs) > 1 else ""
        if message.get("type") == "send":
            payload = message.get("payload")
            if isinstance(payload, dict):
                event = payload.get("event")
                if event == "native-bridge-ready" and realm_label == "Native Realm":
                    native_bridge_ready.set()
                if event == "script-configured":
                    configured[realm_label].set()
                if event == "neox-bypass-ready" and realm_label == realm_specs[-1][0]:
                    bypass_ready.set()
                if event in ("native-bridge-failed", "neox-bypass-failed", "script-failed"):
                    failure_messages.append(str(payload.get("message", event)))
                    failure.set()
                detail = payload.get("message", json.dumps(payload, ensure_ascii=False))
                print(f"{prefix}{detail}", flush=True)
            else:
                print(f"{prefix}{payload}", flush=True)
            return

        description = message.get("description", "未知 Frida 错误")
        print(f"{prefix}[Frida 错误] {description}", file=sys.stderr, flush=True)
        if stack := message.get("stack"):
            print(stack, file=sys.stderr, flush=True)
        failure_messages.append(str(description))
        failure.set()

    def on_detached(reason: str, crash: object | None, realm_label: str) -> None:
        if crash is None:
            print(f"[{realm_label}] 游戏进程连接已断开：{reason}", flush=True)
        else:
            print(
                f"[{realm_label}] 游戏进程异常退出：{reason}，{crash}",
                file=sys.stderr,
                flush=True,
            )
        stopped.set()
        if reason != "application-requested":
            failure_messages.append(f"{realm_label}: {reason}")
            failure.set()

    def stop(_signum: int, _frame: object) -> None:
        stopped.set()

    signal.signal(signal.SIGINT, stop)
    signal.signal(signal.SIGTERM, stop)

    if config["clientArch"] == "arm64-v8a" and config["emulator"]:
        # Java/Native Bridge 在宿主 Realm；Python 解释器在 ARM64 guest Realm。
        realm_specs = (
            ("Native Realm", "native", "platform"),
            ("Emulated Realm", "emulated", "neox"),
        )
    else:
        realm_specs = (("Native Realm", "native", "all"),)
    configured = {label: threading.Event() for label, _, _ in realm_specs}

    def wait_ready(event: threading.Event, timeout: float | None, description: str) -> None:
        deadline = None if timeout is None else time.monotonic() + timeout
        while not event.is_set():
            if failure.is_set():
                raise RuntimeError(failure_messages[-1])
            if stopped.is_set():
                raise RuntimeError(f"等待{description}时已停止")
            remaining = None if deadline is None else deadline - time.monotonic()
            if remaining is not None and remaining <= 0:
                raise RuntimeError(f"等待{description}超时；未验证成功")
            event.wait(0.1 if remaining is None else min(remaining, 0.1))
        if failure.is_set():
            raise RuntimeError(failure_messages[-1])

    def attach_realm(realm_label: str, realm: str, runtime_role: str) -> None:
        if pid is None:
            raise RuntimeError("游戏进程尚未启动")

        if realm == "emulated":
            session = device.attach(
                pid,
                realm=realm,
                emulated_agent_path=EMULATED_AGENT_PATH,
            )
        else:
            session = device.attach(pid, realm=realm)

        def handle_detached(reason: str, crash: object | None) -> None:
            on_detached(reason, crash, realm_label)

        session.on("detached", handle_detached)
        sessions.append(session)

        script = session.create_script(source)
        scripts.append(script)
        script.on(
            "message",
            lambda message, data: on_message(message, data, realm_label),
        )
        script.load()
        script.post(
            {
                "type": "config",
                "payload": config
                | {
                    "runtimeRole": runtime_role,
                    "emulatedAgentPath": EMULATED_AGENT_PATH,
                },
            }
        )
        attached_realms.append(realm_label)
        wait_ready(configured[realm_label], SCRIPT_CONFIG_WAIT_SECONDS, f"{realm_label}配置握手")

    try:
        print(f"正在通过 spawn 启动 {PACKAGE_NAME}...", flush=True)
        pid = device.spawn([PACKAGE_NAME])
        if len(realm_specs) > 1:
            device.resume(pid)
            resumed = True
            # MuMu 在 Houdini 装载 ARM 库前探测 Emulated Realm 会触发 linker 崩溃。
            if stopped.wait(EMULATED_REALM_ATTACH_DELAY_SECONDS):
                raise RuntimeError("等待 Native Bridge 初始化时游戏进程已退出")
            attach_realm(*realm_specs[0])
            wait_ready(native_bridge_ready, NATIVE_BRIDGE_SETUP_WAIT_SECONDS, "Native Bridge兼容层")
            # 缺少 ARM Realm 就不能运行 NeoX Hook，禁止静默降级为“启动成功”。
            attach_realm(*realm_specs[1])
        else:
            attach_realm(*realm_specs[0])
            device.resume(pid)
            resumed = True

        if config["useNeoXSdkBypass"]:
            print("等待解释器...", flush=True)
            wait_ready(bypass_ready, None, "NeoX SDK Bypass回执")

        realm_description = " + ".join(attached_realms)
        print(
            f"游戏已启动，PID={pid}，架构={config['clientArch']}，"
            f"Realm={realm_description}，HTTP={config['httpUrlBase']}，"
            f"Gateway={config['gatewayHost']}:{config['gatewayPort']}",
            flush=True,
        )
        while not stopped.wait(0.25):
            if failure.is_set():
                raise RuntimeError(failure_messages[-1])
    finally:
        if pid is not None and not resumed:
            try:
                device.resume(pid)
            except FRIDA_ERRORS:
                pass
        for script in reversed(scripts):
            try:
                script.unload()
            except FRIDA_ERRORS:
                pass
        for session in reversed(sessions):
            try:
                session.detach()
            except FRIDA_ERRORS:
                pass

    return 1 if failure.is_set() else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (configparser.Error, OSError, ValueError, RuntimeError) as error:
        print(f"启动失败：{error}", file=sys.stderr)
        sys.exit(1)
    except FRIDA_ERRORS as error:
        print(f"启动失败：{error}", file=sys.stderr)
        sys.exit(1)
