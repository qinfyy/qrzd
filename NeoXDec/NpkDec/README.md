# NpkDec - NeoX NPK 容器纯解包模块

**职责**：仅负责解包与解密网易 NeoX 引擎的 NPK 容器数据，**不修改、不反混淆脚本字节码**。

1. **脚本包 (`script.npk`)**：解密 Rotor 6 轮流密码、解压缩、反转字节流，将条目解出为未修改的原始 NeoX 编译脚本裸文件（`.nxc`）。字节码的混淆指令、融合指令保留给 `PyDec` 处理。
2. **资源包 (`assets/res/*.npk`)**：解密 AES-128-ECB 索引表，提取各条目内容（LZ4 解压缩或直存），自动探测资源格式（PNG, KTX, NTRK, DDS, CCZ, MP4, RIFF, XML, JSON 等）。

---

## 使用方法 (CLI)

```bash
# 1. 解包 script.npk（解出未修复的 .nxc 裸脚本）
python NpkDec/unpack.py path/to/assets/script.npk -o ./output/nxc --jobs 8

# 2. 解包资源包 res/*.npk
python NpkDec/unpack.py path/to/assets/res -o ./output/res
```

---

## 命令行参数一览

| 参数 | 简写 | 说明 |
| :--- | :--- | :--- |
| `input` | | 输入的 `.npk` 文件路径或包含多个 `.npk` 的目录 |
| `--out` | `-o` | 目标解包输出目录（必填） |
| `--type` | | 指定类型：`auto`（默认自动检测）、`script`（强制按脚本解）、`res`（强制按资源解） |
| `--jobs` | `-j` | 脚本解包并发进程数（默认 4） |
| `--limit` | | 限制解包前 N 个条目（便于调试排查） |
| `--by-id` | | 强制按条目十六进制 ID 命名输出文件（如 `0a0d60dc.nxc`） |
| `--decode-ccz`| | 遇到 CCZ 容器时自动尝试解压为原始二进制 |
| `--ccz-keys` | | 指定 CCZp 解密所用的 4 个无符号整数密钥 |
