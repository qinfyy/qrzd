# QRZD Redirector 开发约定

- 使用中文，不启动子代理。
- 当前游戏是 NeoX/Python 2.7 的 com.netease.qrzd，不是 Unity。禁止重新引入 IL2CPP、BH2 Java SDK、RSA 字段旁路或猜测 Java 类名。
- 唯一运行配置为 Config.ini；最终源脚本只有 Redirector.py、FridaScript.js。
- Redirector.py 负责 spawn、双 Realm、配置握手、错误处理和生命周期；只加载 dist/FridaScript.js，不能自动编译。
- FridaScript.js 是唯一 Hook 源。Native Realm 只管理 MuMu Native Bridge；ARM64 Emulated Realm 处理 libclient.so/Python。
- 保留 ARM64 agent 路径、Native Bridge namespace 兼容方案。缺少 guest Realm 必须失败，不能静默降级。
- NativeFunction/Hook 前核对当前样本入口字节和映射。不同 SO 不得盲套 RVA。
- Python 注入只在持有 GIL 的游戏线程/模块完成回调执行，导入线程不操作 UI，不从定时器直接调用 Python。
- SDK 跳过使用 DEBUG_LOGIN；不改全局 DEBUG、不伪造通行证、角色或登录成功响应。
- HTTP 重定向只匹配已确认的主机、端口、方法和路径。保留请求体、签名及查询参数，重建本地 HTTPConnection，不能只把 HTTPS 地址换成本地明文端口。
- 当前 HTTP 白名单：g58.update.netease.com:443 的 server_list_android.txt、announcement_android、announcement_other；g58https.netease.com:11011 的 POST /account。
- 不接管 MPay、遥测、补丁/资源 CDN，不做任意 URL 代理；新增地址必须有客户端证据和 Sv 对应接口。
- Gateway 只改本地目标，保持客户端握手、加密和压缩，已经是本地地址时保留二次下发端口。
- 修改后运行 npm run build、Python 编译检查及 D:\f7\work\qrzd_tests\test_http.py。
- 验收需要客户端 URL 命中与 Sv 收到请求的双向证据，不能只看 Hook 已安装。
