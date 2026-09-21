# BH2Redirector ARM64 Native 特征分析

## 问题现象

在 `13.3.8_342` ARM64 客户端通过 MuMu Native Bridge 的 Emulated Realm 启动时，
脚本能够定位两个 IL2CPP 字符串构造函数和 `WebRequestUtils.MakeInitialUrl`，随后在安装
`UnityWebRequest.set_url` Hook 时失败：

```text
[Emulated Realm] [Address] il2cpp_string_new: ... (RVA: 0x5ff1820)
[Emulated Realm] [Address] il2cpp_string_new_utf16: ... (RVA: 0x6002190)
[Emulated Realm] [Address] WebRequestUtils.MakeInitialUrl: ... (RVA: 0x96edca0)
[Emulated Realm] [错误] Hook 安装失败：Error: invalid match pattern
```

日志顺序说明 `MakeInitialUrl` 已经扫描完成，异常来自下一个
`UnityWebRequest.set_url` 特征，而不是 Emulated Realm、模块映射或前三个地址。

## 直接原因

旧 ARM64 `UnityWebRequest.set_url` 特征以 `??` 通配字节结尾。Frida 的
`Memory.scanSync()` 不接受这种末尾没有确定字节的 pattern，因此在开始扫描前直接抛出
`invalid match pattern`。这属于特征语法错误，和目标函数是否存在无关。

只删除末尾通配符不能解决版本兼容问题。旧特征还混入了元数据页地址、字段偏移和 `BL`
相对位移，这些编码会随着 IL2CPP 重新链接和元数据布局变化。

## 静态证据

函数边界来自以下 ARM64 材料：

- `Reverse/13.2.8_341/armv8a/dump.cs` 与同目录 `libil2cpp.so`；
- `Reverse/13.3.8_342/arm64-v8a/types.cs` 与同目录 `libil2cpp.so`。

对应类结构文件中确认的业务函数 RVA 如下：

| 函数 | 13.2.8_341 | 13.3.8_342 |
| --- | ---: | ---: |
| `WebRequestUtils.MakeInitialUrl` | `0x096C5498` | `0x096EDCA0` |
| `UnityWebRequest.set_url` | `0x096C4364` | `0x096ECB6C` |
| `BaseNetworkClient.LoadDefaultServerInfo` | `0x0B7DC878` | `0x0B7F0CEC` |
| `BaseNetworkClient.SetNetAddrPort` | `0x0B7DC978` | `0x0B7F0DEC` |
| `NetConnetData.SetIpAddress` | `0x07D221B8` | `0x07D70224` |

对两个 ELF 的函数入口逐条反汇编后，可见栈帧、callee-saved 寄存器和参数保存顺序保持
一致，变化主要集中在：

- `ADRP` 引用的元数据页；
- `LDR`、`LDRB`、`STRB` 的元数据槽和初始化标志偏移；
- `BL` 的相对调用位移；
- `SetNetAddrPort` 使用的元数据索引。

因此，新特征保留函数序言、寄存器分配和关键控制流，只通配随布局变化的编码字节。每条
候选都以一条包含确定字节的完整 ARM64 指令结束，避免再次产生 Frida pattern 语法错误。

## 唯一性验证

对上述两个完整 `libil2cpp.so` 文件扫描后，以下七个入口在各自版本中都恰好命中一次：

- `il2cpp_string_new`；
- `il2cpp_string_new_utf16`；
- `WebRequestUtils.MakeInitialUrl`；
- `UnityWebRequest.set_url`；
- `BaseNetworkClient.LoadDefaultServerInfo`；
- `BaseNetworkClient.SetNetAddrPort`；
- `NetConnetData.SetIpAddress`。

这里的“更通用”不是无限增加 `??`。过度通配后，部分序言会在同一 ELF 中命中二十多个
相似 IL2CPP 方法，无法安全安装 Hook。最终特征以“跨两个已知版本保持稳定，同时在每版完整
ELF 中唯一”为边界。

## 运行时修复

`FUNCTION_SIGNATURES` 现在允许一个函数保存多个候选。扫描规则为：

1. 按候选顺序扫描当前 `libil2cpp.so` 的有效映射；
2. 单个候选语法无效时记录函数名和候选序号，再尝试下一条；
3. 零命中时尝试下一条；
4. 任一候选出现多命中时立即拒绝安装，不能猜测地址；
5. 只有候选恰好命中一次时才安装 Hook。

该机制参考了 `SRRedirector` 的多候选组织方式，但 BH2Redirector 继续强制每个实际采用的
候选唯一命中，不回退固定 RVA，也不根据客户端版本号选择地址。

## 13.3.8 实机验证

在 `13.3.8_342` ARM64 MuMu Emulated Realm 中，修复后的脚本解析并定位到：

```text
il2cpp_string_new                         RVA 0x05FF1820
il2cpp_string_new_utf16                   RVA 0x06002190
WebRequestUtils.MakeInitialUrl            RVA 0x096EDCA0
UnityWebRequest.set_url                   RVA 0x096ECB6C
BaseNetworkClient.LoadDefaultServerInfo   RVA 0x0B7F0CEC
BaseNetworkClient.SetNetAddrPort          RVA 0x0B7F0DEC
NetConnetData.SetIpAddress                RVA 0x07D70224
```

随后出现最终的 HTTP/Gateway 就绪日志，并观察到
`UnityWebRequest.set_url` 对 `DataVersion.unity3d` 和
`ResourceVersion.unity3d` 的真实 URL 重定向。运行过程中没有再出现
`invalid match pattern`。

## 版本边界

静态唯一性目前覆盖 `13.2.8_341` 与 `13.3.8_342` 的 ARM64 ELF，动态验证覆盖
`13.3.8_342` 的 MuMu Emulated Realm。后续客户端升级时，必须依据新版本 `types.cs`
确认函数边界，对完整 ELF 做唯一性扫描，再把新候选加入数组；不能仅凭相似序言或旧 RVA
直接安装 Hook。
