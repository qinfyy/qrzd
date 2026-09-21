"""
NeoX CCZ 容器解码器。

实现 libclient.so XXTEA 6 轮 1024 词密钥表派生、
稀疏异或解密、XOR 校验和以及 zlib 解压缩。
"""

import struct
import zlib

MASK = 0xffffffff


def derive_table(keys):
    """通过 XXTEA 从 4 个 uint32 密钥派生 1024 长度的 uint32 密钥表。"""
    if len(keys) != 4 or any(not 0 <= key <= MASK for key in keys):
        raise ValueError('需要 4 个 32 位无符号整数密钥')
    values = [0] * 1024
    total = z = 0
    for _ in range(6):
        total = (total + 0x9e3779b9) & MASK
        e = (total >> 2) & 3
        for p in range(1024):
            y = values[(p + 1) & 1023]
            mx = (((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4)))
            mx ^= (total ^ y) + (keys[(p & 3) ^ e] ^ z)
            values[p] = z = (values[p] + mx) & MASK
    return values


def xor_payload(data, table):
    """使用派生的密钥表对有效载荷执行稀疏异或解密。"""
    if len(data) < 16 or len(table) != 1024:
        raise ValueError('无效的 CCZ 数据或密钥表')
    result = bytearray(data)
    count = (len(data) - 12) // 4
    key_index = 0
    for positions in (range(min(count, 512)), range(512, count, 64)):
        for index in positions:
            offset = 12 + index * 4
            value = struct.unpack_from('<I', result, offset)[0] ^ table[key_index & 1023]
            struct.pack_into('<I', result, offset, value)
            key_index += 1
    return bytes(result)


def checksum(data):
    """计算 CCZ 头部前 64 个字（Word）的异或校验和。"""
    result = 0
    for index in range(min((len(data) - 12) // 4, 64)):
        result ^= struct.unpack_from('<I', data, 12 + index * 4)[0]
    return result


def decode(data, keys=None, max_output=64 * 1024 * 1024):
    """解码未加密（CCZ!）或已加密（CCZp）的 CCZ 容器载荷。"""
    if len(data) <= 16 or data[:4] not in (b'CCZ!', b'CCZp'):
        raise ValueError('不支持的 CCZ 容器格式')
    if data[:4] == b'CCZp':
        if keys is None:
            raise ValueError('解密加密 CCZ 容器需要 4 个 uint32 密钥')
        data = xor_payload(data, derive_table(keys))
        if checksum(data) != struct.unpack_from('>I', data, 8)[0]:
            raise ValueError('CCZ 校验和不匹配')
    compression, version, reserved, size = struct.unpack_from('>HHII', data, 4)
    if compression != 0:
        raise ValueError(f'不支持的 CCZ 压缩类型: {compression}')
    if size > max_output:
        raise ValueError(f'CCZ 解包后大小超过上限: {size}')
    decoder = zlib.decompressobj()
    result = decoder.decompress(data[16:], size + 1)
    if len(result) != size or not decoder.eof:
        raise ValueError('CCZ 解压后大小或结束标志不匹配')
    return result
