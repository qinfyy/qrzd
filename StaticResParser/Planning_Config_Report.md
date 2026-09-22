# QRZD 策划配置定位报告

更新时间：2026-09-22。范围：用户提供的版本 83 APK、热更目录和已恢复 Python；不是线上当前版本认证，也不是美术资源导出。

## 1. 结论

**有独立于 Python 脚本的外置策划表。主体在 `res/dbx.npk`，不是散落的 JSON/CSV，也不需要去模型、贴图包里找。**

同一批配置存在两种发布形态：

1. `script.npk` 中的 Python 数据模块，已恢复到 `D:\f7\Reverse\Script\py\com\data\cdata`，模块的 `data` 本身就包含配置数据。
2. `res/dbx.npk` 中的二进制表，由 `dbx` 库加载；每张表由正文、行索引以及可选压缩字典组成。

两份 DBX 包合并后已经完成全量校验：**11,020 张表，6,528 张非空、4,492 张空表，共 458,631 行，全部 LZ4 解压和 MessagePack 解析成功。**

这不是“11,020 张服务端玩法表”：其中也有对话、界面、皮肤等客户端配置。能确定的是，这批数据中确实包含道具、角色属性、升级经验、城市建设、关卡、奖励等策划数据。

## 2. 物理位置与热更合并

| 来源 | 文件 | NPK 条目数 |
|---|---|---:|
| APK 基础库 | `D:\f7\Reverse\android_gles2_release_common_min_netease_83\assets\res\dbx.npk` | 24,279 |
| 热更覆盖层 | `D:\f7\Reverse\com.netease.qrzd\files\netease\qrzd\Documents\res\dbx.npk` | 6,416 |
| 热更校验清单 | `D:\f7\Reverse\com.netease.qrzd\files\netease\qrzd\Documents\cache\checks\dbx.check` | 24,279 条记录 |
| 热更脚本 | `D:\f7\Reverse\com.netease.qrzd\files\netease\qrzd\Documents\script.npk` | 71 |

**热更包不能单独当作完整配置库。按资源条目优先取热更，缺失时回退 APK。**

| 组成部分 | 取自热更 | 回退 APK | 不存在 |
|---|---:|---:|---:|
| `.dbx` 正文 | 2,160 | 8,860 | 0 |
| `.dbxh` 索引 | 2,155 | 8,865 | 0 |
| `.dbxcd` 字典 | 2,100 | 138 | 8,782 |

回退必须以单个文件为单位。例如 `chunjiejiyifanbei` 正文在热更包，索引仍取 APK。没有字典的表不一定异常，本次无字典的表同样通过了全量解码。

依据：`D:\f7\Reverse\android_gles2_release_common_min_netease_83\assets\neox.xml:32` 的资源加载顺序是 Documents 散文件、Documents NPK、APK 散文件、APK NPK。更直接的验证是：合并后的 24,278 个表组成文件以及 1 个清单，全部与热更 `dbx.check` 的 MD5 一致。

`dbx.check` 是 zlib 压缩的热更元数据，不是策划表正文。格式由 `D:\f7\Reverse\Script\py\patch\patch_file.py:24` 附近的加载代码确认，每行包含 ID、MD5、压缩/解压长度与校验值、版本及偏移。

## 3. 逻辑文件与格式

两份包都有 `dbx_md5.json`，内容是 11,020 个表名到正文 MD5 的映射，实际解包文件均为 `4b1354f6.json`。

```text
逻辑资源名                         内容
dbx/dbx_md5.json                   表名 -> .dbx 正文 MD5
dbx/item_data.dbxh                 MessagePack: 行主键 -> (offset, length)
dbx/item_data.dbxcd                可选 LZ4 原始字典
dbx/item_data.dbx                  多段独立 LZ4 block 拼接

读取一行：索引定位 -> 截取正文片段 -> 带字典 LZ4 解压 -> MessagePack 解码
```

主要源码证据：

- `D:\f7\Reverse\Script\py\lib\dbx\dbx\config.py:6`：`lz4`、`line`、`npk`、`msgpack`、`res/dbx/`。
- `D:\f7\Reverse\Script\py\lib\dbx\dbx\storage_engine.py:90`：索引、字典、逐行读取实现。
- `D:\f7\Reverse\Script\py\lib\dbx\dbx\compress_lib.py:29`：`lz4.block.decompress(msg, dict=self.cdict)`。
- `D:\f7\Reverse\Script\py\lib\dbx\dbx\serialize_lib.py:32`：`use_list=False`，复合主键需保持 tuple。
- `D:\f7\Reverse\Script\py\lib\dbx\dbx\file_operator.py:20`：通过 `C_file` 读取逻辑资源。

这些不是普通独立 `.bin` 表，也不能直接对整份 `.dbx` 做一次 LZ4 解压。MessagePack 中大量文本为 bytes，查看中文时需按 UTF-8 解码；导出时不能将整数主键和 tuple 主键无差别转为字符串。

### 文件名恢复

NPK 条目只保存散列 ID，解包器显示的 `.bin`、`.json` 是文件头启发式猜测，不是真实扩展名。比如城市升级正文被猜成 `.json`，但内容仍是压缩块。

此样本的实际散列函数是 `libclient.so` 的 `neox::StringIDLegacy`，RVA `0x2589C48`。通过离线 Unicorn 执行原函数确认，未手写替代散列算法：

```text
StringIDLegacy("dbx_md5.json")  = 4b1354f6
StringIDLegacy("item_data.dbx") = e8687560
```

散列输入是包内文件名，不带 `dbx/` 前缀。包头里的 `0,0,2` 组合经原生 `0x195F310` 判断实际选择 Legacy，不能看到最后一个 `2` 就直接使用 Murmur3。

## 4. 确实属于策划数据的实例

以下表均已逐行成功解码；本表中的正文、索引、字典均来自热更，除 `city_development` 没有字典。

| 表名 | 行数 | 正文 ID | 索引 ID | 字典 ID | 关键内容 |
|---|---:|---|---|---|---|
| `item_data` | 12,434 | `e8687560` | `15a75cf8` | `71ca2652` | 道具类型、叠加上限、使用类型、角色关联 |
| `hero` | 140 | `310e68e6` | `5fa283b8` | `fdd08b16` | 基础属性、属性成长、技能、建设与巡查能力 |
| `player_exp` | 200 | `5cfc781c` | `3ca55dac` | `b711d12d` | 等级、升级经验、累计经验 |
| `city_building` | 47 | `c637cf8e` | `86d04d5b` | `8e293328` | 建设条件、建筑类型、产出属性 |
| `city_development` | 72 | `33e86aaf` | `86e39494` | 无 | 区域与开发等级要求 |
| `city_upgrade` | 45 | `c87546d9` | `6eeb1a2a` | `5fdd849f` | 行动力、疲劳、金币消耗与产出 |
| `mission` | 2,326 | `e5fcaa8b` | `5e6bd2fa` | `11062d9b` | 关卡限制、怪物、初始化事件、队伍人数 |
| `reward_common` | 31,907 | `3a4075bc` | `93f64a3a` | `19407115` | 奖励道具、数量、奖池与交易类型 |
| `reward_random` | 22,214 | `0eb589da` | `ae344d82` | `0b95cc61` | 展示条目、抽取权重 |

实际读取示例，以下仅节选字段：

```python
player_exp[1] = {'lv': 1, 'lv_exp': 1300, 'all_exp': 1300}
item_data[1] = {'name': '阿岚碎片', 'heroid': 6, 'wrap': 10000, ...}
hero[2] = {'name': '米菈', 'constructValue': 4, 'insightValue': 9, ...}
city_development[(1, 1)] = {'level_require': 1, 'name': '中央庭'}
city_upgrade[(1, 1)] = {'money_output': 50, 'money_consume': 0, ...}
```

可读样本在 `D:\f7\StaticResParser\analysis\evidence\samples.json`。该文件只展示代表行，并用 `{"tuple": [...]}` 保留 tuple 含义，不是完整服务端可直接导入的 JSON 表库。

## 5. Python 与 DBX 的关系、版本变化

`D:\f7\Reverse\Script\py\com\data\cdata` 有 11,021 个 `.py`：11,019 个名称与 DBX 对应，另有 `__init__` 和 `state_mutex`。DBX 多出的 `model_data` 对应 `combat.base.data.model_data`，不能简单说目录和清单完全一一相等。

`state_mutex.py` 自身含 `data`，不在这份 DBX 清单中。因此 **DBX 完整解码不等于获得客户端所有常量和所有配置**。还应结合 `com/design_const.py`、`com/const.py` 及其他直接嵌在脚本中的数据。

两份 DBX 清单的表名集合一致，有 36 张表的正文 MD5 改变。完整列表在 `analysis/evidence/summary.json` 的 `changed_tables`。

例如 `item_data` 正文 MD5：

```text
APK   2b483a138a5fe471fae7cb76b64e392e
热更  78b063e1ec79be563f1daa710211c4cc
```

热更脚本包用现有工具全部解包、恢复为 PYC：71/71 成功，其中 38 个是数据模块。上述 36 张变化表全部有对应的热更脚本，另有 `release_model_blacklist`、`release_ui_blacklist` 两个数据模块。热更 `hero` 正文则与 APK 的 MD5 相同，说明“存在于热更包”不代表“相对 APK 有数值变化”。

### 运行时启用状态的边界

- `dbx_import_hook.py:14` 只有在 `config.USE_DBX` 为真时才接管数据模块导入。
- `dbx_import_hook.py:124` 将模块 `data` 设置为 `CollectionProxy(partname)`。
- `collection_proxy.py:33` 根据 `USE_DBX` 选择 DBX 行读取或原始 Python 模块数据。
- APK 中 `dbx/dbx/config.py:23` 的静态值是 `USE_DBX = False`。
- APK 与本次恢复的热更 `com.const` 都有 `DEBUG_DBX = 1`；热更反编译文件是 `analysis/hotupdate_script/py/const.py:331`。
- `patch/patch.py:598` 安装 hook 并设置 `gg.use_dbx`，这本身不等于把 `config.USE_DBX` 设置为真。

本轮检查时模拟器中的游戏进程未运行，没有启动游戏、没有使用 Frida 注入。因此这里只确认两套数据实际存在及静态加载分支，**不声称当前运行实例已经启用 DBX，也不声称两套数据每个字段都完全一致**。后续若需确定运行时选择，应在游戏解释器持 GIL 的回调内只读检查这两个开关和 `data` 的实际类型。

## 6. 排除容易混淆的资源

- `model_dbx.npk`：虽然也是 DBX 格式，索引的 501 个键却是 `model/*.gim`、`model_skin/*.gim`。已解码样本为模型路径、尺寸、挂点变换矩阵、动画长度与事件时间，不是本次要找的通用玩法数值表。不能将它和主 `dbx.npk` 中同名的 `model_data` 混为一谈。
- `csv.npk`：现有解包器解出 1 个 JSON 对象，1,260 条记录，样本为名称对应作者标识与版本字段 `a/v`。内容更符合导出来源/版本元数据，未发现它是一套道具、角色、奖励数值表。没有仅凭包名把它认作策划 CSV。
- `hero.npk`、`model*.npk`、`cocos2.npk` 等大型美术包未做全量解包；本次没有转向图片、模型提取。
- `input/DataVersion.unity3d`、`input/ResourceVersion.unity3d` 及原有依赖没有用于本次 NeoX 表定位，也未删除。

## 7. 复现方式与交付证据

NPK 解包全部复用 `D:\f7\NeoXDec\NpkDec\unpack.py`，没有另造 NPK 解包器，也没有修改现有工具源码。

```powershell
python -B D:\f7\NeoXDec\NpkDec\unpack.py D:\f7\Reverse\android_gles2_release_common_min_netease_83\assets\res\dbx.npk --type res -o D:\f7\StaticResParser\analysis\apk
python -B D:\f7\NeoXDec\NpkDec\unpack.py D:\f7\Reverse\com.netease.qrzd\files\netease\qrzd\Documents\res\dbx.npk --type res -o D:\f7\StaticResParser\analysis\hotupdate
```

研究脚本 `D:\f7\StaticResParser\analysis\verify_dbx.py` 只读取**既有解包文件**，复用 `Reverse/work/native_probe.py` 读取 ELF，以 Unicorn 调用原生散列，再调用标准 `lz4`、`msgpack` 校验 DBX。它不读取或解密 NPK 容器，也没有构建服务器表加载器。

首次建立证据目录时运行：

```powershell
python -B D:\f7\StaticResParser\analysis\verify_dbx.py
python -B D:\f7\StaticResParser\analysis\verify_dbx.py --verify-all-rows
```

为避免覆盖既有证据，首个命令拒绝已存在的 `evidence` 目录；第二个命令拒绝覆盖已有 `all_rows.json`。本工作目录已执行完毕，不应直接重复运行覆盖结果。

环境：Python 3.14、lz4 4.4.5、msgpack 1.1.2；msgpack 隔离安装到 `StaticResParser/.deps`，Unicorn、pyelftools、capstone 复用 `Reverse/work/.deps`。热更脚本修复用现有 `NeoXDec/PyDec/fix.py`，单个常量模块反编译用 `NeoXDec/venv/Scripts/python.exe` 运行现有 `PyDecompiler/decompile.py`。默认系统 Python 缺少 xdis，已改用项目已有虚拟环境验证成功。

| 证据文件 | 内容 |
|---|---|
| `analysis/apk/dbx/manifest.tsv` | APK 24,279 条解包记录，全部成功 |
| `analysis/hotupdate/dbx/manifest.tsv` | 热更 6,416 条解包记录，全部成功 |
| `analysis/evidence/tables.tsv` | 11,020 张表的真实表名、条目 ID、实际文件路径、来源、行数、MD5 |
| `analysis/evidence/summary.json` | 原生样本身份、热更变化列表、第一轮校验结果 |
| `analysis/evidence/all_rows.json` | 全量 458,631 行解码结果和热更校验清单比对，失败数 0 |
| `analysis/evidence/samples.json` | 43 张代表/变化表的可读样本，每表最多 3 行 |
| `analysis/hotupdate_script/nxc/manifest.tsv` | 71 条热更原始脚本的解包记录 |
| `analysis/hotupdate_script/pyc/manifest.tsv` | 原始 ID 到脚本原始路径的恢复映射，71/71 成功 |

SHA-256 样本身份：

```text
APK dbx.npk     3f80ec7e366b522c26d626ad059b602b7b96f55c9457b145dd9e4a110ed74ab1
热更 dbx.npk    48732c4a01f8d52a0a61a34dbfbfcbeaef600a7e9690e3b6d01052ea37cb4497
热更 script.npk 8d5e4783a2451aac8e7cbbb772578d2cf423b9e2b2be1571928ea2f6a6c4a94b
libclient.so    a8c2e2f915413640eab7e9615f47787f04c36dd1bbfdec26c80fb2dfee6e60dc
```

## 8. 后续使用建议

需要服务端策划表时，可从已校验的合并 DBX 读取 `item_data`、`hero`、`player_exp`、`city_*`、`mission`、`reward_*` 等，再按业务模块筛选字段。当前证据已经足够确定数据来源和格式，无需继续猜资源文件名。

客户端表不自动等于完整原厂服务端配置：服务端专属表、活动后台参数、运营即时配置仍可能没有下发。后续加载器需要保留复合主键、bytes/Unicode、tuple 和嵌套结构，同时结合 Python 业务代码确认字段含义。此次没有修改 `main.py`、服务器、重定向脚本，也没有导出一套宣称可直接替换服务端的完整 JSON 库。

## 9. 仓库构建检查

按 `CLAUDE.md` 执行了 `dotnet build Sv.slnx`，结果为 0 警告、3 错误，均在本次未修改的服务器文件中：

- `D:\f7\Sv\Resources\ServerTables\StoreServerTables.cs:235`：`WalletCoinType` 未定义。
- `D:\f7\Sv\Resources\ServerTables\LevelChooseServerTables.cs:65`：`GameLevelCategory` 未定义。
- `D:\f7\Sv\Resources\ServerTables\LevelChooseServerTables.cs:66`：`ActivityStatus` 未定义。

没有扩展任务去修改服务器。上述构建失败与 DBX 独立校验结果分开记录，不能据此声称仓库构建通过。
