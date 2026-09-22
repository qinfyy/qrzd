"""
NeoXDec.PyDec 脚本字节码修复与路径恢复工具。

功能：
- 读取 NpkDec 解包出的原始 .nxc 裸脚本文件；
- 反序列化 NeoX CodeObject，提取内部固化的真实路径 co.filename 并恢复完整的项目目录树；
- 还原乱序混淆的 Opcode，展开 NeoX 专有融合指令 173 (FUSED_LOAD_FAST_CONST)；
- 迭代重定位绝对与相对跳转目标，修正行号表 lnotab；
- 封装标准 Python 2.7 文件头 (Magic 0x0AF303 + 时间戳)，写出标准合法 .pyc 文件。
"""

import argparse
from concurrent.futures import ProcessPoolExecutor, as_completed
import csv
import hashlib
from pathlib import Path
import struct
import sys
import time

try:
    from .nxmarshal import Reader as NxReader, dump as nxdump, Code as NxCode, PY27_MAGIC
    from .nxdis import fix_opcodes, relocate_lnotab
except ImportError:
    from nxmarshal import Reader as NxReader, dump as nxdump, Code as NxCode, PY27_MAGIC
    from nxdis import fix_opcodes, relocate_lnotab


def fix_code_tree(co):
    """递归反混淆代码对象树中的全部指令、融合指令与跳转表。"""
    if not isinstance(co.code, bytes):
        co.code = bytes(co.code)
    co.code, _, mapping = fix_opcodes(co.code, strict=True, return_mapping=True)
    if co.lnotab:
        co.lnotab = relocate_lnotab(co.lnotab, mapping)
    for c in (co.consts or ()):
        if isinstance(c, NxCode):
            fix_code_tree(c)
    return co


def safe_relative_path(filename, default_stem, suffix='.pyc'):
    """清洗内部代码对象的 co.filename，转换为合法的工程相对路径。"""
    if isinstance(filename, bytes):
        name = filename.decode('utf-8', 'replace')
    else:
        name = str(filename or '')
    name = name.replace('\\', '/')
    parts = [p for p in name.split('/') if p not in ('', '.', '..') and ':' not in p]
    if not parts:
        parts = [f'{default_stem}{suffix}']
    path = Path(*parts)
    if path.suffix == '.py':
        return path.with_suffix(suffix)
    return Path(str(path) + suffix)


def process_file(nxc_path, by_id=False):
    """处理单个 .nxc 文件，提取路径并返回修复后的标准 .pyc 二进制。"""
    nxc_path = Path(nxc_path)
    meta = {
        'input': nxc_path.name,
        'filename': '',
        'output': '',
        'pyc_sha256': '',
        'status': 'ok',
        'error': '',
    }
    try:
        data = nxc_path.read_bytes()
        reader = NxReader(data)
        co = reader.obj()
        if not isinstance(co, NxCode) or reader.i != len(data):
            raise ValueError(f'Marshal 边界校验不匹配: {reader.i}/{len(data)}')

        if isinstance(co.filename, bytes):
            co_fn = co.filename.decode('utf-8', 'replace')
        else:
            co_fn = str(co.filename or '')
        meta['filename'] = co_fn

        if by_id or not co_fn:
            rel = Path(nxc_path.stem + '.pyc')
        else:
            rel = safe_relative_path(co_fn, nxc_path.stem, suffix='.pyc')

        fix_code_tree(co)
        pyc_bytes = PY27_MAGIC + struct.pack('<I', 0) + nxdump(co)

        meta['output'] = rel.as_posix()
        meta['pyc_sha256'] = hashlib.sha256(pyc_bytes).hexdigest()
        return meta, pyc_bytes
    except Exception as exc:
        meta['status'] = 'failed'
        meta['error'] = f'{type(exc).__name__}: {exc}'
        return meta, None


def fix_batch(input_path, output_dir, jobs=4, limit=0, by_id=False):
    """批量修复 .nxc 裸文件并恢复完整目录树输出为 .pyc。"""
    input_path = Path(input_path).resolve()
    output_dir = Path(output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    if input_path.is_file():
        files = [input_path]
    else:
        files = sorted(input_path.glob('*.nxc'))
        if not files:
            files = sorted(input_path.rglob('*.nxc'))
        if not files:
            files = sorted(input_path.rglob('*.raw')) + sorted(input_path.rglob('*.bin'))

    if limit and limit > 0:
        files = files[:limit]

    total = len(files)
    print(f"[*] PyDec 开始修复脚本并恢复路径: 共 {total} 个文件, {jobs} 个进程并发")

    manifest_path = output_dir / 'manifest.tsv'
    fields = ['input', 'filename', 'output', 'pyc_sha256', 'status', 'error']

    ok_count = 0
    fail_count = 0
    started_time = time.monotonic()

    with manifest_path.open('w', encoding='utf-8', newline='') as mf:
        writer = csv.DictWriter(mf, fieldnames=fields, delimiter='\t')
        writer.writeheader()

        def handle_result(meta, pyc_bytes):
            nonlocal ok_count, fail_count
            if meta['status'] == 'ok' and pyc_bytes is not None:
                out_file = output_dir / meta['output']
                out_file.parent.mkdir(parents=True, exist_ok=True)
                out_file.write_bytes(pyc_bytes)
                ok_count += 1
            else:
                fail_count += 1
            writer.writerow(meta)

        if jobs <= 1 or total <= 1:
            for idx, f in enumerate(files, 1):
                meta, pyc_bytes = process_file(f, by_id=by_id)
                handle_result(meta, pyc_bytes)
                if idx % 1000 == 0 or idx == total:
                    elapsed = time.monotonic() - started_time
                    print(f"  -> 修复进度: {idx}/{total} (成功: {ok_count}, 失败: {fail_count}, 耗时: {elapsed:.1f}s)")
        else:
            with ProcessPoolExecutor(max_workers=jobs) as executor:
                futures = {executor.submit(process_file, f, by_id): f for f in files}
                for idx, fut in enumerate(as_completed(futures), 1):
                    meta, pyc_bytes = fut.result()
                    handle_result(meta, pyc_bytes)
                    if idx % 1000 == 0 or idx == total:
                        elapsed = time.monotonic() - started_time
                        print(f"  -> 修复进度: {idx}/{total} (成功: {ok_count}, 失败: {fail_count}, 耗时: {elapsed:.1f}s)")

    elapsed = time.monotonic() - started_time
    print(f"[+] PyDec 脚本修复与路径恢复完成: 成功 {ok_count} 个, 失败 {fail_count} 个, 耗时 {elapsed:.1f}s")
    print(f"[+] pyc 输出目录: {output_dir}")
    print(f"[+] 清单文件: {manifest_path}")


def main():
    parser = argparse.ArgumentParser(
        description="NeoX 脚本字节码修复与 Pyc 重建工具 (PyDec - 负责脚本修复与路径恢复)",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument('input', type=Path, help="输入的 .nxc 文件路径，或包含 .nxc 文件的目录")
    parser.add_argument('-o', '--out', type=Path, required=True, help="修复后 .pyc 文件的输出目标目录")
    parser.add_argument('-j', '--jobs', type=int, default=4, help="并发工作进程数 (默认: 4)")
    parser.add_argument('--by-id', action='store_true', help="强制按输入文件名命名，不恢复内部工程路径 (默认关闭，自动按 co.filename 恢复目录树)")
    parser.add_argument('--limit', type=int, default=0, help="限制处理的文件数量 (默认 0 表示处理全部，仅调试使用)")

    args = parser.parse_args()
    fix_batch(
        input_path=args.input,
        output_dir=args.out,
        jobs=args.jobs,
        limit=args.limit,
        by_id=args.by_id,
    )


if __name__ == '__main__':
    main()
