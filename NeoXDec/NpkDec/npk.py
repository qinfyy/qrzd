# -*- coding: utf-8 -*-
"""NeoX NPK container reader and parser.

Supports standard unencrypted index NPKs (e.g. script.npk) and AES-128-ECB
encrypted index resource NPKs (e.g. assets/res/*.npk).
"""

import hashlib
import struct

try:
    from Crypto.Cipher import AES
except ImportError:
    AES = None

HDR_FORMAT = '<6IB'
ENTRY_FORMAT = '<7I'
ENTRY_SIZE = 28
DEFAULT_AES_INDEX_KEY = b'g58i^C04SW!@e}ad'


def murmur3(data, seed=0x9747B28C):
    """32-bit MurmurHash3 implementation matching NeoX asset name hashing."""
    if isinstance(data, str):
        data = data.encode('utf-8')
    c1 = 0xcc9e2d51
    c2 = 0x1b873593
    h = seed & 0xffffffff
    n = len(data)
    i = 0
    while n - i >= 4:
        k = struct.unpack_from('<I', data, i)[0]
        k = (k * c1) & 0xffffffff
        k = ((k << 15) | (k >> 17)) & 0xffffffff
        k = (k * c2) & 0xffffffff
        h ^= k
        h = ((h << 13) | (h >> 19)) & 0xffffffff
        h = (h * 5 + 0xe6546b64) & 0xffffffff
        i += 4
    k = 0
    tail = n & 3
    if tail == 3:
        k ^= data[i + 2] << 16
    if tail >= 2:
        k ^= data[i + 1] << 8
    if tail >= 1:
        k ^= data[i]
        k = (k * c1) & 0xffffffff
        k = ((k << 15) | (k >> 17)) & 0xffffffff
        k = (k * c2) & 0xffffffff
        h ^= k
    h ^= n
    h ^= h >> 16
    h = (h * 0x85ebca6b) & 0xffffffff
    h ^= h >> 13
    h = (h * 0xc2b2ae35) & 0xffffffff
    h ^= h >> 16
    return h


class Npk(object):
    """Parser for NeoX NPK archive files."""

    def __init__(self, path_or_data, index_key=DEFAULT_AES_INDEX_KEY):
        if isinstance(path_or_data, (bytes, bytearray)):
            self.data = bytes(path_or_data)
        else:
            with open(path_or_data, 'rb') as f:
                self.data = f.read()

        if len(self.data) < 25:
            raise ValueError("NPK file too small for header")

        (
            self.magic,
            self.entries_count,
            self.flags1,
            self.flags2,
            self.hash_mode,
            self.entry_offset,
            self.encrypted,
        ) = struct.unpack_from(HDR_FORMAT, self.data, 0)

        if self.magic != 0x4B50584E:  # 'NPXK'
            raise ValueError(f"Invalid NPK magic: 0x{self.magic:08x}")

        table_size = self.entries_count * ENTRY_SIZE
        disk_size = (table_size + 15) & ~15 if self.encrypted else table_size
        if self.entry_offset + disk_size > len(self.data):
            raise ValueError("Index table exceeds file boundary")

        raw_table = bytes(self.data[self.entry_offset:self.entry_offset + disk_size])
        if self.encrypted:
            if AES is None:
                raise ImportError("PyCryptodome (Crypto) is required for encrypted index NPKs")
            cipher = AES.new(index_key, AES.MODE_ECB)
            raw_table = cipher.decrypt(raw_table)

        raw_table = raw_table[:table_size]
        self.list = list(struct.iter_unpack(ENTRY_FORMAT, raw_table))

    def raw(self, entry):
        """Extract raw packed bytes for an entry (eid, offset, packed, raw, cp, cr, flags)."""
        _, offset, packed, _, _, _, _ = entry
        return self.data[offset:offset + packed]

    def __len__(self):
        return len(self.list)

    def __iter__(self):
        return iter(self.list)
