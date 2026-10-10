# -*- coding: utf-8 -*-
"""Patch 模式：允许修剪 = 用 Rhino Brep.CreatePatch 拟合（边界曲线当约束）+ 修剪"""
from pathlib import Path
p = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\surfaceunify\src\SurfaceUnifyCore.cs')
t = p.read_text(encoding='utf-8-sig')

# ① 在 wrapped 判定之后插入 Patch 早返回
anchor = """            if (wrapped)
            {
                int rowsUsed, stalled; bool degPole;"""
insert = """            // ---- Patch 模式（用户口径）：允许修剪时用 Rhino 的 Patch 拟合 ——
            //  边界曲线当约束 → 边界与拐角精确过点，内部尽量贴合，再按边界修剪。
            //  失败自动退回下面的 Coons + 射线。
            if (s.AllowTrim && !wrapped)
            {
                Brep patched = TryPatchFit(brep, sheet, loops, s, diag, notes);
                if (patched != null)
                {
                    res.Result = patched;
                    res.Trimmed = true;
                    try { res.Surface = patched.Faces[0].UnderlyingSurface(); } catch { }
                    NurbsSurface ns = res.Surface as NurbsSurface;
                    if (ns != null) { try { res.ControlU = ns.Points.CountU; res.ControlV = ns.Points.CountV; } catch { } }
                    res.Faces = patched.Faces.Count;
                    try { res.Loops = patched.Faces[0].Loops.Count; } catch { }
                    res.HolesFound = loops.Length > 0 ? loops.Length - 1 : 0;
                    res.Holes = s.KeepHoles ? res.HolesFound : 0;
                    double md, rd, bd;
                    if (MeasureTrimmedDeviation(patched, brep, sheet, cap, out md, out rd))
                    {
                        res.MaxDeviation = md; res.RmsDeviation = rd;
                    }
                    if (MeasureResultBoundary(patched, loops, out bd)) res.BoundaryDeviation = bd;
                    double aIn = AreaOf(sheet);
                    res.AreaRatio = aIn > 1e-12 ? AreaOf(patched) / aIn : 0.0;
                    res.Diag = diag;
                    res.Note = string.Format(CultureInfo.InvariantCulture,
                        "Patch 拟合：{0} 张面 · 控制点 {1}×{2} · 最大偏差 {3:0.####} / 平均 {4:0.####} · 边界偏差 {5:0.####} · 面积比 {6:0.####} · {7:0.00}s（边界曲线当约束、已按边界修剪）",
                        res.Faces, res.ControlU, res.ControlV, res.MaxDeviation, res.RmsDeviation,
                        res.BoundaryDeviation, res.AreaRatio, sw.Elapsed.TotalSeconds);
                    report = res.Note;
                    res.Seconds = sw.Elapsed.TotalSeconds;
                    return res;
                }
                notes.Add("Patch 拟合未成功 → 退回 Coons + 射线");
            }

            if (wrapped)
            {
                int rowsUsed, stalled; bool degPole;"""
print('A', anchor in t)
t = t.replace(anchor, insert, 1)

# ② 新方法
anchor2 = """        /// <summary>点是否落在某个内孔环里（把点和环都投到拟合平面上做 2D 射线法）</summary>"""
method = """        /// <summary>
        /// Rhino Patch 式拟合：把原曲面 + 边界（外环与内孔）曲线交给 `Brep.CreatePatch`，
        /// 边界曲线当约束 → 边界与拐角都精确过点，内部尽量贴合原曲面；结果按边界修剪。
        /// 失败返回 null（调用方退回 Coons + 射线）。
        /// </summary>
        internal static Brep TryPatchFit(Brep brep, Mesh sheet, Polyline[] loops, SurfaceUnifySettings s,
            double diag, List<string> notes)
        {
            if (loops == null || loops.Length == 0) return null;
            var geo = new List<GeometryBase>();
            if (brep != null) geo.Add(brep);
            else if (sheet != null) geo.Add(sheet);
            int curveCount = 0;
            for (int i = 0; i < loops.Length; i++)
            {
                try
                {
                    Polyline pl = loops[i];
                    if (pl == null || pl.Count < 3) continue;
                    var pts = new List<Point3d>(pl.Count + 1);
                    for (int k = 0; k < pl.Count; k++) pts.Add(pl[k]);
                    if (pts[0].DistanceTo(pts[pts.Count - 1]) > 1e-9) pts.Add(pts[0]);
                    geo.Add(new PolylineCurve(new Polyline(pts)));
                    curveCount++;
                }
                catch { }
            }
            if (curveCount == 0) return null;
            int spans = Math.Max(2, s.GridCount - 3);
            double tol = Math.Max(diag * 1e-3, 1e-9);
            Brep patch = null;
            try { patch = Brep.CreatePatch(geo, spans, spans, tol); } catch { patch = null; }
            if (patch == null || patch.Faces.Count == 0) return null;
            int loopCount = 0;
            try { loopCount = patch.Faces[0].Loops.Count; } catch { }
            notes.Add(string.Format(CultureInfo.InvariantCulture,
                "Patch 拟合：{0} 条边界曲线当约束 · {1}×{1} spans · 容差 {2:0.####} → {3} 张面 / {4} 个修剪环",
                curveCount, spans, tol, patch.Faces.Count, loopCount));
            return patch;
        }

        /// <summary>点是否落在某个内孔环里（把点和环都投到拟合平面上做 2D 射线法）</summary>"""
print('B', anchor2 in t)
t = t.replace(anchor2, method, 1)

p.write_text(t, encoding='utf-8')
print('written')
