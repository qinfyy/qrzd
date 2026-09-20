# -*- coding: utf-8 -*-
"""Pure-python port of CPython 2.7 Modules/rotormodule.c (rotor.newrotor)."""

import math


def _cdiv(a, b):
    q = abs(a) // abs(b)
    if (a < 0) != (b < 0):
        q = -q
    return q


def _cmod(a, b):
    return a - _cdiv(a, b) * b


def _to_short(v):
    v &= 0xffff
    return v - 0x10000 if v >= 0x8000 else v


def _rot16(v, n):
    v &= 0xffff
    return ((v << n) | (v >> (16 - n))) & 0xffff


class Rotor(object):
    """NeoX compatible software rotor stream cipher."""

    def __init__(self, key, num_rotors=6):
        if isinstance(key, str):
            key = key.encode('latin-1')
        self.size = 256
        self.rotors = num_rotors
        self._set_key(key)

    def _set_key(self, key):
        k1, k2, k3, k4, k5 = 995, 576, 767, 671, 463
        for ch in key:
            ki = ch & 0xff
            k1 = (_rot16(k1, 3) + ki) & 0xffff
            k2 = (_rot16(k2, 3) ^ ki) & 0xffff
            k3 = (_rot16(k3, 3) - ki) & 0xffff
            k4 = (ki - _rot16(k4, 3)) & 0xffff
            k5 = (_rot16(k5, 3) ^ (~ki & 0xffff)) & 0xffff
        self.key = [_to_short(k1), _to_short(k2 | 1), _to_short(k3), _to_short(k4), _to_short(k5)]
        self._set_seed()

    def _set_seed(self):
        self.seed = [self.key[0], self.key[1], self.key[2]]
        self.isinited = False

    def _random(self):
        x, y, z = self.seed
        x = 171 * _cmod(x, 177) - 2 * _cdiv(x, 177)
        y = 172 * _cmod(y, 176) - 35 * _cdiv(y, 176)
        z = 170 * _cmod(z, 178) - 63 * _cdiv(z, 178)
        if x < 0:
            x += 30269
        if y < 0:
            y += 30307
        if z < 0:
            z += 30323
        self.seed = [x, y, z]
        term = x / 30269.0 + y / 30307.0 + z / 30323.0
        val = term - math.floor(term)
        if val >= 1.0:
            val = 0.0
        return val

    def _rand(self, s):
        return int(self._random() * s) % s

    def _permute(self, e, d):
        size = self.size
        for j in range(size):
            e[j] = j
        i = size
        while i >= 2:
            q = self._rand(i)
            i -= 1
            j = e[q]
            e[q] = e[i]
            e[i] = j
            d[j] = i
        d[e[0]] = 0

    def _init(self):
        self._set_seed()
        n = self.rotors
        self.positions = [1] * n
        self.advances = [1] * n
        self.e_rotor = [bytearray(256) for _ in range(n)]
        self.d_rotor = [bytearray(256) for _ in range(n)]
        for i in range(n):
            self.positions[i] = self._rand(256) & 0xff
            self.advances[i] = (1 + 2 * self._rand(128)) & 0xff
            self._permute(self.e_rotor[i], self.d_rotor[i])
        self.isinited = True

    def _advance(self):
        n = self.rotors
        for i in range(n):
            temp = self.positions[i] + self.advances[i]
            self.positions[i] = temp % 256
            if temp >= 256 and i < n - 1:
                self.positions[i + 1] = (1 + self.positions[i + 1]) & 0xff

    def _e_char(self, p):
        tp = p & 0xff
        for i in range(self.rotors):
            tp = self.e_rotor[i][(self.positions[i] ^ tp) % 256]
        self._advance()
        return tp

    def _d_char(self, c):
        tc = c & 0xff
        for i in range(self.rotors - 1, -1, -1):
            tc = (self.positions[i] ^ self.d_rotor[i][tc]) % 256
        self._advance()
        return tc

    def decrypt(self, data, doinit=True):
        if doinit or not self.isinited:
            self._init()
        out = bytearray(len(data))
        for k, b in enumerate(data):
            out[k] = self._d_char(b)
        return bytes(out)

    def encrypt(self, data, doinit=True):
        if doinit or not self.isinited:
            self._init()
        out = bytearray(len(data))
        for k, b in enumerate(data):
            out[k] = self._e_char(b)
        return bytes(out)

    def decryptmore(self, data):
        return self.decrypt(data, doinit=False)

    def encryptmore(self, data):
        return self.encrypt(data, doinit=False)


def newrotor(key, num_rotors=6):
    return Rotor(key, num_rotors)
