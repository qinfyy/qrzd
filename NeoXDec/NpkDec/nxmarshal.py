# -*- coding: utf-8 -*-
"""Python 2.7 marshal reader/writer for NeoX code objects.

Preserves raw bytes for TYPE_STRING/TYPE_INTERNED and safely handles
surrogate characters in TYPE_UNICODE.
"""

import struct

# ---- py2.7 marshal type tags ----
T_NULL = ord('0')
T_NONE = ord('N')
T_FALSE = ord('F')
T_TRUE = ord('T')
T_STOPITER = ord('S')
T_ELLIPSIS = ord('.')
T_INT = ord('i')
T_INT64 = ord('I')
T_FLOAT = ord('f')
T_BINFLOAT = ord('g')
T_COMPLEX = ord('x')
T_BINCOMPLEX = ord('y')
T_STRING = ord('s')
T_INTERNED = ord('t')
T_STRINGREF = ord('R')
T_REF = ord('r')
T_UNICODE = ord('u')
T_TUPLE = ord('(')
T_LIST = ord('[')
T_DICT = ord('{')
T_CODE = ord('c')
T_LONG = ord('l')
T_SET = ord('<')
T_FROZENSET = ord('>')

PY27_MAGIC = b'\x03\xf3\x0d\x0a'


class Code(object):
    """Container for Python 2.7 code objects parsed from NeoX marshal data."""

    __slots__ = (
        'argcount',
        'nlocals',
        'stacksize',
        'flags',
        'code',
        'consts',
        'names',
        'varnames',
        'freevars',
        'cellvars',
        'filename',
        'name',
        'firstlineno',
        'lnotab',
    )

    def __init__(self, **kw):
        for k in self.__slots__:
            setattr(self, k, kw.get(k))

    def __repr__(self):
        return f'<Code {self.name!r} {self.filename!r}:{self.firstlineno}>'


class Reader(object):
    """NeoX marshal deserializer."""

    def __init__(self, buf):
        self.b = buf
        self.i = 0
        self.refs = []
        self.intern = []
        self.tag_census = {}

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
            raise ValueError('marshal recursion depth exceeded')
        t = self.u8()
        self.tag_census[t] = self.tag_census.get(t, 0) + 1
        if t == T_NULL or t == T_NONE:
            return None
        if t == T_FALSE:
            return False
        if t == T_TRUE:
            return True
        if t == T_STOPITER:
            return StopIteration
        if t == T_ELLIPSIS:
            return Ellipsis
        if t == T_INT:
            return self.i32()
        if t == T_INT64:
            v = struct.unpack_from('<q', self.b, self.i)[0]
            self.i += 8
            return v
        if t == T_BINFLOAT:
            v = struct.unpack_from('<d', self.b, self.i)[0]
            self.i += 8
            return v
        if t == T_FLOAT:
            n = self.u8()
            s = self.b[self.i:self.i + n]
            self.i += n
            return float(s)
        if t == T_COMPLEX:
            n = self.u8()
            re = float(self.b[self.i:self.i + n])
            self.i += n
            n = self.u8()
            im = float(self.b[self.i:self.i + n])
            self.i += n
            return complex(re, im)
        if t == T_BINCOMPLEX:
            real, imag = struct.unpack_from('<dd', self.b, self.i)
            self.i += 16
            return complex(real, imag)
        if t == T_STRING:
            n = self.i32()
            v = bytes(self.b[self.i:self.i + n])
            self.i += n
            return v
        if t == T_INTERNED:
            n = self.i32()
            v = bytes(self.b[self.i:self.i + n])
            self.i += n
            self.intern.append(v)
            return v
        if t == T_STRINGREF:
            n = self.i32()
            return self.intern[n]
        if t == T_REF:
            n = self.i32()
            return self.refs[n]
        if t == T_UNICODE:
            n = self.i32()
            if n < 0 or n > len(self.b) - self.i:
                raise ValueError(f'invalid Unicode payload length: {n}')
            v = bytes(self.b[self.i:self.i + n])
            self.i += n
            return v.decode('utf-8', 'surrogatepass')
        if t == T_TUPLE:
            n = self.i32()
            return tuple(self.obj(depth + 1) for _ in range(n))
        if t == T_LIST:
            n = self.i32()
            return [self.obj(depth + 1) for _ in range(n)]
        if t == T_DICT:
            d = {}
            while True:
                k = self.obj(depth + 1)
                if k is None and self.b[self.i - 1] == T_NULL:
                    break
                d[k] = self.obj(depth + 1)
            return d
        if t == T_SET:
            n = self.i32()
            return set(self.obj(depth + 1) for _ in range(n))
        if t == T_FROZENSET:
            n = self.i32()
            return frozenset(self.obj(depth + 1) for _ in range(n))
        if t == T_LONG:
            n = self.i32()
            sign = 1
            if n < 0:
                sign = -1
                n = -n
            v = 0
            for k in range(n):
                d = struct.unpack_from('<H', self.b, self.i)[0]
                self.i += 2
                v |= d << (15 * k)
            return v * sign
        if t == T_CODE:
            return self.code(depth)
        raise ValueError(f'unknown marshal tag {chr(t)!r} ({t}) at {self.i - 1}')

    def code(self, depth=0):
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
        return Code(
            argcount=argcount,
            nlocals=nlocals,
            stacksize=stacksize,
            flags=flags,
            code=code,
            consts=consts,
            names=names,
            varnames=varnames,
            freevars=freevars,
            cellvars=cellvars,
            filename=filename,
            name=name,
            firstlineno=firstlineno,
            lnotab=lnotab,
        )


def load(buf):
    return Reader(buf).obj()


def _w_str(out, tag, b):
    out.append(tag)
    out += struct.pack('<i', len(b))
    out += b
    return out


def dump(obj, out=None):
    if out is None:
        out = bytearray()
    _w(out, obj)
    return bytes(out)


def _w(out, o):
    if o is None:
        out.append(T_NONE)
    elif o is True:
        out.append(T_TRUE)
    elif o is False:
        out.append(T_FALSE)
    elif o is StopIteration:
        out.append(T_STOPITER)
    elif o is Ellipsis:
        out.append(T_ELLIPSIS)
    elif isinstance(o, int):
        if -0x80000000 <= o <= 0x7fffffff:
            out.append(T_INT)
            out += struct.pack('<i', o)
        else:
            out.append(T_LONG)
            neg = o < 0
            v = abs(o)
            digs = []
            while v:
                digs.append(v & 0x7fff)
                v >>= 15
            if not digs:
                digs = [0]
            n = len(digs)
            out += struct.pack('<i', -n if neg else n)
            for d in digs:
                out += struct.pack('<H', d)
    elif isinstance(o, float):
        out.append(T_BINFLOAT)
        out += struct.pack('<d', o)
    elif isinstance(o, complex):
        out.append(T_COMPLEX)
        for part in (o.real, o.imag):
            s = repr(part).encode()
            out.append(len(s))
            out += s
    elif isinstance(o, Code):
        out.append(T_CODE)
        out += struct.pack('<iiii', o.argcount, o.nlocals, o.stacksize, o.flags)
        for f in (
            'code',
            'consts',
            'names',
            'varnames',
            'freevars',
            'cellvars',
            'filename',
            'name',
        ):
            _w(out, getattr(o, f))
        out += struct.pack('<i', o.firstlineno)
        _w(out, o.lnotab)
    elif isinstance(o, bytes):
        _w_str(out, T_STRING, o)
    elif isinstance(o, str):
        _w_str(out, T_UNICODE, o.encode('utf-8', 'surrogatepass'))
    elif isinstance(o, tuple):
        out.append(T_TUPLE)
        out += struct.pack('<i', len(o))
        for x in o:
            _w(out, x)
    elif isinstance(o, list):
        out.append(T_LIST)
        out += struct.pack('<i', len(o))
        for x in o:
            _w(out, x)
    elif isinstance(o, dict):
        out.append(T_DICT)
        for k, v in o.items():
            _w(out, k)
            _w(out, v)
        out.append(T_NULL)
    elif isinstance(o, (set, frozenset)):
        out.append(T_SET if isinstance(o, set) else T_FROZENSET)
        out += struct.pack('<i', len(o))
        for x in o:
            _w(out, x)
    else:
        raise TypeError(f'cannot marshal {type(o)!r}')
