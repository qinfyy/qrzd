# -*- coding: utf-8 -*-
"""NeoX 纯数据模块快速 AST/表达式提取器 (Data Lifter)。

针对 NeoX 游戏包中包含数十万常量与超大字典的纯配置表模块（如 model_data, skill 等），
绕过 uncompyle6 的 Earley/GLR 语法推导机，直接采用确定性指令流重构 AST 并流式输出 Python 源码。
- 内存占用从数 GB 降至 < 50MB；
- 耗时从数十分钟/崩溃降至 1~5 秒；
- 遇到非纯数据指令（如函数定义、类定义、复杂控制流）时抛出 Unsupported，无缝回退至 uncompyle6。
"""

import math
from pathlib import Path
import struct
import sys

# 模块级递归深度提升
sys.setrecursionlimit(20000)

TAGGED_PREFIX = '''import sys
try:
    TD = dict
    if not sys.platform.startswith('linux'):
        from taggeddict import taggeddict as TD
except:
    TD = dict
'''


class Unsupported(Exception):
    """当模块不是纯数据表或包含未支持指令时抛出。"""
    pass


class Node:
    __slots__ = ('kind', 'items')

    def __init__(self, kind, items):
        self.kind = kind
        self.items = items


class Code27:
    __slots__ = (
        'argcount', 'nlocals', 'stacksize', 'flags', 'code', 'consts',
        'names', 'varnames', 'freevars', 'cellvars', 'filename', 'name',
        'firstlineno', 'lnotab'
    )

    def __init__(self, **kw):
        for k in self.__slots__:
            setattr(self, k, kw.get(k))


class Py27Reader:
    """轻量级 Python 2.7 marshal 反序列化器，用于高速解析 .pyc。"""

    def __init__(self, data):
        self.b = data
        self.i = 0
        self.intern = []

    def u8(self):
        v = self.b[self.i]
        self.i += 1
        return v

    def i32(self):
        v = struct.unpack_from('<i', self.b, self.i)[0]
        self.i += 4
        return v

    def obj(self, depth=0):
        if depth > 500:
            raise Unsupported("marshal 嵌套层级超过限制")
        t = chr(self.u8())
        if t in ('0', 'N'):
            return None
        if t == 'F':
            return False
        if t == 'T':
            return True
        if t == 'S':
            return StopIteration
        if t == '.':
            return Ellipsis
        if t == 'i':
            return self.i32()
        if t == 'I':
            v = struct.unpack_from('<q', self.b, self.i)[0]
            self.i += 8
            return v
        if t == 'f':
            n = self.u8()
            s = self.b[self.i:self.i + n]
            self.i += n
            return float(s)
        if t == 'g':
            v = struct.unpack_from('<d', self.b, self.i)[0]
            self.i += 8
            return v
        if t in ('s', 't'):
            n = self.i32()
            s = self.b[self.i:self.i + n]
            self.i += n
            if t == 't':
                self.intern.append(s)
            return s
        if t == 'R':
            idx = self.i32()
            return self.intern[idx]
        if t == 'u':
            n = self.i32()
            s = self.b[self.i:self.i + n].decode('utf-8', 'surrogateescape')
            self.i += n
            return s
        if t == '(':
            n = self.i32()
            return tuple(self.obj(depth + 1) for _ in range(n))
        if t == '[':
            n = self.i32()
            return [self.obj(depth + 1) for _ in range(n)]
        if t == '{':
            d = {}
            while True:
                k = self.obj(depth + 1)
                if k is None and self.b[self.i - 1:self.i] in (b'0', b'N'):
                    break
                v = self.obj(depth + 1)
                d[k] = v
            return d
        if t == 'c':
            argcount = self.i32()
            nlocals = self.i32()
            stacksize = self.i32()
            flags = self.i32()
            code = self.obj(depth + 1)
            consts = self.obj(depth + 1)
            names = self.obj(depth + 1)
            varnames = self.obj(depth + 1)
            freevars = self.obj(depth + 1)
            cellvars = self.obj(depth + 1)
            filename = self.obj(depth + 1)
            name = self.obj(depth + 1)
            firstlineno = self.i32()
            lnotab = self.obj(depth + 1)
            return Code27(
                argcount=argcount, nlocals=nlocals, stacksize=stacksize,
                flags=flags, code=code, consts=consts, names=names,
                varnames=varnames, freevars=freevars, cellvars=cellvars,
                filename=filename, name=name, firstlineno=firstlineno,
                lnotab=lnotab
            )
        if t == 'l':
            n = self.i32()
            size = abs(n)
            digits = struct.unpack_from(f'<{size}H', self.b, self.i)
            self.i += size * 2
            val = 0
            for d in reversed(digits):
                val = (val << 15) | d
            return -val if n < 0 else val
        raise Unsupported(f"未知的 marshal 标签类型: {t}")


def format_literal(val):
    """将常量值安全打包为 AST 渲染节点。"""
    if val is None:
        return Node('literal', ['None'])
    if val is True:
        return Node('literal', ['True'])
    if val is False:
        return Node('literal', ['False'])
    if val is Ellipsis:
        return Node('literal', ['...'])
    if isinstance(val, (int, float)):
        if isinstance(val, float) and (math.isnan(val) or math.isinf(val)):
            raise Unsupported('包含非有限浮点数 (NaN/Inf)')
        return Node('literal', [repr(val)])
    if isinstance(val, bytes):
        try:
            s = val.decode('utf-8')
            return Node('literal', [repr(s)])
        except UnicodeDecodeError:
            try:
                s = val.decode('gbk')
                return Node('literal', [repr(s)])
            except UnicodeDecodeError:
                return Node('literal', [repr(val)])
    if isinstance(val, str):
        return Node('literal', [repr(val)])
    if isinstance(val, tuple):
        return Node('tuple', [format_literal(x) for x in val])
    raise Unsupported(f'不支持的常量类型: {type(val)}')


def render(node, write):
    """流式输出节点代码，避免超大对象在内存中拼接字符串。"""
    k, items = node.kind, node.items
    if k in ('literal', 'name'):
        write(items[0])
    elif k in ('tuple', 'list', 'dict'):
        l, r = {'tuple': ('(', ')'), 'list': ('[', ']'), 'dict': ('{', '}')}[k]
        write(l)
        for i, item in enumerate(items):
            if i:
                write(', ')
            if k == 'dict':
                render(item.items[0], write)
                write(': ')
                render(item.items[1], write)
            else:
                render(item, write)
        if k == 'tuple' and len(items) == 1:
            write(',')
        write(r)
    elif k == 'attr':
        render(items[0], write)
        write('.' + items[1])
    elif k == 'subscript':
        render(items[0], write)
        write('[')
        render(items[1], write)
        write(']')
    elif k == 'call':
        render(items[0], write)
        write('(')
        for i, arg in enumerate(items[1:]):
            if i:
                write(', ')
            render(arg, write)
        write(')')
    elif k == 'neg':
        write('-')
        render(items[0], write)
    elif k == 'not':
        write('not ')
        render(items[0], write)
    elif k == 'invert':
        write('~')
        render(items[0], write)
    else:
        raise Unsupported(f'无法渲染节点类型: {k}')


def try_lift_data(pyc_path: Path, output_path: Path) -> int:
    """尝试以纯数据表模式反编译 .pyc 文件。

    若模块包含非数据指令或异常则抛出 Unsupported。
    若成功写入目标文件，返回输出字符数。
    """
    with open(pyc_path, 'rb') as f:
        magic = f.read(4)
        if magic != b'\x03\xf3\x0d\x0a':
            raise Unsupported('非标准 Python 2.7 pyc 魔数')
        f.read(4)  # 跳过时间戳
        raw_bytes = f.read()

    reader = Py27Reader(raw_bytes)
    co = reader.obj()

    if not isinstance(co, Code27):
        raise Unsupported('未解析出有效的 Code 对象')

    if co.name not in (b'<module>', '<module>'):
        raise Unsupported('非模块顶级代码对象')

    # 若常量列表中含有函数/类代码对象，则必须使用常规反编译器
    if any(isinstance(c, Code27) for c in co.consts):
        raise Unsupported('模块中存在嵌套函数/类代码对象')

    raw = co.code
    consts = co.consts
    names = [n.decode('latin1') if isinstance(n, bytes) else str(n) for n in co.names]

    # 检测是否包含 NeoX taggeddict 固定前缀 (76 字节)
    start = 0
    tagged = False
    if len(raw) >= 76 and raw[:4] == b'd\x00\x00d\x01\x00' and raw[6:8] == b'l\x00':
        start = 76
        tagged = True

    stack = []
    statements = []
    offset = start
    extended = 0
    returned = False

    while offset < len(raw):
        op = raw[offset]
        offset += 1
        arg = None
        if op >= 90:
            arg = raw[offset] | (raw[offset + 1] << 8) | extended
            offset += 2
        if op == 145:  # EXTENDED_ARG
            extended = arg << 16
            continue
        extended = 0

        if returned:
            raise Unsupported('返回语句之后存在多余指令')

        if op == 100:  # LOAD_CONST
            stack.append(format_literal(consts[arg]))
        elif op == 101:  # LOAD_NAME
            stack.append(Node('name', [names[arg]]))
        elif op == 102:  # BUILD_TUPLE
            items = stack[-arg:] if arg else []
            if arg:
                del stack[-arg:]
            stack.append(Node('tuple', items))
        elif op == 103:  # BUILD_LIST
            items = stack[-arg:] if arg else []
            if arg:
                del stack[-arg:]
            stack.append(Node('list', items))
        elif op == 104:  # BUILD_SET
            items = stack[-arg:] if arg else []
            if arg:
                del stack[-arg:]
            stack.append(Node('list', items))
        elif op == 105:  # BUILD_MAP
            stack.append(Node('dict', []))
        elif op == 54:  # STORE_MAP
            k, v = stack.pop(), stack.pop()
            if not stack or stack[-1].kind != 'dict':
                raise Unsupported('非法 STORE_MAP 操作')
            stack[-1].items.append(Node('pair', [k, v]))
        elif op == 106:  # LOAD_ATTR
            stack.append(Node('attr', [stack.pop(), names[arg]]))
        elif op == 25:  # BINARY_SUBSCR
            idx, base = stack.pop(), stack.pop()
            stack.append(Node('subscript', [base, idx]))
        elif op == 131:  # CALL_FUNCTION
            if arg >= 256:
                raise Unsupported('CALL_FUNCTION 含有关键字参数')
            call_args = stack[-arg:] if arg else []
            if arg:
                del stack[-arg:]
            stack.append(Node('call', [stack.pop()] + call_args))
        elif op == 90:  # STORE_NAME
            name = names[arg]
            val = stack.pop()
            if stack:
                raise Unsupported('赋值后堆栈非空')
            statements.append((name, val))
        elif op == 1:  # POP_TOP
            stack.pop()
        elif op == 2:  # ROT_TWO
            stack[-1], stack[-2] = stack[-2], stack[-1]
        elif op == 4:  # DUP_TOP
            stack.append(stack[-1])
        elif op == 9:  # NOP
            pass
        elif op == 11:  # UNARY_NEGATIVE
            stack.append(Node('neg', [stack.pop()]))
        elif op == 12:  # UNARY_NOT
            stack.append(Node('not', [stack.pop()]))
        elif op == 15:  # UNARY_INVERT
            stack.append(Node('invert', [stack.pop()]))
        elif op == 83:  # RETURN_VALUE
            if len(stack) != 1:
                raise Unsupported('非法模块返回栈状态')
            stack.pop()
            returned = True
        else:
            raise Unsupported(f'指令操作码 {op} 超出纯数据提取器支持范围')

    if not returned or stack:
        raise Unsupported('模块未正常以 RETURN_VALUE 终结')

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write('# -*- coding: utf-8 -*-\n')
        if tagged:
            f.write(TAGGED_PREFIX)
            f.write('\n')
        for name, val in statements:
            f.write(f'{name} = ')
            render(val, f.write)
            f.write('\n')

    return output_path.stat().st_size
