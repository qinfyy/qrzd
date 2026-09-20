# -*- coding: utf-8 -*-
"""NeoXDec.NpkDec 命令行解包工具。

功能：
- 仅负责解包 NPK 容器，不解析内部代码对象文件名，不修复字节码指令；
- 脚本包 (script.npk)：解密 Rotor/LZ4 并执行字节异或反转，直接按条目 ID 输出原始未修改的 .nxc 裸文件；
- 资源包 (res/*.npk)：解密 AES 索引表，提取各条目内容（LZ4 解压或直存），自动识别资源类型（PNG/KTX/CCZ等）。
"""

import argparse
from concurrent.futures import ProcessPoolExecutor, as_completed
import csv
import hashlib
from pathlib import Path
import struct
import sys
import time
import zlib

try:
    import lz4.block
except ImportError:
    lz4 = None

try:
    from .npk import Npk, DEFAULT_AES_INDEX_KEY
    from .fastrotor import FastRotor
    from .rotor import Rotor
    from .ccz import decode as decode_ccz
except ImportError:
    from npk import Npk, DEFAULT_AES_INDEX_KEY
    from fastrotor import FastRotor
    from rotor import Rotor
    from ccz import decode as decode_ccz

ASDF_DN = 'j2h56ogodh3se'
ASDF_DT = '=dziaq.'
ASDF_DF = '|os=5v7!"-234'
DEFAULT_ROTOR_KEY = (
    ASDF_DN * 4 + (ASDF_DT + ASDF_DN + ASDF_DF) * 5 + '!' + '#' +
    ASDF_DT * 7 + ASDF_DF * 2 + '*' + '&' + "'"
)


def reverse_string(s):
    """NeoX 专有字节反转与首 128 字节异或 154。"""
    buf = list(s)
    head_len = min(128, len(buf))
    buf[:head_len] = [c ^ 154 for c in buf[:head_len]]
    buf.reverse()
    return bytes(buf)


# 多进程解包工作进程的全局句柄
_WORKER_NPK = None
_WORKER_ROTOR = None


def _init_script_worker(npk_path, rotor_key):
    global _WORKER_NPK, _WORKER_ROTOR
    _WORKER_NPK = Npk(npk_path)
    maxlen = max(e[2] for e in _WORKER_NPK.list) if len(_WORKER_NPK.list) > 0 else 1024
    _WORKER_ROTOR = FastRotor(rotor_key, 6, maxlen)


def _process_script_entry(index):
    entry = _WORKER_NPK.list[index]
    eid, offset, packed, unpacked, packed_crc, raw_crc, flags = entry
    result_meta = {
        'index': index,
        'id': f'{eid:08x}',
        'output': f'{eid:08x}.nxc',
        'flags': f'{flags:08x}',
        'packed_size': packed,
        'raw_size': unpacked,
        'raw_sha256': '',
        'status': 'ok',
        'error': '',
    }
    try:
        raw = _WORKER_NPK.raw(entry)
        flag_val = flags & 0xff
        if flag_val == 2:
            if lz4 is None:
                raise ImportError("解压此条目需要 lz4 库，请先执行 pip install lz4")
            blob = lz4.block.decompress(raw, uncompressed_size=unpacked)
        elif flag_val == 0:
            decrypted = _WORKER_ROTOR.decrypt(raw)
            decompressed = zlib.decompress(decrypted)
            blob = reverse_string(decompressed)
        else:
            raise ValueError(f'不支持的条目标志位: {flags:#x}')

        result_meta['raw_sha256'] = hashlib.sha256(blob).hexdigest()
        return result_meta, blob
    except Exception as exc:
        result_meta['status'] = 'failed'
        result_meta['error'] = f'{type(exc).__name__}: {exc}'
        return result_meta, None


def unpack_script_npk(npk_path, output_dir, rotor_key=DEFAULT_ROTOR_KEY, jobs=4, limit=0):
    """解包 script.npk 归档为原始 .nxc 裸文件（不解析路径、不修复字节码）。"""
    npk_path = Path(npk_path)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    npk = Npk(npk_path)
    total_entries = len(npk.list) if not limit else min(len(npk.list), limit)
    print(f"[*] NpkDec 开始解包脚本包: {npk_path} (共 {total_entries} 个条目, {jobs} 个进程并发)")

    indices = list(range(total_entries))
    manifest_path = output_dir / 'manifest.tsv'
    fields = ['index', 'id', 'output', 'flags', 'packed_size', 'raw_size', 'raw_sha256', 'status', 'error']

    ok_count = 0
    fail_count = 0
    started_time = time.monotonic()

    with manifest_path.open('w', encoding='utf-8', newline='') as manifest_file:
        writer = csv.DictWriter(manifest_file, fieldnames=fields, delimiter='\t')
        writer.writeheader()

        def handle_result(meta, data):
            nonlocal ok_count, fail_count
            if meta['status'] == 'ok' and data is not None:
                out_path = output_dir / meta['output']
                out_path.write_bytes(data)
                ok_count += 1
            else:
                fail_count += 1
            writer.writerow(meta)

        if jobs <= 1 or total_entries <= 1:
            _init_script_worker(str(npk_path), rotor_key)
            for i, idx in enumerate(indices, 1):
                meta, data = _process_script_entry(idx)
                handle_result(meta, data)
                if i % 1000 == 0 or i == total_entries:
                    elapsed = time.monotonic() - started_time
                    print(f"  -> 解包进度: {i}/{total_entries} (成功: {ok_count}, 失败: {fail_count}, 耗时: {elapsed:.1f}s)")
        else:
            with ProcessPoolExecutor(max_workers=jobs, initializer=_init_script_worker,
                                     initargs=(str(npk_path), rotor_key)) as executor:
                futures = {executor.submit(_process_script_entry, idx): idx for idx in indices}
                for i, fut in enumerate(as_completed(futures), 1):
                    meta, data = fut.result()
                    handle_result(meta, data)
                    if i % 1000 == 0 or i == total_entries:
                        elapsed = time.monotonic() - started_time
                        print(f"  -> 解包进度: {i}/{total_entries} (成功: {ok_count}, 失败: {fail_count}, 耗时: {elapsed:.1f}s)")

    elapsed = time.monotonic() - started_time
    print(f"[+] NpkDec 脚本包解包完成: 成功 {ok_count} 个, 失败 {fail_count} 个, 总耗时 {elapsed:.1f}s")
    print(f"[+] 清单记录文件: {manifest_path}")


def format_hint(data):
    """探测常见的 NeoX 资源格式文件头。"""
    if not data:
        return 'empty', '.bin'
    for magic, label, suffix in (
        (b'\xabKTX 11\xbb\r\n\x1a\n', 'KTX1', '.ktx'),
        (b'\x89PNG\r\n\x1a\n', 'PNG', '.png'),
        (b'NTRK', 'NTRK', '.bin'),
        (b'DDS ', 'DDS', '.dds'),
        (b'CCZ!', 'CCZ', '.ccz'),
        (b'CCZp', 'CCZ_ENC', '.ccz'),
    ):
        if data.startswith(magic):
            return label, suffix
    if len(data) >= 8 and data[4:8] == b'ftyp':
        return 'MP4', '.mp4'
    if data.startswith(b'RIFF'):
        return 'RIFF', '.riff'
    stripped = data[:128].lstrip(b'\xef\xbb\xbf \r\n\t')
    if stripped.startswith((b'<?xml', b'<')):
        return 'XML', '.xml'
    if stripped.startswith((b'{', b'[')):
        return 'JSON', '.json'
    return 'binary', '.bin'


def unpack_resource_npk(npk_path, output_dir, index_key=DEFAULT_AES_INDEX_KEY, decode_ccz_files=False, ccz_keys=None):
    """解包资源 NPK (AES-128-ECB 索引表 + LZ4/Raw 资源条目)。"""
    npk_path = Path(npk_path)
    pkg_name = npk_path.stem
    target_dir = Path(output_dir) / pkg_name
    target_dir.mkdir(parents=True, exist_ok=True)

    npk = Npk(npk_path, index_key=index_key)
    print(f"[*] NpkDec 开始解包资源包: {npk_path} (共 {len(npk.list)} 个条目)")

    manifest_path = target_dir / 'manifest.tsv'
    fields = ['index', 'id', 'offset', 'packed', 'raw', 'flags', 'format', 'output', 'status', 'error']

    ok_count = 0
    fail_count = 0

    with manifest_path.open('w', encoding='utf-8', newline='') as mf:
        writer = csv.DictWriter(mf, fieldnames=fields, delimiter='\t')
        writer.writeheader()

        for idx, entry in enumerate(npk.list):
            eid, offset, packed, raw_sz, cp, cr, flags = entry
            row = {
                'index': idx,
                'id': f'{eid:08x}',
                'offset': f'{offset:#x}',
                'packed': packed,
                'raw': raw_sz,
                'flags': f'{flags:#x}',
                'format': '',
                'output': '',
                'status': 'ok',
                'error': '',
            }
            try:
                raw_bytes = npk.raw(entry)
                if flags == 0:
                    payload = raw_bytes
                elif flags == 2:
                    if lz4 is None:
                        raise ImportError("解压此条目需要 lz4 库，请先执行 pip install lz4")
                    payload = lz4.block.decompress(raw_bytes, uncompressed_size=raw_sz)
                else:
                    raise ValueError(f"不支持的条目标志位: {flags:#x}")

                fmt, suffix = format_hint(payload)
                row['format'] = fmt

                if decode_ccz_files and fmt in ('CCZ', 'CCZ_ENC'):
                    try:
                        payload = decode_ccz(payload, keys=ccz_keys)
                        fmt = 'CCZ_DECODED'
                        suffix = '.bin'
                    except Exception as e:
                        row['error'] = f'CCZ 解密失败: {e}'

                out_filename = f'{eid:08x}{suffix}'
                (target_dir / out_filename).write_bytes(payload)
                row['output'] = out_filename
                ok_count += 1
            except Exception as e:
                row['status'] = 'failed'
                row['error'] = str(e)
                fail_count += 1

            writer.writerow(row)

    print(f"[+] NpkDec 资源包 {pkg_name} 解包完成: 成功 {ok_count} 个, 失败 {fail_count} 个。")


def is_script_npk(npk_path):
    """判断是否为脚本包 (script.npk)。"""
    path = Path(npk_path)
    if 'script' in path.name.lower():
        return True
    try:
        npk = Npk(path)
        return not npk.encrypted
    except Exception:
        return False


def main():
    parser = argparse.ArgumentParser(
        description="NeoX NPK 归档解包工具 (NpkDec - 仅解包，不修复脚本)",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
使用示例:
  # 解包脚本包 script.npk (生成原始 .nxc 文件):
  python unpack.py D:/assets/script.npk -o D:/Reverse2/unpack --jobs 4

  # 解包资源包 (解出各类多媒体与配置文件):
  python unpack.py D:/assets/res/sound.npk -o D:/Reverse2/res
        """
    )
    parser.add_argument('input', type=Path, help="输入的 NPK 文件路径，或包含多个 NPK 文件的目录")
    parser.add_argument('-o', '--out', type=Path, required=True, help="解包输出的目标目录")
    parser.add_argument('--type', choices=['auto', 'script', 'res'], default='auto', help="指定解包模式: auto(自动检测, 默认), script(强制按脚本解包), res(强制按资源解包)")
    parser.add_argument('-j', '--jobs', type=int, default=4, help="并发工作进程数 (默认: 4)")
    parser.add_argument('--limit', type=int, default=0, help="限制解包的条目数量 (默认 0 表示解包全部，仅调试使用)")
    parser.add_argument('--decode-ccz', action='store_true', help="如果遇到 CCZ 容器，自动尝试调用算法解密")
    parser.add_argument('--ccz-keys', nargs=4, type=lambda v: int(v, 0), help="解密 CCZp 加密容器所需的 4 个 32 位无符号整数密钥")

    args = parser.parse_args()

    input_path = args.input.resolve()
    if not input_path.exists():
        parser.error(f"输入路径不存在: {input_path}")

    files = []
    if input_path.is_dir():
        files = sorted(input_path.glob('*.npk'))
        if not files:
            parser.error(f"指定目录下未找到任何 .npk 文件: {input_path}")
    else:
        files = [input_path]

    for f in files:
        mode = args.type
        if mode == 'auto':
            mode = 'script' if is_script_npk(f) else 'res'

        if mode == 'script':
            unpack_script_npk(f, args.out, jobs=args.jobs, limit=args.limit)
        else:
            unpack_resource_npk(f, args.out, decode_ccz_files=args.decode_ccz, ccz_keys=args.ccz_keys)


if __name__ == '__main__':
    main()
