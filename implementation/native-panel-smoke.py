# -*- coding: utf-8 -*-
"""Native UI-only smoke check for the five Rhino plugin panels.

Run inside Rhino's IronPython 2 `RunPythonScript` command in a new, empty
document. It does not pick, generate, or change user objects. It clicks only
VapeVolume's no-selection Rebuild (guarded by zero objects) and Close buttons,
and invokes VapeVolumeSelfTest, whose temporary geometry must be cleaned up.
It writes PNGs, JSON reports, and a failure summary beside itself.
"""

import datetime
import hashlib
import io
import json
import os
import re
import shutil
import sys
import time
import traceback

import clr
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

from System.Drawing import Bitmap, Graphics, Point, Size
from System.Drawing.Imaging import ImageFormat
from System.Windows.Forms import Application, ButtonBase, Control, Form, NumericUpDown
from System.Threading import Thread

import Rhino

# VapeVolume is Eto-based; on Rhino for Windows its Eto peer is normally WPF,
# not a WinForms Form. Load the WPF assemblies opportunistically so a missing
# backend remains an explicit unverified result instead of breaking the other
# four WinForms panel checks.
WPF_AVAILABLE = False
WPF_IMPORT_ERROR = None
WpfDependencyObject = None
WpfFrameworkElement = None
WpfWindow = None
WpfPoint = None
WpfVisualTreeHelper = None
WpfButtonBase = None
WpfBorder = None
WpfRoutedEventArgs = None
WpfVisibility = None
try:
    clr.AddReference("WindowsBase")
    clr.AddReference("PresentationCore")
    clr.AddReference("PresentationFramework")
    from System.Windows import DependencyObject as WpfDependencyObject
    from System.Windows import FrameworkElement as WpfFrameworkElement
    from System.Windows import Point as WpfPoint
    from System.Windows import RoutedEventArgs as WpfRoutedEventArgs
    from System.Windows import Visibility as WpfVisibility
    from System.Windows import Window as WpfWindow
    from System.Windows.Controls import Border as WpfBorder
    from System.Windows.Controls.Primitives import ButtonBase as WpfButtonBase
    from System.Windows.Media import VisualTreeHelper as WpfVisualTreeHelper
    WPF_AVAILABLE = True
except Exception as ex:
    WPF_IMPORT_ERROR = str(ex)


try:
    _SCRIPT_PATH = __file__
except NameError:
    _SCRIPT_PATH = r"E:\IVAN-LiquidGlass-preview\implementation\native-panel-smoke.py"
OUTPUT_DIR = os.path.dirname(os.path.abspath(_SCRIPT_PATH))
WAIT_FOR_WINDOW_SECONDS = 12.0
WAIT_FOR_CLOSE_SECONDS = 3.0
SHOW_SETTLE_SECONDS = 0.35
# Leave Rhino running by default so the operator can inspect the report/session.
# In a disposable run, the operator may set this True to issue the safe exit macro.
EXIT_RHINO_WHEN_DONE = True

PLUGINS = [
    {"name": "VoronoiTexture", "command": "VoronoiTexture", "form_type": "VoronoiPanel", "window_kind": "winforms"},
    {"name": "StripeOnSurface", "command": "StripeOnSurface", "form_type": "StripePanel", "window_kind": "winforms"},
    {"name": "ParametricTexture", "command": "ParametricTexture", "form_type": "HalftonePanel", "window_kind": "winforms"},
    {"name": "RadialDots", "command": "RadialDots", "form_type": "RadialDotsPanel", "window_kind": "winforms"},
    {"name": "VapeVolume", "command": "VapeVolume", "form_type": "VolumeDialog", "window_kind": "eto"},
]

EXPECTED_CATEGORIES = {
    "VoronoiTexture": ["buttons", "numericFields", "checkboxes", "segmented"],
    "StripeOnSurface": ["buttons", "numericFields", "checkboxes"],
    # On the empty-document fixture the halftone panel has FaceCount == 1.
    # Its scope card (including the IvanSegmented control) is intentionally
    # hidden; the ten visible shape tiles are the relevant shape selector.
    "ParametricTexture": ["buttons", "numericFields", "checkboxes", "tiles"],
    "RadialDots": ["buttons", "numericFields", "checkboxes", "segmented"],
    "VapeVolume": ["buttons", "numericFields", "checkboxes"],
}

EXPECTED_TOTAL_CATEGORIES = {
    "ParametricTexture": ["segmented"],
}

EXPECTED_CATEGORY_MINIMUMS = {
    # 10 cells are the visible shape-selector tiles on a single-face fixture.
    "ParametricTexture": {"tiles": 10, "segmented": 1},
}

EXPECTED_VISIBLE_MINIMUMS = {
    "ParametricTexture": {"tiles": 10},
}


def _type_name(obj):
    try:
        return str(obj.GetType().Name)
    except Exception:
        try:
            return str(type(obj).__name__)
        except Exception:
            return "<unknown>"


def _safe_text(value):
    try:
        return str(value)
    except Exception:
        try:
            return repr(value)
        except Exception:
            return "<unprintable>"


def _safe_get(obj, name, default=None):
    try:
        return getattr(obj, name)
    except Exception:
        return default


def _winforms_forms():
    try:
        return [form for form in Application.OpenForms]
    except Exception:
        return []


def _find_winforms_form(type_name):
    forms = _winforms_forms()
    for form in reversed(forms):
        if _type_name(form) == type_name:
            return form
        if type_name == "VolumeDialog" and _safe_text(_safe_get(form, "Text", "")) == u"烟油容量":
            return form
    return None


def _eto_application():
    try:
        from Eto.Forms import Application as EtoApplication
        return EtoApplication
    except Exception:
        return None


def _eto_windows():
    result = {"available": False, "source": "Eto.Forms.Application.Instance.Windows", "error": None}
    app_type = _eto_application()
    if app_type is None:
        result["error"] = "Eto.Forms.Application import unavailable in this Rhino runtime."
        return [], result
    try:
        app = app_type.Instance
        windows = getattr(app, "Windows")
        items = [window for window in windows]
        result["available"] = True
        result["count"] = len(items)
        return items, result
    except Exception as ex:
        result["error"] = _safe_text(ex)
        return [], result


def _find_eto_window(type_name):
    windows, query = _eto_windows()
    for window in reversed(windows):
        title = _safe_text(_safe_get(window, "Title", ""))
        if _type_name(window) == type_name:
            return window, query
        # The source title is Simplified Chinese; keep a second exact spelling
        # check in case the IronPython/Eto bridge returns a Unicode title.
        if type_name == "VolumeDialog" and title == u"烟油容量":
            return window, query
    return None, query


def _enumerate_values(value):
    if value is None or isinstance(value, basestring):
        return []
    try:
        return [item for item in value]
    except Exception:
        return []


def _is_wpf_native_control(obj):
    if obj is None:
        return False
    if WpfDependencyObject is not None:
        try:
            if isinstance(obj, WpfDependencyObject):
                return True
        except Exception:
            pass
    try:
        namespace = str(obj.GetType().Namespace or "")
        return namespace.startswith("System.Windows") and \
            _safe_get(obj, "ActualWidth", None) is not None and \
            _safe_get(obj, "ActualHeight", None) is not None and \
            _safe_get(obj, "PointToScreen", None) is not None
    except Exception:
        return False


def _is_wpf_window(obj):
    if obj is None:
        return False
    if WpfWindow is not None:
        try:
            return isinstance(obj, WpfWindow)
        except Exception:
            pass
    return _type_name(obj) == "Window" and _is_wpf_native_control(obj)


def _native_control_for_eto(window):
    """Best-effort Eto -> native WinForms or WPF root lookup; never creates one."""
    queue = [(window, 0, "window")]
    seen = set()
    attrs = ("Handler", "Control", "ControlObject", "Form", "NativeControl",
             "Widget", "Window", "Host", "Element")
    first_wpf = None
    while queue:
        obj, depth, via = queue.pop(0)
        if obj is None or depth > 5:
            continue
        try:
            ident = int(obj.GetHashCode())
        except Exception:
            ident = id(obj)
        if ident in seen:
            continue
        seen.add(ident)
        try:
            if isinstance(obj, Control):
                form = obj if isinstance(obj, Form) else obj.FindForm()
                if form is not None:
                    return form, via, "winforms"
        except Exception:
            pass

        if _is_wpf_window(obj):
            return obj, via, "wpf"
        if first_wpf is None and _is_wpf_native_control(obj):
            first_wpf = (obj, via)

        for attr in attrs:
            child = _safe_get(obj, attr, None)
            if child is not None and not isinstance(child, basestring):
                queue.append((child, depth + 1, via + "." + attr))

        # Eto WinForms handlers expose their Control through a backend-specific
        # property. Inspect zero-argument WinForms properties, then verify objects
        # with isinstance before adding them to the search queue.
        try:
            for prop in obj.GetType().GetProperties():
                try:
                    if prop.GetIndexParameters().Length != 0:
                        continue
                    property_namespace = str(prop.PropertyType.Namespace or "")
                    if not (property_namespace.startswith("System.Windows.Forms") or
                            property_namespace.startswith("System.Windows")):
                        continue
                    child = prop.GetValue(obj, None)
                    if child is not None and (isinstance(child, Control) or _is_wpf_native_control(child)):
                        queue.append((child, depth + 1, via + "." + str(prop.Name)))
                except Exception:
                    continue
        except Exception:
            pass
    if first_wpf is not None:
        return first_wpf[0], first_wpf[1], "wpf"
    return None, "not exposed by the Eto backend", None


def _find_eto_native_form(type_name):
    # First use Eto's own open-window collection. Then inspect handler/control
    # peers for the actual WinForms or WPF native root used to render the window.
    window, query = _find_eto_window(type_name)
    if window is not None:
        native, via, backend = _native_control_for_eto(window)
        if native is not None:
            return window, native, query, via, backend
        fallback = _find_winforms_form(type_name)
        if fallback is not None:
            query["winformsFallback"] = True
            return window, fallback, query, "System.Windows.Forms.Application.OpenForms fallback", "winforms"
        # Eto window is present but no native peer is exposed; callers must keep
        # the native-boundary audit explicitly unverified.
        return window, None, query, via, None

    # Some Rhino Eto builds surface the Eto dialog's WinForms peer directly in
    # Application.OpenForms. Use that only as a documented fallback.
    form = _find_winforms_form(type_name)
    if form is not None:
        query["winformsFallback"] = True
        return form, form, query, "System.Windows.Forms.Application.OpenForms", "winforms"
    return None, None, query, "not found", None


def _pump_events(seconds):
    deadline = time.time() + max(0.0, seconds)
    while time.time() < deadline:
        try:
            Application.DoEvents()
        except Exception:
            pass
        Thread.Sleep(20)


def _show_and_redraw(window, native_form, native_kind=None):
    shown = False
    errors = []
    try:
        window.Show()
        shown = True
    except Exception as ex:
        errors.append("Show: " + _safe_text(ex))
    if native_form is not None:
        try:
            if native_kind == "wpf":
                native_form.Activate()
            else:
                native_form.Activate()
        except Exception:
            pass
    redraws = 0
    for index in range(2):
        try:
            window.Invalidate(True)
        except Exception:
            try:
                window.Invalidate()
            except Exception:
                pass
        try:
            window.Refresh()
        except Exception:
            pass
        if native_form is not None:
            try:
                if native_kind == "wpf":
                    native_form.InvalidateVisual()
                    native_form.UpdateLayout()
                else:
                    native_form.Refresh()
                    native_form.Update()
            except Exception:
                pass
        try:
            Application.DoEvents()
        except Exception:
            pass
        redraws += 1
    _pump_events(SHOW_SETTLE_SECONDS)
    return {"showCalled": shown, "redrawPasses": redraws, "errors": errors}


def _screenshot(window, native_form, path, native_kind=None):
    # Eto bounds are retained for screenshots because they describe the window
    # frame consistently across backends. WPF native bounds are audited
    # separately with VisualTreeHelper and PointToScreen.
    target = native_form if native_form is not None and native_kind == "winforms" else window
    bounds = _safe_get(target, "Bounds", None)
    if bounds is None:
        raise RuntimeError("Neither the native control nor the Eto window exposed screen bounds.")
    try:
        left = int(bounds.Left)
        top = int(bounds.Top)
    except Exception:
        left = int(bounds.X)
        top = int(bounds.Y)
    width = int(bounds.Width)
    height = int(bounds.Height)
    if width <= 0 or height <= 0:
        raise RuntimeError("Window has non-positive bounds: {0}x{1}.".format(bounds.Width, bounds.Height))
    bitmap = Bitmap(width, height)
    graphics = None
    try:
        graphics = Graphics.FromImage(bitmap)
        graphics.CopyFromScreen(Point(left, top), Point(0, 0), Size(width, height))
        bitmap.Save(path, ImageFormat.Png)
    finally:
        if graphics is not None:
            graphics.Dispose()
        bitmap.Dispose()
    source = "WinForms native bounds" if native_kind == "winforms" else "Eto window bounds"
    return {"path": path, "width": width, "height": height, "captureSource": source}


def _control_category(control):
    name = _type_name(control).lower()
    if isinstance(control, ButtonBase) or "button" in name:
        return "buttons"
    if isinstance(control, NumericUpDown) or "numeric" in name or "numericstepper" in name or "numberbox" in name:
        return "numericFields"
    if "check" in name or "ivancheck" in name:
        return "checkboxes"
    if "segment" in name:
        return "segmented"
    if "tile" in name:
        return "tiles"
    if "slider" in name:
        return "sliders"
    return None


def _control_caption(control):
    text = _safe_get(control, "Text", "")
    name = _safe_get(control, "Name", "")
    return _safe_text(text or name or _type_name(control))


def _bounds_outside(child, parent):
    try:
        b = child.Bounds
        r = parent.ClientRectangle
        return b.Left < r.Left or b.Top < r.Top or b.Right > r.Right or b.Bottom > r.Bottom
    except Exception:
        return None


def _has_direct_vscrollbar(parent):
    try:
        for child in parent.Controls:
            if "vscrollbar" in _type_name(child).lower():
                return True
    except Exception:
        pass
    return False


def _numeric_label_for_control(parent, numeric):
    candidates = []
    try:
        n_top = float(numeric.Top)
        n_left = float(numeric.Left)
        for sibling in parent.Controls:
            if "label" not in _type_name(sibling).lower():
                continue
            text = _safe_text(_safe_get(sibling, "Text", "")).strip()
            if not text:
                continue
            try:
                y_distance = abs(float(sibling.Top) - n_top)
                left = float(sibling.Left)
                right = left + float(sibling.Width)
            except Exception:
                continue
            # AddRow labels sit on the same row as their number box (normally
            # Top + 4 px) and to its left; prefer the nearest such label.
            if y_distance <= 10.0 and left < n_left:
                horizontal_gap = max(0.0, n_left - right)
                candidates.append(((y_distance, horizontal_gap), text))
    except Exception:
        pass
    if not candidates:
        return None
    candidates.sort(key=lambda item: item[0])
    return candidates[0][1]


def _numeric_display_details(parent, control):
    label = _numeric_label_for_control(parent, control)
    try:
        decimals = int(control.DecimalPlaces)
    except Exception:
        decimals = None
    is_mm = bool(label and re.search(r"mm|毫米", label, re.IGNORECASE))
    return {"label": label, "isMmField": is_mm,
            "decimalPlaces": decimals,
            "expectedMmDecimalPlaces": 2 if is_mm else None,
            "mmDisplaysTwoDecimals": (decimals == 2) if is_mm else None,
            "increment": _safe_text(_safe_get(control, "Increment", None)),
            "minimum": _safe_text(_safe_get(control, "Minimum", None)),
            "maximum": _safe_text(_safe_get(control, "Maximum", None)),
            "value": _safe_text(_safe_get(control, "Value", None)),
            "displayText": _safe_text(_safe_get(control, "Text", "")),
            "bounds": _safe_text(_safe_get(control, "Bounds", ""))}


def _audit_winforms_controls(form):
    counts = {"buttons": {"total": 0, "visible": 0},
              "numericFields": {"total": 0, "visible": 0},
              "checkboxes": {"total": 0, "visible": 0},
              "segmented": {"total": 0, "visible": 0},
              "tiles": {"total": 0, "visible": 0},
              "sliders": {"total": 0, "visible": 0}}
    all_total = 0
    all_visible = 0
    hidden = []
    overflows = []
    allowed_scroll_overflows = []
    controls_by_category = dict((key, []) for key in counts.keys())
    stack = [(form, False, "root")]
    while stack:
        parent, in_scroll, parent_label = stack.pop()
        try:
            children = [child for child in parent.Controls]
        except Exception:
            children = []
        this_scroll = in_scroll or bool(_safe_get(parent, "AutoScroll", False))
        for child in children:
            all_total += 1
            try:
                visible = bool(child.Visible)
            except Exception:
                visible = False
            if visible:
                all_visible += 1
            else:
                hidden.append({"type": _type_name(child), "caption": _control_caption(child)})
            category = _control_category(child)
            if category:
                counts[category]["total"] += 1
                if visible:
                    counts[category]["visible"] += 1
                try:
                    parent_visible = bool(child.Parent.Visible)
                except Exception:
                    parent_visible = None
                controls_by_category[category].append({
                    "type": _type_name(child), "caption": _control_caption(child),
                    "visible": visible, "parentType": _type_name(parent),
                    "parentVisible": parent_visible,
                    "bounds": _safe_text(child.Bounds)
                })
                if category == "numericFields":
                    controls_by_category[category][-1]["displayAudit"] = _numeric_display_details(parent, child)
            outside = _bounds_outside(child, parent)
            if outside:
                detail = {"type": _type_name(child), "caption": _control_caption(child),
                          "parent": _type_name(parent), "bounds": _safe_text(child.Bounds)}
                # The modern panels use direct Form children plus VScrollBar and
                # Region clipping, not AutoScroll containers. Only clipped
                # CardPanel children are allowed to exceed the overall form;
                # their internal controls still receive normal bounds checks.
                is_region_clipped_card = ("cardpanel" in _type_name(child).lower() and
                                          _safe_get(child, "Region", None) is not None and
                                          _has_direct_vscrollbar(parent))
                if this_scroll or is_region_clipped_card:
                    allowed_scroll_overflows.append(detail)
                else:
                    overflows.append(detail)
            stack.append((child, this_scroll, _control_caption(child)))
    settings = _safe_get(form, "Settings", None)
    face_count = _safe_get(settings, "FaceCount", None) if settings is not None else None
    numeric_fields = [item.get("displayAudit", {}) for item in controls_by_category.get("numericFields", [])]
    mm_fields = [item for item in numeric_fields if item.get("isMmField")]
    numeric_display_audit = {
        "numericFieldCount": len(numeric_fields),
        "mmLabelFieldCount": len(mm_fields),
        "mmFields": mm_fields,
        "allMmFieldsDisplayTwoDecimals": bool(mm_fields) and all(item.get("mmDisplaysTwoDecimals") is True for item in mm_fields),
        "description": "For each NumericUpDown, the closest same-row left label is recorded; every label containing mm/毫米 must report DecimalPlaces=2."
    }
    return {"treeAvailable": True, "totalControls": all_total,
            "visibleControls": all_visible, "hiddenControls": hidden,
            "categorized": counts, "outOfBounds": overflows,
            "controlsByCategory": controls_by_category,
            "settingsFaceCount": face_count,
            "numericDisplayAudit": numeric_display_audit,
            "scrollContentOverflowsAllowed": allowed_scroll_overflows,
            "visibilitySemantics": "WinForms Control.Visible; scroll clipping may leave a control Visible=True",
            "layoutPass": "recursive WinForms Control.Controls bounds vs parent ClientRectangle"}


def _evaluate_expected_controls(spec, audit):
    categorized = audit.get("categorized", {}) if audit else {}
    required_visible = EXPECTED_CATEGORIES.get(spec["name"], [])
    missing_visible = [category for category in required_visible
                       if int(categorized.get(category, {}).get("visible", 0)) < 1]
    minimums = EXPECTED_CATEGORY_MINIMUMS.get(spec["name"], {})
    visible_minimums = EXPECTED_VISIBLE_MINIMUMS.get(spec["name"], {})
    total_gaps = []
    visible_gaps = []
    for category, minimum in minimums.items():
        count = int(categorized.get(category, {}).get("total", 0))
        if count < int(minimum):
            total_gaps.append({"category": category, "minimum": int(minimum), "actual": count})
    for category, minimum in visible_minimums.items():
        count = int(categorized.get(category, {}).get("visible", 0))
        if count < int(minimum):
            visible_gaps.append({"category": category, "minimum": int(minimum), "actual": count})

    numeric_display_audit = audit.get("numericDisplayAudit", {}) if audit else {}
    numeric_display_passed = None
    if spec["window_kind"] == "winforms":
        numeric_display_passed = (
            int(numeric_display_audit.get("mmLabelFieldCount", 0)) > 0 and
            numeric_display_audit.get("allMmFieldsDisplayTwoDecimals") is True)

    face_scope_rule = None
    if spec["name"] == "ParametricTexture":
        controls = audit.get("controlsByCategory", {}).get("segmented", []) if audit else []
        face_count = audit.get("settingsFaceCount") if audit else None
        hidden_one_face_hint = False
        for hidden in (audit.get("hiddenControls", []) if audit else []):
            caption = _safe_text(hidden.get("caption", ""))
            if u"当前对象只有 1 个面" in caption:
                hidden_one_face_hint = True
                break
        scope_parent_hidden = (len(controls) == 1 and controls[0].get("visible") is False and
                               controls[0].get("parentVisible") is False)
        one_face_evidence = (face_count is not None and int(face_count) == 1) or hidden_one_face_hint
        accepted = (one_face_evidence and len(controls) == 1 and
                    scope_parent_hidden and hidden_one_face_hint)
        face_scope_rule = {
            "status": "passed" if accepted else "failed",
            "expected": "one scope IvanSegmented exists but its parent CardPanel is hidden when FaceCount == 1",
            "faceCount": face_count, "segmentedTotal": len(controls),
            "segmentedVisible": int(categorized.get("segmented", {}).get("visible", 0)),
            "oneFaceEvidence": one_face_evidence,
            "scopeParentHidden": scope_parent_hidden,
            "singleFaceHintFound": hidden_one_face_hint,
            "note": "A missing visible segmented control is not a failure for this fixture; the frozen baseline intentionally hides single-face scope UI."
        }

    return {"requiredVisibleCategories": required_visible,
            "missingVisibleCategories": missing_visible,
            "minimumTotals": minimums,
            "totalCategoryGaps": total_gaps,
            "minimumVisibleCounts": visible_minimums,
            "visibleCountGaps": visible_gaps,
            "numericDisplayAudit": numeric_display_audit if spec["window_kind"] == "winforms" else None,
            "numericDisplayPassed": numeric_display_passed,
            "parametricSingleFaceScopeRule": face_scope_rule,
            "passed": not missing_visible and not total_gaps and not visible_gaps and
                      (numeric_display_passed is not False) and
                      (face_scope_rule is None or face_scope_rule.get("status") == "passed")}


def _eto_control_children(obj):
    result = []
    for attr in ("Content", "Children", "Items", "Rows", "Cells", "Control", "View"):
        value = _safe_get(obj, attr, None)
        if value is None or isinstance(value, basestring):
            continue
        items = _enumerate_values(value)
        if items:
            result.extend(items)
        else:
            result.append(value)
    expanded = []
    for item in result:
        control = _safe_get(item, "Control", None)
        if control is not None and control is not item:
            expanded.append(control)
        else:
            expanded.append(item)
    return expanded


def _audit_eto_controls(window):
    counts = {"buttons": {"total": 0, "visible": 0},
              "numericFields": {"total": 0, "visible": 0},
              "checkboxes": {"total": 0, "visible": 0},
              "segmented": {"total": 0, "visible": 0},
              "tiles": {"total": 0, "visible": 0},
              "sliders": {"total": 0, "visible": 0}}
    hidden = []
    visited = set()
    stack = [(window, 0)]
    total = 0
    while stack:
        obj, depth = stack.pop()
        if obj is None or depth > 24:
            continue
        try:
            ident = int(obj.GetHashCode())
        except Exception:
            ident = id(obj)
        if ident in visited:
            continue
        visited.add(ident)
        name = _type_name(obj).lower()
        try:
            from Eto.Forms import Control as EtoControl
            is_control = depth == 0 or isinstance(obj, EtoControl)
        except Exception:
            try:
                namespace = str(obj.GetType().Namespace or "").lower()
            except Exception:
                namespace = ""
            is_control = depth == 0 or namespace.startswith("eto.forms")
        if is_control:
            total += 1
            category = None
            if "button" in name:
                category = "buttons"
            elif "numeric" in name or "number" in name or "stepper" in name:
                category = "numericFields"
            elif "checkbox" in name:
                category = "checkboxes"
            elif "segment" in name:
                category = "segmented"
            elif "tile" in name:
                category = "tiles"
            elif "slider" in name:
                category = "sliders"
            visible = bool(_safe_get(obj, "Visible", True))
            if category:
                counts[category]["total"] += 1
                if visible:
                    counts[category]["visible"] += 1
            if not visible:
                hidden.append({"type": _type_name(obj), "caption": _control_caption(obj)})
        for child in _eto_control_children(obj):
            if child is not obj:
                stack.append((child, depth + 1))
    return {"treeAvailable": True, "totalControls": total,
            "hiddenControls": hidden, "categorized": counts,
            "visibilitySemantics": "Eto Control.Visible; layout clipping may not change the Visible property",
            "boundsAudit": "not available from generic Eto layout enumeration; native WinForms bounds are checked when exposed"}


def _wpf_visual_children(obj):
    if WpfVisualTreeHelper is None or obj is None:
        return []
    try:
        count = int(WpfVisualTreeHelper.GetChildrenCount(obj))
    except Exception:
        return []
    children = []
    for index in range(count):
        try:
            children.append(WpfVisualTreeHelper.GetChild(obj, index))
        except Exception:
            continue
    return children


def _wpf_walk(root):
    """Return native WPF visual descendants with parent type context."""
    items = []
    stack = [(root, None, [])]
    seen = set()
    while stack:
        obj, parent, ancestors = stack.pop()
        if obj is None:
            continue
        try:
            ident = int(obj.GetHashCode())
        except Exception:
            ident = id(obj)
        if ident in seen:
            continue
        seen.add(ident)
        items.append((obj, parent, ancestors))
        current_ancestors = ancestors + [_type_name(obj)]
        for child in reversed(_wpf_visual_children(obj)):
            stack.append((child, obj, current_ancestors))
    return items


def _wpf_caption(obj):
    for name in ("Text", "Content", "Header", "AutomationProperties.Name"):
        value = _safe_get(obj, name, None)
        if value is None:
            continue
        if isinstance(value, basestring):
            text = _safe_text(value).strip()
            if text:
                return text
        nested = _safe_get(value, "Text", None)
        if nested is not None:
            text = _safe_text(nested).strip()
            if text:
                return text
    return ""


def _wpf_visible(obj):
    try:
        return bool(obj.IsVisible)
    except Exception:
        try:
            value = obj.Visibility
            return str(value).lower().endswith("visible")
        except Exception:
            return True


def _wpf_screen_points(element, width, height):
    if WpfPoint is None:
        return None, None
    try:
        left_top = element.PointToScreen(WpfPoint(0.0, 0.0))
        right_bottom = element.PointToScreen(WpfPoint(float(width), float(height)))
        return left_top, right_bottom
    except Exception:
        return None, None


def _wpf_point_xy(point):
    if point is None:
        return None
    try:
        return float(point.X), float(point.Y)
    except Exception:
        return None


def _audit_wpf_controls(root):
    """Audit rendered WPF FrameworkElements and bounds, not only the Eto tree."""
    if not WPF_AVAILABLE or root is None or not _is_wpf_native_control(root):
        return {"treeAvailable": False, "backend": "wpf", "error": WPF_IMPORT_ERROR or "WPF native root unavailable",
                "outOfBounds": [], "categorized": {}}

    nodes = _wpf_walk(root)
    try:
        root_width = float(root.ActualWidth)
        root_height = float(root.ActualHeight)
    except Exception:
        root_width = 0.0
        root_height = 0.0
    root_tl, root_br = _wpf_screen_points(root, root_width, root_height)
    root_xy0, root_xy1 = _wpf_point_xy(root_tl), _wpf_point_xy(root_br)
    root_rect = None
    if root_xy0 is not None and root_xy1 is not None:
        root_rect = {"left": root_xy0[0], "top": root_xy0[1],
                     "right": root_xy1[0], "bottom": root_xy1[1]}

    counts = {"buttons": {"total": 0, "visible": 0},
              "numericFields": {"total": 0, "visible": 0},
              "checkboxes": {"total": 0, "visible": 0},
              "segmented": {"total": 0, "visible": 0},
              "tiles": {"total": 0, "visible": 0},
              "sliders": {"total": 0, "visible": 0}}
    elements = []
    hidden = []
    overflows = []
    allowed_scroll_overflows = []
    for obj, parent, ancestors in nodes:
        type_name = _type_name(obj)
        lower = type_name.lower()
        visible = _wpf_visible(obj)
        if "button" in lower:
            category = "buttons"
        elif "numeric" in lower or "numberbox" in lower or "stepper" in lower:
            category = "numericFields"
        elif "checkbox" in lower:
            category = "checkboxes"
        elif "segment" in lower:
            category = "segmented"
        elif "tile" in lower:
            category = "tiles"
        elif "slider" in lower:
            category = "sliders"
        else:
            category = None
        if category:
            counts[category]["total"] += 1
            if visible:
                counts[category]["visible"] += 1
        if not visible:
            hidden.append({"type": type_name, "caption": _wpf_caption(obj)})

        try:
            width = float(obj.ActualWidth)
            height = float(obj.ActualHeight)
        except Exception:
            continue
        tl, br = _wpf_screen_points(obj, width, height)
        xy0, xy1 = _wpf_point_xy(tl), _wpf_point_xy(br)
        if xy0 is None or xy1 is None:
            continue
        rect = {"left": xy0[0], "top": xy0[1], "right": xy1[0], "bottom": xy1[1],
                "width": width, "height": height}
        element = {"type": type_name, "name": _safe_text(_safe_get(obj, "Name", "")),
                   "caption": _wpf_caption(obj), "visible": visible,
                   "screenBounds": rect, "parentType": _type_name(parent) if parent is not None else None,
                   "ancestors": ancestors}
        elements.append(element)
        if root_rect is not None and width > 0 and height > 0 and obj is not root:
            outside = (rect["left"] < root_rect["left"] - 1.0 or
                       rect["top"] < root_rect["top"] - 1.0 or
                       rect["right"] > root_rect["right"] + 1.0 or
                       rect["bottom"] > root_rect["bottom"] + 1.0)
            if outside:
                detail = {"type": type_name, "caption": element["caption"],
                          "parent": element["parentType"], "screenBounds": rect,
                          "scrollAncestor": any("scroll" in _safe_text(name).lower() for name in ancestors)}
                if detail["scrollAncestor"]:
                    allowed_scroll_overflows.append(detail)
                else:
                    overflows.append(detail)

    return {"treeAvailable": True, "backend": "wpf VisualTreeHelper",
            "nativeRootType": _type_name(root), "nativeRootViaPointToScreen": True,
            "rootScreenBounds": root_rect, "totalVisuals": len(nodes),
            "categorized": counts, "hiddenControls": hidden,
            "outOfBounds": overflows,
            "scrollContentOverflowsAllowed": allowed_scroll_overflows,
            "elements": elements,
            "visibilitySemantics": "WPF UIElement.IsVisible/Visibility",
            "layoutBoundsAudit": "FrameworkElement.ActualWidth/ActualHeight and PointToScreen; root client screen rectangle from PointToScreen(0,0) to PointToScreen(ActualWidth,ActualHeight)"}


def _wpf_brush_colors(brush):
    colors = []
    if brush is None:
        return colors
    color = _safe_get(brush, "Color", None)
    if color is not None:
        colors.append(color)
    stops = _safe_get(brush, "GradientStops", None)
    if stops is not None:
        try:
            for stop in stops:
                stop_color = _safe_get(stop, "Color", None)
                if stop_color is not None:
                    colors.append(stop_color)
        except Exception:
            pass
    result = []
    for color in colors:
        try:
            rgb = (int(color.A), int(color.R), int(color.G), int(color.B))
            result.append({"argb": rgb, "hex": "#{0:02X}{1:02X}{2:02X}{3:02X}".format(*rgb)})
        except Exception:
            result.append({"value": _safe_text(color)})
    return result


def _wpf_corner_radius(border):
    radius = _safe_get(border, "CornerRadius", None)
    if radius is None:
        return None
    try:
        return [float(radius.TopLeft), float(radius.TopRight),
                float(radius.BottomRight), float(radius.BottomLeft)]
    except Exception:
        return _safe_text(radius)


def _wpf_button_style_audit(button, role):
    template = _safe_get(button, "Template", None)
    borders = []
    for node, parent, ancestors in _wpf_walk(button):
        if WpfBorder is not None:
            try:
                if isinstance(node, WpfBorder):
                    borders.append(node)
                    continue
            except Exception:
                pass
        if _type_name(node) == "Border":
            borders.append(node)
    matching = []
    for border in borders:
        radius = _wpf_corner_radius(border)
        if isinstance(radius, list) and radius and all(abs(v - 9.0) < 0.05 for v in radius):
            matching.append(border)
    rounded = matching[0] if matching else None
    if rounded is None and borders:
        rounded = borders[0]
    brush = _safe_get(rounded, "Background", None) if rounded is not None else None
    if brush is None:
        brush = _safe_get(button, "Background", None)
    color_values = _wpf_brush_colors(brush)
    rgba = [entry.get("argb") for entry in color_values if entry.get("argb") is not None]
    if role == "primary":
        appearance_match = any((b > r * 1.2 and b > g * 1.05 and b >= 140) for a, r, g, b in rgba)
        exact_accent = any((r, g, b) == (52, 125, 241) for a, r, g, b in rgba)
        expected = "blue accent (#347DF1 preferred)"
    else:
        # The shared glass theme intentionally uses a white-to-pale-blue
        # gradient (#FFFFFFFF -> #FFEDF4FB) for secondary actions. Accept that
        # concrete light gradient as well as an all-near-white glass surface.
        all_near_white = bool(rgba) and all(r >= 238 and g >= 238 and b >= 238 for a, r, g, b in rgba)
        light_tint_range = bool(rgba) and all(r >= 232 and g >= 239 and b >= 246 for a, r, g, b in rgba)
        white_stop = any(r >= 248 and g >= 248 and b >= 248 for a, r, g, b in rgba)
        pale_blue_stop = any(r >= 232 and g >= 239 and b >= 246 and
                             b - r >= 10 and g - r >= 3 for a, r, g, b in rgba)
        glass_blue_gradient = light_tint_range and white_stop and pale_blue_stop
        appearance_match = all_near_white or glass_blue_gradient
        exact_accent = False
        expected = "near-white surface or white-to-pale-blue glass gradient (#FFFFFFFF -> #FFEDF4FB)"
    try:
        style_present = button.Style is not None
    except Exception:
        style_present = False
    return {"role": role, "caption": _wpf_caption(button),
            "buttonType": _type_name(button), "stylePresent": style_present,
            "templatePresent": template is not None,
            "roundedBorder": {"found": rounded is not None,
                              "visualType": _type_name(rounded) if rounded is not None else None,
                              "cornerRadius": _wpf_corner_radius(rounded) if rounded is not None else None,
                              "radiusNineOnAllCorners": rounded in matching if rounded is not None else False},
            "background": {"brushType": _type_name(brush) if brush is not None else None,
                           "colors": color_values, "expected": expected,
                           "appearanceMatches": appearance_match,
                           "matchesAccentToken": exact_accent}}


def _wpf_find_button(root, expected_caption):
    target = expected_caption.strip()
    for node, parent, ancestors in _wpf_walk(root):
        type_name = _type_name(node).lower()
        is_button = "button" in type_name
        if WpfButtonBase is not None:
            try:
                is_button = is_button or isinstance(node, WpfButtonBase)
            except Exception:
                pass
        if not is_button:
            continue
        caption = _wpf_caption(node)
        if caption == target or target in caption:
            return node
    return None


def _wpf_text_values(root):
    values = []
    for node, parent, ancestors in _wpf_walk(root):
        for attr in ("Text", "Content"):
            value = _safe_get(node, attr, None)
            if isinstance(value, basestring):
                text = _safe_text(value).strip()
                if text:
                    values.append(text)
    return values


def _click_wpf_button(button):
    if button is None:
        return {"clicked": False, "method": None, "error": "native WPF button not found"}
    try:
        if WpfButtonBase is None or WpfRoutedEventArgs is None:
            raise RuntimeError("WPF ButtonBase/RoutedEventArgs not available")
        event = WpfButtonBase.ClickEvent
        button.RaiseEvent(WpfRoutedEventArgs(event))
        return {"clicked": True, "method": "WPF ButtonBase.ClickEvent RoutedEventArgs"}
    except Exception as ex:
        return {"clicked": False, "method": "WPF ButtonBase.ClickEvent RoutedEventArgs",
                "error": _safe_text(ex)}


def _find_eto_button(window, expected_caption):
    target = expected_caption.strip()
    queue = [window]
    seen = set()
    while queue:
        obj = queue.pop(0)
        if obj is None:
            continue
        try:
            ident = int(obj.GetHashCode())
        except Exception:
            ident = id(obj)
        if ident in seen:
            continue
        seen.add(ident)
        name = _type_name(obj).lower()
        caption = _control_caption(obj)
        if "button" in name and (caption == target or target in caption):
            return obj
        for child in _eto_control_children(obj):
            queue.append(child)
    return None


def _click_vape_button(window, native_root, caption):
    """Prefer Eto Button.PerformClick; fall back to native WPF routed click."""
    eto_button = _find_eto_button(window, caption)
    if eto_button is not None:
        try:
            perform = getattr(eto_button, "PerformClick")
            perform()
            return {"clicked": True, "method": "Eto.Button.PerformClick",
                    "buttonType": _type_name(eto_button), "caption": _control_caption(eto_button)}
        except Exception as ex:
            eto_error = _safe_text(ex)
    else:
        eto_error = "Eto button with the requested caption was not exposed"

    native_button = _wpf_find_button(native_root, caption)
    result = _click_wpf_button(native_button)
    result["fallbackFromEto"] = eto_error
    result["caption"] = _wpf_caption(native_button) if native_button is not None else caption
    return result


def _document_object_state():
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None:
        return {"available": False, "error": "RhinoDoc.ActiveDoc is null."}
    try:
        count = int(doc.Objects.Count)
        selected = doc.Objects.GetSelectedObjects(False, False)
        selected_count = len([obj for obj in selected]) if selected is not None else 0
        try:
            serial_number = int(doc.RuntimeSerialNumber)
        except Exception:
            serial_number = None
        return {"available": True, "passed": count == 0 and selected_count == 0,
                "objectCount": count, "selectedObjectCount": selected_count,
                "documentRuntimeSerialNumber": serial_number}
    except Exception as ex:
        return {"available": False, "error": _safe_text(ex)}


def _wait_eto_window_closed(window, native_root, native_kind):
    deadline = time.time() + WAIT_FOR_CLOSE_SECONDS
    while time.time() < deadline:
        _pump_events(0.05)
        try:
            if _safe_get(window, "Visible", None) is False:
                return True
        except Exception:
            pass
        if native_kind == "wpf" and native_root is not None:
            if _safe_get(native_root, "IsVisible", None) is False:
                return True
            if _safe_get(native_root, "IsLoaded", None) is False:
                return True
        try:
            windows, unused = _eto_windows()
            if not any(item is window for item in windows):
                return True
        except Exception:
            pass
    return False


def _run_vape_native_checks(window, native_root, native_kind):
    result = {"status": "unverified", "backend": native_kind,
              "styleAudit": None, "noTargetRebuild": None, "nativeClose": None}
    if native_kind != "wpf" or native_root is None:
        result["note"] = "Vape Eto native WPF root unavailable; native Button.Template/bounds/action checks were not verified."
        return result

    rebuild = _wpf_find_button(native_root, u"重新生成标注")
    close = _wpf_find_button(native_root, u"关闭")
    rebuild_style = _wpf_button_style_audit(rebuild, "primary") if rebuild is not None else None
    close_style = _wpf_button_style_audit(close, "secondary") if close is not None else None
    style_pass = bool(rebuild_style and close_style and
                      rebuild_style.get("stylePresent") and rebuild_style.get("templatePresent") and
                      rebuild_style.get("roundedBorder", {}).get("radiusNineOnAllCorners") and
                      rebuild_style.get("background", {}).get("appearanceMatches") and
                      close_style.get("stylePresent") and close_style.get("templatePresent") and
                      close_style.get("roundedBorder", {}).get("radiusNineOnAllCorners") and
                      close_style.get("background", {}).get("appearanceMatches"))
    result["styleAudit"] = {"status": "passed" if style_pass else "failed",
                             "requirements": "Rebuild and Close WPF Buttons have a non-null Style and Template, a visual Border with CornerRadius 9 on all corners, and primary blue / secondary white backgrounds.",
                             "rebuild": rebuild_style, "close": close_style}

    before = _document_object_state()
    if not before.get("available") or before.get("objectCount") != 0 or before.get("selectedObjectCount") != 0:
        result["noTargetRebuild"] = {"status": "skipped", "before": before,
                                      "reason": "Safety gate requires an empty, unselected active document."}
        result["nativeClose"] = {"status": "skipped", "reason": "No-target rebuild safety gate failed; normal finally-close will be used."}
        result["status"] = "failed"
        return result

    rebuild_click = _click_vape_button(window, native_root, u"重新生成标注")
    _pump_events(0.25)
    status_texts = _wpf_text_values(native_root)
    status_expected = any(u"还没有选择物件。" in text for text in status_texts)
    after_rebuild = _document_object_state()
    rebuild_pass = (rebuild_click.get("clicked") and status_expected and
                    after_rebuild.get("available") and after_rebuild.get("objectCount") == 0 and
                    after_rebuild.get("selectedObjectCount") == 0)
    result["noTargetRebuild"] = {
        "status": "passed" if rebuild_pass else "failed",
        "buttonClick": rebuild_click,
        "expectedStatusText": u"还没有选择物件。",
        "statusTextObserved": status_expected,
        "nativeStatusTexts": status_texts,
        "before": before, "after": after_rebuild,
        "assertion": "The original no-target handler only updates status and does not create document geometry."
    }

    close_click = _click_vape_button(window, native_root, u"关闭")
    closed = False
    if close_click.get("clicked"):
        closed = _wait_eto_window_closed(window, native_root, "wpf")
    result["nativeClose"] = {"status": "passed" if close_click.get("clicked") and closed else "failed",
                              "buttonClick": close_click, "closed": closed,
                              "method": close_click.get("method")}
    result["status"] = "passed" if style_pass and rebuild_pass and result["nativeClose"]["status"] == "passed" else "failed"
    return result


def _close_normally(window, native_form, eto_mode, native_kind=None):
    close_error = None
    try:
        window.Close()
    except Exception as ex:
        close_error = _safe_text(ex)
        if native_form is not None and native_kind == "winforms":
            try:
                native_form.Close()
                close_error = None
            except Exception as ex2:
                close_error += "; native Close: " + _safe_text(ex2)
    deadline = time.time() + WAIT_FOR_CLOSE_SECONDS
    while time.time() < deadline:
        _pump_events(0.05)
        try:
            if native_form is not None and native_kind == "winforms" and bool(native_form.IsDisposed):
                return {"closed": True, "closeMethod": "Close", "error": close_error}
        except Exception:
            pass
        if native_form is not None and native_kind == "wpf":
            native_visible = _safe_get(native_form, "IsVisible", None)
            native_loaded = _safe_get(native_form, "IsLoaded", None)
            if native_visible is False or native_loaded is False:
                return {"closed": True, "closeMethod": "Close", "error": close_error}
        visible = _safe_get(window, "Visible", None)
        if visible is False:
            return {"closed": True, "closeMethod": "Close", "error": close_error}
        if eto_mode:
            windows, unused = _eto_windows()
            if window not in windows:
                return {"closed": True, "closeMethod": "Close", "error": close_error}
        else:
            try:
                if native_form not in _winforms_forms():
                    return {"closed": True, "closeMethod": "Close", "error": close_error}
            except Exception:
                pass
    return {"closed": False, "closeMethod": "Close", "error": close_error,
            "note": "Window did not report closed before timeout; it was not forcibly disposed."}


def _empty_document_preflight():
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None:
        return {"passed": False, "error": "RhinoDoc.ActiveDoc is null."}
    try:
        count = int(doc.Objects.Count)
    except Exception as ex:
        return {"passed": False, "error": "Cannot safely read ActiveDoc.Objects.Count: " + _safe_text(ex)}
    try:
        selected = doc.Objects.GetSelectedObjects(False, False)
        selected_count = len([obj for obj in selected]) if selected is not None else 0
    except Exception as ex:
        return {"passed": False, "error": "Cannot safely verify selection state: " + _safe_text(ex)}
    passed = count == 0 and selected_count == 0
    try:
        serial_number = int(doc.RuntimeSerialNumber)
    except Exception:
        serial_number = None
    return {"passed": passed, "objectCount": count, "selectedObjectCount": selected_count,
            "documentRuntimeSerialNumber": serial_number,
            "note": "Commands are not executed unless the active document has zero objects and zero selected objects."}


def _write_text(path, text):
    # IronPython 2 json.dumps(..., ensure_ascii=True) commonly returns a byte
    # string. io.open(text mode) requires unicode, so decode before writing and
    # preserve literal UTF-8 text from external/self-test reports.
    unicode_text = _as_unicode(text)
    with io.open(path, "w", encoding="utf-8") as handle:
        handle.write(unicode_text)


def _as_unicode(value):
    try:
        if isinstance(value, unicode):
            return value
        elif isinstance(value, str):
            return value.decode("utf-8", "replace")
        else:
            return unicode(value)
    except NameError:
        # Lets the file remain syntax/testable under a non-IronPython runtime.
        try:
            if isinstance(value, bytes):
                return value.decode("utf-8", "replace")
        except Exception:
            pass
        return value if isinstance(value, str) else str(value)
    except Exception:
        try:
            return unicode(repr(value))
        except Exception:
            return u"<unprintable>"


def _json_plain(value, active_containers=None):
    """Recursively convert IronPython/.NET values to strict JSON primitives.

    IronPython's json encoder may see a CLR System.String as a byte-string and
    call decode('utf-8') through its ASCII codec. Convert every string and
    scalar explicitly before the encoder sees it.
    """
    if active_containers is None:
        active_containers = set()
    if value is None:
        return None

    # Determine the CLR type before Python's dynamic string/numeric coercions.
    dotnet_type = None
    try:
        dotnet_type = str(value.GetType().FullName)
    except Exception:
        pass

    if dotnet_type == "System.String":
        return unicode(value)
    if dotnet_type == "System.Boolean":
        return bool(value)
    if dotnet_type in ("System.Decimal", "System.Double", "System.Single"):
        return float(value)
    if dotnet_type in ("System.Byte", "System.SByte", "System.Int16", "System.UInt16",
                       "System.Int32", "System.UInt32", "System.Int64", "System.UInt64"):
        return int(value)

    try:
        if isinstance(value, unicode):
            return value
    except NameError:
        pass
    try:
        if isinstance(value, str):
            return _as_unicode(value)
    except Exception:
        pass
    if isinstance(value, bool):
        return bool(value)
    try:
        if isinstance(value, (int, long)):
            return int(value)
    except NameError:
        if isinstance(value, int):
            return int(value)
    if isinstance(value, float):
        return float(value)

    if dotnet_type and dotnet_type.endswith("Enum"):
        try:
            return _as_unicode(value.ToString())
        except Exception:
            return _as_unicode(value)

    # Handle native Python mappings and sequences without retaining CLR values.
    is_mapping = isinstance(value, dict)
    is_sequence = isinstance(value, (list, tuple, set))
    # CLR dictionaries expose Keys and an indexer but are not Python dicts.
    if not is_mapping and dotnet_type and ("Dictionary" in dotnet_type or "Hashtable" in dotnet_type):
        try:
            keys = [key for key in value.Keys]
            is_mapping = True
        except Exception:
            keys = None
    else:
        keys = None

    if is_mapping or is_sequence:
        identity = id(value)
        if identity in active_containers:
            return u"<recursive>"
        active_containers.add(identity)
        try:
            if is_mapping:
                if keys is None:
                    keys = value.keys()
                result = {}
                for key in keys:
                    clean_key = _json_plain(key, active_containers)
                    if not isinstance(clean_key, (str, unicode)):
                        clean_key = _as_unicode(clean_key)
                    result[clean_key] = _json_plain(value[key], active_containers)
                return result
            return [_json_plain(item, active_containers) for item in value]
        finally:
            active_containers.remove(identity)

    # CLR arrays and enumerable collections (except strings handled above).
    if dotnet_type and (dotnet_type.endswith("]") or "Collection" in dotnet_type or "List`" in dotnet_type):
        try:
            identity = id(value)
            if identity in active_containers:
                return u"<recursive>"
            active_containers.add(identity)
            try:
                return [_json_plain(item, active_containers) for item in value]
            finally:
                active_containers.remove(identity)
        except Exception:
            pass

    # Last resort: no unhandled CLR object can escape into json.dumps.
    return _as_unicode(value)


def _file_sha256(path):
    try:
        with open(path, "rb") as handle:
            return hashlib.sha256(handle.read()).hexdigest()
    except Exception:
        return None


def _preserve_first_run_report():
    source = os.path.join(OUTPUT_DIR, "native-panel-smoke.json")
    first = os.path.join(OUTPUT_DIR, "native-panel-smoke-attempt1.json")
    if os.path.isfile(first):
        return {"path": first, "created": False, "sha256": _file_sha256(first),
                "note": "Existing first-attempt evidence preserved without overwrite."}
    if os.path.isfile(source):
        try:
            shutil.copyfile(source, first)
            return {"path": first, "created": True, "sha256": _file_sha256(first),
                    "note": "First pre-fix smoke report copied before writing the next result."}
        except Exception as ex:
            return {"path": first, "created": False, "error": _safe_text(ex)}
    return {"path": first, "created": False, "note": "No prior native-panel-smoke.json existed to snapshot."}


def _read_selftest_report(path):
    try:
        with io.open(path, "r", encoding="utf-8-sig") as handle:
            return handle.read()
    except Exception as ex:
        return None


def _document_object_ids():
    """Return verified object IDs or an explicit enumeration diagnostic."""
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None:
        return {"available": False, "ids": None, "attempts": [], "expectedCount": None}
    try:
        expected_count = int(doc.Objects.Count)
    except Exception as ex:
        return {"available": False, "ids": None, "attempts": [_safe_text(ex)], "expectedCount": None}

    queries = []
    attempts = []
    settings = None
    try:
        settings = Rhino.DocObjects.ObjectEnumeratorSettings()
        settings.ObjectTypeFilter = Rhino.DocObjects.ObjectType.AnyObject
        for property_name in ("ActiveObjects", "HiddenObjects", "LockedObjects", "ReferenceObjects", "IncludeLights"):
            if _safe_get(settings, property_name, None) is not None:
                try:
                    setattr(settings, property_name, True)
                except Exception:
                    pass
        queries.append(("GetObjectList(ObjectEnumeratorSettings)",
                        lambda: doc.Objects.GetObjectList(settings)))
    except Exception as ex:
        attempts.append({"method": "ObjectEnumeratorSettings", "error": _safe_text(ex),
                         "expectedCount": expected_count})

    try:
        queries.append(("GetObjectList(ObjectType.AnyObject)",
                        lambda: doc.Objects.GetObjectList(Rhino.DocObjects.ObjectType.AnyObject)))
    except Exception as ex:
        attempts.append({"method": "GetObjectList(ObjectType.AnyObject)", "error": _safe_text(ex),
                         "expectedCount": expected_count})
    try:
        queries.append(("GetObjectList()", lambda: doc.Objects.GetObjectList()))
    except Exception as ex:
        attempts.append({"method": "GetObjectList()", "error": _safe_text(ex),
                         "expectedCount": expected_count})
    try:
        queries.append(("RhinoObjectTable iteration", lambda: [obj for obj in doc.Objects]))
    except Exception as ex:
        attempts.append({"method": "RhinoObjectTable iteration", "error": _safe_text(ex),
                         "expectedCount": expected_count})

    for method, query in queries:
        try:
            objects = query()
            ids = []
            for obj in objects:
                object_id = _safe_get(obj, "Id", None)
                if object_id is not None:
                    ids.append(object_id)
            attempt = {"method": method, "observedCount": len(ids), "expectedCount": expected_count}
            attempts.append(attempt)
            if len(ids) == expected_count:
                return {"available": True, "ids": ids, "method": method,
                        "attempts": attempts, "expectedCount": expected_count,
                        "observedCount": len(ids)}
        except Exception as ex:
            attempts.append({"method": method, "error": _safe_text(ex),
                             "expectedCount": expected_count})
    return {"available": False, "ids": None, "attempts": attempts,
            "expectedCount": expected_count, "observedCount": None}


def _cleanup_owned_vape_fixtures(preflight):
    """Remove only objects created in the known-empty disposable smoke doc."""
    before = _document_object_state()
    id_audit = _document_object_ids()
    before_ids = id_audit.get("ids")
    same_empty_document = bool(preflight.get("passed") and preflight.get("objectCount") == 0 and
                               before.get("available") and before.get("documentRuntimeSerialNumber") ==
                               preflight.get("documentRuntimeSerialNumber"))
    result = {"beforeCleanup": before, "beforeCleanupObjectIds":
              [_safe_text(object_id) for object_id in before_ids] if before_ids is not None else None,
              "beforeCleanupObjectEnumeration": id_audit,
              "afterCleanup": None, "deletedFixtureIds": [], "deleteErrors": [],
              "cleanupAttempted": False, "cleanupVerified": False,
              "sameInitiallyEmptySmokeDocument": same_empty_document,
              "discardOwnedDocumentOnExit": False}
    if (not preflight.get("passed") or preflight.get("objectCount") != 0 or
            not before.get("available") or before.get("documentRuntimeSerialNumber") !=
            preflight.get("documentRuntimeSerialNumber") or before_ids is None or
            before.get("objectCount") != len(before_ids)):
        result["safetyDecision"] = "Skipped deletion: exact fixture IDs/count could not be enumerated or the original empty-document identity/count could not be proven."
        result["afterCleanup"] = before
        result["cleanupVerified"] = bool(same_empty_document and before.get("objectCount") == 0 and
                                          before.get("selectedObjectCount") == 0)
        result["discardOwnedDocumentOnExit"] = bool(same_empty_document and EXIT_RHINO_WHEN_DONE)
        result["afterCleanupMeaning"] = ("No fixture deletion was attempted; this is the final pre-exit object snapshot. "
                                          "The owned smoke Rhino process is instructed to exit/discard its disposable document." if
                                          result["discardOwnedDocumentOnExit"] else
                                          "No cleanup or document discard is claimed.")
        return result

    result["cleanupAttempted"] = True
    result["safetyDecision"] = "All current object IDs were created after the empty-document preflight in the owned smoke document."
    doc = Rhino.RhinoDoc.ActiveDoc
    for object_id in before_ids:
        try:
            deleted = bool(doc.Objects.Delete(object_id, True))
            if deleted:
                result["deletedFixtureIds"].append(_safe_text(object_id))
            else:
                result["deleteErrors"].append({"id": _safe_text(object_id), "error": "Delete returned false"})
        except Exception as ex:
            result["deleteErrors"].append({"id": _safe_text(object_id), "error": _safe_text(ex)})
    result["afterCleanup"] = _document_object_state()
    after = result["afterCleanup"]
    result["cleanupVerified"] = bool(after.get("available") and after.get("objectCount") == 0 and
                                     after.get("selectedObjectCount") == 0 and not result["deleteErrors"])
    result["discardOwnedDocumentOnExit"] = bool(EXIT_RHINO_WHEN_DONE)
    result["afterCleanupMeaning"] = "Object IDs were deleted and the post-cleanup snapshot is recorded; the owned smoke Rhino process will then exit."
    return result


def _run_vape_volume_selftest(report, failures):
    before = _document_object_state()
    result = {"command": "Rhino.RhinoApp.RunScript('_VapeVolumeSelfTest', False)",
              "preflight": before, "commandReturn": None, "reportFiles": [],
              "geometryResults": [], "postCleanup": None, "passed": False}
    if not before.get("available") or before.get("objectCount") != 0 or before.get("selectedObjectCount") != 0:
        result["status"] = "skipped"
        result["reason"] = "VapeVolumeSelfTest requires an empty, unselected document; no command was issued."
        failures.append("VapeVolumeSelfTest: safety preflight failed; test command skipped.")
        report["vapeVolumeSelfTest"] = result
        return result

    paths = [r"D:\UserData\Desktop\VapeVolume自检结果.txt", r"C:\zct\live_result.txt"]
    snapshots = {}
    for path in paths:
        try:
            snapshots[path] = {"mtime": os.path.getmtime(path), "sha256": _file_sha256(path)}
        except Exception:
            snapshots[path] = {"mtime": None, "sha256": None}
    start = time.time()
    report["safety"]["vapeSelfTestInvoked"] = True
    report["safety"]["temporarySyntheticGeometryExpected"] = True
    try:
        result["commandReturn"] = bool(Rhino.RhinoApp.RunScript("_VapeVolumeSelfTest", False))
        result["commandIssued"] = True
    except Exception as ex:
        result["commandIssued"] = False
        result["commandError"] = _safe_text(ex)

    fresh_texts = []
    deadline = time.time() + 10.0
    while time.time() < deadline:
        for path in paths:
            exists = os.path.isfile(path)
            mtime = os.path.getmtime(path) if exists else None
            sha = _file_sha256(path) if exists else None
            old = snapshots.get(path, {})
            fresh = exists and ((old.get("mtime") is None and old.get("sha256") is None) or
                                sha != old.get("sha256") or
                                (mtime is not None and old.get("mtime") is not None and mtime > old.get("mtime")))
            literal = _read_selftest_report(path) if fresh else None
            entry = {"path": path, "exists": exists, "fresh": bool(fresh),
                     "mtime": mtime, "sha256": sha, "literal": literal,
                     "readError": "report missing or no fresh write" if literal is None else None}
            if literal is not None:
                fresh_texts.append((mtime or 0, path, literal))
            existing = [item for item in result["reportFiles"] if item.get("path") == path]
            if existing:
                result["reportFiles"].remove(existing[0])
            result["reportFiles"].append(entry)
        if fresh_texts:
            break
        _pump_events(0.1)

    chosen = sorted(fresh_texts, key=lambda item: item[0], reverse=True)[0] if fresh_texts else None
    result["selectedReportPath"] = chosen[1] if chosen else None
    report_text = chosen[2] if chosen else u""
    markers = [
        ("A", u"A 实心长方体"),
        ("B", u"B 空心盒"),
        ("C", u"C 油杯"),
        ("D", u"D 雾化芯"),
    ]
    offsets = [(key, title, report_text.find(title)) for key, title in markers]
    offsets = [entry for entry in offsets if entry[2] >= 0]
    offsets.sort(key=lambda item: item[2])
    case_sections = {}
    for index, (key, title, offset) in enumerate(offsets):
        end = offsets[index + 1][2] if index + 1 < len(offsets) else report_text.find(u"E 活标注", offset)
        if end < 0:
            end = len(report_text)
        literal = report_text[offset:end].strip()
        passed = u"通过" in literal and u"Ok=True" in literal
        case_sections[key] = {"case": key, "heading": title, "found": True,
                              "status": "passed" if passed else "reported_not_passed",
                              "passed": passed, "literal": literal}

    # A supported Boolean-difference skip is explicit coverage data, not a
    # fabricated geometric pass and not an absent/unparsed result.
    skip_literal = u"B 布尔差集失败，跳过"
    b_skipped = skip_literal in report_text
    if b_skipped and "B" not in case_sections:
        skip_line = None
        for line in report_text.splitlines():
            if skip_literal in line:
                skip_line = line.strip()
                break
        case_sections["B"] = {"case": "B", "heading": None, "found": True,
                               "status": "skipped", "passed": False,
                               "skipReason": "Boolean difference failed; self-test explicitly skipped this case.",
                               "literal": skip_line or skip_literal}
    result["geometryResults"] = [case_sections[key] for key in ("A", "B", "C", "D")
                                  if key in case_sections]
    result["skippedCases"] = [item["case"] for item in result["geometryResults"]
                              if item.get("status") == "skipped"]
    result["missingGeometryCases"] = [key for key, title in markers if key not in case_sections]
    result["completedGeometryCases"] = [item["case"] for item in result["geometryResults"]
                                         if item.get("passed")]
    result["algorithmCoverage"] = "full" if all(
        key in case_sections and case_sections[key].get("passed") for key, title in markers) else "partial"
    e_section_start = report_text.find(u"E 活标注")
    f_section_start = report_text.find(u"F 转换率推荐规则")
    e_section = report_text[e_section_start:f_section_start] if e_section_start >= 0 and f_section_start > e_section_start else u""
    f_section = report_text[f_section_start:] if f_section_start >= 0 else u""
    e_pass = u"结果：自动更新成功" in e_section
    f_pass = u"规则全部正确：是" in f_section
    result["followupAssertions"] = {
        "E": {"status": "passed" if e_pass else "missing_or_failed",
              "automaticUpdatePassed": e_pass, "literal": e_section.strip()},
        "F": {"status": "passed" if f_pass else "missing_or_failed",
              "conversionRulesPassed": f_pass, "literal": f_section.strip()}
    }
    result["reportLiteral"] = report_text
    result["documentResidue"] = _cleanup_owned_vape_fixtures(result["preflight"])
    result["beforeCleanup"] = result["documentResidue"].get("beforeCleanup")
    result["afterCleanup"] = result["documentResidue"].get("afterCleanup")
    result["postCleanup"] = result["afterCleanup"]
    smoke_fixture_cleanup_pass = result["documentResidue"].get("cleanupVerified") is True
    residue_count = result["beforeCleanup"].get("objectCount") if result["beforeCleanup"] else None
    result["selfTestResidualObjectCountBeforeCleanup"] = residue_count
    core_selftest_left_no_objects = bool(result["beforeCleanup"] and
                                          result["beforeCleanup"].get("available") and
                                          result["beforeCleanup"].get("objectCount") == 0 and
                                          result["beforeCleanup"].get("selectedObjectCount") == 0)
    result["coreSelfTestCleanupVerified"] = core_selftest_left_no_objects
    result["smokeFixtureCleanupVerified"] = smoke_fixture_cleanup_pass
    result["coreSelfTestResidueStatus"] = ("clean" if core_selftest_left_no_objects else
                                           "residual_objects_before_smoke_cleanup")
    same_owned_doc = result["documentResidue"].get("sameInitiallyEmptySmokeDocument") is True
    discard_on_exit = result["documentResidue"].get("discardOwnedDocumentOnExit") is True
    fixture_containment_verified = bool(smoke_fixture_cleanup_pass or
                                        (same_owned_doc and discard_on_exit))
    result["smokeFixtureContainmentVerified"] = fixture_containment_verified
    # Keep core/self-test cleanup distinct from this smoke script's cleanup.
    report["safety"]["vapeSelfTestCleanupVerified"] = bool(core_selftest_left_no_objects)
    report["safety"]["vapeSmokeFixtureCleanupVerified"] = bool(smoke_fixture_cleanup_pass)
    report["safety"]["ownedSmokeDocumentDiscardRequested"] = bool(discard_on_exit)
    if residue_count:
        if smoke_fixture_cleanup_pass:
            residue_note = "VapeVolumeSelfTest left {0} object(s) before smoke-owned fixture cleanup; object IDs and before/after counts are recorded. This is not attributed to the UI smoke.".format(residue_count)
        elif same_owned_doc and discard_on_exit:
            residue_note = "VapeVolumeSelfTest left {0} object(s) in the confirmed empty disposable document; object enumeration/cleanup was not proven, so the runner will discard the owned Rhino process/document on exit. This is not attributed to the UI smoke.".format(residue_count)
        else:
            residue_note = "VapeVolumeSelfTest left {0} object(s); cleanup and safe document discard were not verified.".format(residue_count)
        report.setdefault("warnings", []).append(residue_note)
    command_pass = bool(result.get("commandIssued") and result.get("commandReturn") and chosen is not None)
    all_geometry_pass = (len(result["completedGeometryCases"]) == 4 and not result["missingGeometryCases"])
    result["passed"] = bool(command_pass and all_geometry_pass and e_pass and f_pass and
                             core_selftest_left_no_objects and smoke_fixture_cleanup_pass)
    required_partial_geometry_pass = all(key in result["completedGeometryCases"] for key in ("A", "C", "D"))
    documented_b_outcome = ("B" in result["completedGeometryCases"] or
                            result["skippedCases"] == ["B"]) and not result["missingGeometryCases"]
    result["acceptanceSatisfied"] = bool(command_pass and required_partial_geometry_pass and
                                         documented_b_outcome and e_pass and f_pass and fixture_containment_verified)
    if result["passed"]:
        result["status"] = "passed"
    elif result["acceptanceSatisfied"]:
        if result["skippedCases"]:
            result["status"] = "completed_with_skipped_case"
        else:
            result["status"] = "completed_with_warning"
        if result["skippedCases"]:
            report.setdefault("warnings", []).append(
                "VapeVolumeSelfTest algorithm coverage is partial: case B (Boolean difference) was explicitly skipped; A/C/D, E/F, and smoke-fixture cleanup passed.")
    else:
        result["status"] = "failed"
        failures.append("VapeVolumeSelfTest: required A/C/D geometry, documented B skip, E/F assertions, report freshness, or empty-document cleanup assertion failed.")
    report["vapeVolumeSelfTest"] = result
    return result


def _run_one(spec, report, failures):
    item = {"name": spec["name"], "command": spec["command"],
            "targetWindowType": spec["form_type"], "windowKind": spec["window_kind"],
            "status": "started", "commandReturn": None, "screenshot": None,
            "windowSearch": None, "controlAudit": None, "close": None}
    window = None
    native_form = None
    native_kind = None
    try:
        before_forms = [_type_name(form) for form in _winforms_forms()]
        item["winformsOpenFormsBefore"] = before_forms
        item["commandReturn"] = bool(Rhino.RhinoApp.RunScript("_" + spec["command"], False))
        item["commandIssued"] = True
        item["commandIssuedAtUtc"] = datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")

        deadline = time.time() + WAIT_FOR_WINDOW_SECONDS
        eto_query = None
        eto_via = None
        while time.time() < deadline:
            if spec["window_kind"] == "eto":
                window, native_form, eto_query, eto_via, native_kind = _find_eto_native_form(spec["form_type"])
            else:
                window = _find_winforms_form(spec["form_type"])
                native_form = window
                native_kind = "winforms" if window is not None else None
            if window is not None:
                break
            _pump_events(0.10)

        item["windowSearch"] = {"found": window is not None,
                                "enumeration": eto_query,
                                "nativeControlVia": eto_via,
                                "nativeControlKind": native_kind,
                                "wpfAssembliesAvailable": WPF_AVAILABLE,
                                "wpfImportError": WPF_IMPORT_ERROR,
                                "winformsOpenFormsAfter": [_type_name(form) for form in _winforms_forms()]}
        if window is None:
            message = "Target window not found after {0:.1f}s; UI and screenshot unverified.".format(WAIT_FOR_WINDOW_SECONDS)
            if spec["window_kind"] == "eto":
                message += " Eto.Forms.Application.Instance.Windows query result: " + _safe_text(eto_query)
            raise RuntimeError(message)

        item["showAndRedraw"] = _show_and_redraw(window, native_form, native_kind)
        if spec["window_kind"] == "eto":
            item["controlAudit"] = _audit_eto_controls(window)
            if eto_query and eto_query.get("winformsFallback"):
                item["controlAudit"]["auditSource"] = "System.Windows.Forms.Application.OpenForms fallback"
                item["controlAudit"]["etoEnumeration"] = eto_query
            if native_form is not None:
                if native_kind == "winforms":
                    native_audit = _audit_winforms_controls(native_form)
                elif native_kind == "wpf":
                    native_audit = _audit_wpf_controls(native_form)
                else:
                    native_audit = {"treeAvailable": False, "error": "unknown native backend", "outOfBounds": []}
                item["controlAudit"]["nativeBoundsBackend"] = native_kind
                item["controlAudit"]["nativeBoundsAvailable"] = bool(native_audit.get("treeAvailable", False))
                item["controlAudit"]["nativeBoundsAudit"] = native_audit
            else:
                item["controlAudit"]["nativeBoundsAvailable"] = False
                item["controlAudit"]["nativeBoundsAudit"] = {
                    "treeAvailable": False,
                    "error": "Eto dialog found, but no native WinForms/WPF control root was exposed",
                    "wpfImportError": WPF_IMPORT_ERROR, "outOfBounds": []}
                item["controlAudit"]["layoutBoundsVerified"] = False
        else:
            item["controlAudit"] = _audit_winforms_controls(native_form)

        item["expectedControlAudit"] = _evaluate_expected_controls(spec, item["controlAudit"])
        expectation_ok = item["expectedControlAudit"].get("passed", False)
        gaps = item["expectedControlAudit"].get("missingVisibleCategories", [])
        total_gaps = item["expectedControlAudit"].get("totalCategoryGaps", [])
        visible_count_gaps = item["expectedControlAudit"].get("visibleCountGaps", [])
        numeric_display_passed = item["expectedControlAudit"].get("numericDisplayPassed")

        png_path = os.path.join(OUTPUT_DIR, "native-smoke-" + spec["name"].lower() + ".png")
        item["screenshot"] = _screenshot(window, native_form, png_path, native_kind)
        if spec["name"] == "VapeVolume":
            item["nativeInteractionAudit"] = _run_vape_native_checks(window, native_form, native_kind)
            if report.get("safety") is not None:
                rebuild_clicked = item["nativeInteractionAudit"].get("noTargetRebuild", {}).get("buttonClick", {}).get("clicked", False)
                close_clicked = item["nativeInteractionAudit"].get("nativeClose", {}).get("buttonClick", {}).get("clicked", False)
                if rebuild_clicked or close_clicked:
                    report["safety"]["controlsClicked"] = True
                    if rebuild_clicked:
                        report["safety"].setdefault("clickedControls", []).append(
                            "VapeVolume: 重新生成标注 (empty-document guard)")
                    if close_clicked:
                        report["safety"].setdefault("clickedControls", []).append(
                            "VapeVolume: 关闭 (native button)")
            if item["nativeInteractionAudit"].get("nativeClose", {}).get("closed", False):
                item["close"] = {"closed": True,
                                  "closeMethod": item["nativeInteractionAudit"].get("nativeClose", {}).get("method"),
                                  "error": None}
        overflow = item["controlAudit"].get("outOfBounds", [])
        native_audit = item["controlAudit"].get("nativeBoundsAudit", {})
        if isinstance(native_audit, dict):
            overflow = overflow + native_audit.get("outOfBounds", [])
        show_ok = item.get("showAndRedraw", {}).get("showCalled", False)
        unverified_eto_bounds = (spec["window_kind"] == "eto" and
                                 (native_form is None or not item["controlAudit"].get("nativeBoundsAvailable", False)))
        interaction_status = item.get("nativeInteractionAudit", {}).get("status")
        if overflow or not expectation_ok or not show_ok or interaction_status == "failed":
            item["status"] = "failed"
        elif unverified_eto_bounds or interaction_status == "unverified":
            item["status"] = "unverified"
        else:
            item["status"] = "passed"
        if overflow or not expectation_ok or not show_ok or unverified_eto_bounds or interaction_status in ("failed", "unverified"):
            details = []
            if overflow:
                details.append("one or more controls exceed a non-scroll parent client area")
            if gaps:
                details.append("missing visible controls in categories: " + ", ".join(gaps))
            if total_gaps or visible_count_gaps:
                details.append("control category count minima were not met")
            if numeric_display_passed is False:
                mm_audit = item["expectedControlAudit"].get("numericDisplayAudit", {})
                details.append("mm NumericUpDown.DecimalPlaces audit failed (observed fields: {0})".format(
                    _safe_text(mm_audit.get("mmFields", []))))
            scope_rule = item["expectedControlAudit"].get("parametricSingleFaceScopeRule") or {}
            if not expectation_ok and scope_rule.get("status") == "failed":
                details.append("ParametricTexture single-face hidden scope rule was not satisfied")
            if not show_ok:
                details.append("Show could not be confirmed")
            if unverified_eto_bounds:
                details.append("Eto window was found, but native WinForms/WPF layout bounds are unavailable; layout is unverified")
            if interaction_status == "failed":
                details.append("Vape native button style/action assertions failed")
            elif interaction_status == "unverified":
                details.append("Vape native WPF button style/action assertions are unverified")
            failures.append(spec["name"] + ": " + "; ".join(details) + ".")
    except Exception as ex:
        item["status"] = "failed"
        item["error"] = _safe_text(ex)
        item["traceback"] = traceback.format_exc()
        failures.append(spec["name"] + ": " + _safe_text(ex))
    finally:
        if window is None:
            # Find a late-arriving dialog after an exception, but only close the
            # exact plugin window this iteration owns.
            try:
                if spec["window_kind"] == "eto":
                    window, native_form, unused_query, unused_via, native_kind = _find_eto_native_form(spec["form_type"])
                else:
                    window = _find_winforms_form(spec["form_type"])
                    native_form = window
                    native_kind = "winforms" if window is not None else None
            except Exception:
                window = None
        if window is not None and not (item.get("close") and item["close"].get("closed", False)):
            try:
                item["close"] = _close_normally(window, native_form, spec["window_kind"] == "eto", native_kind)
                if not item["close"].get("closed", False):
                    item["status"] = "failed"
                    failures.append(spec["name"] + ": normal Close did not finish before timeout.")
            except Exception as ex:
                item["status"] = "failed"
                item["close"] = {"closed": False, "error": _safe_text(ex)}
                failures.append(spec["name"] + ": close failed: " + _safe_text(ex))
        report["plugins"].append(item)


def main():
    failures = []
    first_attempt = _preserve_first_run_report()
    report = {"title": "Native plugin panel UI smoke check",
              "startedAtUtc": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
              "runtime": {"python": sys.version, "rhino": _safe_text(_safe_get(Rhino, "RhinoApp", "unknown")),
                          "wpfAvailable": WPF_AVAILABLE, "wpfImportError": WPF_IMPORT_ERROR},
              "firstAttemptEvidence": first_attempt,
              "safety": {"userObjectsCreatedOrChanged": False, "panelCommandsCreatedObjects": False,
                         "controlsClicked": False, "clickedControls": [],
                         "pickCommandsTriggered": False, "documentSettingsChanged": False,
                         "temporarySelfTestGeometryExpected": False,
                         "vapeSelfTestCleanupVerified": None},
              "preflight": _empty_document_preflight(), "plugins": [], "failures": [], "warnings": [],
              "nativeBoundsAudit": "WinForms Control bounds for WinForms panels; Eto virtual tree plus WPF VisualTreeHelper/ActualWidth/ActualHeight/PointToScreen for VapeVolume."}
    if not report["preflight"].get("passed", False):
        reason = "Safety preflight failed; no plugin command was executed: " + _safe_text(report["preflight"])
        failures.append(reason)
        for spec in PLUGINS:
            report["plugins"].append({"name": spec["name"], "command": spec["command"],
                                      "status": "skipped", "error": reason,
                                      "windowKind": spec["window_kind"]})
    else:
        for spec in PLUGINS:
            _run_one(spec, report, failures)
        _run_vape_volume_selftest(report, failures)

    report["finishedAtUtc"] = datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
    report["failures"] = failures
    report_path = os.path.join(OUTPUT_DIR, "native-panel-smoke.json")
    extended_path = os.path.join(OUTPUT_DIR, "native-smoke-extended-audit.json")
    failure_path = os.path.join(OUTPUT_DIR, "native-panel-smoke-failures.txt")
    serialization_error_path = os.path.join(OUTPUT_DIR, "native-panel-smoke-serialize-error.txt")
    report["serializationErrorPath"] = serialization_error_path
    try:
        json_report = _json_plain(report)
        serialized = unicode(_as_unicode(json.dumps(json_report, ensure_ascii=False, indent=2)))
        _write_text(report_path, serialized)
        _write_text(extended_path, serialized)
        if failures:
            failure_text = "\n".join(failures) + "\n"
        else:
            failure_text = "No failures reported. Review native-panel-smoke.json and native-smoke-extended-audit.json.\n"
        _write_text(failure_path, failure_text)
    except Exception as ex:
        # Preserve a concrete on-disk error even if a .NET object in the report
        # cannot be serialized by IronPython's json module. Do not rely on the
        # Rhino command line, which may disappear when the runner exits Rhino.
        error_text = (u"Native panel smoke report serialization/write failed.\n"
                      u"Exception type: {0}\nException: {1}\n\nTraceback:\n{2}\n"
                      u"Started: {3}\nFinished: {4}\n".format(
                          _as_unicode(_type_name(ex)), _as_unicode(_safe_text(ex)),
                          _as_unicode(traceback.format_exc()),
                          _as_unicode(report.get("startedAtUtc", "unknown")),
                          _as_unicode(report.get("finishedAtUtc", "unknown"))))
        try:
            _write_text(serialization_error_path, error_text)
        except Exception as fallback_ex:
            # Final byte-level fallback is UTF-8 with replacement; still record
            # the primary and fallback errors rather than silently losing them.
            try:
                raw = (u"{0}\nFallback writer error: {1}\n".format(
                    error_text, _safe_text(fallback_ex))).encode("utf-8", "replace")
                with open(serialization_error_path, "wb") as handle:
                    handle.write(raw)
            except Exception:
                pass
        try:
            Rhino.RhinoApp.WriteLine("[native-panel-smoke] Could not write report: " + _safe_text(ex) +
                                     "; details: " + serialization_error_path)
        except Exception:
            pass

    try:
        Rhino.RhinoApp.WriteLine("[native-panel-smoke] Finished. Results: {0}".format(report_path))
        Rhino.RhinoApp.WriteLine("[native-panel-smoke] Extended native audit: {0}".format(extended_path))
        Rhino.RhinoApp.WriteLine("[native-panel-smoke] Failures: {0}".format(len(failures)))
    except Exception:
        pass
    if EXIT_RHINO_WHEN_DONE:
        try:
            Rhino.RhinoApp.RunScript("_-Exit _Enter", False)
        except Exception:
            pass
    return report


RESULT = main()

