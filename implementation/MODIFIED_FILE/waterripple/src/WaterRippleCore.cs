using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace WaterRipplePattern
{
    /// <summary>水波纹参数（面板 → 核心；长度都是模型单位）</summary>
    public class WaterRippleSettings
    {
        public double Wavelength = 12.0;      // 波长
        public double WaveHeight = 2.0;       // 波高（峰到谷）
        public int WaveMode = 0;              // 0 有机水波 / 1 定向条带 / 2 同心涟漪
        public int WaveCount = 5;             // 叠加波数（有机：方向数；条带：1~2 条）
        public double Direction = 0.0;        // 主方向（度）
        public double Spread = 55.0;          // 方向散布（度，有机）
        public double Crest = 0.0;            // 波峰形状：0 圆滑 … 1 陡峭（平顶+陡壁）
        public bool LockBoundary = false;     // 固定边界：边界顶点锁在原位置（纹理从边界向内过渡）
        public double Fade = 0.0;             // 边界过渡宽度（mm；0 = 用 波长×0.5 兜底）
        public double BlendSmooth = 0.5;      // 边界过渡平滑度：0 线性 … 0.5 smoothstep … 1 smootherstep
        public int Seed = 7;                  // 随机种子
        public double SamplesPerWave = 16.0;  // 每波长分段（剖分精度，越高越光滑）
        public double MmToModel = 1.0;        // 面板 mm → 模型单位
        public bool Smooth = false;           // 一键平滑 → 细分曲面（SubD）

        public WaterRippleSettings Clone()
        {
            return (WaterRippleSettings)MemberwiseClone();
        }
    }

    /// <summary>一次生成的结果</summary>
    public class WaterRippleResult
    {
        public Mesh Ripple;            // 位移后的网格
        public SubD SmoothSubD;        // 细分曲面（勾了一键平滑）
        public int Faces, Vertices;
        public double MinH, MaxH;      // 实际位移范围（模型单位）
        public double Wavelength, WaveHeight;
        public double BoundaryDistance = -1;   // 到边界的最近距离（诊断）
        public double Seconds;
        public string Note = "";
        public string Error = "";
    }

    public static class WaterRippleCore
    {
        internal const string LayerMesh = "水波纹-网格";
        internal const string LayerSmooth = "水波纹-平滑";
        public const string EmptyTargetHint = "先选一个曲面 / 多重曲面（当成一整个面）或一条闭合平面曲线边界";
        internal const double MaxSamplesPerWave = 64.0;

        // ============================================================ 目标解析

        /// <summary>
        /// 目标：曲面 / 多重曲面（当成一整个面处理）/ 闭合平面曲线边界。
        /// 网格 / 细分物件明确拒绝（先转 NURBS 或选它的平面边界曲线）。
        /// </summary>
        internal static bool TryResolve(RhinoObject obj, out GeometryBase target, out string why)
        {
            target = null; why = "";
            if (obj == null) { why = "没有选中物件"; return false; }
            GeometryBase g = obj.Geometry;
            if (g == null) { why = "物件几何为空"; return false; }

            if (g is Curve c)
            {
                if (!c.IsClosed) { why = "曲线不是闭合的（要闭合的平面曲线边界）"; return false; }
                bool planar;
                try { planar = c.IsPlanar(); } catch { planar = false; }
                if (!planar) { why = "曲线不在同一个平面上（要平面闭合曲线）"; return false; }
                target = c;
                return true;
            }
            if (g is Mesh) { why = "网格不支持：请选曲面 / 多重曲面，或它的平面边界曲线"; return false; }
            if (g is SubD) { why = "细分物件不支持：请先转成 NURBS 曲面，或选它的平面边界曲线"; return false; }
            if (g is Rhino.Geometry.Point) { why = "点不支持：请选曲面 / 多重曲面 / 闭合平面曲线"; return false; }

            Brep b = null;
            try { b = Brep.TryConvertBrep(g); } catch { b = null; }
            if (b == null || b.Faces.Count == 0) { why = "不是曲线或曲面"; return false; }
            target = b;
            return true;
        }

        /// <summary>把「曲线边界」变成一张平面片（其它输入原样返回）</summary>
        internal static Brep ToBrep(GeometryBase target, out string why)
        {
            why = "";
            if (target == null) { why = "目标为空"; return null; }
            if (target is Curve c)
            {
                Plane pl;
                if (!c.TryGetPlane(out pl)) { why = "曲线不在平面上，无法生成平面片"; return null; }
                Brep bp = null;
                try { bp = Brep.CreateTrimmedPlane(pl, c); } catch { bp = null; }
                if (bp == null || bp.Faces.Count == 0) { why = "曲线无法封成平面片（自交或太碎？）"; return null; }
                return bp;
            }
            Brep b = null;
            try { b = Brep.TryConvertBrep(target); } catch { b = null; }
            if (b == null || b.Faces.Count == 0) why = "不是曲面";
            return b;
        }

        // ============================================================ 参考平面

        /// <summary>
        /// 参考平面 = 波纹的 2D 坐标系（相位从这里算，所以多重曲面跨面也连续）。
        /// 平面输入用自身平面；弯曲输入对网格顶点做最小二乘拟合。
        /// </summary>
        internal static Plane RefPlane(Brep b, Mesh mesh, out bool planar)
        {
            planar = false;
            Plane pl = Plane.Unset;
            if (b != null)
            {
                try
                {
                    if (b.Faces.Count == 1 && b.Faces[0].TryGetPlane(out pl) && pl.IsValid) { planar = true; return pl; }
                }
                catch { }
            }
            var pts = new List<Point3d>();
            if (mesh != null)
            {
                int n = mesh.Vertices.Count;
                int stride = Math.Max(1, n / 400);
                for (int i = 0; i < n; i += stride) pts.Add(mesh.Vertices.Point3dAt(i));
            }
            if (pts.Count >= 3)
            {
                try
                {
                    Plane fit;
                    PlaneFitResult r = Plane.FitPlaneToPoints(pts, out fit);
                    if (r != PlaneFitResult.Failure && fit.IsValid)
                    {
                        // 采样点都贴在拟合平面上 → 视为平面输入（多重曲面的共面拼片走这条）
                        double dev = 0;
                        for (int i = 0; i < pts.Count; i++)
                            dev = Math.Max(dev, Math.Abs((pts[i] - fit.Origin) * fit.ZAxis));
                        planar = dev < 1e-6;
                        return fit;
                    }
                }
                catch { }
            }
            if (mesh != null)
            {
                BoundingBox bb = mesh.GetBoundingBox(true);
                if (bb.IsValid) { pl = new Plane(bb.Center, Vector3d.ZAxis); return pl; }
            }
            return Plane.WorldXY;
        }

        // ============================================================ 波形场

        /// <summary>
        /// 波形场：返回 -1..1。
        ///   0 有机水波 = N 个方向（主方向 ± 散布）+ 波长抖动 + 随机相位的正弦叠加 → 图 1 的活水感；
        ///   1 定向条带 = 主方向一条（可选第二条轻微起伏）→ 图 2 的规则条带；
        ///   2 同心涟漪 = sin(k·r)，从 (cu,cv) 向外扩散。
        /// u/v 是相对参考中心的平面坐标；k = 2π/波长。
        /// </summary>
        /// <summary>
        /// 波形场：返回 -1..1。**每种波形下每个参数都有明确作用**（面板不做「灰掉」——
        /// 用户口径：灰了就是点了没反应，不允许）：
        ///   有机水波：波数 = 叠加几个方向；主方向 = 主方向；方向散布 = 方向随机范围；
        ///   定向条带：波数 = 叠几层谐波；主方向 = 条带方向；方向散布 = 条带摆动（低频横波）幅度；
        ///   同心涟漪：波数 = 叠加几列涟漪（干涉）；主方向 = 涟漪源偏移方向；方向散布 = 环的起伏（角向谐波）。
        /// 波长/波高/波峰形状/随机种子 三种波形通用。u/v 是相对参考中心的平面坐标；k = 2π/波长。
        /// </summary>
        internal static double WaveField(double u, double v, WaterRippleSettings s, double k, double cu, double cv)
        {
            double x = u - cu, y = v - cv;
            var rng = new Random(s.Seed);
            double seedPh = rng.NextDouble() * Math.PI * 2.0;
            double seedPh2 = rng.NextDouble() * Math.PI * 2.0;
            double a = s.Direction * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            int n = s.WaveCount;
            if (n < 1) n = 1;
            if (n > 10) n = 10;
            double spread = s.Spread / 180.0;
            if (spread < 0) spread = 0; else if (spread > 1) spread = 1;
            double h;

            if (s.WaveMode == 1)                 // ---- 定向条带
            {
                double along = x * ca + y * sa;
                double across = -x * sa + y * ca;
                h = Math.Sin(k * along + seedPh);
                double amp = 1.0;
                for (int i = 2; i <= n && i <= 4; i++)      // 波数 = 叠几层谐波
                {
                    double w = 0.34 / (i - 1);
                    h += w * Math.Sin(k * i * 0.5 * along + seedPh * i);
                    amp += w;
                }
                if (spread > 1e-9)                          // 方向散布 = 条带摆动
                {
                    double w = 0.6 * spread;
                    h += w * Math.Sin(k * 0.35 * across + seedPh2);
                    amp += w;
                }
                h /= amp > 1e-9 ? amp : 1.0;
            }
            else if (s.WaveMode == 2)            // ---- 同心涟漪
            {
                // 同心涟漪 = **正圆环**（用户实测反馈：以前把半径按 1.45 拉成了椭圆，不是圆）。
                // 主方向 = 涟漪源偏移方向：**0° = 源就在中心**（默认，图案居中）；角度偏得越远，
                // 源沿该方向挪得越远（最多半个波长）→ 参数照样有作用，环永远是正圆。
                double lam = 2.0 * Math.PI / k;
                double mag = 0.25 * (1.0 - Math.Cos(a)) * lam;
                double ox = Math.Cos(a) * mag, oy = Math.Sin(a) * mag;
                double rx = x - ox, ry = y - oy;
                double r = Math.Sqrt(rx * rx + ry * ry);
                double th = Math.Atan2(ry, rx);
                h = Math.Sin(k * r + seedPh);
                double amp = 1.0;
                for (int i = 1; i < n && i <= 3; i++)       // 波数 = 叠加几列涟漪（干涉）
                {
                    double w = 0.42 / i;
                    h += w * Math.Sin(k * r * (1.0 + 0.22 * i) + seedPh + i * 1.7);
                    amp += w;
                }
                if (spread > 1e-9)                          // 方向散布 = 环的起伏
                {
                    double w = 0.55 * spread;
                    h += w * Math.Sin(3.0 * th + seedPh2);
                    amp += w;
                }
                h /= amp > 1e-9 ? amp : 1.0;
            }
            else                                 // ---- 有机水波
            {
                double a0 = a;
                double spreadRad = s.Spread * Math.PI / 180.0;
                double sum = 0, norm = 0;
                for (int i = 0; i < n; i++)
                {
                    double ai = a0 + (rng.NextDouble() * 2.0 - 1.0) * spreadRad;
                    double w = 0.55 + 0.45 * rng.NextDouble();              // 振幅权重
                    double ph = rng.NextDouble() * Math.PI * 2.0;
                    double kk = k * (0.75 + 0.5 * rng.NextDouble());        // 波长也抖 ±25%
                    sum += w * Math.Sin(kk * (x * Math.Cos(ai) + y * Math.Sin(ai)) + ph);
                    norm += w;
                }
                h = norm > 1e-9 ? sum / norm : 0.0;
            }
            return Shape(h, s.Crest);
        }

        /// <summary>波峰塑形：crest=0 正弦（圆滑）；越大越接近「平顶 + 陡壁」（tanh 压形，|h| ≤ 1 不变）</summary>
        internal static double Shape(double h, double crest)
        {
            if (crest <= 1e-9) return h;
            double g = 1.0 + Math.Min(1.0, Math.Max(0.0, crest)) * 3.0;
            double t = Math.Tanh(g);
            if (t < 1e-9) return h;
            return Math.Tanh(g * h) / t;
        }

        // ============================================================ 生成

        public static WaterRippleResult Generate(GeometryBase target, WaterRippleSettings s, out string report)
        {
            var sw = Stopwatch.StartNew();
            var res = new WaterRippleResult();
            var notes = new List<string>();
            report = "";
            if (s == null) { res.Error = "参数为空"; return res; }

            string why;
            Brep brep = ToBrep(target, out why);
            if (brep == null) { res.Error = why; return res; }

            double wl = Math.Max(1e-6, s.Wavelength);
            double amp = s.WaveHeight * 0.5;                     // 波高 = 峰到谷 → 半幅
            double spw = s.SamplesPerWave;
            if (spw < 4) spw = 4;
            if (spw > MaxSamplesPerWave) spw = MaxSamplesPerWave;
            double step = wl / spw;

            // ① 网格化：密度跟着波长走（这是「高光面光滑」的关键）
            Mesh mesh = null;
            try
            {
                var mp = new MeshingParameters();
                mp.MaximumEdgeLength = step;
                mp.MinimumEdgeLength = step * 0.2;
                mp.GridAspectRatio = 1.0;
                mp.RefineGrid = true;
                mp.SimplePlanes = false;
                Mesh[] parts = Mesh.CreateFromBrep(brep, mp);
                if (parts != null && parts.Length > 0)
                {
                    mesh = new Mesh();
                    for (int i = 0; i < parts.Length; i++) if (parts[i] != null) mesh.Append(parts[i]);
                }
            }
            catch (Exception ex) { notes.Add("网格化异常：" + ex.Message); }
            if (mesh == null || mesh.Faces.Count == 0) { res.Error = "网格化失败（目标太小或退化？）"; return res; }

            // 多重曲面：缝上顶点合并（不合并 → 跨面位移会裂开）
            try { mesh.Weld(Math.Max(1e-9, step * 1e-3)); } catch { }
            try { mesh.Faces.CullDegenerateFaces(); } catch { }
            try { mesh.FaceNormals.ComputeFaceNormals(); mesh.Normals.ComputeNormals(); } catch { }

            // ② 参考平面 + 2D 中心
            bool planar;
            Plane pl = RefPlane(brep, mesh, out planar);
            var uvs = new List<Point2d>(mesh.Vertices.Count);
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Point2d q = To2d(pl, mesh.Vertices.Point3dAt(i));
                uvs.Add(q);
                if (q.X < u0) u0 = q.X; if (q.X > u1) u1 = q.X;
                if (q.Y < v0) v0 = q.Y; if (q.Y > v1) v1 = q.Y;
            }
            double cu = (u0 + u1) * 0.5, cv = (v0 + v1) * 0.5;

            // ③ 边界（固定边界用）：网格裸边
            Polyline[] naked = null;
            double fadeW = s.Fade;
            if (s.LockBoundary)
            {
                try { naked = mesh.GetNakedEdges(); } catch { naked = null; }
                if (naked == null || naked.Length == 0)
                {
                    notes.Add("固定边界：目标没有开放边界（闭合体），按全幅处理");
                    naked = null;
                }
                else if (!(fadeW > 1e-9))
                {
                    fadeW = wl * 0.5;                       // 没给过渡宽度 → 用半个波长兜底，避免硬台阶
                    notes.Add(string.Format(CultureInfo.InvariantCulture, "固定边界：过渡宽度按 波长×0.5 = {0:0.###} 兜底", fadeW));
                }
            }

            // ④ 位移：沿顶点法向，相位取参考平面 2D 坐标（多重曲面跨面连续）
            double k = 2.0 * Math.PI / wl;
            double lo = double.MaxValue, hi = double.MinValue, minEdge = double.MaxValue;
            int moved = 0, locked = 0;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Point3d p = mesh.Vertices.Point3dAt(i);
                double h = WaveField(uvs[i].X, uvs[i].Y, s, k, cu, cv) * amp;
                if (naked != null && naked.Length > 0)
                {
                    double d = DistToPolylines(naked, p);
                    if (d < minEdge) minEdge = d;
                    double t = fadeW > 1e-9 ? d / fadeW : 1.0;
                    if (t > 1) t = 1;
                    if (d <= 1e-9) locked++;                 // 边界顶点：硬锁（一点不动）
                    h *= BlendCurve(t, s.BlendSmooth);
                }
                if (h < lo) lo = h;
                if (h > hi) hi = h;
                if (Math.Abs(h) > 1e-12) moved++;
                Vector3d n = mesh.Normals[i];
                if (!n.IsValid || n.Length < 1e-9) n = pl.ZAxis;
                n.Unitize();
                mesh.Vertices.SetVertex(i, p + n * h);
            }
            try { mesh.FaceNormals.ComputeFaceNormals(); mesh.Normals.ComputeNormals(); } catch { }

            res.Ripple = mesh;
            res.Faces = mesh.Faces.Count;
            res.Vertices = mesh.Vertices.Count;
            res.MinH = lo == double.MaxValue ? 0 : lo;
            res.MaxH = hi == double.MinValue ? 0 : hi;
            res.Wavelength = wl;
            res.WaveHeight = s.WaveHeight;
            res.BoundaryDistance = minEdge == double.MaxValue ? -1 : minEdge;

            // ⑤ 一键平滑 → 细分曲面
            if (s.Smooth)
            {
                try
                {
                    res.SmoothSubD = SubD.CreateFromMesh(mesh);
                    if (res.SmoothSubD == null) notes.Add("细分曲面转换失败（按网格输出）");
                    else if (s.LockBoundary)
                    {
                        // 固定边界时把 SubD 的边界打成 crease / 角点打成 corner：
                        // 否则 Catmull-Clark 的极限面会把边界往里收、四个角变圆（用户实测反馈）
                        int creased, corners;
                        CreaseSubDBoundary(res.SmoothSubD, out creased, out corners);
                        if (creased > 0)
                            notes.Add(string.Format(CultureInfo.InvariantCulture,
                                "细分曲面：边界已打 crease（{0} 条边 / {1} 个角点）→ 极限面贴住原边界、不收角", creased, corners));
                    }
                }
                catch (Exception ex) { notes.Add("细分曲面转换失败：" + ex.Message); }
            }

            string mode = s.WaveMode == 1 ? "定向条带" : (s.WaveMode == 2 ? "同心涟漪" : "有机水波");
            notes.Insert(0, string.Format(CultureInfo.InvariantCulture,
                "水波纹：{0} · 波长 {1:0.###} / 波高 {2:0.###} · 位移 {3:0.###}~{4:0.###} · {5} 顶点 / {6} 面 · {7:0.00}s{8}",
                mode, wl, s.WaveHeight, res.MinH, res.MaxH, res.Vertices, res.Faces, sw.Elapsed.TotalSeconds,
                planar ? "（平面输入）" : "（弯曲输入：按拟合平面取相位）"));
            if (s.Smooth) notes.Add(res.SmoothSubD != null ? "一键平滑：已转成细分曲面（SubD）" : "一键平滑：未生成细分曲面");
            if (s.LockBoundary)
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "固定边界：开（边界锁在原位置；过渡宽度 {0:0.###}，平滑度 {1:0.##}，锁住顶点 {2} 个）",
                    fadeW, s.BlendSmooth, locked));
            else
                notes.Add("固定边界：关（全幅到边，边界跟着起伏）");
            report = string.Join("；", notes.ToArray());
            res.Note = report;
            res.Seconds = sw.Elapsed.TotalSeconds;
            return res;
        }

        /// <summary>
        /// SubD 边界保形：**边界边 → crease（折痕）**、**边界顶点 → corner（角点）**。
        /// 不做这一步时，Catmull-Clark 的极限面会把边界往里收、四个角磨圆
        /// （用户实测：「一键平滑成 SubD 之后 边界就没有固定到 4 个节点上了」）。
        /// </summary>
        internal static void CreaseSubDBoundary(SubD sd, out int creased, out int corners)
        {
            creased = 0; corners = 0;
            if (sd == null) return;
            try
            {
                var bEdges = new List<SubDEdge>();
                foreach (SubDEdge e in sd.Edges)
                {
                    if (e == null || e.FaceCount != 1) continue;      // 边界边 = 只连一张面
                    e.Tag = SubDEdgeTag.Crease;
                    bEdges.Add(e);
                    creased++;
                }
                if (bEdges.Count == 0) return;
#if !RH7
                // Rhino 7 的 RhinoCommon 没有 SubDVertex.Tag / SetVertexTags（角点标记是 8 才有的 API）
                var bVerts = new List<SubDVertex>();
                for (int i = 0; i < bEdges.Count; i++)
                {
                    SubDVertex v1 = bEdges[i].VertexFrom, v2 = bEdges[i].VertexTo;
                    if (v1 != null) bVerts.Add(v1);
                    if (v2 != null) bVerts.Add(v2);
                }
                if (bVerts.Count > 0)
                {
                    sd.Vertices.SetVertexTags(bVerts, SubDVertexTag.Corner);
                    corners = bVerts.Count;
                }
                try { sd.UpdateAllTagsAndSectorCoefficients(); } catch { }
#endif
            }
            catch { }
        }

        // ============================================================ 工具

        /// <summary>3D → 参考平面 2D 坐标（相位用）</summary>
        internal static Point2d To2d(Plane pl, Point3d p)
        {
            Vector3d d = p - pl.Origin;
            return new Point2d(Vector3d.Multiply(d, pl.XAxis), Vector3d.Multiply(d, pl.YAxis));
        }

        /// <summary>目标摘要（面板/命令行提示用）</summary>
        internal static string Describe(RhinoObject o)
        {
            if (o == null) return "（已删除）";
            string name = null;
            try { name = o.Name; } catch { }
            string kind = "物件";
            try
            {
                GeometryBase g = o.Geometry;
                if (g is Curve) kind = "曲线";
                else if (g is Brep b) kind = b.Faces.Count > 1 ? "多重曲面" : "曲面";
                else if (g is Extrusion) kind = "挤出体";
                else if (g is Surface) kind = "曲面";
                else if (g is Mesh) kind = "网格";
                else if (g is SubD) kind = "细分物件";
                else if (g is Rhino.Geometry.Point) kind = "点";
            }
            catch { }
            return string.IsNullOrEmpty(name) ? kind : (name + "（" + kind + "）");
        }

        internal static double Smoothstep(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>
        /// 边界 → 纹理的过渡曲线：t = 到边界距离 / 过渡宽度（0 = 在边界上，1 = 已到全幅）。
        /// smooth = 0 线性；0.5 = smoothstep（默认，两端柔和）；1 = smootherstep（最柔，起步/收尾都缓）。
        /// </summary>
        internal static double BlendCurve(double t, double smooth)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (smooth < 0) smooth = 0; else if (smooth > 1) smooth = 1;
            double s1 = t;                                       // 线性
            double s2 = t * t * (3.0 - 2.0 * t);                 // smoothstep
            double s3 = t * t * t * (t * (6.0 * t - 15.0) + 10.0); // smootherstep
            if (smooth <= 0.5)
            {
                double u = smooth * 2.0;
                return s1 * (1 - u) + s2 * u;
            }
            double v = (smooth - 0.5) * 2.0;
            return s2 * (1 - v) + s3 * v;
        }

        /// <summary>点到多段线集合的最短距离（收平用）</summary>
        internal static double DistToPolylines(Polyline[] polys, Point3d p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < polys.Length; i++)
            {
                Polyline pl = polys[i];
                if (pl == null || pl.Count < 2) continue;
                for (int j = 0; j < pl.Count - 1; j++)
                {
                    double d = DistToSegment(p, pl[j], pl[j + 1]);
                    if (d < best) best = d;
                }
            }
            return best == double.MaxValue ? 0 : best;
        }

        internal static double DistToSegment(Point3d p, Point3d a, Point3d b)
        {
            Vector3d ab = b - a;
            double l2 = ab.SquareLength;
            if (l2 < 1e-18) return p.DistanceTo(a);
            double t = ((p - a) * ab) / l2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return p.DistanceTo(a + ab * t);
        }

        // ============================================================ 写文档

        /// <summary>
        /// 勾「一键平滑」→ 只写细分曲面（SubD，图层「水波纹-平滑」）；不勾 → 只写网格（图层「水波纹-网格」）。
        /// </summary>
        internal static void AddToDocument(RhinoDoc doc, List<WaterRippleResult> results,
            out int meshCount, out int smoothCount)
        {
            meshCount = 0; smoothCount = 0;
            if (doc == null || results == null) return;
            int lm = EnsureLayer(doc, LayerMesh, Color.FromArgb(96, 148, 208));
            int ls = EnsureLayer(doc, LayerSmooth, Color.FromArgb(64, 148, 208));
            var am = new ObjectAttributes(); am.LayerIndex = lm;
            var asm = new ObjectAttributes(); asm.LayerIndex = ls;
            for (int i = 0; i < results.Count; i++)
            {
                WaterRippleResult r = results[i];
                if (r == null) continue;
                if (r.SmoothSubD != null)
                {
                    if (doc.Objects.AddSubD(r.SmoothSubD, asm) != Guid.Empty) smoothCount++;
                    continue;
                }
                if (r.Ripple != null && r.Ripple.Faces.Count > 0)
                {
                    if (doc.Objects.AddMesh(r.Ripple, am) != Guid.Empty) meshCount++;
                }
            }
        }

        internal static int EnsureLayer(RhinoDoc doc, string name, Color color)
        {
            int idx = doc.Layers.FindByFullPath(name, -1);
            if (idx >= 0) return idx;
            var layer = new Layer { Name = name, Color = color };
            return doc.Layers.Add(layer);
        }
    }
}
