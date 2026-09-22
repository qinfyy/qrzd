"""从同版本 Android x86_64 Frida Server 提取 ARM64 Emulated Realm agent。"""

from __future__ import annotations

import argparse
import lzma
import re
import shutil
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

import frida
import requests

PROJECT_DIRECTORY = Path(__file__).resolve().parent
RELEASE_URL_TEMPLATE = (
    "https://github.com/frida/frida/releases/download/{version}/"
    "frida-server-{version}-android-x86_64.xz"
)
ELF_MAGIC = b"\x7fELF"
ELF_CLASS_64 = 2
ELF_DATA_LSB = 1
EM_AARCH64 = 183
ET_DYN = 3
PT_LOAD = 1
MINIMUM_AGENT_SIZE = 1024 * 1024


@dataclass(frozen=True)
class EmbeddedElf:
    """嵌入在 Frida Server 中的完整 ELF 文件范围。"""

    offset: int
    size: int


def parse_arguments() -> argparse.Namespace:
    """解析提取工具参数。"""
    parser = argparse.ArgumentParser(
        description="从同版本 Android x86_64 Frida Server 提取 ARM64 agent",
    )
    parser.add_argument(
        "--server",
        type=Path,
        help="已下载的 Frida Server 或 .xz 压缩包；未指定时复用或下载子项目根目录中的压缩包",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="提取结果路径；未指定时写入子项目根目录的 frida-agent-arm64.so",
    )
    return parser.parse_args()


def download_server(version: str, destination: Path) -> None:
    """下载并解压与本机 Python Frida 完全相同版本的 Android x86_64 Server。"""
    url = RELEASE_URL_TEMPLATE.format(version=version)
    archive_path = destination.with_name(destination.name + ".xz.part")
    temporary_path = destination.with_name(destination.name + ".part")
    destination.parent.mkdir(parents=True, exist_ok=True)
    print(f"正在下载 Frida Server {version}...", flush=True)
    try:
        with requests.get(url, stream=True, timeout=60) as response:
            response.raise_for_status()
            with archive_path.open("wb") as output:
                for chunk in response.iter_content(chunk_size=1024 * 1024):
                    if not chunk:
                        continue
                    output.write(chunk)

        with lzma.open(archive_path, "rb") as archive:
            with temporary_path.open("wb") as output:
                shutil.copyfileobj(archive, output, length=1024 * 1024)
        temporary_path.replace(destination)
    except (OSError, lzma.LZMAError, requests.RequestException) as error:
        temporary_path.unlink(missing_ok=True)
        raise RuntimeError(f"下载 Frida Server 失败：{error}，地址：{url}") from error
    finally:
        archive_path.unlink(missing_ok=True)


def read_server_binary(source_path: Path) -> bytes:
    """读取原始 Server，或解压官方提供的 .xz 包。"""
    if not source_path.is_file():
        raise FileNotFoundError(f"找不到 Frida Server 文件：{source_path}")

    if source_path.suffix.lower() == ".xz":
        try:
            with lzma.open(source_path, "rb") as archive:
                return archive.read()
        except lzma.LZMAError as error:
            raise RuntimeError(f"无法解压 Frida Server：{source_path}") from error
    return source_path.read_bytes()


def find_arm64_agent(server: bytes) -> EmbeddedElf:
    """寻找 Server 内嵌的唯一 ARM64 注入 agent。"""
    candidates: list[EmbeddedElf] = []
    for match in re.finditer(re.escape(ELF_MAGIC), server):
        candidate = parse_arm64_shared_object(server, match.start())
        if candidate is not None and candidate.size >= MINIMUM_AGENT_SIZE:
            candidates.append(candidate)

    if len(candidates) != 1:
        details = ", ".join(
            f"偏移 {candidate.offset:#x}，大小 {candidate.size}" for candidate in candidates
        )
        raise RuntimeError(
            "未能唯一识别 Frida Server 内嵌的 ARM64 agent"
            + (f"：{details}" if details else "")
        )
    return candidates[0]


def parse_arm64_shared_object(server: bytes, offset: int) -> EmbeddedElf | None:
    """验证 offset 是否为完整的 ARM64 ET_DYN ELF，并计算文件长度。"""
    elf_header_format = "<16sHHIQQQIHHHHHH"
    program_header_format = "<IIQQQQQQ"
    elf_header_size = struct.calcsize(elf_header_format)
    program_header_size = struct.calcsize(program_header_format)

    if offset + elf_header_size > len(server):
        return None
    header = struct.unpack_from(elf_header_format, server, offset)
    ident, elf_type, machine, _, _, program_offset, section_offset, _, _, program_size, program_count, section_size, section_count, _ = header
    if (
        ident[:4] != ELF_MAGIC
        or ident[4] != ELF_CLASS_64
        or ident[5] != ELF_DATA_LSB
        or elf_type != ET_DYN
        or machine != EM_AARCH64
        or program_size < program_header_size
    ):
        return None

    program_table_end = program_offset + program_size * program_count
    if offset + program_table_end > len(server):
        return None

    load_end = 0
    for index in range(program_count):
        program_header_offset = offset + program_offset + index * program_size
        program_type, _, file_offset, _, _, file_size, _, _ = struct.unpack_from(
            program_header_format,
            server,
            program_header_offset,
        )
        if program_type == PT_LOAD:
            load_end = max(load_end, file_offset + file_size)

    section_end = section_offset + section_size * section_count
    size = max(load_end, section_end)
    if size == 0 or offset + size > len(server):
        return None
    return EmbeddedElf(offset=offset, size=size)


def main() -> int:
    """下载或读取 Server，并输出可由 Native Bridge 加载的 raw agent。"""
    arguments = parse_arguments()
    version = frida.__version__
    default_server_path = PROJECT_DIRECTORY / (
        f"frida-server-{version}-android-x86_64"
    )
    legacy_archive_path = default_server_path.with_name(default_server_path.name + ".xz")
    output_path = (
        arguments.output.resolve()
        if arguments.output is not None
        else PROJECT_DIRECTORY / "frida-agent-arm64.so"
    )
    if arguments.server is None:
        if default_server_path.is_file():
            print(f"复用已下载的 Frida Server：{default_server_path}", flush=True)
        elif legacy_archive_path.is_file():
            print(f"正在将压缩包解压为 Frida Server：{legacy_archive_path}", flush=True)
            try:
                with lzma.open(legacy_archive_path, "rb") as archive:
                    with default_server_path.open("wb") as output:
                        shutil.copyfileobj(archive, output, length=1024 * 1024)
            except (OSError, lzma.LZMAError) as error:
                default_server_path.unlink(missing_ok=True)
                raise RuntimeError(f"无法解压 Frida Server：{legacy_archive_path}") from error
            legacy_archive_path.unlink()
        else:
            download_server(version, default_server_path)
        server = read_server_binary(default_server_path)
    else:
        server = read_server_binary(arguments.server.resolve())

    agent = find_arm64_agent(server)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(server[agent.offset : agent.offset + agent.size])
    print(
        f"已提取 ARM64 Frida agent：{output_path}"
        f"（Server 偏移 {agent.offset:#x}，大小 {agent.size}）",
        flush=True,
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, ValueError) as error:
        print(f"提取失败：{error}", file=sys.stderr)
        sys.exit(1)
