# BH2Redirector ARMv8a 兼容问题分析报告

更新时间：2026-08-12
目标客户端：BH2 13.2.8，`arm64-v8a` 版本  
测试环境：MuMu 模拟器 V6、Android 15、x86_64 架构、Frida 17

## 结论

`arm64-v8a` 客户端本身没有运行在原生 ARM64 Android 系统上，而是由 MuMu 的 Native Bridge（Houdini）在 x86_64 系统中转译执行。因此，同一个游戏进程中同时存在两个运行视角：

- Native Realm：宿主 x86_64 视角，负责 Java、ART、VDEX/OAT 和宿主 zlib。
- Emulated Realm：ARM64 guest 视角，能够识别 ARM64 `libil2cpp.so`，并按 ARM64 RVA 安装业务 Hook。

最终方案不是强行在 x64 地址空间中 Hook ARM64 RVA，而是让两个 Realm 各自负责对应架构的功能，并修复 MuMu Native Bridge 无法按 Frida 默认方式装载 ARM64 agent 的兼容问题。

目前已经确认 Emulated Realm 中的 `UnityWebRequest.set_url`、`NetConnetData.SetIpAddress` 和 `BaseNetworkClient.LoadDefaultServerInfo` 会实际命中并完成重定向。后续无法进入游戏的问题已确认发生在 `Sv` 服务端，不属于 ARMv8a Hook 链路问题。

## 一、实现时遇到的问题

### 1. ARM64 APK 实际运行在 x86_64 宿主中

应用的 `primaryCpuAbi` 是 `arm64-v8a`，但模拟器系统 ABI 是 `x86_64`。在普通 Native Realm 中观察到：

```text
Process.arch = x64
Process.pointerSize = 8
```

此时 ARM64 `libil2cpp.so` 由 Houdini 管理。普通 Native Realm 无法将它作为可执行 ARM64 模块使用，因此不能直接执行：

```javascript
Process.getModuleByName('libil2cpp.so').base.add(arm64Rva)
```

这解释了最初只能修改 Java、VDEX/OAT，而不能命中 `libil2cpp.so` Hook 的现象。

### 2. 直接附加 Emulated Realm 失败

最初直接使用 Frida Emulated Realm 附加进程时，控制器返回：

```text
process is not using emulation
```

问题不在于游戏没有使用 Houdini，而在于该版本 MuMu 的 Native Bridge 不接受 Frida 17 默认使用的旧式 `NativeBridgeLoadLibrary()` 装载流程。Frida 无法把 ARM64 agent 注入 guest 运行环境，所以 Emulated Realm 无法建立。

### 3. 附加时机过早会导致游戏崩溃

spawn 后立即探测或附加 Emulated Realm 时，Houdini 和 Native Bridge 尚未完成初始化。该阶段触发 guest agent 装载可能导致 linker/native bridge 异常，甚至使游戏进程崩溃。

因此，ARMv8a 模拟器路径不能沿用 x86 路径中“spawn 后立即附加、最后 resume”的固定顺序。

### 4. Emulated Realm 建立后找不到 IL2CPP 字符串构造函数

成功进入 Emulated Realm 后，第一版 IL2CPP Hook 报错：

```text
libil2cpp.so 未导出字符串构造函数
```

`module.findExportByName()` 和 `Module.findGlobalExportByName()` 在该运行环境中没有返回 `il2cpp_string_new`。但是重定向 URL 和 Gateway 地址时必须创建新的托管 `System.String`，不能把 Frida 分配的普通 UTF-8 缓冲区直接当作 `Il2CppString` 使用。

### 5. ARM guest 代码映射被误判为不可执行

加入字符串构造函数 RVA 后又出现：

```text
il2cpp_string_new 的 RVA 不在可执行内存中: 0x400028bb8be8
```

在宿主 `/proc/<pid>/maps` 视角中，Houdini 管理的 ARM64 ELF 代码段显示为 `r--`，真正执行的是转译后的代码缓存。因此，宿主映射缺少 `x` 权限并不能证明 ARM guest RVA 不可执行。

原来的统一权限检查适用于原生 x86 和原生 ARM64，但会错误拒绝 ARMv8a-on-x86_64 的合法 guest 地址。

### 6. x86 与 ARM64 的对象布局和函数地址不同

仅增加一组 ARM64 RVA 还不够。以下结构均受指针宽度影响：

- `Il2CppString` 的长度和字符数据偏移。
- `z_stream` 的 `next_out` 和 `avail_out` 偏移。
- `BaseNetworkClient` 中 IPv4、IPv6 和端口字段偏移。
- 所有 IL2CPP 方法 RVA。

如果继续使用 x86 的 32 位偏移，读取到的字符串、对象字段和函数参数都会错误，严重时会直接导致 SIGSEGV。

### 7. Java/平台 Hook 与 IL2CPP Hook 不属于同一架构视角

ARMv8a 模拟器模式下：

- Java VM、ART、VDEX/OAT 和宿主 zlib 位于 Native Realm。
- 游戏的 ARM64 IL2CPP 业务代码位于 Emulated Realm。

把所有 Hook 都安装到一个 Realm 中，要么找不到 Java/平台模块，要么找不到 ARM64 `libil2cpp.so`。同一份脚本重复安装全部 Hook 还会造成重复改写和错误的架构校验。

## 二、解决方法

### 1. 将 ARMv8a 模拟器模式拆分为双 Realm

`Redirector.py` 根据配置决定运行方式：

```ini
CLIENT_ARCH=arm64-v8a
EMULATOR=true
```

该组合会建立两个 Frida Session，并给脚本传入不同职责：

| Realm | `runtimeRole` | 职责 |
| --- | --- | --- |
| Native Realm | `platform` | Java 配置、VDEX/OAT、zlib 和 Native Bridge 兼容层 |
| Emulated Realm | `il2cpp` | ARM64 `libil2cpp.so` HTTP 与 Gateway Hook |

其他配置保持单 Realm：

- `CLIENT_ARCH=x86`、`EMULATOR=false`：使用 x86 Native Realm。
- `CLIENT_ARCH=arm64-v8a`、`EMULATOR=false`：预留给原生 ARM64 真机，直接使用 ARM64 Native Realm。
- `CLIENT_ARCH=x86`、`EMULATOR=true`：配置无意义，控制器会直接拒绝。

### 2. 调整 spawn 和附加顺序

ARMv8a 模拟器路径采用以下时序：

```text
spawn 游戏
  -> resume 游戏
  -> 等待 8 秒，让 Houdini/Native Bridge 初始化
  -> 附加 Native Realm
  -> 安装 Native Bridge 兼容层
  -> 等待兼容层 ready 事件，最长 5 秒
  -> 使用 ARM64 agent 附加 Emulated Realm
  -> 在 Emulated Realm 安装 IL2CPP Hook
```

这里保留了 spawn-only 的启动约束，同时避开 Native Bridge 尚未就绪时的 linker 崩溃窗口。控制器分别保存两个 Session 和 Script，并在退出时按相反顺序卸载。

### 3. 使用专用 namespace 和 `NativeBridgeLoadLibraryExt()` 装载 agent

Native Realm 脚本从 `libnativebridge.so` 获取：

- `NativeBridgeCreateNamespace`
- `NativeBridgeLoadLibrary`
- `NativeBridgeLoadLibraryExt`
- `NativeBridgeGetError`

脚本创建名为 `frida-emulated` 的专用 namespace，搜索路径包括：

```text
/data/local/tmp/re.frida.server
/system/lib64/arm64
/system_ext/lib64/arm64
```

随后替换旧的 `NativeBridgeLoadLibrary()` 入口，把 Frida 的 ARM64 agent 装载请求转交给：

```text
NativeBridgeLoadLibraryExt(path, flags, bridgeNamespace)
```

控制器附加 Emulated Realm 时显式指定：

```text
/data/local/tmp/re.frida.server/frida-agent-arm64.so
```

这个文件不是 Frida Gadget。它必须从同版本 Android x86_64 Frida Server 内嵌的 `frida-agent-arm64.so` 资源提取，并与 Python Frida、设备 Frida Server 保持完全一致的版本。误用 Release 中的 `frida-gadget-<版本>-android-arm64.so` 会启动独立监听器并绑定 `127.0.0.1:27042`；该端口已由 Frida Server 占用时，Houdini guest 线程可能在加载阶段发生 SIGSEGV。

这一步解决了 MuMu 模拟器 V6 拒绝 Frida 默认 Native Bridge 装载方式的问题。日志出现以下内容说明 guest agent 已经真正装载：

```text
[Native Realm] [就绪] ARM64 Frida agent 已通过 NativeBridgeLoadLibraryExt 加载
Realm=Native Realm + Emulated Realm
```

### 4. 为 x86 和 ARM64 分别维护目标描述

`FridaScript.js` 中的 `TARGETS` 按架构保存：

- Frida `Process.arch` 和指针宽度。
- `Il2CppString`、`z_stream` 和 `BaseNetworkClient` 字段偏移。

IL2CPP 的业务函数和字符串构造函数由 `FUNCTION_SIGNATURES` 在模块内定位。每个函数可以保存多个经过完整 ELF 唯一性验证的候选；相对调用和布局相关字节使用 `??` 掩码。运行时不保存客户端版本、不按版本选择地址，也不回退到静态 RVA。候选按顺序扫描，只有恰好命中一次时才采用；详细提取和验证过程见 [ARM64 Native 特征分析](NATIVE_SIGNATURE_ANALYSIS.md)。

ARM64 数据布局使用：

| 结构 | 字段 | 偏移 |
| --- | --- | ---: |
| `Il2CppString` | `length` | `0x10` |
| `Il2CppString` | `chars` | `0x14` |
| `z_stream` | `next_out` | `0x18` |
| `z_stream` | `avail_out` | `0x20` |
| `BaseNetworkClient` | `_serverAddress` | `0x20` |
| `BaseNetworkClient` | `_serverAddressV6` | `0x28` |
| `BaseNetworkClient` | `_serverPort` | `0x30` |

这些对象布局不能与 x86 版本混用。

### 5. 通过特征定位 IL2CPP 字符串构造函数

字符串构造函数优先使用导出符号；导出不可见时，脚本使用同一 ABI 的唯一通配特征定位实现函数。ARM64 导出入口可能是随镜像布局变化的跳板，因此特征取自跳板目标函数，而不是分支位移本身。

这样仍然通过游戏自己的 IL2CPP API 创建 `System.String`，避免伪造对象布局或把临时 Native 缓冲区误传给游戏。

### 6. 修正 Houdini guest 地址的权限校验

所有特征命中地址仍必须落在已映射内存范围内。只有同时满足以下条件时，才跳过宿主映射的 `x` 权限检查：

```text
CLIENT_ARCH=arm64-v8a
EMULATOR=true
```

原生 x86 和原生 ARM64 路径仍要求地址位于可执行映射中。这个例外仅用于修正 Houdini guest 映射的权限语义，不是全局关闭地址校验。

### 7. 用实际 Hook 命中验证结果

最终日志已观察到：

```text
[Emulated Realm] [就绪] HTTP -> http://<主机地址>:21000，Gateway -> <主机地址>:26000
[Emulated Realm] [重定向] UnityWebRequest.set_url: ... -> http://<主机地址>:21000/...
[Emulated Realm] [重定向] NetConnetData.SetIpAddress: <原地址> -> <主机地址>:26000
[Emulated Realm] [重定向] BaseNetworkClient.LoadDefaultServerInfo: <原地址> -> <主机地址>:26000
```

这比“脚本安装成功”更有意义：它证明 ARM64 guest 方法确实执行、参数能够正确读取、新 `Il2CppString` 能够正常构造，并且 Gateway 地址已经被客户端采用。

## 当前边界与注意事项

1. 对象偏移绑定 ABI；业务 Hook 与字符串构造函数以 `FUNCTION_SIGNATURES` 为准。客户端更新后必须使用对应的 `types.cs`、`il2cpp.json` 与逻辑路径下的 `libil2cpp.so` 扩展特征候选，并验证每个候选在对应完整 ELF 中唯一命中。
2. Native Realm 的 VDEX/OAT 与 Java 配置改写已经观察到实际命中。
3. `UES_ZLIB_HOOK=true` 时已确认 zlib Hook 能安装，但现有 ARMv8a 日志没有出现真实的 `zlib.inflate` URL 改写命中。由于 Native Realm 延迟附加 8 秒，启动早期解压可能已经结束，不能把“Hook 已安装”写成“inflate 已验证命中”。
4. `Redirector.py` 不负责编译脚本，只检查 `dist/FridaScript.js` 是否存在。修改 `FridaScript.js` 后需要手动在 `BH2Redirector` 中执行 `npm run build`。
5. 本报告只说明 ARMv8a 模拟器重定向链路。客户端连接 `Sv` 后的登录协议、角色数据和进门问题属于服务端范围，应根据 `Sv` 日志单独分析。
