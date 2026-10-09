#!/usr/bin/env python3
"""Final static UI/functional-contract audit for the staged candidate.

Reads BASELINE and MODIFIED_FILE, writes only final-ui-audit.json. This audit is
deliberately text-only: it does not compile, launch Rhino, or modify sources.
It imports tokenization and contract helpers from verify-contracts.py.
"""

from __future__ import annotations

import argparse
import collections
import datetime as dt
import hashlib
import importlib.util
import json
import re
import sys
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


ROOT = Path(__file__).resolve().parent
DEFAULT_REPORT = ROOT / "final-ui-audit.json"
VERIFY_PATH = ROOT / "verify-contracts.py"
LEGACY_MANIFEST_PATH = ROOT / "source-baseline.json"
LATEST_MANIFEST_PATH = ROOT / "source-latest.json"

THEME_PATHS = [
    "shared/PanelTheme.cs",
    "diamondfacet/src/PanelTheme.cs",
    "halftone/src/PanelTheme.cs",
    "radialdots/src/PanelTheme.cs",
    "stripe/center/PanelTheme.cs",
    "stripe/src/PanelTheme.cs",
    "voronoi/src/PanelTheme.cs",
]
PANEL_TYPES = {
    "halftone/src/HalftoneUi.cs": "HalftonePanel",
    "radialdots/src/RadialDotsUi.cs": "RadialDotsPanel",
    "voronoi/src/VoronoiUi.cs": "VoronoiPanel",
    "stripe/src/StripePanel.cs": "StripePanel",
}
VOLUME_PATH = "src/VapeVolume/UI/VolumeDialog.cs"
CENTER_PATH = "stripe/center/CenterForm.cs"
INSTALLER_PATH = "stripe/center/Installer.cs"
ICONMAKE_PATH = "iconmake/Program.cs"
UNITS_PATH = "src/VapeVolume/Core/Units.cs"
LATEST_CORE_IMPORTS = [
    "voronoi/src/CellNurbs.cs",
    "voronoi/src/VoronoiCore.cs",
    "voronoi/src/VoronoiPlugin.cs",
    "voronoi/src/VoronoiSelfTest.cs",
]

# UI-only constructor/build exceptions. Every baseline method outside these
# named visual builders is compared token-for-token so new scroll helpers remain
# permissible without masking existing setting/operation logic.
VOLUME_VISUAL_NAMES = {
    "ShowInternal", "SetPickButtonState", "FontBig", "FontTitle", "FontSmall",
    "StateColor", "SectionLabel", "MixColor", "GlassCard", "SectionCard",
    "RoundedPath",
}
VOLUME_VISUAL_PREFIXES = ("OnPaint", "OnDraw", "Draw", "Paint", "Render", "BuildVisual", "CreateVisual")
CENTER_VISUAL_METHODS = {"BuildRows"}

# Methods that changed on the previous icon iteration are app-tile-only. The
# recorded previous run observed the other 12 renderer helper bodies unchanged.
APP_ONLY_RENDERER_METHODS = {"CreateGlassIcon", "GlassDrawAppTile", "GlassDrawApp"}
PLUGIN_RENDERER_METHODS = {
    "GlassDrawTile", "GlassDrawRibbon", "GlassDrawStripe", "GlassDrawSphere",
    "GlassDrawHalftone", "GlassCellPath", "GlassDrawCell", "GlassDrawVoronoi",
    "GlassDrawRadialDots", "GlassDropPath", "GlassDrawVape", "GlassDrawMiniNode",
}


def _load_verify():
    spec = importlib.util.spec_from_file_location("verify_contracts_for_final_audit", VERIFY_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load helper module: {VERIFY_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return vars(module)


V = _load_verify()
# DecimalPlaces affects numeric-entry semantics just like bounds/step size; the
# shared verifier did not list it, so include it in every numeric assignment scan.
V["SETTING_NAMES"].add("DecimalPlaces")


def _read(path: Path) -> Optional[str]:
    try:
        return path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError):
        return None


def _hash(path: Path, algorithm: str = "sha256") -> Optional[str]:
    try:
        h = hashlib.new(algorithm)
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                h.update(chunk)
        return h.hexdigest()
    except OSError:
        return None


def _method_map(root: Path, rel: str, class_name: str):
    src = _read(root / rel)
    if src is None:
        return None, {}, []
    tokens = V["csharp_tokens"](src)
    info = V["find_type"](src, tokens, class_name)
    if info is None:
        return src, {}, tokens
    return src, V["extract_methods"](tokens, info), tokens


def _type_header(src: Optional[str], class_name: str) -> Optional[Tuple[str, ...]]:
    """Return modifiers, type name and base/interface list for a declared type."""
    if src is None:
        return None
    tokens = V["csharp_tokens"](src)
    info = V["find_type"](src, tokens, class_name)
    if info is None:
        return None
    start = info["keyword_index"]
    modifiers = {"public", "internal", "protected", "private", "static", "abstract",
                 "sealed", "partial", "unsafe", "new", "file"}
    while start > 0 and tokens[start - 1].value in modifiers:
        start -= 1
    return tuple(t.value for t in tokens[start:info["open"]])


def _missing(expected: collections.Counter, actual: collections.Counter) -> List[dict]:
    return V["missing_counter"](expected, actual)


def _event_handler_bodies(src: str) -> Dict[Tuple[str, str], collections.Counter]:
    """Map event receiver/name to exact subscribed expression token bodies."""
    tokens = V["csharp_tokens"](src)
    values = [t.value for t in tokens]
    names = set(V["EVENT_NAMES"]) | {"SelectedChanged"}
    found: Dict[Tuple[str, str], collections.Counter] = collections.defaultdict(collections.Counter)
    for i in range(len(tokens) - 3):
        if values[i] != "." or values[i + 1] not in names or values[i + 2] != "+=":
            continue
        start = i - 1
        if start < 0 or tokens[start].kind != "id":
            receiver = "?"
        else:
            while start >= 2 and values[start - 1] in {".", "?."} and tokens[start - 2].kind == "id":
                start -= 2
            receiver = ".".join(t.value for t in tokens[start:i] if t.value not in {".", "?."})
        j = i + 3
        parens = brackets = braces = 0
        while j < len(tokens):
            value = values[j]
            if value == "(": parens += 1
            elif value == ")": parens -= 1
            elif value == "[": brackets += 1
            elif value == "]": brackets -= 1
            elif value == "{": braces += 1
            elif value == "}": braces -= 1
            if value == ";" and parens == brackets == braces == 0:
                break
            j += 1
        expression = tuple(values[i + 3:j])
        found[(receiver, values[i + 1])][expression] += 1
    return found


def _event_handler_checks(baseline_root: Path, candidate_root: Path, paths: Iterable[str]) -> List[dict]:
    checks = []
    for rel in paths:
        base_src, cand_src = _read(baseline_root / rel), _read(candidate_root / rel)
        if base_src is None or cand_src is None:
            checks.append({"path": rel, "passed": False, "issue": "source missing/unreadable"})
            continue
        base, cand = _event_handler_bodies(base_src), _event_handler_bodies(cand_src)
        missing, changed = [], []
        for key, expressions in base.items():
            absent = expressions - cand.get(key, collections.Counter())
            if not absent:
                continue
            same_key_exists = bool(cand.get(key))
            detail = {"receiver": key[0], "event": key[1],
                      "missing_expression_count": sum(absent.values()),
                      "baseline_expressions": [list(expr) for expr in absent],
                      "candidate_expressions": [list(expr) for expr in cand.get(key, {})]}
            (changed if same_key_exists else missing).append(detail)
        item = {"path": rel, "passed": not missing and not changed,
                "baseline_handler_count": sum(sum(x.values()) for x in base.values()),
                "candidate_handler_count": sum(sum(x.values()) for x in cand.values()),
                "missing_handlers": missing, "changed_handler_expressions": changed,
                "comparison": "Exact C# subscribed-expression token bodies keyed by receiver/event; includes SelectedChanged."}
        checks.append(item)
    return checks


def _compare_method_groups(baseline: Dict, candidate: Dict, *, path: str,
                           class_name: str, excluded_names: Iterable[str] = ()) -> dict:
    excluded = set(excluded_names)
    checks, violations = [], []
    base_by_name: Dict[str, Dict[Tuple[str, Tuple[str, ...]], Tuple[str, ...]]] = collections.defaultdict(dict)
    cand_by_name: Dict[str, Dict[Tuple[str, Tuple[str, ...]], Tuple[str, ...]]] = collections.defaultdict(dict)
    for key, body in baseline.items():
        base_by_name[key[0]][key] = body
    for key, body in candidate.items():
        cand_by_name[key[0]][key] = body
    for name in sorted(base_by_name):
        if name in excluded:
            continue
        old, new = base_by_name[name], cand_by_name.get(name, {})
        if not new:
            item = {"method": name, "passed": False, "issue": "baseline method missing"}
        elif old.keys() != new.keys():
            item = {"method": name, "passed": False, "issue": "signature missing or changed",
                    "baseline_signatures": [list(k[1]) for k in old],
                    "candidate_signatures": [list(k[1]) for k in new]}
        else:
            changed = [list(k[1]) for k in old if old[k] != new[k]]
            item = {"method": name, "passed": not changed, "changed_signatures": changed,
                    "comparison": "C# method-body token equality; comments/whitespace ignored, literals retained"}
        checks.append(item)
        if not item["passed"]:
            violations.append({"category": "frozen_logic_method_changed", "path": path,
                               "class": class_name, **item})
    return {
        "path": path, "class": class_name,
        "baseline_method_count": sum(len(x) for x in base_by_name.values()),
        "candidate_method_count": len(candidate),
        "excluded_visual_methods": sorted(excluded),
        "checks": checks, "violations": violations,
        "passed": all(x["passed"] for x in checks),
    }


def _method_body_for_name(methods: Dict, name: str):
    matches = [(key, body) for key, body in methods.items() if key[0] == name]
    return matches


def _call_arguments(tokens: Sequence, at: int) -> Optional[Tuple[str, ...]]:
    if at >= len(tokens) or tokens[at].value != "(":
        return None
    end = V["matching_paren"](tokens, at)
    if end is None:
        return None
    return tuple(t.value for t in tokens[at + 1:end])


def _constructor_body_tokens(methods: Dict, class_name: str) -> Optional[Tuple[str, ...]]:
    matches = _method_body_for_name(methods, class_name)
    if not matches:
        return None
    return matches[0][1]


def _value(token) -> str:
    return token.value if hasattr(token, "value") else str(token)


def _created_card_names(body: Sequence) -> List[str]:
    """Return CardPanel variable/field names created in a panel constructor."""
    names = []
    vals = [_value(t) for t in body]
    for i in range(len(vals) - 2):
        if vals[i] != "new" or vals[i + 1].lstrip("@") != "CardPanel":
            continue
        start = i - 1
        while start >= 0 and vals[start] not in {";", "{", "}", ","}:
            start -= 1
        segment = vals[start + 1:i]
        first_eq = next((j for j, value in enumerate(segment) if value == "="), None)
        if first_eq is not None and first_eq > 0 and re.fullmatch(r"@?[A-Za-z_][A-Za-z0-9_]*", segment[first_eq - 1]):
            names.append(segment[first_eq - 1].lstrip("@"))
    return names


def _direct_form_add(tokens: Sequence, child: str) -> bool:
    vals = [_value(t) for t in tokens]
    child = child.lstrip("@")
    for i in range(len(vals) - 5):
        if vals[i:i + 5] != ["Controls", ".", "Add", "(", child]:
            continue
        # Do not accept another CardPanel's Controls.Add. this.Controls is the
        # Form's own collection; unqualified Controls is also the Form property.
        if i > 0 and vals[i - 1] == ".":
            receiver = vals[i - 2] if i > 1 else ""
            if receiver != "this":
                continue
        if i + 5 < len(vals) and vals[i + 5] == ")":
            return True
    return False


def _scroll_helper_is_direct(methods: Dict) -> bool:
    for key, body in methods.items():
        if key[0] != "AddScrollCard":
            continue
        if len(key[1]) < 2 or "CardPanel" not in key[1]:
            continue
        if _direct_form_add(body, "card"):
            return True
    return False


def _scroll_owner_audit(baseline_root: Path, candidate_root: Path) -> dict:
    checks, violations = [], []
    for rel, class_name in PANEL_TYPES.items():
        bsrc, bmethods, btokens = _method_map(baseline_root, rel, class_name)
        csrc, cmethods, ctokens = _method_map(candidate_root, rel, class_name)
        bbody, cbody = _constructor_body_tokens(bmethods, class_name), _constructor_body_tokens(cmethods, class_name)
        row = {"path": rel, "class": class_name, "passed": False}
        if bsrc is None or csrc is None or bbody is None or cbody is None:
            row["issue"] = "source/class/constructor missing"
            violations.append({"category": "scroll_card_source_missing", **row})
            checks.append(row)
            continue
        base_names, cand_names = _created_card_names(bbody), _created_card_names(cbody)
        cv = [t.value.lower() for t in ctokens]
        parent_assignment = any(cv[i:i + 4] == ["scrollbody", ".", "parent", "="]
                                for i in range(max(0, len(cv) - 3)))
        helper_direct = _scroll_helper_is_direct(cmethods)
        card_routes = []
        for name in cand_names:
            direct = _direct_form_add(cbody, name)
            helper = any(
                _value(cbody[i]) == "AddScrollCard" and i + 1 < len(cbody)
                and _value(cbody[i + 1]) == "(" and i + 2 < len(cbody)
                and _value(cbody[i + 2]).lstrip("@") == name
                for i in range(len(cbody))
            )
            card_routes.append({"card": name, "direct_form_add": direct,
                                "through_addscrollcard": helper and helper_direct,
                                "passed": direct or (helper and helper_direct)})
        same_count = len(cand_names) >= len(base_names)
        all_direct = all(x["passed"] for x in card_routes)
        row.update({
            "baseline_card_count": len(base_names),
            "candidate_card_count": len(cand_names),
            "baseline_card_sequence": base_names,
            "candidate_card_sequence": cand_names,
            "candidate_cards_are_direct_form_children": card_routes,
            "addscrollcard_helper_adds_to_form_controls": helper_direct,
            "forbidden_scrollbody_parent_assignment": parent_assignment,
            "preserves_all_baseline_cards": same_count,
            "passed": same_count and all_direct and not parent_assignment,
        })
        if not row["passed"]:
            violations.append({"category": "scroll_card_parenting_contract", **row})
        checks.append(row)

    # Voronoi's segmented choices are functional controls, not scroll wrappers.
    rel = "voronoi/src/VoronoiUi.cs"
    def segmented_owners(root: Path):
        src, methods, _ = _method_map(root, rel, PANEL_TYPES[rel])
        body = _constructor_body_tokens(methods, PANEL_TYPES[rel]) or ()
        vals = [_value(t).lstrip("@") for t in body]
        owners = {}
        for i in range(len(vals) - 5):
            if vals[i + 1:i + 4] == [".", "Controls", "."] and vals[i + 4] == "Add" and i + 6 < len(vals) and vals[i + 5] == "(":
                end = i + 6
                child = vals[end] if end < len(vals) else ""
                if child.startswith("_seg"):
                    owners[child] = vals[i]
        return owners
    bowners, cowners = segmented_owners(baseline_root), segmented_owners(candidate_root)
    expected = {"_segShape", "_segFace", "_segCell"}
    seg_missing = sorted(expected - set(bowners))
    seg_changed = {name: {"baseline_parent": bowners.get(name), "candidate_parent": cowners.get(name)}
                   for name in sorted(expected) if bowners.get(name) != cowners.get(name)}
    seg_row = {
        "path": rel,
        "baseline_segmented_control_owners": bowners,
        "candidate_segmented_control_owners": cowners,
        "required_control_names": sorted(expected),
        "missing_from_baseline_scope_scan": seg_missing,
        "reparented_or_missing_controls": seg_changed,
        "passed": not seg_missing and not seg_changed,
        "interpretation": "Static owner scan preserves _segShape in its original shape card and _segFace/_segCell in the original reference/output scope card.",
    }
    if not seg_row["passed"]:
        violations.append({"category": "voronoi_segmented_control_scope_changed", **seg_row})
    return {"passed": not violations, "panels": checks, "voronoi_segmented_control_scope": seg_row,
            "violations": violations}


def _renderer_audit(candidate_root: Path, baseline_root: Path) -> dict:
    theme_src, theme_methods, _ = _method_map(candidate_root, "shared/PanelTheme.cs", "Theme")
    program_src, program_methods, _ = _method_map(candidate_root, ICONMAKE_PATH, "Program")
    baseline_program_src, baseline_program, _ = _method_map(baseline_root, ICONMAKE_PATH, "Program")
    renderer_names = sorted({k[0] for k in theme_methods if k[0].startswith("Glass") or k[0] == "CreateGlassIcon"})
    theme_glass = {k: v for k, v in theme_methods.items() if k[0] in renderer_names}
    program_glass = {k: v for k, v in program_methods.items() if k[0] in renderer_names}
    missing = [list(k) for k in theme_glass.keys() - program_glass.keys()]
    extra = [list(k) for k in program_glass.keys() - theme_glass.keys()]
    changed = [list(k) for k in theme_glass.keys() & program_glass.keys() if theme_glass[k] != program_glass[k]]
    theme_copy_parity = []
    for rel in THEME_PATHS:
        _, methods, _ = _method_map(candidate_root, rel, "Theme")
        candidate = {k: v for k, v in methods.items() if k[0] in renderer_names}
        diffs = [list(k) for k in theme_glass.keys() if candidate.get(k) != theme_glass[k]]
        theme_copy_parity.append({"path": rel, "passed": not diffs, "renderer_differences": diffs})

    copy_hashes = {rel: _hash(candidate_root / rel, "md5") for rel in THEME_PATHS}
    unique_theme_md5 = sorted(set(copy_hashes.values()))
    motion_hashes = {}
    for rel in THEME_PATHS:
        src = _read(candidate_root / rel)
        info = V["find_type"](src, V["csharp_tokens"](src), "Motion") if src else None
        motion_hashes[rel] = hashlib.md5(info["raw"].encode("utf-8")).hexdigest() if info else None
    motion_unique = sorted(set(motion_hashes.values()))

    utility_checks = []
    for name in ("ToDib", "WriteIco"):
        old = {k: v for k, v in baseline_program.items() if k[0] == name}
        now = {k: v for k, v in program_methods.items() if k[0] == name}
        passed = bool(old) and old == now
        utility_checks.append({"method": name, "passed": passed,
                               "comparison": "baseline vs candidate C# method-body token equality"})
    draw_methods = _method_body_for_name(program_methods, "Draw")
    draw_tokens = draw_methods[0][1] if draw_methods else ()
    expected_draw = ("{", "return", "CreateGlassIcon", "(", '"app"', ",", "size", ")", ";", "}")
    draw_app = tuple(draw_tokens) == expected_draw
    sizes = [16, 24, 32, 48, 64, 128, 256]
    size_sequence = tuple(map(str, sizes))
    size_declared = any("16" in method_body and
                        all(str(n) in method_body for n in size_sequence)
                        for key, method_body in program_methods.items() if key[0] == "Main")
    expected_program_hash = "95d54d1720d6cd290832b8082ed673a0a64192a84ee49493be1d6f427125b456"
    actual_program_hash = _hash(candidate_root / ICONMAKE_PATH)
    return {
        "theme_to_iconmake_renderer_parity": {
            "passed": not missing and not extra and not changed,
            "renderer_method_count": len(renderer_names),
            "missing_from_iconmake": missing,
            "extra_in_iconmake": extra,
            "body_differences": changed,
            "methods": renderer_names,
            "theme_copy_parity": theme_copy_parity,
        },
        "theme_file_md5": {"hashes": copy_hashes, "unique_md5": unique_theme_md5,
                           "six_copies_identical": len(unique_theme_md5) == 1},
        "theme_motion_md5": {"hashes": motion_hashes, "unique_md5": motion_unique,
                              "six_motion_classes_identical": len(motion_unique) == 1},
        "iconmake_integrity": {
            "utility_method_checks": utility_checks,
            "draw_uses_app_renderer": draw_app,
            "draw_body_tokens": list(draw_tokens),
            "expected_draw_body_tokens": list(expected_draw),
            "required_ico_sizes": sizes,
            "size_list_present": size_declared,
            "candidate_program_sha256": actual_program_hash,
            "previous_app-only_candidate_sha256": expected_program_hash,
            "program_hash_matches_previous_app-only_observation": actual_program_hash == expected_program_hash,
        },
        "historical_plugin_renderer_preservation": {
            "status": "previously_observed; old pre-app Program bytes are not retained in this workspace",
            "pre_app_program_sha256": "7a9a775cd603e7a8fa52afe1e60a82f88623c4069b1ae41e03d6a064536e3b0b",
            "app_only_program_sha256": expected_program_hash,
            "plugin_renderer_helpers_previously_compared_unchanged": sorted(PLUGIN_RENDERER_METHODS),
            "app_only_methods_allowed_to_differ": sorted(APP_ONLY_RENDERER_METHODS),
            "current_theme_iconmake_renderer_parity_passed": not missing and not extra and not changed,
            "caveat": "This historical delta is carried forward from the earlier observed comparison; this run freshly verifies only current Theme/IconMake parity.",
        },
    }


def run_audit(report_path: Path, manifest_path: Path = LATEST_MANIFEST_PATH) -> dict:
    # Prefer the current frozen-source extension; the older manifest remains an
    # explicit fallback for reproducing an earlier audit snapshot.
    if not manifest_path.is_file():
        manifest_path = LEGACY_MANIFEST_PATH
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    baseline_root, candidate_root = Path(manifest["baseline"]), Path(manifest["candidate"])
    entries = manifest.get("files", [])
    manifest_paths = {e["relative"].replace("\\", "/") for e in entries}
    allowed = set(THEME_PATHS) | set(PANEL_TYPES) | {VOLUME_PATH, CENTER_PATH, INSTALLER_PATH, ICONMAKE_PATH,
                                                     "stripe/center/app.ico"}
    fidelity = {"baseline_file_count": len(entries), "changed_paths": [], "unexpected_modified_paths": [],
                "missing_paths": [], "ignored_generated_paths": [], "unexpected_candidate_sources": [],
                "baseline_integrity_issues": []}
    violations = []
    for entry in entries:
        rel = entry["relative"].replace("\\", "/")
        bp, cp = baseline_root / rel, candidate_root / rel
        if not bp.is_file():
            fidelity["baseline_integrity_issues"].append({"path": rel, "issue": "baseline_missing"})
            continue
        base_sha = _hash(bp)
        if base_sha != entry.get("sha256", "").lower():
            fidelity["baseline_integrity_issues"].append({"path": rel, "issue": "manifest_hash_mismatch"})
        if not cp.is_file():
            fidelity["missing_paths"].append(rel)
            violations.append({"category": "missing_baseline_file", "path": rel})
            continue
        candidate_sha = _hash(cp)
        if candidate_sha != base_sha:
            fidelity["changed_paths"].append(rel)
            if rel not in allowed:
                fidelity["unexpected_modified_paths"].append(rel)
                violations.append({"category": "frozen_file_changed", "path": rel,
                                   "baseline_sha256": base_sha, "candidate_sha256": candidate_sha})
    candidate_sources = {p.relative_to(candidate_root).as_posix() for p in candidate_root.rglob("*")
                         if p.is_file() and p.suffix.lower() in {".cs", ".csproj", ".manifest"}}
    generated_parts = {"bin", "obj", "out"}
    for rel in sorted(candidate_sources - manifest_paths):
        if any(part.lower() in generated_parts for part in Path(rel).parts):
            fidelity["ignored_generated_paths"].append(rel)
        else:
            fidelity["unexpected_candidate_sources"].append(rel)
            violations.append({"category": "unexpected_candidate_source", "path": rel})
    if fidelity["baseline_integrity_issues"]:
        violations.extend({"category": "baseline_integrity", **x} for x in fidelity["baseline_integrity_issues"])

    # Reuse the source-freeze helper's token contracts for public API, settings,
    # events, AddRow signatures, installer boundary, and frozen methods.
    contract_paths = sorted(set(THEME_PATHS + list(PANEL_TYPES) + [VOLUME_PATH, CENTER_PATH, INSTALLER_PATH]))
    contract_checks = {"public_members": [], "setting_assignments_and_ranges": [],
                       "event_bindings": [], "event_handler_bodies": [], "settings_rows": []}
    for rel in contract_paths:
        bsrc, csrc = _read(baseline_root / rel), _read(candidate_root / rel)
        if bsrc is None or csrc is None:
            continue
        bt, ct = V["csharp_tokens"](bsrc), V["csharp_tokens"](csrc)
        for field, fn, category in [
            ("public_members", "public_contracts", "public_member_missing"),
            ("setting_assignments_and_ranges", "setting_assignments", "setting_or_range_missing"),
            ("event_bindings", "event_bindings", "event_binding_missing"),
        ]:
            missing = _missing(V[fn](bt), V[fn](ct))
            item = {"path": rel, "passed": not missing, "missing": missing}
            contract_checks[field].append(item)
            if missing:
                violations.append({"category": category, **item})
        if rel in PANEL_TYPES:
            missing = _missing(V["addrow_contracts"](bt), V["addrow_contracts"](ct))
            item = {"path": rel, "passed": not missing, "missing": missing}
            contract_checks["settings_rows"].append(item)
            if missing:
                violations.append({"category": "settings_row_or_numeric_contract_missing", **item})

    handler_paths = list(PANEL_TYPES) + [VOLUME_PATH, CENTER_PATH]
    contract_checks["event_handler_bodies"] = _event_handler_checks(baseline_root, candidate_root, handler_paths)
    for item in contract_checks["event_handler_bodies"]:
        if not item["passed"]:
            violations.append({"category": "event_handler_body_changed_or_missing", **item})

    logic_checks = []
    for rel, class_name in PANEL_TYPES.items():
        _, bm, _ = _method_map(baseline_root, rel, class_name)
        _, cm, _ = _method_map(candidate_root, rel, class_name)
        result = _compare_method_groups(bm, cm, path=rel, class_name=class_name,
                                        excluded_names={class_name})
        logic_checks.append(result)
        violations.extend(result["violations"])

    _, base_volume, _ = _method_map(baseline_root, VOLUME_PATH, "VolumeDialog")
    _, cand_volume, _ = _method_map(candidate_root, VOLUME_PATH, "VolumeDialog")
    base_volume_src, cand_volume_src = _read(baseline_root / VOLUME_PATH), _read(candidate_root / VOLUME_PATH)
    volume_type = {"path": VOLUME_PATH,
                   "baseline_header_tokens": list(_type_header(base_volume_src, "VolumeDialog") or ()),
                   "candidate_header_tokens": list(_type_header(cand_volume_src, "VolumeDialog") or ())}
    volume_type["passed"] = bool(volume_type["baseline_header_tokens"]) and (
        volume_type["baseline_header_tokens"] == volume_type["candidate_header_tokens"])
    if not volume_type["passed"]:
        violations.append({"category": "volume_dialog_type_signature_changed", **volume_type})
    volume_excluded = {k[0] for k in base_volume if k[0] in VOLUME_VISUAL_NAMES or
                       any(k[0].startswith(p) for p in VOLUME_VISUAL_PREFIXES)} | {"VolumeDialog"}
    volume_result = _compare_method_groups(base_volume, cand_volume, path=VOLUME_PATH,
                                           class_name="VolumeDialog", excluded_names=volume_excluded)
    violations.extend(volume_result["violations"])

    _, base_center, _ = _method_map(baseline_root, CENTER_PATH, "CenterForm")
    _, cand_center, _ = _method_map(candidate_root, CENTER_PATH, "CenterForm")
    center_excluded = {"CenterForm"} | CENTER_VISUAL_METHODS
    center_result = _compare_method_groups(base_center, cand_center, path=CENTER_PATH,
                                           class_name="CenterForm", excluded_names=center_excluded)
    violations.extend(center_result["violations"])

    installer_result = {"passed": False, "outside_iconfactory_byte_identical": False}
    bsrc, csrc = _read(baseline_root / INSTALLER_PATH), _read(candidate_root / INSTALLER_PATH)
    if bsrc and csrc:
        bt, ct = V["csharp_tokens"](bsrc), V["csharp_tokens"](csrc)
        bi, ci = V["find_type"](bsrc, bt, "IconFactory"), V["find_type"](csrc, ct, "IconFactory")
        if bi and ci:
            same_outside = bsrc[:bi["start"]] + "\0" + bsrc[bi["end"]:] == csrc[:ci["start"]] + "\0" + csrc[ci["end"]:]
            installer_result = {"passed": same_outside, "outside_iconfactory_byte_identical": same_outside}
            if not same_outside:
                violations.append({"category": "installer_changed_outside_iconfactory", "path": INSTALLER_PATH})
        else:
            installer_result["issue"] = "IconFactory type missing"
            violations.append({"category": "installer_iconfactory_missing", "path": INSTALLER_PATH})

    units = {"path": UNITS_PATH,
             "baseline_sha256": _hash(baseline_root / UNITS_PATH),
             "candidate_sha256": _hash(candidate_root / UNITS_PATH)}
    units["byte_identical"] = units["baseline_sha256"] is not None and units["baseline_sha256"] == units["candidate_sha256"]
    if not units["byte_identical"]:
        violations.append({"category": "units_source_changed", **units})

    latest_core_imports = []
    latest_baseline_root = ROOT / "BASELINE_LATEST"
    for rel in LATEST_CORE_IMPORTS:
        candidate_sha = _hash(candidate_root / rel)
        latest_sha = _hash(latest_baseline_root / rel)
        row = {"path": rel, "candidate_sha256": candidate_sha,
               "baseline_latest_sha256": latest_sha,
               "byte_identical_to_baseline_latest": candidate_sha is not None and candidate_sha == latest_sha}
        latest_core_imports.append(row)
        if not row["byte_identical_to_baseline_latest"]:
            violations.append({"category": "latest_core_import_not_frozen", **row})

    scroll = _scroll_owner_audit(baseline_root, candidate_root)
    violations.extend(scroll["violations"])
    renderer = _renderer_audit(candidate_root, baseline_root)
    renderer_checks = renderer["theme_to_iconmake_renderer_parity"]
    if not renderer_checks["passed"]:
        violations.append({"category": "glass_renderer_source_diverged", **renderer_checks})
    if not renderer["theme_file_md5"]["six_copies_identical"]:
        violations.append({"category": "theme_copy_md5_mismatch", **renderer["theme_file_md5"]})
    if not renderer["theme_motion_md5"]["six_motion_classes_identical"]:
        violations.append({"category": "theme_motion_md5_mismatch", **renderer["theme_motion_md5"]})
    for utility in renderer["iconmake_integrity"]["utility_method_checks"]:
        if not utility["passed"]:
            violations.append({"category": "iconmake_utility_method_changed", **utility})
    for key in ("draw_uses_app_renderer", "size_list_present"):
        if not renderer["iconmake_integrity"][key]:
            violations.append({"category": "iconmake_app_icon_contract", "check": key})

    # The source-freeze check also reports all modified and generated candidate
    # files; no generated bin/obj/out sources are treated as hand-authored edits.
    passed = not violations
    report = {
        "schema_version": 1,
        "generated_at": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
        "mode": "text-only; no compile, Rhino launch, or source edits",
        "baseline_manifest": str(manifest_path.resolve()),
        "baseline_root": str(baseline_root.resolve()),
        "candidate_root": str(candidate_root.resolve()),
        "summary": {"passed": passed, "violation_count": len(violations),
                    "baseline_file_count": len(entries),
                    "changed_allowed_files": sorted(set(fidelity["changed_paths"]) & allowed),
                    "unexpected_modified_files": fidelity["unexpected_modified_paths"],
                    "missing_baseline_files": fidelity["missing_paths"],
                    "unexpected_candidate_source_files": fidelity["unexpected_candidate_sources"],
                    "ignored_generated_source_files": fidelity["ignored_generated_paths"]},
        "file_fidelity": fidelity,
        "contracts": {
            **contract_checks,
            "panel_logic_methods": logic_checks,
            "volumedialog_nonvisual_logic": volume_result,
            "volumedialog_type_signature": volume_type,
            "centerform_operation_methods": center_result,
            "installer_outside_iconfactory": installer_result,
            "units_file": units,
            "latest_core_imports": {"baseline_root": str(latest_baseline_root.resolve()),
                                    "paths": latest_core_imports,
                                    "all_byte_identical": all(x["byte_identical_to_baseline_latest"] for x in latest_core_imports)},
            "scroll_hierarchy": scroll,
            "glass_renderers": renderer,
        },
        "violations": violations,
        "caveats": [
            "Static source audit only; no build or Rhino runtime verification is performed.",
            "Method-body comparisons use C# tokens, ignoring comments and whitespace while retaining literals.",
            "Scroll checks prove source-level direct Controls.Add/AddScrollCard ownership and unchanged Voronoi segmented-control card owners; they do not simulate WinForms clipping or mouse behavior.",
            "The pre-app icon renderer comparison is carried forward as an earlier observed result because that prior Program source is not retained; current Theme/IconMake parity is freshly recomputed.",
        ],
    }
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_REPORT)
    parser.add_argument("--manifest", type=Path, default=LATEST_MANIFEST_PATH,
                        help="frozen-source manifest (defaults to source-latest.json, falls back to source-baseline.json)")
    args = parser.parse_args()
    report = run_audit(args.output, args.manifest)
    print(json.dumps(report["summary"], ensure_ascii=False, indent=2))
    print("Report:", args.output.resolve())
    return 0 if report["summary"]["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
