"""
NeoXDec.PyDecompiler 命令行反编译工具。

功能：
- 接收 PyDec 修复后的标准 Python 2.7 .pyc 文件；
- 优先采用高速轻量级 Data Lifter 流式提取纯配置/数据表模块（耗时 1~5s，内存 < 50MB）；
- 复杂逻辑代码无缝回退至 uncompyle6 3.9.3 与特化 AST/控制流适配器；
- 支持超大型模块 AST 安全语句分块反编译 (--chunked)；
- 支持断点续跑 (--resume)，跳过已成功生成的 .py 文件；
- ProcessPoolExecutor 子进程定期回收 (max_tasks_per_child=50)，从根本上杜绝内存堆积泄漏；
- 滑动窗口任务提交与异常断池自我恢复机制 (BrokenProcessPool Resilience)；
- 全 Python 3 原生运行，全中文日志输出。
"""

import argparse
from concurrent.futures import FIRST_COMPLETED, ProcessPoolExecutor, wait
import contextlib
import csv
import io
from pathlib import Path
import sys
import time

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

try:
    from .data_lifter import Unsupported, try_lift_data
except ImportError:
    from data_lifter import Unsupported, try_lift_data

try:
    from .adapters.control_flow import install as install_control_flow
    from .adapters.literals import install as install_literals
    from .adapters.chunked import install as install_chunked
except ImportError:
    from adapters.control_flow import install as install_control_flow
    from adapters.literals import install as install_literals
    from adapters.chunked import install as install_chunked

import uncompyle6
from uncompyle6.main import decompile_file


def init_adapters(chunked=False):
    """安装 NeoX 控制流与字面量适配器。"""
    install_control_flow()
    install_literals()
    if chunked:
        install_chunked()


_WORKER_INITIALIZED = False


def _init_decompile_worker(chunked):
    global _WORKER_INITIALIZED
    if not _WORKER_INITIALIZED:
        init_adapters(chunked=chunked)
        _WORKER_INITIALIZED = True


def decompile_single_file(pyc_path: Path, target_path: Path) -> int:
    """反编译单个 .pyc 文件至 target_path。

    策略：
    1. 优先使用纯数据模块提取器 (Data Lifter)，秒级提取超大数据表且几乎不占内存；
    2. 若包含函数/类/复杂控制流则抛出 Unsupported，无缝回退至 uncompyle6。
    """
    try:
        chars = try_lift_data(pyc_path, target_path)
        if chars > 0:
            return chars
    except Unsupported:
        pass
    except Exception:
        pass

    output_stream = io.StringIO()
    diagnostic_stream = io.StringIO()

    with contextlib.redirect_stdout(diagnostic_stream), contextlib.redirect_stderr(diagnostic_stream):
        decompile_file(str(pyc_path), output_stream)

    text = output_stream.getvalue()
    if not text.strip():
        diag = diagnostic_stream.getvalue().strip()
        raise ValueError(f"反编译输出为空，诊断信息: {diag[:300]}")

    target_path.parent.mkdir(parents=True, exist_ok=True)
    header = "# -*- coding: utf-8 -*-\n"
    if not text.startswith(header):
        text = header + text
    target_path.write_text(text, encoding='utf-8')
    return len(text)


def process_entry(args_tuple):
    """处理单个反编译任务（供多进程调用）。"""
    pyc_path_str, base_input_dir_str, output_dir_str, resume = args_tuple
    pyc_path = Path(pyc_path_str)
    base_input_dir = Path(base_input_dir_str)
    output_dir = Path(output_dir_str)

    try:
        rel = pyc_path.relative_to(base_input_dir)
    except ValueError:
        rel = Path(pyc_path.name)

    target_file = output_dir / rel.with_suffix('.py')
    row = {
        'file': rel.as_posix(),
        'output': target_file.as_posix(),
        'status': 'ok',
        'chars': 0,
        'error': '',
    }

    if resume and target_file.exists() and target_file.stat().st_size > 0:
        row['status'] = 'skipped'
        row['chars'] = target_file.stat().st_size
        return row

    try:
        chars = decompile_single_file(pyc_path, target_file)
        row['chars'] = chars
    except Exception as exc:
        row['status'] = 'failed'
        row['error'] = f'{type(exc).__name__}: {exc}'

    return row


def decompile_batch(input_path, output_dir, jobs=4, chunked=False, resume=False, limit=0):
    """批量反编译指定目录下的所有 .pyc 文件。"""
    input_path = Path(input_path).resolve()
    output_dir = Path(output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    if input_path.is_file():
        pyc_files = [input_path]
        base_dir = input_path.parent
    else:
        base_dir = input_path
        pyc_files = sorted(input_path.rglob('*.pyc'))

    if limit and limit > 0:
        pyc_files = pyc_files[:limit]

    total = len(pyc_files)
    print(f"[*] PyDecompiler 开始反编译: 共 {total} 个文件, {jobs} 进程并发 (分块模式: {chunked}, 断点续跑: {resume})")

    manifest_path = output_dir / 'manifest.tsv'
    fields = ['file', 'output', 'status', 'chars', 'error']

    ok_count = 0
    fail_count = 0
    skip_count = 0
    processed_count = 0
    started_time = time.monotonic()

    tasks = [(str(p), str(base_dir), str(output_dir), resume) for p in pyc_files]

    with manifest_path.open('w', encoding='utf-8', newline='') as mf:
        writer = csv.DictWriter(mf, fieldnames=fields, delimiter='\t')
        writer.writeheader()

        if jobs <= 1 or total <= 1:
            init_adapters(chunked=chunked)
            for idx, task in enumerate(tasks, 1):
                row = process_entry(task)
                if row['status'] == 'ok':
                    ok_count += 1
                elif row['status'] == 'skipped':
                    skip_count += 1
                else:
                    fail_count += 1
                writer.writerow(row)
                processed_count = idx
                if idx % 500 == 0 or idx == total:
                    elapsed = time.monotonic() - started_time
                    print(f"  -> 反编译进度: {idx}/{total} (成功: {ok_count}, 跳过: {skip_count}, 失败: {fail_count}, 耗时: {elapsed:.1f}s)")
        else:
            # 滑动窗口任务队列 + 子进程定期回收 (max_tasks_per_child=50) + 异常断池自动重连
            pending_queue = list(tasks)
            max_in_flight = jobs * 4

            while pending_queue:
                try:
                    with ProcessPoolExecutor(
                        max_workers=jobs,
                        max_tasks_per_child=50,
                        initializer=_init_decompile_worker,
                        initargs=(chunked,)
                    ) as executor:
                        active_futures = {}

                        while pending_queue or active_futures:
                            # 填充飞行中任务至上限
                            while pending_queue and len(active_futures) < max_in_flight:
                                next_task = pending_queue.pop(0)
                                fut = executor.submit(process_entry, next_task)
                                active_futures[fut] = next_task

                            if not active_futures:
                                break

                            # 等待至少一个任务完成
                            done, _ = wait(active_futures.keys(), return_when=FIRST_COMPLETED)

                            for fut in done:
                                original_task = active_futures.pop(fut)
                                row = fut.result()
                                if row['status'] == 'ok':
                                    ok_count += 1
                                elif row['status'] == 'skipped':
                                    skip_count += 1
                                else:
                                    fail_count += 1

                                writer.writerow(row)
                                mf.flush()
                                processed_count += 1

                                if processed_count % 500 == 0 or processed_count == total:
                                    elapsed = time.monotonic() - started_time
                                    print(f"  -> 反编译进度: {processed_count}/{total} (成功: {ok_count}, 跳过: {skip_count}, 失败: {fail_count}, 耗时: {elapsed:.1f}s)")

                except Exception as pool_err:
                    print(f"[!] 进程池检测到异常或子进程崩溃: {pool_err}，正在自动重启工作池并继续...")
                    # 如果有未完成的 active_futures，将其放回待处理队列重试
                    if 'active_futures' in locals():
                        for leftover_fut, leftover_task in active_futures.items():
                            pending_queue.insert(0, leftover_task)

    elapsed = time.monotonic() - started_time
    print(f"[+] PyDecompiler 反编译完成: 成功 {ok_count} 个, 跳过 {skip_count} 个, 失败 {fail_count} 个, 耗时 {elapsed:.1f}s")
    print(f"[+] 输出源码目录: {output_dir}")
    print(f"[+] 清单记录文件: {manifest_path}")


def main():
    parser = argparse.ArgumentParser(
        description="NeoX Python 字节码反编译器 (PyDecompiler - 负责反编译修复后的 Pyc)",
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument('input', type=Path, help="输入的 .pyc 文件路径，或包含 .pyc 文件的根目录")
    parser.add_argument('-o', '--out', type=Path, required=True, help="反编译输出 .py 源码的目标目录")
    parser.add_argument('-j', '--jobs', type=int, default=4, help="并发工作进程数 (默认: 4)")
    parser.add_argument('--chunked', action='store_true', help="启用 AST 安全语句分块反编译 (面向超大型复杂模块如 CEGUI)")
    parser.add_argument('--resume', action='store_true', help="断点续跑，跳过已经成功反编译且存在的目标文件")
    parser.add_argument('--limit', type=int, default=0, help="限制反编译的文件数量 (默认 0 表示反编译全部，仅调试使用)")

    args = parser.parse_args()
    decompile_batch(
        input_path=args.input,
        output_dir=args.out,
        jobs=args.jobs,
        chunked=args.chunked,
        resume=args.resume,
        limit=args.limit,
    )


if __name__ == '__main__':
    main()
