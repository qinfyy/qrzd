from __future__ import annotations

import hashlib
import json
import math
import re
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / '.deps'))
import lz4.block
import msgpack

from resource_names import string_id

CATALOG_ID = '4b1354f6'
MAX_ROW_SIZE = 64 * 1024 * 1024
JSON_TAGS = {'$tuple', '$map', '$binary', '$ext', '$float'}


def safe_component(name: str) -> str:
    if not name or name in {'.', '..'} or re.search(r'[<>:"/\\|?*\x00-\x1f]', name) or name.endswith((' ', '.')):
        raise ValueError(f'非法逻辑文件名：{name!r}')
    if re.fullmatch(r'(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])', name.split('.')[0]):
        raise ValueError(f'Windows 保留文件名：{name!r}')
    return name


def is_catalog(path: Path) -> bool:
    return path.name.casefold() == 'dbx_md5.json' or path.stem.casefold() == CATALOG_ID


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f'JSON 重复键：{key}')
        result[key] = value
    return result


def read_catalog(path: Path) -> dict[str, str]:
    value = json.loads(path.read_bytes(), object_pairs_hook=unique_object)
    if not isinstance(value, dict):
        raise ValueError(f'{path}: DBX 清单不是对象')
    for name, md5 in value.items():
        safe_component(name)
        if name.casefold() == 'dbx_md5':
            raise ValueError('表名与清单输出文件冲突')
        if not isinstance(md5, str) or not re.fullmatch(r'[0-9a-fA-F]{32}', md5):
            raise ValueError(f'{path}: {name} 的 MD5 无效')
    return {name: md5.lower() for name, md5 in value.items()}


def json_value(value):
    """文本转 UTF-8；JSON 不原生支持的值用明确标签表示，避免主键丢失。"""
    if isinstance(value, bytes):
        try:
            return value.decode('utf-8')
        except UnicodeDecodeError:
            return {'$binary': value.hex()}
    if isinstance(value, msgpack.ExtType):
        return {'$ext': {'code': value.code, 'hex': value.data.hex()}}
    if isinstance(value, tuple):
        return {'$tuple': [json_value(item) for item in value]}
    if isinstance(value, list):
        return [json_value(item) for item in value]
    if isinstance(value, dict):
        keys = [json_value(key) for key in value]
        if all(isinstance(key, str) for key in keys) and len(set(keys)) == len(keys) and not JSON_TAGS.intersection(keys):
            return {key: json_value(item) for key, item in zip(keys, value.values())}
        return {'$map': [[key, json_value(item)] for key, item in zip(keys, value.values())]}
    if isinstance(value, float) and not math.isfinite(value):
        return {'$float': str(value)}
    if value is None or isinstance(value, (bool, int, float, str)):
        return value
    raise ValueError(f'无法转为 JSON 的类型：{type(value).__name__}')


def unpack_message(data: bytes):
    return msgpack.unpackb(data, raw=False, use_list=False, strict_map_key=False)


class DbxPackage:
    """同一逻辑包的多个覆盖层；后加入的文件优先，最新清单决定有效表集合。"""

    def __init__(self, name: str, directories: list[Path]):
        self.name = safe_component(name)
        self.directories = directories
        self.entries: dict[str, Path] = {}
        self.catalog: dict[str, str] = {}
        self.catalog_path: Path | None = None
        for directory in directories:
            layer = {}
            for path in sorted(directory.iterdir()):
                if not path.is_file():
                    continue
                entry_id = None
                if re.fullmatch(r'[0-9a-fA-F]{8}', path.stem):
                    entry_id = path.stem.lower()
                elif path.suffix.casefold() in {'.dbx', '.dbxh', '.dbxcd'} or path.name.casefold() == 'dbx_md5.json':
                    entry_id = string_id(path.name)
                if entry_id is not None:
                    if entry_id in layer:
                        raise ValueError(f'同一层中 ID {entry_id} 重复：{layer[entry_id]} / {path}')
                    layer[entry_id] = path
            self.entries.update(layer)
            if CATALOG_ID in layer:
                self.catalog_path = layer[CATALOG_ID]
                self.catalog = read_catalog(self.catalog_path)
        if self.catalog_path is None:
            raise ValueError(f'{self.name}: 缺少 dbx_md5.json')
        output_names = [name.casefold() for name in self.catalog]
        if len(set(output_names)) != len(output_names):
            raise ValueError(f'{self.name}: 表名在 Windows 下存在大小写冲突')

    def table_files(self, table: str) -> dict[str, Path | None]:
        safe_component(table)
        files = {suffix: self.entries.get(string_id(f'{table}.{suffix}')) for suffix in ('dbx', 'dbxh', 'dbxcd')}
        for suffix in ('dbx', 'dbxh'):
            if files[suffix] is None:
                raise ValueError(f'缺少 {self.name}/{table}.{suffix} (ID={string_id(f"{table}.{suffix}")})，请同时提供 APK 基础层')
        return files

    def load_table(self, table: str):
        files = self.table_files(table)
        body = files['dbx'].read_bytes()
        actual_md5 = hashlib.md5(body).hexdigest()
        if actual_md5 != self.catalog[table]:
            raise ValueError(f'正文 MD5 不符：实际 {actual_md5}，清单 {self.catalog[table]}；检查覆盖层顺序')
        header = unpack_message(files['dbxh'].read_bytes())
        if not isinstance(header, dict):
            raise ValueError('索引不是 MessagePack 字典')
        dictionary = files['dbxcd'].read_bytes() if files['dbxcd'] else None
        rows = []
        for key, span in header.items():
            if not isinstance(span, tuple) or len(span) != 2 or any(type(item) is not int for item in span):
                raise ValueError(f'行 {key!r} 的索引格式无效')
            offset, length = span
            if offset < 0 or length < 4 or offset + length > len(body):
                raise ValueError(f'行 {key!r} 的索引越界：offset={offset}, length={length}')
            raw_size = struct.unpack_from('<I', body, offset)[0]
            if raw_size > MAX_ROW_SIZE:
                raise ValueError(f'行 {key!r} 声明的解压大小超过 {MAX_ROW_SIZE} 字节限制')
            try:
                raw = lz4.block.decompress(body[offset:offset + length], dict=dictionary)
                value = unpack_message(raw)
            except Exception as error:
                raise ValueError(f'行 {key!r} 解码失败，字典={files["dbxcd"]}：{error}') from error
            rows.append({'key': json_value(key), 'value': json_value(value)})
        if not header and body:
            raise ValueError('空索引对应非空正文')
        return {'resource': f'{self.name}/{table}.dbx', 'rows': rows}, files

    def unknown_entries(self) -> list[str]:
        known = {CATALOG_ID}
        known.update(string_id(f'{name}.{suffix}') for name in self.catalog for suffix in ('dbx', 'dbxh', 'dbxcd'))
        return sorted(set(self.entries) - known)
