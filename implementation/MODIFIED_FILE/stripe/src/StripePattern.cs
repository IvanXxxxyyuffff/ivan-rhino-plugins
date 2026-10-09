using System;
using System.Collections.Generic;
using System.Text;
using Rhino;
using Rhino.Geometry;

namespace StripeOnSurface
{
    /// <summary>条纹参数（长度单位 mm，角度单位度）</summary>
    public class StripeSettings
    {
        public double Width = 1.5;      // 条纹宽度 mm
        public double Spacing = 3.0;    // 相邻条纹净间距 mm
        public double AngleDeg = 45.0;  // 倾斜角度（相对世界 X 轴在面内的投影）
        public double Margin = 2.0;     // 距面边缘距离 mm
        public bool RoundedEnds = true; // 端部圆角
        public bool ConformToBoundary = false; // 完全贴合边界：条纹被边界（按边缘距离内缩）裁剪，端部随边界形状
        public double CornerRadius = 0.0;      // 硬边倒圆角半径 mm（贴合边界 / 端部平齐 的直角变圆角）
        public double Tol = 0.01;
        public double MmToModel = 1.0;  // mm -> 文档模型单位 的换算系数
        public double Depth = 0.0;      // 保留（当前不使用）

        public StripeSettings Clone() { return (StripeSettings)MemberwiseClone(); }
    }

    /// <summary>单个面的生成结果</summary>
    public class StripeFaceResult
    {
        public int FaceIndex;
        public bool Planar;
        public Plane Plane;
        public List<Curve> Stripes = new List<Curve>();
        public string Note = "";
        public double MinEdgeDistance = double.MaxValue;
        public Vector3d DiagNormal = Vector3d.Unset;   // 条纹排布方向（垂直于条纹，用于校验）

        public int Count { get { return Stripes.Count; } }
    }

    /// <summary>2D 区域：环点串 + 网格加速的距离查询 + 奇偶内外判定</summary>
    internal class Region2D
    {
        public readonly List<Point2d[]> Rings = new List<Point2d[]>();
        readonly List<bool> _closed = new List<bool>();
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;

        readonly List<Point2d> _sa = new List<Point2d>();
        readonly List<Point2d> _sb = new List<Point2d>();
        Dictionary<long, List<int>> _grid;
        double _cell = 1.0;

        public double W { get { return MaxX - MinX; } }
        public double H { get { return MaxY - MinY; } }
        public double Diagonal { get { double w = W, h = H; return Math.Sqrt(w * w + h * h); } }

        public void AddRing(List<Point2d> pts) { AddRing(pts, true); }

        /// <summary>closed = false 用于「自由边/边界段」拼出来的距离查询区域（不闭合，不产生跨面的假线段）</summary>
        public void AddRing(List<Point2d> pts, bool closed)
        {
            if (pts == null || pts.Count < 2) return;
            var clean = new List<Point2d>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Point2d p = pts[i];
                if (!Ok(p)) continue;
                if (clean.Count > 0 && clean[clean.Count - 1].DistanceTo(p) < 1e-12) continue;
                clean.Add(p);
            }
            if (closed && clean.Count > 1 && clean[0].DistanceTo(clean[clean.Count - 1]) < 1e-12)
                clean.RemoveAt(clean.Count - 1);
            if (clean.Count < 2) return;
            Rings.Add(clean.ToArray());
            _closed.Add(closed);
            for (int i = 0; i < clean.Count; i++)
            {
                Point2d p = clean[i];
                if (p.X < MinX) MinX = p.X;
                if (p.Y < MinY) MinY = p.Y;
                if (p.X > MaxX) MaxX = p.X;
                if (p.Y > MaxY) MaxY = p.Y;
            }
        }

        static bool Ok(Point2d p)
        {
            return !double.IsNaN(p.X) && !double.IsNaN(p.Y) &&
                   !double.IsInfinity(p.X) && !double.IsInfinity(p.Y);
        }

        public void BuildGrid(double cell)
        {
            _cell = Math.Max(cell, 1e-6);
            _grid = new Dictionary<long, List<int>>();
            _sa.Clear();
            _sb.Clear();
            for (int r = 0; r < Rings.Count; r++)
            {
                Point2d[] ring = Rings[r];
                int n = _closed[r] ? ring.Length : ring.Length - 1;
                for (int i = 0; i < n; i++)
                {
                    Point2d a = ring[i];
                    Point2d b = ring[(i + 1) % ring.Length];
                    if (a.DistanceTo(b) < 1e-12) continue;
                    int idx = _sa.Count;
                    _sa.Add(a);
                    _sb.Add(b);
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
            if (len2 > 1e-20)
            {
                t = (wx * vx + wy * vy) / len2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
            }
            double dx = wx - t * vx, dy = wy - t * vy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public List<double> Crossings(Point2d o, Vector2d d)
        {
            var ts = new List<double>();
            const double eps = 1e-14;
            for (int i = 0; i < _sa.Count; i++)
            {
                Point2d a = _sa[i], b = _sb[i];
                double sa = d.X * (a.Y - o.Y) - d.Y * (a.X - o.X);
                double sb = d.X * (b.Y - o.Y) - d.Y * (b.X - o.X);
                if (Math.Abs(sa) < eps) sa = eps;
                if (Math.Abs(sb) < eps) sb = eps;
                if ((sa > 0) == (sb > 0)) continue;
                double ta = (a.X - o.X) * d.X + (a.Y - o.Y) * d.Y;
                double tb = (b.X - o.X) * d.X + (b.Y - o.Y) * d.Y;
                ts.Add(ta + (sa / (sa - sb)) * (tb - ta));
            }
            ts.Sort();
            return ts;
        }
    }

    public static class StripePattern
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
            if (ts == null || ts.Length == 0) return null;
            var pts = new Point3d[ts.Length];
            for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
            return pts;
        }

        // ==================================================================
        public static List<StripeFaceResult> Generate(Brep brep, StripeSettings settings, out string report)
        {
            var results = new List<StripeFaceResult>();
            var sb = new StringBuilder();
            if (brep == null) { report = "无有效几何体"; return results; }

            StripeSettings s = settings.Clone();
            s.Width = Math.Max(s.Width, 1e-4);
            s.Spacing = Math.Max(s.Spacing, 0.0);
            s.Margin = Math.Max(s.Margin, 0.0);
            if (!(s.MmToModel > 0)) s.MmToModel = 1.0;
            s.AngleDeg = ((s.AngleDeg % 180.0) + 180.0) % 180.0;
            s.Tol = Math.Max(s.Tol, 1e-5);

            // ---- 全局排版基准：多重曲面按「一个整体」排条纹
            // 取面积最大的平面面当参考面：方向 = 该面坐标系里 AngleDeg 对应的 3D 方向（所有面共用，
            // 各自投影到自己的切平面）；锚点 = 该面 2D 区域的中心（各面都从这族条纹里取覆盖自己的那些）。
            // 单个面时参考面就是它自己、锚点就是本面中心 —— 与老版本行为完全一致。
            StripeLayout layout = BuildLayout(brep, s);

            for (int fi = 0; fi < brep.Faces.Count; fi++)
            {
                StripeFaceResult r;
                try { r = GenerateFace(brep, brep.Faces[fi], fi, s, layout); }
                catch (Exception ex) { r = new StripeFaceResult { FaceIndex = fi, Note = "异常: " + ex.Message }; }
                if (r == null) r = new StripeFaceResult { FaceIndex = fi, Note = "无结果" };
                results.Add(r);
                sb.AppendFormat("面{0}[{1}] 条纹 {2} 条{3}{4}\r\n",
                    fi, r.Planar ? "平面" : "曲面",
                    r.Stripes.Count,
                    r.MinEdgeDistance < double.MaxValue ? string.Format(" 实测最小边距 {0:0.###}", r.MinEdgeDistance) : "",
                    string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note);
            }
            report = sb.ToString();
            return results;
        }

        // ================================================================== 全局排版基准
        /// <summary>多重曲面的全局排版基准：一个 3D 方向 + 一个 3D 锚点 + 参考面序号</summary>
        internal class StripeLayout
        {
            public int RefFace;
            public int FaceCount = 1;                 // 参考对象的面数（>1 时所有面都按全局整数格取条纹）
            public Vector3d Dir3 = Vector3d.XAxis;    // 全局条纹方向（3D，单位向量）
            public Point3d Anchor = Point3d.Origin;   // 全局相位锚点（3D，落在参考面上）
            public bool HasAnchor;
            // 参考面自己的 2D 工作平面（TryBuildFrame 出来的那个 frame）。
            // 多面对象时**所有面共用它**做 2D 投影：同一套 2D 坐标 -> 同一族条纹 -> 方向/相位一致、接缝接得上。
            public Plane RefPlane = Plane.Unset;
            // 等值线带（曲面面）用的**全局带法向**：落在参考平面内、垂直于条纹方向 Dir3
            // （= 2D 里的 nrm 方向写成 3D）。只由全局基准决定，不含任何本面信息 ——
            // 于是同一对象任意两面在公共接缝上 phi = (p-A)·n3d 逐点相等，条纹跨缝严格对齐。
            // （若改用「Dir3 × 本面法向」，法向随面变化，接缝处就对不上了。）
            public Vector3d BandNormal = Vector3d.Unset;
        }

        /// <summary>
        /// 定全局基准：参考面 = 面积最大的平面面（没有平面就取第一个面）；
        /// 方向 = 参考面坐标系里 AngleDeg 对应的 3D 方向；锚点 = 参考面 2D 区域的中心。
        /// </summary>
        static StripeLayout BuildLayout(Brep brep, StripeSettings s)
        {
            var L = new StripeLayout();
            if (brep == null || brep.Faces.Count == 0) return L;

            double u2m = s.MmToModel > 0 ? s.MmToModel : 1.0;
            double step = Math.Max(s.Tol * u2m, 1e-4);

            int best = 0; double bestArea = -1;
            for (int i = 0; i < brep.Faces.Count; i++)
            {
                Plane pl; bool planar = false;
                try { planar = brep.Faces[i].TryGetPlane(out pl, 1e-3) && pl.IsValid; } catch { planar = false; }
                if (!planar) continue;
                double a = 0;
                try
                {
                    Brep fb = brep.Faces[i].DuplicateFace(false);
                    if (fb != null)
                    {
                        var amp = AreaMassProperties.Compute(fb);
                        if (amp != null && amp.Area > 0) a = amp.Area;
                    }
                }
                catch { a = 0; }
                if (a > bestArea) { bestArea = a; best = i; }
            }
            L.RefFace = best;
            L.FaceCount = brep.Faces.Count;

            Plane frame; bool plan;
            if (!TryBuildFrame(brep.Faces[best], out frame, out plan) || !frame.IsValid) return L;
            L.RefPlane = frame;                       // 多面对象：所有面共用的 2D 工作平面

            double ang = s.AngleDeg * Math.PI / 180.0;
            Vector3d d = frame.XAxis * Math.Cos(ang) + frame.YAxis * Math.Sin(ang);
            if (d.IsValid && d.Unitize()) L.Dir3 = d;
            else L.Dir3 = frame.XAxis;

            // 全局带法向（等值线带用）：参考平面内、垂直于条纹方向 —— 就是 2D 里的 nrm 的 3D 版。
            // 由 Dir3 与参考平面法向叉乘再单位化；因为 Dir3 本来就在参考平面内，结果也严格在平面内。
            Vector3d nb = Vector3d.CrossProduct(frame.ZAxis, L.Dir3);
            if (nb.IsValid && nb.Unitize()) L.BandNormal = nb;

            try
            {
                Region2D r = BuildRegion(brep.Faces[best], frame, step, plan, 4000);
                if (r.Rings.Count > 0)
                {
                    var c2 = new Point2d((r.MinX + r.MaxX) * 0.5, (r.MinY + r.MaxY) * 0.5);
                    Point3d a3 = frame.PointAt(c2.X, c2.Y);
                    if (a3.IsValid) { L.Anchor = a3; L.HasAnchor = true; }
                }
            }
            catch { }
            if (!L.HasAnchor && frame.Origin.IsValid) { L.Anchor = frame.Origin; L.HasAnchor = true; }
            return L;
        }

        /// <summary>把全局 3D 方向投影到本面的切平面，再换算成本面 2D 方向（退化时返回 false）</summary>
        static bool GlobalDir2d(Plane frame, StripeLayout L, double ang, out Vector2d dir)
        {
            dir = new Vector2d(Math.Cos(ang), Math.Sin(ang));
            if (L == null || !L.Dir3.IsValid) return false;
            Vector3d n = frame.ZAxis;
            Vector3d t = L.Dir3 - n * Dot(L.Dir3, n);       // 减掉法向分量 = 投影到切平面
            if (!t.IsValid || !t.Unitize()) return false;
            if (t.Length < 0.2) return false;               // 几乎垂直于切平面：本面看不到这个方向，退回本面坐标系
            double cx = Dot(t, frame.XAxis), cy = Dot(t, frame.YAxis);
            double len = Math.Sqrt(cx * cx + cy * cy);
            if (!(len > 1e-9)) return false;
            dir = new Vector2d(cx / len, cy / len);
            return true;
        }

        /// <summary>
        /// 条纹带的法向偏移量（沿 nrm、相对锚点）。
        /// globalPhase = false：以锚点为中心对称排布（单面 / 参考面，与老版本一致）；
        /// globalPhase = true ：取「过全局锚点」的那一族里覆盖本面的那些 —— 与参考面条纹严格对齐。
        /// </summary>
        static List<double> BandOffsets(Region2D region, Point2d anchor, Vector2d nrm, double pitch,
                                        bool globalPhase, out bool truncated)
        {
            truncated = false;
            var list = new List<double>();
            if (!(pitch > 1e-12)) return list;

            double tmin = double.MaxValue, tmax = double.MinValue;
            double[] xs = { region.MinX, region.MaxX };
            double[] ys = { region.MinY, region.MaxY };
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    var c = new Point2d(xs[i], ys[j]);
                    double t = (c.X - anchor.X) * nrm.X + (c.Y - anchor.Y) * nrm.Y;
                    if (t < tmin) tmin = t;
                    if (t > tmax) tmax = t;
                }
            if (!(tmax > tmin)) return list;

            int n = (int)Math.Floor((tmax - tmin) / pitch) + 1;
            if (n < 1) n = 1;
            if (n > 6000) { n = 6000; truncated = true; }

            if (globalPhase)
            {
                // 覆盖本面的那一段「全局条带编号」：k 取遍 [floor(tmin/pitch), ceil(tmax/pitch)]
                // （这段长度天然 ≈ 本面能容纳的条带数，不会失控；上限只是保险）
                int kLo = (int)Math.Floor(tmin / pitch), kHi = (int)Math.Ceiling(tmax / pitch);
                if ((long)kHi - kLo + 1 > 6000)
                {
                    truncated = true;
                    int mid = (int)Math.Round(((tmin + tmax) * 0.5) / pitch);
                    kLo = mid - 3000;
                    kHi = mid + 2999;
                }
                for (int k = kLo; k <= kHi; k++) list.Add(k * pitch);
            }
            else
            {
                for (int i = 0; i < n; i++) list.Add((i - (n - 1) * 0.5) * pitch);
            }
            return list;
        }

        // ==================================================================
        static StripeFaceResult GenerateFace(Brep brep, BrepFace face, int index, StripeSettings s, StripeLayout layout)
        {
            var res = new StripeFaceResult { FaceIndex = index };

            // 2D 工作平面：
            //   多面对象（多重曲面）-> 所有面共用**全局参考平面**（layout.RefPlane）。同一个 2D 坐标系
            //     下，条纹在全局平面上是同一条直线族 —— 各面方向一致、相位一致、接缝处接得上。
            //     （倾斜面上的图案会被「整体投影」拉伸，这是这套做法的应有结果）
            //   单面对象 -> 本面自己的切平面（TryBuildFrame），行为与老版本完全一致。
            bool multi = layout != null && layout.FaceCount > 1 && layout.RefPlane.IsValid;
            bool globalOK = multi;        // 本面是否用「全局方向 + 全局相位」（垂直面回退时置 false，彻底回到老行为）
            Plane frame;
            bool planar;      // 本面自己的平面性（报告/提示用；不用全局平面的）
            bool planarMap;   // 2D -> 3D 的回投方式：true = 平面直投（本面就在 frame 平面里），false = ClosestPoint 回投到面
            if (multi)
            {
                frame = layout.RefPlane;
                try { planar = face.TryGetPlane(out Plane fp, 1e-3) && fp.IsValid; }
                catch { planar = false; }

                if (FaceNearlyPerpendicularTo(face, frame))
                {
                    // 面几乎垂直于参考平面：垂直投影退化成一条线（投影面积 -> 0，比如盒子的侧面），
                    // 本面退回自己的切平面，免得整面铺不出条纹。
                    if (!TryBuildFrame(face, out frame, out planar))
                    {
                        res.Note = "无法建立参考坐标系，跳过";
                        return res;
                    }
                    planarMap = planar;
                    globalOK = false;      // 方向与相位也回到本面自己的，否则全局锚点算出的条带区间会落在面外
                    res.Note = "面几乎垂直于参考平面：按本面切平面生成";
                }
                else
                {
                    // 只有「本面就落在参考平面里」（参考面自己 / 与它共面的面片）才敢平面直投；
                    // 倾斜的面必须 ClosestPoint 回投到面上，否则条纹会留在参考平面里、脱离曲面。
                    planarMap = planar && (index == layout.RefFace || FaceLiesInFramePlane(face, frame));
                }
            }
            else
            {
                if (!TryBuildFrame(face, out frame, out planar))
                {
                    res.Note = "无法建立参考坐标系，跳过";
                    return res;
                }
                planarMap = planar;
            }
            res.Planar = planar;
            res.Plane = frame;

            double u2m = s.MmToModel;
            double w = s.Width * u2m;
            double gap = s.Spacing * u2m;
            double margin = s.Margin * u2m;
            double pitch = w + gap;
            if (pitch <= 1e-12 || w <= 1e-12)
            {
                res.Note = "宽度/间距无效";
                return res;
            }

            double step = Math.Max(s.Tol * u2m, 1e-4);
            Region2D region = BuildRegion(face, frame, step, planarMap, 4000);
            if (region.Rings.Count == 0)
            {
                res.Note = "无有效边界";
                return res;
            }

            double cornerR = s.CornerRadius * u2m;
            // 端部形状决定需要预留的半宽
            double rEff = margin + (s.RoundedEnds ? w * 0.5 : w * 0.70710678118654752);
            double cell = s.ConformToBoundary
                ? Math.Max(region.Diagonal / 512.0, Math.Max(w * 0.25, 1e-4))
                : Math.Max(rEff, Math.Max(region.Diagonal / 512.0, 1e-4));
            region.BuildGrid(cell);

            // 留边距用的边界：把「顺接的内部接缝」排除掉 —— 多重曲面上条纹跨过接缝连成一片，
            // 只在折痕 / 实体棱边 / 外轮廓处留边距。取不到时退回整面边界（单面输入 = 老行为）。
            Region2D marginRegion = BuildEdgeRegion(face, brep, frame, step, planarMap, 4000, false);
            if (marginRegion.Rings.Count == 0) marginRegion = region;
            marginRegion.BuildGrid(cell);
            // 顺接接缝单独一份：圆头端不能越过接缝（端部圆心压在接缝上，圆头正好顶到缝），
            // 这样接缝两侧的两段端头严丝合缝地接上，不会互相压过去。
            Region2D seamRegion = BuildEdgeRegion(face, brep, frame, step, planarMap, 4000, true);
            seamRegion.BuildGrid(cell);

            // 条纹方向：0° = 参考坐标系的 X 轴 = 世界 X 在面内的投影。
            // 多重曲面时用**全局方向**在本面切平面上的投影 —— 所有面方向一致，条纹才对得上。
            double ang = s.AngleDeg * Math.PI / 180.0;
            Vector2d dir;
            if (!globalOK || !GlobalDir2d(frame, layout, ang, out dir))
                dir = new Vector2d(Math.Cos(ang), Math.Sin(ang));   // 单面 / 垂直面回退：本面坐标系（与老版本一致）
            var nrm = new Vector2d(-dir.Y, dir.X);
            // 圆头端要「顶到缝、但不过缝」：端头圆心离缝的预留量 = (w/2)·|条纹方向与缝法向的夹角余弦|。
            // 斜着撞缝时按 w/2 预留会让圆头越过缝（0.707·w/2 那种），按投影算才正好顶到缝上。
            double endClear = 0.0;
            if (s.RoundedEnds && !s.ConformToBoundary)
            {
                endClear = w * 0.5;
                double cosA = SeamNormalDot(seamRegion, dir);
                if (cosA > 1e-6) endClear = w * 0.5 * cosA;
                else if (seamRegion.Rings.Count == 0) endClear = 0.0;      // 没有顺接接缝：不需要预留
            }

            // 相位锚点：多重曲面的其它面用全局锚点（与参考面是同一族条纹，接缝处条条对齐）；
            // 参考面 / 单面就是本面区域中心（与老版本完全一致）。
            var center = new Point2d((region.MinX + region.MaxX) * 0.5, (region.MinY + region.MaxY) * 0.5);
            Point2d bandAnchor = center;
            // 多重曲面（>1 个面）：**所有面**都按「过全局锚点的整数格」取条纹。
            // 参考面以前用「以自己区域中心对称排布」，条带数是偶数时它落在半格上，
            // 与邻面差半个间距（自检实测端头错位 2.06mm）—— 统一成整数格后两面严丝合缝。
            // 单面对象仍然用老的对称排布（与老版本逐点一致）。
            bool globalPhase = globalOK && layout != null && layout.HasAnchor;
            if (globalPhase)
            {
                Point2d a2 = To2d(frame, layout.Anchor);
                if (!double.IsNaN(a2.X) && !double.IsNaN(a2.Y)) bandAnchor = a2;
                else globalPhase = false;
            }
            res.DiagNormal = new Vector3d(
                frame.XAxis.X * nrm.X + frame.YAxis.X * nrm.Y,
                frame.XAxis.Y * nrm.X + frame.YAxis.Y * nrm.Y,
                frame.XAxis.Z * nrm.X + frame.YAxis.Z * nrm.Y);
            res.DiagNormal.Unitize();

            bool truncated;
            List<double> offsets = BandOffsets(region, bandAnchor, nrm, pitch, globalPhase, out truncated);
            if (truncated) res.Note = "条纹数超上限，已截断";
            int count = offsets.Count;
            if (count == 0)
            {
                if (res.Note.Length == 0) res.Note = "边距过大或区域过小，未生成条纹";
                return res;
            }

            if (s.ConformToBoundary)
            {
                Region2D clip = BuildRegion(face, frame, step, planarMap, 320);
                clip.BuildGrid(Math.Max(clip.Diagonal / 512.0, 1e-4));
                GenerateConform(res, region, clip, marginRegion, frame, face, planarMap,
                    bandAnchor, dir, nrm, offsets, w, margin, pitch, s.CornerRadius * u2m, s.Tol * u2m);
                if (res.Stripes.Count == 0 && res.Note.Length == 0)
                    res.Note = "贴合模式：边距过大或区域过小，未生成条纹";
                res.MinEdgeDistance = MeasureMinDistance(marginRegion, res, frame);
                return res;
            }

            // ---- 多面对象上的**曲面面**：改走「等值线带」（3D 平行板 ∩ 曲面），不再用投影排版。
            //      触发条件与老路径里 use3dMargin 完全一致（多面 + 全局基准可用 + 本面不是平面）：
            //      · 单面对象（layout.FaceCount == 1）        -> 不走这里，行为逐点不变；
            //      · 平面面 / 与参考面共面的面（planar）      -> 不走这里，解析圆弧口径不变；
            //      · 垂直面回退（globalOK == false）          -> 不走这里，回退行为不变；
            //      · 拿不到全局锚点 / 带法向无效（返回 null） -> 落回下面老的投影路径。
            if (globalOK && !planar)
            {
                StripeFaceResult lvl = GenerateLevelSetFace(face, frame, planarMap, region, marginRegion, layout,
                    w, pitch, margin, s.Tol * u2m);
                if (lvl != null)
                {
                    lvl.FaceIndex = index;
                    lvl.Planar = planar;
                    lvl.Plane = frame;
                    return lvl;
                }
            }

            // 多面（共享参考平面）+ 曲面时：边距/接缝改用 3D 判定（投影自相交会误杀条纹）
            bool use3dMargin = globalOK && !planar;
            Border3d border3d = use3dMargin ? BuildBorder3d(brep, face) : null;
            double halfLen = region.Diagonal * 0.5 + 2.0 * (pitch + margin + w) + 1.0;
            double sampleStep = Math.Max(Math.Min(w, Math.Max(rEff, 1e-6)), region.Diagonal / 4000.0);
            if (sampleStep <= 0) sampleStep = region.Diagonal / 1000.0;

            int made = 0;
            for (int i = 0; i < count; i++)
            {
                double off = offsets[i];
                var o = new Point2d(bandAnchor.X + nrm.X * off, bandAnchor.Y + nrm.Y * off);
                Func<double, Point3d> to3d = null;
                if (use3dMargin)
                {
                    Point2d oo = o;
                    to3d = delegate (double t)
                    {
                        var q = new Point2d(oo.X + dir.X * t, oo.Y + dir.Y * t);
                        if (planarMap) return frame.PointAt(q.X, q.Y);
                        Point3d P0 = frame.PointAt(q.X, q.Y);
                        double u, v;
                        if (face.ClosestPoint(P0, out u, out v)) return face.PointAt(u, v);
                        return Point3d.Unset;
                    };
                }

                List<double> ts = region.Crossings(o, dir);
                if (ts.Count < 2) continue;

                for (int k = 0; k + 1 < ts.Count; k += 2)
                {
                    double a = Math.Max(ts[k], -halfLen);
                    double b = Math.Min(ts[k + 1], halfLen);
                    if (b - a <= 1e-9) continue;

                    foreach (var seg in GoodRuns(marginRegion, seamRegion, endClear, o, dir, a, b, rEff, sampleStep,
                                                 use3dMargin ? (Func<double, Point3d>)to3d : null, use3dMargin ? border3d : null))
                    {
                        Curve c;
                        if (!s.RoundedEnds && cornerR > 1e-9)
                        {
                            // 平齐端 + 倒圆角：四条直角边倒圆（保留解析圆弧，输出真曲线）
                            List<Point2d> pts = StadiumPoints(seg.Item1, seg.Item2, w, false, 4);
                            List<Seg2d> csegs = RoundCornersSegs(pts, cornerR, Math.Max(cornerR * 0.25, region.Diagonal / 900.0));
                            c = MakeOutlineCurve(csegs, frame, face, planarMap, s.Tol * u2m);
                        }
                        else
                        {
                            c = planarMap
                                ? MakeStadiumPlanar(frame, seg.Item1, seg.Item2, w, s.RoundedEnds)
                                : MakeStadiumOnSurface(face, frame, seg.Item1, seg.Item2, w, s.RoundedEnds, s.Tol * u2m);
                        }
                        if (c != null) { res.Stripes.Add(c); made++; }
                    }
                }
            }

            if (made == 0 && res.Note.Length == 0)
                res.Note = (!planar && FaceWraps(brep, face))
                    ? "整圈环绕的曲面：条纹在这个面上铺不开，请把面裁成一段（例如 120° 弧片）再生成，或改用「贴合边界」模式"
                    : "边距过大或区域过小，未生成条纹";
            res.MinEdgeDistance = MeasureMinDistance(marginRegion, res, frame);
            return res;
        }

        /// <summary>
        /// 整圈环绕的面：有一条「自己对自己」的接缝边（圆柱整个侧面的那条竖缝）。
        /// 这种面用切平面投影会自己叠起来 —— 普通模式铺不出条纹，得明确告诉用户。
        /// （不用 uv 环是否闭合来判：整圈面的 uv 修剪环是闭合矩形，判不出来）
        /// </summary>
        static bool FaceWraps(Brep brep, BrepFace face)
        {
            try
            {
                if (brep == null) return false;
                int[] eis = face.AdjacentEdges();
                if (eis == null) return false;
                foreach (int ei in eis)
                {
                    if (ei < 0 || ei >= brep.Edges.Count) continue;
                    int[] af = brep.Edges[ei].AdjacentFaces();
                    if (af != null && af.Length >= 2 && af[0] == af[1]) return true;
                }
            }
            catch { }
            return false;
        }

        // ================================================================== 等值线带（多面对象上的曲面面）
        /// <summary>等值线带的栅格顶点预算：超了按比例放大步长（保证单面 1-3 秒出结果，并在 Note 里说明）</summary>
        const int LevelSetMaxVerts = 320000;

        /// <summary>
        /// 等值线带（level-set band）：多面对象上的**曲面面**专用，替代「投影排版」。
        ///
        /// 第 k 条带 = 面上满足 |(p - A)·n3d - k*pitch| &lt;= w/2 的点集
        /// （A = 全局锚点 layout.Anchor，n3d = 全局带法向 layout.BandNormal，pitch = w + 净间距）。
        /// 与投影排版的本质区别：带是 3D 空间里一族平行板与曲面的交，宽度按真实 3D 距离度量，
        /// 不随面相对参考平面的倾斜被拉伸；而 phi = (p-A)·n3d 只由全局基准决定，
        /// 任意两面在公共接缝上逐点相等 —— 所以条纹跨缝对齐、整壳上是一个整体。
        ///
        /// 实现（栅格 + marching squares，无拓扑噩梦）：
        ///  1) 在共享参考平面的 2D 空间里，按本面区域的包围盒铺栅格：h = max(w/4, 对角/900)，
        ///     900x900 格硬上限，再套顶点预算（超了按比例放大步长）；
        ///  2) 每个格点：逐行扫描线奇偶判定「面内」（等价 region.Inside，但一次扫描一整行，快几十倍），
        ///     2D 里量到 marginRegion（非顺接边界：折痕/棱边/外轮廓）的距离，顺接接缝不在其中 ——
        ///     图案要跨过接缝连成一片；再按现有映射（planarMap ? frame.PointAt : ClosestPoint 回投）
        ///     把 2D 点映射成 3D 点 P3，算 phi = (P3 - A)·n3d；
        ///  3) 标量场 G = min(定义域余量, 带内余量)：定义域余量 = 面内符号距离与非顺接边界余量的较小者，
        ///     带内余量 = w/2 - |phi - k*pitch|（k = round(phi/pitch)，条带编号范围由本面 phi 极值决定，
        ///     上限 3000 条）。三个都是距离型场，沿格边线性插值定交点 -> 边界位置误差 O(h²)（远好于半格）；
        ///  4) marching squares：每格按四角符号（>0 = 带内）提有向边段（带内区域恒在左侧），
        ///     交点按格边规范化编号（同一条格边两侧的格共用同一个交点），首尾相接成**闭合环**
        ///     （鞍点用四角均值判定连通性）；一个带可以产出多个环；
        ///  5) 每个环 -> MakeOutlineCurve(PolylineSegs(ring), frame, face, planarMap, tol)：与现有口径一致，
        ///     曲面上是回投到面的真曲线，平面上是解析段。
        ///
        /// 返回 null = 不适用（无全局锚点 / 带法向无效 / 区域退化），调用方继续走老的投影路径。
        /// </summary>
        static StripeFaceResult GenerateLevelSetFace(BrepFace face, Plane frame, bool planarMap, Region2D region,
            Region2D marginRegion, StripeLayout layout,
            double w, double pitch, double margin, double tol)
        {
            if (face == null || region == null || layout == null) return null;
            if (!layout.HasAnchor || !layout.BandNormal.IsValid) return null;
            Point3d A = layout.Anchor;
            Vector3d n3d = layout.BandNormal;
            if (!n3d.IsValid || !n3d.Unitize()) return null;
            double half = w * 0.5;
            if (!(half > 0) || !(pitch > 1e-12)) return null;

            double diag = region.Diagonal;
            double W = region.MaxX - region.MinX, H = region.MaxY - region.MinY;
            if (!(diag > 1e-12) || !(W > 0) || !(H > 0)) return null;

            var res = new StripeFaceResult();

            // ---- 1) 栅格：h = max(w/4, 对角/900) -> 900x900 硬上限 -> 顶点预算
            double h = Math.Max(half * 0.5, diag / 900.0);
            if (!(h > 1e-12)) h = Math.Max(diag / 900.0, 1e-6);
            bool coarse = false;
            int nx = (int)Math.Ceiling(W / h) + 1;
            int ny = (int)Math.Ceiling(H / h) + 1;
            if (nx > 900 || ny > 900)
            {
                h = Math.Max(W, H) / 899.0;
                nx = (int)Math.Ceiling(W / h) + 1;
                ny = (int)Math.Ceiling(H / h) + 1;
                coarse = true;
            }
            long vcount = (long)(nx + 1) * (ny + 1);
            if (vcount > LevelSetMaxVerts)
            {
                h *= Math.Sqrt(vcount / (double)LevelSetMaxVerts) * 1.02;
                nx = (int)Math.Ceiling(W / h) + 1;
                ny = (int)Math.Ceiling(H / h) + 1;
                coarse = true;
            }
            if (nx < 2) nx = 2;
            if (ny < 2) ny = 2;
            if (nx > 900 || ny > 900) return null;                     // 兜底：绝不越过硬上限
            int stride = nx + 1;

            double x0 = region.MinX, y0 = region.MinY;
            double capR = h * 2.5 + 1e-9;                  // 距离查询半径：覆盖「跨格边插值」用到的邻域
            double edgeR = Math.Max(margin, 0.0) + capR;
            bool hasMarginRegion = marginRegion != null && marginRegion.Rings.Count > 0;

            var g = new double[stride * (ny + 1)];         // 标量场（> 0 = 带内）
            var phi = new double[stride * (ny + 1)];       // 算过 phi 的格点（= 可能落在带内的点）
            var hasPhi = new bool[stride * (ny + 1)];
            double phiMin = double.MaxValue, phiMax = double.MinValue;

            // ---- 2) 逐行扫描：面内（奇偶） + 距离场 + phi
            var rowX = new List<double>();
            for (int j = 0; j <= ny; j++)
            {
                double y = y0 + j * h;
                rowX.Clear();
                try { rowX.AddRange(region.Crossings(new Point2d(x0 - (W + 1.0), y), new Vector2d(1, 0))); }
                catch { }
                double ox = x0 - (W + 1.0);
                int ptr = 0; bool inside = false;
                for (int i = 0; i <= nx; i++)
                {
                    double x = x0 + i * h;
                    while (ptr < rowX.Count && ox + rowX[ptr] <= x) { inside = !inside; ptr++; }
                    int idx = i + j * stride;

                    double dReg = region.Distance(new Point2d(x, y), capR);   // 到面边界（trim 环）的距离
                    if (dReg == double.MaxValue) dReg = capR;
                    if (!inside)
                    {
                        // 面外：G < 0，量级取到边界的距离 —— 跨边插值出的交点才落在真正的边界上
                        g[idx] = -dReg;
                        continue;
                    }
                    double s = dReg;                                          // 「带定义域」的符号距离
                    if (margin > 1e-12 && hasMarginRegion)
                    {
                        double dEdge = marginRegion.Distance(new Point2d(x, y), edgeR);
                        if (dEdge == double.MaxValue) dEdge = edgeR;
                        double se = dEdge - margin;                           // 距非顺接边界的余量（顺接接缝不在此列）
                        if (se < s) s = se;
                    }
                    if (s < 0) { g[idx] = s; continue; }                      // 太靠边界：直接判外

                    Point3d P3 = Point3d.Unset;
                    if (planarMap) P3 = frame.PointAt(x, y);
                    else
                    {
                        double u, v;
                        if (face.ClosestPoint(frame.PointAt(x, y), out u, out v)) P3 = face.PointAt(u, v);
                    }
                    if (!P3.IsValid) { g[idx] = -Math.Max(dReg, 1e-9); continue; }
                    double ph = Dot(P3 - A, n3d);
                    phi[idx] = ph; hasPhi[idx] = true; g[idx] = s;
                    if (ph < phiMin) phiMin = ph;
                    if (ph > phiMax) phiMax = ph;
                }
            }

            // ---- 3) 条带编号范围（由本面 phi 极值决定，条数上限 3000）+ 合成标量场
            long kLo = 0, kHi = -1;
            bool capped = false;
            if (phiMax >= phiMin)
            {
                double klo = Math.Floor((phiMin - half) / pitch);
                double khi = Math.Ceiling((phiMax + half) / pitch);
                if (khi - klo + 1.0 > 3000.0)
                {
                    double mid = Math.Round(((phiMin + phiMax) * 0.5) / pitch);
                    klo = mid - 1500.0; khi = mid + 1499.0;
                    capped = true;
                }
                if (klo < -9.0e15) klo = -9.0e15;
                if (khi > 9.0e15) khi = 9.0e15;
                kLo = (long)klo; kHi = (long)khi;
            }
            var bands = new HashSet<long>();
            if (kHi >= kLo)
            {
                for (int idx = 0; idx < g.Length; idx++)
                {
                    if (!hasPhi[idx]) continue;
                    double ph = phi[idx];
                    double kd = Math.Round(ph / pitch);
                    if (kd < (double)kLo || kd > (double)kHi) { g[idx] = -1e-9; continue; }   // 被截断掉的带
                    double bandField = half - Math.Abs(ph - kd * pitch);
                    if (bandField < g[idx]) g[idx] = bandField;      // G = min(定义域余量, 带内余量)
                    if (bandField > 0) bands.Add((long)kd - kLo);    // 本面真正出现过的带（相对编号，防溢出）
                }
            }

            // ---- 4) marching squares：G 的零等值线 -> 闭合边界环
            //      格边交点按「规范化格边编号」建一次、两侧共用（拼接因此是精确的整数匹配，不是浮点比对）。
            var hIdx = new int[nx * (ny + 1)];             // 水平格边 (i,j)：格点 (i,j)-(i+1,j)
            var vIdx = new int[(nx + 1) * ny];             // 垂直格边 (i,j)：格点 (i,j)-(i,j+1)
            var pts = new List<Point2d>();
            var nextOf = new List<int>();                  // 每个交点的后继（有向边段：带内在左侧）
            var used = new List<bool>();

            Func<int, int, int> hCross = delegate (int i, int j)
            {
                int e = i + j * nx;
                int id = hIdx[e];
                if (id > 0) return id - 1;
                int ia = i + j * stride, ib = ia + 1;
                double ga = g[ia], gb = g[ib];
                double t = 0.5;
                if (Math.Abs(gb - ga) > 1e-300) t = ga / (ga - gb);
                if (!(t > 0)) t = 0; else if (t > 1) t = 1;
                pts.Add(new Point2d(x0 + (i + t) * h, y0 + j * h));
                nextOf.Add(-1); used.Add(false);
                hIdx[e] = pts.Count;                       // 存「索引 + 1」，0 = 还没建
                return pts.Count - 1;
            };
            Func<int, int, int> vCross = delegate (int i, int j)
            {
                int e = i + j * (nx + 1);
                int id = vIdx[e];
                if (id > 0) return id - 1;
                int ia = i + j * stride, ib = ia + stride;
                double ga = g[ia], gb = g[ib];
                double t = 0.5;
                if (Math.Abs(gb - ga) > 1e-300) t = ga / (ga - gb);
                if (!(t > 0)) t = 0; else if (t > 1) t = 1;
                pts.Add(new Point2d(x0 + i * h, y0 + (j + t) * h));
                nextOf.Add(-1); used.Add(false);
                vIdx[e] = pts.Count;
                return pts.Count - 1;
            };

            for (int cj = 0; cj < ny; cj++)
            {
                for (int ci = 0; ci < nx; ci++)
                {
                    int i0 = ci + cj * stride;
                    double g0 = g[i0];                     // 左下
                    double g1 = g[i0 + 1];                 // 右下
                    double g2 = g[i0 + 1 + stride];        // 右上
                    double g3 = g[i0 + stride];            // 左上
                    bool s0 = g0 > 0, s1 = g1 > 0, s2 = g2 > 0, s3 = g3 > 0;
                    int code = (s0 ? 1 : 0) | (s1 ? 2 : 0) | (s2 ? 4 : 0) | (s3 ? 8 : 0);
                    if (code == 0 || code == 15) continue;
                    switch (code)
                    {
                        case 1: nextOf[hCross(ci, cj)] = vCross(ci, cj); break;               // 只左下在内
                        case 2: nextOf[vCross(ci + 1, cj)] = hCross(ci, cj); break;           // 只右下在内
                        case 3: nextOf[vCross(ci + 1, cj)] = vCross(ci, cj); break;           // 下边两个在内
                        case 4: nextOf[hCross(ci, cj + 1)] = vCross(ci + 1, cj); break;       // 只右上在内
                        case 6: nextOf[hCross(ci, cj + 1)] = hCross(ci, cj); break;           // 右边两个在内
                        case 7: nextOf[hCross(ci, cj + 1)] = vCross(ci, cj); break;           // 除左上在内
                        case 8: nextOf[vCross(ci, cj)] = hCross(ci, cj + 1); break;           // 只左上在内
                        case 9: nextOf[hCross(ci, cj)] = hCross(ci, cj + 1); break;           // 左边两个在内
                        case 11: nextOf[vCross(ci + 1, cj)] = hCross(ci, cj + 1); break;      // 除右上在内
                        case 12: nextOf[vCross(ci, cj)] = vCross(ci + 1, cj); break;          // 上边两个在内
                        case 13: nextOf[hCross(ci, cj)] = vCross(ci + 1, cj); break;          // 除右下在内
                        case 14: nextOf[vCross(ci, cj)] = hCross(ci, cj); break;              // 除左下在内
                        case 5:                                                               // 左下+右上（鞍点）
                        case 10:                                                              // 右下+左上（鞍点）
                            {
                                bool centerIn = (g0 + g1 + g2 + g3) > 0;
                                if (code == 5)
                                {
                                    if (centerIn) { nextOf[hCross(ci, cj)] = vCross(ci + 1, cj); nextOf[hCross(ci, cj + 1)] = vCross(ci, cj); }
                                    else { nextOf[hCross(ci, cj)] = vCross(ci, cj); nextOf[hCross(ci, cj + 1)] = vCross(ci + 1, cj); }
                                }
                                else
                                {
                                    if (centerIn) { nextOf[vCross(ci + 1, cj)] = hCross(ci, cj + 1); nextOf[vCross(ci, cj)] = hCross(ci, cj); }
                                    else { nextOf[vCross(ci + 1, cj)] = hCross(ci, cj); nextOf[vCross(ci, cj)] = hCross(ci, cj + 1); }
                                }
                                break;
                            }
                    }
                }
            }

            // 首尾相接成环 -> 真曲线（口径与贴合模式一致：PolylineSegs + MakeOutlineCurve）
            int made = 0;
            for (int st = 0; st < pts.Count; st++)
            {
                if (used[st] || nextOf[st] < 0) continue;
                var ring = new List<Point2d>();
                int cur = st;
                int guard = pts.Count + 8;
                while (guard-- > 0)
                {
                    if (used[cur]) break;
                    used[cur] = true;
                    ring.Add(pts[cur]);
                    int nx2 = nextOf[cur];
                    if (nx2 < 0 || nx2 == st) break;
                    cur = nx2;
                }
                if (ring.Count < 3) continue;                                  // 环点数太少：跳过
                List<Seg2d> psegs = PolylineSegs(ring);
                Curve c = MakeOutlineCurve(psegs, frame, face, planarMap, tol);
                if (c != null) { res.Stripes.Add(c); made++; }
            }

            res.DiagNormal = n3d;
            res.Note = string.Format("等值线带：{0} 条带 / {1} 个环", bands.Count, made);
            if (capped) res.Note += "，条数超上限已截断";
            if (coarse) res.Note += string.Format("，栅格已放大(h={0:0.###})", h);
            if (made == 0) res.Note += "，边距过大或区域过小";
            res.MinEdgeDistance = MeasureMinDistance(marginRegion, res, frame);
            return res;
        }

        // ================================================================== 贴合边界模式
        // 条纹 = 长条带 ∩ 内缩区域（边界按 margin 内缩）。端部随边界形状切割，
        // 边缘距离 margin 控制条纹距面边缘的距离（0 = 一直铺到边界）。
        static void GenerateConform(StripeFaceResult res, Region2D region, Region2D clip, Region2D fine,
            Plane frame, BrepFace face, bool planar,
            Point2d bandAnchor, Vector2d dir, Vector2d nrm, List<double> offsets, double w, double margin, double pitch,
            double cornerR, double tol)
        {
            double half = w * 0.5;
            double searchR = margin > 1e-12 ? margin * 1.001 + 1e-9 : 1e-9;

            // 扫描范围：区域包围盒在 dir 上的投影（条带在参数 t 处的投影恰好是 t）
            double tLo = double.MaxValue, tHi = double.MinValue;
            double[] xs = { region.MinX, region.MaxX };
            double[] ys = { region.MinY, region.MaxY };
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    var c = new Point2d(xs[i], ys[j]);
                    double t = (c.X - bandAnchor.X) * dir.X + (c.Y - bandAnchor.Y) * dir.Y;
                    if (t < tLo) tLo = t;
                    if (t > tHi) tHi = t;
                }
            if (tHi - tLo < 1e-9) return;

            double diag = region.Diagonal;
            double coarse = Math.Max(diag / 160.0, Math.Max(w, 1e-4));

            int made = 0;
            for (int i = 0; i < offsets.Count; i++)
            {
                double off = offsets[i];
                var o = new Point2d(bandAnchor.X + nrm.X * off, bandAnchor.Y + nrm.Y * off);
                List<List<Point2d>> polys = ClipBand(fine, clip, o, dir, nrm, half, margin, searchR, tLo, tHi, coarse);
                for (int k = 0; k < polys.Count; k++)
                {
                    List<Point2d> poly = polys[k];
                    // 保留解析圆弧的段列表：圆角走 RoundCornersSegs，其余保持折线口径不变
                    List<Seg2d> psegs = cornerR > 1e-9
                        ? RoundCornersSegs(poly, cornerR, Math.Max(cornerR * 0.25, diag / 900.0))
                        : PolylineSegs(poly);
                    Curve c = MakeOutlineCurve(psegs, frame, face, planar, tol);
                    if (c != null) { res.Stripes.Add(c); made++; }
                }
            }
            if (made > 0) res.Note = "";
        }

        /// <summary>条带（沿 dir 无限长、宽 w）与内缩区域的交：按 t 扫描，逐处求有效 s 区间</summary>
        static List<List<Point2d>> ClipBand(Region2D fine, Region2D clip, Point2d o, Vector2d dir, Vector2d nrm,
            double half, double margin, double searchR, double tLo, double tHi, double coarse)
        {
            var outPolys = new List<List<Point2d>>();
            int nc = (int)Math.Ceiling((tHi - tLo) / Math.Max(coarse, 1e-9));
            if (nc < 4) nc = 4;
            if (nc > 240) nc = 240;
            double cdt = (tHi - tLo) / nc;

            // 第一遍（粗）：找出条带上连续有效的 t 段
            var runs = new List<List<double[]>>();
            List<double[]> cur = null;
            double curLo = 0, curHi = 0;
            for (int k = 0; k <= nc; k++)
            {
                double t = tLo + cdt * k;
                List<double[]> ivs = IntervalsAt(fine, clip, Pt(o, dir, t), nrm, half, margin, searchR);
                double[] pick = null;
                if (cur != null)
                {
                    double bestOv = -1e-9;
                    for (int q = 0; q < ivs.Count; q++)
                    {
                        double ov = Math.Min(ivs[q][1], curHi) - Math.Max(ivs[q][0], curLo);
                        if (ov > bestOv) { bestOv = ov; pick = ivs[q]; }
                    }
                }
                else pick = PickBest(ivs);

                if (pick != null)
                {
                    if (cur == null) { cur = new List<double[]>(); runs.Add(cur); }
                    cur.Add(new double[] { t, pick[0], pick[1] });
                    curLo = pick[0]; curHi = pick[1];
                }
                else cur = null;
            }

            // 第二遍（细）：每段内细化端部并采样成多边形
            for (int ri = 0; ri < runs.Count; ri++)
            {
                List<double[]> run = runs[ri];
                if (run.Count < 2) continue;
                double ta = run[0][0], tb = run[run.Count - 1][0];
                double ta2 = RefineEnd(fine, clip, o, dir, nrm, half, margin, searchR, ta, ta - cdt);
                double tb2 = RefineEnd(fine, clip, o, dir, nrm, half, margin, searchR, tb, tb + cdt);
                if (tb2 - ta2 < 1e-9) continue;

                int m = 56;
                var chain = new List<double[]>();
                double prevLo = 0, prevHi = 0;
                bool has = false;
                for (int k = 0; k <= m; k++)
                {
                    double t = ta2 + (tb2 - ta2) * k / (double)m;
                    List<double[]> ivs = IntervalsAt(fine, clip, Pt(o, dir, t), nrm, half, margin, searchR);
                    double[] pick = null;
                    if (has)
                    {
                        double bestOv = -1e-9;
                        for (int q = 0; q < ivs.Count; q++)
                        {
                            double ov = Math.Min(ivs[q][1], prevHi) - Math.Max(ivs[q][0], prevLo);
                            if (ov > bestOv) { bestOv = ov; pick = ivs[q]; }
                        }
                    }
                    if (pick == null) pick = PickBest(ivs);
                    if (pick == null) continue;
                    chain.Add(new double[] { t, pick[0], pick[1] });
                    prevLo = pick[0]; prevHi = pick[1]; has = true;
                }
                if (chain.Count < 2) continue;

                var poly = new List<Point2d>(chain.Count * 2);
                for (int k = 0; k < chain.Count; k++)
                    poly.Add(At(o, dir, chain[k][0], nrm, chain[k][2]));
                for (int k = chain.Count - 1; k >= 0; k--)
                    poly.Add(At(o, dir, chain[k][0], nrm, chain[k][1]));
                outPolys.Add(poly);
            }
            return outPolys;
        }

        /// <summary>固定 t 处，条带在 s 方向上的所有有效区间（在区域内且距边界 >= margin）</summary>
        static List<double[]> IntervalsAt(Region2D fine, Region2D clip, Point2d basePt, Vector2d nrm,
            double half, double margin, double searchR)
        {
            var res = new List<double[]>();
            List<double> ts = clip.Crossings(basePt, nrm);
            for (int k = 0; k + 1 < ts.Count; k += 2)
            {
                double c0 = ts[k], c1 = ts[k + 1];
                if (c1 - c0 < 1e-12) continue;
                double d0 = c0, d1 = c1;
                if (margin > 1e-12 && !ShrinkToMargin(fine, basePt, nrm, c0, c1, margin, searchR, out d0, out d1))
                    continue;
                double lo = Math.Max(d0, -half);
                double hi = Math.Min(d1, half);
                if (hi - lo < 1e-9) continue;
                res.Add(new double[] { lo, hi });
            }
            return res;
        }

        /// <summary>把区间 [c0,c1] 收缩到距边界 >= margin 的子区间；无则返回 false</summary>
        static bool ShrinkToMargin(Region2D fine, Point2d basePt, Vector2d nrm, double c0, double c1,
            double margin, double searchR, out double d0, out double d1)
        {
            d0 = c0; d1 = c1;
            const int N = 8;
            double bestS = c0, bestD = -1;
            for (int i = 0; i <= N; i++)
            {
                double s = c0 + (c1 - c0) * i / (double)N;
                double d = fine.Distance(At(basePt, nrm, s), searchR);
                if (d > bestD) { bestD = d; bestS = s; }
            }
            if (bestD < margin) return false;
            d0 = BisectDist(fine, basePt, nrm, c0, bestS, margin, searchR);
            d1 = BisectDist(fine, basePt, nrm, c1, bestS, margin, searchR);
            return d1 - d0 > 1e-12;
        }

        /// <summary>在 [badS, goodS] 之间二分求 dist == margin 的位置</summary>
        static double BisectDist(Region2D fine, Point2d basePt, Vector2d nrm, double badS, double goodS,
            double margin, double searchR)
        {
            for (int i = 0; i < 20; i++)
            {
                double mid = 0.5 * (badS + goodS);
                double d = fine.Distance(At(basePt, nrm, mid), searchR);
                if (d >= margin) goodS = mid; else badS = mid;
            }
            return goodS;
        }

        /// <summary>沿 t 方向二分细化条带端部（tGood 有效，tBad 无效）</summary>
        static double RefineEnd(Region2D fine, Region2D clip, Point2d o, Vector2d dir, Vector2d nrm,
            double half, double margin, double searchR, double tGood, double tBad)
        {
            if (Math.Abs(tBad - tGood) < 1e-12) return tGood;
            double good = tGood, bad = tBad;
            for (int i = 0; i < 14; i++)
            {
                double mid = 0.5 * (good + bad);
                List<double[]> ivs = IntervalsAt(fine, clip, Pt(o, dir, mid), nrm, half, margin, searchR);
                if (ivs.Count > 0) good = mid; else bad = mid;
            }
            return good;
        }

        /// <summary>优先取包含中心线(s=0)的区间，否则取最宽的</summary>
        static double[] PickBest(List<double[]> ivs)
        {
            double[] best = null;
            double bestScore = double.MinValue;
            for (int i = 0; i < ivs.Count; i++)
            {
                double score = ivs[i][1] - ivs[i][0];
                if (ivs[i][0] <= 0 && ivs[i][1] >= 0) score += 1e6;
                if (score > bestScore) { bestScore = score; best = ivs[i]; }
            }
            return best;
        }

        static Point2d At(Point2d o, Vector2d d, double t, Vector2d n, double s)
        {
            return new Point2d(o.X + d.X * t + n.X * s, o.Y + d.Y * t + n.Y * s);
        }

        static Point2d At(Point2d o, Vector2d n, double s)
        {
            return new Point2d(o.X + n.X * s, o.Y + n.Y * s);
        }

        /// <summary>
        /// 给多边形的硬角倒圆角：每个转角处退让 r/tan(θ/2)，用圆弧替代尖角。
        /// 采样点之间的小转角（近直线）不动；退让距离受相邻边长限制（最大取半边长），
        /// 所以圆角半径大于短边时自动缩小，不会把图形吃掉。
        /// </summary>
        // 调试计数：被倒圆的角 / 因太尖跳过 / 因退化跳过
        internal static int DbgCorners, DbgRounded, DbgSharp, DbgDegenerate, DbgNoFit;

        // ==== 临时探针诊断（定位「倒了圆角却仍留硬角」）====
        internal static Point2d DbgProbe = new Point2d(double.MaxValue, double.MaxValue);
        internal static double DbgProbeR = 0.0;
        internal static List<string> DbgProbeLog = new List<string>();

        static bool NearProbe(Point2d p)
        {
            if (DbgProbeR <= 0) return false;
            double dx = p.X - DbgProbe.X, dy = p.Y - DbgProbe.Y;
            return dx * dx + dy * dy <= DbgProbeR * DbgProbeR;
        }

        /// <summary>
        /// 合并相邻过近的顶点（相对容差 = 包围盒对角线 × 1e-6，与 MakePolygonCurve 的输出去重口径一致）。
        /// 这些点在最终曲线上本来就会被去重丢掉，若留给拐点检测，会把一个真实拐点拆成
        /// 两个假拐点，导致圆角半径被压到微米级、进而被去重吃掉，硬角留在结果里。
        /// </summary>
        internal static List<Point2d> MergeNearDuplicates(List<Point2d> poly)
        {
            int n = poly == null ? 0 : poly.Count;
            if (n < 2) return poly;
            double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (poly[i].X < mnx) mnx = poly[i].X;
                if (poly[i].Y < mny) mny = poly[i].Y;
                if (poly[i].X > mxx) mxx = poly[i].X;
                if (poly[i].Y > mxy) mxy = poly[i].Y;
            }
            double tol = Math.Max(Math.Sqrt((mxx - mnx) * (mxx - mnx) + (mxy - mny) * (mxy - mny)) * 1e-6, 1e-9);
            var outp = new List<Point2d>(n);
            for (int i = 0; i < n; i++)
            {
                if (outp.Count > 0 && outp[outp.Count - 1].DistanceTo(poly[i]) < tol) continue;
                outp.Add(poly[i]);
            }
            while (outp.Count > 2 && outp[0].DistanceTo(outp[outp.Count - 1]) < tol)
                outp.RemoveAt(outp.Count - 1);
            return outp;
        }

        internal static List<Point2d> RoundCorners(List<Point2d> poly, double radius, double maxStep)
        {
            if (poly == null) return null;
            if (poly.Count < 3 || radius <= 1e-12) return poly;   // 与旧实现一致：不倒角就原样返回
            return DensifySegs(RoundCornersSegs(poly, radius, maxStep), maxStep);
        }

        /// <summary>折线点串 -> 直线段列表（去重口径与 MakePolygonCurve 一致）</summary>
        internal static List<Seg2d> PolylineSegs(List<Point2d> poly)
        {
            var segs = new List<Seg2d>();
            PolylineSegs(poly, segs);
            return segs;
        }

        static void PolylineSegs(List<Point2d> poly, List<Seg2d> segs)
        {
            if (poly == null) return;
            List<Point2d> clean = MergeNearDuplicates(poly);
            if (clean.Count < 2) return;
            for (int i = 0; i < clean.Count; i++)
            {
                Point2d a = clean[i], b = clean[(i + 1) % clean.Count];
                if (a.DistanceTo(b) > 1e-12) segs.Add(Seg2d.Line(a, b));
            }
        }

        /// <summary>把带类型的段列表按旧口径（maxStep / 每段 20° / 6..48 段）离散回点串</summary>
        internal static List<Point2d> DensifySegs(List<Seg2d> segs, double maxStep)
        {
            var outp = new List<Point2d>();
            if (segs == null) return outp;
            for (int i = 0; i < segs.Count; i++)
            {
                Seg2d sg = segs[i];
                if (outp.Count == 0 || outp[outp.Count - 1].DistanceTo(sg.A) > 1e-15) outp.Add(sg.A);
                if (!sg.Arc) continue;
                int k = ArcSegCountLegacy(sg.Sweep, sg.R, maxStep);
                for (int j = 1; j < k; j++) outp.Add(sg.PointAt(j / (double)k));
            }
            return outp;
        }

        /// <summary>旧口径的圆角离散段数（保持自检里 RoundCorners 的顶点数/面积诊断不变）</summary>
        static int ArcSegCountLegacy(double sweep, double radius, double maxStep)
        {
            double sw = Math.Abs(sweep);
            int segs = 6;
            if (maxStep > 1e-9) segs = (int)Math.Ceiling(sw * radius / maxStep);
            int byAngle = (int)Math.Ceiling(sw / (Math.PI / 9.0));   // 每段不超过 20°
            if (segs < byAngle) segs = byAngle;
            if (segs < 6) segs = 6;      // 至少 6 段，圆角看起来才是圆的
            if (segs > 48) segs = 48;
            return segs;
        }

        /// <summary>
        /// 圆弧采样段数：由弦高容差 tol 与半径决定（半径越大 / 容差越小 -> 段数越多，不再是固定值），
        /// 再叠加「最大弦不超过 8°」的下限，最后夹到 [minSegs, maxSegs]。
        /// 采样本身用余弦（Chebyshev）分布，见 MakeOutlineCurve —— 余弦分布下最大弦角 = |sweep|*π/(2n)，
        /// 据此反解 n，保证弦高仍然满足容差。
        /// </summary>
        internal static int ArcSegCount(double radius, double sweep, double tol, int minSegs, int maxSegs)
        {
            double sw = Math.Abs(sweep);
            if (sw < 1e-12) return 1;
            double r = Math.Max(radius, 1e-12);
            double ratio = Math.Min(Math.Max(tol, 1e-9) / r, 1.0);
            double maxStepAng = 2.0 * Math.Acos(1.0 - ratio);   // 弦高 = r*(1-cos(step/2))
            int n;
            if (maxStepAng > 1e-6) n = (int)Math.Ceiling(sw * Math.PI / (2.0 * maxStepAng));
            else n = maxSegs;
            int byAngle = (int)Math.Ceiling(sw * Math.PI / (2.0 * (8.0 * Math.PI / 180.0)));   // 最大弦 <= 8°
            if (n < byAngle) n = byAngle;
            if (n < minSegs) n = minSegs;
            if (n > maxSegs) n = maxSegs;
            return n;
        }

        static void EmitLine(List<Seg2d> segs, ref bool has, ref Point2d first, ref Point2d cur, Point2d p)
        {
            if (!has) { first = p; cur = p; has = true; return; }
            if (cur.DistanceTo(p) > 1e-12) segs.Add(Seg2d.Line(cur, p));
            cur = p;
        }

        /// <summary>
        /// 给多边形的硬角倒圆角：每个转角处退让 r/tan(θ/2)，用圆弧替代尖角。
        /// 采样点之间的小转角（近直线）不动；退让距离受相邻边长限制（最大取半边长），
        /// 所以圆角半径大于短边时自动缩小，不会把图形吃掉。
        /// 输出的是【带类型的段】：直线段 + 解析圆弧段（圆心/半径/起止角），
        /// 映射到曲面后才能生成真正的曲线，而不是把圆弧先采样成折线。
        /// </summary>
        internal static List<Seg2d> RoundCornersSegs(List<Point2d> poly, double radius, double maxStep)
        {
            var segs = new List<Seg2d>();
            int n = poly == null ? 0 : poly.Count;
            if (n < 1) return segs;

            // 0) 先按相对容差合并过近的相邻顶点（口径与 MakePolygonCurve 的去重一致）。
            //    贴合模式的条带端部是「上链终点 + 下链起点」拼出来的：尖端处两链几乎重合，
            //    会留下 ~1e-6 的微段。它会把一个真实拐点拆成两个假拐点（45° 楔角被拆成
            //    135° + 90°），两个假拐点的可用边长只有微段本身那么长，倒出的圆角半径
            //    也只有 ~1e-6，最后又被 MakePolygonCurve 的去重阈值吃掉 —— 硬角原样保留
            //    （自检里报出的 2.356 rad 残余转角就是这么来的）。
            if (n < 3 || radius <= 1e-12) { PolylineSegs(poly, segs); return segs; }
            poly = MergeNearDuplicates(poly);
            n = poly.Count;
            if (n < 3) { PolylineSegs(poly, segs); return segs; }

            bool probe = DbgProbeR > 0;
            int probeIdx = -1;
            if (probe)
                for (int i = 0; i < n; i++) if (NearProbe(poly[i])) { probeIdx = i; break; }
            if (probe && probeIdx >= 0)
            {
                DbgProbeLog.Add(string.Format("== 命中：输入多边形 n={0}，radius={1:0.####}，maxStep={2:0.####}，命中顶点 {3}",
                    n, radius, maxStep, probeIdx));
                for (int i = 0; i < n; i++)
                {
                    if (!NearProbe(poly[i])) continue;
                    Point2d pv = poly[(i - 1 + n) % n], cu = poly[i], nx = poly[(i + 1) % n];
                    DbgProbeLog.Add(string.Format("   输入 v{0} cur=({1:0.######},{2:0.######}) prev=({3:0.######},{4:0.######}) next=({5:0.######},{6:0.######}) |prev-cur|={7:0.########} |next-cur|={8:0.########}",
                        i, cu.X, cu.Y, pv.X, pv.Y, nx.X, nx.Y, pv.DistanceTo(cu), nx.DistanceTo(cu)));
                }
            }

            // 1) 先找出真正的拐点：转角（偏离直线的角度）大于阈值的才算，
            //    边界采样点之间那些一两度的小折角不算，否则会被误当成角来倒圆。
            const double minTurn = 12.0 * Math.PI / 180.0;
            var inter = new double[n];      // 顶点处的内角（直线段 = π）
            var isCorner = new bool[n];
            int cornerCount = 0;
            for (int i = 0; i < n; i++)
            {
                Point2d prev = poly[(i - 1 + n) % n], cur = poly[i], next = poly[(i + 1) % n];
                double dx1 = prev.X - cur.X, dy1 = prev.Y - cur.Y;
                double dx2 = next.X - cur.X, dy2 = next.Y - cur.Y;
                double l1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
                double l2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                if (l1 < 1e-12 || l2 < 1e-12) { inter[i] = Math.PI; continue; }
                double dot = (dx1 * dx2 + dy1 * dy2) / (l1 * l2);
                inter[i] = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
                double turn = Math.PI - inter[i];
                if (turn > minTurn && turn < Math.PI - 1e-6) { isCorner[i] = true; cornerCount++; DbgCorners++; }
                if (probe && NearProbe(cur))
                    DbgProbeLog.Add(string.Format("   检测 v{0}: l1={1:0.########} l2={2:0.########} 内角={3:0.####}° 转角={4:0.####}° isCorner={5}",
                        i, l1, l2, inter[i] * 180.0 / Math.PI, turn * 180.0 / Math.PI, isCorner[i]));
            }
            if (cornerCount == 0) { PolylineSegs(poly, segs); return segs; }
            if (probe && probeIdx >= 0)
                DbgProbeLog.Add(string.Format("   总拐点 {0} / n={1}", cornerCount, n));

            // 2) 每个拐点到「相邻拐点」的边长（沿多边形累加），
            //    半径上限按它算 —— 用相邻采样点的间距算的话，条纹越短采样越密，
            //    上限越小，圆角就会一条一个样（bug：倒角从左到右越来越大）。
            var edgePrev = new double[n];
            var edgeNext = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (!isCorner[i]) continue;
                double len = 0; int k = i;
                do
                {
                    int kp = (k - 1 + n) % n;
                    len += poly[k].DistanceTo(poly[kp]);
                    k = kp;
                } while (k != i && !isCorner[k]);
                edgePrev[i] = len;

                len = 0; k = i;
                do
                {
                    int kn = (k + 1) % n;
                    len += poly[k].DistanceTo(poly[kn]);
                    k = kn;
                } while (k != i && !isCorner[k]);
                edgeNext[i] = len;
            }

            Point2d first = Point2d.Unset, cursor = Point2d.Unset;
            bool has = false;
            for (int i = 0; i < n; i++)
            {
                if (!isCorner[i]) { EmitLine(segs, ref has, ref first, ref cursor, poly[i]); continue; }
                Point2d prev = poly[(i - 1 + n) % n], cur = poly[i], next = poly[(i + 1) % n];
                double dx1 = prev.X - cur.X, dy1 = prev.Y - cur.Y;
                double dx2 = next.X - cur.X, dy2 = next.Y - cur.Y;
                double l1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
                double l2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                if (l1 < 1e-12 || l2 < 1e-12) { EmitLine(segs, ref has, ref first, ref cursor, cur); DbgDegenerate++; continue; }
                double ux1 = dx1 / l1, uy1 = dy1 / l1;
                double ux2 = dx2 / l2, uy2 = dy2 / l2;

                double ang = inter[i];                              // 内角
                if (ang < 25.0 * Math.PI / 180.0) { EmitLine(segs, ref has, ref first, ref cursor, cur); DbgSharp++; continue; }  // 太尖的针尖放不下圆角
                double half = ang * 0.5;
                double tanH = Math.Tan(half);
                if (tanH < 1e-9) { EmitLine(segs, ref has, ref first, ref cursor, cur); DbgSharp++; continue; }

                double d = radius / tanH;                            // 切点退让距离
                double maxD = Math.Min(edgePrev[i], edgeNext[i]) * 0.5;
                if (d > maxD) d = maxD;
                // 切点不要正好落在相邻顶点上：会留下零长边，那个顶点的方向是纯浮点噪声，
                // 量出来的「转角」是假的（曾经报出过 112° 的假硬角）
                if (d > l1 * 0.98) d = l1 * 0.98;
                if (d > l2 * 0.98) d = l2 * 0.98;
                if (probe && NearProbe(cur))
                    DbgProbeLog.Add(string.Format("   倒圆 v{0}: 内角={1:0.####}° edgePrev={2:0.######} edgeNext={3:0.######} d={4:0.########} maxD={5:0.######} rr={6:0.########}",
                        i, ang * 180.0 / Math.PI, edgePrev[i], edgeNext[i], d, maxD, d * tanH));
                if (d < 1e-9) { EmitLine(segs, ref has, ref first, ref cursor, cur); DbgNoFit++; continue; }
                double rr = d * tanH;
                DbgRounded++;

                double p1x = cur.X + ux1 * d, p1y = cur.Y + uy1 * d;
                double p2x = cur.X + ux2 * d, p2y = cur.Y + uy2 * d;

                // 圆心在角平分线方向上（凸角在材料内侧，凹角在小楔形一侧），
                // 到两条边的距离都等于 rr，所以到顶点距离 = rr / sin(half)
                double bx = ux1 + ux2, by = uy1 + uy2;
                double bl = Math.Sqrt(bx * bx + by * by);
                if (bl < 1e-12) { EmitLine(segs, ref has, ref first, ref cursor, cur); continue; }
                bx /= bl; by /= bl;
                double cdist = rr / Math.Sin(half);
                double cx = cur.X + bx * cdist;
                double cy = cur.Y + by * cdist;

                double a1 = Math.Atan2(p1y - cy, p1x - cx);
                double a2 = Math.Atan2(p2y - cy, p2x - cx);
                double da = a2 - a1;
                while (da > Math.PI) da -= 2 * Math.PI;
                while (da < -Math.PI) da += 2 * Math.PI;

                int segsN = ArcSegCountLegacy(da, rr, maxStep);

                var p1 = new Point2d(p1x, p1y);
                var p2 = new Point2d(p2x, p2y);
                EmitLine(segs, ref has, ref first, ref cursor, p1);
                segs.Add(Seg2d.ArcSeg(new Point2d(cx, cy), rr, a1, da, p1, p2));
                cursor = p2;
                if (probe && NearProbe(cur))
                    DbgProbeLog.Add(string.Format("   弧 v{0}: da={1:0.####}° segs={2} rr={3:0.########} p1=({4:0.######},{5:0.######}) p2=({6:0.######},{7:0.######})",
                        i, da * 180.0 / Math.PI, segsN, rr, p1x, p1y, p2x, p2y));
            }
            // 闭合：最后一段回到起点（旧实现把首点留给 MakePolygonCurve 补，这里由段列表自带）
            if (has && cursor.DistanceTo(first) > 1e-12) segs.Add(Seg2d.Line(cursor, first));
            if (probe && probeIdx >= 0)
            {
                DbgProbeLog.Add("   输出点(探针附近):");
                List<Point2d> outp = DensifySegs(segs, maxStep);
                for (int i = 0; i < outp.Count; i++)
                    if (NearProbe(outp[i]))
                        DbgProbeLog.Add(string.Format("     o{0}=({1:0.######},{2:0.######})", i, outp[i].X, outp[i].Y));
            }
            return segs;
        }

        /// <summary>
        /// 2D 轮廓的一段：直线段或【解析圆弧段】。保留圆心/半径/起止角，
        /// 映射到 3D 时才能生成真正的曲线（ArcCurve / NURBS），而不是先采样成折线。
        /// </summary>
        internal sealed class Seg2d
        {
            public bool Arc;
            public Point2d A, B;      // 直线：两端点；圆弧：起点/终点切点（精确值，不由角度反算）
            public Point2d C;         // 圆弧中心
            public double R;          // 圆弧半径
            public double A0;         // 圆弧起始角（标准 atan2 角，rad）
            public double Sweep;      // 圆弧有向扫角（rad，可负）

            public static Seg2d Line(Point2d a, Point2d b)
            {
                return new Seg2d { Arc = false, A = a, B = b };
            }

            public static Seg2d ArcSeg(Point2d c, double r, double a0, double sweep, Point2d a, Point2d b)
            {
                return new Seg2d { Arc = true, C = c, R = r, A0 = a0, Sweep = sweep, A = a, B = b };
            }

            public Point2d PointAt(double t)
            {
                if (!Arc) return new Point2d(A.X + (B.X - A.X) * t, A.Y + (B.Y - A.Y) * t);
                if (t <= 0) return A;
                if (t >= 1) return B;
                double a = A0 + Sweep * t;
                return new Point2d(C.X + R * Math.Cos(a), C.Y + R * Math.Sin(a));
            }
        }

        /// <summary>2D 轮廓点 -> 3D：平面面用参考系直投（刚性映射），曲面面用 ClosestPoint 回投到面上</summary>
        internal static bool MapTo3d(Plane frame, BrepFace face, bool planar, Point2d p, out Point3d q)
        {
            q = frame.PointAt(p.X, p.Y);
            if (planar) return true;
            double u, v;
            if (face == null || !face.ClosestPoint(q, out u, out v)) { q = Point3d.Unset; return false; }
            q = face.PointAt(u, v);
            return true;
        }

        /// <summary>2D 多边形 -> 3D 曲线（平面面用参考系，曲面面回投到面）</summary>
        static Curve MakePolygonCurve(List<Point2d> pts, Plane frame, BrepFace face, bool planar)
        {
            if (pts == null || pts.Count < 3) return null;

            // 先按相对容差去掉重合点：重合点会留下方向不确定的退化顶点，
            // 在它上面量出来的「转角」是纯噪声（曾经报出过假的 142° 硬角）。
            // 与 RoundCorners 用同一个合并口径，保证拐点检测看到的点集与最终曲线一致。
            List<Point2d> pts2 = MergeNearDuplicates(pts);
            if (pts2.Count < 3) return null;

            var clean = new List<Point3d>(pts2.Count + 1);
            for (int i = 0; i < pts2.Count; i++)
            {
                Point3d p = frame.PointAt(pts2[i].X, pts2[i].Y);
                if (!planar)
                {
                    double u, v;
                    if (!face.ClosestPoint(p, out u, out v)) return null;
                    p = face.PointAt(u, v);
                }
                if (clean.Count > 0 && clean[clean.Count - 1].DistanceTo(p) < 1e-9) continue;
                clean.Add(p);
            }
            if (clean.Count < 3) return null;
            if (clean[0].DistanceTo(clean[clean.Count - 1]) > 1e-9) clean.Add(clean[0]);
            var plc = new PolylineCurve(clean);
            return plc.IsValid ? plc : null;
        }

        /// <summary>
        /// 带类型的 2D 轮廓 -> 3D 曲线。直线段给 LineCurve；圆弧段：
        ///  · 平面面：映射是刚性的，直接给【精确 ArcCurve】（与两侧直线相切）；
        ///  · 曲面面：曲面上的圆角不是圆弧，按弦高容差自适应采样后做 3 次 NURBS 插值
        ///    （真正的曲线，放大不会出现一段段直线）。
        /// 全是直线段时退回旧的折线口径，保证不改变原有行为。
        /// </summary>
        internal static Curve MakeOutlineCurve(List<Seg2d> segs, Plane frame, BrepFace face, bool planar, double tol)
        {
            if (segs == null || segs.Count == 0) return null;

            bool anyArc = false;
            for (int i = 0; i < segs.Count; i++) if (segs[i].Arc) { anyArc = true; break; }
            if (!anyArc)
            {
                var pts = new List<Point2d>(segs.Count);
                for (int i = 0; i < segs.Count; i++) pts.Add(segs[i].A);
                return MakePolygonCurve(pts, frame, face, planar);
            }

            var pieces = new List<Curve>(segs.Count);
            for (int i = 0; i < segs.Count; i++)
            {
                Seg2d sg = segs[i];
                Curve piece = null;
                if (!sg.Arc)
                {
                    Point3d p0, p1;
                    if (!MapTo3d(frame, face, planar, sg.A, out p0)) return null;
                    if (!MapTo3d(frame, face, planar, sg.B, out p1)) return null;
                    if (p0.DistanceTo(p1) < 1e-12) continue;
                    piece = new LineCurve(p0, p1);
                }
                else if (planar)
                {
                    Point3d c3;
                    if (!MapTo3d(frame, face, true, sg.C, out c3)) return null;
                    Vector3d ax = frame.XAxis * Math.Cos(sg.A0) + frame.YAxis * Math.Sin(sg.A0);
                    Vector3d ay = Vector3d.CrossProduct(frame.ZAxis, ax);
                    if (!ax.Unitize() || !ay.Unitize()) continue;
                    var arc = new Arc(new Plane(c3, ax, ay), sg.R, sg.Sweep);
                    if (arc.IsValid) piece = new ArcCurve(arc);
                }
                else
                {
                    int k = ArcSegCount(sg.R, sg.Sweep, tol, 16, 96);
                    var pp = new List<Point3d>(k + 1);
                    for (int j = 0; j <= k; j++)
                    {
                        // 余弦（Chebyshev）分布：两端密、中间疏。端部弦长 ~ (π/k)²，
                        // 使 3 次 NURBS 在端点的切线≈真实切线 —— 与两侧直线相切，接点不留折角。
                        double uu = 0.5 * (1.0 - Math.Cos(Math.PI * j / (double)k));
                        Point3d q;
                        if (!MapTo3d(frame, face, false, sg.PointAt(uu), out q)) return null;
                        if (pp.Count > 0 && pp[pp.Count - 1].DistanceTo(q) < 1e-12) continue;
                        pp.Add(q);
                    }
                    if (pp.Count < 2) continue;
                    if (pp.Count == 2) piece = new LineCurve(pp[0], pp[1]);
                    else
                    {
                        Curve nu = Curve.CreateInterpolatedCurve(pp, 3);
                        if (nu != null && nu.IsValid) piece = nu;
                    }
                }
                if (piece != null) pieces.Add(piece);
            }
            if (pieces.Count == 0) return null;

            var pc = new PolyCurve();
            for (int i = 0; i < pieces.Count; i++) pc.Append(pieces[i]);
            if (pc.IsValid) return pc;
            Curve[] joined = Curve.JoinCurves(pieces, Math.Max(tol, 1e-9), true);
            if (joined != null && joined.Length > 0) return joined[0];
            return null;
        }

        // ---------------------------------------------------------------- 参考坐标系
        /// <summary>本面是否整个落在给定参考平面里（共面才敢用「平面直投」把 2D 映射回 3D）</summary>
        static bool FaceLiesInFramePlane(BrepFace face, Plane frame)
        {
            try
            {
                BoundingBox bb = face.GetBoundingBox(true);
                double diag = bb.IsValid ? bb.Diagonal.Length : 0;
                double tol = Math.Max(diag * 1e-6, 1e-9);
                foreach (BrepLoop loop in face.Loops)
                {
                    Point3d[] pts = SamplePoints(loop.To3dCurve(), 16);
                    if (pts == null) continue;
                    for (int i = 0; i < pts.Length; i++)
                        if (Math.Abs(Dot(pts[i] - frame.Origin, frame.ZAxis)) > tol) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>本面（以中心法向代表）是否几乎垂直于给定参考平面：垂直投影会退化成一条线</summary>
        static bool FaceNearlyPerpendicularTo(BrepFace face, Plane frame)
        {
            try
            {
                Point3d org; Vector3d nz;
                if (!GetFaceCenterFrame(face, out org, out nz) || !nz.IsValid) return false;
                if (!nz.Unitize()) return false;
                return Math.Abs(Dot(nz, frame.ZAxis)) < 0.1;      // 夹角 > ~84° 视为投影退化
            }
            catch { return false; }
        }

        internal static bool TryBuildFrame(BrepFace face, out Plane frame, out bool planar)
        {
            frame = Plane.Unset;
            planar = false;
            Plane pl = Plane.Unset;
            try { planar = face.TryGetPlane(out pl, 1e-3) && pl.IsValid; }
            catch { planar = false; }

            Point3d org;
            Vector3d nz;
            if (!GetFaceCenterFrame(face, out org, out nz))
            {
                if (!planar) return false;
                org = pl.Origin;
                nz = pl.ZAxis;
            }
            if (!nz.Unitize()) return false;

            Vector3d x = WorldReference(nz);
            Vector3d y = Vector3d.CrossProduct(nz, x);
            if (!y.Unitize()) return false;
            frame = new Plane(org, x, y);
            return frame.IsValid;
        }

        static Vector3d WorldReference(Vector3d z)
        {
            var gx = new Vector3d(1, 0, 0);
            var p = gx - z * Dot(gx, z);
            if (p.Length < 1e-6)
            {
                var gz = new Vector3d(0, 0, 1);
                p = gz - z * Dot(gz, z);
            }
            if (p.Length < 1e-6)
            {
                var gy = new Vector3d(0, 1, 0);
                p = gy - z * Dot(gy, z);
            }
            if (!p.Unitize()) p = new Vector3d(1, 0, 0);
            return p;
        }

        static bool GetFaceCenterFrame(BrepFace face, out Point3d org, out Vector3d nz)
        {
            org = Point3d.Unset;
            nz = Vector3d.Unset;
            Point3d c = Point3d.Unset;
            try
            {
                double sum = 0;
                var acc = new Point3d(0, 0, 0);
                foreach (BrepLoop loop in face.Loops)
                {
                    Curve cv = loop.To3dCurve();
                    Point3d[] pts = SamplePoints(cv, 32);
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

            double u, v;
            if (!face.ClosestPoint(c, out u, out v)) return false;
            org = face.PointAt(u, v);
            nz = face.NormalAt(u, v);
            return org.IsValid && nz.IsValid;
        }

        // ---------------------------------------------------------------- 边界 -> 2D
        static Region2D BuildRegion(BrepFace face, Plane frame, double step, bool planar, int maxPts)
        {
            var region = new Region2D();
            foreach (BrepLoop loop in face.Loops)
            {
                Curve c3 = loop.To3dCurve();
                if (c3 == null) continue;
                var pts = new List<Point2d>();
                SampleCurve(c3, step, delegate(Point3d p) { return (Point2d?)To2d(frame, p); }, pts, maxPts);
                region.AddRing(pts);
            }
            return region;
        }

        /// <summary>
        /// 本面「某一类边」拼成的 2D 距离查询区域：
        /// smoothSeams = false → 折痕 / 实体棱边 / 外轮廓（这些地方留边距）；
        /// smoothSeams = true  → 顺接的内部接缝（夹角小于 35°，同一个面延续过去 —— 条纹要跨过去）。
        /// </summary>
        static Region2D BuildEdgeRegion(BrepFace face, Brep brep, Plane frame, double step, bool planar, int maxPts,
                                        bool smoothSeams)
        {
            var region = new Region2D();
            if (brep == null) return region;
            try
            {
                int[] eis = face.AdjacentEdges();
                if (eis == null) return region;
                foreach (int ei in eis)
                {
                    if (ei < 0 || ei >= brep.Edges.Count) continue;
                    BrepEdge e = brep.Edges[ei];
                    if (IsSmoothSeam(brep, e) != smoothSeams) continue;
                    Curve c3 = e.DuplicateCurve();
                    if (c3 == null) continue;
                    var pts = new List<Point2d>();
                    SampleCurve(c3, step, delegate(Point3d p) { return (Point2d?)To2d(frame, p); }, pts, maxPts);
                    region.AddRing(pts, false);                   // 开放折线：只用来量距离，不闭合（避免假线段）
                }
            }
            catch { }
            return region;
        }

        /// <summary>两侧都有面、且夹角小于 35°（或接缝自己对自己）= 顺接的内部接缝</summary>
        static bool IsSmoothSeam(Brep brep, BrepEdge e)
        {
            try
            {
                int[] af = e.AdjacentFaces();
                if (af == null || af.Length < 2) return false;
                if (af[0] == af[1]) return true;                  // 闭合面的接缝：本来就不是真边界
                if (af[0] < 0 || af[1] < 0 || af[0] >= brep.Faces.Count || af[1] >= brep.Faces.Count) return false;
                Point3d mid = e.PointAt(e.Domain.Mid);
                double u, v;
                Vector3d n0 = Vector3d.Zero, n1 = Vector3d.Zero;
                if (brep.Faces[af[0]].ClosestPoint(mid, out u, out v)) n0 = brep.Faces[af[0]].NormalAt(u, v);
                if (brep.Faces[af[1]].ClosestPoint(mid, out u, out v)) n1 = brep.Faces[af[1]].NormalAt(u, v);
                if (!n0.IsValid || !n1.IsValid) return false;
                if (!n0.Unitize() || !n1.Unitize()) return false;
                return Math.Abs(Dot(n0, n1)) >= 0.8191520442889918;   // cos 35°
            }
            catch { return false; }
        }

        /// <summary>
        /// 取本面的 3D 边界：Curves = 需要留边距的边（自由边 + 折痕边，夹角 > 35°）；
        /// Seams = 顺接接缝（夹角 < 35° 或接缝自己对自己）—— 图案要跨过去。
        /// </summary>
        static Border3d BuildBorder3d(Brep brep, BrepFace face)
        {
            var b = new Border3d();
            try
            {
                int[] eis = face.AdjacentEdges();
                if (eis == null) return b;
                foreach (int ei in eis)
                {
                    if (ei < 0 || ei >= brep.Edges.Count) continue;
                    BrepEdge e = brep.Edges[ei];
                    Curve c = e.DuplicateCurve();
                    if (c == null) continue;
                    if (IsSmoothSeam(brep, e)) b.Seams.Add(c); else b.Curves.Add(c);
                }
            }
            catch { }
            return b;
        }

        /// <summary>条纹方向与「顺接接缝法向」的夹角余弦（取绝对值里最大的那个，最保守 → 不会越缝）</summary>
        static double SeamNormalDot(Region2D seam, Vector2d dir)
        {
            double best = 0;
            if (seam == null) return best;
            for (int r = 0; r < seam.Rings.Count; r++)
            {
                Point2d[] ring = seam.Rings[r];
                for (int i = 0; i + 1 < ring.Length; i++)
                {
                    double vx = ring[i + 1].X - ring[i].X, vy = ring[i + 1].Y - ring[i].Y;
                    double len = Math.Sqrt(vx * vx + vy * vy);
                    if (!(len > 1e-12)) continue;
                    // 缝的法向 = 段方向的垂线
                    double nx = -vy / len, ny = vx / len;
                    double d = Math.Abs(dir.X * nx + dir.Y * ny);
                    if (d > best) best = d;
                }
            }
            return best;
        }

        static void SampleCurve(Curve c, double step, Func<Point3d, Point2d?> conv, List<Point2d> outp, int maxPts)
        {
            double len = c.GetLength();
            if (!(len > 0)) len = 1.0;
            int n = (int)Math.Round(len / Math.Max(step, 1e-6));
            n = Math.Max(16, Math.Min(Math.Max(maxPts, 16), n));
            Point3d[] pts = SamplePoints(c, n);
            if (pts == null) return;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Point2d? q = conv(pts[i]);
                if (!q.HasValue) continue;
                if (outp.Count > 0 && outp[outp.Count - 1].DistanceTo(q.Value) < 1e-12) continue;
                outp.Add(q.Value);
            }
        }

        // ---------------------------------------------------------------- 区间裁剪
        /// <summary>
        /// 3D 边界距离判定（多面 / 曲面时用）：曲面片投影到共享平面会自相交，2D 距离判定会把大部分条纹误杀
        /// （实测：200mm 的壳上只出 7 条，应有 ~50 条）。这里把 2D 参数 t 映射回 3D，直接量到边界曲线的 3D 距离。
        /// </summary>
        internal class Border3d
        {
            public List<Curve> Curves = new List<Curve>();
            public List<Curve> Seams = new List<Curve>();
            public bool Has { get { return Curves.Count > 0 || Seams.Count > 0; } }
        }

        static double Dist3d(List<Curve> curves, Point3d p, double maxR)
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

        static List<Tuple<Point2d, Point2d>> GoodRuns(Region2D region, Region2D seamRegion, double endClear,
            Point2d o, Vector2d dir, double a, double b, double rEff, double step,
            Func<double, Point3d> to3d = null, Border3d borders = null)
        {
            var runs = new List<Tuple<Point2d, Point2d>>();
            double len = b - a;
            int n = (int)Math.Ceiling(len / Math.Max(step, 1e-9));
            n = Math.Max(2, Math.Min(n, 4000));
            double r = rEff * (1 - 1e-9);
            bool useSeam = seamRegion != null && seamRegion.Rings.Count > 0 && endClear > 1e-9;
            bool use3d = to3d != null && borders != null && borders.Has;

            Func<double, bool> good = t =>
            {
                var p = new Point2d(o.X + dir.X * t, o.Y + dir.Y * t);
                if (use3d)
                {
                    Point3d P3 = to3d(t);
                    if (!P3.IsValid) return false;
                    if (borders.Curves.Count > 0 && !(Dist3d(borders.Curves, P3, rEff) >= r)) return false;
                    if (borders.Seams.Count > 0 && endClear > 1e-9 &&
                        !(Dist3d(borders.Seams, P3, endClear * 1.01 + 1e-9) >= endClear * (1 - 1e-9))) return false;
                    return true;
                }
                if (!(region.Distance(p, rEff) >= r)) return false;
                // 顺接接缝：圆头端不能越过去（圆头圆心压在接缝上，圆头正好顶到缝）
                if (useSeam && !(seamRegion.Distance(p, endClear * 1.01 + 1e-9) >= endClear * (1 - 1e-9))) return false;
                return true;
            };

            bool prevGood = false;
            double start = a;
            for (int k = 0; k <= n; k++)
            {
                double t = a + len * k / (double)n;
                bool g = good(t);
                if (g && !prevGood)
                    start = (k == 0) ? a : Bisect(good, a + len * (k - 1) / n, t);
                else if (!g && prevGood)
                {
                    double end = Bisect(good, t, a + len * (k - 1) / n);
                    if (end - start > 1e-9)
                        runs.Add(Tuple.Create(Pt(o, dir, start), Pt(o, dir, end)));
                }
                prevGood = g;
            }
            if (prevGood && b - start > 1e-9)
                runs.Add(Tuple.Create(Pt(o, dir, start), Pt(o, dir, b)));
            return runs;
        }

        static Point2d Pt(Point2d o, Vector2d d, double t) { return new Point2d(o.X + d.X * t, o.Y + d.Y * t); }

        static double Bisect(Func<double, bool> good, double badT, double goodT)
        {
            for (int i = 0; i < 24; i++)
            {
                double mid = 0.5 * (badT + goodT);
                if (good(mid)) goodT = mid; else badT = mid;
            }
            return goodT;
        }

        // ---------------------------------------------------------------- 条纹形状
        static Curve MakeStadiumPlanar(Plane frame, Point2d a, Point2d b, double width, bool rounded)
        {
            Vector2d d = b - a;
            double len = d.Length;
            if (len < 1e-9) return null;
            Vector2d u = new Vector2d(d.X / len, d.Y / len);
            Vector2d n = new Vector2d(-u.Y, u.X);
            double r = width * 0.5;

            Point3d A = frame.PointAt(a.X, a.Y);
            Point3d B = frame.PointAt(b.X, b.Y);
            Vector3d u3 = new Vector3d(
                frame.XAxis.X * u.X + frame.YAxis.X * u.Y,
                frame.XAxis.Y * u.X + frame.YAxis.Y * u.Y,
                frame.XAxis.Z * u.X + frame.YAxis.Z * u.Y);
            Vector3d n3 = new Vector3d(
                frame.XAxis.X * n.X + frame.YAxis.X * n.Y,
                frame.XAxis.Y * n.X + frame.YAxis.Y * n.Y,
                frame.XAxis.Z * n.X + frame.YAxis.Z * n.Y);
            if (!u3.Unitize() || !n3.Unitize()) return null;

            var segs = new List<Curve>();
            segs.Add(new LineCurve(A + n3 * r, B + n3 * r));
            if (rounded)
            {
                segs.Add(new ArcCurve(new Arc(new Plane(B, n3, u3), B, r, Math.PI)));
                segs.Add(new LineCurve(B - n3 * r, A - n3 * r));
                segs.Add(new ArcCurve(new Arc(new Plane(A, -n3, -u3), A, r, Math.PI)));
            }
            else
            {
                segs.Add(new LineCurve(B + n3 * r, B - n3 * r));
                segs.Add(new LineCurve(B - n3 * r, A - n3 * r));
                segs.Add(new LineCurve(A - n3 * r, A + n3 * r));
            }

            double jtol = Math.Max(Math.Min(width, len) * 1e-6, 1e-9);
            Curve[] joined = Curve.JoinCurves(segs, jtol);
            if (joined != null && joined.Length == 1) return joined[0];

            var pc = new PolyCurve();
            for (int i = 0; i < segs.Count; i++) pc.Append(segs[i]);
            return pc;
        }

        internal static Curve MakeStadiumOnSurface(BrepFace face, Plane frame, Point2d a, Point2d b, double width, bool rounded, double tol)
        {
            List<Seg2d> segs = StadiumSegs(a, b, width, rounded);
            if (segs.Count < 3) return null;
            return MakeOutlineCurve(segs, frame, face, false, tol);
        }

        /// <summary>
        /// 胶囊形（stadium）轮廓：两条直线 + 两个半圆端头。
        /// 端头是【解析半圆】，映射到曲面后才不会变成一段段直线。
        /// </summary>
        internal static List<Seg2d> StadiumSegs(Point2d a, Point2d b, double width, bool rounded)
        {
            var segs = new List<Seg2d>();
            Vector2d d = b - a;
            double len = d.Length;
            if (len < 1e-9) return segs;
            Vector2d u = new Vector2d(d.X / len, d.Y / len);
            Vector2d n = new Vector2d(-u.Y, u.X);
            double r = width * 0.5;

            var t0 = new Point2d(a.X + n.X * r, a.Y + n.Y * r);   // a 端 +n 侧
            var t1 = new Point2d(b.X + n.X * r, b.Y + n.Y * r);   // b 端 +n 侧
            var s1 = new Point2d(b.X - n.X * r, b.Y - n.Y * r);   // b 端 -n 侧
            var s0 = new Point2d(a.X - n.X * r, a.Y - n.Y * r);   // a 端 -n 侧

            segs.Add(Seg2d.Line(t0, t1));
            if (rounded)
            {
                // 端头半圆：从 +n 侧绕到 -n 侧，中点在 u 方向（即尖端），与两侧直线相切
                segs.Add(Seg2d.ArcSeg(b, r, Math.Atan2(n.Y, n.X), -Math.PI, t1, s1));
                segs.Add(Seg2d.Line(s1, s0));
                segs.Add(Seg2d.ArcSeg(a, r, Math.Atan2(-n.Y, -n.X), -Math.PI, s0, t0));
            }
            else
            {
                segs.Add(Seg2d.Line(t1, s1));
                segs.Add(Seg2d.Line(s1, s0));
                segs.Add(Seg2d.Line(s0, t0));
            }
            return segs;
        }

        internal static List<Point2d> StadiumPoints(Point2d a, Point2d b, double width, bool rounded, int capSegs)
        {
            var pts = new List<Point2d>();
            Vector2d d = b - a;
            double len = d.Length;
            if (len < 1e-9) return pts;
            Vector2d u = new Vector2d(d.X / len, d.Y / len);
            Vector2d n = new Vector2d(-u.Y, u.X);
            double r = width * 0.5;
            if (!rounded)
            {
                pts.Add(a + n * r);
                pts.Add(b + n * r);
                pts.Add(b - n * r);
                pts.Add(a - n * r);
                return pts;
            }
            pts.Add(a + n * r);
            for (int i = 1; i < capSegs; i++)
            {
                double th = Math.PI * i / capSegs;
                pts.Add(b + n * (r * Math.Cos(th)) + u * (r * Math.Sin(th)));
            }
            pts.Add(b - n * r);
            pts.Add(a - n * r);
            for (int i = 1; i < capSegs; i++)
            {
                double th = Math.PI * i / capSegs;
                pts.Add(a - n * (r * Math.Cos(th)) - u * (r * Math.Sin(th)));
            }
            return pts;
        }

        // ---------------------------------------------------------------- 诊断
        static double MeasureMinDistance(Region2D region, StripeFaceResult res, Plane frame)
        {
            double best = double.MaxValue;
            try
            {
                foreach (Curve c in res.Stripes)
                {
                    Point3d[] pts = SamplePoints(c, 48);
                    if (pts == null) continue;
                    foreach (Point3d p in pts)
                    {
                        double d = region.Distance(To2d(frame, p), region.Diagonal);
                        if (d < best) best = d;
                    }
                }
            }
            catch { }
            return best == double.MaxValue ? double.MaxValue : best;
        }
    }
}
