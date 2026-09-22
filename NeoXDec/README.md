# NeoXDec

`NeoXDec` 是针对 NeoX 引擎资产包与脚本层逆向的独立工具集，按照功能职责清晰划分为三个模块：

1. **`NpkDec`**：NPK 容器纯解包模块。负责解包 `script.npk` 和 `res/*.npk`，输出未修复的原始 NeoX 脚本裸文件（`.nxc`）或解密资源文件（PNG, KTX, CCZ等）；
2. **`PyDec`**：脚本字节码修复模块。负责将 `NpkDec` 解包出的 `.nxc` 反序列化、还原混淆 Opcode、展开融合指令 173、迭代重定位跳转并组装输出标准的 Python 2.7 `.pyc` 文件；
3. **`PyDecompiler`**：脚本反编译模块。负责将 `PyDec` 修复出的标准 `.pyc` 文件通过特化 AST 适配器批量反编译为可读的 Python 源码（`.py`）。

本项目**在 Python 3（例如 Python 3.14+ 环境）下运行**，无需安装 Python 2。

---

## 三步完整流水线演示

### 第一步：NpkDec 解包

```bash
# 解包 script.npk，输出未修复的原始 .nxc 裸脚本文件
python NpkDec/unpack.py path/to/assets/script.npk -o ./output/step1_nxc --jobs 8
```

### 第二步：PyDec 修复

```bash
# 将第一步解出的 .nxc 修复为标准的 Python 2.7 .pyc 文件
python PyDec/fix.py ./output/step1_nxc -o ./output/step2_pyc --jobs 8
```

### 第三步：PyDecompiler 反编译

```bash
# 将第二步修复好的 .pyc 反编译为 Python 源代码 (.py)
python PyDecompiler/decompile.py ./output/step2_pyc -o ./output/step3_py --jobs 8

# 若包含大型文件 (如 CEGUI)，可开启 AST 分块反编译
python PyDecompiler/decompile.py ./output/step2_pyc -o ./output/step3_py --chunked --jobs 8
```
