# QRZD Redirector

服务对象为 com.netease.qrzd，NeoX/Python 2.7 ARM64 客户端。目录名暂沿用 BH2Redirector，旧 Unity/Java Hook 已清除。

## 交付与启动

源脚本只有 FridaScript.js 和 Redirector.py。Config.ini 是唯一配置入口；dist/FridaScript.js 为编译产物，不手改。

在本目录执行：

~~~powershell
py -m pip install -r requirements.txt
npm install
npm run build
py Redirector.py
~~~

先在 D:\f7\Sv 执行 dotnet run 启动 HTTP 服务。两边配置中的本地地址应一致。

当前模拟器使用 CLIENT_ARCH=arm64-v8a、EMULATOR=true。Native Realm 管理 Native Bridge，Emulated Realm 操作 ARM64 libclient.so。设备端须有同版本 /data/local/tmp/re.frida.server/frida-agent-arm64.so。本次使用 Frida 17.17.0。

## 已接入

| 原地址 | 方法/路径 | 本地响应 |
| --- | --- | --- |
| g58.update.netease.com:443 | GET /server_list_android.txt | 本地区服及 Gateway 地址 |
| g58.update.netease.com:443 | GET /announcement_android | 本地公告 CSV |
| g58.update.netease.com:443 | GET /announcement_other | 本地公告 CSV，调试登录实测使用此路径 |
| g58https.netease.com:11011 | POST /account | 验证原始字节 HMAC-SHA256，返回空角色列表 |

HTTP 转发在 Python httplib 中按主机、端口、方法、路径精确匹配。转到本地明文 HTTP，保持原请求体与 ACCOUNT-SIGNATURE，不伪造 SDK 凭据。
URL 命中日志在 Android logcat 中以 [NeoX-HTTP] 开头，服务端日志位于 D:\f7\Sv\logs。

## 当前边界

- DEBUG_LOGIN 已在模拟器验证，用户确认可以打开调试账号输入框；只启用登录调试分支，不打开全局 DEBUG。
- 当前只支持已核对的 ARM64 libclient.so，SHA256 a8c2e2f915413640eab7e9615f47787f04c36dd1bbfdec26c80fb2dfee6e60dc。
- 双 Realm 启动保留 Native Bridge 初始化等待；最早的启动请求可能发生在 ARM64 agent 安装之前，不能宣称全部启动流量都被接管。
- NeoX 游戏模块（包括 cocos.panels.login）和 SDK Bypass 回执无限等待，不再使用 110/120 秒截止时间。可按 Ctrl+C 停止；进程退出和实际脚本错误仍会中止。Native Bridge、配置握手和 libclient.so 装载检查仍保留各自超时。
- 补丁、资源 CDN、遥测和 MPay 不在 HTTP 白名单中。未实现的本地路径返回 404。
- SDK 登录被跳过不等于进入游戏。TCP Gateway、会话握手、角色创建和游戏世界尚未实现。
- USE_NEOX_SDK_BYPASS=false 会关闭 NeoX 注入，不是“保留转发、只关 SDK 开关”。
- 本地 Frida 二进制部署材料移到 D:\f7\work；旧工具/文档归档为 D:\f7\work\redirector_legacy_20260921.zip。

## 验证

~~~powershell
npm run build
py -m py_compile Redirector.py
py D:\f7\work\qrzd_tests\test_http.py
py -B D:\f7\work\qrzd_tests\test_redirector.py
~~~

测试要求本地 Sv 已监听 21000。测试不会访问外部游戏服务器，也不会读取 HAR 中的真实账号。
