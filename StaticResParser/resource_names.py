"""NeoX Legacy 逻辑文件名散列，不处理 NPK 容器。"""

MASK32 = 0xffffffff


def string_id(name: str) -> str:
    """复现版本 83 的 StringIDLegacy，输入为大小写敏感的包内文件名。"""
    data = name.encode('utf-8')
    if b'\0' in data:
        raise ValueError('逻辑文件名不能包含 NUL')
    words = [int.from_bytes(data[i:i + 4].ljust(4, b'\0'), 'little') for i in range(0, len(data), 4)]
    words.extend((0x9be74448, 0x66f42c48))
    seed, left, right = -184907480 & MASK32, 2002301995, 933775118
    for word in words:
        seed = ((seed << 1) | (seed >> 31)) & MASK32
        left ^= word
        right ^= word
        salt = seed ^ 0x267b0b11
        product_right = ((((left + salt) & MASK32) & 0xbdeb77de) | 0x02040801) * right
        product_left = ((((right + salt) & MASK32) & 0x7d7ebbde) | 0x00804021) * left
        # 原函数在两个乘积上使用不同的进位折叠，不能替换成普通 uint32 乘法。
        high = product_right >> 32
        folded = high + (product_right & MASK32) + bool(high)
        right = (folded + (folded >> 32)) & MASK32
        folded = ((product_left >> 31) & 0xfffffffe) + (product_left & MASK32)
        left = (folded + ((folded >> 31) & 2)) & MASK32
    return f'{left ^ right:08x}'
