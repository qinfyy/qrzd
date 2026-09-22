# PyDec

将 `NpkDec` 解包出的未修改 NeoX 编译脚本裸文件（`.nxc`）反混淆并重新组装为标准 CPython 2.7 `.pyc` 文件。

---

## 修复流程

1. **反序列化 NeoX Marshal**：解析裸 CodeObject 结构树（支持带有代理码点的 `surrogatepass` Unicode）。
2. **Opcode 逆向映射**：将 NeoX 加密洗牌后的 Opcode 还原为 CPython 2.7 官方 Opcode。
3. **展开专有融合指令 173**：将 `0xAD`（`FUSED_LOAD_FAST_CONST`）拆解展开为标准的 `LOAD_FAST 0` + `LOAD_CONST`。
4. **多轮迭代收敛重定位**：指令膨胀后，重新计算所有绝对跳转、相对跳转目标，并重映射行号表 `lnotab` 直至收敛。
5. **组装标准 `.pyc`**：补齐 Python 2.7 Magic (`0x0AF303`)、时间戳并写出标准格式文件。

---

## 使用方法 (CLI)

```bash
# 1. 批量修复 NpkDec 解包出的 .nxc 目录，生成标准 .pyc
python PyDec/fix.py ./output_nxc -o ./output_pyc --jobs 8

# 2. 修复单个文件
python PyDec/fix.py ./output_nxc/logic/game_logic.nxc -o ./output_pyc
```

---

## 命令行参数一览

| 参数 | 简写 | 说明 |
| :--- | :--- | :--- |
| `input` | | 输入的 `.nxc` 文件路径或包含 `.nxc` 的目录 |
| `--out` | `-o` | 修复后的 `.pyc` 目标输出目录（必填） |
| `--jobs` | `-j` | 并发工作进程数（默认 4） |
| `--limit` | | 限制处理前 N 个文件（便于调试） |
| `--by-id` | | 按输入文件名命名，不提取内部 `co.filename` |
