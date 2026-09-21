"""
NeoX (Python 2.7) 字节码解码器、操作码去混淆与反汇编工具。

将 NeoX 加密操作码映射回标准 CPython 2.7 操作码，
展开复合融合指令（173 -> LOAD_FAST 0 + LOAD_CONST），修正 EXTENDED_ARG（160 -> 145），
并迭代重新计算/重定位跳转目标偏移与行号表（lnotab）直至收敛。
"""

import struct
import sys

# CPython 2.7 操作码 -> NeoX 加密操作码
ENC = {
    1: 38,
    2: 46,
    3: 37,
    4: 66,
    5: 12,
    10: 35,
    11: 67,
    12: 81,
    13: 32,
    15: 9,
    19: 63,
    20: 70,
    21: 44,
    22: 36,
    23: 39,
    24: 57,
    25: 10,
    26: 52,
    28: 49,
    30: 86,
    31: 87,
    32: 88,
    33: 89,
    40: 24,
    41: 25,
    42: 26,
    43: 27,
    50: 14,
    51: 15,
    52: 16,
    53: 17,
    54: 8,
    55: 21,
    56: 55,
    57: 82,
    58: 34,
    59: 22,
    60: 65,
    61: 6,
    62: 58,
    63: 71,
    64: 43,
    65: 30,
    66: 19,
    67: 5,
    68: 60,
    71: 53,
    72: 42,
    73: 3,
    74: 48,
    75: 84,
    76: 77,
    77: 78,
    78: 85,
    79: 47,
    80: 51,
    81: 54,
    82: 50,
    83: 83,
    84: 74,
    85: 64,
    86: 31,
    87: 72,
    88: 45,
    89: 33,
    90: 145,
    91: 159,
    92: 125,
    93: 149,
    94: 157,
    95: 132,
    96: 95,
    97: 113,
    98: 111,
    99: 138,
    100: 153,
    101: 101,
    102: 135,
    103: 90,
    104: 99,
    105: 151,
    106: 96,
    107: 114,
    108: 134,
    109: 116,
    110: 156,
    111: 105,
    112: 130,
    113: 137,
    114: 148,
    115: 172,
    116: 155,
    119: 103,
    120: 158,
    121: 128,
    122: 110,
    124: 97,
    125: 104,
    126: 118,
    130: 93,
    131: 131,
    132: 136,
    133: 115,
    134: 100,
    135: 120,
    136: 129,
    137: 102,
    140: 140,
    141: 141,
    142: 142,
    143: 94,
    146: 109,
    147: 123,
    # 从 script.npk 中恢复的额外操作码条目：
    27: 13,   # BINARY_TRUE_DIVIDE (源自 __future__ import division)
    145: 160, # EXTENDED_ARG
}
DEC = {v: k for k, v in ENC.items()}

# 加密指令 173 (0xAD) 是 NeoX 的复合融合指令：LOAD_FAST 0 + LOAD_CONST
FUSED_LOAD_FAST_CONST = 173
ENC_EXTENDED_ARG = 160
REAL_EXTENDED_ARG = 145

OPNAME = {
    0: 'STOP_CODE',
    1: 'POP_TOP',
    2: 'ROT_TWO',
    3: 'ROT_THREE',
    4: 'DUP_TOP',
    5: 'ROT_FOUR',
    9: 'NOP',
    10: 'UNARY_POSITIVE',
    11: 'UNARY_NEGATIVE',
    12: 'UNARY_NOT',
    13: 'UNARY_CONVERT',
    15: 'UNARY_INVERT',
    19: 'BINARY_POWER',
    20: 'BINARY_MULTIPLY',
    21: 'BINARY_DIVIDE',
    22: 'BINARY_MODULO',
    23: 'BINARY_ADD',
    24: 'BINARY_SUBTRACT',
    25: 'BINARY_SUBSCR',
    26: 'BINARY_FLOOR_DIVIDE',
    27: 'BINARY_TRUE_DIVIDE',
    28: 'INPLACE_FLOOR_DIVIDE',
    29: 'INPLACE_TRUE_DIVIDE',
    30: 'SLICE+0',
    31: 'SLICE+1',
    32: 'SLICE+2',
    33: 'SLICE+3',
    40: 'STORE_SLICE+0',
    41: 'STORE_SLICE+1',
    42: 'STORE_SLICE+2',
    43: 'STORE_SLICE+3',
    50: 'DELETE_SLICE+0',
    51: 'DELETE_SLICE+1',
    52: 'DELETE_SLICE+2',
    53: 'DELETE_SLICE+3',
    54: 'STORE_MAP',
    55: 'INPLACE_ADD',
    56: 'INPLACE_SUBTRACT',
    57: 'INPLACE_MULTIPLY',
    58: 'INPLACE_DIVIDE',
    59: 'INPLACE_MODULO',
    60: 'STORE_SUBSCR',
    61: 'DELETE_SUBSCR',
    62: 'BINARY_LSHIFT',
    63: 'BINARY_RSHIFT',
    64: 'BINARY_AND',
    65: 'BINARY_XOR',
    66: 'BINARY_OR',
    67: 'INPLACE_POWER',
    68: 'GET_ITER',
    70: 'PRINT_EXPR',
    71: 'PRINT_ITEM',
    72: 'PRINT_NEWLINE',
    73: 'PRINT_ITEM_TO',
    74: 'PRINT_NEWLINE_TO',
    75: 'INPLACE_LSHIFT',
    76: 'INPLACE_RSHIFT',
    77: 'INPLACE_AND',
    78: 'INPLACE_XOR',
    79: 'INPLACE_OR',
    80: 'BREAK_LOOP',
    81: 'WITH_CLEANUP',
    82: 'LOAD_LOCALS',
    83: 'RETURN_VALUE',
    84: 'IMPORT_STAR',
    85: 'EXEC_STMT',
    86: 'YIELD_VALUE',
    87: 'POP_BLOCK',
    88: 'END_FINALLY',
    89: 'BUILD_CLASS',
    90: 'STORE_NAME',
    91: 'DELETE_NAME',
    92: 'UNPACK_SEQUENCE',
    93: 'FOR_ITER',
    94: 'LIST_APPEND',
    95: 'STORE_ATTR',
    96: 'DELETE_ATTR',
    97: 'STORE_GLOBAL',
    98: 'DELETE_GLOBAL',
    99: 'DUP_TOPX',
    100: 'LOAD_CONST',
    101: 'LOAD_NAME',
    102: 'BUILD_TUPLE',
    103: 'BUILD_LIST',
    104: 'BUILD_SET',
    105: 'BUILD_MAP',
    106: 'LOAD_ATTR',
    107: 'COMPARE_OP',
    108: 'IMPORT_NAME',
    109: 'IMPORT_FROM',
    110: 'JUMP_FORWARD',
    111: 'JUMP_IF_FALSE_OR_POP',
    112: 'JUMP_IF_TRUE_OR_POP',
    113: 'JUMP_ABSOLUTE',
    114: 'POP_JUMP_IF_FALSE',
    115: 'POP_JUMP_IF_TRUE',
    116: 'LOAD_GLOBAL',
    119: 'CONTINUE_LOOP',
    120: 'SETUP_LOOP',
    121: 'SETUP_EXCEPT',
    122: 'SETUP_FINALLY',
    124: 'LOAD_FAST',
    125: 'STORE_FAST',
    126: 'DELETE_FAST',
    130: 'RAISE_VARARGS',
    131: 'CALL_FUNCTION',
    132: 'MAKE_FUNCTION',
    133: 'BUILD_SLICE',
    134: 'MAKE_CLOSURE',
    135: 'LOAD_CLOSURE',
    136: 'LOAD_DEREF',
    137: 'STORE_DEREF',
    140: 'CALL_FUNCTION_VAR',
    141: 'CALL_FUNCTION_KW',
    142: 'CALL_FUNCTION_VAR_KW',
    143: 'SETUP_WITH',
    145: 'EXTENDED_ARG',
    146: 'SET_ADD',
    147: 'MAP_ADD',
}
CMP = ['<', '<=', '==', '!=', '>', '>=']

# Python 2.7 跳转指令操作码
JUMP_ABS = {111, 112, 113, 114, 115, 119}
JUMP_REL = {93, 110, 120, 121, 122, 143}


def _emit(out, real, arg):
    for opcode, part in _enc_instr(real, arg):
        out.append(opcode)
        if part is not None:
            out.extend(struct.pack('<H', part))


def _enc_instr(real, arg):
    if arg is None:
        return [(real, None)]
    if arg < 0:
        raise ValueError(f'negative bytecode argument: {arg}')
    chunks = []
    a = arg
    while a > 0xffff:
        chunks.append(a & 0xffff)
        a >>= 16
    chunks.append(a)
    res = [(REAL_EXTENDED_ARG, c) for c in reversed(chunks[1:])]
    res.append((real, chunks[0]))
    return res


def fix_opcodes(code, strict=False, return_mapping=False):
    """将 NeoX 打乱的操作码转换还原为标准 CPython 2.7 操作码。"""
    items = []
    i = 0
    n = len(code)
    ext = 0
    ext_olds = []
    while i < n:
        enc = code[i]
        if enc == ENC_EXTENDED_ARG:
            if i + 3 > n:
                raise ValueError(f'truncated EXTENDED_ARG at {i}')
            part = code[i + 1] | (code[i + 2] << 8)
            ext = (ext << 16) | part
            ext_olds.append(i)
            i += 3
            continue
        if enc == FUSED_LOAD_FAST_CONST:
            if i + 3 > n:
                raise ValueError(f'truncated fused op at {i}')
            arg = code[i + 1] | (code[i + 2] << 8)
            if ext:
                arg = (ext << 16) | arg
                ext = 0
            items.append({
                'old': i,
                'real': None,
                'arg': arg,
                'fused': True,
                'ext_olds': ext_olds,
            })
            ext_olds = []
            i += 3
            continue
        if enc not in DEC:
            if strict:
                raise ValueError(f'unknown encrypted opcode {enc} at {i}')
            if ext_olds:
                raise ValueError(f'EXTENDED_ARG before unknown opcode at {i}')
            items.append({
                'old': i,
                'real': enc,
                'arg': None,
                'raw': True,
                'ext_olds': ext_olds,
            })
            ext_olds = []
            i += 1
            continue
        real = DEC[enc]
        if real >= 90:
            if i + 3 > n:
                raise ValueError(f'truncated opcode at {i}')
            arg = code[i + 1] | (code[i + 2] << 8)
            if ext:
                arg = (ext << 16) | arg
                ext = 0
            items.append({
                'old': i,
                'real': real,
                'arg': arg,
                'ext_olds': ext_olds,
            })
            ext_olds = []
            i += 3
        else:
            if ext_olds:
                raise ValueError(f'EXTENDED_ARG before argumentless opcode at {i}')
            items.append({
                'old': i,
                'real': real,
                'arg': None,
                'ext_olds': ext_olds,
            })
            ext_olds = []
            i += 1

    if ext_olds:
        raise ValueError(f'dangling EXTENDED_ARG at {ext_olds[0]}')

    for it in items:
        it['oarg'] = it['arg']
        it['start'] = it['ext_olds'][0] if it['ext_olds'] else it['old']

    starts = {it['start'] for it in items} | {len(code)}
    for it in items:
        if it['real'] in JUMP_ABS:
            it['target'] = it['oarg']
        elif it['real'] in JUMP_REL:
            it['target'] = it['old'] + 3 + it['oarg']
        else:
            continue
        if it['target'] not in starts:
            raise ValueError(f"invalid jump target {it['target']} at {it['old']}")

    def sequence(it):
        if it.get('fused'):
            return [(124, 0), (100, it['arg'])]
        return [(it['real'], it['arg'])]

    def encoded_size(it):
        return sum(1 if a is None else 3 * len(_enc_instr(r, a)) for r, a in sequence(it))

    old2new = {}
    new_len = 0
    seen_sizes = set()
    for _round in range(len(items) + 2):
        off = 0
        old2new = {}
        for it in items:
            it['new'] = off
            old2new[it['old']] = off
            for o in it['ext_olds']:
                old2new[o] = off
            it['size'] = encoded_size(it)
            off += it['size']
        new_len = off
        old2new[len(code)] = new_len
        sizes = tuple(it['size'] for it in items)
        if sizes in seen_sizes:
            raise ValueError('bytecode layout failed to converge')
        seen_sizes.add(sizes)
        changed = False
        for it in items:
            real = it['real']
            if real is None or it['oarg'] is None or it.get('raw'):
                continue
            if real in JUMP_ABS:
                newarg = old2new[it['target']]
            elif real in JUMP_REL:
                t_new = old2new[it['target']]
                newarg = t_new - (it['new'] + it['size'])
            else:
                continue
            it['arg'] = newarg
            changed |= encoded_size(it) != it['size']
        if not changed:
            break
    else:
        raise ValueError('bytecode layout exceeded iteration bound')

    out = bytearray()
    ops = []
    for it in items:
        for r, a in sequence(it):
            ops.append((len(out), r, a, code[it['old']]))
            _emit(out, r, a)
    assert len(out) == new_len
    if return_mapping:
        return bytes(out), ops, old2new
    return bytes(out), ops


def relocate_lnotab(lnotab, old2new):
    """将 Python 2.7 无符号行号增量重定位到新的指令偏移量。"""
    if len(lnotab) % 2:
        raise ValueError('odd lnotab length')
    out = bytearray()
    old_addr = last_new = 0
    for addr_delta, line_delta in zip(lnotab[::2], lnotab[1::2]):
        old_addr += addr_delta
        if not line_delta:
            continue
        if old_addr not in old2new:
            raise ValueError(f'lnotab offset is not an instruction boundary: {old_addr}')
        new_addr = old2new[old_addr]
        delta = new_addr - last_new
        if delta < 0:
            raise ValueError('non-monotonic lnotab')
        while delta > 255:
            out.extend((255, 0))
            delta -= 255
        out.extend((delta, line_delta))
        last_new = new_addr
    return bytes(out)
