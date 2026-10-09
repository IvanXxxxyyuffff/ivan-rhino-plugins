using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace HalftonePattern
{
    /// <summary>
    /// 多重曲面「整体」排版：整个对象当一个整体排阵列 —— 在**拟合平面**里铺候选点，
    /// 再投到离它最近的面上成形。接缝两侧因此是同一个阵列的延续。
    ///
    /// 关键点（踩过的坑）：参考平面不能用「面积最大的平面面」——曲面对象没有平面面，
    /// 退化成「包围盒中心的水平面」时可能与曲面几乎垂直（实测 7616 个候选只有 25 个投到面上）。
    /// 这里改成**对整个对象做最佳拟合平面**（Plane.FitPlaneToPoints），曲面也能贴上。
    /// 若对象弯得太厉害（命中率过低）则返回 null，调用方退回「逐面 + 全局相位锚点」。
    /// </summary>
    internal static class HalftoneGlobal
    {
        /// <summary>多重曲面的全局相位基准：一个 3D 锚点（参考面 2D 区域中心）</summary>
        internal class Layout
        {
            public int RefFace;
            public Plane Frame;
            public Point3d Anchor;
            public bool HasAnchor;
        }

        /// <summary>定全局锚点：参考面 = 面积最大的平面面（没有平面就取第一个面），锚点 = 该面包围盒中心在面上的最近点</summary>
        internal static Layout BuildLayout(Brep brep)
        {
            var L = new Layout();
            if (brep == null || brep.Faces.Count == 0) return L;
            int best = 0; double bestArea = -1;
            for (int i = 0; i < brep.Faces.Count; i++)
            {
                Plane pl;
                bool planar = false;
                try { planar = brep.Faces[i].TryGetPlane(out pl, 1e-3) && pl.IsValid; } catch { planar = false; }
                if (!planar) continue;
                double a = 0;
                try
                {
                    Brep fb = brep.Faces[i].DuplicateFace(false);
                    if (fb != null) { var amp = AreaMassProperties.Compute(fb); if (amp != null) a = amp.Area; }
                }
                catch { a = 0; }
                if (a > bestArea) { bestArea = a; best = i; }
            }
            L.RefFace = best;
            try
            {
                BoundingBox bb = brep.Faces[best].GetBoundingBox(true);
                if (bb.IsValid)
                {
                    Point3d c = bb.Center;
                    double u, v;
                    if (brep.Faces[best].ClosestPoint(c, out u, out v))
                    {
                        Point3d p = brep.Faces[best].PointAt(u, v);
                        if (p.IsValid) { L.Anchor = p; L.HasAnchor = true; }
                    }
                }
            }
            catch { }
            return L;
        }

        /// <summary>最佳拟合平面：在整个对象上撒点做拟合（曲面对象也能得到贴合它的基准面）</summary>
        static Plane FitPlane(Brep brep, out bool ok)
        {
            ok = false;
            Plane pl = Plane.Unset;
            try
            {
                var pts = new List<Point3d>();
                for (int fi = 0; fi < brep.Faces.Count; fi++)
                {
                    BrepFace f = brep.Faces[fi];
                    double u0 = f.Domain(0).Min, u1 = f.Domain(0).Max;
                    double v0 = f.Domain(1).Min, v1 = f.Domain(1).Max;
                    for (int i = 0; i <= 4; i++)
                        for (int j = 0; j <= 4; j++)
                        {
                            double u = u0 + (u1 - u0) * i / 4.0, v = v0 + (v1 - v0) * j / 4.0;
                            Point3d p = f.PointAt(u, v);
                            if (!p.IsValid) continue;
                            pts.Add(p);
                        }
                }
                if (pts.Count >= 3)
                {
                    Plane fit;
                    if (Plane.FitPlaneToPoints(pts, out fit) == PlaneFitResult.Success && fit.IsValid)
                    {
                        pl = fit; ok = true;
                    }
                }
            }
            catch { ok = false; }
            if (!ok)
            {
                BoundingBox bb = brep.GetBoundingBox(true);
                if (bb.IsValid) { pl = new Plane(bb.Center, Vector3d.ZAxis); ok = true; }
            }
            return pl;
        }

        /// <summary>
        /// 全局排版。成功返回按面归类的图形；命中率太低（对象太弯）返回 null，调用方退回逐面模式。
        /// </summary>
        internal static List<HalftoneFaceResult> Generate(Brep brep, HalftoneSettings s, out string report)
        {
            report = "";
            int nf = brep.Faces.Count;
            var results = new List<HalftoneFaceResult>(nf);
            for (int i = 0; i < nf; i++)
                results.Add(new HalftoneFaceResult { FaceIndex = i, Planar = IsPlanar(brep.Faces[i]) });

            double u2m = s.MmToModel > 0 ? s.MmToModel : 1.0;
            double pitch = s.Pitch * u2m;
            double margin = s.Margin * u2m;
            double rMax = s.MaxDia * u2m * 0.5;
            double rMin = s.MinDia * u2m * 0.5;
            double rot = s.Rotation * Math.PI / 180.0;
            if (!(pitch > 1e-9)) { report = "间距无效"; return null; }

            bool okPlane;
            Plane gp = FitPlane(brep, out okPlane);
            if (!okPlane) { report = "拟合平面失败"; return null; }

            Point3d gcenter = Point3d.Unset;
            if (s.UsePickedCenter && s.CenterPoints != null && s.CenterPoints.Count > 0)
            {
                var acc = new Point3d(0, 0, 0);
                foreach (Point3d p in s.CenterPoints) acc += p;
                gcenter = new Point3d(acc.X / s.CenterPoints.Count, acc.Y / s.CenterPoints.Count, acc.Z / s.CenterPoints.Count);
            }
            BoundingBox bc = brep.GetBoundingBox(true);
            if (!gcenter.IsValid && bc.IsValid) gcenter = bc.Center;
            if (!gcenter.IsValid) gcenter = gp.Origin;

            double gmax = 0;
            if (bc.IsValid)
                for (int i = 0; i < 8; i++)
                {
                    Point3d c = BBoxPoint(bc, (i & 1) == 0 ? bc.Min.X : bc.Max.X,
                                           (i & 2) == 0 ? bc.Min.Y : bc.Max.Y,
                                           (i & 4) == 0 ? bc.Min.Z : bc.Max.Z);
                    double d = c.DistanceTo(gcenter);
                    if (d > gmax) gmax = d;
                }
            if (!(gmax > 1e-9)) gmax = 1.0;

            if (!bc.IsValid) { report = "无有效包围盒"; return null; }
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Point3d c = BBoxPoint(bc, (i & 1) == 0 ? bc.Min.X : bc.Max.X,
                                       (i & 2) == 0 ? bc.Min.Y : bc.Max.Y,
                                       (i & 4) == 0 ? bc.Min.Z : bc.Max.Z);
                Vector3d v = c - gp.Origin;
                double px = v * gp.XAxis, py = v * gp.YAxis;
                if (px < x0) x0 = px; if (px > x1) x1 = px;
                if (py < y0) y0 = py; if (py > y1) y1 = py;
            }
            x0 -= pitch; x1 += pitch; y0 -= pitch; y1 += pitch;

            var cands = new List<Point3d>();
            var idxs = new List<int>();
            BuildLattice(s.ArrayMode, gp, gcenter, pitch, s.Jitter, 7, x0, x1, y0, y1, cands, idxs);
            if (cands.Count == 0) { report = "阵列点为空"; return null; }
            if (cands.Count > 200000) { report = "阵列过密（超过 20 万个候选点），请增大间距"; return null; }

            double keepR = pitch * 1.2;
            double dedupR = pitch * 0.35;
            var kept = new List<Point3d>();
            var keptFace = new List<int>();
            var keptIdx = new List<int>();
            var buckets = new Dictionary<long, List<int>>();
            double dcell = Math.Max(dedupR, 1e-6);
            int skipped = 0, dup = 0;
            for (int k = 0; k < cands.Count; k++)
            {
                Point3d Q = cands[k];
                Point3d cp = Point3d.Unset;
                ComponentIndex ci = ComponentIndex.Unset;
                double u = 0, v = 0;
                Vector3d n = Vector3d.Zero;
                bool ok = false;
                try { ok = brep.ClosestPoint(Q, out cp, out ci, out u, out v, keepR, out n); } catch { ok = false; }
                if (!ok || !cp.IsValid) { skipped++; continue; }
                if (ci.ComponentIndexType != ComponentIndexType.BrepFace) { skipped++; continue; }
                int fi = ci.Index;
                if (fi < 0 || fi >= nf) { skipped++; continue; }

                int cx = (int)Math.Floor(cp.X / dcell), cy = (int)Math.Floor(cp.Y / dcell), cz = (int)Math.Floor(cp.Z / dcell);
                bool near = false;
                for (int ax = -1; ax <= 1 && !near; ax++)
                    for (int ay = -1; ay <= 1 && !near; ay++)
                        for (int az = -1; az <= 1 && !near; az++)
                        {
                            List<int> lst;
                            if (!buckets.TryGetValue(Key(cx + ax, cy + ay, cz + az), out lst)) continue;
                            for (int q = 0; q < lst.Count; q++)
                                if (kept[lst[q]].DistanceTo(cp) < dedupR) { near = true; break; }
                        }
                if (near) { dup++; continue; }

                int id = kept.Count;
                kept.Add(cp); keptFace.Add(fi); keptIdx.Add(idxs[k]);
                List<int> l2;
                long key = Key(cx, cy, cz);
                if (!buckets.TryGetValue(key, out l2)) { l2 = new List<int>(2); buckets[key] = l2; }
                l2.Add(id);
            }

            int hit = kept.Count;
            if (cands.Count > 20 && hit * 100 < cands.Count * 15)
            {
                report = string.Format("对象弯曲过大，全局排版命中率太低（{0}/{1}）", hit, cands.Count);
                return null;
            }

            List<Curve> borders = BorderEdges(brep);
            int made = 0, offEdge = 0;
            double minEdge = double.MaxValue;
            for (int k = 0; k < kept.Count; k++)
            {
                Point3d p = kept[k];
                int fi = keptFace[k];
                double r = rMin + (rMax - rMin) * Math.Pow(1.0 - Clamp01(p.DistanceTo(gcenter) / gmax), s.Falloff > 0.01 ? s.Falloff : 1.0);
                if (r < 1e-4) continue;

                double need = margin + r;
                if (borders.Count > 0)
                {
                    double dEdge = DistanceToCurves(borders, p, need);
                    if (dEdge < need) { offEdge++; continue; }
                    if (dEdge < minEdge) minEdge = dEdge;
                }

                BrepFace face = brep.Faces[fi];
                Vector3d n = Vector3d.Zero;
                double u, v;
                if (face.ClosestPoint(p, out u, out v)) n = face.NormalAt(u, v);
                if (!n.IsValid || !n.Unitize()) continue;
                Curve c = ShapeOnFace(face, p, n, r, s.Shape, keptIdx[k], k, rot);
                if (c != null) { results[fi].Shapes.Add(c); made++; }
            }
            for (int i = 0; i < results.Count; i++) results[i].MinEdge = minEdge;
            report = string.Format("全局排版（拟合平面）：候选 {0} → 命中 {1}（离面太远 {2}、重复 {3}）→ 成形 {4}（贴边去掉 {5}）",
                cands.Count, hit, skipped, dup, made, offEdge);
            return results;
        }

        /// <summary>
        /// BoundingBox.PointAt 要的是 0..1 的**比例**，不是绝对坐标 —— 把绝对坐标当比例传会让范围炸开
        /// （踩过：候选点从 200 个炸到 20 万+，曲面命中率掉到 35/7616）。这里统一走这个显式构造函数。
        /// </summary>
        static Point3d BBoxPoint(BoundingBox bb, double x, double y, double z)
        {
            return new Point3d(x, y, z);
        }

        static double Clamp01(double t) { return t < 0 ? 0 : (t > 1 ? 1 : t); }

        static bool IsPlanar(BrepFace f)
        {
            try { Plane pl; return f.TryGetPlane(out pl, 1e-3) && pl.IsValid; }
            catch { return false; }
        }

        /// <summary>在拟合平面里铺阵列点（网格 / 交错 / 六边 / 同心环 / 螺旋 / 抖动）</summary>
        static void BuildLattice(int mode, Plane gp, Point3d center3d, double pitch, double jitter, int seed,
                                 double x0, double x1, double y0, double y1, List<Point3d> outp, List<int> idx)
        {
            var rnd = new Rng32(seed);
            Vector3d vc = center3d - gp.Origin;
            double cx = vc * gp.XAxis, cy = vc * gp.YAxis;

            if (mode == 3 || mode == 4 || mode == 5)
            {
                double rmax = 0;
                double[] xs = { x0, x1 }, ys = { y0, y1 };
                for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        double dx = xs[i] - cx, dy = ys[j] - cy;
                        double dd = Math.Sqrt(dx * dx + dy * dy);
                        if (dd > rmax) rmax = dd;
                    }
                if (mode == 4)
                {
                    double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
                    int n = (int)Math.Round(Math.PI * rmax * rmax / (pitch * pitch));
                    if (n < 1) n = 1;
                    if (n > 200000) n = 200000;
                    for (int k = 0; k < n; k++)
                    {
                        double t = (k + 0.5) / n;
                        double rr = rmax * Math.Sqrt(t);
                        double a = golden * k;
                        outp.Add(gp.PointAt(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
                        idx.Add(k);
                    }
                    return;
                }
                int rings = (int)Math.Floor(rmax / pitch) + 1;
                if (rings > 3000) rings = 3000;
                for (int ring = 0; ring < rings; ring++)
                {
                    double rr = ring * pitch;
                    if (rr < 1e-9) { outp.Add(gp.PointAt(cx, cy)); idx.Add(0); continue; }
                    int n = (int)Math.Round(2.0 * Math.PI * rr / pitch);
                    if (n < 1) n = 1;
                    double off = (mode == 3 && (ring % 2 == 1)) ? Math.PI / n : 0.0;
                    for (int i = 0; i < n; i++)
                    {
                        double a = off + 2.0 * Math.PI * i / n;
                        double px = cx + rr * Math.Cos(a), py = cy + rr * Math.Sin(a);
                        if (mode == 5)
                        {
                            px += rnd.NextSigned() * pitch * jitter * 0.5;
                            py += rnd.NextSigned() * pitch * jitter * 0.5;
                        }
                        outp.Add(gp.PointAt(px, py));
                        idx.Add(i + ring);
                    }
                }
                return;
            }

            int i0 = (int)Math.Floor((x0 - cx) / pitch) - 1, i1 = (int)Math.Ceiling((x1 - cx) / pitch) + 1;
            double rowH = mode == 2 ? pitch * 0.8660254037844386 : pitch;
            int jj0 = (int)Math.Floor((y0 - cy) / rowH) - 1, jj1 = (int)Math.Ceiling((y1 - cy) / rowH) + 1;
            for (int j = jj0; j <= jj1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double px = cx + i * pitch, py = cy + j * rowH;
                    if ((mode == 1 || mode == 2) && (j & 1) != 0) px += pitch * 0.5;
                    if (mode == 5)
                    {
                        px += rnd.NextSigned() * pitch * jitter * 0.5;
                        py += rnd.NextSigned() * pitch * jitter * 0.5;
                    }
                    outp.Add(gp.PointAt(px, py));
                    idx.Add(i + j);
                }
        }

        static long Key(int x, int y, int z)
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }

        /// <summary>「算边界」的边：自由边 + 折痕边（两侧夹角 > 35°）。顺接接缝不算。</summary>
        static List<Curve> BorderEdges(Brep brep)
        {
            var list = new List<Curve>();
            try
            {
                for (int ei = 0; ei < brep.Edges.Count; ei++)
                {
                    BrepEdge e = brep.Edges[ei];
                    int[] af = e.AdjacentFaces();
                    bool border;
                    if (af == null || af.Length < 2) border = true;
                    else if (af[0] == af[1]) border = false;
                    else border = Dihedral(brep, e) > 35.0;
                    if (!border) continue;
                    Curve c = e.DuplicateCurve();
                    if (c != null) list.Add(c);
                }
            }
            catch { }
            return list;
        }

        static double Dihedral(Brep brep, BrepEdge e)
        {
            try
            {
                int[] af = e.AdjacentFaces();
                if (af == null || af.Length < 2 || af[0] == af[1]) return 0;
                Point3d mid = e.PointAt(e.Domain.Mid);
                double u, v;
                Vector3d n0 = Vector3d.Zero, n1 = Vector3d.Zero;
                if (brep.Faces[af[0]].ClosestPoint(mid, out u, out v)) n0 = brep.Faces[af[0]].NormalAt(u, v);
                if (brep.Faces[af[1]].ClosestPoint(mid, out u, out v)) n1 = brep.Faces[af[1]].NormalAt(u, v);
                if (!n0.IsValid || !n1.IsValid || !n0.Unitize() || !n1.Unitize()) return 0;
                double d = n0 * n1;
                if (d > 1) d = 1; else if (d < -1) d = -1;
                return Math.Acos(d) * 180.0 / Math.PI;
            }
            catch { return 0; }
        }

        static double DistanceToCurves(List<Curve> curves, Point3d p, double maxR)
        {
            double best = double.MaxValue;
            for (int i = 0; i < curves.Count; i++)
            {
                try
                {
                    double t;
                    if (curves[i].ClosestPoint(p, out t))
                    {
                        double d = curves[i].PointAt(t).DistanceTo(p);
                        if (d < best) best = d;
                        if (best <= maxR) return best;
                    }
                }
                catch { }
            }
            return best;
        }

        /// <summary>在面上的某点、按该处切平面做一个图形（圆/方/三角/六边，朝向按阵列序号交替）</summary>
        static Curve ShapeOnFace(BrepFace face, Point3d p, Vector3d n, double r, int shape, int i, int j, double rot)
        {
            if (!(r > 1e-9)) return null;
            try
            {
                Vector3d tx = Vector3d.Zero;
                double u, v;
                if (face.ClosestPoint(p, out u, out v))
                {
                    double hu = Math.Max((face.Domain(0).Max - face.Domain(0).Min) * 1e-4, 1e-6);
                    Vector3d du = face.PointAt(u + hu, v) - p;
                    if (du.IsValid && du.Unitize()) tx = du;
                }
                tx = tx - n * (tx * n);
                if (!tx.IsValid || !tx.Unitize()) tx = Perp(n);
                Vector3d ty = Vector3d.CrossProduct(n, tx);
                if (!ty.IsValid || !ty.Unitize()) return null;
                var pl = new Plane(p, tx, ty);

                int sh = shape;
                if (sh == 0)
                {
                    try { return new ArcCurve(new Circle(pl, p, r)); } catch { return null; }
                }
                int sides = sh == 1 ? 3 : (sh == 2 ? 4 : 6);
                double a0 = rot + (((i + j) & 1) == 0 ? 0 : Math.PI / sides);
                var pts = new List<Point3d>(sides + 1);
                for (int k = 0; k < sides; k++)
                {
                    double a = a0 + 2.0 * Math.PI * k / sides;
                    pts.Add(pl.PointAt(r * Math.Cos(a), r * Math.Sin(a)));
                }
                pts.Add(pts[0]);
                return new PolylineCurve(pts);
            }
            catch { return null; }
        }

        static Vector3d Perp(Vector3d n)
        {
            var gx = new Vector3d(1, 0, 0);
            var p = gx - n * (gx * n);
            if (p.Length < 1e-6) { var gz = new Vector3d(0, 0, 1); p = gz - n * (gz * n); }
            if (!p.Unitize()) p = new Vector3d(0, 1, 0);
            return p;
        }
    }

    /// <summary>xorshift32：自带随机数（保证 Rhino 7/8 同 seed 一致）</summary>
    internal class Rng32
    {
        uint _s;
        public Rng32(int seed)
        {
            unchecked { _s = (uint)seed * 2654435761u + 1013904223u; }
            if (_s == 0) _s = 0x9E3779B9;
        }
        public double Next()
        {
            unchecked
            {
                _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
                return (_s & 0xFFFFFF) / (double)0x1000000;
            }
        }
        public double NextSigned() { return Next() * 2.0 - 1.0; }
    }
}
