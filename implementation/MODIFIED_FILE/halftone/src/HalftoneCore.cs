using System;
using System.Collections.Generic;
using System.Text;
using Rhino;
using Rhino.Geometry;

namespace HalftonePattern
{
    /// <summary>圆点渐变阵列参数（长度单位 mm）</summary>
    public class HalftoneSettings
    {
        public double MaxDia = 8.0;     // 最大直径 mm（中心）
        public double MinDia = 0.5;     // 最小直径 mm（边缘）
        public double Pitch = 4.0;      // 阵列间距 mm（越小越密）
        public double Margin = 1.0;     // 图形到曲面边界的距离 mm
        public int ArrayMode = 0;       // 0=方形网格 1=交错网格 2=六边形 3=同心环 4=螺旋(黄金角) 5=抖动网格
        public int Shape = 0;           // 0=圆形 1=三角形 2=方形 3=六边形
        public double Rotation = 0.0;   // 图形整体旋转角度（度，叠加在自动交替朝向之上）
        public double Falloff = 1.0;    // 渐变衰减指数：1=线性；越大衰减越快（中心大圆收缩得越快）
        public double Jitter = 0.35;    // 抖动幅度（占间距比例，仅抖动网格用）
        // 渐变起点：用户指定的物件采样点（世界坐标，点=1个；曲线=沿线采样；其它=包围盒中心）。
        // 为 null 或 UsePickedCenter=false 时用曲面自身中心。
        public List<Point3d> CenterPoints = null;
        public bool UsePickedCenter = true;
        public double Tol = 0.01;
        public double MmToModel = 1.0;  // mm → 文档模型单位

        /// <summary>参考面：-1 = 整个多重曲面（所有面）；&gt;=0 = 只生成该面（面板的「选中面 / 全部面」）</summary>
        public int OnlyFace = -1;
        /// <summary>命令里点选到的面序号（面板切「选中面」时用它）</summary>
        public int PickedFace = -1;
        /// <summary>参考对象的面数（面板用来判断「参考面」选项要不要禁用）</summary>
        public int FaceCount = 1;
        public HalftoneSettings Clone() { return (HalftoneSettings)MemberwiseClone(); }

        public static readonly string[] ArrayNames = { "方形网格", "交错网格", "六边形", "同心环", "螺旋(黄金角)", "抖动网格" };
        public static readonly string[] ShapeNames = { "圆形", "三角形", "方形", "六边形" };
    }

    /// <summary>候选点：坐标 + 网格序号（形状朝向按序号交替）</summary>
    internal struct Cand
    {
        public Point2d P;
        public int I, J;
        public Cand(Point2d p, int i, int j) { P = p; I = i; J = j; }
    }

    public class HalftoneFaceResult
    {
        public int FaceIndex;
        public bool Planar;
        public Plane Plane;
        public List<Curve> Shapes = new List<Curve>();
        public string Note = "";
        public double MinEdge = double.MaxValue;
        public int Count { get { return Shapes.Count; } }
    }

    /// <summary>2D 区域：环点串 + 网格加速距离查询</summary>
    internal class Region2D
    {
        public readonly List<Point2d[]> Rings = new List<Point2d[]>();
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;

        readonly List<Point2d> _sa = new List<Point2d>();
        readonly List<Point2d> _sb = new List<Point2d>();
        Dictionary<long, List<int>> _grid;
        double _cell = 1.0;

        public double W { get { return MaxX - MinX; } }
        public double H { get { return MaxY - MinY; } }
        public double Diagonal { get { double w = W, h = H; return Math.Sqrt(w * w + h * h); } }

        public void AddRing(List<Point2d> pts)
        {
            if (pts == null || pts.Count < 2) return;
            var clean = new List<Point2d>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Point2d p = pts[i];
                if (double.IsNaN(p.X) || double.IsNaN(p.Y)) continue;
                if (clean.Count > 0 && clean[clean.Count - 1].DistanceTo(p) < 1e-12) continue;
                clean.Add(p);
            }
            if (clean.Count > 1 && clean[0].DistanceTo(clean[clean.Count - 1]) < 1e-12)
                clean.RemoveAt(clean.Count - 1);
            if (clean.Count < 2) return;
            Rings.Add(clean.ToArray());
            for (int i = 0; i < clean.Count; i++)
            {
                Point2d p = clean[i];
                if (p.X < MinX) MinX = p.X;
                if (p.Y < MinY) MinY = p.Y;
                if (p.X > MaxX) MaxX = p.X;
                if (p.Y > MaxY) MaxY = p.Y;
            }
        }

        public void BuildGrid(double cell)
        {
            _cell = Math.Max(cell, 1e-6);
            _grid = new Dictionary<long, List<int>>();
            _sa.Clear(); _sb.Clear();
            for (int r = 0; r < Rings.Count; r++)
            {
                Point2d[] ring = Rings[r];
                for (int i = 0; i < ring.Length; i++)
                {
                    Point2d a = ring[i], b = ring[(i + 1) % ring.Length];
                    if (a.DistanceTo(b) < 1e-12) continue;
                    int idx = _sa.Count; _sa.Add(a); _sb.Add(b);
                    int x0 = CellI(Math.Min(a.X, b.X)), x1 = CellI(Math.Max(a.X, b.X));
                    int y0 = CellI(Math.Min(a.Y, b.Y)), y1 = CellI(Math.Max(a.Y, b.Y));
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                        {
                            long key = Key(x, y);
                            List<int> list;
                            if (!_grid.TryGetValue(key, out list)) { list = new List<int>(2); _grid[key] = list; }
                            list.Add(idx);
                        }
                }
            }
        }

        int CellI(double v) { return (int)Math.Floor(v / _cell); }
        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }

        public double Distance(Point2d p, double maxR)
        {
            if (_grid == null) return double.MaxValue;
            int rr = (int)Math.Ceiling(maxR / _cell);
            if (rr < 1) rr = 1;
            if (rr > 96) rr = 96;
            int cx = CellI(p.X), cy = CellI(p.Y);
            double best = double.MaxValue;
            for (int x = cx - rr; x <= cx + rr; x++)
                for (int y = cy - rr; y <= cy + rr; y++)
                {
                    List<int> list;
                    if (!_grid.TryGetValue(Key(x, y), out list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        int s = list[i];
                        double d = DistPointSeg(p, _sa[s], _sb[s]);
                        if (d < best) best = d;
                    }
                }
            return best;
        }

        static double DistPointSeg(Point2d p, Point2d a, Point2d b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            double t = 0;
            if (len2 > 1e-20) { t = (wx * vx + wy * vy) / len2; if (t < 0) t = 0; else if (t > 1) t = 1; }
            double dx = wx - t * vx, dy = wy - t * vy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>点是否在区域内（奇偶射线法，支持带孔面）</summary>
        public bool Inside(Point2d p)
        {
            int cnt = 0;
            for (int i = 0; i < _sa.Count; i++)
            {
                Point2d a = _sa[i], b = _sb[i];
                if ((a.Y > p.Y) == (b.Y > p.Y)) continue;
                double x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (x > p.X) cnt++;
            }
            return (cnt & 1) == 1;
        }
    }

    /// <summary>
    /// 二维工作空间：把面上的点映射到一块「近似等距」的平面坐标。
    /// - 平面面：直接用面平面投影（精确、无损）。
    /// - 曲面：用曲面的 UV 参数域，按每轴平均比例尺（|Su|、|Sv|）换算成模型长度。
    ///
    /// 不能用「面中心处的一张切平面」统一投影：曲面会弯离切平面，边界在切平面里
    /// 向内收缩，图形于是永远离真实边界差一条缝（边缘间距调到 0 也铺不满）；
    /// 弯曲超过 180°（整圈圆柱侧面）时投影还会自交，直接算不出区域。
    /// </summary>
    internal class SurfaceMap
    {
        public bool Planar;
        public Plane Frame;      // 平面面用
        public BrepFace Face;    // 曲面用
        public double U0, V0;    // 参考点（面中心）在 uv 域里的位置
        public double SU = 1, SV = 1;   // uv -> 模型单位 的比例尺

        static double Dot3(Vector3d a, Vector3d b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        public static SurfaceMap Create(BrepFace face)
        {
            if (face == null) return null;
            var m = new SurfaceMap();
            m.Face = face;

            double u0, v0;
            if (!GetFaceCenterUV(face, out u0, out v0)) return null;
            m.U0 = u0; m.V0 = v0;

            Plane pl = Plane.Unset;
            bool planar = false;
            try { planar = face.TryGetPlane(out pl, 1e-3) && pl.IsValid; } catch { planar = false; }

            if (planar)
            {
                Point3d org = face.PointAt(u0, v0);
                Vector3d nz = face.NormalAt(u0, v0);
                if (nz.IsValid && nz.Unitize())
                {
                    Vector3d x = WorldReference(nz);
                    Vector3d y = Vector3d.CrossProduct(nz, x);
                    if (y.Unitize())
                    {
                        var fr = new Plane(org, x, y);
                        if (fr.IsValid) { m.Frame = fr; m.Planar = true; }
                    }
                }
            }

            if (!m.Planar)
            {
                ComputeScales(face, out m.SU, out m.SV);
                // 记录一张切平面，仅供诊断显示（几何计算不再用它）
                Point3d org = face.PointAt(u0, v0);
                Vector3d nz = face.NormalAt(u0, v0);
                if (nz.IsValid && nz.Unitize())
                {
                    Vector3d x = WorldReference(nz);
                    Vector3d y = Vector3d.CrossProduct(nz, x);
                    if (y.Unitize()) m.Frame = new Plane(org, x, y);
                }
            }
            return m;
        }

        /// <summary>uv 每单位对应的模型长度（在域上取平均）</summary>
        static void ComputeScales(BrepFace face, out double su, out double sv)
        {
            su = 1; sv = 1;
            try
            {
                Interval du = face.Domain(0), dv = face.Domain(1);
                double hu = Math.Max((du.Max - du.Min) * 1e-3, 1e-9);
                double hv = Math.Max((dv.Max - dv.Min) * 1e-3, 1e-9);
                double accU = 0, accV = 0; int n = 0;
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        double u = du.ParameterAt(i / 4.0), v = dv.ParameterAt(j / 4.0);
                        Point3d P = face.PointAt(u, v);
                        Point3d Pu = face.PointAt(u + hu, v);
                        Point3d Pv = face.PointAt(u, v + hv);
                        if (!P.IsValid || !Pu.IsValid || !Pv.IsValid) continue;
                        accU += Pu.DistanceTo(P) / hu;
                        accV += Pv.DistanceTo(P) / hv;
                        n++;
                    }
                if (n > 0) { su = accU / n; sv = accV / n; }
            }
            catch { }
            if (!(su > 1e-9)) su = 1;
            if (!(sv > 1e-9)) sv = 1;
        }

        static bool GetFaceCenterUV(BrepFace face, out double u, out double v)
        {
            u = 0; v = 0;
            Point3d c = Point3d.Unset;
            try
            {
                var acc = new Point3d(0, 0, 0);
                double sum = 0;
                foreach (BrepLoop loop in face.Loops)
                {
                    Point3d[] pts = Halftone.SamplePoints(loop.To3dCurve(), 32);
                    if (pts == null) continue;
                    for (int i = 0; i < pts.Length; i++) { acc += pts[i]; sum += 1; }
                }
                if (sum > 0) c = new Point3d(acc.X / sum, acc.Y / sum, acc.Z / sum);
            }
            catch { c = Point3d.Unset; }

            if (!c.IsValid)
            {
                try { c = face.PointAt(face.Domain(0).Mid, face.Domain(1).Mid); }
                catch { return false; }
            }
            if (!c.IsValid) return false;
            return face.ClosestPoint(c, out u, out v);
        }

        static Vector3d WorldReference(Vector3d z)
        {
            var gx = new Vector3d(1, 0, 0);
            var p = gx - z * Dot3(gx, z);
            if (p.Length < 1e-6) { var gz = new Vector3d(0, 0, 1); p = gz - z * Dot3(gz, z); }
            if (p.Length < 1e-6) { var gy = new Vector3d(0, 1, 0); p = gy - z * Dot3(gy, z); }
            if (!p.Unitize()) p = new Vector3d(1, 0, 0);
            return p;
        }

        public Point2d To2d(Point3d p)
        {
            if (Planar)
            {
                Vector3d v = p - Frame.Origin;
                return new Point2d(Dot3(v, Frame.XAxis), Dot3(v, Frame.YAxis));
            }
            double u, v2;
            if (!Face.ClosestPoint(p, out u, out v2)) return Point2d.Unset;
            return new Point2d((u - U0) * SU, (v2 - V0) * SV);
        }

        public Point3d To3d(double x, double y)
        {
            if (Planar) return Frame.PointAt(x, y);
            return Face.PointAt(U0 + x / SU, V0 + y / SV);
        }

        public Point3d To3d(Point2d q) { return To3d(q.X, q.Y); }

        /// <summary>二维 x / y 方向对应的曲面切向（图形朝向的参考）</summary>
        public void GetTangents(out Vector3d tx, out Vector3d ty)
        {
            if (Planar) { tx = Frame.XAxis; ty = Frame.YAxis; return; }
            Point3d P = Face.PointAt(U0, V0);
            tx = Face.PointAt(U0 + 1.0 / Math.Max(SU, 1e-9), V0) - P;
            ty = Face.PointAt(U0, V0 + 1.0 / Math.Max(SV, 1e-9)) - P;
            if (!tx.Unitize()) tx = new Vector3d(1, 0, 0);
            if (!ty.Unitize()) ty = new Vector3d(0, 1, 0);
        }
    }

    public static class Halftone
    {
        internal static double Dot(Vector3d a, Vector3d b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        internal static Point2d To2d(Plane pl, Point3d p)
        {
            Vector3d v = p - pl.Origin;
            return new Point2d(Dot(v, pl.XAxis), Dot(v, pl.YAxis));
        }

        internal static Point3d[] SamplePoints(Curve c, int n)
        {
            if (c == null) return null;
            double[] ts = c.DivideByCount(n, true);
            if (ts == null) return null;
            var pts = new Point3d[ts.Length];
            for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
            return pts;
        }

        static double Smoothstep(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * (3 - 2 * t);
        }

        public static List<HalftoneFaceResult> Generate(Brep brep, HalftoneSettings settings, out string report)
        {
            var results = new List<HalftoneFaceResult>();
            var sb = new StringBuilder();
            if (brep == null) { report = "无有效几何体"; return results; }

            HalftoneSettings s = settings.Clone();
            s.MaxDia = Math.Max(s.MaxDia, 1e-4);
            s.MinDia = Math.Max(s.MinDia, 0.0);
            if (s.MinDia > s.MaxDia) s.MinDia = s.MaxDia;
            s.Pitch = Math.Max(s.Pitch, 1e-3);
            if (!(s.MmToModel > 0)) s.MmToModel = 1.0;
            s.Tol = Math.Max(s.Tol, 1e-5);

            // 参考面：单面时只处理点选的那一个面（多重曲面上的「选中面 / 全部面」）
            int nf = brep.Faces.Count;
            int only = (s.OnlyFace >= 0 && s.OnlyFace < nf) ? s.OnlyFace : -1;

            // 多重曲面 + 全部面 → 先试**全局排版**（在拟合平面里铺一套阵列，再投到各面成形）：
            // 这样整个对象就是一套阵列，接缝两侧是同一个图案的延续。对象太弯（命中率过低）时返回 null，
            // 退回「逐面 + 全局相位锚点」（每个面按自己参数域铺，但格点对齐到同一套全局阵列）。
            // 单面 / 选中面不加锚点（保持老行为）。
            HalftoneGlobal.Layout lay = null;
            if (nf > 1 && only < 0)
            {
                string greport;
                List<HalftoneFaceResult> g = HalftoneGlobal.Generate(brep, s, out greport);
                if (g != null)
                {
                    report = greport + "\r\n";
                    return g;
                }
                lay = HalftoneGlobal.BuildLayout(brep);
                sb.AppendLine("  （" + (string.IsNullOrEmpty(greport) ? "全局排版不可用" : greport) + "；退回逐面 + 全局相位锚点）");
            }

            for (int fi = 0; fi < nf; fi++)
            {
                if (only >= 0 && fi != only) continue;
                HalftoneFaceResult r;
                try { r = GenerateFace(brep.Faces[fi], fi, s, lay); }
                catch (Exception ex) { r = new HalftoneFaceResult { FaceIndex = fi, Note = "异常: " + ex.Message }; }
                if (r == null) r = new HalftoneFaceResult { FaceIndex = fi, Note = "无结果" };
                results.Add(r);
                sb.AppendFormat("面{0}[{1}] 图形 {2} 个{3}{4}\r\n",
                    fi, r.Planar ? "平面" : "曲面", r.Shapes.Count,
                    r.MinEdge < double.MaxValue ? string.Format(" 实测最小边距 {0:0.###}", r.MinEdge) : "",
                    string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note);
            }
            report = sb.ToString();
            return results;
        }

        static HalftoneFaceResult GenerateFace(BrepFace face, int index, HalftoneSettings s, HalftoneGlobal.Layout lay)
        {
            var res = new HalftoneFaceResult { FaceIndex = index };

            SurfaceMap map = SurfaceMap.Create(face);
            if (map == null)
            {
                res.Note = "无法建立参考坐标系，跳过";
                return res;
            }
            res.Planar = map.Planar;
            res.Plane = map.Frame;

            double u2m = s.MmToModel;
            double pitch = s.Pitch * u2m;
            double margin = s.Margin * u2m;
            double rMax = s.MaxDia * u2m * 0.5;
            double rMin = s.MinDia * u2m * 0.5;

            // 整圈环绕的面（例如圆柱整个侧面）：uv 域里边界是两条从 u=0 拉到 u=2π 的开口边，
            // 拼不出闭合区域（这种面拓扑上是圆环，不是圆盘）。明确报出来，不要硬算出垃圾几何。
            if (!map.Planar && FaceWraps(map.Face))
            {
                res.Note = "整圈环绕的曲面暂不支持，请把面裁成一段（例如 120° 弧片）再生成";
                return res;
            }

            double step = Math.Max(s.Tol * u2m, 1e-4);
            Region2D region = BuildRegion(map, step);
            if (region.Rings.Count == 0) { res.Note = "无有效边界"; return res; }

            region.BuildGrid(Math.Max(rMax, Math.Max(region.Diagonal / 512.0, 1e-4)));

            // 渐变起点：优先用用户指定的物件（点/线/物件采样点，投影到本面参考系），否则用面中心（边界点平均）
            Point2d faceCenter = RingCentroid(region);
            List<Point2d> gradPts = null;
            if (s.UsePickedCenter && s.CenterPoints != null && s.CenterPoints.Count > 0)
            {
                gradPts = new List<Point2d>(s.CenterPoints.Count);
                foreach (Point3d p3 in s.CenterPoints) gradPts.Add(map.To2d(p3));
            }
            Point2d center = faceCenter;
            if (gradPts != null)
            {
                double sx = 0, sy = 0;
                foreach (Point2d q in gradPts) { sx += q.X; sy += q.Y; }
                center = new Point2d(sx / gradPts.Count, sy / gradPts.Count);   // 点阵原点取指定物件的重心
            }

            // 渐变半径 = 起点到边界最远点的距离，让「最大直径 → 最小直径」正好铺满整个面
            double maxDist = 0;
            foreach (Point2d[] ring in region.Rings)
                for (int k = 0; k < ring.Length; k++)
                {
                    double d = DistToGradient(ring[k], gradPts, faceCenter);
                    if (d > maxDist) maxDist = d;
                }
            if (!(maxDist > 1e-9)) maxDist = Math.Max(region.Diagonal * 0.5, 1e-6);

            // 网格类模式：把格点铺满整张面 —— 最外一排正好落在「边界 + 该处图形半径」上，
            // 而不是以某个原点等距排下去（那样边界永远剩不到一格的空档，看起来没铺满）。
            Point2d eL, eR, eB, eT;
            ExtremeBoundaryPoints(region, out eL, out eR, out eB, out eT);
            double gx0 = eL.X + margin + RadiusAt2d(s, eL, gradPts, faceCenter, maxDist, rMin, rMax);
            double gx1 = eR.X - margin - RadiusAt2d(s, eR, gradPts, faceCenter, maxDist, rMin, rMax);
            double gy0 = eB.Y + margin + RadiusAt2d(s, eB, gradPts, faceCenter, maxDist, rMin, rMax);
            double gy1 = eT.Y - margin - RadiusAt2d(s, eT, gradPts, faceCenter, maxDist, rMin, rMax);

            // 全局相位：把格点起点挪到「过全局锚点」的那一套格子上（相邻面因此对齐）
            if (lay != null)
            {
                Point2d a2 = map.To2d(lay.Anchor);
                if (!double.IsNaN(a2.X) && !double.IsNaN(a2.Y))
                {
                    gx0 = a2.X + Math.Ceiling((gx0 - a2.X) / pitch) * pitch;
                    gx1 = a2.X + Math.Floor((gx1 - a2.X) / pitch) * pitch;
                    gy0 = a2.Y + Math.Ceiling((gy0 - a2.Y) / pitch) * pitch;
                    gy1 = a2.Y + Math.Floor((gy1 - a2.Y) / pitch) * pitch;
                    if (gx1 < gx0) gx1 = gx0;
                    if (gy1 < gy0) gy1 = gy0;
                }
            }

            List<Cand> cands = BuildPoints(s, region, center, pitch, maxDist, gx0, gx1, gy0, gy1);
            if (cands == null) { res.Note = "阵列过密（超过 40000 个），请增大间距"; return res; }

            int made = 0;
            double minEdge = double.MaxValue;
            for (int k = 0; k < cands.Count; k++)
            {
                Cand q = cands[k];
                Point2d p = q.P;

                double r = RadiusAt2d(s, p, gradPts, faceCenter, maxDist, rMin, rMax);
                if (r < 1e-4) continue;

                if (!region.Inside(p)) continue;          // 中心必须在面内
                double rEff = margin + r;                  // 图形到边界的净距
                double dEdge = region.Distance(p, rEff);
                if (dEdge < rEff) continue;                // 图形必须完整落在面内（含间距）
                if (dEdge < minEdge) minEdge = dEdge;

                Curve c = MakeShape(map, p, r, s.Shape, q.I, q.J, s.Rotation * Math.PI / 180.0);
                if (c != null) { res.Shapes.Add(c); made++; }
            }

            if (made == 0 && res.Note.Length == 0) res.Note = "面太小或阵列间距过大，未生成图形";
            res.MinEdge = minEdge;
            return res;
        }

        // ---------------------------------------------------------------- 阵列点位生成
        /// <summary>某点处图形的半径（按渐变）</summary>
        static double RadiusAt2d(HalftoneSettings s, Point2d p, List<Point2d> gradPts, Point2d faceCenter,
                                 double maxDist, double rMin, double rMax)
        {
            double t = DistToGradient(p, gradPts, faceCenter) / maxDist;
            if (t > 1) t = 1;
            // 衰减：t=0（起点）→ 最大直径，t=1（最远）→ 最小直径；指数越大，尺寸掉得越快
            double fexp = s.Falloff > 0.01 ? s.Falloff : 1.0;
            return rMin + (rMax - rMin) * Math.Pow(1.0 - t, fexp);
        }

        /// <summary>区域边界上 X/Y 最小、最大的四个点</summary>
        static void ExtremeBoundaryPoints(Region2D region, out Point2d minX, out Point2d maxX,
                                          out Point2d minY, out Point2d maxY)
        {
            minX = maxX = minY = maxY = new Point2d((region.MinX + region.MaxX) * 0.5,
                                                    (region.MinY + region.MaxY) * 0.5);
            double bx0 = double.MaxValue, bx1 = double.MinValue, by0 = double.MaxValue, by1 = double.MinValue;
            foreach (Point2d[] ring in region.Rings)
                for (int i = 0; i < ring.Length; i++)
                {
                    Point2d p = ring[i];
                    if (p.X < bx0) { bx0 = p.X; minX = p; }
                    if (p.X > bx1) { bx1 = p.X; maxX = p; }
                    if (p.Y < by0) { by0 = p.Y; minY = p; }
                    if (p.Y > by1) { by1 = p.Y; maxY = p; }
                }
        }

        /// <summary>
        /// 按所选阵列方式生成候选点；返回 null 表示数量超限。
        /// 网格类模式（网格/交错/六边/抖动）的格点跨度由 gx0..gx1 / gy0..gy1 给出
        /// （已按「边界 + 该处半径」内缩），并把格数取整后重新均分 —— 这样最外一排正好贴到边界。
        /// 同心环 / 螺旋 仍以 center 为中心向外扩散。
        /// </summary>
        static List<Cand> BuildPoints(HalftoneSettings s, Region2D region, Point2d center, double pitch, double maxDist,
                                      double gx0, double gx1, double gy0, double gy1)
        {
            var list = new List<Cand>();
            // 同心环 / 螺旋 的索引范围（以 center 为原点）
            int i0 = (int)Math.Floor((region.MinX - center.X) / pitch) - 1;
            int i1 = (int)Math.Ceiling((region.MaxX - center.X) / pitch) + 1;
            int j0 = (int)Math.Floor((region.MinY - center.Y) / pitch) - 1;
            int j1 = (int)Math.Ceiling((region.MaxY - center.Y) / pitch) + 1;
            if ((long)(i1 - i0 + 1) * (j1 - j0 + 1) > 40000) return null;

            // 网格跨度：格数取整后重新均分，首末格正好落在 gx0 / gx1（gy0 / gy1）
            int nx = 1, ny = 1;
            double sx = pitch, sy = pitch;
            if (gx1 - gx0 > 1e-9) { nx = (int)Math.Floor((gx1 - gx0) / pitch) + 1; if (nx < 1) nx = 1; sx = nx > 1 ? (gx1 - gx0) / (nx - 1) : pitch; }
            else { gx0 = center.X; nx = 1; }
            if (gy1 - gy0 > 1e-9) { ny = (int)Math.Floor((gy1 - gy0) / pitch) + 1; if (ny < 1) ny = 1; sy = ny > 1 ? (gy1 - gy0) / (ny - 1) : pitch; }
            else { gy0 = center.Y; ny = 1; }
            if ((long)nx * ny > 40000) return null;

            switch (s.ArrayMode)
            {
                case 0: // 方形网格
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i++)
                            list.Add(new Cand(new Point2d(gx0 + i * sx, gy0 + j * sy), i, j));
                    break;

                case 1: // 交错网格：奇数行错半格
                    for (int j = 0; j < ny; j++)
                    {
                        double ox = (j & 1) == 1 ? sx * 0.5 : 0.0;
                        for (int i = 0; i < nx; i++)
                            list.Add(new Cand(new Point2d(gx0 + i * sx + ox, gy0 + j * sy), i, j));
                    }
                    break;

                case 2: // 六边形：行距 = 间距 * sin60°，奇数行错半格
                    {
                        sy *= 0.8660254037844386;
                        for (int j = 0; j < ny; j++)
                        {
                            double ox = (j & 1) == 1 ? sx * 0.5 : 0.0;
                            for (int i = 0; i < nx; i++)
                                list.Add(new Cand(new Point2d(gx0 + i * sx + ox, gy0 + j * sy), i, j));
                        }
                    }
                    break;

                case 3: // 同心环：每环周长按间距布点
                    {
                        int maxRing = (int)Math.Ceiling(maxDist / pitch) + 1;
                        for (int k = 0; k <= maxRing; k++)
                        {
                            double rr = k * pitch;
                            if (k == 0) { list.Add(new Cand(center, 0, 0)); continue; }
                            int n = Math.Max(6, (int)Math.Round(2 * Math.PI * rr / pitch));
                            for (int m = 0; m < n; m++)
                            {
                                double a = 2 * Math.PI * m / n;
                                list.Add(new Cand(new Point2d(
                                    center.X + rr * Math.Cos(a),
                                    center.Y + rr * Math.Sin(a)), k, m));
                            }
                        }
                    }
                    break;

                case 4: // 螺旋（黄金角 / 向日葵）
                    {
                        double area = Math.PI * maxDist * maxDist;
                        int n = (int)Math.Round(area / (pitch * pitch));
                        if (n > 40000) return null;
                        const double golden = 2.399963229728653; // 137.5°
                        for (int t = 0; t < n; t++)
                        {
                            double rr = pitch * Math.Sqrt(t) * 0.92;
                            if (rr > maxDist + pitch) break;
                            double a = t * golden;
                            list.Add(new Cand(new Point2d(
                                center.X + rr * Math.Cos(a),
                                center.Y + rr * Math.Sin(a)), t, 0));
                        }
                    }
                    break;

                case 5: // 抖动网格：网格 + 确定性伪随机偏移
                    {
                        double amp = pitch * Math.Max(0.0, Math.Min(0.9, s.Jitter));
                        for (int j = 0; j < ny; j++)
                            for (int i = 0; i < nx; i++)
                                list.Add(new Cand(new Point2d(
                                    gx0 + i * sx + (Hash01(i, j, 1) - 0.5) * amp,
                                    gy0 + j * sy + (Hash01(i, j, 2) - 0.5) * amp), i, j));
                    }
                    break;

                default:
                    goto case 0;
            }
            // 环形/螺旋等模式不受 nx*ny 上限约束，这里统一兜底
            if (list.Count > 40000) return null;
            return list;
        }

        /// <summary>点到「渐变起点物件」的距离：点集为空→到面中心；单点→点距；多点→折线最近距离</summary>
        static double DistToGradient(Point2d p, List<Point2d> gradPts, Point2d faceCenter)
        {
            if (gradPts == null || gradPts.Count == 0) return p.DistanceTo(faceCenter);
            if (gradPts.Count == 1) return p.DistanceTo(gradPts[0]);
            double best = double.MaxValue;
            for (int i = 0; i + 1 < gradPts.Count; i++)
            {
                double d = DistPointSeg(p, gradPts[i], gradPts[i + 1]);
                if (d < best) best = d;
            }
            return best;
        }

        static double DistPointSeg(Point2d p, Point2d a, Point2d b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            double t = 0;
            if (len2 > 1e-20) { t = (wx * vx + wy * vy) / len2; if (t < 0) t = 0; else if (t > 1) t = 1; }
            double dx = wx - t * vx, dy = wy - t * vy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>确定性伪随机（同一 (i,j) 每次结果一致，保证预览与生成结果相同）</summary>
        static double Hash01(int i, int j, int salt)
        {
            unchecked
            {
                int h = i * 374761393 + j * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (double)0x7fffffff;
            }
        }

        /// <summary>面的边界在 uv 域里是否开口（= 面绕曲面一圈，例如圆柱整侧面）</summary>
        static bool FaceWraps(BrepFace face)
        {
            try
            {
                foreach (BrepLoop loop in face.Loops)
                {
                    Curve c2 = loop.To2dCurve();
                    if (c2 == null) continue;
                    if (!c2.IsClosed) return true;
                }
            }
            catch { }
            return false;
        }

        // ---------------------------------------------------------------- 边界采样
        static Region2D BuildRegion(SurfaceMap map, double step)
        {
            var region = new Region2D();
            if (map.Planar)
            {
                foreach (BrepLoop loop in map.Face.Loops)
                {
                    Curve c3 = loop.To3dCurve();
                    if (c3 == null) continue;
                    var pts = new List<Point2d>();
                    double len = c3.GetLength();
                    if (!(len > 0)) len = 1.0;
                    int n = (int)Math.Round(len / Math.Max(step, 1e-6));
                    n = Math.Max(16, Math.Min(4000, n));
                    Point3d[] sp = SamplePoints(c3, n);
                    if (sp == null) continue;
                    for (int i = 0; i < sp.Length - 1; i++)
                    {
                        Point2d q = map.To2d(sp[i]);
                        if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(q) < 1e-12) continue;
                        pts.Add(q);
                    }
                    region.AddRing(pts);
                }
                return region;
            }

            // 曲面：边界直接取曲面 uv 域里的边界曲线（精确，不会因投影收缩/自交）
            double scale = Math.Max((map.SU + map.SV) * 0.5, 1e-9);
            double stepUV = Math.Max(step / scale, 1e-9);
            foreach (BrepLoop loop in map.Face.Loops)
            {
                Curve c2 = loop.To2dCurve();
                if (c2 == null) continue;
                double len = c2.GetLength();
                if (!(len > 0)) len = 1.0;
                int n = (int)Math.Round(len / stepUV);
                n = Math.Max(16, Math.Min(4000, n));
                Point3d[] sp = SamplePoints(c2, n);
                if (sp == null) continue;
                var pts = new List<Point2d>();
                for (int i = 0; i < sp.Length - 1; i++)
                {
                    var q = new Point2d((sp[i].X - map.U0) * map.SU, (sp[i].Y - map.V0) * map.SV);
                    if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(q) < 1e-12) continue;
                    pts.Add(q);
                }
                region.AddRing(pts);
            }
            return region;
        }

        static Point2d RingCentroid(Region2D region)
        {
            double sx = 0, sy = 0; int n = 0;
            foreach (Point2d[] ring in region.Rings)
                for (int i = 0; i < ring.Length; i++) { sx += ring[i].X; sy += ring[i].Y; n++; }
            if (n == 0) return new Point2d((region.MinX + region.MaxX) * 0.5, (region.MinY + region.MaxY) * 0.5);
            return new Point2d(sx / n, sy / n);
        }

        static double MaxDistToRings(Region2D region, Point2d c)
        {
            double best = 0;
            foreach (Point2d[] ring in region.Rings)
                for (int i = 0; i < ring.Length; i++)
                {
                    double d = ring[i].DistanceTo(c);
                    if (d > best) best = d;
                }
            return best;
        }

        // ---------------------------------------------------------------- 图形
        /// <summary>形状：0=圆 1=三角 2=方 3=六边；朝向 = 自动交替 + 用户旋转角</summary>
        static Curve MakeShape(SurfaceMap map, Point2d p, double r, int shape, int i, int j, double rotRad)
        {
            if (shape == 0)
            {
                if (map.Planar)
                {
                    Point3d c3 = map.To3d(p);
                    var circ = new Circle(new Plane(c3, map.Frame.ZAxis), r);
                    return circ.ToNurbsCurve();
                }
                return ShapeOnSurface(map, p, r, 40, rotRad);
            }

            int sides;
            double baseAng;
            switch (shape)
            {
                case 1: // 三角形：四种朝向按 (i,j) 奇偶交替
                    sides = 3;
                    baseAng = Math.PI / 2 + ((i % 2) + 2 * (j % 2)) * Math.PI / 2;
                    break;
                case 2: // 方形：按 (i+j) 奇偶在 0°/45° 之间交替
                    sides = 4;
                    baseAng = Math.PI / 4 + ((i + j) & 1) * Math.PI / 4;
                    break;
                default: // 六边形：三种朝向轮换
                    sides = 6;
                    baseAng = ((i + j) % 3) * Math.PI / 6;
                    break;
            }
            return ShapeOnSurface(map, p, r, sides, baseAng + rotRad);
        }

        /// <summary>
        /// 曲面上的图形：先取图形中心在曲面上的点与法向，建立「该点处」的局部切平面，
        /// 图形在这个局部平面内构造后回投到曲面。
        /// 不能用面中心的大切平面统一投影：离中心越远，曲面越「弯离」切平面，
        /// 投影会把图形按方向压扁（圆柱上表现为圆变椭圆）。
        /// </summary>
        static Curve ShapeOnSurface(SurfaceMap map, Point2d p, double r, int sides, double baseAng)
        {
            Point3d P = map.To3d(p);
            if (!P.IsValid) return null;

            Vector3d N;
            if (map.Planar) N = map.Frame.ZAxis;
            else
            {
                N = map.Face.NormalAt(map.U0 + p.X / map.SU, map.V0 + p.Y / map.SV);
            }
            if (!N.IsValid || !N.Unitize()) return null;

            Vector3d tx, ty;
            map.GetTangents(out tx, out ty);
            Vector3d lx = tx - N * Dot(tx, N);
            if (lx.Length < 1e-9) lx = ty - N * Dot(ty, N);
            if (!lx.Unitize()) return null;
            Vector3d ly = Vector3d.CrossProduct(N, lx);
            if (!ly.Unitize()) return null;

            var pts = new List<Point3d>(sides + 1);
            for (int k = 0; k < sides; k++)
            {
                double a = baseAng + k * 2 * Math.PI / sides;
                Point3d q = P + (lx * Math.Cos(a) + ly * Math.Sin(a)) * r;
                if (map.Planar) { pts.Add(q); continue; }
                double u, v;
                if (!map.Face.ClosestPoint(q, out u, out v)) { pts.Add(q); continue; }
                pts.Add(map.Face.PointAt(u, v));
            }
            pts.Add(pts[0]);
            var plc = new PolylineCurve(pts);
            return plc.IsValid ? plc : null;
        }
    }
}
