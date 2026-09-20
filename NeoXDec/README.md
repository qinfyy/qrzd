# NeoXDec - 网易 NeoX 引擎逆向解密与反编译工具集

`NeoXDec` 是针对网易 NeoX 引擎资产包与脚本层逆向的独立工具集，按照功能职责清晰划分为三个**彼此完全独立、零依赖耦合**的模块：

1. **`NpkDec`**：NPK 容器纯解包模块。负责解包 `script.npk` 和 `res/*.npk`，输出未修复的原始 NeoX 脚本裸文件（`.nxc`）或解密资源文件（PNG, KTX, CCZ等）；
2. **`PyDec`**：脚本字节码修复模块。负责将 `NpkDec` 解包出的 `.nxc` 反序列化、还原混淆 Opcode、展开融合指令 173、迭代重定位跳转并组装输出标准的 Python 2.7 `.pyc` 文件；
3. **`PyDecompiler`**：脚本反编译模块。负责将 `PyDec` 修复出的标准 `.pyc` 文件通过特化 AST 适配器批量反编译为可读的 Python 源码（`.py`）。

本项目**在 Python 3（例如 Python 3.14+ 虚拟环境）下原生运行**，无需安装 Python 2。

---

## 目录结构

```text
NeoXDec/
├── NpkDec/                 # 1. 容器解包模块 (解包 NPK 容器，不修复字节码)
│   ├── npk.py              # NPK 容器解析 (Header, Entries, AES 索引)
│   ├── rotor.py            # Rotor 6轮流加密算法 (纯 Python)
│   ├── fastrotor.py        # Rotor 预计算加速表 (numpy)
│   ├── nxmarshal.py        # NeoX Marshal 反序列化器 (支持 surrogatepass)
│   ├── ccz.py              # CCZ 容器解密 (XXTEA 派生表 + 稀疏异或 + zlib)
│   ├── unpack.py           # NpkDec 命令行主入口 (输出 .nxc 裸文件)
│   └── README.md
│
├── PyDec/                  # 2. 脚本字节码修复模块 (混淆字节码 -> 标准 .pyc)
│   ├── nxmarshal.py        # NeoX 特化 Marshal 读写器
│   ├── nxdis.py            # Opcode 还原、融合指令 173 展开、跳转与行号表重定位
│   ├── fix.py              # PyDec 命令行主入口 (输入 .nxc，输出标准 .pyc)
│   └── README.md
│
├── PyDecompiler/           # 3. 脚本反编译模块 (标准 .pyc -> .py 源码)
│   ├── adapters/           # uncompyle6 / xdis 特化适配层
│   │   ├── control_flow.py # 控制流修复 (None返回、空分支、推导式lambda等)
│   │   ├── literals.py     # Unicode 与复数字面量修复
│   │   └── chunked.py      # 大型复杂模块 (如 CEGUI) AST 空栈边界分块反编译
│   ├── decompile.py        # PyDecompiler 命令行主入口
│   └── README.md
│
├── requirements.txt        # Python 3 运行时依赖
├── .gitignore
├── venv/                   # 项目虚拟环境
└── README.md
```

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
