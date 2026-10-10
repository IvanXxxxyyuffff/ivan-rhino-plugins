# -*- coding: utf-8 -*-
"""① 边界钉合（外扩时把落在原边界带上的网格点钉到原边界）
   ② 修剪后偏差按**修剪后的面**量（域外裙边不参与）"""
from pathlib import Path
p = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\surfaceunify\src\SurfaceUnifyCore.cs')
t = p.read_text(encoding='utf-8-sig')
pairs = []

# ① 边界钉合：接在 Coons 分支的贴合循环之后（该块以 4 个 if/for 结尾，用 「                }\n            }\n\n            // ---- ⑤ 光顺」定位）
pairs.append((
"""                }
            }

            // ---- ⑤ 光顺（边界保形 = 边界点不参与）""",
"""                }

                // 边界钉合（外扩时）：把落在**原边界带**上的网格点钉到原边界上。
                // 不做这一步，修剪线只是「原边界投到结果面」的投影，结果面在边界处差多少、边界就偏多少
                // （实测：不钉合时修剪后边界偏差 11.25，钉合后才可能贴齐）
                if (extend)
                {
                    double pin = Math.Max(cell * 0.9, 1e-9);
                    int pinned = 0;
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                        {
                            if (DistToPolylines(loops, grid[i, j]) > pin) continue;
                            Point3d q = ClosestOnPolylines(loops, grid[i, j]);
                            if (q.IsValid) { grid[i, j] = q; pinned++; }
                        }
                    if (pinned > 0) notes.Add("边界钉合：" + pinned + " 个网格点钉到原边界上");
                }
            }

            // ---- ⑤ 光顺（边界保形 = 边界点不参与）"""))

# ② 修剪后偏差按修剪后的面量
pairs.append((
"""            double bdev;
            if (MeasureResultBoundary(result, loops, out bdev)) res.BoundaryDeviation = bdev;""",
"""            double bdev;
            if (MeasureResultBoundary(result, loops, out bdev)) res.BoundaryDeviation = bdev;
            // 修剪过 → 偏差按**修剪后的面**重新量一遍（域外的裙边不该算进偏差里）
            if (res.Trimmed)
            {
                double md, rd;
                if (MeasureTrimmedDeviation(result, brep, sheet, cap, out md, out rd))
                {
                    res.MaxDeviation = md;
                    res.RmsDeviation = rd;
                }
            }"""))

# ③ 两个新工具函数
pairs.append((
"""        internal static double DistToPolylines(Polyline[] polys, Point3d p)""",
"""        /// <summary>点到多段线集合的最近点</summary>
        internal static Point3d ClosestOnPolylines(Polyline[] polys, Point3d p)
        {
            Point3d best = Point3d.Unset;
            double bestD = double.MaxValue;
            for (int i = 0; i < polys.Length; i++)
            {
                Polyline pl = polys[i];
                if (pl == null || pl.Count < 2) continue;
                for (int j = 0; j < pl.Count - 1; j++)
                {
                    Point3d q = ClosestOnSegment(p, pl[j], pl[j + 1]);
                    double d = q.DistanceTo(p);
                    if (d < bestD) { bestD = d; best = q; }
                }
                if (pl.IsClosed && pl.Count >= 3)
                {
                    Point3d q = ClosestOnSegment(p, pl[pl.Count - 1], pl[0]);
                    double d = q.DistanceTo(p);
                    if (d < bestD) { bestD = d; best = q; }
                }
            }
            return best;
        }

        internal static Point3d ClosestOnSegment(Point3d p, Point3d a, Point3d b)
        {
            Vector3d ab = b - a;
            double l2 = ab.SquareLength;
            if (l2 < 1e-18) return a;
            double tt = ((p - a) * ab) / l2;
            if (tt < 0) tt = 0; else if (tt > 1) tt = 1;
            return a + ab * tt;
        }

        /// <summary>修剪后的结果面偏差：把结果面网格化，取顶点到原曲面/网格的距离（最大 + RMS）</summary>
        internal static bool MeasureTrimmedDeviation(Brep result, Brep brep, Mesh sheet, double cap,
            out double maxDev, out double rmsDev)
        {
            maxDev = 0; rmsDev = 0;
            if (result == null) return false;
            Mesh m = null;
            try
            {
                var mp = new MeshingParameters();
                mp.MaximumEdgeLength = Math.Max(cap * 0.02, 1e-9);
                Mesh[] parts = Mesh.CreateFromBrep(result, mp);
                if (parts != null && parts.Length > 0)
                {
                    m = new Mesh();
                    for (int i = 0; i < parts.Length; i++) if (parts[i] != null) m.Append(parts[i]);
                }
            }
            catch { m = null; }
            if (m == null || m.Vertices.Count == 0) return false;
            double mcap = Math.Max(cap * 4.0, cap);
            double sum = 0; int cnt = 0;
            int stride = Math.Max(1, m.Vertices.Count / 400);
            for (int i = 0; i < m.Vertices.Count; i += stride)
            {
                Point3d q = m.Vertices.Point3dAt(i);
                Point3d hit; Vector3d hn; bool al, ur;
                if (!SnapToSheet(brep, sheet, q, mcap, Vector3d.Zero, out hit, out hn, out al, out ur)) continue;
                double d = q.DistanceTo(hit);
                if (d > maxDev) maxDev = d;
                sum += d * d; cnt++;
            }
            if (cnt == 0) return false;
            rmsDev = Math.Sqrt(sum / cnt);
            return true;
        }

        internal static double DistToPolylines(Polyline[] polys, Point3d p)"""))

for a, b in pairs:
    ok = a in t
    print(('OK  ' if ok else 'MISS'), a.strip().split('\n')[0][:64])
    if ok:
        t = t.replace(a, b, 1)
p.write_text(t, encoding='utf-8')
print('written')
