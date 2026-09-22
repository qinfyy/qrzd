# 静态策划表导出

## 运行

在 `D:\f7\StaticResParser` 运行：

```powershell
python -m pip install -r requirements.txt
python main.py
```

默认输入、输出都相对于 `main.py` 所在目录，不受当前终端工作目录影响。程序只读取已有 NPK 解包结果，不解包 NPK，不需要 IDA、Frida、游戏进程、libclient.so 或历史研究清单。

## 输入

保持不同资源包的目录边界，不要把多个 NPK 解出的同名 ID 文件混到一起。

```text
input/
  apk/
    dbx/
      4b1354f6.json
      e8687560.bin
      15a75cf8.bin
      71ca2652.bin
      ...
    model_dbx/            可选，独立处理，不混入 dbx
  hotupdate/
    dbx/
      4b1354f6.json
      ...
```

自动先读 `apk`，再应用 `hotupdate`。同一逻辑文件优先取后者；缺失的正文、索引或字典分别回退，不能以整张表或整个包为单位替换。

也支持 `input/dbx/...`，或直接把单个 DBX 包的解包文件放到 `input`。`manifest.tsv` 非必需；文件扩展名只是解包器猜测，`.json` 后缀的文件仍可能是压缩正文，程序以逻辑 ID 决定解析方式。已恢复的 `表名.dbx/.dbxh/.dbxcd` 也可直接读取。

自定义目录与额外补丁层：

```powershell
python main.py --input D:\f7\StaticResParser\input\apk --overlay D:\f7\StaticResParser\input\hotupdate --output D:\f7\StaticResParser\output
python main.py --detailed-log
```

`--overlay` 可重复，后指定者优先。补丁层可以只有更新文件、没有重复的清单，但基础层与覆盖层合起来必须有 `dbx_md5.json` 或 `4b1354f6.*`。最新清单决定有效表集合。

## 输出

```text
output/
  dbx/
    dbx_md5.json
    item_data.json
    hero.json
    player_exp.json
    city_upgrade.json
    ...
  model_dbx/              输入包含此包时才产生，是模型元数据，不是主策划库
    dbx_md5.json
    model_data.json
  manifest.json
  report.json
```

每张表一个 UTF-8 JSON，保留原始逻辑资源名：

```json
{
  "resource": "dbx/player_exp.dbx",
  "rows": [
    {"key": 1, "value": {"lv": 1, "lv_exp": 1300, "all_exp": 1300}}
  ]
}
```

使用行数组而非 JSON 对象存放整张表，避免 JSON 将整数主键强制变为字符串。复合主键 `(1, 2)` 写为 `{"$tuple": [1, 2]}`。

特殊值约定：

| JSON 形式 | 原始含义 |
|---|---|
| `{"$tuple": [...]}` | tuple，包括复合主键和数组值 |
| `{"$map": [[key, value], ...]}` | 含非字符串键、键转换冲突或保留标签的字典 |
| `{"$binary": "ff00..."}` | 无法按 UTF-8 解码的原始 bytes，十六进制 |
| `{"$ext": {"code": 2, "hex": "..."}}` | MessagePack 扩展类型 |
| `{"$float": "nan"}` | 非有限浮点，其他值为 `inf`、`-inf` |

正常 UTF-8 bytes 转为可读文本。上述标签同名的原始字典会用 `$map` 包装，避免和特殊类型混淆。

`manifest.json` 记录每张表的逻辑名、正文 MD5、各组成文件的实际来源、导出行数与错误，还记录生成文件的 SHA256。`report.json` 提供总体统计和未识别资源目录。

## 错误与重复运行

- 校验正文 MD5、索引范围、LZ4 字典解压和 MessagePack 格式；单行声明解压大小上限为 64 MiB。
- 某张表失败会继续处理其他表，写入清单并返回非零退出码；不会用旧版同名 JSON 冒充成功结果。
- 重复运行只清理上次清单中、SHA256 未被改动的生成文件；不递归删除输出目录，也不删除额外放入的笔记等文件。
- 手动修改过的生成文件会被保留，并停止运行。此时可用 `--output` 指向新的空目录。
- 输入和输出不能重叠；拒绝路径穿越和输出路径中的符号链接/目录联接。
- 不含 DBX 清单、无法确定逻辑名的资源只记为跳过，不臆造文件名。当前 `csv` 目录属于这种情况；Python 脚本和美术文件也不由此程序反编译或转码。

## 验证

```powershell
python -B -m unittest discover -s tests -v
```

真实输入已导出主 `dbx` 的 11,020 张表、458,631 行，以及独立 `model_dbx` 的 1 张表、501 行。主库中有 4,492 张空表；空表正常导出为空 `rows`，不当作失败。
