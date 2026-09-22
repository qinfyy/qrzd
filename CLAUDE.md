# CLAUDE.md

This file provides architecture standards, domain rules, and development guidelines for AI coding agents and human contributors working in this repository.

---

## 1. 架构总览与定位

本项目（服务端工程 `Sv`）是围绕网易 NeoX 引擎都市幻想手游《永远的 7 日之都》（`com.netease.qrzd`，版本 83）展开的协议逆向研究、网络线缆分析与私服环境实现。

### 1.1 技术栈与核心依赖
- **运行时环境**: .NET 10 (C# 13)
- **网络层**: 异步 TCP Socket、小端 6 字节二进制帧头、Google.Protobuf (`proto2`)、BouncyCastle (RSA-OAEP / RC4 流密码)、SharpZipLib (全会话连续 ZLib 字典流)
- **Web / HTTP**: ASP.NET Core Minimal API（区服分发、运营公告、免 SDK 账号角色映射）
- **持久化层**: Microsoft.EntityFrameworkCore.Sqlite，单表 `Players` 存储 `ServerProto.PlayerSaveData` 二进制 BLOB（开启 WAL 模式）
- **混合序列化**: BSON (MongoDB.Bson，用于实体 RPC 参数) 与 MessagePack + ZLib (用于全量角色快照)
- **日志诊断**: Serilog 异步控制台与文件结构化日志

### 1.2 核心系统分层架构
整个服务端严格对齐统一架构规范，保持高度解耦的分层设计：

```
┌─────────────────────────────────────────────────────────────┐
│                    HTTP & TCP Ingress                       │
│    ASP.NET Core (Port 21000)   │    TCP Gateway (Port 4120) │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                    Gateway Routing Layer                    │
│   GatewayHostedService (在线连接管理)                       │
│   GatewaySession (Socket流, RC4流加密, 连续ZLib流)          │
│   GatewayRouter (MD5方法签名散列分发)                       │
│   AeadTool (RSA-OAEP, RC4, MD5 散列唯一加解密入口)          │
│   Gateway/Handlers/ (LoginHandlers, PlayerHandlers 同步处理)│
│   Gateway/Packets/ (BasePacket 充血数据包派生类)             │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                   Domain Layer (Sv.Game)                    │
│  ┌───────────────────────────────────────────────────────┐  │
│  │               Player (唯一聚合根，持有 SyncRoot)       │  │
│  │        按值持有 ServerProto.PlayerSaveData (存储态)    │  │
│  │        提供 ToAvatarSnapshot() 生成角色初始快照        │  │
│  └───────────────────────────┬───────────────────────────┘  │
│                              │ 组合持有各充血业务子模块     │
│    ┌───────────────┬─────────┴─────┬───────────────┐        │
│    ▼               ▼               ▼               ▼        │
│ PlayerProfileLogic CityLogic   WeekNumLogic    StatusLogic  │
│ InventoryLogic HeroMgrLogic    SocialLogic   IntelligenceLogic│
│ (充血业务类，继承自 PlayerLogicBase，各自独占对应 Comp 切片) │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                 Singletons & Foundation                     │
│  GameDatabase.Instance (SQLite 数据库单例, WAL 模式, BLOB)  │
│  GameDbContext (EF Core 上下文与时区转换)                   │
│  Entities.cs (PlayerEntity 实体定义)                        │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. 领域层开发规范 (Domain Standards)

### 2.1 严格的面向对象 (OOP) 组合架构
- **充血模型高于一切**：业务校验、数值运算、状态变更、资产发放一律封装在对应的 `*Logic` class 中。
- **严禁滥用贫血 record / DTO**：严禁创建仅用于在方法之间搬运几个字段的无行为 DTO。
- **严禁制造伪抽象 (No Artificial DTOs)**：
  - 严禁发明 `*Outcome`、`*Result`、`*Options`、`*Context`、`*Mutation` 等多余类型。
  - 领域方法入参直接使用领域语义明确的参数（如 `int heroId`、`int count`）；返回值直接返回领域对象或基础类型。

### 2.2 玩家聚合根 (`Player`) 规范
- **唯一领域聚合根**：`Player.cs` 严禁写成 `partial class`，严禁在其中堆积具体玩法的细节代码。它仅承担：
  - 独占持有并发排他锁：`public object SyncRoot { get; } = new();`
  - 玩家存档存储态：`public PlayerSaveData SaveData { get; }`
  - 脏标记跟踪：`public bool IsDirty { get; private set; }`
  - 初始全量快照生成：`public byte[] ToAvatarSnapshot(ServerOptions options)`
  - 各领域 Logic 属性的只读暴露（如 `player.City`, `player.HeroMgr`, `player.Inventory`）
  - 确定性生命周期调度（`OnCreate`, `OnLoad`, `OnLogin`, `BeforeSave`）与事件派发（`Trigger`）
- **内存态 = 存储态**：`PlayerSaveData`（Protobuf 生成类）是唯一的玩家存储状态，无二次转化层。
- **持久化入口**：持久化调用统一为 `player.Save()`，内部委托 `GameDatabase.Instance.Save(this)`。

### 2.3 业务 Logic (`PlayerLogicBase`) 规范
- **构造函数规范**：有状态 Logic 统一继承 `PlayerLogicBase`，构造函数**仅允许接收 `Player` 聚合根自身**。
  - 对应的 `XxxComp` 切片通过 `player.SaveData.XxxComp` 获取。
- **状态边界**：每个 Logic 严格只读写归属于自己的 `XxxComp` 切片，严禁跨模块篡改他人数据。
- **修改即标记**：一旦数据发生变更，必须调用 `MarkDirty()` 标记存档变动。

### 2.4 代码排版与格式规范 (Formatting Rules)
- **单行不超过 170 字符不换行**：
  - 方法声明/签名、方法调用、构造函数调用、参数传递等，只要合并后单行不超过 170 字符（含缩进），一律写在同一行，严禁无故折行。
  - 左括号 `(` 后面直接跟第一个参数，严禁在 `(` 与首个参数之间插入空格（如 `Method(arg1, arg2)`）。
- **`if` 条件不换行**：`if (...)` 判定条件只要单行不超过 170 字符，必须写在同一行内，不进行跨行折断。
- **三元表达式永远不允许换行**：三元表达式（`condition ? trueValue : falseValue`）必须保持在同一行书写，永远不允许折行。
- **三元表达式不得超过两层**：三元表达式嵌套最多允许两层；若逻辑超过两层嵌套，必须使用 `if / else` 语句重构。
- **LINQ 链式调用照常换行**：LINQ 链式查询（如 `.Where(...)`、`.Select(...)`、`.OrderBy(...)` 等）维持标准多行折行排版。

---

## 3. 网关与网络分层规范 (Gateway Standards)

### 3.1 实体寻址与 MD5 散列路由
- 网络传输严格遵循网易 NeoX 分布式实体协议：
  - **寻址模型**：12 字节 MongoDB ObjectId 实体寻址 + 16 字节 MD5 方法名散列。
  - **加解密唯一入口**：所有 RSA-OAEP、RC4 密钥流、MD5 方法哈希收敛至 `Gateway/Protocol/AeadTool.cs`。
- 网关根目录仅保留 3 个核心基础设施类：
  - `GatewaySession.cs`：独占管理 Socket 流、RC4 加密流、ZLib 连续压缩流与发包互斥锁。
  - `GatewayHostedService.cs`：监听 TCP 4120 端口，管理在线玩家 Session 映射与互踢。
  - `GatewayRouter.cs`：根据 MD5 方法哈希将实体报文快速分发至对应的 Handler。

### 3.2 Gateway Handler 规范
- **同步纯函数式处理**：Handler 方法统一为纯同步 `void OnXXX(GatewaySession session, ...)` 命名。
- **严禁滥用 `CancellationToken`**：网关收发由 Session 统一接管，Handler 内部**严禁随意传递 `CancellationToken`**。
- **状态安全锁**：凡涉及玩家领域状态修改的逻辑，必须置于 `lock (player.SyncRoot)` 锁块内执行，执行完毕后调用 `player.Save()` 存盘，随后调用 `session.SendPack(packet)` 推送下行包。

### 3.3 数据包充血类 (`Gateway/Packets/`)
- 所有下行数据包统一继承 `BasePacket`，必须实现 `ushort Method` 与 `IMessage CreateMessage()`。
- 发包时调用 `session.SendPack(new XxxPacket(...))`，由 Session 自动处理 Protobuf 序列化、ZLib 压缩与 RC4 加密。

---

## 4. Coding Agents 红线规范 (Red-Line Rules)

凡是 AI Agent 在本仓库执行任何任务，必须无条件遵守以下十条绝对禁令：

> [!CAUTION]
> ### 🚨 红线 1：零编译警告与零编译错误
> 本项目开启了 `TreatWarningsAsErrors=true`。任何代码改动后，必须运行 `dotnet build Sv.slnx` 并确保输出为 **0 Warning(s), 0 Error(s)**。
> 严禁引入任何未使用的变量、空引用警告或废弃 API 警告。

> [!CAUTION]
> ### 🚨 红线 2：严禁制造贫血 DTO 与中间过渡伪抽象
> 严禁创建 `*Outcome`、`*Result`、`*Options`、`*Context`、`*Mutation` 等伪抽象类。
> 坚持高内聚 OOP：业务运算与校验属于领域对象的职责，方法间直接传递充血实体或基本类型。

> [!CAUTION]
> ### 🚨 红线 3：并发排他与确定性存盘
> 所有引起玩家状态改变的代码，必须置于 `lock (player.SyncRoot)` 保护之下。
> 状态变更完成后必须显式执行 `player.Save()` 完成 BLOB 刷盘。严禁绕过锁直接读写玩家数据。

> [!CAUTION]
> ### 🚨 红线 4：严格遵守架构分层边界
> 1. 网关 Handler 严禁内联具体的业务计算逻辑，必须分发给领域层；
> 2. 严禁把 `Player` 作为 EF Core 实体，禁止把玩家状态拆分为关系型多表。

> [!CAUTION]
> ### 🚨 红线 5：数据库持久化纯净化
> 本项目为《永远的 7 日之都》，数据库实体仅保留 `PlayerEntity`（`Uid`, `Name`, `ServerId`, `CreatedAt`, `UpdatedAt`, `Data`），严禁再次引入任何米哈游（BH2）残留的 `ComboToken`、`SessionToken`、`ChannelId`、`OpenId`、`Uuid` 等字段。

> [!CAUTION]
> ### 🚨 红线 6：严禁硬编码二进制抓包 Payload
> 严禁把抓包的十六进制字符串或 Base64 字符串作为硬编码常量下发。
> 必须使用强类型的 `BasePacket` 或结构化 BSON/Protobuf 消息构建下行数据。

> [!CAUTION]
> ### 🚨 红线 7：精确结构化代码编辑
> Agent 修改代码必须使用系统提供的结构化编辑工具（`replace_file_content` 或 `write_to_file`），并进行完整上下文定位。
> 严禁使用 PowerShell 脚本、sed、awk 等命令行工具粗暴批处理替换源码。

> [!CAUTION]
> ### 🚨 红线 8：加解密与协议收敛
> 所有网络协议加解密（RSA-OAEP、RC4、MD5）必须唯一收敛在 `Gateway/Protocol/AeadTool.cs`，其他协议基础工具放在 `Gateway/Protocol/`目录下，严禁在其他地方另起炉灶编写冗余的加解密工具类和工具函数。

> [!CAUTION]
> ### 🚨 红线 10：严禁擅自更新 README.md 与 CLAUDE.md
> `README.md` 与 `CLAUDE.md` 分别是项目的对外说明与全局 Agent 规则基准。
> Agent 严禁在日常开发、功能迭代或代码重构中随意自行更新 `README.md` 和 `CLAUDE.md`。
> 必须在用户明确发出指令要求更新时，才允许对相应文档进行修改。

---

## 5. 日常验证与交付清单

在向用户汇报或结束任务前，按顺序完成以下验证：

1. **静态编译检查**：
   ```powershell
   dotnet build Sv.slnx
   ```
   必须确保输出为 `0 Warning(s), 0 Error(s)`。

2. **代码整洁度自查**：
   - 是否严格遵守了 10 大红线？
   - 是否存在遗留的未跟踪临时文件？
   - Git 状态是否保持干净聚焦（`git status -s`）？
