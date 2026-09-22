# BH2Redirector

BH2Redirector 是基于 Frida 的客户端网络流量重定向工具。通过 Frida spawn 启动《崩坏学园2》客户端，将客户端的 SDK 认证 HTTP 请求、AssetBundle/WebView 资源地址以及 Dispatch Gateway TCP 连接透明重定向至本地服务端（`Sv`），并内置了客户端登录字段的 RSA 自动解密旁路（RSA Bypass）。

---

## 依赖环境

- **Python**: 3.10 或更高版本
- **Node.js**: 18.0 或更高版本
- **Frida**: 本地 Python `frida` 模块版本必须与目标设备上运行的 `frida-server` 主版本完全一致
- **Android 设备/模拟器**: 已获得 root 权限并开启 ADB 调试（如 MuMu 模拟器）

---

## 安装与编译

1. **安装 Python 与 Node.js 依赖**：
   ```powershell
   Set-Location BH2Redirector
   py -m pip install -r requirements.txt
   npm install
   ```

2. **编译 Frida 脚本**：
   ```powershell
   npm run build
   ```
   > **说明**：该命令将 `FridaScript.js` 及其依赖打包生成 `dist/FridaScript.js`。修改脚本后必须重新执行编译。

---

## 配置文件说明 (`Config.ini`)

运行前在 `Config.ini` 中配置服务端地址与客户端架构：

```ini
[Settings]
HTTP_URL_BASE=http://127.0.0.1:21000
GATEWAY_HOST=127.0.0.1
GATEWAY_PORT=26000
GATEWAY_IPV6=
CLIENT_ARCH=x86
EMULATOR=false
UES_ZLIB_HOOK=false
USE_JAVA_URL_MODIFY=true
USE_JAVA_BYPASS_RSA=true
```

### 配置项速查

| 配置项 | 说明 | 推荐值 / 示例 |
| :--- | :--- | :--- |
| `HTTP_URL_BASE` | 本地 HTTP 网关根地址（必须以 `http://` 开头，不带末尾斜杠） | `http://192.168.0.x:21000` |
| `GATEWAY_HOST` | Dispatch Gateway 的 IPv4 地址或主机名（不带协议和端口） | `192.168.0.x` |
| `GATEWAY_PORT` | Dispatch Gateway 端口（客户端握手后会自动转向下发的 Game Gateway）| `26000` |
| `GATEWAY_IPV6` | 可选 IPv6 地址，无 IPv6 环境留空 | 留空 |
| `CLIENT_ARCH` | 客户端原生库架构，可选 `x86` 或 `arm64-v8a` | 见下表常见组合 |
| `EMULATOR` | 是否在 x86 模拟器（如 MuMu）中运行 ARM64 客户端（Native Bridge 模式） | 见下表常见组合 |
| `UES_ZLIB_HOOK` | 开启 zlib inflate 与 VDEX/OAT 内存常量改写（历史兼容项） | 默认 `false` |
| `USE_JAVA_URL_MODIFY` | 启用 Java SDK 层 URL 配置对象改写 | 默认 `true` |
| `USE_JAVA_BYPASS_RSA` | 启用 SDK 登录字段 RSA 旁路（以明文向服务端发送账号密码/验证码） | 默认 `true` |

### 常见运行环境组合

| 运行环境与安装包 | `CLIENT_ARCH` | `EMULATOR` |
| :--- | :---: | :---: |
| **MuMu 模拟器 + x86 架构客户端**（推荐，最稳定） | `x86` | `false` |
| **MuMu 模拟器 + ARM64 架构客户端**（Native Bridge 双 Realm 模式） | `arm64-v8a` | `true` |
| **真实 Android 手机/平板 (ARM64)** | `arm64-v8a` | `false` |

---

## 核心工作机制

### 1. SDK 登录字段 RSA Bypass
- 当 `USE_JAVA_BYPASS_RSA=true` 时，Hook 脚本自动拦截 SDK 层的 `RSAUtils.encryptByPublicKey` 方法，使其直接返回原始明文。
- 使得密码登录、手机验证码登录的字段以明文 JSON 传至服务端 `Sv`，免除服务端配置私钥解密的复杂流程。

### 2. Java SDK URL 常驻改写

Java 侧 SDK 的地址来源有三处：`com.mihoyo.combo.config.ConfigCenter` 的 `EnvConfig.urls`（单个环境上千条）、
`com.mihoyo.platform.account.sdk.PorteInfo` 的 7 个 URL 静态字段、`com.mihoyo.hotfix.runtime.patch.http.NetClient.BASE_URLS`。

这些地址**会被 SDK 自己重新覆盖**：客户端每次回到登录页（包括被踢下线、退出登录、顶号）都会走
`MonoTheIndex.UpdateForceLogin -> TheBaseAccountManager.Init -> MiHoYoSDK.Invoke("login_init") ->
Java Porte.setup -> PorteInfo.setup`，把 `PorteInfo` 的 URL 静态字段写回官方域名。

因此改写**不能是一次性的**，脚本按以下方式常驻：

- 每个目标带一个廉价指纹（`PorteInfo` 七个字段值、`NetClient.BASE_URLS` 全部元素、
  `ConfigCenter` 的配置数量 + 每个 `EnvConfig` 的对象身份）。指纹不变时每轮只读值、不做全量扫描。
- 指纹变化（说明 SDK 重新初始化或重建了配置）时立刻重扫，并在终端打印
  `[重定向] <目标> 地址被 SDK 重新初始化覆盖，已重新改写 N 个地址`。
- 额外 Hook `PorteInfo.setup(Context, PorteConfig)`，在它写回官方地址后**同步**再改写一次，
  消除 100ms 巡检窗口，避免初始化后紧接着发出的请求落到官方服务器。

### 3. MuMu ARM64 双 Realm 支持
当在 MuMu x86_64 系统中运行 ARM64 客户端时（`CLIENT_ARCH=arm64-v8a` 且 `EMULATOR=true`）：
- Frida 需通过 Emulated Realm 进行 Hook，要求设备路径 `/data/local/tmp/re.frida.server/frida-agent-arm64.so` 存在与当前 Frida 版本完全一致的 agent。
- 提取并推送 agent 命令：
  ```powershell
  # 自动下载同版本 Frida Server 并提取 agent
  py ExtractFridaAgent.py

  # 推送至设备
  & $adb push .\frida-agent-arm64.so /data/local/tmp/re.frida.server/frida-agent-arm64.so
  & $adb shell chmod 0644 /data/local/tmp/re.frida.server/frida-agent-arm64.so
  ```

### 4. IL2CPP 动态特征扫描
- 脚本摒弃了脆弱的硬编码 RVA 地址，采用运行时内存通配特征扫描（Signature Pattern Scanning）定位 `libil2cpp.so` 中的目标函数，保障不同微版本客户端之间的跨版本兼容性。

---

## 启动运行

确保设备上的 `frida-server` 已启动且端口已转发，随后在终端执行：

```powershell
py Redirector.py
```

- Redirector 将以 spawn 模式拉起游戏包名 `com.miHoYo.HSoDv2Original`。
- 成功运行时，终端将输出 `[命中]` 与各 URL / Gateway 地址重定向日志。

---

## 常见排错

- **`缺少 Frida 编译产物`**：未执行编译，执行 `npm install` 与 `npm run build` 即可。
- **`unable to connect to remote frida-server`**：检查设备端 `frida-server` 进程是否以 root 运行，并执行 `adb forward tcp:27042 tcp:27042`。
- **`process is not using emulation`**：检查 APK 架构与 `Config.ini` 配置。仅在 MuMu 运行 ARM64 客户端时将 `EMULATOR` 设为 `true`，x86 客户端必须为 `false`。
- **HTTP 有请求但服务端无 Gateway 连接**：检查 `Config.ini` 的 `GATEWAY_HOST` 是否配置为模拟器可达的宿主机 IP，且防火墙未拦截 `26000` / `26001` 端口。

---

## 相关技术文档

- [客户端与私服启动完整教程](../docs/BH2_CLIENT_SERVER_STARTUP_GUIDE.md)
- [SDK RSA Bypass 逆向问题深度分析](SDK_RSA_BYPASS_ANALYSIS.md)
- [ARM64 Native 特征扫描分析](NATIVE_SIGNATURE_ANALYSIS.md)
- [ARMv8a 双 Realm 架构兼容分析](BH2_ARMV8A_ANALYSIS.md)
