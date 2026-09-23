# QRZD 配置资源分析

本文所称“游戏 Python”是从游戏 Python 2.7 字节码恢复出的代码，文件含 uncompyle6 标头，不是原开发团队的工程源码。可直接核对的数据定义、调用接口与配置项作为证据；存在反编译伪影的控制流不视为已完整恢复。

## 1. 结论

**外置策划配置主体在 `res/dbx.npk`。包内是带逻辑表名的 DBX 数据库，不是 Excel 文件，也不是一堆可以直接按扩展名识别的 JSON/CSV。**

同类数据还有 Python 模块发布形态：`com.data.cdata.<表名>.data`。DBX 导入钩子可以用 `CollectionProxy` 替代模块中的 `data`，让业务代码继续使用 `data[key]`、`data.get(key)` 等接口。

当前静态恢复链路为：

```text
APK res/dbx.npk + Documents/res/dbx.npk
  -> 既有 NpkDec 解包器
  -> input/apk/dbx + input/hotfix/dbx
  -> dbx_md5.json 取得表名
  -> StringIDLegacy(表名 + 原始扩展名) 定位条目
  -> 按文件取热更，缺失时回退 APK
  -> .dbxh 定位行 -> .dbx 截取行 -> 可选 .dbxcd + LZ4
  -> MessagePack 解码
  -> output/dbx/<表名>.json
```

本轮重新核对了现存清单、代表性输出、源码和统计，未重新运行全量导出，未启动游戏或执行 Frida/IDA 动态验证。

## 2. 物理位置与清单

### 2.1 原始包与当前输入

| 用途 | 位置 |
| --- | --- |
| APK 基础配置包 | `assets/res/dbx.npk` |
| 热更配置包 | `Documents/res/dbx.npk` |
| 热更校验清单 | `Documents/cache/checks/dbx.check` |
| 热更脚本包 | `Documents/script.npk` |

### 2.2 两类清单不是同一个东西

`dbx/dbx_md5.json` 是表名到 `.dbx` 正文 MD5 的 JSON 映射。它本身确实是 JSON；不能据此认定所有表正文也是 JSON。

`dbx.check` 是热更文件元数据。游戏 `/py/patch/patch_file.py:24` 先用 zlib 解压，再逐行解析以下八项：

```text
fid  fmd5  csize  chash  usize  uhash  ver  offset
```

这里包含条目 ID、MD5、压缩与解压长度/校验值、版本和偏移。它用于文件更新与校验，不是道具、角色、奖励等表的业务字段。

### 2.3 热更覆盖粒度

APK neox.xml 为 `res` 配置了以下查找顺序：

```text
Documents/res 散文件
  -> Documents/res NPK
  -> APK res 散文件
  -> APK res NPK
```

当前导出器输入对应其中的两层 NPK 解包结果。依据现存 manifest.json 重新汇总：

| 组成部分 | 取自热更 | 回退 APK | 无此可选文件 |
| --- | ---: | ---: | ---: |
| `.dbx` 正文 | 2,160 | 8,860 | 0 |
| `.dbxh` 索引 | 2,155 | 8,865 | 0 |
| `.dbxcd` 字典 | 2,100 | 138 | 8,782 |

**必须逐文件覆盖，不能整表选一个来源。** 例如 `chunjiejiyifanbei` 的正文来自 `input\hotfix\dbx\6c996316.bin`，索引来自 `input\apk\dbx\da17e567.bin`，且没有字典。相对于本项目的完整路径以本节输入目录为准。

两份 `4b1354f6.json` 清单的表名集合相同，36 张表的正文 MD5 不同。例如：

```text
item_data APK MD5    = 2b483a138a5fe471fae7cb76b64e392e
item_data 热更 MD5   = 78b063e1ec79be563f1daa710211c4cc
```

“文件在热更包里”不等于“相对 APK 已改变”，例如 `hero` 的正文 MD5 相同。实际输出采用最高优先级清单决定有效表集合，再逐文件解析来源。

## 3. 逻辑资源名与散列文件名

### 3.1 从 Python 模块到表名

游戏 `/lib/dbx/dbx/config.py:18` 定义：

```python
SERIALIZE_ALGORITHM = 'msgpack'
DATA_IMPORT_PREFIX = 'com.data.cdata.'
DBX_CONTENT_PATH = 'res/dbx/'
DBX_MD5_RECORD = 'dbx_md5.json'
MODEL_DATA_IMPORT = 'combat.base.data.model_data'
```

`/lib/dbx/dbx_import_hook.py:139` 用 `C_file.get_res_file('dbx/dbx_md5.json', '')` 读取清单。导入时取模块名最后一段，如 `com.data.cdata.item_data` 对应 `item_data`，并检查该名称是否在清单中。

因此逻辑名称不是靠看到十六进制文件名后猜测，而是来自游戏自身的清单和模块接口。

### 3.2 条目 ID 的含义

本样本使用 NeoX `StringIDLegacy`，输入是**大小写敏感、不含目录前缀的包内文件名**：

| 散列输入 | 条目 ID |
| --- | --- |
| `dbx_md5.json` | `4b1354f6` |
| `item_data.dbx` | `e8687560` |
| `item_data.dbxh` | `15a75cf8` |
| `item_data.dbxcd` | `71ca2652` |

`dbx/item_data.dbx` 是 C_file 层的逻辑资源路径；计算本包条目 ID 时只使用 `item_data.dbx`。清单给出候选名称后正向计算 ID，即可恢复文件身份，不是在数学上反解散列。

现有 resource_names.py 已独立实现该算法，按 UTF-8、小端 32 位字、固定初始状态和进位折叠运算生成 8 位十六进制 ID。

既有原生逆向记录中的定位点如下，本轮作为历史证据沿用，没有重新执行 IDA/Unicorn：

| `libclient.so` RVA | 作用 |
| --- | --- |
| `0x2589C48` | `neox::StringIDLegacy` |
| `0x195F310` | 对包头 `header+8` 的 12 字节组合 `0,0,2` 作兼容判断，选择 Legacy |
| `0x258A310` | `GetStringIDFunc`：0 为 Legacy，1 为 FNV，2 为 Murmur |

不能看到包头末项是 `2` 就跳过兼容分支直接使用 Murmur。地址只适用于已分析的样本，不是跨版本通用偏移。历史过程见 [Planning_Config_Report.md](./Planning_Config_Report.md)，其中旧临时目录和旧运行环境不作为当前复现前提。

### 3.3 三种名称不要混淆

| 名称/校验值 | 含义 |
| --- | --- |
| `e8687560` | `item_data.dbx` 的名称散列，定位 NPK 条目 |
| `78b063e1ec79be563f1daa710211c4cc` | 当前 `item_data.dbx` 正文内容的 MD5 |
| `item_data.json` | 静态解析器恢复逻辑名后生成的可读输出文件 |

解包器的 `.bin`、`.json` 是 [format_hint](../NeoXDec/NpkDec/unpack.py:158) 根据文件头猜测的后缀，不是包里保存的原始扩展名。例如 `city_upgrade.dbx` 被输出为 `c87546d9.json`，但它仍是 DBX 压缩正文，不能直接当 JSON 解析。

## 4. Python 加载链

### 4.1 导入钩子与集合代理

仅描述满足 DBX 开启条件时的分支：

```text
业务 import com.data.cdata.<表名>
  -> dbx_import_hook.import_hook
  -> 检查 USE_DBX、模块前缀和清单
  -> 创建/取得模块对象
  -> m.data = CollectionProxy(表名)
  -> 业务调用 m.data[key] 或 m.data.get(key)
  -> DatabaseManagement.read(表名, key)
  -> 缓存/内存补丁，或 StorageEngineLine.read
  -> C_file -> .dbxh/.dbxcd/.dbx -> 行对象
```

主要位置：`/lib/dbx/dbx_import_hook.py:14`、`/lib/dbx/dbx_import_hook.py:124`、`/lib/dbx/dbx_import_hook.py:193`、`/lib/dbx/dbx/collection_proxy.py:33`

代理还提供 `get`、`keys`、`items`、`values`、迭代和长度查询。`__getitem__` 在行不存在时抛出 `KeyError`，`get` 返回调用者指定的默认值。未走 DBX 的分支则访问原 Python 模块的 `data`。

### 4.2 C_file 不是普通文件 open

`/lib/dbx/dbx/file_operator.py:20` 去掉 `res/` 前缀，把 `res/dbx/item_data.dbx` 转为 `dbx/item_data.dbx`：

```text
exists     -> C_file.find_res_file(path, '')
read()     -> C_file.get_res_file(path, '')
seek(n)    -> 保存逻辑偏移
read(size) -> C_file.read_res_file(path, '', offset, size)
```

Python 侧请求的是资源文件的内容或某个范围，底层 NPK 解密、容器级解压由资源系统负责。因此 DBX 行偏移是**解包后的 `.dbx` 内容偏移**，不是整个 NPK 文件的磁盘偏移。

### 4.3 缓存与内存热修

`/lib/dbx/dbx/database_management.py:16` 持有存储引擎、缓存，以及 `patch`、`patchInfo`、`hotfix`、`hotfixInfo`。单行读取可优先命中内存热修或既存补丁；未命中时才需要缓存/存储路径。

`/lib/dbx/dbx/cache_engine.py:10` 按表和主键缓存结果，并区分单行缓存与已读取的整表。当前 `CACHE_NUM=None` 分支使用普通字典，关闭基于数量的淘汰操作。`/lib/dbx/dbx/database_management.py:168` 清理缓存并维护改动；增删改接口受 `inHotfix` 限制。

恢复出的 `database_management.py` 存在如 `doc, found = found or self.se.read(...)` 这样的异常表达式，部分分支也有变量初始化疑点。这里确认组件、接口与显式优先分支，不将整份反编译结果视为可直接执行的原代码。

**磁盘热更与内存补丁是两层机制。** 本程序只合并输入文件，不读取正在运行的 Python 对象，也不会包含游戏后来在内存中施加的增删改。

### 4.4 不能仅凭常量宣称运行实例启用 DBX

- `/lib/dbx/dbx/config.py:23` 静态值为 `USE_DBX = False`。
- `/py/com/const.py:313` 定义 `DEBUG_DBX = 1`。
- `/patch/patch.py:598` 在 `DEBUG_DBX` 条件下导入钩子并设置 `gg.use_dbx = True`，失败时关闭相关开关。
- 钩子内部仍单独检查 `config.USE_DBX`，上述成功分支本身没有把该值设置为真。

因此可以确认两种数据形态和加载机制，不能据此确认某次游戏运行实际选择了哪一种。确认运行态需要检查相关开关、已导入模块 `data` 的实际类型及读取调用；本轮没有进行这项动态验证。

## 5. 容器、压缩与序列化

### 5.1 NPK 外层复用既有工具

使用 [NeoXDec/NpkDec/unpack.py](../NeoXDec/NpkDec/unpack.py:184)，不另造 NPK 解包器。资源包分支通过 [npk.py](../NeoXDec/NpkDec/npk.py:87) 读取条目索引；索引加密标志开启时执行 AES-128-ECB 解密，密钥使用既有工具的 `DEFAULT_AES_INDEX_KEY`。

随后按资源条目标志处理：`flags=0` 直接取内容，`flags=2` 按条目的原始长度进行 LZ4 block 解压。这里只描述 `--type res` 分支，不能把脚本包分支的处理逻辑套到 DBX 资源包上。

**NPK 条目级 LZ4 与 DBX 行级 LZ4 是两个独立层次。** NPK 解包成功只得到表组成文件，仍须按行索引解开内部数据。当前 `StaticResParser` 的职责从解包结果开始，不执行这一外层 AES/NPK 处理。

### 5.2 一张表的文件布局

游戏 `/lib/dbx/dbx/config.py` 明确配置 `lz4 + line + npk`，序列化算法为 `msgpack`。

| 逻辑文件 | 内容 | 必需性 |
| --- | --- | --- |
| `dbx/<表名>.dbxh` | MessagePack map：主键 -> `(offset, length)` | 必需 |
| `dbx/<表名>.dbxcd` | LZ4 原始压缩字典，不是字段名词典 | 可选 |
| `dbx/<表名>.dbx` | 多个独立行压缩块的拼接 | 必需，空表可为空 |

`/lib/dbx/dbx/storage_engine.py` 解析并缓存索引；`__getCD` 读取可选字典。单行过程在 `/lib/dbx/dbx/storage_engine.py:117`：

```text
offset, length = header[key]
block = body[offset : offset + length]
raw = LZ4.block.decompress(block, dict=可选字典)
row = MessagePack.loads(raw)
```

这是流程等价说明，不是新增脚本。压缩实现见 `/lib/dbx/dbx/compress_lib.py`，解码实现见 `/lib/dbx/dbx/serialize_lib.py`。当前解析器还读取行块开头的小端 32 位解压长度，并限制单行最大 64 MiB，见 [dbx.py](./dbx.py:135)；该限制是导出器的保护，不是已确认的游戏协议上限。

不能把整份 `.dbx` 当成单个 MessagePack 对象，也不能忽略 `.dbxh` 后对整份正文只做一次 LZ4 解压。整表读取也是先取得索引，再逐行切片解码。查询键集合和长度则可以只用索引，不必展开所有行。

## 6. 原始数据结构与业务证据

### 6.1 是对象还是 Excel 表

从现存资源可确定的是：**存储层为行索引与独立序列化行，业务层通常是“主键 -> 行字段字典”的表，行内允许嵌套结构。** “由 Python 对象承载”与“逻辑上是表”并不矛盾。

无法从这些运行资源证明策划最初使用了 Excel、内部编辑器、数据库还是脚本生成工具。没有恢复出 `.xlsx` 工作簿、单元格公式、格式、注释或编辑历史。

游戏 `/com/data/cdata/player_exp.py` 直接定义 `data`，行值由 `TD({...})` 包装；文件开头可见 `TD=dict` 和可选 `taggeddict`。以下为从其数据定义节选的逻辑表示：

```python
data = {
    1: {'lv': 1, 'lv_exp': 1300, 'all_exp': 1300},
    2: {'lv': 2, 'lv_exp': 1700, 'all_exp': 3000}
}
```

这张表适合整理为平面 CSV。`/com/data/cdata/city_upgrade.py` 则用 `(1, 1)` 等复合主键；`/com/data/cdata/hero.py` 的行包含 `attrs` 嵌套字典和 `a_skills` 序列，不能统一按“每个字段都是一个标量单元格”处理。

### 6.2 代表表

下表行数和 ID 来自当前导出清单；完整行内容可在对应输出文件查看。

| 表 | 行数 | 正文 / 索引 / 字典 ID | 已见内容 |
| --- | ---: | --- | --- |
| `item_data` | 12,434 | `e8687560` / `15a75cf8` / `71ca2652` | 类型、叠加上限、使用方式、角色关联 |
| `hero` | 140 | `310e68e6` / `5fa283b8` / `fdd08b16` | 基础属性、成长、技能序列、建设与巡查数值 |
| `player_exp` | 200 | `5cfc781c` / `3ca55dac` / `b711d12d` | 等级、升级经验、累计经验 |
| `city_building` | 47 | `c637cf8e` / `86d04d5b` / `8e293328` | 建筑数值、说明和条件 |
| `city_development` | 72 | `33e86aaf` / `86e39494` / 无 | 区域与开发条件 |
| `city_upgrade` | 45 | `c87546d9` / `6eeb1a2a` / `5fdd849f` | 行动力、疲劳、金币消耗与产出 |
| `mission` | 2,326 | `e5fcaa8b` / `5e6bd2fa` / `11062d9b` | 关卡、怪物、事件、队伍限制 |
| `reward_common` | 31,907 | `3a4075bc` / `93f64a3a` / `19407115` | 道具、对应数量、奖池等 |
| `reward_random` | 22,214 | `0eb589da` / `ae344d82` / `0b95cc61` | 奖励权重序列和展示项 |

### 6.3 字段如何被玩法使用

`/com/utils/CityBuildings.py` 从实例信息取得 `self.id` 后执行：

```python
self._developVal = CITY_BUILDING_DATA.data.get(self.id, {}).get('dev_val', 0)
self._forceVal = CITY_BUILDING_DATA.data.get(self.id, {}).get('force_val', 0)
self._researchVal = CITY_BUILDING_DATA.data.get(self.id, {}).get('research_val', 0)
```

同文件 `/com/utils/CityBuildings.py` 直接读取 `desc`、`tips_desc`。这把城市表主键、字段名与建筑对象的实际初始化对应了起来。

`/com/utils/helpers.py` 读取 `reward_common` 的 `items` 和 `itemsNum`，检查两者等长，随后按相同下标配对为 `(道具 ID, 数量)`。`/com/utils/helpers.py` 从 `reward_random[groupId]['weights']` 抽取奖励 ID，再交给普通奖励逻辑。

因此导出必须保留序列顺序和嵌套配对关系；不能分别排序道具与数量，也不能把权重序列误解释为一个独立字符串。上述函数同时包含其他分支，本文没有把它们简化为完整原厂奖励系统实现。

## 7. MessagePack 与 JSON/CSV 的对应边界

### 7.1 不是天然一一对应

游戏调用 `msgpack.loads(..., use_list=False, encoding='utf-8')`。**MessagePack 自身有 array，没有独立的 tuple 类型；Python tuple 是 `use_list=False` 的解码结果。** 复合主键必须维持其组成、顺序和可区分性，不能无差别转成字符串。

普通 JSON 对象的键是字符串，不能原生表达所有 MessagePack map 键；二进制、扩展值、非有限浮点数也需要额外约定。同一逻辑值还可能有不同整数宽度、浮点宽度和压缩结果，因此“读取含义相同”不等于“二进制逐字节相同”。

### 7.2 当前 JSON 中哪些是新增结构

[dbx.py](./dbx.py:149) 将每张表导出为以下形式，例子只保留 `player_exp` 第一行：

```json
{
  "resource": "dbx/player_exp.dbx",
  "rows": [
    {"key": 1, "value": {"all_exp": 1300, "lv": 1, "lv_exp": 1300}}
  ]
}
```

`resource`、`rows`、`key`、`value` 是导出器新增外壳，不是游戏 MessagePack 行里的固定字段。原始键来自 `.dbxh`，原始行值来自 `.dbx` 解压结果；`lv`、`lv_exp`、`all_exp` 才是这个例子的实际字段。

[json_value](./dbx.py:54) 的约定如下。这些是转换器支持的能力，不表示本批表都出现过每种类型。

| 解码值 | 当前 JSON 表示 |
| --- | --- |
| 整数主键 | `key` 中的 JSON 整数，不改为字符串键 |
| tuple / 按 tuple 解出的 array | `{"$tuple": [...]}` |
| 非字符串键映射，或与标签冲突的映射 | `{"$map": [[键, 值], ...]}` |
| 不能按 UTF-8 解码的 bytes | `{"$binary": "十六进制"}` |
| MessagePack ExtType | `{"$ext": {"code": 类型码, "hex": "十六进制"}}` |
| NaN / Infinity 等非有限浮点 | `{"$float": "文本值"}` |
| 可按 UTF-8 解码的 bytes | 直接转成 JSON 字符串 |

`$tuple/$map/$binary/$ext/$float` 同样不是游戏的原始字段。这些标签可供约定一致的读取器恢复相应逻辑结构，但**当前实现会合并“UTF-8 有效的 bytes”和字符串的表示，不能宣称对任意 MessagePack 类型完全无损**。当前也没有 JSON 回编 DBX/NPK 功能，未验证逐字节往返。

### 7.3 何时选 CSV，何时选 JSON

- 固定字段、标量行、明确主键的表，如 `player_exp`，适合 CSV；保留主键列和字段类型约定即可。
- 有嵌套对象、变长序列或复杂映射的表，如 `hero`、`reward_random`，JSON 更直接。
- 复合主键不意味着绝对不能用 CSV；可在确认每个分量的业务含义后拆成多个主键列。
- CSV 也能在单元格里装 JSON，或拆成多张关联表，但那属于另定导出模式，不再是无需约定的平面表。
- YAML/XML 也可以承载这些数据，但同样需要键类型、二进制、序列等约定，不能自动恢复 Excel 原貌。

**当前程序统一输出 JSON，没有实现按表自动选择 CSV、YAML 或 XML。** 本文给出格式判断依据，不把建议写成已经交付的功能。

## 8. 覆盖范围与容易混淆的资源

本轮重新比较名称集合：`\com\data\cdata` 有 11,021 个 `.py`，其中 11,019 个名称与 DBX 清单相交；Python 额外有 `__init__`、`state_mutex`，DBX 额外有 `model_data`，后者由配置映射到 `combat.base.data.model_data`。

`/com/data/cdata/state_mutex.py` 自身含数据，却不在 DBX 清单中。故 **11,020 张 DBX 全部导出，不等于全部 Python 常量和全部客户端配置都已导出**。脚本里的 `/com/design_const.py` 等仍是另一类数据来源。

以下排除结论沿用此前研究记录，不是本轮对其他资源包重新全量扫描：

| 对象 | 已有观察与边界 |
| --- | --- |
| `model_dbx.npk` | 已见 501 个模型路径键，内容为挂点、变换矩阵、动画事件等模型相关元数据。与主 `dbx.npk` 中的 `model_data` 不是同一个对象；当前输入和输出不含该包。 |
| `csv.npk` | 此前解出含 1,260 条记录的 JSON，样本为作者与版本 `a/v` 元数据。不能仅凭包名证明这是玩法 CSV，更不能证明上游一定是 Excel。 |
| `hero.npk`、`model*.npk`、`cocos2.npk` | 不是本报告寻找通用策划表的主路径，未据此宣称已经排除其中每个文件。 |
| `script.npk` | Python 代码及数据模块的发布载体，不能因为有 DBX 就把脚本内数据视为冗余并丢弃。 |

客户端下发配置还不等于完整原厂服务端配置。服务端专属表、后台活动参数、即时运营配置及运行时补丁是否齐全，当前材料不能证明。

## 9. 当前实现与验证边界

当前运行入口为 `python D:\f7\StaticResParser\main.py`，默认输入输出都相对于程序所在目录，不是任意 shell 当前目录。输入必须是**既有解包器的输出**，不接受原始 `.npk` 作为表文件。自动层名是 `apk`、`hotfix`，不是旧研究目录名。

现有代码检查正文 MD5、必需文件、索引形状和边界、解压大小，并逐行报告解码错误。没有字典不必然是失败；空表也不能等同于解析失败。它不解析 `dbx.check`，不执行 NPK 容器层校验，也不根据 `.check` 另行校验全部索引/字典。

当前报告 `completed=true`、`failed_tables=0`、`unknown_entry_ids=[]`、`skipped=[]`。这说明现存导出记录在输入范围内成功，不证明任意其他版本、任意 MessagePack 输入或运行时内存数据都受支持。生成程序最后修改时间晚于现存输出，本轮不把历史导出结果冒充为对最新文件重新执行的测试。

本轮仅新增本分析文档，未修改解析器、解包器、游戏脚本、服务器、重定向脚本或现存输出。没有恢复已删除的旧分析目录，也未使用旧临时文件作为运行依赖。
