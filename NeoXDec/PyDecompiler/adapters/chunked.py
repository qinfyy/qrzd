"""针对超大型 Python 2.7 顶层模块（如 CEGUI）的安全空栈语句 AST 分块反编译补丁。"""

import argparse
from functools import wraps
import hashlib
import io
import json
from pathlib import Path
import time

from xdis.opcodes import opcode_27 as opc

try:
    from .control_flow import install as control_flow_install
    from .literals import install as literal_install
except ImportError:
    from control_flow import install as control_flow_install
    from literals import install as literal_install

_installed = False
_events = []


def instructions(code):
    raw = code.co_code
    pos = 0
    while pos < len(raw):
        start = pos
        op = raw[pos]
        pos += 1
        arg = None
        if op >= opc.HAVE_ARGUMENT:
            arg = raw[pos] | raw[pos + 1] << 8
            pos += 2
        if op == opc.EXTENDED_ARG:
            raise ValueError("分块反编译不支持扩展参数 EXTENDED_ARG")
        yield start, op, arg, pos


def stack_io(op, arg):
    name = opc.opname[op]
    if name.startswith("LOAD_"):
        return (1, 1) if name == "LOAD_ATTR" else (0, 1)
    if name in ("STORE_NAME", "STORE_GLOBAL", "STORE_FAST", "STORE_DEREF", "POP_TOP"):
        return 1, 0
    if name in ("DELETE_NAME", "DELETE_GLOBAL", "DELETE_FAST", "NOP"):
        return 0, 0
    if name == "STORE_ATTR":
        return 2, 0
    if name == "STORE_SUBSCR":
        return 3, 0
    if name == "DELETE_ATTR":
        return 1, 0
    if name == "DELETE_SUBSCR":
        return 2, 0
    if name == "IMPORT_NAME":
        return 2, 1
    if name == "IMPORT_FROM":
        return 0, 1
    if name == "IMPORT_STAR":
        return 1, 0
    if name == "BUILD_CLASS":
        return 3, 1
    if name in ("BUILD_TUPLE", "BUILD_LIST", "BUILD_SET"):
        return arg, 1
    if name == "BUILD_MAP":
        return 0, 1
    if name == "STORE_MAP":
        return 3, 1
    if name == "UNPACK_SEQUENCE":
        return 1, arg
    if name == "MAKE_FUNCTION":
        return arg + 1, 1
    if name == "MAKE_CLOSURE":
        return arg + 2, 1
    if name.startswith("CALL_FUNCTION"):
        extra = {"CALL_FUNCTION": 0, "CALL_FUNCTION_VAR": 1,
                 "CALL_FUNCTION_KW": 1, "CALL_FUNCTION_VAR_KW": 2}[name]
        return 1 + (arg & 255) + 2 * (arg >> 8) + extra, 1
    if name.startswith(("BINARY_", "INPLACE_")) or name == "COMPARE_OP":
        return 2, 1
    if name.startswith("UNARY_") or name == "GET_ITER":
        return 1, 1
    if name == "DUP_TOP":
        return 1, 2
    if name == "DUP_TOPX":
        return arg, arg * 2
    if name in ("ROT_TWO", "ROT_THREE", "ROT_FOUR"):
        count = {"ROT_TWO": 2, "ROT_THREE": 3, "ROT_FOUR": 4}[name]
        return count, count
    raise ValueError("分块反编译不支持的线性操作码: " + name)


def module_cuts(code, target_bytes=1200):
    if code.co_name != "<module>" or code.co_argcount or code.co_freevars:
        return [0, len(code.co_code)]
    ops = list(instructions(code))
    if (len(ops) < 2 or ops[-1][1] != opc.opmap["RETURN_VALUE"]
            or ops[-2][1] != opc.opmap["LOAD_CONST"]
            or code.co_consts[ops[-2][2]] is not None):
        raise ValueError("模块结尾必须是 LOAD_CONST None; RETURN_VALUE")
    end = ops[-2][0]
    offsets = {off for off, _, _, _ in ops} | {len(code.co_code)}
    prefix = 0
    for off, op, arg, nxt in ops[:-2]:
        if op in opc.hasjabs or op in opc.hasjrel:
            target = arg if op in opc.hasjabs else nxt + arg
            if target not in offsets or target > end:
                raise ValueError("不支持的控制流跳转目标")
            prefix = max(prefix, nxt, target)
        elif opc.opname[op] in ("END_FINALLY", "POP_BLOCK"):
            prefix = max(prefix, nxt)
        elif opc.opname[op] in ("RETURN_VALUE", "RAISE_VARARGS", "BREAK_LOOP", "YIELD_VALUE"):
            raise ValueError("不支持非终结控制流转移")
    if prefix not in offsets:
        raise ValueError("前缀未对齐到指令边界")
    safe = [prefix] if prefix else [0]
    depth = 0
    for off, op, arg, nxt in ops[:-2]:
        if off < prefix:
            continue
        pops, pushes = stack_io(op, arg)
        if depth < pops:
            raise ValueError(f"非空栈前缀或线性栈下溢，位于偏移量 {off}")
        depth += pushes - pops
        if depth == 0:
            safe.append(nxt)
    if depth:
        raise ValueError("线性后缀未在空栈处结束")
    cuts = [0]
    for boundary in safe:
        if boundary - cuts[-1] >= target_bytes and boundary < end:
            cuts.append(boundary)
    cuts.append(len(code.co_code))
    for start, stop in zip(cuts, cuts[1:]):
        for off, op, arg, nxt in ops:
            if start <= off < stop and op in opc.hasjabs + opc.hasjrel:
                target = arg if op in opc.hasjabs else nxt + arg
                if not start <= target < stop:
                    raise ValueError("分块越过了控制流分支边界")
    return cuts


def diagnostics(reset=False):
    result = list(_events)
    if reset:
        _events.clear()
    return result


def install(target_bytes=1200):
    global _installed
    if _installed:
        return False
    import uncompyle6
    from uncompyle6.parsers.treenode import SyntaxTree
    from uncompyle6.semantics.pysource import SourceWalker
    if uncompyle6.__version__ != "3.9.3":
        raise RuntimeError(f"分块适配器需要 uncompyle6 3.9.3，当前版本为: {uncompyle6.__version__}")
    control_flow_install()
    literal_install()
    original = SourceWalker.build_ast

    @wraps(original)
    def build_ast(self, tokens, customize, code, is_lambda=False,
                  noneInNames=False, is_top_level_module=False, compile_mode="exec"):
        arguments = dict(is_lambda=is_lambda, noneInNames=noneInNames,
                         is_top_level_module=is_top_level_module, compile_mode=compile_mode)
        if not (self.version[:2] == (2, 7) and is_top_level_module
                and code.co_name == "<module>" and compile_mode == "exec"
                and not is_lambda and len(code.co_code) > target_bytes * 2):
            return original(self, tokens, customize, code, **arguments)
        try:
            cuts = module_cuts(code, target_bytes)
        except ValueError as exc:
            _events.append(dict(code=code.co_name, file=code.co_filename, skipped=str(exc)))
            return original(self, tokens, customize, code, **arguments)
        if len(cuts) == 2:
            return original(self, tokens, customize, code, **arguments)
        groups = [[] for _ in cuts[:-1]]
        index = 0
        for token in tokens:
            offset = token.off2int()
            while index + 1 < len(groups) and offset >= cuts[index + 1]:
                index += 1
            groups[index].append(token)
        if sum(map(len, groups)) != len(tokens) or any(not group for group in groups):
            raise ValueError("Token 划分不完整")
        before = hashlib.sha256(code.co_code).hexdigest()
        tree = SyntaxTree("stmts", [])
        timings = []
        for group in groups:
            started = time.monotonic()
            subtree = original(self, list(group), customize, code, **arguments)
            if subtree.kind != "stmts":
                raise ValueError("分块未能解析为合法的语句序列")
            tree.extend(subtree)
            timings.append(round(time.monotonic() - started, 4))
        if hashlib.sha256(code.co_code).hexdigest() != before:
            raise ValueError("分块解析意外修改了原始代码")
        _events.append(dict(code=code.co_name, file=code.co_filename,
                            cuts=cuts, token_counts=list(map(len, groups)), seconds=timings))
        return tree

    SourceWalker.build_ast = build_ast
    _installed = True
    return True
