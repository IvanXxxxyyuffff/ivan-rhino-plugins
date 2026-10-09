#!/usr/bin/env python3
"""Source-freeze and UI contract audit for the staged IVAN-CENTER candidate.

This is a text-only audit: it never compiles, launches Rhino, or edits candidate
sources. It writes only contract-audit.json beside this script.
"""

from __future__ import annotations

import argparse
import collections
import datetime as _dt
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Dict, Iterable, List, NamedTuple, Optional, Sequence, Tuple


ROOT = Path(__file__).resolve().parent
MANIFEST = ROOT / "source-baseline.json"
DEFAULT_REPORT = ROOT / "contract-audit.json"

THEME_PATHS = [
    "shared/PanelTheme.cs",
    "halftone/src/PanelTheme.cs",
    "radialdots/src/PanelTheme.cs",
    "stripe/center/PanelTheme.cs",
    "stripe/src/PanelTheme.cs",
    "voronoi/src/PanelTheme.cs",
]
PANEL_PATHS = [
    "halftone/src/HalftoneUi.cs",
    "radialdots/src/RadialDotsUi.cs",
    "voronoi/src/VoronoiUi.cs",
    "stripe/src/StripePanel.cs",
]
VOLUME_PATH = "src/VapeVolume/UI/VolumeDialog.cs"
CENTER_PATH = "stripe/center/CenterForm.cs"
INSTALLER_PATH = "stripe/center/Installer.cs"
ICONMAKE_PATH = "iconmake/Program.cs"
APP_ICON_PATH = "stripe/center/app.ico"

ALLOWED_CHANGES = set(
    THEME_PATHS
    + PANEL_PATHS
    + [VOLUME_PATH, CENTER_PATH, INSTALLER_PATH, ICONMAKE_PATH, APP_ICON_PATH]
)
CONTRACT_SOURCE_PATHS = set(THEME_PATHS + PANEL_PATHS + [VOLUME_PATH, CENTER_PATH, INSTALLER_PATH])
FROZEN_CENTER_METHODS = ["DoAll", "InstallOne", "UninstallOne", "RefreshStatus", "RunSilent", "RunUiTest"]

SETTING_NAMES = {
    "Minimum", "Maximum", "MinValue", "MaxValue", "Increment", "Step",
    "TickFrequency", "SmallChange", "LargeChange", "Value",
}
EVENT_NAMES = {
    "Click", "ValueChanged", "CheckedChanged", "TextChanged", "SelectedIndexChanged",
    "SelectedValueChanged", "Scroll", "MouseDown", "MouseUp", "MouseMove", "MouseEnter",
    "MouseLeave", "KeyDown", "KeyUp", "KeyPress", "FormClosing", "FormClosed", "Load",
    "Shown", "Closed", "SizeChanged", "Resize", "Paint", "LinkClicked", "ItemCheck",
    "CellValueChanged", "SelectionChanged", "DoubleClick", "MouseClick", "Validated",
    "Validating", "Enter", "Leave", "GotFocus", "LostFocus",
}
VISUAL_VOLUME_METHODS = {
    "ShowInternal", "SetPickButtonState", "FontBig", "FontTitle", "FontSmall", "StateColor",
    "SectionLabel", "MixColor", "GlassCard", "SectionCard", "RoundedPath",
}
VISUAL_VOLUME_PREFIXES = ("OnPaint", "OnDraw", "Draw", "Paint", "Render", "BuildVisual", "CreateVisual")

Token = NamedTuple("Token", [("value", str), ("start", int), ("end", int), ("kind", str)])


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def _skip_line_comment(src: str, i: int) -> int:
    j = src.find("\n", i + 2)
    return len(src) if j < 0 else j + 1


def _skip_block_comment(src: str, i: int) -> int:
    j = src.find("*/", i + 2)
    return len(src) if j < 0 else j + 2


def _string_prefix(src: str, i: int) -> Optional[Tuple[int, bool, bool]]:
    """Return (quote index, verbatim, interpolated) for a C# string at i."""
    n = len(src)
    if i >= n:
        return None
    if src.startswith('$@"', i) or src.startswith('@$"', i):
        return i + 2, True, True
    if src.startswith('@"', i):
        return i + 1, True, False
    if src.startswith('$"', i):
        return i + 1, False, True
    if src[i] == '"':
        return i, False, False
    # C# raw literals (rare in this codebase); recognize quote runs of 3+.
    k = i
    while k < n and src[k] == '$':
        k += 1
    if k < n and src[k] == '"':
        q = k
        while k < n and src[k] == '"':
            k += 1
        if k - q >= 3:
            return q, True, q != i
    return None


def _skip_char(src: str, i: int) -> int:
    j = i + 1
    while j < len(src):
        if src[j] == "\\":
            j += 2
        elif src[j] == "'":
            return j + 1
        else:
            j += 1
    return len(src)


def _skip_interpolation_expr(src: str, i: int) -> int:
    """i points just after an interpolation's opening brace."""
    depth = 1
    n = len(src)
    while i < n:
        if src.startswith("//", i):
            i = _skip_line_comment(src, i)
            continue
        if src.startswith("/*", i):
            i = _skip_block_comment(src, i)
            continue
        if src[i] == "'":
            i = _skip_char(src, i)
            continue
        pref = _string_prefix(src, i)
        if pref is not None:
            i = _skip_string(src, i, pref)
            continue
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return n


def _skip_string(src: str, start: int, info: Optional[Tuple[int, bool, bool]] = None) -> int:
    info = info or _string_prefix(src, start)
    if info is None:
        return start + 1
    quote, verbatim, interpolated = info
    n = len(src)
    qrun = 1
    while quote + qrun < n and src[quote + qrun] == '"':
        qrun += 1
    raw = qrun >= 3
    if not raw:
        # A normal empty C# string is two adjacent quotes: one opener, one closer.
        qrun = 1
    j = quote + qrun
    while j < n:
        if raw:
            if src[j] == '"':
                k = j
                while k < n and src[k] == '"':
                    k += 1
                if k - j >= qrun:
                    return k
                j = k
                continue
            j += 1
            continue
        if verbatim and src[j] == '"':
            if j + 1 < n and src[j + 1] == '"':
                j += 2
                continue
            return j + 1
        if not verbatim and src[j] == "\\":
            j += 2
            continue
        if interpolated and src[j] == "{":
            if j + 1 < n and src[j + 1] == "{":
                j += 2
                continue
            j = _skip_interpolation_expr(src, j + 1)
            continue
        if interpolated and src[j] == "}" and j + 1 < n and src[j + 1] == "}":
            j += 2
            continue
        if src[j] == '"':
            return j + 1
        j += 1
    return n


def csharp_tokens(src: str) -> List[Token]:
    """Lex enough C# to ignore comments and literals while balancing braces."""
    result: List[Token] = []
    i = 0
    n = len(src)
    operators = (
        ">>=", "<<=", "??=", "=>", "::", "?.", "??", "==", "!=", "<=", ">=", "++", "--",
        "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "??",
    )
    while i < n:
        ch = src[i]
        if ch.isspace():
            i += 1
            continue
        if src.startswith("//", i):
            i = _skip_line_comment(src, i)
            continue
        if src.startswith("/*", i):
            i = _skip_block_comment(src, i)
            continue
        if ch == "'":
            end = _skip_char(src, i)
            result.append(Token(src[i:end], i, end, "literal"))
            i = end
            continue
        info = _string_prefix(src, i)
        if info is not None:
            end = _skip_string(src, i, info)
            result.append(Token(src[i:end], i, end, "literal"))
            i = end
            continue
        if ch.isalpha() or ch == "_" or (ch == "@" and i + 1 < n and (src[i + 1].isalpha() or src[i + 1] == "_")):
            j = i + 1
            while j < n and (src[j].isalnum() or src[j] == "_"):
                j += 1
            result.append(Token(src[i:j], i, j, "id"))
            i = j
            continue
        if ch.isdigit():
            j = i + 1
            while j < n and (src[j].isalnum() or src[j] in "._"):
                j += 1
            result.append(Token(src[i:j], i, j, "number"))
            i = j
            continue
        op = next((op for op in operators if src.startswith(op, i)), None)
        if op:
            result.append(Token(op, i, i + len(op), "op"))
            i += len(op)
        else:
            result.append(Token(ch, i, i + 1, "punct"))
            i += 1
    return result


def matching_token(tokens: Sequence[Token], at: int, opener: str, closer: str) -> Optional[int]:
    if at >= len(tokens) or tokens[at].value != opener:
        return None
    depth = 0
    for i in range(at, len(tokens)):
        v = tokens[i].value
        if v == opener:
            depth += 1
        elif v == closer:
            depth -= 1
            if depth == 0:
                return i
    return None


def find_type(src: str, tokens: Sequence[Token], name: str) -> Optional[dict]:
    type_words = {"class", "struct", "interface", "record"}
    for i in range(len(tokens) - 1):
        if tokens[i].value not in type_words or tokens[i + 1].value.lstrip("@") != name:
            continue
        open_i = None
        for j in range(i + 2, len(tokens)):
            if tokens[j].value == ";":
                break
            if tokens[j].value == "{":
                open_i = j
                break
        if open_i is None:
            continue
        close_i = matching_token(tokens, open_i, "{", "}")
        if close_i is None:
            continue
        line_start = src.rfind("\n", 0, tokens[i].start) + 1
        return {"name": name, "keyword_index": i, "open": open_i, "close": close_i,
                "start": line_start, "end": tokens[close_i].end,
                "raw": src[line_start:tokens[close_i].end]}
    return None


def matching_paren(tokens: Sequence[Token], at: int) -> Optional[int]:
    return matching_token(tokens, at, "(", ")")


def extract_methods(tokens: Sequence[Token], type_info: dict) -> Dict[Tuple[str, Tuple[str, ...]], Tuple[str, ...]]:
    methods: Dict[Tuple[str, Tuple[str, ...]], Tuple[str, ...]] = {}
    i = type_info["open"] + 1
    end = type_info["close"]
    while i < end:
        if tokens[i].value == "{":
            close = matching_token(tokens, i, "{", "}")
            i = (close + 1) if close is not None else i + 1
            continue
        if tokens[i].value != "(" or i == 0 or tokens[i - 1].kind != "id":
            i += 1
            continue
        name = tokens[i - 1].value.lstrip("@")
        close_paren = matching_paren(tokens, i)
        if close_paren is None:
            i += 1
            continue
        body_at = close_paren + 1
        # Skip optional generic constraints in method declarations.
        while body_at < end and tokens[body_at].value not in {"{", "=>", ";"}:
            if tokens[body_at].value in {"=", ","}:
                break
            body_at += 1
        if body_at >= end or tokens[body_at].value not in {"{", "=>"}:
            i = close_paren + 1
            continue
        signature = tuple(t.value for t in tokens[i + 1:close_paren])
        if tokens[body_at].value == "{":
            body_end = matching_token(tokens, body_at, "{", "}")
            if body_end is None:
                i = body_at + 1
                continue
            body = tuple(t.value for t in tokens[body_at:body_end + 1])
            i = body_end + 1
        else:
            j = body_at + 1
            parens = brackets = braces = 0
            while j < end:
                v = tokens[j].value
                if v == "(" : parens += 1
                elif v == ")": parens -= 1
                elif v == "[": brackets += 1
                elif v == "]": brackets -= 1
                elif v == "{": braces += 1
                elif v == "}":
                    if braces == 0: break
                    braces -= 1
                if v == ";" and parens == brackets == braces == 0:
                    break
                j += 1
            body = tuple(t.value for t in tokens[body_at:j + (1 if j < end and tokens[j].value == ";" else 0)])
            i = j + 1
        methods[(name, signature)] = body
    return methods


def public_contracts(tokens: Sequence[Token]) -> collections.Counter:
    """Collect public type/member signatures; bodies and comments are ignored."""
    contracts: collections.Counter = collections.Counter()
    modifiers = {"static", "readonly", "const", "virtual", "override", "abstract", "sealed", "new", "async", "extern", "event", "partial", "unsafe"}
    type_words = {"class", "struct", "interface", "enum", "delegate", "record"}
    for i, tok in enumerate(tokens):
        if tok.value != "public":
            continue
        limit = min(len(tokens), i + 120)
        # A public constructor has its name immediately followed by '(' (after modifiers).
        first = i + 1
        while first < limit and tokens[first].value in modifiers:
            first += 1
        # Only treat a type keyword in the declaration head as a nested type;
        # do not mistake a later class declaration for part of a constructor.
        if first < limit and tokens[first].value in type_words:
            name_i = first + 1
            while name_i < limit and tokens[name_i].kind != "id": name_i += 1
            if name_i < limit:
                sig = tuple(t.value for t in tokens[i:name_i + 1])
                contracts[" ".join(sig)] += 1
                continue
        if first + 1 < limit and tokens[first].kind == "id" and tokens[first + 1].value == "(":
            close = matching_paren(tokens, first + 1)
            if close is not None:
                contracts[" ".join(t.value for t in tokens[i:close + 1])] += 1
                continue
        # Locate a field/property/event name or a normal method name. The name is
        # followed by a declaration delimiter, not merely another type token.
        member_i = None
        member_delims = {"(", "{", ";", "=", ",", "=>", "["}
        for j in range(first, limit - 1):
            if tokens[j].kind == "id" and tokens[j + 1].value in member_delims:
                member_i = j
                break
            if tokens[j].value in {";", "}"}:
                break
        if member_i is None:
            continue
        if tokens[member_i + 1].value == "(":
            close = matching_paren(tokens, member_i + 1)
            if close is not None:
                contracts[" ".join(t.value for t in tokens[i:close + 1])] += 1
                continue
        contracts[" ".join(t.value for t in tokens[i:member_i + 1])] += 1
    return contracts


def setting_assignments(tokens: Sequence[Token]) -> collections.Counter:
    found: collections.Counter = collections.Counter()
    for i in range(len(tokens) - 2):
        if tokens[i].value not in SETTING_NAMES or tokens[i + 1].value != "=":
            continue
        j = i + 2
        parens = brackets = braces = 0
        expression = []
        while j < len(tokens):
            v = tokens[j].value
            if v == "(" : parens += 1
            elif v == ")":
                if parens == 0: break
                parens -= 1
            elif v == "[": brackets += 1
            elif v == "]":
                if brackets == 0: break
                brackets -= 1
            elif v == "{": braces += 1
            elif v == "}":
                if braces == 0: break
                braces -= 1
            if v in {",", ";"} and parens == brackets == braces == 0:
                break
            expression.append(v)
            j += 1
        if expression:
            found[(tokens[i].value, tuple(expression))] += 1
    return found


def addrow_contracts(tokens: Sequence[Token]) -> collections.Counter:
    found: collections.Counter = collections.Counter()
    for i in range(len(tokens) - 1):
        if tokens[i].value != "AddRow" or tokens[i + 1].value != "(":
            continue
        close = matching_paren(tokens, i + 1)
        if close is None:
            continue
        # Declarations end in a method body; calls usually end in a comma/semicolon,
        # lambda continuation, or closing initializer delimiter.
        after = close + 1
        while after < len(tokens) and tokens[after].value not in {"{", ";", ",", "}", ")"}:
            after += 1
        if after < len(tokens) and tokens[after].value == "{":
            continue
        args = tuple(t.value for t in tokens[i + 2:close])
        found[args] += 1
    return found


def event_bindings(tokens: Sequence[Token]) -> collections.Counter:
    found: collections.Counter = collections.Counter()
    for i in range(1, len(tokens) - 2):
        if tokens[i].value != "." or tokens[i + 1].value not in EVENT_NAMES or tokens[i + 2].value != "+=":
            continue
        start = i - 1
        if tokens[start].kind != "id":
            receiver = "?"
        else:
            while start >= 2 and tokens[start - 1].value in {".", "?."} and tokens[start - 2].kind == "id":
                start -= 2
            receiver = ".".join(t.value for t in tokens[start:i] if t.value not in {".", "?."})
        found[(receiver, tokens[i + 1].value)] += 1
    return found


def missing_counter(expected: collections.Counter, actual: collections.Counter) -> List[dict]:
    result = []
    for item, count in expected.items():
        missing = count - actual.get(item, 0)
        if missing > 0:
            result.append({"contract": _jsonable(item), "missing_count": missing})
    return result


def _jsonable(value):
    if isinstance(value, tuple):
        return [_jsonable(v) for v in value]
    if isinstance(value, list):
        return [_jsonable(v) for v in value]
    return value


def _file_text(path: Path) -> Optional[str]:
    try:
        return path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError):
        return None


def _method_map(path: Path, class_name: str) -> Tuple[Optional[str], Dict[Tuple[str, Tuple[str, ...]], Tuple[str, ...]]]:
    src = _file_text(path)
    if src is None:
        return None, {}
    tokens = csharp_tokens(src)
    info = find_type(src, tokens, class_name)
    if info is None:
        return None, {}
    return info["raw"], extract_methods(tokens, info)


def _counter_description(counter: collections.Counter) -> List[dict]:
    return [{"contract": _jsonable(k), "count": v} for k, v in sorted(counter.items(), key=lambda item: repr(item[0]))]


def run_audit(report_path: Path) -> dict:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8-sig"))
    baseline = Path(manifest["baseline"])
    candidate = Path(manifest["candidate"])
    entries = manifest.get("files", [])
    violations: List[dict] = []
    baseline_integrity = []
    missing_files = []
    changed_files = []
    unexpected_modified = []
    manifest_paths = set()

    for entry in entries:
        rel = entry["relative"].replace("\\", "/")
        manifest_paths.add(rel)
        bpath, cpath = baseline / rel, candidate / rel
        if not bpath.is_file():
            baseline_integrity.append({"path": rel, "issue": "baseline_missing"})
            continue
        actual_base_hash = sha256(bpath)
        if actual_base_hash != entry["sha256"].lower():
            baseline_integrity.append({"path": rel, "issue": "manifest_hash_mismatch",
                                       "manifest_sha256": entry["sha256"], "actual_sha256": actual_base_hash})
        if not cpath.is_file():
            missing_files.append(rel)
            violations.append({"category": "missing_baseline_file", "path": rel})
            continue
        candidate_hash = sha256(cpath)
        if candidate_hash != actual_base_hash:
            changed_files.append(rel)
            if rel not in ALLOWED_CHANGES:
                unexpected_modified.append(rel)
                violations.append({"category": "frozen_file_changed", "path": rel,
                                   "baseline_sha256": actual_base_hash, "candidate_sha256": candidate_hash})

    candidate_sources = set()
    for p in candidate.rglob("*"):
        if p.is_file() and p.suffix.lower() in {".cs", ".csproj", ".manifest"}:
            candidate_sources.add(p.relative_to(candidate).as_posix())
    generated_sources = sorted(
        rel for rel in candidate_sources - manifest_paths
        if any(part.lower() in {"bin", "obj", "out"} for part in Path(rel).parts)
    )
    extra_sources = sorted((candidate_sources - manifest_paths) - set(generated_sources))
    for rel in extra_sources:
        violations.append({"category": "unexpected_candidate_source", "path": rel})

    if len(entries) != 62:
        violations.append({"category": "manifest_file_count_unexpected", "expected": 62, "actual": len(entries)})
    if baseline_integrity:
        violations.extend({"category": "baseline_integrity", **item} for item in baseline_integrity)

    # Installer.cs may change only inside the IconFactory class.
    installer_result = {"path": INSTALLER_PATH, "passed": False}
    b_installer, c_installer = baseline / INSTALLER_PATH, candidate / INSTALLER_PATH
    bsrc, csrc = _file_text(b_installer), _file_text(c_installer)
    if bsrc is not None and csrc is not None:
        bt, ct = csharp_tokens(bsrc), csharp_tokens(csrc)
        bi, ci = find_type(bsrc, bt, "IconFactory"), find_type(csrc, ct, "IconFactory")
        if bi and ci:
            boutside = bsrc[:bi["start"]] + "\0" + bsrc[bi["end"]:]
            coutside = csrc[:ci["start"]] + "\0" + csrc[ci["end"]:]
            installer_result["passed"] = boutside == coutside
            installer_result["outside_class_identical"] = installer_result["passed"]
            installer_result["baseline_class_span"] = [bi["start"], bi["end"]]
            installer_result["candidate_class_span"] = [ci["start"], ci["end"]]
            if not installer_result["passed"]:
                violations.append({"category": "installer_changed_outside_iconfactory", "path": INSTALLER_PATH})
        else:
            installer_result["issue"] = "IconFactory declaration not found"
            violations.append({"category": "installer_iconfactory_missing", "path": INSTALLER_PATH})

    # Motion is a frozen nested class in all six PanelTheme copies.
    motion_checks = []
    shared_candidate_motion = None
    for rel in THEME_PATHS:
        bpath, cpath = baseline / rel, candidate / rel
        bsrc, csrc = _file_text(bpath), _file_text(cpath)
        item = {"path": rel, "passed": False}
        if bsrc is not None and csrc is not None:
            bi, ci = find_type(bsrc, csharp_tokens(bsrc), "Motion"), find_type(csrc, csharp_tokens(csrc), "Motion")
            if bi and ci:
                item["passed"] = bi["raw"] == ci["raw"]
                item["identical_to_baseline"] = item["passed"]
                if rel == "shared/PanelTheme.cs":
                    shared_candidate_motion = ci["raw"]
                if not item["passed"]:
                    violations.append({"category": "theme_motion_changed", "path": rel})
            else:
                item["issue"] = "Motion class not found"
                violations.append({"category": "theme_motion_missing", "path": rel})
        else:
            item["issue"] = "source missing or unreadable"
        motion_checks.append(item)
    if shared_candidate_motion is not None:
        for item in motion_checks:
            csrc = _file_text(candidate / item["path"])
            if csrc is None:
                continue
            info = find_type(csrc, csharp_tokens(csrc), "Motion")
            if info and info["raw"] != shared_candidate_motion:
                item["identical_to_shared_candidate"] = False
                if item["passed"]:
                    violations.append({"category": "theme_motion_copy_diverged", "path": item["path"]})
            elif info:
                item["identical_to_shared_candidate"] = True

    # Compare all legacy public API, numeric setting expressions, and event bindings
    # in the UI/theme files. Additions are allowed; baseline contracts may not vanish.
    api_checks, setting_checks, event_checks, addrow_checks = [], [], [], []
    for rel in sorted(CONTRACT_SOURCE_PATHS):
        bsrc, csrc = _file_text(baseline / rel), _file_text(candidate / rel)
        if bsrc is None or csrc is None:
            continue
        bt, ct = csharp_tokens(bsrc), csharp_tokens(csrc)
        api_missing = missing_counter(public_contracts(bt), public_contracts(ct))
        api_item = {"path": rel, "passed": not api_missing, "missing": api_missing}
        api_checks.append(api_item)
        if api_missing:
            violations.append({"category": "public_member_missing", "path": rel, "missing": api_missing})

        expected_settings, actual_settings = setting_assignments(bt), setting_assignments(ct)
        setting_missing = missing_counter(expected_settings, actual_settings)
        setting_item = {"path": rel, "passed": not setting_missing, "missing": setting_missing}
        setting_checks.append(setting_item)
        if setting_missing:
            violations.append({"category": "setting_property_or_range_missing", "path": rel, "missing": setting_missing})

        expected_events, actual_events = event_bindings(bt), event_bindings(ct)
        event_missing = missing_counter(expected_events, actual_events)
        event_item = {"path": rel, "passed": not event_missing, "missing": event_missing}
        event_checks.append(event_item)
        if event_missing:
            violations.append({"category": "event_binding_missing", "path": rel, "missing": event_missing})

        if rel in PANEL_PATHS:
            expected_rows, actual_rows = addrow_contracts(bt), addrow_contracts(ct)
            row_missing = missing_counter(expected_rows, actual_rows)
            row_item = {"path": rel, "passed": not row_missing, "missing": row_missing,
                        "baseline_addrow_signatures": sum(expected_rows.values()),
                        "candidate_addrow_signatures": sum(actual_rows.values())}
            addrow_checks.append(row_item)
            if row_missing:
                violations.append({"category": "settings_row_or_numeric_contract_missing", "path": rel, "missing": row_missing})

    # Method bodies explicitly frozen by the task; compare C# tokens so whitespace
    # and comments are ignored while string literals remain behaviorally significant.
    frozen_method_checks = []
    for rel, class_name, method_names in [
        (CENTER_PATH, "CenterForm", FROZEN_CENTER_METHODS),
    ]:
        _, bmethods = _method_map(baseline / rel, class_name)
        _, cmethods = _method_map(candidate / rel, class_name)
        for method_name in method_names:
            bentries = {k: v for k, v in bmethods.items() if k[0] == method_name}
            centries = {k: v for k, v in cmethods.items() if k[0] == method_name}
            if not bentries:
                item = {"path": rel, "class": class_name, "method": method_name, "passed": None,
                        "status": "not_in_baseline" if not centries else "candidate_only_method",
                        "issue": "no baseline body exists to freeze; candidate-only body is not baseline-verifiable"}
            elif bentries.keys() != centries.keys():
                item = {"path": rel, "class": class_name, "method": method_name, "passed": False,
                        "issue": "method signature missing or changed"}
            else:
                same = all(bentries[k] == centries[k] for k in bentries)
                item = {"path": rel, "class": class_name, "method": method_name, "passed": same,
                        "comparison": "C# tokens; comments and whitespace ignored; string literals retained"}
            frozen_method_checks.append(item)
            if item["passed"] is False:
                violations.append({"category": "frozen_method_body_changed", **item})

    # All non-constructor, non-visual logic methods in VolumeDialog stay unchanged.
    _, volume_base = _method_map(baseline / VOLUME_PATH, "VolumeDialog")
    _, volume_candidate = _method_map(candidate / VOLUME_PATH, "VolumeDialog")
    volume_frozen, volume_visual, volume_missing = [], [], []
    for key, body in volume_base.items():
        name, signature = key
        if name == "VolumeDialog":
            continue
        if name in VISUAL_VOLUME_METHODS or any(name.startswith(p) for p in VISUAL_VOLUME_PREFIXES):
            volume_visual.append({"method": name, "signature": list(signature)})
            continue
        match = volume_candidate.get(key)
        if match is None:
            volume_missing.append({"method": name, "signature": list(signature), "issue": "method missing/signature changed"})
            continue
        same = match == body
        volume_frozen.append({"method": name, "signature": list(signature), "passed": same})
        if not same:
            violations.append({"category": "volume_logic_method_changed", "path": VOLUME_PATH,
                               "method": name, "signature": list(signature)})
    for item in volume_missing:
        violations.append({"category": "volume_logic_method_missing", "path": VOLUME_PATH, **item})
    volume_result = {
        "path": VOLUME_PATH,
        "passed": not volume_missing and all(x["passed"] for x in volume_frozen),
        "frozen_method_count": len(volume_frozen),
        "visual_method_exclusions": volume_visual,
        "frozen_methods": volume_frozen,
        "missing_methods": volume_missing,
        "comparison": "C# method-body tokens; comments and whitespace ignored; string literals retained",
    }

    # Cross-copy parity for all Theme public UI declarations is already measured
    # against the baseline above. Report exact changed whitelisted files as context.
    changed_allowed = sorted(set(changed_files) & ALLOWED_CHANGES)
    passed = not violations
    report = {
        "schema_version": 1,
        "generated_at": _dt.datetime.now(_dt.timezone.utc).isoformat(timespec="seconds"),
        "mode": "text-only; no compile or application launch",
        "baseline_manifest": str(MANIFEST),
        "baseline_root": str(baseline),
        "candidate_root": str(candidate),
        "summary": {
            "passed": passed,
            "baseline_file_count": len(entries),
            "allowed_change_count": len(ALLOWED_CHANGES),
            "changed_allowed_files": changed_allowed,
            "unexpected_modified_files": sorted(unexpected_modified),
            "missing_baseline_files": sorted(missing_files),
            "unexpected_candidate_source_files": extra_sources,
            "ignored_generated_source_files": generated_sources,
            "violation_count": len(violations),
        },
        "file_fidelity": {
            "allowed_changed_paths": sorted(ALLOWED_CHANGES),
            "changed_paths": sorted(changed_files),
            "unexpected_modified_paths": sorted(unexpected_modified),
            "missing_baseline_paths": sorted(missing_files),
            "unexpected_candidate_source_paths": extra_sources,
            "ignored_generated_source_paths": generated_sources,
            "baseline_manifest_integrity_issues": baseline_integrity,
        },
        "contracts": {
            "installer_outside_iconfactory": installer_result,
            "theme_motion": motion_checks,
            "public_member_retention": api_checks,
            "setting_property_retention": setting_checks,
            "event_binding_retention": event_checks,
            "settings_rows": addrow_checks,
            "centerform_frozen_methods": frozen_method_checks,
            "volumedialog_frozen_logic": volume_result,
        },
        "violations": violations,
        "caveats": [
            "Static source audit only; this script does not compile or launch Rhino.",
            "C# comments and whitespace are ignored in token-based method comparisons; literal tokens are retained.",
            "VolumeDialog visual exclusions are name-based; inspect the reported exclusion list when reviewing UI-only changes.",
            "Generated files under bin/, obj/, and out/ are listed but excluded from source-fidelity violations.",
        ],
    }
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report


def main() -> int:
    global MANIFEST
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=MANIFEST, help="Immutable source manifest; prior baseline remains preserved")
    parser.add_argument("--output", type=Path, default=DEFAULT_REPORT, help="JSON report destination")
    args = parser.parse_args()
    MANIFEST = args.manifest
    report = run_audit(args.output)
    print(json.dumps(report["summary"], ensure_ascii=False, indent=2))
    print("Report:", args.output.resolve())
    return 0 if report["summary"]["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
