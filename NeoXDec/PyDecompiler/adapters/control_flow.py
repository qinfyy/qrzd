"""针对 uncompyle6 3.9.3 的 NeoX 特化控制流 AST 补丁。"""

from functools import wraps

_installed = False
_counts = {
    "module_return": 0,
    "empty_then_jump": 0,
    "lambda_comprehension_return": 0,
    "loop_latch": 0,
    "comprehension_loop": 0,
    "nested_loop_condition": 0,
}
_events = []


def diagnostics(reset=False):
    result = dict(_counts)
    result["events"] = list(_events)
    if reset:
        for key in _counts:
            _counts[key] = 0
        _events.clear()
    return result


def _record(kind, **event):
    _counts[kind] += 1
    if len(_events) < 64:
        _events.append(dict(kind=kind, **event))


def _comprehension_loop_head(scanner, offset):
    code, opc = scanner.code, scanner.opc
    target = scanner.get_target(offset)
    if not (offset + 6 < target < len(code)) or not scanner.lines:
        return None
    if code[offset + 3] != opc.BUILD_LIST or scanner.get_argument(offset + 3) != 0:
        return None
    parents = [s for s in scanner.structs if s["start"] <= offset < s["end"]]
    if not parents or min(s["end"] for s in parents) < target:
        return None
    pop = scanner.prev[target]
    latch = scanner.prev[pop]
    if code[pop] != opc.POP_BLOCK or code[latch] != opc.JUMP_ABSOLUTE:
        return None
    head = scanner.get_target(latch)
    if not (offset + 3 < head < latch and code[head] == opc.FOR_ITER
            and scanner.get_target(head) == pop):
        return None
    comprehension_end = scanner.prev[head]
    comprehension_latch = scanner.prev[comprehension_end]
    if (code[comprehension_end] != opc.GET_ITER
            or code[comprehension_latch] != opc.JUMP_ABSOLUTE):
        return None
    comprehension_head = scanner.get_target(comprehension_latch)
    if not (offset + 3 < comprehension_head < comprehension_latch
            and code[comprehension_head] == opc.FOR_ITER
            and scanner.get_target(comprehension_head) == comprehension_end
            and scanner.lines[offset].next <= comprehension_latch):
        return None
    return head


def _separate_nested_loop_condition(scanner, offset, op):
    opc, code = scanner.opc, scanner.code
    if op != opc.PJIF:
        return False
    merged = scanner.fixed_jumps.get(offset)
    head = scanner.get_target(offset)
    if (merged is None or not (0 <= head < offset < merged < len(code))
            or code[head] != opc.FOR_ITER or code[merged] != opc.PJIF
            or scanner.get_target(merged) != head):
        return False
    loops = [s for s in scanner.structs
             if s["type"] == "for-loop" and s["start"] == head]
    if len(loops) != 1:
        return False
    latch = loops[0]["end"]
    explicit_continue, branch_end = latch - 6, latch - 9
    if not (merged + 3 < branch_end and latch < len(code)):
        return False
    if not all(code[p] == opc.JUMP_ABSOLUTE and scanner.get_target(p) == head
               for p in (explicit_continue, latch - 3, latch)):
        return False
    if (code[branch_end] != opc.JUMP_ABSOLUTE
            or scanner.get_target(branch_end) != latch):
        return False
    if not (explicit_continue in scanner.linestarts
            and latch not in scanner.linestarts
            and latch - 3 not in scanner.linestarts
            and scanner.lines[offset].l_no < scanner.lines[merged].l_no
            < scanner.lines[explicit_continue].l_no):
        return False
    conditions = [p for p in scanner.op_range(offset + 3, merged)
                  if code[p] in opc.JUMP_OPs]
    if not (len(conditions) == 1 and code[conditions[0]] == opc.PJIF
            and scanner.get_target(conditions[0]) == head
            and scanner.lines[conditions[0]].l_no == scanner.lines[merged].l_no):
        return False
    if any(code[p] in opc.JUMP_OPs
           or code[p] in (opc.RETURN_VALUE, opc.BREAK_LOOP, opc.RAISE_VARARGS)
           for p in scanner.op_range(merged + 3, branch_end)):
        return False
    return True


def install():
    """向 uncompyle6 安装控制流猴子补丁。"""
    global _installed
    if _installed:
        return False

    import uncompyle6
    from uncompyle6.scanners.scanner2 import Scanner2
    from uncompyle6.semantics.pysource import SourceWalker

    if uncompyle6.__version__ != "3.9.3":
        raise RuntimeError(f"控制流适配器需要 uncompyle6 3.9.3，当前版本为: {uncompyle6.__version__}")

    original_build_ast = SourceWalker.build_ast
    original_detect = Scanner2.detect_control_flow
    original_ingest = Scanner2.ingest

    @wraps(original_build_ast)
    def build_ast(self, tokens, customize, code, is_lambda=False,
                  noneInNames=False, is_top_level_module=False,
                  compile_mode="exec"):
        if (self.version[:2] == (2, 7) and is_top_level_module
                and code.co_name == "<module>" and compile_mode == "exec"
                and not is_lambda and not noneInNames and self.hide_internal
                and len(tokens) >= 2
                and tokens[-1].kind == "RETURN_VALUE"
                and tokens[-2].kind == "LOAD_CONST"):
            terminal = tokens[-2]
            index = terminal.attr
            if (type(index) is int and 0 <= index < len(code.co_consts)
                    and code.co_consts[index] is None
                    and terminal.pattr is None):
                terminal.attr = None
                terminal.linestart = None
                _record("module_return", offset=terminal.offset)
        return original_build_ast(
            self, tokens, customize, code, is_lambda=is_lambda,
            noneInNames=noneInNames,
            is_top_level_module=is_top_level_module,
            compile_mode=compile_mode)

    @wraps(original_detect)
    def detect_control_flow(self, offset, op, extended_arg):
        head = None
        if self.version[:2] == (2, 7) and op == self.opc.SETUP_LOOP:
            head = _comprehension_loop_head(self, offset)
        if head is None:
            result = original_detect(self, offset, op, extended_arg)
        else:
            original_line = self.lines[offset]
            self.lines[offset] = original_line._replace(next=head + 3)
            try:
                result = original_detect(self, offset, op, extended_arg)
            finally:
                self.lines[offset] = original_line
            _record("comprehension_loop", offset=offset, loop_head=head)
        if (self.version[:2] == (2, 7)
                and _separate_nested_loop_condition(self, offset, op)):
            merged = self.fixed_jumps.pop(offset)
            _record("nested_loop_condition", offset=offset,
                    removed_merge=merged, loop_head=self.get_target(offset))
        if self.version[:2] != (2, 7) or op != self.opc.PJIT:
            return result
        next_stmt = self.next_stmt[offset]
        if not (offset + 3 < next_stmt < len(self.code)):
            return result
        inner = self.prev[next_stmt]
        if not (offset + 3 <= inner == next_stmt - 3):
            return result
        target = self.get_target(offset)
        if (next_stmt + 3 < target <= len(self.code)
                and self.code[next_stmt] == self.opc.JUMP_ABSOLUTE
                and self.get_target(next_stmt) == target
                and self.code[inner] == self.opc.PJIF
                and self.get_target(inner) == next_stmt + 3
                and self.fixed_jumps.get(offset) == inner):
            self.fixed_jumps[offset] = target
            _record("empty_then_jump", offset=offset,
                    previous_target=inner, target=target)
        return result

    @wraps(original_ingest)
    def ingest(self, code, *args, **kwargs):
        tokens, customize = original_ingest(self, code, *args, **kwargs)
        if self.version[:2] != (2, 7):
            return tokens, customize
        for token in tokens:
            if (token.kind != "CONTINUE" or type(token.offset) is not int
                    or token.linestart is not None
                    or token.offset not in self.not_continue):
                continue
            offset = token.offset
            previous = self.prev[offset]
            loop_head = self.get_target(offset)
            if loop_head <= 0 or offset + 3 >= len(self.code):
                continue
            setup = self.prev[loop_head]
            if (self.code[offset] == self.opc.JUMP_ABSOLUTE
                    and previous == offset - 3
                    and self.code[previous] == self.opc.JUMP_ABSOLUTE
                    and self.get_target(previous) == loop_head
                    and self.code[offset + 3] == self.opc.JUMP_FORWARD
                    and self.code[setup] == self.opc.SETUP_LOOP
                    and self.fixed_jumps.get(setup) == offset + 3
                    and self.get_target(setup) == self.get_target(offset + 3)):
                token.kind = "JUMP_BACK"
                _record("loop_latch", offset=offset, loop_head=loop_head)
        if (code.co_name != "<lambda>" or not tokens
                or tokens[-1].kind != "RETURN_VALUE"
                or type(tokens[-1].offset) is not int):
            return tokens, customize
        final_return = tokens[-1].offset
        for index, token in enumerate(tokens[:-1]):
            if token.kind != "RETURN_VALUE" or type(token.offset) is not int:
                continue
            offset = token.offset
            previous = self.prev[offset]
            if self.code[previous] != self.opc.JUMP_ABSOLUTE:
                continue
            loop_head = self.get_target(previous)
            if not (loop_head < previous
                    and self.code[loop_head] == self.opc.FOR_ITER
                    and self.get_target(loop_head) == final_return):
                continue
            following = tokens[index + 1]
            if following.kind != "COME_FROM" or following.off2int() != offset + 1:
                continue
            condition = following.attr
            if not (type(condition) is int and condition < loop_head
                    and self.code[condition] in (self.opc.PJIF, self.opc.PJIT)
                    and self.get_target(condition) == offset + 1):
                continue
            token.kind = "RETURN_END_IF"
            _record("lambda_comprehension_return", offset=offset,
                    loop_head=loop_head, final_return=final_return)
        return tokens, customize

    SourceWalker.build_ast = build_ast
    Scanner2.detect_control_flow = detect_control_flow
    Scanner2.ingest = ingest
    _installed = True
    return True
