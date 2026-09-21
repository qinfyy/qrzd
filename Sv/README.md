# QRZD HTTP 框架

保留 .NET 10 / ASP.NET Core、配置和 Serilog 日志，已移除原 BH2 业务、协议、GM、数据库模型和资源表。

在本目录执行 `dotnet run`。HTTP 默认监听 `0.0.0.0:21000`，模拟器访问地址由 `appsettings.json` 的 `Server:AdvertiseHost` 指定。

## 接口

- `GET /health`：服务状态，明确返回 `gatewayImplemented=false`。
- `GET /server_list_android.txt`：九列网关记录、`########\n` 分隔符、六列区服记录；当前只发布本地测试服。
- `GET /announcement_android`、`GET /announcement_other`：客户端要求的十四列 UTF-8 CSV 公告。
- `POST /account`：接收 JSON `tm`、`an`，校验原始请求体的 `ACCOUNT-SIGNATURE` HMAC-SHA256；返回当前 HostId 对应的空角色数组。

账户查询最大 4096 字节，默认允许 300 秒时钟偏差。错误签名返回 401，非法 JSON/时间返回 400，过大请求返回 413。日志不记录请求体、签名或账号。

未知路径返回 404，无通用代理，无 BH2/MPay 认证兼容接口。HTTP 调试不代表游戏登录完成；4120 只是区服发布地址，当前不启动 TCP Gateway。

## 证据与验证

格式来自 `D:\f7\登录.har` 和恢复源码中的 `com/utils/server_info.py`、`cocos/panels/activity_notice.py`、`cutils/chelpers.py`。不导入 HAR 中的真实账号、令牌或角色数据。

执行 `dotnet build`，启动服务后运行 `py D:\f7\work\qrzd_tests\test_http.py`。旧源码与存档在 `D:\f7\work\bh2_before_qrzd_20260921.zip`，不再参与当前项目编译。
