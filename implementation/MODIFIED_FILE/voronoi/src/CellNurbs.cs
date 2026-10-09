using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace VoronoiTexture
{
    /// <summary>
    /// 每胞元一张 NURBS 曲面（用户口径）：
    ///   · 每个细胞 = **一张**平滑 NURBS 曲面（不是一堆小方块面）；
    ///   · 细胞之间只要 **G0**（边界重合）——胞元边界处高度为 0，两边的曲面在同一圈上接住；
    ///   · **细胞边缘严格贴合原曲面**（h=0 的那些点回投到面上，不是切平面近似）。
    /// 做法：胞元多边形内的高度场 → 控制网点阵 → NurbsSurface.CreateFromPoints → 按胞元边界修剪。
    /// </summary>
    internal static class CellNurbs
    {
        const int GridN = 12;                 // 控制网 13x13（够表达平顶/穹顶 + 沟槽过渡）
        const double BboxPad = 0.35;          // 多边形包围盒外扩（占胞元尺寸比例）

        /// <summary>某个面的全部胞元曲面（失败/退化的胞元跳过）</summary>
        public static List<Brep> Build(
            Voronoi.FaceCtx ctx, SeedCloud3 cloud, List<Point2d> seeds,
            double cell, double depth, double edgeW, double shape, int cellShape, double domePower,
            Gradient grad, double u2m, double tol, double budgetSec,
            out int trimmed, out int untrimmed, out int skipped, out string note)
        {
            trimmed = 0; untrimmed = 0; skipped = 0;
            note = "";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool timeUp = false;
            var outp = new List<Brep>();
            if (ctx == null || !ctx.Ok || seeds == null || seeds.Count == 0) return outp;

            SurfaceMap map = ctx.Map;
            BrepFace face = map.Face;
            double half = cell * 0.5;
            double nearR = cell * 3.0;

            bool planarFace = ctx.Map.Planar;      // 平面面：h=0 的点本来就在面上，不用再 ClosestPoint
            for (int i = 0; i < seeds.Count; i++)
            {
                // 时间预算：到点就停，并如实报告（宁可少建几个，也不能把 Rhino 卡住）
                if (budgetSec > 0 && clock.Elapsed.TotalSeconds > budgetSec)
                {
                    timeUp = true; skipped += seeds.Count - i; break;
                }
                try
                {
                    Point2d a = seeds[i];

                    // ---- 1) 胞元多边形：以 a 为中心，用「3D 距离 3 倍胞元以内」的邻居做半平面裁剪
                    var poly = new List<Point2d>(8);
                    double half0 = nearR;
                    poly.Add(new Point2d(a.X - half0, a.Y - half0));
                    poly.Add(new Point2d(a.X + half0, a.Y - half0));
                    poly.Add(new Point2d(a.X + half0, a.Y + half0));
                    poly.Add(new Point2d(a.X - half0, a.Y + half0));
                    Point3d A3 = map.OffsetPoint(a.X, a.Y, 0.0);
                    int nbr = 0;
                    for (int j = 0; j < seeds.Count && poly.Count >= 3; j++)
                    {
                        if (j == i) continue;
                        Point2d b = seeds[j];
                        double d2 = b.DistanceTo(a);
                        if (d2 > nearR) continue;
                        Point3d B3 = map.OffsetPoint(b.X, b.Y, 0.0);
                        if (!A3.IsValid || !B3.IsValid) continue;
                        if (A3.DistanceTo(B3) > nearR) continue;      // 3D 确认
                        nbr++;
                        poly = ClipHalf(poly, a, b);
                    }
                    if (poly.Count < 3 || nbr < 2) { skipped++; continue; }

                    double px0 = double.MaxValue, px1 = double.MinValue, py0 = double.MaxValue, py1 = double.MinValue;
                    for (int k = 0; k < poly.Count; k++)
                    {
                        Point2d q = poly[k];
                        if (q.X < px0) px0 = q.X; if (q.X > px1) px1 = q.X;
                        if (q.Y < py0) py0 = q.Y; if (q.Y > py1) py1 = q.Y;
                    }
                    double pad = cell * BboxPad;
                    px0 -= pad; px1 += pad; py0 -= pad; py1 += pad;
                    double w = px1 - px0, h = py1 - py0;
                    if (!(w > 1e-9) || !(h > 1e-9)) { skipped++; continue; }

                    // ---- 2) 控制网点阵：胞元内 = 浮雕高度；胞元外 = 0（平贴基准面）
                    var pts = new List<Point3d>((GridN + 1) * (GridN + 1));
                    for (int iv = 0; iv <= GridN; iv++)
                        for (int iu = 0; iu <= GridN; iu++)
                        {
                            double x = px0 + w * iu / (double)GridN;
                            double y = py0 + h * iv / (double)GridN;
                            Point2d q = new Point2d(x, y);
                            double hh = 0.0;
                            if (Inside(poly, q))
                            {
                                Point3d P = map.OffsetPoint(x, y, 0.0);
                                if (P.IsValid)
                                {
                                    double d1, d2;
                                    cloud.Nearest2(P, out d1, out d2);
                                    if (d1 != double.MaxValue)
                                    {
                                        if (d2 == double.MaxValue) d2 = d1;
                                        double gap = (d2 - d1) * 0.5;
                                        if (cellShape == 1)
                                        {
                                            double t = half > 1e-9 ? gap / half : 1.0;
                                            if (t > 1) t = 1;
                                            hh = depth * Math.Pow(t, domePower > 0.05 ? domePower : 1.0);
                                        }
                                        else
                                        {
                                            double ew = edgeW;
                                            if (grad != null && grad.On)
                                            {
                                                double kk = grad.ScaleAt(P);
                                                if (kk > 1e-6) ew = edgeW * kk;
                                            }
                                            double t = ew > 1e-9 ? gap / ew : 1.0;
                                            if (t > 1) t = 1;
                                            double f = t * t * (3 - 2 * t);          // Smoothstep
                                            if (shape != 1.0) f = Math.Pow(f, shape);
                                            hh = depth * f;
                                        }
                                    }
                                }
                            }
                            Point3d p3;
                            if (Math.Abs(hh) < 1e-9)
                            {
                                // 边界/胞元外：严格回投到原曲面（用户要求：细胞边缘贴合原曲面）。
                                // 平面面不用回投（本来就精确）；曲面面只对**胞元内部**的那一圈做（外圈用切平面近似，够用且快）。
                                Point3d baseP = map.To3d(x, y);
                                p3 = baseP;
                                if (!planarFace && Inside(poly, q))
                                {
                                    double uu, vv;
                                    if (baseP.IsValid && face.ClosestPoint(baseP, out uu, out vv))
                                    {
                                        Point3d onFace = face.PointAt(uu, vv);
                                        if (onFace.IsValid) p3 = onFace;
                                    }
                                }
                            }
                            else p3 = map.OffsetPoint(x, y, hh);
                            if (!p3.IsValid) { skipped++; goto NextSeed; }
                            pts.Add(p3);
                        }

                    // ---- 3) 成面（NURBS）
                    NurbsSurface ns = null;
                    try { ns = NurbsSurface.CreateFromPoints(pts, GridN + 1, GridN + 1, 3, 3); } catch { ns = null; }
                    if (ns == null) { skipped++; continue; }
                    Brep patch = null;
                    try { patch = Brep.CreateFromSurface(ns); } catch { patch = null; }
                    if (patch == null || patch.Faces.Count == 0) { skipped++; continue; }

                    // ---- 4) 按胞元边界修剪（修剪曲线严格落在曲面上：用曲面自身参数域取点）
                    bool ok = false;
                    try
                    {
                        Interval du = ns.Domain(0), dv = ns.Domain(1);
                        var ring = new List<Point3d>(poly.Count + 1);
                        for (int k = 0; k < poly.Count; k++)
                        {
                            double uu = du.Min + (poly[k].X - px0) / w * (du.Max - du.Min);
                            double vv = dv.Min + (poly[k].Y - py0) / h * (dv.Max - dv.Min);
                            Point3d q = ns.PointAt(uu, vv);
                            if (!q.IsValid) { ring.Clear(); break; }
                            ring.Add(q);
                        }
                        // 合理性检查：修剪曲线必须落在定义域附近、长度量级正确（否则 Split 可能卡死）
                        bool sane = ring.Count >= 3;
                        if (sane)
                        {
                            double diag = Math.Sqrt(w * w + h * h);
                            double len = 0;
                            for (int k = 0; k + 1 < ring.Count; k++) len += ring[k].DistanceTo(ring[k + 1]);
                            if (!(len > 1e-9) || len > diag * 8.0) sane = false;
                            for (int k = 0; k < ring.Count && sane; k++)
                            {
                                double uu, vv;
                                if (!ns.ClosestPoint(ring[k], out uu, out vv)) sane = false;
                                else if (Math.Abs(uu) > 1e6 || Math.Abs(vv) > 1e6) sane = false;
                            }
                        }
                        if (sane)
                        {
                            ring.Add(ring[0]);
                            var trimCurve = new PolylineCurve(ring);
                            Brep sp = patch.Faces[0].Split(new Curve[] { trimCurve }, tol);
                            if (sp != null && sp.Faces.Count > 0)
                            {
                                double want = Math.Abs(PolyArea(poly));
                                Brep best = null; double bestErr = double.MaxValue;
                                for (int k = 0; k < sp.Faces.Count; k++)
                                {
                                    Brep piece = sp.Faces[k].DuplicateFace(true);
                                    if (piece == null || piece.Faces.Count == 0) continue;
                                    var amp = AreaMassProperties.Compute(piece);
                                    double ar = amp != null ? amp.Area : 0;
                                    double err = Math.Abs(ar - want);
                                    if (err < bestErr) { bestErr = err; best = piece; }
                                }
                                if (best != null) { patch = best; ok = true; }
                            }
                        }
                    }
                    catch { ok = false; }
                    if (ok) trimmed++; else untrimmed++;

                    outp.Add(patch);
                    continue;
                NextSeed: ;
                }
                catch { skipped++; }
            }
            if (timeUp)
                note = string.Format("胞元曲面时间预算 {0:0.#} 秒用尽：本面已建 {1} 个、剩余 {2} 个跳过（胞元多时请调大胞元尺寸，或分批生成）",
                    budgetSec, outp.Count, skipped);
            return outp;
        }

        static bool Inside(List<Point2d> poly, Point2d p)
        {
            bool inx = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                    (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X))
                    inx = !inx;
            }
            return inx;
        }

        static double PolyArea(List<Point2d> poly)
        {
            double a2 = 0;
            for (int k = 0; k < poly.Count; k++)
            {
                Point2d a = poly[k], b = poly[(k + 1) % poly.Count];
                a2 += a.X * b.Y - b.X * a.Y;
            }
            return a2 * 0.5;
        }

        /// <summary>保留「离 a 比离 b 近」的一侧（Sutherland–Hodgman）</summary>
        static List<Point2d> ClipHalf(List<Point2d> poly, Point2d a, Point2d b)
        {
            var outp = new List<Point2d>(poly.Count + 2);
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            double nx = b.X - a.X, ny = b.Y - a.Y;
            double nl = Math.Sqrt(nx * nx + ny * ny);
            if (nl < 1e-12) return poly;
            nx /= nl; ny /= nl;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d p = poly[i], q = poly[(i + 1) % poly.Count];
                double dp = (p.X - mx) * nx + (p.Y - my) * ny;
                double dq = (q.X - mx) * nx + (q.Y - my) * ny;
                if (dp <= 0) outp.Add(p);
                if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))
                {
                    double t = dp / (dp - dq);
                    outp.Add(new Point2d(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                }
            }
            return outp;
        }
    }
}
