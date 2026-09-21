'use strict';

let CONFIG = null;
const MODULE_WAIT_TIMEOUT_MS = 30000;
const EMULATED_NAMESPACE_DIRECTORY = '/data/local/tmp/re.frida.server';
let nativeBridgeLoadReplacement = null;
let moduleObserver = null;
let moduleWaitTimer = null;
let neoxState = null;

function reportStatus(event, message) {
    send({ event: event, message: message });
}

function report(message) {
    send({ message: message });
}

function installEmulatedRealmLoader() {
    const bridge = Process.getModuleByName('libnativebridge.so');
    const createNamespace = new NativeFunction(
        bridge.getExportByName('NativeBridgeCreateNamespace'), 'pointer',
        ['pointer', 'pointer', 'pointer', 'uint64', 'pointer', 'pointer'],
    );
    const loadAddress = bridge.getExportByName('NativeBridgeLoadLibrary');
    const originalLoad = new NativeFunction(loadAddress, 'pointer', ['pointer', 'int']);
    const loadExt = new NativeFunction(
        bridge.getExportByName('NativeBridgeLoadLibraryExt'),
        'pointer', ['pointer', 'int', 'pointer'],
    );
    const getError = new NativeFunction(bridge.getExportByName('NativeBridgeGetError'), 'pointer', []);
    const namespace = createNamespace(
        Memory.allocUtf8String('frida-emulated'),
        Memory.allocUtf8String(EMULATED_NAMESPACE_DIRECTORY),
        Memory.allocUtf8String(EMULATED_NAMESPACE_DIRECTORY + ':/system/lib64/arm64:/system_ext/lib64/arm64'),
        0, Memory.allocUtf8String(EMULATED_NAMESPACE_DIRECTORY + ':/system'), ptr(0),
    );
    if (namespace.isNull()) {
        throw new Error('NativeBridgeCreateNamespace 返回空指针');
    }

    nativeBridgeLoadReplacement = new NativeCallback(function (path, flags) {
        // 仅为指定 agent 使用独立 namespace，游戏自身的库保持原加载路径。
        let requestedPath = null;
        try {
            requestedPath = path.readUtf8String();
        } catch (_) {}
        if (requestedPath !== CONFIG.emulatedAgentPath) {
            return originalLoad(path, flags);
        }
        const handle = loadExt(path, flags, namespace);
        if (handle.isNull()) {
            const error = getError();
            reportStatus('native-bridge-failed', '[错误] ARM64 agent 加载失败：' +
                (error.isNull() ? '未知错误' : error.readUtf8String()));
        } else {
            report('[就绪] ARM64 agent 已通过 NativeBridgeLoadLibraryExt 加载');
        }
        return handle;
    }, 'pointer', ['pointer', 'int']);
    Interceptor.replace(loadAddress, nativeBridgeLoadReplacement);
    reportStatus('native-bridge-ready', '[就绪] Native Realm：MuMu Native Bridge 兼容层已安装');
}

// 固定样本：a8c2e2f915413640eab7e9615f47787f04c36dd1bbfdec26c80fb2dfee6e60dc。
// 地址及入口字节由 IDA 核对；先校验全部入口，再创建 NativeFunction 或 Hook。
const NEOX_API = {
    run: { rva: 0x1B380CC, bytes: 'f44fbea9fd7b01a9fd430091f40300aa',
        result: 'int', args: ['pointer', 'pointer'] }, // PyRun_SimpleStringFlags
    sysGet: { rva: 0x1B3D480, bytes: '880d01d008dd47f9080140f9080540f9',
        result: 'pointer', args: ['pointer'] }, // PySys_GetObject，借用引用
    stringData: { rva: 0x1AC722C, bytes: 'fd7bbfa9fd030091080440f9095540f9',
        result: 'pointer', args: ['pointer'] }, // PyString_AsString
    clearError: { rva: 0x1B11EAC, bytes: 'f44fbea9fd7b01a9fd430091e80e01d0',
        result: 'void', args: [] }, // PyErr_Clear
    evalFrame: { rva: 0x1AFCAD8, bytes: 'ffc304d1fc6f0da9fa670ea9f85f0fa9' },
    execModule: { rva: 0x1B1A08C, bytes: 'f85fbca9f65701a9f44f02a9fd7b03a9' },
};
const NEOX_THREAD_STATE_RVA = 0x48637F8;
const NEOX_STATUS_KEY = '_neox_local_status';

const NEOX_HTTP_CODE = [
    'def _neox_install_http(local_host, local_port):',
    '    import sys, urlparse',
    '    http = sys.modules.get("httplib")',
    '    if http is None or not hasattr(http, "HTTPConnection"):',
    '        return False',
    '    cls = http.HTTPConnection',
    '    if getattr(cls.request, "_neox_http", False):',
    '        return True',
    '    original_request = cls.request',
    '    original_response = cls.getresponse',
    '    original_close = cls.close',
    '    routes = {',
    '        ("g58.update.netease.com", 443): {',
    '            "/server_list_android.txt": ("GET", "HEAD"),',
    '            "/announcement_android": ("GET", "HEAD"),',
    '            "/announcement_other": ("GET", "HEAD")},',
    '        ("g58https.netease.com", 11011): {"/account": ("POST",)}}',
    '    def request(self, method, url, body=None, headers={}):',
    '        origin = (self.host.lower().rstrip("."), self.port)',
    '        parts = urlparse.urlsplit(url)',
    '        allowed = routes.get(origin, {}).get(parts.path, ())',
    '        if parts.netloc:',
    '            url_port = parts.port or (443 if parts.scheme == "https" else 80)',
    '            if (parts.hostname.lower().rstrip("."), url_port) != origin:',
    '                allowed = ()',
    '        previous = getattr(self, "_neox_http_connection", None)',
    '        if previous is not None:',
    '            previous.close()',
    '            del self._neox_http_connection',
    '        if method.upper() not in allowed:',
    '            return original_request(self, method, url, body, headers)',
    '        destination_path = parts.path + (("?" + parts.query) if parts.query else "")',
    '        forwarded_headers = dict((key, value) for key, value in headers.items() if key.lower() != "host")',
    '        connection = cls(local_host, local_port, timeout=self.timeout)',
    '        self._neox_http_connection = connection',
    '        original_close(self)',
    '        sys.stderr.write("[NeoX-HTTP] " + method.upper() + " " + origin[0] + ":" + str(origin[1]) + parts.path + " -> " + local_host + ":" + str(local_port) + parts.path + "\\n")',
    '        return original_request(connection, method, destination_path, body, forwarded_headers)',
    '    def getresponse(self, *args, **kwargs):',
    '        connection = getattr(self, "_neox_http_connection", None)',
    '        return original_response(connection if connection is not None else self, *args, **kwargs)',
    '    def close(self):',
    '        connection = getattr(self, "_neox_http_connection", None)',
    '        if connection is not None:',
    '            del self._neox_http_connection',
    '            connection.close()',
    '        return original_close(self)',
    '    request._neox_http = True',
    '    request._neox_original = original_request',
    '    getresponse._neox_original = original_response',
    '    close._neox_original = original_close',
    '    cls.request = request',
    '    cls.getresponse = getresponse',
    '    cls.close = close',
    '    return True',
].join('\n');

const NEOX_BYPASS_CODE = [
    'import sys',
    'def _neox_apply_local_login(host, port, allow_ui, http_host, http_port, gateway_key=""):',
    '    import sys',
    '    state = getattr(sys, "_neox_local_state", None)',
    '    if state is None:',
    '        state = {}',
    '        sys._neox_local_state = state',
    '    if not _neox_install_http(http_host, http_port):',
    '        return "wait:httplib"',
    '    state["http_redirect"] = True',
    '    const = sys.modules.get("com.const")',
    '    if const is None or not hasattr(const, "DEBUG_LOGIN"):',
    '        return "wait:com.const"',
    '    const.DEBUG_LOGIN = True',
    '    assert const.DEBUG_LOGIN is True',
    '    if gateway_key:',
    '        const.CLIENT_PUBKEY = gateway_key',
    '        state["const_client_pubkey_patched"] = True',
    '    pending = []',
    '    sdk = sys.modules.get("logic.logic_sdk")',
    '    member = getattr(sdk, "Member", None)',
    '    if member is None:',
    '        pending.append("logic.logic_sdk")',
    '    else:',
    '        original_sdk_login = member.sdk_login',
    '        if not getattr(original_sdk_login, "_neox_local", False):',
    '            def sdk_login(self):',
    '                const.DEBUG_LOGIN = True',
    '                state["sdk_login_calls"] = state.get("sdk_login_calls", 0) + 1',
    '                return False',
    '            sdk_login._neox_local = True',
    '            sdk_login._neox_original = original_sdk_login',
    '            member.sdk_login = sdk_login',
    '    login = sys.modules.get("cocos.panels.login")',
    '    panel_class = getattr(login, "Panel", None)',
    '    if panel_class is None:',
    '        pending.append("cocos.panels.login")',
    '    else:',
    '        original_load = panel_class.on_load',
    '        if not getattr(original_load, "_neox_local", False):',
    '            def on_load(self, *args, **kwargs):',
    '                const.DEBUG_LOGIN = True',
    '                result = original_load(self, *args, **kwargs)',
    '                self.user.setVisible(True)',
    '                state["account_button_visible"] = True',
    '                return result',
    '            on_load._neox_local = True',
    '            on_load._neox_original = original_load',
    '            panel_class.on_load = on_load',
    '        original_request = panel_class.login_request',
    '        if not getattr(original_request, "_neox_local", False):',
    '            def login_request(self, *args, **kwargs):',
    '                const.DEBUG_LOGIN = True',
    '                state["login_requests"] = state.get("login_requests", 0) + 1',
    '                return original_request(self, *args, **kwargs)',
    '            login_request._neox_local = True',
    '            login_request._neox_original = original_request',
    '            panel_class.login_request = login_request',
    '    gate_module = sys.modules.get("client.GateClient")',
    '    gate_class = getattr(gate_module, "GateClient", None)',
    '    if gate_class is None:',
    '        pending.append("client.GateClient")',
    '    else:',
    '        original_gate = gate_class.__init__',
    '        if not getattr(original_gate, "_neox_local", False):',
    '            def gate_init(self, ip, remote_port, clientconf):',
    '                const.DEBUG_LOGIN = True',
    '                destination_port = remote_port if ip == host else port',
    '                state["gateway_calls"] = state.get("gateway_calls", 0) + 1',
    '                state["gateway"] = (host, destination_port)',
    '                if gateway_key:',
    '                    clientconf = dict(clientconf)',
    '                    clientconf["loginkeycontent"] = gateway_key',
    '                    const.CLIENT_PUBKEY = gateway_key',
    '                    state["gateway_key_patched"] = True',
    '                return original_gate(self, host, destination_port, clientconf)',
    '            gate_init._neox_local = True',
    '            gate_init._neox_original = original_gate',
    '            gate_class.__init__ = gate_init',
    '    if allow_ui:',
    '        gg = sys.modules.get("gg")',
    '        ui = getattr(gg, "ui", None)',
    '        panels = getattr(ui, "panels", None)',
    '        existing = getattr(panels, "__dict__", {}).get("_panels", {}).get("login")',
    '        user = getattr(existing, "__dict__", {}).get("user")',
    '        if user is not None:',
    '            user.setVisible(True)',
    '            state["account_button_visible"] = True',
    '    if pending:',
    '        return "wait:" + ",".join(pending)',
    '    if not allow_ui:',
    '        return "wait:game-thread"',
    '    assert getattr(panel_class.on_load, "_neox_local", False)',
    '    assert getattr(panel_class.login_request, "_neox_local", False)',
    '    assert getattr(gate_class.__init__, "_neox_local", False)',
    '    assert const.DEBUG_LOGIN is True',
    '    return "ready"',
].join('\n');

function neoxAddress(module, name, definition) {
    if (!Number.isInteger(definition.rva) || definition.rva < 0 ||
        definition.rva + 16 > module.size) {
        throw new Error(name + ' 地址越界');
    }
    const address = module.base.add(definition.rva);
    const range = Process.findRangeByAddress(address);
    if (range === null || (range.protection.indexOf('x') === -1 && !CONFIG.emulator)) {
        throw new Error(name + ' 不在有效代码映射中');
    }
    const bytes = Array.from(new Uint8Array(address.readByteArray(16)),
        function (value) { return value.toString(16).padStart(2, '0'); }).join('');
    if (bytes !== definition.bytes) {
        throw new Error(name + ' 入口字节不匹配，拒绝使用其他版本的 RVA');
    }
    return address;
}

function neoxReadStatus(state) {
    const object = state.api.sysGet(state.statusKey);
    if (object.isNull()) {
        return 'error:missing-status';
    }
    const data = state.api.stringData(object);
    if (data.isNull()) {
        state.api.clearError();
        return 'error:invalid-status';
    }
    return data.readUtf8String();
}

function neoxTryInjectBypass(allowUi, force) {
    const state = neoxState;
    if (state === null || state.injecting || state.failed) {
        return;
    }
    const now = Date.now();
    if (!force && now - state.lastAttempt < 200) {
        return;
    }
    // 只在解释器回调中执行，沿用当前线程持有的 GIL。
    // 不能从定时器线程 PyGILState_Ensure 后直接触碰游戏/UI 对象。
    const tstate = state.threadState.readPointer();
    if (tstate.isNull() || !tstate.add(72).readPointer().isNull()) {
        return; // 保留游戏当前异常，不在异常传播途中重入解释器。
    }
    state.lastAttempt = now;
    state.injecting = true;
    try {
        const code = NEOX_HTTP_CODE + '\n' + NEOX_BYPASS_CODE + '\nsys.' + NEOX_STATUS_KEY + ' = "error:incomplete"\n' +
            'try:\n    sys.' + NEOX_STATUS_KEY + ' = _neox_apply_local_login(' +
            JSON.stringify(CONFIG.gatewayHost) + ', ' + CONFIG.gatewayPort + ', ' +
            (allowUi ? 'True' : 'False') + ', ' + JSON.stringify(CONFIG.httpHost) + ', ' + CONFIG.httpPort + ', ' +
            JSON.stringify(CONFIG.gatewayPublicKey || '') + ')\n' +
            'except Exception as _neox_error:\n    sys.' + NEOX_STATUS_KEY +
            ' = "error:" + type(_neox_error).__name__ + ":" + str(_neox_error)\n';
        const result = state.api.run(Memory.allocUtf8String(code), ptr(0));
        if (result !== 0) {
            throw new Error('Python 执行失败，返回值 ' + result);
        }
        const status = neoxReadStatus(state);
        if (status.startsWith('error:')) {
            throw new Error(status);
        }
        if (status !== state.status) {
            state.status = status;
            report('[Python] ' + status);
        }
        if (status === 'ready') {
            if (!state.ready) {
                reportStatus('neox-bypass-ready',
                    '[已验证] DEBUG_LOGIN=True；本地 HTTP 白名单已安装；账号按钮和 Gateway 已处理');
            }
            state.ready = true;
            if (state.frameHook !== null) {
                state.frameHook.detach();
                state.frameHook = null;
            }
        }
    } catch (error) {
        state.failed = true;
        if (state.frameHook !== null) {
            state.frameHook.detach();
            state.frameHook = null;
        }
        reportStatus('neox-bypass-failed', '[错误] NeoX 注入未通过验证：' + String(error));
    } finally {
        state.injecting = false;
    }
}

function neoxArmFrameHook() {
    const state = neoxState;
    if (state.frameHook !== null || state.failed) {
        return;
    }
    state.frameHook = Interceptor.attach(state.addresses.evalFrame, {
        onEnter: function (args) {
            if (state.injecting || args[0].isNull() || Date.now() - state.lastAttempt < 200) {
                return;
            }
            try {
                // ARM64 当前样本：f_code=0x20，co_filename=0x50，PyString 数据=0x24。
                const code = args[0].add(0x20).readPointer();
                const filename = code.add(0x50).readPointer().add(0x24).readUtf8String()
                    .replace(/\\/g, '/');
                const gameFrame = /(^|\/)(logic\/logic_(main|init|sdk|state)|cocos\/panels\/login)\.py$/.test(filename);
                if (gameFrame || /(^|\/)patch\/[^/]+\.py$/.test(filename)) {
                    neoxTryInjectBypass(gameFrame, false);
                }
            } catch (error) {
                if (!state.frameReadWarning) {
                    state.frameReadWarning = true;
                    report('[警告] 解释器帧读取失败：' + String(error));
                }
            }
        },
    });
}

function installNeoXPythonBypass(module) {
    if (Process.arch !== 'arm64' || Process.pointerSize !== 8) {
        throw new Error('NeoX Python 必须在 ARM64 Realm 中安装');
    }
    const addresses = {};
    for (const name of Object.keys(NEOX_API)) {
        addresses[name] = neoxAddress(module, name, NEOX_API[name]);
    }
    const api = {};
    for (const name of Object.keys(NEOX_API)) {
        const definition = NEOX_API[name];
        if (definition.args !== undefined) {
            // 禁止注入代码递归命中自身，且不在持有 GIL 时让出 JS 锁。
            api[name] = new NativeFunction(addresses[name], definition.result, definition.args, { scheduling: 'exclusive', traps: 'none' });
        }
    }
    neoxState = {
        addresses: addresses, api: api,
        threadState: module.base.add(NEOX_THREAD_STATE_RVA),
        statusKey: Memory.allocUtf8String(NEOX_STATUS_KEY),
        status: 'waiting', injecting: false, ready: false, failed: false,
        lastAttempt: 0, frameHook: null, frameReadWarning: false,
    };
    neoxState.importHook = Interceptor.attach(addresses.execModule, {
        onEnter: function (args) {
            this.relevant = false;
            if (!neoxState.injecting) {
                const name = args[0].readUtf8String();
                this.relevant = /^(httplib|com\.const|logic\.logic_sdk|cocos\.panels\.login|client\.GateClient)$/.test(name);
            }
        },
        onLeave: function (result) {
            if (this.relevant && !result.isNull() && !neoxState.injecting) {
                // 模块已经执行完毕才能改属性；导入线程不调用 UI。
                neoxArmFrameHook();
                neoxTryInjectBypass(false, true);
            }
        },
    });
    neoxArmFrameHook();
    report('[就绪] ARM64 Python 入口校验通过；游戏线程就绪');
}

function waitForNeoXModule() {
    let installed = false;
    function consider(module) {
        if (installed || module.name !== 'libclient.so') {
            return;
        }
        installed = true;
        if (moduleWaitTimer !== null) {
            clearTimeout(moduleWaitTimer);
        }
        try {
            report('[就绪] libclient.so base=' + module.base + '，Realm arch=' + Process.arch);
            installNeoXPythonBypass(module);
        } catch (error) {
            reportStatus('neox-bypass-failed', '[错误] NeoX Hook 安装失败：' + String(error));
        }
    }
    // 同时覆盖脚本安装前已加载的模块和后续装载，不依赖碰巧命中定时器。
    moduleObserver = Process.attachModuleObserver({ onAdded: consider });
    if (!installed) {
        moduleWaitTimer = setTimeout(function () {
            if (!installed) {
                reportStatus('neox-bypass-failed', '[错误] ARM64 Realm 等待 libclient.so 超时');
            }
        }, MODULE_WAIT_TIMEOUT_MS);
    }
}

function start(config) {
    CONFIG = config;
    if (CONFIG.clientArch !== 'arm64-v8a' || !['platform', 'neox', 'all'].includes(CONFIG.runtimeRole)) {
        throw new Error('仅支持当前 NeoX ARM64 版本');
    }
    if (CONFIG.runtimeRole === 'platform') {
        if (!CONFIG.emulator || Process.arch !== 'x64') {
            throw new Error('双 Realm 的 platform 角色要求 x64 模拟器宿主');
        }
        installEmulatedRealmLoader();
    } else if (CONFIG.useNeoXSdkBypass) {
        waitForNeoXModule();
    }
    reportStatus('script-configured', '[就绪] ' + CONFIG.runtimeRole + ' Realm 配置已确认');
}

recv('config', function (message) {
    try {
        start(message.payload);
    } catch (error) {
        reportStatus('script-failed', '[错误] Frida 配置失败：' + String(error));
    }
});
