# QRZD

`QRZD` 是针对移动端都市幻想 RPG? ADV 手游《永远的 7 日之都》进行协议逆向研究、网络线缆分析与服务端环境模拟的 开源? 研究项目

---

## 核心架构与特性

- **现代 .NET 技术栈**：基于 .NET 10 (C# 13) 与 ASP.NET Core 构建，利用高性能 Socket 与异步 I/O 提供低延迟高并发网关服务。
- **DDD 领域聚合根架构**：服务端领域层严格遵循统一标准，以 `Player` 为核心聚合根，按模块组合 `CityLogic`、`HeroMgrLogic`、`InventoryLogic`、`WeekNumLogic` 等充血子系统。
- **单表 BLOB 高性能持久化**：采用经典商业游戏服务端设计，以二进制 Protobuf 序列化流持久化存储于 SQLite，实现毫秒级原子存盘与向前向后版本兼容。

---

## 工程子系统概览

```text
D:\f7\
├── Sv/                    # 游戏服务器工程
│   ├── Configuration/     # 服务端配置
│   ├── Database/          # 数据库层
│   ├── Game/              # 玩家领域聚合根与充血业务逻辑
│   ├── Gateway/           # 网关长连接服务
│   ├── Http/              # HTTP 端点
│   ├── proto/             # 游戏 Protobuf
│   ├── Resources/         # 网关底层通信 Protobuf
│   └── Utility/           # 通用辅助工具
├── QRZDRedirector/        # 客户端流量重定向模块
├── docs/                  # 文档
└── NeoXDec/               # NeoX 资源与脚本逆向解析恢复工具
```

---

## 依赖环境

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) 或更高版本
- 操作系统：Windows / Linux / macOS (x64 / ARM64)
- 客户端环境：通过 `QRZDRedirector` 将客户端流量重定向至服务端

---

## 快速上手

### 1. 编译项目

在项目根目录下使用 .NET CLI 构建解决方案：

```powershell
dotnet build Sv.slnx
```

如需 Release 优化版本：

```powershell
dotnet publish Sv/Sv.csproj -c Release -o ./publish/Sv
```

### 2. 运行服务端

启动主服务工程 `Sv`：

```powershell
dotnet run --project Sv
```

服务启动后，默认将监听以下网络端口：

| 服务通道 | 默认端口 | 协议类型 | 功能说明 |
| :--- | :---: | :---: | :--- |
| **HTTP Dispatch** | `21000` | HTTP/1.1 | 区服列表分发、运营公告 |
| **TCP Gateway** | `4120` | TCP | 核心游戏长连接 |

### 3. 配置说明

服务端配置文件位于 `Sv/appsettings.json`，核心配置如下：

```json
{
  "Server": {
    "HttpPort": 21000,
    "GatewayPort": 4120,
    "AdvertiseHost": "127.0.0.1",
    "AdvertisePort": 4120,
    "ServerId": 5004,
    "HostId": 1001,
    "GatewayPrivateKeyPemPath": "ServerData/gateway_private.pem",
    "GatewayPublicKeyPemPath": "ServerData/gateway_public.pem"
  },
  "Database": {
    "Path": "save/SaveData.db"
  }
}
```

> **注意**：`AdvertiseHost` 应设置为客户端能实际路由访问的主机 IP 或域名（如局域网 IP），切勿填写 `0.0.0.0`。

### 4. 客户端连接

配合 `QRZDRedirector` 注入并重定向客户端：
1. 在 `QRZDRedirector/Config.ini` 中配置目标 IP 与端口；
2. 运行 `Redirector.py` 启动模拟器流量截获与公钥注入；
3. 打开客户端即可直接连入本地测试服（区服 5004）并顺利进入交界都市。

---

## 文档索引

- **网络协议分析**：请参阅 [网络协议分析](docs/QRZD_NETWORK_PROTOCOL_ANALYSIS.md)，包含握手流程、RC4/ZLib 连续流、MD5 方法散列与完整实体时序。

---

## 免责声明

1. 本项目仅供计算机网络通信协议分析、逆向工程技术研究以及个人学习交流使用。
2. 本项目不包含任何商业游戏的专有受版权保护资产（如音频、模型、贴图及客户端二进制文件）。
3. 请勿将本项目用于任何形式的商业用途或侵犯第三方版权持有者权益的活动。

## 贡献

开发时，如果您使用 Vibe Coding 进行开发，请让 Agent 遵守各 CLAUDE.md 中的开发约定，特别是关于 Sv 代码结构的部分。

## 鸣谢

感谢 ChatGPT、Gemini、Claude、GLM、MiniMax 和 DeepSeek 在项目文档维护，代码实现与逆向分析过程中提供的帮助。所有 Markdown 文档和一大半的代码都是它们写的。

## 许可

许可信息详见 [LICENSE.txt](LICENSE.txt)。
