import argparse
import ast
from pathlib import Path
import re
import sys
from typing import Optional

try:
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if hasattr(sys.stderr, 'reconfigure'):
        sys.stderr.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

try:
    from google.protobuf import descriptor_pb2
except ImportError:
    raise ImportError("需要 google.protobuf 库，请先执行: pip install protobuf")



FIELD_TYPE_NAMES = {
    descriptor_pb2.FieldDescriptorProto.TYPE_DOUBLE: "double",
    descriptor_pb2.FieldDescriptorProto.TYPE_FLOAT: "float",
    descriptor_pb2.FieldDescriptorProto.TYPE_INT64: "int64",
    descriptor_pb2.FieldDescriptorProto.TYPE_UINT64: "uint64",
    descriptor_pb2.FieldDescriptorProto.TYPE_INT32: "int32",
    descriptor_pb2.FieldDescriptorProto.TYPE_FIXED64: "fixed64",
    descriptor_pb2.FieldDescriptorProto.TYPE_FIXED32: "fixed32",
    descriptor_pb2.FieldDescriptorProto.TYPE_BOOL: "bool",
    descriptor_pb2.FieldDescriptorProto.TYPE_STRING: "string",
    descriptor_pb2.FieldDescriptorProto.TYPE_GROUP: "group",
    descriptor_pb2.FieldDescriptorProto.TYPE_MESSAGE: "message",
    descriptor_pb2.FieldDescriptorProto.TYPE_BYTES: "bytes",
    descriptor_pb2.FieldDescriptorProto.TYPE_UINT32: "uint32",
    descriptor_pb2.FieldDescriptorProto.TYPE_ENUM: "enum",
    descriptor_pb2.FieldDescriptorProto.TYPE_SFIXED32: "sfixed32",
    descriptor_pb2.FieldDescriptorProto.TYPE_SFIXED64: "sfixed64",
    descriptor_pb2.FieldDescriptorProto.TYPE_SINT32: "sint32",
    descriptor_pb2.FieldDescriptorProto.TYPE_SINT64: "sint64",
}


def escape_default_string(s: str) -> str:
    """转义 proto 字符串默认值。"""
    res = []
    for ch in s:
        if ch == '\\':
            res.append('\\\\')
        elif ch == '"':
            res.append('\\"')
        elif ch == '\n':
            res.append('\\n')
        elif ch == '\r':
            res.append('\\r')
        elif ch == '\t':
            res.append('\\t')
        elif ord(ch) < 32 or ord(ch) >= 127:
            res.append(f'\\x{ord(ch):02x}')
        else:
            res.append(ch)
    return ''.join(res)


def format_type_name(full_name: str, package: str, enclosing_messages: list[str]) -> str:
    """将全局全限定类型名转换为当前上下文最合适的简写或相对名称。"""
    if not full_name:
        return ""
    if not full_name.startswith('.'):
        return full_name

    pkg_prefix = f".{package}." if package else "."
    if full_name.startswith(pkg_prefix):
        rel_to_pkg = full_name[len(pkg_prefix):]
        enclosing_path = ".".join(enclosing_messages)
        if enclosing_path and rel_to_pkg.startswith(enclosing_path + "."):
            return rel_to_pkg[len(enclosing_path) + 1:]
        return rel_to_pkg

    return full_name.lstrip('.')


class ProtoGenerator:
    """基于 FileDescriptorProto 反构 .proto 源码生成器。"""

    def __init__(self, fd: descriptor_pb2.FileDescriptorProto):
        self.fd = fd
        self.package = fd.package or ""
        self.is_proto3 = (fd.syntax == "proto3")

    def generate(self) -> str:
        lines: list[str] = []

        # 1. 语法头
        syntax_str = "proto3" if self.is_proto3 else "proto2"
        lines.append(f'syntax = "{syntax_str}";')
        lines.append("")

        # 2. 包名
        if self.package:
            lines.append(f"package {self.package};")
            lines.append("")

        # 3. 依赖项 (Imports)
        has_imports = False
        public_deps = set(self.fd.public_dependency)
        weak_deps = set(self.fd.weak_dependency)
        for idx, dep in enumerate(self.fd.dependency):
            has_imports = True
            if idx in public_deps:
                lines.append(f'import public "{dep}";')
            elif idx in weak_deps:
                lines.append(f'import weak "{dep}";')
            else:
                lines.append(f'import "{dep}";')
        if has_imports:
            lines.append("")

        # 4. 文件选项 (File Options)
        has_options = False
        if self.fd.options:
            opts = self.fd.options
            if opts.HasField("csharp_namespace"):
                lines.append(f'option csharp_namespace = "{opts.csharp_namespace}";')
                has_options = True
            if opts.HasField("java_package"):
                lines.append(f'option java_package = "{opts.java_package}";')
                has_options = True
            if opts.HasField("go_package"):
                lines.append(f'option go_package = "{opts.go_package}";')
                has_options = True
        if has_options:
            lines.append("")

        # 5. 顶层枚举 (Top-level Enums)
        for enum in self.fd.enum_type:
            self._write_enum(enum, lines, 0)

        # 6. 顶层消息 (Top-level Messages)
        for msg in self.fd.message_type:
            self._write_message(msg, lines, 0, [])

        # 7. 顶层扩展 (Top-level Extensions)
        if self.fd.extension:
            self._write_extensions(self.fd.extension, lines, 0, [])

        # 8. 服务与 RPC (Services)
        for service in self.fd.service:
            self._write_service(service, lines, 0)

        return "\n".join(lines).rstrip() + "\n"

    def _write_enum(self, enum_desc: descriptor_pb2.EnumDescriptorProto, lines: list[str], indent_level: int):
        indent = "    " * indent_level
        lines.append(f"{indent}enum {enum_desc.name} {{")
        for val in enum_desc.value:
            lines.append(f"{indent}    {val.name} = {val.number};")
        lines.append(f"{indent}}}")
        lines.append("")

    def _write_message(self, msg_desc: descriptor_pb2.DescriptorProto, lines: list[str], indent_level: int, enclosing: list[str]):
        indent = "    " * indent_level
        current_enclosing = enclosing + [msg_desc.name]
        lines.append(f"{indent}message {msg_desc.name} {{")

        # 内部嵌套枚举
        for enum in msg_desc.enum_type:
            self._write_enum(enum, lines, indent_level + 1)

        # 内部嵌套消息 (排除 map_entry 内部伪消息)
        map_entries = {n.name: n for n in msg_desc.nested_type if n.options and n.options.map_entry}
        for nested in msg_desc.nested_type:
            if nested.name not in map_entries:
                self._write_message(nested, lines, indent_level + 1, current_enclosing)

        # 映射字段 (Map fields)
        map_fields = set()
        for field in msg_desc.field:
            entry_name = field.type_name.split('.')[-1]
            if entry_name in map_entries and field.type == descriptor_pb2.FieldDescriptorProto.TYPE_MESSAGE:
                map_entry = map_entries[entry_name]
                key_type = self._get_field_type_string(map_entry.field[0], current_enclosing)
                val_type = self._get_field_type_string(map_entry.field[1], current_enclosing)
                lines.append(f"{indent}    map<{key_type}, {val_type}> {field.name} = {field.number};")
                map_fields.add(field.number)

        # 聚合 Oneof 组 (排除 proto3 合成 oneof)
        oneof_groups: dict[int, list[descriptor_pb2.FieldDescriptorProto]] = {}
        normal_fields: list[descriptor_pb2.FieldDescriptorProto] = []

        for field in msg_desc.field:
            if field.number in map_fields:
                continue
            if field.HasField("oneof_index"):
                # proto3 纯 optional 字段会带合成 oneof，在 proto3 语法下不当作显式 oneof 块输出
                if self.is_proto3 and field.proto3_optional:
                    normal_fields.append(field)
                else:
                    oneof_groups.setdefault(field.oneof_index, []).append(field)
            else:
                normal_fields.append(field)

        # 渲染常规字段
        for field in normal_fields:
            line_str = self._format_field(field, current_enclosing, is_oneof=False)
            lines.append(f"{indent}    {line_str}")

        # 渲染 Oneof 块
        for oneof_idx, fields in oneof_groups.items():
            if oneof_idx < len(msg_desc.oneof_decl):
                oneof_name = msg_desc.oneof_decl[oneof_idx].name
                lines.append(f"{indent}    oneof {oneof_name} {{")
                for field in fields:
                    field_str = self._format_field(field, current_enclosing, is_oneof=True)
                    lines.append(f"{indent}        {field_str}")
                lines.append(f"{indent}    }}")

        # 扩展范围 (Extension ranges)
        for r in msg_desc.extension_range:
            end_val = "max" if r.end >= 536870912 or r.end >= 0x20000000 else str(r.end - 1)
            lines.append(f"{indent}    extensions {r.start} to {end_val};")

        # 消息内部扩展 (Nested extensions)
        if msg_desc.extension:
            self._write_extensions(msg_desc.extension, lines, indent_level + 1, current_enclosing)

        lines.append(f"{indent}}}")
        lines.append("")

    def _format_field(self, field: descriptor_pb2.FieldDescriptorProto, enclosing: list[str], is_oneof: bool) -> str:
        parts: list[str] = []

        # 字段标签 (Label)
        if not is_oneof:
            if self.is_proto3:
                if field.label == descriptor_pb2.FieldDescriptorProto.LABEL_REPEATED:
                    parts.append("repeated")
                elif field.proto3_optional:
                    parts.append("optional")
            else:
                if field.label == descriptor_pb2.FieldDescriptorProto.LABEL_REQUIRED:
                    parts.append("required")
                elif field.label == descriptor_pb2.FieldDescriptorProto.LABEL_REPEATED:
                    parts.append("repeated")
                else:
                    parts.append("optional")

        # 字段类型
        parts.append(self._get_field_type_string(field, enclosing))

        # 字段名与标号
        parts.append(f"{field.name} = {field.number}")

        # 字段选项 (默认值等)
        options: list[str] = []
        if field.HasField("default_value"):
            dflt = field.default_value
            if field.type in (descriptor_pb2.FieldDescriptorProto.TYPE_STRING, descriptor_pb2.FieldDescriptorProto.TYPE_BYTES):
                options.append(f'default = "{escape_default_string(dflt)}"')
            elif field.type == descriptor_pb2.FieldDescriptorProto.TYPE_BOOL:
                options.append(f'default = {dflt.lower()}')
            else:
                options.append(f'default = {dflt}')

        if options:
            return " ".join(parts) + f" [{', '.join(options)}];"
        return " ".join(parts) + ";"

    def _get_field_type_string(self, field: descriptor_pb2.FieldDescriptorProto, enclosing: list[str]) -> str:
        if field.type in (descriptor_pb2.FieldDescriptorProto.TYPE_MESSAGE, descriptor_pb2.FieldDescriptorProto.TYPE_ENUM):
            return format_type_name(field.type_name, self.package, enclosing)
        return FIELD_TYPE_NAMES.get(field.type, "unknown")

    def _write_extensions(self, extensions: list[descriptor_pb2.FieldDescriptorProto], lines: list[str], indent_level: int, enclosing: list[str]):
        indent = "    " * indent_level
        # 按 extend 目标消息分组
        grouped: dict[str, list[descriptor_pb2.FieldDescriptorProto]] = {}
        for ext in extensions:
            target = format_type_name(ext.extendee, self.package, enclosing)
            grouped.setdefault(target, []).append(ext)

        for target, exts in grouped.items():
            lines.append(f"{indent}extend {target} {{")
            for ext in exts:
                line_str = self._format_field(ext, enclosing, is_oneof=False)
                lines.append(f"{indent}    {line_str}")
            lines.append(f"{indent}}}")
            lines.append("")

    def _write_service(self, service_desc: descriptor_pb2.ServiceDescriptorProto, lines: list[str], indent_level: int):
        indent = "    " * indent_level
        lines.append(f"{indent}service {service_desc.name} {{")
        for method in service_desc.method:
            in_t = format_type_name(method.input_type, self.package, [])
            if method.client_streaming:
                in_t = f"stream {in_t}"
            out_t = format_type_name(method.output_type, self.package, [])
            if method.server_streaming:
                out_t = f"stream {out_t}"
            lines.append(f"{indent}    rpc {method.name}({in_t}) returns ({out_t});")
        lines.append(f"{indent}}}")
        lines.append("")


class PyExtractor:
    """Protobuf 描述符静态提取器。"""

    @staticmethod
    def extract_descriptor_from_source(source_code: str) -> Optional[bytes]:
        """通过 AST 解析从 Python 代码中提取 FileDescriptorProto 序列化字节。"""
        try:
            tree = ast.parse(source_code)
            for node in ast.walk(tree):
                if isinstance(node, ast.Call):
                    # 匹配 descriptor.FileDescriptor(..., serialized_pb=...)
                    for kw in node.keywords:
                        if kw.arg == 'serialized_pb':
                            val = PyExtractor._evaluate_ast_bytes(kw.value)
                            if val is not None:
                                return val

                    # 匹配 _descriptor_pool.Default().AddSerializedFile(...)
                    if hasattr(node.func, 'attr') and node.func.attr == 'AddSerializedFile':
                        if node.args:
                            val = PyExtractor._evaluate_ast_bytes(node.args[0])
                            if val is not None:
                                return val
        except Exception:
            pass

        # 正则回退匹配
        match = re.search(r'serialized_pb\s*=\s*(b?([\'"])(?:\\.|[^\\])*?\2)', source_code, re.DOTALL)
        if match:
            try:
                raw_literal = ast.literal_eval(match.group(1))
                return raw_literal if isinstance(raw_literal, bytes) else raw_literal.encode('latin1')
            except Exception:
                pass

        match = re.search(r'AddSerializedFile\((b?([\'"])(?:\\.|[^\\])*?\2)\)', source_code, re.DOTALL)
        if match:
            try:
                raw_literal = ast.literal_eval(match.group(1))
                return raw_literal if isinstance(raw_literal, bytes) else raw_literal.encode('latin1')
            except Exception:
                pass

        return None

    @staticmethod
    def _evaluate_ast_bytes(node: ast.AST) -> Optional[bytes]:
        if isinstance(node, ast.Constant):
            if isinstance(node.value, bytes):
                return node.value
            if isinstance(node.value, str):
                return node.value.encode('latin1')
        elif isinstance(node, ast.Bytes):
            return node.s
        elif isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            left = PyExtractor._evaluate_ast_bytes(node.left)
            right = PyExtractor._evaluate_ast_bytes(node.right)
            if left is not None and right is not None:
                return left + right
        return None

    @classmethod
    def extract_from_file(cls, file_path: Path) -> tuple[str, str]:
        """提取单个文件，返回 (proto_filename, proto_content)。"""
        file_path = Path(file_path).resolve()
        if not file_path.is_file():
            raise FileNotFoundError(f"文件不存在: {file_path}")

        # 如果直接是 .pb 或 .desc 二进制描述符文件
        if file_path.suffix in ('.pb', '.desc', '.bin'):
            raw_bytes = file_path.read_bytes()
        else:
            code = file_path.read_text(encoding='utf-8', errors='replace')
            raw_bytes = cls.extract_descriptor_from_source(code)
            if not raw_bytes:
                raise ValueError(f"在 {file_path.name} 中未能检测到有效 Protobuf serialized_pb 描述符")

        fd = descriptor_pb2.FileDescriptorProto()
        fd.ParseFromString(raw_bytes)

        proto_filename = fd.name if fd.name else f"{file_path.stem}.proto"
        if proto_filename.endswith('_pb2.proto'):
            proto_filename = proto_filename[:-10] + '.proto'

        content = ProtoGenerator(fd).generate()
        return proto_filename, content

    @classmethod
    def process(cls, input_path: Path, output_dir: Optional[Path] = None) -> list[tuple[Path, str]]:
        """批量或单文件处理入口。"""
        input_path = Path(input_path).resolve()
        if not input_path.exists():
            raise FileNotFoundError(f"输入路径不存在: {input_path}")

        if output_dir is None:
            output_dir = Path("proto")
        output_dir = Path(output_dir).resolve()
        output_dir.mkdir(parents=True, exist_ok=True)

        targets = []
        if input_path.is_file():
            targets.append(input_path)
        else:
            targets = sorted(input_path.glob('*_pb2.py'))
            if not targets:
                targets = sorted(input_path.rglob('*_pb2.py'))

        results = []
        for target in targets:
            try:
                proto_name, content = cls.extract_from_file(target)
                out_file = output_dir / Path(proto_name).name
                out_file.parent.mkdir(parents=True, exist_ok=True)
                out_file.write_text(content, encoding='utf-8')
                results.append((out_file, content))
                print(f"[+] 成功提取: {target.name} -> {out_file.name}")
            except Exception as e:
                print(f"[-] 提取跳过 {target.name}: {e}")

        return results


def main():
    parser = argparse.ArgumentParser(
        description="QRZD ProtoDumper",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument('-i', '--input', type=Path, required=True, help="输入的 *_pb2.py 文件路径，或包含 pb2 的协议目录")
    parser.add_argument('-o', '--output', type=Path, default=Path("output"), help="导出的 .proto 目标输出目录 (默认: ./proto)")

    args = parser.parse_args()

    results = PyExtractor.process(args.input, args.output)

    print(f"\n[*] 提取处理完成，共生成 {len(results)} 个 .proto 文件。")


if __name__ == '__main__':
    main()
