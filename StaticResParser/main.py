from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import tempfile
from pathlib import Path

try:
    from dbx import DbxPackage, is_catalog
except ModuleNotFoundError as error:
    raise SystemExit(f'缺少依赖 {error.name}，请运行：python -m pip install -r requirements.txt') from error

ROOT = Path(__file__).resolve().parent
PRODUCER = 'qrzd-dbx-json-v1'


def source_roots(input_path: Path, overlays: list[Path]) -> list[Path]:
    roots = [input_path]
    wrapped = [input_path / name for name in ('apk', 'hotfix') if (input_path / name).is_dir()]
    if wrapped:
        roots = wrapped
    roots.extend(overlays)
    result = []
    for root in roots:
        root = root.resolve()
        if not root.is_dir():
            raise ValueError(f'输入不是目录：{root}。此程序接收已解包文件，不接收 NPK 容器')
        if root in result:
            raise ValueError(f'重复输入层：{root}')
        result.append(root)
    return result


def to_relative_path(path: Path | str | None, base_input: Path, overlays: list[Path] | None = None) -> str | None:
    """将绝对路径转换为以 args.input 或 overlay 起始的相对路径表示。"""
    if path is None:
        return None
    p = Path(path).resolve()
    base_resolved = base_input.resolve()

    if base_input.is_absolute():
        try:
            base_display = base_input.resolve().relative_to(Path.cwd().resolve())
        except ValueError:
            base_display = Path(base_input.name)
    else:
        base_display = base_input

    if p.is_relative_to(base_resolved):
        rel = p.relative_to(base_resolved)
        return str(base_display if rel == Path('.') else base_display / rel)

    if overlays:
        for overlay in overlays:
            overlay_resolved = overlay.resolve()
            if p.is_relative_to(overlay_resolved):
                if overlay.is_absolute():
                    try:
                        overlay_display = overlay_resolved.relative_to(Path.cwd().resolve())
                    except ValueError:
                        overlay_display = Path(overlay.name)
                else:
                    overlay_display = overlay
                rel = p.relative_to(overlay_resolved)
                return str(overlay_display if rel == Path('.') else overlay_display / rel)

    try:
        return str(p.relative_to(Path.cwd().resolve()))
    except ValueError:
        return str(path)


def discover(roots: list[Path], base_input: Path = ROOT / 'input', overlays: list[Path] | None = None):
    candidates = []
    catalog_packages = set()
    skipped = []
    for root in roots:
        for directory, _, names in os.walk(root):
            parent = Path(directory)
            catalogs = [parent / name for name in names if is_catalog(Path(name))]
            has_resources = any(Path(name).suffix.casefold() in {'.dbx', '.dbxh', '.dbxcd'} or
                                (re.fullmatch(r'[0-9a-fA-F]{8}', Path(name).stem) and Path(name).suffix.casefold() not in {'.nxc', '.pyc'})
                                for name in names)
            if not catalogs and not has_resources:
                continue
            if len(catalogs) > 1:
                raise ValueError(f'同目录存在多个 DBX 清单：{parent}')
            package = parent.name
            if parent == root and package.casefold() not in {'dbx', 'model_dbx'}:
                package = 'dbx'
            candidates.append((root, package, parent))
            if catalogs:
                catalog_packages.add(package)
    groups: dict[str, list[Path]] = {}
    seen = set()
    for root, package, parent in candidates:
        if package in catalog_packages:
            identity = (root, package.casefold())
            if identity in seen:
                raise ValueError(f'同一输入层含多个 {package} 包；请用 --overlay 明确覆盖顺序')
            seen.add(identity)
            groups.setdefault(package, []).append(parent)
        else:
            skipped.append({'directory': to_relative_path(parent, base_input, overlays), 'reason': '没有 DBX 表名清单，无法可靠恢复逻辑名，未导出'})
    if not groups:
        raise ValueError('未找到 dbx_md5.json 或 4b1354f6.*。请把 NPK 解包结果放入 input，而不是 NPK 文件本身')
    if len({name.casefold() for name in groups}) != len(groups):
        raise ValueError('资源包名称大小写冲突')
    return [DbxPackage(name, paths) for name, paths in sorted(groups.items())], skipped


def checked_target(output: Path, relative: str) -> Path:
    target = output / relative
    if Path(relative).is_absolute() or not target.resolve().is_relative_to(output.resolve()) or target.resolve() == output.resolve():
        raise ValueError(f'输出路径越界：{relative}')
    # 不沿用链接目录，否则可能覆盖另一个资源或目录中的文件。
    for parent in (target, *target.parents):
        if parent == output.parent:
            break
        if parent.is_symlink() or (hasattr(parent, 'is_junction') and parent.is_junction()):
            raise ValueError(f'输出路径含符号链接或目录联接：{parent}')
    return target


def prepare_output(output: Path, roots: list[Path]) -> None:
    checked_target(output, 'report.json')
    for root in roots:
        if output.resolve().is_relative_to(root) or root.is_relative_to(output.resolve()):
            raise ValueError(f'输入与输出不可重叠：{root} / {output}')
    if output.exists() and not output.is_dir():
        raise ValueError(f'输出不是目录：{output}')
    if output.exists() and any(output.iterdir()):
        report_path = checked_target(output, 'report.json')
        manifest_path = checked_target(output, 'manifest.json')
        if not report_path.is_file() or not manifest_path.is_file():
            raise ValueError('输出目录含非本程序生成的文件，请用 --output 指定空目录')
        report = json.loads(report_path.read_bytes())
        manifest = json.loads(manifest_path.read_bytes())
        if report.get('producer') != PRODUCER or manifest.get('producer') != PRODUCER:
            raise ValueError('输出目录不属于本程序，拒绝清理')
        targets = []
        for item in manifest['files']:
            target = checked_target(output, item['output'])
            if target.is_file():
                if hashlib.sha256(target.read_bytes()).hexdigest() != item['sha256']:
                    raise ValueError(f'上次输出已被修改，保留该文件并停止：{target}')
                targets.append(target)
        # 只清理上次清单中有 SHA256 且内容未改的生成文件，不递归删除目录。
        for target in targets:
            target.unlink()
    output.mkdir(parents=True, exist_ok=True)


def write_json(output: Path, relative: str, value, replace: bool = False) -> dict:
    target = checked_target(output, relative)
    if target.exists() and not replace:
        raise ValueError(f'拒绝覆盖未由本轮管理的文件：{target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    data = (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n').encode('utf-8')
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='wb', dir=target.parent, prefix='.qrzd-', suffix='.tmp', delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data)
        os.replace(temporary, target)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()
    return {'output': relative, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)}


def export(packages: list[DbxPackage], output: Path, detailed: bool, skipped: list[dict], base_input: Path = ROOT / 'input', overlays: list[Path] | None = None) -> dict:
    manifest = {'producer': PRODUCER, 'files': [], 'tables': []}
    report = {'producer': PRODUCER, 'completed': False, 'packages': [], 'table_count': 0, 'exported_tables': 0, 'row_count': 0,
              'empty_tables': 0, 'failed_tables': 0, 'skipped': skipped}
    try:
        for package in packages:
            print(f'[资源包] {package.name}：{len(package.catalog)} 张表，{len(package.directories)} 个覆盖层', flush=True)
            manifest['files'].append(write_json(output, f'{package.name}/dbx_md5.json', package.catalog))
            report['packages'].append({'name': package.name,
                                       'layers': [to_relative_path(path, base_input, overlays) for path in package.directories],
                                       'catalog': to_relative_path(package.catalog_path, base_input, overlays),
                                       'table_count': len(package.catalog),
                                       'unknown_entry_ids': package.unknown_entries()})
            for index, table in enumerate(sorted(package.catalog), 1):
                item = {'resource': f'{package.name}/{table}.dbx', 'md5': package.catalog[table], 'status': 'failed'}
                report['table_count'] += 1
                try:
                    value, files = package.load_table(table)
                    record = write_json(output, f'{package.name}/{table}.json', value)
                    manifest['files'].append(record)
                    item.update(status='ok', output=record['output'], rows=len(value['rows']),
                                sources={f'{table}.{suffix}': to_relative_path(path, base_input, overlays) if path else None for suffix, path in files.items()})
                    report['exported_tables'] += 1
                    report['row_count'] += len(value['rows'])
                    report['empty_tables'] += not value['rows']
                    if detailed:
                        print(f'[导出] {record["output"]}：{len(value["rows"])} 行')
                except Exception as error:
                    item['error'] = f'{type(error).__name__}: {error}'
                    report['failed_tables'] += 1
                    print(f'[错误] {item["resource"]}：{error}', file=sys.stderr, flush=True)
                manifest['tables'].append(item)
                if index % 1000 == 0:
                    print(f'[进度] {package.name}：{index}/{len(package.catalog)}', flush=True)
        report['completed'] = report['failed_tables'] == 0
    finally:
        # 每次运行都写本轮清单，失败的表不伪装为成功，也不保留旧版同名结果。
        for relative, value in (('manifest.json', manifest), ('report.json', report)):
            write_json(output, relative, value, replace=True)
    return report


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description='恢复 NeoX 已解包 DBX 的逻辑资源名并导出 JSON')
    parser.add_argument('--input', type=Path, default=ROOT / 'input', help='已解包资源目录，默认程序所在目录的 input')
    parser.add_argument('--output', type=Path, default=ROOT / 'output', help='JSON 输出目录，默认程序所在目录的 output')
    parser.add_argument('--overlay', type=Path, action='append', default=[], help='额外覆盖层，可重复，后指定的优先')
    parser.add_argument('--detailed-log', action='store_true', help='逐表输出写入日志')
    args = parser.parse_args(argv)
    try:
        roots = source_roots(args.input, args.overlay)
        output = args.output.absolute()
        for root in roots:
            print(f'[输入层] {root}', flush=True)
        packages, skipped = discover(roots, base_input=args.input, overlays=args.overlay)
        prepare_output(output, [args.input.resolve(), *roots])
        for item in skipped:
            print(f'[跳过] {item["directory"]}：{item["reason"]}', flush=True)
        report = export(packages, output, args.detailed_log, skipped, base_input=args.input, overlays=args.overlay)
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(f'[失败] {error}', file=sys.stderr)
        return 1
    print(f'[完成] 成功 {report["exported_tables"]} 张，失败 {report["failed_tables"]} 张，共 {report["row_count"]} 行；输出：{output}', flush=True)
    return 1 if report['failed_tables'] else 0


if __name__ == '__main__':
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8', errors='backslashreplace')
    raise SystemExit(main())
