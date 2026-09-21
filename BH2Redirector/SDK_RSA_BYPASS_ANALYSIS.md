# SDK 首次登录 RSA Bypass 问题分析

## 问题

旧脚本已经对账号 SDK 的 `RSAUtils.encryptByPublicKey(...)` 设置了 Frida Java
`implementation`：调用 RSA 方法时不执行加密，直接返回传入的账号、密码、区号或手机号。

实际运行先后出现了两类固定现象：

1. 启动游戏后第一次使用账号密码登录，服务端收到的仍是 RSA Base64 字符串。
2. 第一次登录期间没有出现 `[RSA Bypass]` 方法命中日志。
3. 再次登录时 `RSAUtils` Hook 才命中，服务端也开始收到未经 RSA 编码的字段。
4. 增加首次密码登录补偿后，第一次 `/loginByPassword` 已恢复正常，但第一次
   `/loginByMobileCaptcha` 的 `mobile` 仍可能保持 RSA Base64 字符串。

所以问题不是 RSA Bypass 的返回值写错，而是**第一次登录根本没有经过旧脚本替换后的
`RSAUtils` 方法入口**。

## Java 登录链路

对 13.2.8_341 APK 的 Java/Dex 代码检查后，密码登录的主要链路是：

```text
登录界面
  -> LoginManager.login(Activity, account, password, callback)
  -> LoginManager.loginByPassword(...)
  -> RSAUtils.INSTANCE.encryptByPublicKey(String)
  -> RequestUtils.createSign(HashMap)
  -> RequestUtils.createBody(HashMap)
  -> LoginApiService.loginByPassword(...)
```

手机验证码登录使用另一个公开 overload，随后进入同一组签名和包体函数：

```text
登录界面
  -> LoginManager.login(Activity, areaCode, phoneNumber, phoneCaptcha, callback)
  -> LoginManager.loginByMobileCaptcha(...)
  -> RSAUtils.INSTANCE.encryptByPublicKey(areaCode)
  -> RSAUtils.INSTANCE.encryptByPublicKey(mobile)
  -> RequestUtils.createSign(HashMap)
  -> RequestUtils.createBody(HashMap)
  -> LoginApiService.loginByMobileCaptcha(...)
```

该请求 Map 的字段为 `area_code`、`mobile`、`captcha`、`action_type`；只有
`area_code` 和 `mobile` 会经过 RSA，验证码和动作类型应保留客户端原值。

`com.mihoyo.platform.account.sdk.utils.RSAUtils` 使用 Java 的 `KeyFactory`、
`javax.crypto.Cipher` 和 `Cipher.doFinal` 完成 RSA，不是 JNI 或 SDK Native RSA。
因此不需要寻找一个不存在的 Native RSA 函数。

## 为什么第一次失败

旧脚本只 Hook 了下列 RSA 方法：

```text
com.mihoyo.platform.account.sdk.utils.RSAUtils.encryptByPublicKey(String)
com.mihoyo.platform.account.sdk.utils.RSAUtils.encryptByPublicKey([B, [B)
com.miHoYo.support.utils.RSAUtils.encryptByPublicKey(String, String)
com.miHoYo.support.utils.RSAUtils.encryptByPublicKey([B, [B)
```

这个方案有一个前提：登录代码调用 RSA 时，必须通过当前 `ArtMethod` 的入口分派到
Frida 安装的 replacement。

首次登录不满足这个前提。实机证据是：

- RSA Hook 在点击登录前已经报告安装成功。
- 第一次 `/loginByPassword` 请求确实已经发到服务端。
- 第一次请求中的字段仍是 RSA Base64 字符串。
- 同一期间没有任何 `RSAUtils` replacement 命中日志。

这说明第一次调用没有通过当前 `ArtMethod` 入口分派，而是走了 APK 已编译的 ART/AOT
直接调用路径并执行原来的 RSA 代码。对 ART 来说，这可能表现为已经解析的 quick entry、
直接调用或内联代码。无论具体属于哪一种，结果都相同：旧脚本虽然修改了 `RSAUtils` 的
方法入口，首次调用却没有从这个入口经过。

这也解释了为什么简单地提前安装 Hook、缩短轮询间隔或打印“Hook 已安装”都不能解决问题：
Hook 已经存在，但第一次登录没有从该入口经过。

## 为什么第二次能够生效

第一次执行会完成账号 SDK 相关类、方法和调用点的初始化与解析。实测第二次登录时，运行时使用的
方法分派路径已经进入当前 `RSAUtils` 方法入口，于是命中 Frida replacement，并直接返回
输入字段。

第二次出现 `[RSA Bypass]` 命中，而第一次完全没有命中，证明两次调用到达 RSA 代码的
运行时分派路径不同。现有证据可以确定“第一次没有走 replacement、第二次走了 replacement”，
但无法仅凭这些日志断言内部变化具体属于调用点解析、AOT quick entry 还是内联代码切换。
在当前 Android 15 x86 环境中继续读取或修改 `ArtMethod/quickCode`、调用
`Java.deoptimizeEverything()` 会触发 `expected a pointer`、SIGSEGV、ANR 或登录卡死。

因此本次修复不再依赖猜测具体是哪一种 ART 内部状态变化，而是直接避开这个不稳定的
RSA 方法入口。

## 新脚本如何解决

新脚本保留原来的四个 `RSAUtils` Hook，用于能够正常经过 RSA 方法入口的登录路径；同时增加
两条分别覆盖首次密码登录和首次手机验证码登录的 RSA Bypass 链路。

### 1. 在 RSA 之前保存原始字段

脚本 Hook：

```text
LoginManager.login(Activity, String, String, ILoginCallback)
LoginManager.login(Activity, String, String, String, ILoginCallback)
```

四参数 overload 拿到原始账号和密码；五参数 overload 拿到原始区号、手机号和验证码。
脚本连同登录类型暂存在 `sdkPendingLoginRsaBypass` 中，然后继续调用对应的原始
`LoginManager.login(...)`。

即使后面的首次调用仍然通过 AOT/内联路径执行 RSA，脚本也已经保存了 RSA 之前的值。

### 2. 在计算请求签名前恢复原始字段

脚本 Hook：

```text
RequestUtils.createSign(HashMap)
```

密码登录只有在 Map 同时包含 `account/password` 时才写回；验证码登录只有在 Map 同时包含
`area_code/mobile/captcha/action_type` 时才写回 `area_code/mobile`。后一个完整形状可排除只包含
`area_code/mobile` 的发送验证码请求。写回后再调用原始 `createSign`。

这一步必须发生在签名之前。否则只修改最终 JSON、不修改签名输入，会造成签名与请求字段不一致。

### 3. 在生成请求体前再次恢复原始字段

脚本 Hook：

```text
RequestUtils.createBody(HashMap)
```

脚本按登录类型再次写回 `account/password` 或 `area_code/mobile`，再由 SDK 正常生成请求体。
这样发往 `/loginByPassword` 或 `/loginByMobileCaptcha` 的 JSON 和上一步签名使用的是同一组
未经过 RSA 编码的字段。

请求体处理完成后，暂存字段立即清理；如果没有走到请求体，暂存数据也会在 30 秒后失效。

## 修复前后的执行差异

旧脚本第一次登录：

```text
原始账号和密码
  -> 首次 AOT/内联 RSA 路径
  -> 绕过 RSAUtils replacement
  -> 得到 RSA Base64 字符串
  -> createSign/createBody
  -> 服务端收到 RSA 字符串
```

新脚本第一次登录：

```text
原始账号和密码
  -> LoginManager.login Hook 保存原始字段
  -> 首次 AOT/内联 RSA 路径仍可能产生 RSA 字符串
  -> createSign Hook 写回原始字段并计算签名
  -> createBody Hook 再次写回原始字段并生成 JSON
  -> 服务端收到未经过 RSA 编码的字段
```

关键变化是：**新脚本不要求首次 RSA 调用必须命中 Frida 的 `RSAUtils` replacement。**
只要登录请求仍然经过 `LoginManager.login` 和 `RequestUtils.createSign/createBody`，RSA 之前的
原始字段就能在签名和组包之前恢复，第一次登录也能完成 RSA Bypass。

首次验证码登录原先仍失败，是因为第一版补偿只 Hook 了四参数密码登录入口，而且
`applyPendingSdkLoginRsaBypass` 只接受 `account/password` Map。即使验证码登录随后进入相同的
`createSign/createBody`，它的 `area_code/mobile/captcha/action_type` Map 也会被条件直接排除。

## 限制范围

- 只处理已确认的 BH2 Passport 密码登录与手机验证码登录字段。
- 只对 `account/password` 密码登录 Map，或完整的
  `area_code/mobile/captcha/action_type` 验证码登录 Map 写回数据。
- 不 Hook 通用 `Cipher`、OkHttp 或任意网络请求。
- 不修改 ART `ArtMethod`、quick entry 或全局去优化状态。
- 原有四个 `RSAUtils` Hook 继续兼容后续调用、短信登录和旧账号模块入口。

## 验证

新脚本实机运行后，首次密码登录和首次手机验证码登录都能进入对应补偿链。13.3.8_342
运行时已成功解析并 Hook 两个 `LoginManager.login` overload；首次验证码登录观察到：

```text
[RSA Bypass] Passport 登录入口已捕获待处理字段 手机验证码登录 ...
[RSA Bypass] Passport 登录签名前已应用原始字段 手机验证码登录 ...
[RSA Bypass] Passport 登录包体已应用原始字段 手机验证码登录 ...
```

同次请求中签名和包体日志显示的 `area_code/mobile` 均为原始值，而不是 RSA Base64。脚本
对应服务端日志显示 `/loginByMobileCaptcha` 请求体为 101 字节，数据库 `mobile` 查询参数长度
为 11；请求返回 200，随后 `/account/ma-cn-session/app/exchange` 也返回 200。这说明服务端实际
收到的是原始手机号而不是长 RSA Base64，并且登录流程继续进入会话交换。日志证据只记录
字段长度，不在报告中保存真实手机号、验证码或令牌。脚本构建和语法检查同时通过：

```text
node --check FridaScript.js
npm run build
py -m py_compile Redirector.py
```

运行日志会在 `[RSA Bypass]` 后显示实际 RSA 参数，以及登录入口、签名和包体阶段使用的
原始账号密码或区号、手机号和验证码。日志可能含敏感字段，只用于本地验收。
