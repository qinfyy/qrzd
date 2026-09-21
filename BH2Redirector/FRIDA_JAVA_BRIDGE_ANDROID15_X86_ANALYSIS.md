# frida-java-bridge Android 15 x86 兼容与 Java Hook 分析报告

更新时间：2026-08-31
目标客户端：BH2 13.2.8 x86 版本
测试环境：MuMu 模拟器 V6、Android 15、x86_64 架构、Frida 17.17.0、`frida-java-bridge 7.0.13`

## 版本对照：旧版崩溃与新版模拟器问题

### 7.0.4 为什么会让 Java/ART 崩溃

旧版在 Android 15 x86 上同时踩中了两条依赖 ART 私有实现的路径：

- `Java.use()` 初始化会进入 `EnsurePluginLoaded`/JVMTI。该调用使用内部 ART 符号和固定 ABI，Android 15 厂商 ART 的 x86 实现与 Bridge 假设不一致。
- Runnable 线程状态转换会重编译 x86 `ExceptionClear`。当前 ART 的指令布局不满足 `recompileExceptionClearForX86` 的解析条件，直接抛出 `Error: expected a pointer`。

即使初始化没有立即抛错，给 Java 方法设置 `.implementation` 仍会进入 `ArtMethodMangler.replace()`：Bridge 会 clone `ArtMethod`，改写 `jniCode`、`quickCode`、`interpreterCode` 和 `accessFlags`，并安装 trampoline。游戏自带 xcrash 同时执行线程暂停和 Java 栈采集时，实测出现 `SuspendThreadByPeer timed out: main`，随后 ANR、`SIGABRT`，部分场景为 `SIGSEGV`。所以旧版的“Java 崩溃”不是单一的业务异常，而是 x86 ART 私有 ABI、方法入口替换和 xcrash 并发共同触发的进程级故障。

### 7.0.13 在模拟器里遇到的具体问题

升级到 7.0.13 后，Bridge 本身的初始化路径有所变化，但在目标模拟器上原版仍可能访问 ia32 的 JVMTI/ART 私有路径；因此只升级版本并不能保证 `Java.use()` 稳定。对本项目应用 ia32 patch 后，`Java.use()`、字段读取和配置对象改写可稳定完成，旧版的 `expected a pointer` 不再出现。

新版的剩余问题发生在“方法替换”而不是“类读取”：隔离测试可以安装并调用 RSA `.implementation`，但把同类 Hook 放进真实游戏登录链路后，xcrash 的主线程暂停/栈采集仍可能与 ART trampoline 竞争，表现为点击登录无响应、ANR 或模拟器进程异常。String overload 安装成功但实际登录零命中时，还必须继续覆盖 SDK 的 `[B,[B` overload；因此“Hook 已安装”不能等同于“登录请求已命中”。

当前正式脚本保留原有 URL、IL2CPP 和 Gateway 重定向，只对四个已确认 RSA overload 做明文返回，并把命中日志延迟到 Java 调用栈之外发送，以降低 xcrash 竞争。

## 2026-08-31 正式变更

- 正式依赖升级为 frida-java-bridge 7.0.13，package.json 与 package-lock.json 已锁定该版本。
- 新补丁文件为 patches/frida-java-bridge+7.0.13.patch；postinstall 会自动应用。
- ia32 补丁包含：跳过 JVMTI EnsurePluginLoaded、禁用 ART 类模型私有偏移快路径、禁用 ART 对象解包路径。
- 隔离实验在 Android 15 x86 上验证：Java.use、RSA implementation 安装和 RSA 主动调用均成功，未出现 expected a pointer、SuspendThreadByPeer 或 SIGABRT。
- 正式 RSA Hook 保留旧的两套类名与 overload；命中日志延迟到 Java 调用返回后发送，避免在游戏 Java 调用栈内同步通信。
- URL、IL2CPP、Gateway 和 Java 配置对象重定向逻辑未删除；重新执行 npm run build 生成 dist/FridaScript.js。

## 结论

BH2Redirector 的 Java 支持经历了两个层次的问题：

1. Frida 17 不再默认提供全局 `Java` 对象，脚本必须显式依赖并编译 `frida-java-bridge`。
2. `frida-java-bridge 7.0.4` 在当前 Android 15 x86 ART 上启用了依赖 ART 内部实现的快速路径，其中 x86 `ExceptionClear` 重编译路径直接报出 `Error: expected a pointer`，并存在导致 `libart.so` 崩溃的风险。

最终方案是只在 Frida 的 `ia32` 进程中禁用两个不稳定的 ART/JVMTI 快速路径，让 Java 类模型退回 JNI Reflection 基础模式；同时，BH2Redirector 不再替换 OkHttp、`java.net.URL` 等 Java/ART 方法，而是直接修改 SDK 已加载的配置对象。

补丁不会禁用 Java，也不会影响 x64、ARM 和 ARM64。当前需要的 `Java.use()`、字段访问、反射和集合修改仍可正常工作。

## 最新结论：方法替换需隔离初始化与调用日志

前一版报告只覆盖了 Java Bridge 初始化阶段的兼容问题；后续实机调试进一步证明，
即使 basic 类模型能够正常完成 `Java.use()`，设置 Java 方法的 `.implementation` 仍会进入
ART 方法替换路径，不能视为安全。

### 复现对照

- 只读取 `RSAUtils`、`LoginManager` 类和 overload，不设置 `.implementation`：客户端进程保持运行。
- 设置 RSA 方法或 `LoginManager.login` 的 `.implementation` 后，点击验证码登录：主线程无响应并触发 ANR。
- `debuggerd` 栈包含 `pthread_join -> libxcrash.so -> libsigchain.so -> frida-agent-32.so -> libart.so`，
  Java 栈位于 `LoginManager.sendLoginCaptcha/loginSendPhoneCaptcha`。
- `logcat` 出现 `SuspendThreadByPeer timed out: main`、`Aborting culprit thread`、
  `InputDispatcher: SdkActivity is unresponsive` 和 `Runtime aborting`，最终为 `SIGABRT`。

### 源码对应关系

`class-factory.js` 的 implementation setter 会创建 MethodMangler 并调用 `replace()`；
Android ART 后端的 `ArtMethodMangler.replace()` 会 clone ArtMethod，修改 `jniCode`、
`quickCode`、`interpreterCode`、`accessFlags`，必要时再安装 `ArtQuickCodeInterceptor`，
最后调用 `notifyArtMethodHooked`。这与上述 ART/xcrash suspend timeout 的时间和调用链一致。

因此，当前根因应表述为：**Android 15 x86 环境下，Frida Java 方法替换与游戏 xcrash 的线程暂停/栈采集链不兼容**。
这不是 `Java.perform()` 调度、原始实现调用方式、URL 重定向或账号数据库导致的问题。

### 方案决策

不再通过继续修改 `ExceptionClear`、JVMTI 或 `Java.perform()` 调度来强行维持 `.implementation`。
这些补丁只能改善初始化或类模型访问，不能消除 `ArtMethodMangler.replace()` 的 ART 元数据和入口改写。

当前保留两处 ia32 兼容补丁，仅用于让 basic `Java.use()` 和字段读取可用；RSA Bypass 改走候选的原生入口路径：

1. 解析 `jmethodID` 是否为 direct/indirect 指针。
2. 取得 RSA `ArtMethod` 的实际 `quickCode`。
3. 先用只读 `Interceptor.attach` 验证调用命中。
4. 确认 x86 参数、返回值和 Java 字符串对象生命周期后，再实现原生入口级替换。

现阶段不能把原生入口 Bypass 说成已完成；也不能把“Hook 安装成功”当作“登录请求已命中”。

本轮用于定位上述问题的 `debug_*` 脚本、编译产物和截图均为临时文件，已在提交前清理；可复核证据、版本边界和未验证事项保留在本报告与 `SDK_HOOK_CONTEXT.md`。

## 一、实现时遇到的问题

### 1. Frida 17 中没有默认的全局 `Java`

旧版脚本可以直接调用：

```javascript
Java.perform(function () {
    // ...
});
```

Frida 17 将 Java Bridge 拆分为独立包后，未编译的脚本中不再自动提供这个全局对象。直接运行旧代码会得到 `Java is not defined` 一类错误。

这不是目标游戏或 Android 权限问题，而是 Frida 17 的脚本依赖方式发生了变化。

### 2. Java Bridge 初始化时报 `expected a pointer`

引入 `frida-java-bridge 7.0.4` 后，在 Android 15 x86 上出现：

```text
Error: expected a pointer
    at recompileExceptionClearForX86
    at makeArtThreadStateTransitionImpl
    at withRunnableArtThread
    at build
    at use
    at installJavaHooks
```

`frida-compile` 会把 Java Bridge 打包进 `dist/FridaScript.js`，因此错误栈中的 `/script1.js:6195` 等行号对应的是编译产物，不是项目源文件行号。真正有定位价值的是函数名。

沿函数调用链检查 `frida-java-bridge/lib/android.js` 后可以确认：

```text
Java.use()
  -> 构建 Java 类模型
  -> withRunnableArtThread()
  -> makeArtThreadStateTransitionImpl()
  -> recompileExceptionClearForX86()
```

Java Bridge 为了在 ART 的 Runnable 线程状态下安全调用内部 API，会读取 JNIEnv vtable 中的 `ExceptionClear`，解析其 x86 指令并重新生成一段代码。当前 Android 15 x86 ART 的实现、包装层或指令布局不符合该重编译器的假设，导致某个值在需要 `NativePointer` 时不是有效指针，最终抛出 `expected a pointer`。

这个错误发生在 Java Bridge 的 ART 适配层中，不是 BH2 的 Java 类不存在，也不是 `Java.perform()` 调用位置错误。

### 3. Java Hook 运行后出现 `libart.so` 崩溃

早期实现曾替换以下 Java 方法或构造流程：

- `okhttp3.Request$Builder`
- SDK 内置 OkHttp 的 `Request$Builder`
- `java.net.URL`

运行日志表明这些 Hook 确实能够改写请求，但随后游戏发生：

```text
signal 11 (SIGSEGV)
fault addr 0x10
Cause: null pointer dereference
backtrace: /apex/com.android.art/lib/libart.so
```

崩溃线程曾出现在 `Monitor-Looper`，另一次报告出现在 `UnityMain`。现有 backtrace 没有完整符号，无法把 SIGSEGV 精确归因到某一个被替换的方法；但可以确认崩溃位于 ART 内部，而且与启用 Java Bridge/方法替换的时间高度相关。

因此不能只捕获 JavaScript 异常后继续运行。桥接器对 ART 内部状态的错误修改可能不会立即报错，而会在其它线程稍后崩溃。

### 4. Android 15 x86 的 JVMTI 插件装载路径存在兼容风险

Java Bridge 的 `temporaryApi.jvmti` getter 会调用：

```text
art::Runtime::EnsurePluginLoaded("libopenjdkjvmti.so", ...)
```

它通过运行时查找的内部 C++ 符号和固定 `NativeFunction` 签名调用 ART。该接口不是稳定的 Android NDK API，容易受 Android 版本、厂商 ART 和 x86 ABI 差异影响。

本次日志没有出现指向 `tryGetEnvJvmti()` 或 `EnsurePluginLoaded()` 的直接错误栈，所以这一项应视为基于源码和平台差异做出的防御性规避，而不是已经单独复现并精确定位的崩溃点。

### 5. 直接修改 `node_modules` 无法持久保存

即使手工修改 Java Bridge 后能够运行，重新执行 `npm install` 或 `npm ci` 也会还原 `node_modules`。如果补丁没有进入项目依赖流程，其他环境无法复现当前状态，编译出的 `dist/FridaScript.js` 也可能重新带回问题代码。

## 二、解决方法

### 1. 显式引入并编译 Java Bridge

`FridaScript.js` 使用 ES Module 导入：

```javascript
import Java from 'frida-java-bridge';
```

`package.json` 固定依赖版本：

```json
{
  "dependencies": {
    "frida-java-bridge": "7.0.13"
  },
  "devDependencies": {
    "frida-compile": "^17.0.0",
    "patch-package": "^8.0.1"
  }
}
```

脚本通过下面的命令生成 Frida 可加载的单文件产物：

```powershell
npm run build
```

该命令实际执行：

```text
frida-compile FridaScript.js -o ./dist/FridaScript.js -S
```

`Redirector.py` 只读取并加载 `dist/FridaScript.js`，不会在启动游戏时自动编译。

### 2. 在 ia32 上禁用 JVMTI 插件装载

补丁对 `frida-java-bridge/lib/android.js` 的 `jvmti` getter 增加架构判断：

```javascript
if (Process.arch === 'ia32') {
    return null;
}
```

这样 Android 15 x86 不会进入 `tryGetEnvJvmti()`，也不会调用内部的 `Runtime::EnsurePluginLoaded()` 去装载 `libopenjdkjvmti.so`。

限制条件很重要：

- 配置中的客户端架构写作 `x86`。
- Frida 运行时对 32 位 x86 的名称是 `ia32`。
- x64、ARM、ARM64 不受该分支影响。

对当前 BH2 配置改写而言不需要 JVMTI 的高级枚举能力，因此该回退不会影响目标功能。

### 3. 强制 Java 类模型使用 JNI Reflection 基础模式

补丁对 `frida-java-bridge/lib/class-model.js` 的 `compileModule()` 修改如下：

```javascript
const artClass = (Process.arch === 'ia32') ? null : getArtClassSpec(vm);
```

原始实现取得 `artClass` 后会继续读取 ART 私有对象布局、调用 `getArtMethodSpec()`，并可能进入 `withRunnableArtThread()` 和 `recompileExceptionClearForX86()`。在 ia32 上令 `artClass` 为 `null` 后：

- 不再初始化 ART Class、ArtMethod 和 ArtField 的私有偏移快路径。
- 不再为类模型构建需要重编译 `ExceptionClear` 的 Runnable 线程转换代码。
- CModule 中的 `art_api.available` 保持为假。
- 类模型的 `mode` 变为 `basic`。

基础模式并不是空实现。`model_new()` 会走 JNI Reflection 分支，使用：

- `Class.getDeclaredMethods()`
- `Class.getDeclaredFields()`
- JNI `FromReflectedMethod`
- JNI `FromReflectedField`

因此 `Java.use()`、读取静态字段、调用普通 Java 方法和反射字段访问仍然可用，只是放弃依赖 ART 私有内存布局的快速路径。

### 4. URL 配置改写与 RSA 方法替换分离

BH2Redirector 的 URL 逻辑不会设置 `.implementation`，而是在 `Java.perform()` 中轮询并直接修改：

- `ConfigCenter.EnvConfig.urls`
- `PorteInfo` 中的 SDK URL 静态字段
- `Hotfix.NetClient.BASE_URLS`

RSA Bypass 单独按类和 overload 设置 `.implementation`，回调直接返回输入参数，命中日志延迟到调用返回后异步发送。URL 日志会明确显示：

```text
[就绪] Java SDK URL 修改已启用
```

运行配置也按职责拆分：`USE_JAVA_URL_MODIFY` 只控制上述 URL 配置对象改写，`USE_JAVA_BYPASS_RSA` 只控制 RSA 方法 Hook，两者可以独立启停。

这种方式仍然使用 Java Bridge，但不会修改 OkHttp、URL 或 ART 方法入口。它减少了多线程网络请求期间的方法替换、重入和 ART trampoline 风险，也更符合 Redirector 只负责改写目标配置的职责。

### 5. 使用 patch-package 固化补丁

兼容补丁保存在：

```text
patches/frida-java-bridge+7.0.13.patch
```

`package.json` 配置：

```json
{
  "scripts": {
    "postinstall": "patch-package"
  }
}
```

安装依赖后，`patch-package` 会把 ia32 兼容改动重新应用到 `node_modules/frida-java-bridge`。依赖版本被固定为 `7.0.13`，确保补丁上下文与目标源码一致。

如果以后升级 `frida-java-bridge`，不能直接沿用文件名或假设补丁仍然适用，必须重新检查新版的 ART/JVMTI 实现并生成对应版本的补丁。

## 验证结果

修复后已经观察到以下行为：

```text
[就绪] Java SDK URL 修改已启用
[重定向] ConfigCenter.EnvConfig.urls: https://api-sdk.mihoyo.com/... -> http://<主机地址>:21000/...
[重定向] Hotfix.NetClient.BASE_URLS[0]: ... -> http://<主机地址>:21000
[重定向] PorteInfo.cdnBaseUrl: ... -> http://<主机地址>:21000/
```

修补后的 x86 测试日志中，`ConfigCenter`、`PorteInfo` 和 `Hotfix.NetClient` 均出现实际改写记录，对应 stderr 为空，没有再次出现 `recompileExceptionClearForX86()` 的 `expected a pointer`。这证明当前 BH2 所需的 Java 配置读取和修改能够在 basic 类模型下完成。

## 证据强度与当前边界

| 结论 | 证据强度 | 依据 |
| --- | --- | --- |
| `expected a pointer` 来自 x86 `ExceptionClear` 重编译路径 | 高 | Frida 错误栈直接包含完整函数调用链 |
| basic 类模型绕过该路径 | 高 | 补丁控制流与 Java Bridge 源码可直接验证 |
| 配置改写在 basic 模式下可用 | 高 | `ConfigCenter`、`PorteInfo`、`NetClient` 实际命中日志 |
| `EnsurePluginLoaded` 在该环境存在 ABI 风险 | 中 | Java Bridge 私有 ART 调用方式和平台版本差异；没有直接错误栈 |
| 早期 SIGSEGV 由某一个具体 Java Hook 导致 | 中 | 崩溃位于 `libart.so` 且时间相关，但缺少完整符号 backtrace |

还需要注意：

1. 补丁只在 `Process.arch === 'ia32'` 时生效。ARMv8a 模拟器的 Java 逻辑运行在 x64 Native Realm，该分支不会启用。
2. basic 模式可能不支持依赖 ART 私有布局、深度方法枚举或 JVMTI 的高级 Java Bridge 功能，但当前 Redirector 不需要这些能力。
3. `dist/FridaScript.js` 内已经包含编译后的 Java Bridge。只修改补丁或 `node_modules` 而不重新执行 `npm run build`，运行时仍会加载旧代码。
4. 判断 Java URL 修改是否成功不能只看 `Java.available`，还应看到具体配置字段的重定向日志和“Java SDK URL 修改完成”。RSA Bypass 则必须看到 Hook 安装与实际 `[RSA Bypass]` 命中日志。

## 2026-08-31 账号字段未命中补充

静态调用链确认密码登录位于 `com.mihoyo.platform.account.sdk.login.LoginManager.loginByPassword`，
其先调用 `com.mihoyo.platform.account.sdk.utils.RSAUtils.INSTANCE.encryptByPublicKey(String)`，
再把结果交给 `LoginApiService.loginByPassword`；短信登录同样通过该 SDK 的 RSA 单例处理区号和手机号。
因此正式脚本只保留该单例入口及旧账号模块入口的四个 overload。

实机日志中曾出现“Hook 已安装但没有 `[RSA Bypass]`”的情况。一个合理的运行时原因是 SDK 启动后的热更新替换了
`ArtMethod` 入口，使早期安装的 implementation 不再位于实际调用路径。当前脚本收到配置后先用 `Java.performNow()` 同步尝试；单 Realm spawn 还会等待 `script-configured` 握手后才恢复游戏；类或 overload 尚未加载时每 500ms 低频重试，任一登录 RSA 入口安装成功后停止轮询。
`ClassLoader.loadClass` 返回目标类后会立即按真实 ClassLoader 补装；同时在 `NewAccountLoginPresenter.login(JSONObject, boolean)` 首次点击入口内再次确认 Hook，以覆盖启动竞态。
正式替换仍只处理四个 RSA overload，没有把 `LoginManager` 或 `RequestUtils.createBody` 等高频方法加入，
避免再次触发 Android 15 x86 的 ART trampoline/ANR 风险。
为消除首次点击早于定时重试的窗口，脚本还在 `ClassLoader.loadClass` 返回两个目标 `RSAUtils` 类时立即同步补装，
该监听只匹配两套 RSAUtils 和一个首次登录入口类，不追踪运营商 SDK 或通用网络类。

本次短时验证只覆盖 spawn、URL/Gateway 重定向和 RSA Hook 安装稳定性，未执行真实账号点击，
因此“实际登录请求已携带明文”仍需在客户端点击登录并同时观察 `[RSA Bypass]` 与服务器收到的 JSON 字段。

## 2026-09-01 ClassLoader 接收者修复

实机日志中的 `TypeError: cannot read property '$getHandle' of undefined` 位于
`frida-java-bridge` 的方法调用路径。根因是 `ClassLoader.loadClass` 的原始方法被传入辅助函数后，
辅助函数内以 `loadClass.call(this, ...)` 调用，但该辅助函数本身没有继承 Java 方法实现的 `this`，
导致 `receiver` 变成 `undefined`，Bridge 随后访问 `receiver.$getHandle()` 失败。

当前脚本改为显式传递 `loadClass` 实例方法的接收者：
`hookLoadClass(this, loadClassByName, [name])`，并在辅助函数中执行
`loadClass.call(receiver, ...)`。同时移除了曾导致 Android 15 x86 主线程 ANR 的
`LoginManager` 入口包装，仅保留窄范围 ClassLoader 监听和四个 RSA overload。

同一轮实测还确认 `Java.enumerateClassLoadersSync()` 在该 Android 15 x86 Bridge 补丁上会
触发 `expected a pointer`，不能放进 100ms 重试路径。正式脚本因此只使用默认
`Java.classFactory`；目标类仍由 `ClassLoader.loadClass` 返回后的即时补装和原有重试覆盖。
