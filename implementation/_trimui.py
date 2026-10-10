# -*- coding: utf-8 -*-
"""面板「允许修剪」开关 + Probe flag + 自检 4 项"""
from pathlib import Path

R = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\surfaceunify\src')

def patch(path, pairs):
    p = R / path
    t = p.read_text(encoding='utf-8-sig')
    for a, b in pairs:
        ok = a in t
        print(('OK  ' if ok else 'MISS'), path, '::', a.strip().split('\n')[0][:56])
        if ok:
            t = t.replace(a, b, 1)
    p.write_text(t, encoding='utf-8')

# ---------- UI：字段 + 复选框 + 卡片高度 + 自检钩子
patch('SurfaceUnifyUi.cs', [
("""        IvanCheck _lockCheck, _holesCheck, _srcCheck;""",
 """        IvanCheck _lockCheck, _holesCheck, _trimCheck, _srcCheck;"""),
("""                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 132, Title = "边界与输出" };""",
 """                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 160, Title = "边界与输出" };"""),
("""                card.Controls.Add(_holesCheck);
                by += 28;""",
 """                card.Controls.Add(_holesCheck);
                by += 28;

                _trimCheck = new IvanCheck { Text = "允许修剪（域外扩后按原边界剪掉多余部分）", Checked = Settings.AllowTrim };
                _trimCheck.SetBounds(12, by, W - 48, 22);
                _trimCheck.CheckedChanged += (s, e) =>
                {
                    Settings.AllowTrim = _trimCheck.Checked;
                    Raise();
                };
                card.Controls.Add(_trimCheck);
                by += 28;"""),
("""        public void SetKeepHoles(bool on) { if (_holesCheck != null) _holesCheck.Checked = on; }
        public bool KeepHolesChecked { get { return _holesCheck != null && _holesCheck.Checked; } }""",
 """        public void SetKeepHoles(bool on) { if (_holesCheck != null) _holesCheck.Checked = on; }
        public bool KeepHolesChecked { get { return _holesCheck != null && _holesCheck.Checked; } }
        public void SetAllowTrim(bool on) { if (_trimCheck != null) _trimCheck.Checked = on; }
        public bool AllowTrimChecked { get { return _trimCheck != null && _trimCheck.Checked; } }"""),
])

# ---------- Probe：trim flag
patch('SurfaceUnifyPlugin.cs', [
("""            if (kv.TryGetValue("holes", out v)) s.KeepHoles = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));""",
 """            if (kv.TryGetValue("holes", out v)) s.KeepHoles = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
            if (kv.TryGetValue("trim", out v)) s.AllowTrim = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));"""),
("""    ///   grid=12  fit=1  smooth=0.15  lock=1  holes=1  snap=0""",
 """    ///   grid=12  fit=1  smooth=0.15  lock=1  holes=1  trim=1  snap=0"""),
])

# ---------- 自检：面板钩子 + 修剪功能对照
patch('SurfaceUnifySelfTest.cs', [
("""                panel.SetKeepHoles(true);""",
 """                panel.SetKeepHoles(true);

                panel.SetAllowTrim(false);
                Check(sb, ref pass, ref fail, !panel.AllowTrimChecked && !panel.Settings.AllowTrim, "面板：允许修剪 取消勾选 → Settings.AllowTrim = false");
                panel.SetAllowTrim(true);
                Check(sb, ref pass, ref fail, panel.AllowTrimChecked && panel.Settings.AllowTrim, "面板：允许修剪 勾选 → Settings.AllowTrim = true");"""),
("""            BoundingBox bb = r.Result.GetBoundingBox(true);
            bool bbOk = Math.Abs(bb.Min.X) < 1e-6 && Math.Abs(bb.Min.Y) < 1e-6 &&
                        Math.Abs(bb.Max.X - 20) < 1e-6 && Math.Abs(bb.Max.Y - 10) < 1e-6;""",
 """            // 允许修剪：域外扩 + 按原边界剪掉多余部分 → 结果边界贴齐原边界
            var sTrim = new SurfaceUnifySettings { AllowTrim = true };
            string repT;
            SurfaceUnifyResult rTrim = SurfaceUnifyCore.Generate(poly, sTrim, out repT);
            Check(sb, ref pass, ref fail, rTrim.Result != null && rTrim.Trimmed && rTrim.BoundaryDeviation < 0.01,
                string.Format(CultureInfo.InvariantCulture, "允许修剪：外扩域 + 按原边界剪掉 → 边界偏差 {0:0.####}（应 ≈ 0）",
                    rTrim != null ? rTrim.BoundaryDeviation : -1));
            var sNo = new SurfaceUnifySettings { AllowTrim = false };
            string repN;
            SurfaceUnifyResult rNo = SurfaceUnifyCore.Generate(poly, sNo, out repN);
            Check(sb, ref pass, ref fail, rNo.Result != null && !rNo.Trimmed && Math.Abs(rNo.AreaRatio - 1.0) < 0.02,
                string.Format(CultureInfo.InvariantCulture, "关掉修剪：不外扩不修剪（原来的效果，面积比 {0:0.####}）",
                    rNo != null ? rNo.AreaRatio : -1));

            BoundingBox bb = r.Result.GetBoundingBox(true);
            bool bbOk = Math.Abs(bb.Min.X) < 1e-6 && Math.Abs(bb.Min.Y) < 1e-6 &&
                        Math.Abs(bb.Max.X - 20) < 1e-6 && Math.Abs(bb.Max.Y - 10) < 1e-6;"""),
])
print('done')
