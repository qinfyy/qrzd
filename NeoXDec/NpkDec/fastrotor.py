"""
向量化 Rotor 流解密（基于 NumPy 预计算加速）。

预先计算所有 Rotor 轮盘步进位置，大幅提升批量解密吞吐率。
"""

import numpy as np

try:
    from .rotor import Rotor
except ImportError:
    from rotor import Rotor


class FastRotor(object):
    """NumPy 向量化加速的 Rotor 密码机，用于高速流式解密。"""

    def __init__(self, key, num_rotors=6, maxlen=1):
        r = Rotor(key, num_rotors)
        r._init()
        self.n = num_rotors
        self.D = [np.frombuffer(bytes(t), dtype=np.uint8) for t in r.d_rotor]
        self.E = [np.frombuffer(bytes(t), dtype=np.uint8) for t in r.e_rotor]
        init = list(r.positions)
        adv = list(r.advances)
        self.init, self.adv = init, adv
        self.maxlen = maxlen
        k = np.arange(maxlen, dtype=np.int64)
        self.pos = []

        C = np.zeros(maxlen, dtype=np.int64)
        carry_prev = np.zeros(maxlen, dtype=np.int64)
        for i in range(num_rotors):
            A = init[i] + adv[i] * k + C
            p = (A % 256)
            self.pos.append(p.astype(np.uint8))
            tmp = ((p + carry_prev) & 0xff) + adv[i]
            carry_i = (tmp >= 256).astype(np.int64)
            C = np.concatenate(([0], np.cumsum(carry_i)[:-1]))
            carry_prev = carry_i

    def decrypt(self, data):
        arr = np.frombuffer(data, dtype=np.uint8)
        L = len(arr)
        out = arr
        for i in range(self.n - 1, -1, -1):
            out = self.pos[i][:L] ^ self.D[i][out]
        return out.tobytes()

    def encrypt(self, data):
        arr = np.frombuffer(data, dtype=np.uint8)
        L = len(arr)
        out = arr
        for i in range(self.n):
            out = self.pos[i][:L] ^ self.E[i][out]
        return out.tobytes()
