# PyDecompiler

将 `PyDec` 修复出的标准 Python 2.7 字节码（`.pyc`）反编译为可读的 Python 源代码（`.py`）。

在 Python 3 环境下原生运行，基于 `uncompyle6 3.9.3` 与 `xdis 6.1.7` 构建，并内置了针对 NeoX 引擎特有语法的控制流与 AST 适配层。

---

## 特性

1. **NeoX 专有控制流修复**：
   - 修复顶层模块 None 返回；
   - 修复空 then 分支与三元跳转；
   - 修复列表推导 lambda 循环返回；
   - 修复循环出口 latch 与 continue 误标；
   - 修复嵌套循环条件合并。
2. **支持超大模块分块反编译 (`--chunked`)**：
   - 针对如 CEGUI 等数万行的大型模块，在已证明的空栈线性语句边界自动分块构建 AST，防止解析器栈溢出或耗尽内存。
3. **并发加速与断点续跑 (`--resume`)**：
   - 多线程并发处理，支持跳过已成功反编译的文件。

---

## 使用方法 (CLI)

```bash
# 1. 批量反编译 PyDec 修复出的 pyc 目录
python PyDecompiler/decompile.py ./output_pyc -o ./output_py --jobs 8

# 2. 启用大型模块 AST 分块反编译 (--chunked)
python PyDecompiler/decompile.py ./output_pyc -o ./output_py --chunked --jobs 8

# 3. 断点续跑 (--resume)
python PyDecompiler/decompile.py ./output_pyc -o ./output_py --resume --jobs 8

# 4. 反编译单个 .pyc 文件
python PyDecompiler/decompile.py ./output_pyc/logic/game_logic.pyc -o ./output_py
```

---

## 命令行参数一览

| 参数 | 简写 | 说明 |
| :--- | :--- | :--- |
| `input` | | 输入的 `.pyc` 文件路径或包含 `.pyc` 树的根目录 |
| `--out` | `-o` | 输出 `.py` 源码的目标目录（必填） |
| `--jobs` | `-j` | 并发工作线程数（默认 4） |
| `--chunked`| | 启用 AST 安全语句分块反编译（针对超大型复杂文件） |
| `--resume` | | 跳过已反编译成功的文件，支持断点续跑 |
| `--limit` | | 限制反编译前 N 个文件（便于调试） |
