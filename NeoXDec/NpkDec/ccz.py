# -*- coding: utf-8 -*-
"""NeoX CCZ container decoder.

Implements libclient.so XXTEA 6-round 1024-word table derivation,
sparse XOR decryption, XOR checksum, and zlib decompression.
"""

import struct
import zlib

MASK = 0xffffffff


def derive_table(keys):
    """Derive 1024-entry uint32 key table from 4 uint32 keys via XXTEA."""
    if len(keys) != 4 or any(not 0 <= key <= MASK for key in keys):
        raise ValueError('Requires four unsigned 32-bit integer keys')
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
    """Decrypt payload using the derived key table."""
    if len(data) < 16 or len(table) != 1024:
        raise ValueError('Invalid CCZ data or key table')
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
    """Compute CCZ header checksum over first 64 words."""
    result = 0
    for index in range(min((len(data) - 12) // 4, 64)):
        result ^= struct.unpack_from('<I', data, 12 + index * 4)[0]
    return result


def decode(data, keys=None, max_output=64 * 1024 * 1024):
    """Decode unencrypted (CCZ!) or encrypted (CCZp) CCZ payload."""
    if len(data) <= 16 or data[:4] not in (b'CCZ!', b'CCZp'):
        raise ValueError('Not a supported CCZ container')
    if data[:4] == b'CCZp':
        if keys is None:
            raise ValueError('Encrypted CCZ requires 4 uint32 keys')
        data = xor_payload(data, derive_table(keys))
        if checksum(data) != struct.unpack_from('>I', data, 8)[0]:
            raise ValueError('CCZ checksum mismatch')
    compression, version, reserved, size = struct.unpack_from('>HHII', data, 4)
    if compression != 0:
        raise ValueError(f'Unsupported CCZ compression type: {compression}')
    if size > max_output:
        raise ValueError(f'CCZ unpacked size exceeds limit: {size}')
    decoder = zlib.decompressobj()
    result = decoder.decompress(data[16:], size + 1)
    if len(result) != size or not decoder.eof:
        raise ValueError('CCZ decompressed size or EOF mismatch')
    return result
