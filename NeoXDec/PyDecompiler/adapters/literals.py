"""针对 xdis 6.1.7 与 uncompyle6 的 NeoX 特化字面量解析补丁。"""

import math

_installed = False


def install():
    """安装字面量解析补丁。"""
    global _installed
    if _installed:
        return False

    import xdis
    from xdis.cross_types import UnicodeForPython3
    from xdis.unmarshal import _VersionIndependentUnmarshaller
    from uncompyle6.semantics.pysource import SourceWalker

    if xdis.__version__ != '6.1.7':
        raise RuntimeError(f'字面量适配器需要 xdis 6.1.7，当前版本为: {xdis.__version__}')

    original_complex = _VersionIndependentUnmarshaller.t_complex
    original_const = SourceWalker.n_LOAD_CONST

    def unicode_repr(self):
        value = self.value
        if isinstance(value, bytes):
            value = value.decode('utf-8', 'surrogatepass')
        return 'u' + ascii(value)

    def t_complex(self, save_ref, bytes_for_s=False):
        if self.version_tuple[:2] != (2, 7):
            return original_complex(self, save_ref, bytes_for_s)

        def get_float():
            length = self.fp.read(1)
            if len(length) != 1:
                raise EOFError('Python 2 复数字节长度被截断')
            raw = self.fp.read(length[0])
            if len(raw) != length[0]:
                raise EOFError('Python 2 复数分量数据被截断')
            return float(raw.decode('ascii'))

        return self.r_ref(complex(get_float(), get_float()), save_ref)

    def n_LOAD_CONST(self, node):
        if self.version[:2] == (2, 7):
            value = node.pattr
            if isinstance(value, UnicodeForPython3):
                self.write(repr(value))
                self.prune()
            if isinstance(value, complex) and math.isfinite(value.real) and math.isfinite(value.imag):
                self.write(repr(value))
                self.prune()
        return original_const(self, node)

    UnicodeForPython3.__repr__ = unicode_repr
    _VersionIndependentUnmarshaller.t_complex = t_complex
    SourceWalker.n_LOAD_CONST = n_LOAD_CONST
    _installed = True
    return True
