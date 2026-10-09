using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace DiamondFacetPattern
{
    /// <summary>钻石切面参数（面板单位是 mm，MmToModel 换算到文档单位）</summary>
    public class DiamondFacetSettings
    {
        public double FacetSize = 10.0;      // 面片大小（目标边长）
        public double ReliefHeight = 1.5;    // 起伏高度（顶点沿法向随机 ±该值）
        public int Relax = 0;                // 松弛次数（0 = 最随机；越大面片大小越均匀）
        public int Seed = 1;                 // 随机种子
        public bool LockBoundary = true;     // 固定边界：边界上的点锁在边界上（高度 0）
        public int OutputMode = 1;           // 0 = 仅线框；1 = 线框 + 每个三角面独立的 NURBS 平面
        public bool Smooth = false;          // 勾选 = 每个三角切面细分出一张网格片（输出网格）；不勾 = 只输出原平面
        public double SmoothAdaptive = 95.0; // 平滑度 %（80~100）= 细分网格边长的均匀度：100% 各切面边长一致，越低大面越粗、小面越细
        public int SmoothQuadCount = 5000;   // 细分程度 = 细分后的目标总面数
        public double MmToModel = 1.0;

        public DiamondFacetSettings Clone() { return (DiamondFacetSettings)MemberwiseClone(); }

        public double FacetSizeModel { get { return FacetSize * MmToModel; } }
        public double ReliefModel { get { return ReliefHeight * MmToModel; } }
    }

    /// <summary>一个边界生成出来的一套切面</summary>
    public class DiamondFacetResult
    {
        public List<Curve> Wireframe = new List<Curve>();
        public List<Brep> Faces = new List<Brep>();
        public Mesh Shaded;                  // 带凹凸的三角网格（预览用）
        public int Triangles;
        public int Points;
        public int BoundaryPoints;
        public int FaceSkipped;
        public bool Smoothed;                // 是否真的做出了细分网格（每个切面一张网格片）
        public int SmoothFaces;              // 细分网格的总面数
        public int SmoothPatches;            // 网格片数（= 参与细分的三角切面数）
        public List<int> SmoothGrids;        // 每张网格片的细分档位（n×n 张面），顺序同切面
        public Mesh SmoothMesh;              // 细分网格（输出用）
        public double BoundaryArea;
        public double FacetArea;
        public double MinEdge;
        public double MaxEdge;
        public double Seconds;
        public string BoundaryDesc = "";
        public string Note = "";
        public string Error = "";            // 非空 = 这个边界失败
    }

    /// <summary>解析出来的平面边界</summary>
    internal class FacetBoundary
    {
        public Plane Plane;
        public Curve Outline;                // 平面闭合曲线（世界坐标）
        public double Area;
        public int InnerLoops;
        public string Desc = "";
        public Guid SourceId = Guid.Empty;
    }

    internal class Rng
    {
        ulong _s;
        public Rng(int seed)
        {
            _s = (ulong)(uint)seed * 6364136223846793005UL + 1442695040888963407UL;
            if (_s == 0) _s = 0x9E3779B97F4A7C15UL;
        }
        public double Next01()
        {
            _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
            return (_s >> 11) * (1.0 / 9007199254740992.0);
        }
        public double NextRange(double a, double b) { return a + (b - a) * Next01(); }
    }

    public static class DiamondFacetCore
    {
        internal const string EmptyTargetHint = "未选择边界 —— 点「选择边界」按钮选平面或闭合曲线";
        internal const int MaxFaces = 20000;      // 面片数上限（超过只出线框，如实报告）
        internal const double MinFacetSize = 1e-6;
        internal const double MaxInteriorCells = 8000;   // 内部随机点上限（面片太小自动放大）
        /// <summary>自检用：强制走自实现 Delaunay 兜底路径</summary>
        internal static bool ForceFallbackTessellation = false;
        internal const string LayerWire = "钻石切面-线框";
        internal const string LayerFace = "钻石切面-面";
        internal const string LayerSmooth = "钻石切面-平滑";

        // ============================================================ 边界解析

        /// <summary>把一个物件解析成平面边界：平面曲面/平面多重曲面/闭合平面曲线</summary>
        internal static bool TryResolve(RhinoObject obj, out FacetBoundary b, out string why)
        {
            b = null; why = "";
            if (obj == null) { why = "对象不存在"; return false; }

            double tol = 1e-6;
            try { if (RhinoDoc.ActiveDoc != null) tol = Math.Max(1e-9, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance); }
            catch { }

            Curve outline = null;
            Plane plane = Plane.Unset;
            int inner = 0;

            var curve = obj.Geometry as Curve;
            if (curve != null)
            {
                if (!curve.IsClosed) { why = "曲线不闭合"; return false; }
                if (!curve.IsPlanar(tol * 10.0)) { why = "曲线不在同一平面上"; return false; }
                if (!curve.TryGetPlane(out plane)) { why = "曲线无法拟合出平面"; return false; }
                outline = curve.DuplicateCurve();
            }
            else
            {
                var brep = obj.Geometry as Brep;
                if (brep == null)
                {
                    // 挤出物件（Extrusion）/ 曲面（Surface）等不是 Brep 类型，但能转成 Brep —— 用 TryConvertBrep 收进来
                    // （踩过：挤出物件被 `as Brep` 判成「不是曲线或曲面」，用户选平面挤出体时直接失败）
                    try { brep = Brep.TryConvertBrep(obj.Geometry); } catch { brep = null; }
                }
                if (brep == null)
                {
                    string kind = obj.Geometry == null ? "空几何" : obj.Geometry.GetType().Name;
                    why = "不是曲线或曲面（" + kind + " 不支持：网格/细分物件请先转成 NURBS 曲面，或直接选它的平面边界曲线）";
                    return false;
                }
                if (brep.Faces.Count == 0) { why = "曲面没有面"; return false; }

                BrepFace best = null;
                double bestArea = -1.0;
                for (int i = 0; i < brep.Faces.Count; i++)
                {
                    BrepFace f = brep.Faces[i];
                    if (!f.IsPlanar(tol * 10.0)) continue;
                    double a = FaceArea(f);
                    if (a > bestArea) { bestArea = a; best = f; }
                }
                if (best == null) { why = "没有平面面（弯曲面不支持）"; return false; }
                if (!best.TryGetPlane(out plane)) { why = "平面面无法取出平面"; return false; }
                outline = best.OuterLoop != null ? best.OuterLoop.To3dCurve() : null;
                if (outline == null) { why = "取不到外轮廓"; return false; }
                if (!outline.IsClosed) { why = "外轮廓不闭合"; return false; }
                inner = Math.Max(0, best.Loops.Count - 1);
            }
            if (outline == null) { why = "取不到边界曲线"; return false; }

            double dev = MaxPlaneDeviation(outline, plane);
            double diag = CurveDiag(outline, plane);
            if (dev > Math.Max(1e-7, diag * 1e-4))
            {
                why = string.Format(CultureInfo.InvariantCulture, "轮廓偏离平面 {0:0.###e+0}（不是平面边界）", dev);
                return false;
            }

            double area = CurveArea(outline);
            if (!(area > 0)) { why = "边界面积为零"; return false; }

            b = new FacetBoundary
            {
                Plane = plane,
                Outline = outline,
                Area = area,
                InnerLoops = inner,
                SourceId = obj.Id,
                Desc = Describe(obj)
            };
            return true;
        }

        static double FaceArea(BrepFace f)
        {
            try
            {
                Brep one = f.DuplicateFace(false);
                if (one == null) return 0.0;
                AreaMassProperties amp = AreaMassProperties.Compute(one);
                return amp != null ? amp.Area : 0.0;
            }
            catch { return 0.0; }
        }

        internal static double CurveArea(Curve c)
        {
            try
            {
                AreaMassProperties amp = AreaMassProperties.Compute(c);
                return amp != null ? Math.Abs(amp.Area) : 0.0;
            }
            catch { return 0.0; }
        }

        static double MaxPlaneDeviation(Curve outline, Plane plane)
        {
            double max = 0;
            try
            {
                Point3d[] pts;
                if (outline.DivideByCount(32, true, out pts) == null || pts == null) return 0;
                for (int i = 0; i < pts.Length; i++)
                {
                    double d = Math.Abs(plane.DistanceTo(pts[i]));
                    if (d > max) max = d;
                }
            }
            catch { }
            return max;
        }

        static double CurveDiag(Curve outline, Plane plane)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            try
            {
                Point3d[] pts;
                if (outline.DivideByCount(32, true, out pts) == null || pts == null) return 1.0;
                for (int i = 0; i < pts.Length; i++)
                {
                    Point2d q = To2d(plane, pts[i]);
                    if (q.X < x0) x0 = q.X; if (q.X > x1) x1 = q.X;
                    if (q.Y < y0) y0 = q.Y; if (q.Y > y1) y1 = q.Y;
                }
            }
            catch { return 1.0; }
            double dx = x1 - x0, dy = y1 - y0;
            double d = Math.Sqrt(dx * dx + dy * dy);
            return d > 0 ? d : 1.0;
        }

        internal static string Describe(RhinoObject obj)
        {
            if (obj == null) return "未选择";
            string name = obj.Name;
            string id = obj.Id.ToString();
            if (id.Length > 8) id = id.Substring(0, 8);
            string kind = obj.Geometry is Curve ? "曲线" : "曲面";
            return string.IsNullOrEmpty(name) ? (kind + " " + id) : (kind + "：" + name);
        }

        internal static int EnsureLayer(RhinoDoc doc, string name, Color col)
        {
            foreach (Layer l in doc.Layers)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
            var nl = new Layer();
            nl.Name = name;
            nl.Color = col;
            return doc.Layers.Add(nl);
        }

        /// <summary>
        /// 把结果写进文档。**输出二选一，不混着给**：
        ///   仅线框 → 只写曲线（图层「钻石切面-线框」）；
        ///   面 → 勾了「一键平滑」就只写细分网格片（图层「钻石切面-平滑」，网格本身就是切面形状，不再叠平面），
        ///        不勾就只写原本的平面（图层「钻石切面-面」）；两种都不带线框。
        /// </summary>
        internal static void AddToDocument(RhinoDoc doc, List<DiamondFacetResult> results, int outputMode,
            out int curveCount, out int faceCount, out int smoothCount)
        {
            curveCount = 0; faceCount = 0; smoothCount = 0;
            int lw = EnsureLayer(doc, LayerWire, Color.FromArgb(96, 108, 132));
            int lf = EnsureLayer(doc, LayerFace, Color.FromArgb(122, 92, 214));
            int ls = EnsureLayer(doc, LayerSmooth, Color.FromArgb(64, 148, 208));
            var aw = new ObjectAttributes(); aw.LayerIndex = lw;
            var af = new ObjectAttributes(); af.LayerIndex = lf;
            var asm = new ObjectAttributes(); asm.LayerIndex = ls;
            for (int i = 0; i < results.Count; i++)
            {
                DiamondFacetResult r = results[i];
                if (outputMode == 0)
                {
                    for (int k = 0; k < r.Wireframe.Count; k++)
                    {
                        if (r.Wireframe[k] == null) continue;
                        if (doc.Objects.AddCurve(r.Wireframe[k], aw) != Guid.Empty) curveCount++;
                    }
                    continue;
                }
                if (r.SmoothMesh != null && r.SmoothMesh.Faces.Count > 0)
                {
                    if (doc.Objects.AddMesh(r.SmoothMesh, asm) != Guid.Empty) smoothCount++;
                    continue;
                }
                for (int k = 0; k < r.Faces.Count; k++)
                {
                    if (r.Faces[k] == null) continue;
                    if (doc.Objects.AddBrep(r.Faces[k], af) != Guid.Empty) faceCount++;
                }
            }
        }

        // ============================================================ 2D 工具

        internal static Point2d To2d(Plane pl, Point3d p)
        {
            Vector3d d = p - pl.Origin;
            return new Point2d(Vector3d.Multiply(d, pl.XAxis), Vector3d.Multiply(d, pl.YAxis));
        }

        internal static Point3d To3d(Plane pl, Point2d q) { return pl.PointAt(q.X, q.Y); }

        internal static double Diag(List<Point2d> poly)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d p = poly[i];
                if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
                if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y;
            }
            double dx = x1 - x0, dy = y1 - y0;
            double d = Math.Sqrt(dx * dx + dy * dy);
            return d > 0 ? d : 1.0;
        }

        /// <summary>射线法：点是否在多边形内（边界上算内）</summary>
        internal static bool Inside(List<Point2d> poly, Point2d p)
        {
            bool inx = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Point2d a = poly[i], b = poly[j];
                if (((a.Y > p.Y) != (b.Y > p.Y)) &&
                    (p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X))
                    inx = !inx;
            }
            return inx;
        }

        /// <summary>点到多边形边界的最短距离</summary>
        internal static double DistToPoly(List<Point2d> poly, Point2d p)
        {
            double best = double.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                best = Math.Min(best, DistSeg(p, poly[j], poly[i]));
            return best;
        }

        static double DistSeg(Point2d p, Point2d a, Point2d b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double l2 = dx * dx + dy * dy;
            if (l2 < 1e-18) return p.DistanceTo(a);
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double ex = p.X - (a.X + t * dx), ey = p.Y - (a.Y + t * dy);
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>
        /// 沿轮廓按目标间距取点（闭合，末点不与首点重复）。
        /// **拐角必须进环**：纯按弧长等分会把矩形的 4 个角切掉（用户实测「边界有一部分是空的」就是这个）——
        /// 所以候选 = 等分点 + 拐角（折线顶点 / G1 不连续点），按**曲线参数**排序（参数沿曲线单调，
        /// 不能用 LengthParameter —— 它是「弧长→参数」，用反了会把拐角排到错位置、环多边形自交），
        /// 再去重：拐角必留，离已留点太近的等分点丢掉。
        /// </summary>
        internal static List<Point3d> SampleBoundary(Curve outline, double spacing)
        {
            var outp = new List<Point3d>();
            double len = outline.GetLength();
            if (!(len > 0)) return outp;
            double step = Math.Max(MinFacetSize, spacing);
            double near = step * 0.5;                       // 两个环点靠得比这还近就丢掉一个

            var ts = new List<double>();
            var ps = new List<Point3d>();
            var isCorner = new List<bool>();

            // 1) 弧长等分点（DivideByCount 同时给出参数与点）
            int n = (int)Math.Round(len / step);
            if (n < 3) n = 3;
            if (n > 4000) n = 4000;
            Point3d[] div;
            double[] divT = outline.DivideByCount(n, true, out div);
            if (divT == null || div == null || div.Length < 3) return outp;
            for (int i = 0; i < div.Length; i++) { ts.Add(divT[i]); ps.Add(div[i]); isCorner.Add(false); }

            // 2) 拐角（点用折线顶点原值，参数用 ClosestPoint 反查）
            var pl = (Polyline)null;
            if (outline.TryGetPolyline(out pl) && pl.Count >= 4)
            {
                for (int i = 0; i < pl.Count - 1; i++) AddCorner(outline, pl[i], ts, ps, isCorner);
            }
            else
            {
                try
                {
                    double t = outline.Domain.Min, tEnd = outline.Domain.Max;
                    double k;
                    int guard = 0;
                    while (guard++ < 512 && outline.GetNextDiscontinuity(Continuity.G1_continuous, t, tEnd, out k))
                    {
                        Point3d pk = outline.PointAt(k);
                        if (pk.IsValid) { ts.Add(k); ps.Add(pk); isCorner.Add(true); }
                        if (k >= tEnd - 1e-12) break;
                        t = k + (tEnd - t) * 1e-9;
                    }
                }
                catch { }
            }

            // 3) 按参数排序（沿曲线顺序）
            var order = new List<int>();
            for (int i = 0; i < ts.Count; i++) order.Add(i);
            order.Sort((a, b2) => ts[a].CompareTo(ts[b2]));

            // 4) 走一遍：拐角必留（上一个点若是离它太近的等分点就顶掉它）
            var keptP = new List<Point3d>();
            var keptCorner = new List<bool>();
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                Point3d p = ps[i];
                if (!p.IsValid) continue;
                if (isCorner[i])
                {
                    if (keptP.Count > 0 && !keptCorner[keptCorner.Count - 1] &&
                        keptP[keptP.Count - 1].DistanceTo(p) < near)
                    {
                        keptP.RemoveAt(keptP.Count - 1);
                        keptCorner.RemoveAt(keptCorner.Count - 1);
                    }
                    if (keptP.Count > 0 && keptP[keptP.Count - 1].DistanceTo(p) < 1e-9) continue;
                    keptP.Add(p); keptCorner.Add(true);
                }
                else
                {
                    if (keptP.Count > 0 && keptP[keptP.Count - 1].DistanceTo(p) < near) continue;
                    keptP.Add(p); keptCorner.Add(false);
                }
            }

            // 5) 闭合：末点与首点重合就丢掉末点
            if (keptP.Count > 1 && keptP[0].DistanceTo(keptP[keptP.Count - 1]) < Math.Max(1e-9, len * 1e-6))
                keptP.RemoveAt(keptP.Count - 1);
            return keptP;
        }

        static void AddCorner(Curve outline, Point3d p, List<double> ts, List<Point3d> ps, List<bool> isCorner)
        {
            try
            {
                double t;
                if (!outline.ClosestPoint(p, out t)) return;
                ts.Add(t); ps.Add(p); isCorner.Add(true);
            }
            catch { }
        }

        /// <summary>内部随机点：抖动网格（网格边长 = 面片大小）</summary>
        internal static List<Point2d> MakeInteriorPoints(List<Point2d> region, double spacing, int seed,
            out int rejected, out bool autoScaled)
        {
            rejected = 0;
            autoScaled = false;
            var outp = new List<Point2d>();
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            for (int i = 0; i < region.Count; i++)
            {
                Point2d p = region[i];
                if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
                if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y;
            }
            var rng = new Rng(seed);
            double step = Math.Max(MinFacetSize, spacing);
            // 面片大小给得太小时点数会爆掉（内存/时间）：自动放大到点数上限，并如实报告
            double cells = ((x1 - x0) / step) * ((y1 - y0) / step);
            if (cells > MaxInteriorCells)
            {
                step *= Math.Sqrt(cells / MaxInteriorCells);
                autoScaled = true;
            }
            double clear = step * 0.45;                 // 离边界太近的点丢掉，避免贴边碎面
            for (double y = y0 + step * 0.5; y < y1; y += step)
                for (double x = x0 + step * 0.5; x < x1; x += step)
                {
                    var q = new Point2d(x + rng.NextRange(-0.5, 0.5) * step, y + rng.NextRange(-0.5, 0.5) * step);
                    if (!Inside(region, q)) { rejected++; continue; }
                    if (DistToPoly(region, q) < clear) { rejected++; continue; }
                    outp.Add(q);
                }
            return outp;
        }

        // ============================================================ 剖分

        /// <summary>
        /// 三角剖分：先用 Rhino 原生 Delaunay（得到凸包剖分），再用「重心是否在边界内」剔掉边界外的三角面。
        ///
        /// 实测结论（Rhino 7.0.20314 / 8.30.26103 都一样）：Mesh.CreateFromTessellation 的
        /// outlines 参数**不生效** —— 只要传了非空轮廓环，返回的网格就是「有顶点、0 个面」；
        /// 不传轮廓时给出的凸包 Delaunay 是准的（100×100 点集 → 面积正好 10000）。
        /// 所以轮廓点也放进点集，靠剔面把边界形状做出来。
        ///
        /// 原生不可用时回退自实现 Delaunay（同样剔面），并且做面积可信度检查：对不上就不给几何。
        /// </summary>
        internal static Mesh Tessellate(Plane plane, List<Point2d> interior, List<Point3d> ring3,
            List<Point2d> region, out string how, out string why)
        {
            how = "原生"; why = "";
            var all3 = new List<Point3d>(ring3.Count + interior.Count);
            for (int i = 0; i < ring3.Count; i++) all3.Add(ring3[i]);
            for (int i = 0; i < interior.Count; i++) all3.Add(To3d(plane, interior[i]));

            if (!ForceFallbackTessellation)
            {
                try
                {
                    Mesh m = Mesh.CreateFromTessellation(all3, new List<IEnumerable<Point3d>>(), plane, false);
                    if (m != null && m.Faces.Count > 0)
                    {
                        Prepare(m);
                        Mesh culled = CullOutside(m, region, plane);
                        if (culled != null) return culled;
                        why = "原生剖分的三角面全部落在边界外";
                    }
                    else why = "Rhino 原生剖分返回空";
                }
                catch (Exception ex) { why = "原生剖分异常：" + ex.Message; }
            }

            how = "自实现";
            var all = new List<Point2d>(all3.Count);
            for (int i = 0; i < all3.Count; i++) all.Add(To2d(plane, all3[i]));
            List<int[]> tris = Delaunay(all);
            if (tris.Count == 0) { why += "；自实现 Delaunay 没有三角面"; return null; }
            var mm = new Mesh();
            for (int i = 0; i < all.Count; i++) mm.Vertices.Add(To3d(plane, all[i]));
            for (int i = 0; i < tris.Count; i++) mm.Faces.AddFace(tris[i][0], tris[i][1], tris[i][2]);
            mm.FaceNormals.ComputeFaceNormals();
            Mesh culled2 = CullOutside(mm, region, plane);
            if (culled2 == null) { why += "；自实现剖分没有三角面落在边界内"; return null; }

            // 可信度检查：兜底剖分出来的面积必须对得上边界面积
            // （圆周那种「一堆点共圆」的退化点集，兜底剖分可能给出互相重叠的面 —— 宁可明确拒绝，也不给错几何）
            double polyArea = Math.Abs(PolyArea(region));
            double triArea = MeshArea(culled2);
            if (polyArea > 0 && Math.Abs(triArea - polyArea) / polyArea > 0.05)
            {
                why += string.Format(CultureInfo.InvariantCulture, "；自实现剖分结果不可信（面积 {0:0.#} vs 边界 {1:0.#}，差 {2:0.0%}），已拒绝", triArea, polyArea, Math.Abs(triArea - polyArea) / polyArea);
                return null;
            }
            return culled2;
        }

        static void Prepare(Mesh m)
        {
            try { if (m.Ngons.Count > 0) m.Ngons.Clear(); } catch { }
            m.Faces.ConvertQuadsToTriangles();
            m.Vertices.CombineIdentical(true, true);
            m.Vertices.CullUnused();
            m.FaceNormals.ComputeFaceNormals();
        }

        /// <summary>剔掉重心落在边界外的三角面（凸包多出来的部分）；正好落在边界线上的也算保留</summary>
        static Mesh CullOutside(Mesh m, List<Point2d> region, Plane plane)
        {
            double eps = Math.Max(1e-9, Diag(region) * 1e-9);
            var outp = new Mesh();
            for (int i = 0; i < m.Vertices.Count; i++) outp.Vertices.Add(m.Vertices.Point3dAt(i));
            for (int i = 0; i < m.Faces.Count; i++)
            {
                MeshFace f = m.Faces.GetFace(i);
                if (!f.IsTriangle) continue;
                Point3d cen = m.Faces.GetFaceCenter(i);
                Point2d c = To2d(plane, cen);
                if (!Inside(region, c) && DistToPoly(region, c) > eps) continue;
                outp.Faces.AddFace(f.A, f.B, f.C);
            }
            if (outp.Faces.Count == 0) return null;
            outp.Vertices.CullUnused();
            outp.FaceNormals.ComputeFaceNormals();
            return outp;
        }

        internal static double MeshArea(Mesh m)
        {
            double sum = 0;
            if (m == null) return 0;
            for (int i = 0; i < m.Faces.Count; i++)
            {
                MeshFace f = m.Faces.GetFace(i);
                Point3d p0 = m.Vertices.Point3dAt(f.A);
                Point3d p1 = m.Vertices.Point3dAt(f.B);
                Point3d p2 = m.Vertices.Point3dAt(f.C);
                sum += TriangleArea(p0, p1, p2);
            }
            return sum;
        }

        internal static double PolyArea(List<Point2d> poly)
        {
            double a2 = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a = poly[i], b = poly[(i + 1) % poly.Count];
                a2 += a.X * b.Y - b.X * a.Y;
            }
            return a2 * 0.5;
        }

        /// <summary>
        /// 自实现 Delaunay（Bowyer–Watson，兜底用）。
        /// 两点加固：① 插入顺序确定性打乱 —— 顺序插入会在网格点上出退化连锁；
        /// ② 外接圆判定带相对容差 —— 圆周上那种「一堆点共圆」的情形按圆外处理，
        ///   否则会剔出一片不连通的三角形、再补出来的面互相重叠（实测圆边界会炸成 70 倍面积）。
        /// </summary>
        internal static List<int[]> Delaunay(List<Point2d> pts)
        {
            var outp = new List<int[]>();
            int n = pts.Count;
            if (n < 3) return outp;

            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (pts[i].X < x0) x0 = pts[i].X; if (pts[i].X > x1) x1 = pts[i].X;
                if (pts[i].Y < y0) y0 = pts[i].Y; if (pts[i].Y > y1) y1 = pts[i].Y;
            }
            double dx = Math.Max(1e-9, x1 - x0), dy = Math.Max(1e-9, y1 - y0);
            double d = Math.Max(dx, dy) * 8.0 + 1.0;
            var p = new List<Point2d>(pts);
            p.Add(new Point2d(x0 - d, y0 - d));
            p.Add(new Point2d(x0 + dx * 0.5, y1 + d));
            p.Add(new Point2d(x1 + d, y0 - d));

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var rng = new Rng(0x5EED);
            for (int i = n - 1; i > 0; i--)
            {
                int j = (int)(rng.Next01() * (i + 1));
                if (j < 0) j = 0; if (j > i) j = i;
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            var tris = new List<int[]> { new int[] { n, n + 1, n + 2 } };
            var bad = new List<int>();
            var edges = new List<int[]>();
            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                bad.Clear();
                for (int t = 0; t < tris.Count; t++)
                {
                    int[] tr = tris[t];
                    if (InCircle(p[tr[0]], p[tr[1]], p[tr[2]], p[i])) bad.Add(t);
                }
                edges.Clear();
                for (int k = 0; k < bad.Count; k++)
                {
                    int[] t = tris[bad[k]];
                    for (int e = 0; e < 3; e++)
                    {
                        int a = t[e], b = t[(e + 1) % 3];
                        bool shared = false;
                        for (int k2 = 0; k2 < bad.Count && !shared; k2++)
                        {
                            if (k2 == k) continue;
                            int[] u = tris[bad[k2]];
                            for (int e2 = 0; e2 < 3; e2++)
                                if (u[e2] == b && u[(e2 + 1) % 3] == a) { shared = true; break; }
                        }
                        if (!shared) edges.Add(new int[] { a, b });
                    }
                }
                for (int k = bad.Count - 1; k >= 0; k--) tris.RemoveAt(bad[k]);
                for (int k = 0; k < edges.Count; k++) tris.Add(new int[] { edges[k][0], edges[k][1], i });
            }
            for (int t = 0; t < tris.Count; t++)
            {
                int[] tr = tris[t];
                if (tr[0] >= n || tr[1] >= n || tr[2] >= n) continue;
                outp.Add(tr);
            }
            return outp;
        }

        static bool InCircle(Point2d a, Point2d b, Point2d c, Point2d d)
        {
            double ax = a.X - d.X, ay = a.Y - d.Y;
            double bx = b.X - d.X, by = b.Y - d.Y;
            double cx = c.X - d.X, cy = c.Y - d.Y;
            double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            double t1 = a2 * (bx * cy - cx * by);
            double t2 = b2 * (ax * cy - cx * ay);
            double t3 = c2 * (ax * by - bx * ay);
            double det = t1 - t2 + t3;
            double scale = Math.Abs(t1) + Math.Abs(t2) + Math.Abs(t3);
            if (scale > 0 && Math.Abs(det) <= scale * 1e-12) return false;   // 共圆/退化：当圆外
            double o = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            return o > 0 ? det > 0 : det < 0;
        }

        // ============================================================ 高度

        static double Hash01(int seed, long a, long b)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                h = (h ^ (ulong)(uint)seed) * 1099511628211UL;
                h = (h ^ (ulong)a) * 1099511628211UL;
                h = (h ^ (ulong)b) * 1099511628211UL;
                h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33;
                h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
                return (h >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        static long Q(double v) { return (long)Math.Round(v * 1e6); }

        /// <summary>顶点高度：按平面坐标哈希（同一位置永远同一高度 → 共享顶点不裂开）</summary>
        internal static double HeightAt(int seed, double x, double y, double relief)
        {
            if (relief <= 0) return 0.0;
            return (Hash01(seed, Q(x), Q(y)) * 2.0 - 1.0) * relief;
        }

        // ============================================================ 生成

        internal static DiamondFacetResult GenerateOne(FacetBoundary b, DiamondFacetSettings s)
        {
            var res = new DiamondFacetResult();
            var clock = Stopwatch.StartNew();
            if (b == null) { res.Error = "没有边界"; return res; }
            res.BoundaryDesc = b.Desc;

            double spacing = Math.Max(MinFacetSize, s.FacetSizeModel);
            double relief = Math.Max(0.0, s.ReliefModel);
            Plane plane = b.Plane;

            // 1) 边界采样 → 面片区域多边形（面片外轮廓就是它）
            List<Point3d> ring3 = SampleBoundary(b.Outline, spacing);
            if (ring3.Count < 3) { res.Error = "边界太短，取不到 3 个点"; return res; }
            var region = new List<Point2d>(ring3.Count);
            for (int i = 0; i < ring3.Count; i++) region.Add(To2d(plane, ring3[i]));
            double eps = Math.Max(1e-9, spacing * 1e-6);

            int rejected;
            bool autoScaled;
            List<Point2d> inner = MakeInteriorPoints(region, spacing, s.Seed, out rejected, out autoScaled);
            res.BoundaryPoints = ring3.Count;

            // 2) 剖分（松弛时反复重剖）
            string how, why;
            Mesh mesh = Tessellate(plane, inner, ring3, region, out how, out why);
            if (mesh == null) { res.Error = "三角剖分失败：" + why; return res; }

            for (int it = 0; it < Math.Max(0, s.Relax); it++)
            {
                List<Point2d> moved = RelaxOnce(inner, region, spacing);
                if (moved == null) break;
                inner = moved;
                Mesh again = Tessellate(plane, inner, ring3, region, out how, out why);
                if (again == null) break;
                mesh = again;
            }

            // 3) 顶点沿法向抬高低（边界上的点：固定边界 = 锁在边界上）
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Point3d p = mesh.Vertices.Point3dAt(i);
                Point2d q = To2d(plane, p);
                bool onBoundary = DistToPoly(region, q) <= eps;
                double h = (onBoundary && s.LockBoundary) ? 0.0 : HeightAt(s.Seed, q.X, q.Y, relief);
                mesh.Vertices.SetVertex(i, plane.PointAt(q.X, q.Y) + plane.ZAxis * h);
            }

            // 3b) 顶点贴住边界：外轮廓（裸边）顶点 + 贴边顶点精确吸附到边界曲线上（高度规则不变）
            int snapped; double snapMax;
            SnapToBoundary(mesh, b.Outline, plane, s.LockBoundary, s.Seed, relief, region, spacing,
                out snapped, out snapMax);
            mesh.FaceNormals.ComputeFaceNormals();

            // 4) 线框：三角网格的全部边（含边界）
            var topo = mesh.TopologyEdges;
            for (int i = 0; i < topo.Count; i++)
            {
                Line ln = topo.EdgeLine(i);
                if (!ln.IsValid) continue;
                res.Wireframe.Add(new LineCurve(ln));
            }

            // 5) 面：每个三角面一张独立的 NURBS 平面
            res.Triangles = mesh.Faces.Count;
            res.Points = mesh.Vertices.Count;
            if (res.Triangles == 0) { res.Error = "剖分结果没有三角面"; return res; }

            double sum = 0, minE = double.MaxValue, maxE = 0;
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                MeshFace f = mesh.Faces.GetFace(i);
                Point3d p0 = mesh.Vertices.Point3dAt(f.A);
                Point3d p1 = mesh.Vertices.Point3dAt(f.B);
                Point3d p2 = mesh.Vertices.Point3dAt(f.C);
                double e0 = p0.DistanceTo(p1), e1 = p1.DistanceTo(p2), e2 = p2.DistanceTo(p0);
                sum += TriangleArea(p0, p1, p2);
                minE = Math.Min(minE, Math.Min(e0, Math.Min(e1, e2)));
                maxE = Math.Max(maxE, Math.Max(e0, Math.Max(e1, e2)));

                if (s.OutputMode != 1 || i >= MaxFaces) continue;
                Vector3d n = Vector3d.CrossProduct(p1 - p0, p2 - p0);
                if (Vector3d.Multiply(n, plane.ZAxis) < 0) { Point3d tmp = p1; p1 = p2; p2 = tmp; }
                Brep face = null;
                try
                {
                    var tri = new PolylineCurve(new Point3d[] { p0, p1, p2, p0 });
                    face = Brep.CreateTrimmedPlane(new Plane(p0, p1, p2), tri);
                }
                catch { face = null; }
                if (face != null && face.Faces.Count > 0) res.Faces.Add(face);
                else res.FaceSkipped++;
            }

            res.Shaded = mesh;
            res.BoundaryArea = b.Area;
            res.FacetArea = sum;
            res.MinEdge = minE == double.MaxValue ? 0 : minE;
            res.MaxEdge = maxE;
            res.Seconds = clock.Elapsed.TotalSeconds;

            var note = new List<string>();
            if (snapped > 0 && snapMax > spacing * 0.05)
                note.Add(string.Format(CultureInfo.InvariantCulture, "边界吸附：{0} 个外轮廓顶点贴到边界上（最大移动 {1:0.###}）", snapped, snapMax));

            // 6) 勾选「一键平滑」：每个三角切面各自生成一张网格片，再在片内细分到「细分程度」的目标面数
            if (s.OutputMode == 1 && s.Smooth)
                BuildFacetMeshPatches(res, s, note);
            if (s.OutputMode == 1 && res.Triangles > MaxFaces)
                note.Add(string.Format(CultureInfo.InvariantCulture, "三角面 {0} 超过上限 {1}：只输出线框，面未生成", res.Triangles, MaxFaces));
            if (res.FaceSkipped > 0) note.Add(string.Format(CultureInfo.InvariantCulture, "{0} 个三角面封平面失败已跳过", res.FaceSkipped));
            if (b.InnerLoops > 0) note.Add(string.Format(CultureInfo.InvariantCulture, "边界含 {0} 个内孔：按外轮廓生成（内孔不挖）", b.InnerLoops));
            if (autoScaled) note.Add(string.Format(CultureInfo.InvariantCulture, "面片大小太小：点数上限 {0}，网格已自动放大到 {1:0.###}", (int)MaxInteriorCells, Math.Sqrt((b.Area > 0 ? b.Area : 1.0) / MaxInteriorCells)));
            if (rejected > 0) note.Add(string.Format(CultureInfo.InvariantCulture, "{0} 个随机点落在边界外/贴边被丢弃", rejected));
            if (how == "自实现") note.Add("原生剖分不可用，已用自实现 Delaunay 兜底" + (string.IsNullOrEmpty(why) ? "" : "（" + why + "）"));
            res.Note = string.Join("；", note.ToArray());
            return res;
        }

        static double TriangleArea(Point3d a, Point3d b, Point3d c)
        {
            return 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
        }

        /// <summary>
        /// 顶点贴住边界：把「外轮廓（裸边）顶点」和「离边界小于 0.25×面片大小的顶点」精确投到边界曲线上。
        /// 这样面片一定顶到边界（用户实测：不吸附时外圈会内缩、角上是空的）。
        /// 高度规则不变：固定边界 = 0，否则按吸附后的位置取哈希高度。
        /// </summary>
        static void SnapToBoundary(Mesh mesh, Curve outline, Plane plane, bool lockBoundary, int seed, double relief,
            List<Point2d> region, double spacing, out int snapped, out double maxMove)
        {
            snapped = 0; maxMove = 0;
            var targets = new HashSet<int>();

            // ① 裸边（只连一个面的边）的两个顶点 = 网格外轮廓
            try
            {
                var topo = mesh.TopologyEdges;
                for (int i = 0; i < topo.Count; i++)
                {
                    int[] faces = topo.GetConnectedFaces(i);
                    if (faces == null || faces.Length != 1) continue;
                    IndexPair tv = topo.GetTopologyVertices(i);
                    AddMeshVertices(mesh, tv.I, targets);
                    AddMeshVertices(mesh, tv.J, targets);
                }
            }
            catch { }

            // ② 贴边的顶点（保险：万一剖分把轮廓点丢了，外圈也还在 0.25 间距以内）
            double near = spacing * 0.25;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                if (targets.Contains(i)) continue;
                if (DistToPoly(region, To2d(plane, mesh.Vertices.Point3dAt(i))) <= near) targets.Add(i);
            }
            if (targets.Count == 0) return;

            double maxAllowed = Math.Max(spacing * 1.5, 1e-9);
            foreach (int i in targets)
            {
                Point3d p = mesh.Vertices.Point3dAt(i);
                Point2d q = To2d(plane, p);
                Point3d q3 = plane.PointAt(q.X, q.Y);
                double t;
                if (!outline.ClosestPoint(q3, out t)) continue;
                Point3d on = outline.PointAt(t);
                if (!on.IsValid) continue;
                Point2d q2 = To2d(plane, on);
                Point3d np = plane.PointAt(q2.X, q2.Y);
                double move = np.DistanceTo(plane.PointAt(q.X, q.Y));
                if (move > maxAllowed) continue;                       // 异常大的移动不做（防拓扑翻折）
                double h = lockBoundary ? 0.0 : HeightAt(seed, q2.X, q2.Y, relief);
                mesh.Vertices.SetVertex(i, np + plane.ZAxis * h);
                if (move > 1e-9) snapped++;
                if (move > maxMove) maxMove = move;
            }
        }

        static void AddMeshVertices(Mesh mesh, int topologyVertexIndex, HashSet<int> targets)
        {
            try
            {
                int[] mv = mesh.TopologyVertices.MeshVertexIndices(topologyVertexIndex);
                if (mv == null) return;
                for (int k = 0; k < mv.Length; k++) targets.Add(mv[k]);
            }
            catch { }
        }

        /// <summary>内部边两侧面的二面角（度）：0 = 共面，越大越尖</summary>
        internal static double DihedralDeg(Brep brep, BrepEdge edge, int f0, int f1)
        {
            try
            {
                Point3d mid = edge.PointAt(edge.Domain.Mid);
                double u0, v0, u1, v1;
                BrepFace a = brep.Faces[f0], b = brep.Faces[f1];
                if (!a.ClosestPoint(mid, out u0, out v0)) return 0;
                if (!b.ClosestPoint(mid, out u1, out v1)) return 0;
                Vector3d n0 = a.NormalAt(u0, v0), n1 = b.NormalAt(u1, v1);
                if (!n0.IsValid || !n1.IsValid) return 0;
                return Vector3d.VectorAngle(n0, n1) * 180.0 / Math.PI;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 一键平滑（网格输出）：**每个三角切面各自生成一张网格片**，再在片内细分。
        /// 细分点全部由该切面 3 个角点线性插值得到 → 片内保持平面（钻石切面的棱线原样保留）；
        /// 相邻切面共享边上的细分点由同一段端点插值 → 位置完全重合，不裂缝（同坐标顶点按容差合并）。
        /// 细分程度 = 细分后的目标总面数；平滑度（80~100%）→ 网格边长的均匀度：100% 各切面细分档位按面积自动配平（边长一致），
        /// 越低大面越粗、小面越细（省面）。
        /// ⚠ 用户口径：**不要**把整张三角网丢给 QuadRemesh 重构——那会毁掉切面拓扑（实测的坑）。
        /// </summary>
        static void BuildFacetMeshPatches(DiamondFacetResult res, DiamondFacetSettings s, List<string> note)
        {
            Mesh src = res.Shaded;
            if (src == null || src.Faces.Count == 0) { note.Add("网格：没有可细分的切面三角网"); return; }

            int tris = src.Faces.Count;
            int target = s.SmoothQuadCount;
            if (target < 50) target = 50;
            if (target > 500000) target = 500000;
            double adaptive = s.SmoothAdaptive;
            if (adaptive < 0.0) adaptive = 0.0;
            if (adaptive > 100.0) adaptive = 100.0;
            // 平滑度 → 档位指数 beta：100% = 1（档位 ∝ 切面最长边 → 网格边长一致），80% = 0.6（大面更粗、更省面）
            double beta = (adaptive - 50.0) / 50.0;
            if (beta < 0.0) beta = 0.0;
            if (beta > 1.0) beta = 1.0;

            // 每个切面的「局部尺寸」= 最长边；细分档位按它配平（beta = 0 时所有切面同一档细分）
            var longEdges = new double[tris];
            double meanLong = 0;
            for (int i = 0; i < tris; i++)
            {
                MeshFace f = src.Faces.GetFace(i);
                Point3d a = src.Vertices.Point3dAt(f.A), b = src.Vertices.Point3dAt(f.B), c = src.Vertices.Point3dAt(f.C);
                double le = Math.Max(a.DistanceTo(b), Math.Max(b.DistanceTo(c), c.DistanceTo(a)));
                if (!(le > 0)) le = 1e-9;
                longEdges[i] = le;
                meanLong += le;
            }
            meanLong /= tris;
            if (!(meanLong > 0)) meanLong = 1.0;
            double sumR2 = 0;
            for (int i = 0; i < tris; i++) sumR2 += Math.Pow(longEdges[i] / meanLong, 2.0 * beta);
            if (!(sumR2 > 0)) sumR2 = tris;
            double m0 = Math.Sqrt(target / sumR2);

            var outp = new Mesh();
            var map = new Dictionary<Key3, int>();
            double tol = 1e-9;
            try
            {
                BoundingBox bb = src.GetBoundingBox(true);
                if (bb.IsValid) tol = Math.Max(1e-9, bb.Diagonal.Length * 1e-9);
            }
            catch { }

            int patches = 0, subFaces = 0;
            var grids = new List<int>(tris);
            for (int i = 0; i < tris; i++)
            {
                MeshFace f = src.Faces.GetFace(i);
                Point3d a = src.Vertices.Point3dAt(f.A);
                Point3d b = src.Vertices.Point3dAt(f.B);
                Point3d c = src.Vertices.Point3dAt(f.C);
                int n = (int)Math.Round(m0 * Math.Pow(longEdges[i] / meanLong, beta));
                if (n < 1) n = 1;
                if (n > MaxPatchGrid) n = MaxPatchGrid;
                AddPatch(outp, map, tol, a, b, c, n);
                grids.Add(n);
                patches++;
                subFaces += n * n;
            }
            try { outp.FaceNormals.ComputeFaceNormals(); outp.Normals.ComputeNormals(); } catch { }   // 预览着色要用（缺法向会渲染成黑）
            res.SmoothMesh = outp;
            res.SmoothPatches = patches;
            res.SmoothGrids = grids;
            res.SmoothFaces = outp.Faces.Count;
            res.Smoothed = outp.Faces.Count > 0;
            note.Add(string.Format(CultureInfo.InvariantCulture,
                "网格：{0} 个切面各一张网格片 → 细分 {1} 个三角面（目标 {2}，平滑度 {3:0.#}%）",
                patches, outp.Faces.Count, target, adaptive));
        }

        internal const int MaxPatchGrid = 64;   // 单张网格片每边最多细分 64 段（n×n 张面）

        /// <summary>把 1 个三角切面拆成 n×n 张三角面（重心格点，线性插值 → 片内保持平面）</summary>
        static void AddPatch(Mesh m, Dictionary<Key3, int> map, double tol, Point3d a, Point3d b, Point3d c, int n)
        {
            int[] idx = new int[(n + 1) * (n + 2) / 2];
            for (int i = 0; i <= n; i++)
                for (int j = 0; i + j <= n; j++)
                    idx[GIndex(i, j, n)] = Vid(m, map, tol, new Point3d(
                        a.X + (b.X - a.X) * i / n + (c.X - a.X) * j / n,
                        a.Y + (b.Y - a.Y) * i / n + (c.Y - a.Y) * j / n,
                        a.Z + (b.Z - a.Z) * i / n + (c.Z - a.Z) * j / n));
            for (int i = 0; i < n; i++)
                for (int j = 0; i + j < n; j++)
                {
                    int v00 = idx[GIndex(i, j, n)], v10 = idx[GIndex(i + 1, j, n)], v01 = idx[GIndex(i, j + 1, n)];
                    m.Faces.AddFace(v00, v10, v01);
                    if (i + j + 2 <= n)
                        m.Faces.AddFace(v10, idx[GIndex(i + 1, j + 1, n)], v01);
                }
        }

        /// <summary>重心格点 v(i,j)（i+j≤n）在一维数组里的下标</summary>
        static int GIndex(int i, int j, int n) { return i * (n + 1) - i * (i - 1) / 2 + j; }

        /// <summary>取顶点：同坐标（容差内）合并成一个 → 相邻网格片共享边上的点自动焊在一起</summary>
        static int Vid(Mesh m, Dictionary<Key3, int> map, double tol, Point3d p)
        {
            var k = new Key3((long)Math.Round(p.X / tol), (long)Math.Round(p.Y / tol), (long)Math.Round(p.Z / tol));
            int id;
            if (map.TryGetValue(k, out id)) return id;
            id = m.Vertices.Add(p);
            map[k] = id;
            return id;
        }

        /// <summary>整数坐标三元组（顶点合并用的哈希键）</summary>
        struct Key3 : IEquatable<Key3>
        {
            readonly long _x, _y, _z;
            public Key3(long x, long y, long z) { _x = x; _y = y; _z = z; }
            public bool Equals(Key3 o) { return _x == o._x && _y == o._y && _z == o._z; }
            public override bool Equals(object o) { return o is Key3 && Equals((Key3)o); }
            public override int GetHashCode() { return (_x.GetHashCode() * 397 ^ _y.GetHashCode()) * 397 ^ _z.GetHashCode(); }
        }

        static Mesh CombineMeshes(Mesh[] parts)
        {
            if (parts == null || parts.Length == 0) return null;
            if (parts.Length == 1) return parts[0];
            var m = new Mesh();
            for (int i = 0; i < parts.Length; i++) if (parts[i] != null) m.Append(parts[i]);
            return m;
        }

        /// <summary>
        /// 一次松弛：把互相靠得太近的点推开（边界点当固定源），让面片大小更均匀。
        /// 用抖动网格的格子做邻域（O(n)，不需要再剖分一遍）；不做「向邻居平均靠拢」，
        /// 否则整片点会往中间收缩、边界一圈反而变成大三角。
        /// </summary>
        internal static List<Point2d> RelaxOnce(List<Point2d> inner, List<Point2d> region, double spacing)
        {
            if (inner.Count == 0) return null;
            double step = Math.Max(MinFacetSize, spacing);
            double target = step * 0.95;
            double damp = 0.5;

            double x0 = double.MaxValue, y0 = double.MaxValue;
            for (int i = 0; i < region.Count; i++)
            {
                if (region[i].X < x0) x0 = region[i].X;
                if (region[i].Y < y0) y0 = region[i].Y;
            }

            var innerIndex = BuildCellIndex(inner, x0, y0, step);
            var ringIndex = BuildCellIndex(region, x0, y0, step);

            var pushX = new double[inner.Count];
            var pushY = new double[inner.Count];
            var probe = new List<int>();
            for (int i = 0; i < inner.Count; i++)
            {
                Point2d p = inner[i];
                long cx = CellOf(p.X, x0, step), cy = CellOf(p.Y, y0, step);
                probe.Clear();
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                    {
                        List<int> bucket;
                        if (innerIndex.TryGetValue(Key(cx + dx, cy + dy), out bucket))
                            for (int k = 0; k < bucket.Count; k++) if (bucket[k] != i) probe.Add(bucket[k]);
                        if (ringIndex.TryGetValue(Key(cx + dx, cy + dy), out bucket))
                            for (int k = 0; k < bucket.Count; k++) Accumulate(pushX, pushY, i, p, region[bucket[k]], target);
                    }
                for (int k = 0; k < probe.Count; k++) Accumulate(pushX, pushY, i, p, inner[probe[k]], target);
            }

            var outp = new List<Point2d>(inner.Count);
            for (int i = 0; i < inner.Count; i++)
            {
                double mx = pushX[i] * damp, my = pushY[i] * damp;
                double len = Math.Sqrt(mx * mx + my * my);
                if (len > target * 0.5 && len > 1e-12) { mx = mx / len * target * 0.5; my = my / len * target * 0.5; }
                var q = new Point2d(inner[i].X + mx, inner[i].Y + my);
                if (len < 1e-12 || !Inside(region, q) || DistToPoly(region, q) < step * 0.3) q = inner[i];
                outp.Add(q);
            }
            return outp;
        }

        /// <summary>两点太近就把 i 往反方向推（推到 target 距离为止）</summary>
        static void Accumulate(double[] px, double[] py, int i, Point2d from, Point2d to, double target)
        {
            double dx = from.X - to.X, dy = from.Y - to.Y;
            double l2 = dx * dx + dy * dy;
            if (l2 >= target * target || l2 < 1e-18) return;
            double l = Math.Sqrt(l2);
            double k = (target - l) / l;
            px[i] += dx * k;
            py[i] += dy * k;
        }

        static Dictionary<long, List<int>> BuildCellIndex(List<Point2d> pts, double x0, double y0, double step)
        {
            var map = new Dictionary<long, List<int>>(pts.Count * 2);
            for (int i = 0; i < pts.Count; i++)
            {
                long key = Key(CellOf(pts[i].X, x0, step), CellOf(pts[i].Y, y0, step));
                List<int> bucket;
                if (!map.TryGetValue(key, out bucket)) { bucket = new List<int>(4); map[key] = bucket; }
                bucket.Add(i);
            }
            return map;
        }

        static long CellOf(double v, double origin, double step)
        {
            double d = (v - origin) / step;
            if (d < -1e6) d = -1e6; if (d > 1e6) d = 1e6;
            return (long)Math.Floor(d);
        }

        static long Key(long a, long b)
        {
            unchecked { return (a << 32) ^ (b & 0xffffffffL); }
        }

        // ============================================================ 批量

        /// <summary>按一组物件 ID 生成（面板与自检共用）</summary>
        internal static List<DiamondFacetResult> Generate(RhinoDoc doc, IList<Guid> ids,
            DiamondFacetSettings s, out string report)
        {
            var outp = new List<DiamondFacetResult>();
            var lines = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                RhinoObject obj = doc.Objects.FindId(ids[i]);
                string why;
                FacetBoundary b;
                if (!TryResolve(obj, out b, out why))
                {
                    var bad = new DiamondFacetResult { Error = why, BoundaryDesc = obj != null ? Describe(obj) : ids[i].ToString() };
                    outp.Add(bad);
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "跳过 {0}：{1}", bad.BoundaryDesc, why));
                    continue;
                }
                DiamondFacetResult r = GenerateOne(b, s);
                outp.Add(r);
                if (!string.IsNullOrEmpty(r.Error))
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}：失败 —— {1}", r.BoundaryDesc, r.Error));
                else
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}：{1} 个三角面 / {2} 条线 / {3} 张平面面 · 面积 {4:0.###}（边界 {5:0.###}）· {6:0.00}s{7}",
                        r.BoundaryDesc, r.Triangles, r.Wireframe.Count, r.Faces.Count,
                        r.FacetArea, r.BoundaryArea, r.Seconds,
                        string.IsNullOrEmpty(r.Note) ? "" : " · " + r.Note));
            }
            report = string.Join("\n", lines.ToArray());
            return outp;
        }
    }
}
