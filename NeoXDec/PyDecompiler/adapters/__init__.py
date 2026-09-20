# -*- coding: utf-8 -*-
"""NeoXDec.PyDec.adapters - uncompyle6 / xdis in-memory patches for NeoX bytecode."""

from .control_flow import install as install_control_flow
from .literals import install as install_literals
from .chunked import install as install_chunked

__all__ = [
    'install_control_flow',
    'install_literals',
    'install_chunked',
]
