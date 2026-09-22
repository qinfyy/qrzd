# StaticResParser 架构

## 数据流

```text
现有 NPK 解包器的输出目录
  -> main.py 发现 DBX 包和按顺序排列的覆盖层
  -> resource_names.py 计算 StringIDLegacy 恢复逻辑名映射
  -> dbx.py 合并文件索引、校验 MD5、逐行 LZ4 + MessagePack
  -> output/<包名>/<表名>.json
  -> manifest.json / report.json
```

## 边界

- `main.py`：命令行、目录发现、覆盖层顺序、输出保护、JSON 写入、统计和错误清单。
- `dbx.py`：清单与组成文件解析、DBX 行解码、JSON 类型表达。`DbxPackage` 持有同一个逻辑包的各个覆盖层，不跨包合并 ID。
- `resource_names.py`：纯 Python Legacy 散列；不依赖原生镜像或旧清单，33,060 个真实文件名已与原生验证记录逐项匹配。
- `tests/test_parser.py`：使用临时目录构造标准 LZ4/MessagePack 输入，验证格式、覆盖、错误与文件保护。

仅依赖 `lz4`、`msgpack`。既有 `.deps` 可用于当前工作区的隔离依赖；新环境按 requirements.txt 安装。NPK 解密、脚本反编译、网络协议、服务端业务均不属于此程序职责。

导出按表逐个处理，不同时加载完整表库。JSON 文件先写同目录临时文件再原子替换；重复运行先核对上次生成文件的 SHA256，再仅清理已登记且未修改的文件。统计清单明确区分成功与失败，完整说明见 USAGE.md。
