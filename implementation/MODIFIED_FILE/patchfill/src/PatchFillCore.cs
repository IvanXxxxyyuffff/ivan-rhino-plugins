using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace PatchFillPattern
{
    /// <summary>多边补面参数（面板全部行都要可用，不许灰掉）</summary>
    public class PatchFillSettings
    {
        public int ControlCount = 12;        // 控制点数（每向基础控制点数）4~40
        public double FitStrength = 1.0;     // 贴合强度 0..1：0 = 只用 Coons 基面，1 = 完全贴相邻曲面
        public double Smooth = 0.15;         // 平滑度 0..1：映射薄板能量正则项 λ
        public int Continuity = 0;           // 0 = G0（位置），1 = G1（相切），2 = G2（曲率）
        public double G1Distance = 1.0;      // G1 采样距离（mm）：沿相邻面切平面向洞内偏移的距离
        public bool KeepHoles = true;        // 保留内孔（内孔环投影到结果面做修剪）
        public bool ShowSourceBoundary = true; // 预览里画原边界（红线）
        public double MmToModel = 1.0;       // 面板 mm → 模型单位

        public PatchFillSettings Clone()
        {
            return new PatchFillSettings
            {
                ControlCount = ControlCount,
                FitStrength = FitStrength,
                Smooth = Smooth,
                Continuity = Continuity,
                G1Distance = G1Distance,
                KeepHoles = KeepHoles,
                ShowSourceBoundary = ShowSourceBoundary,
                MmToModel = MmToModel
            };
        }
    }

    /// <summary>一条边界边：曲线几何 + 所属 Brep（独立曲线为 null）+ 相邻面（G1 用）</summary>
    public class PatchFillEdge
    {
        public Curve Curve;              // 精确几何（曲线 / 曲面边）
        public Brep Host;                // 所属 Brep（独立曲线 = null）
        public int HostEdgeIndex = -1;   // Host.Edges 里的序号
        public int[] AdjacentFaces;      // Host 里的相邻面序号（G1 与参考几何用）
        public string Source = "";       // 诊断文字（对象名 + 边序号）

        public PatchFillEdge() { }

        public PatchFillEdge(Curve c, Brep host, int edgeIndex, int[] faces, string source)
        {
            Curve = c; Host = host; HostEdgeIndex = edgeIndex; AdjacentFaces = faces; Source = source;
        }
    }

    /// <summary>补面结果（报告字段一次给全：逐边缝隙 + 偏差 + G1 夹角 + 位掩码告警）</summary>
    public class PatchFillResult
    {
        public Brep Result;                  // 输出：一张 NURBS 面（可带内孔修剪环）
        public NurbsSurface Surface;         // 底层曲面（诊断/自检用）
        public int ControlU, ControlV;       // 实际控制点数（含拐角插节点带来的额外控制点）
        public int Edges;                    // 输入边数
        public int Corners;                  // 4 角是否有效（4 = 有效）
        public string CornerMethod = "";     // 曲率 / 对角极值 / 弧长四等分
        public int KinkKnots;                // 插入的拐角节点数
        public double MaxDeviation;          // 结果面 → 参考几何 的最大偏差
        public double RmsDeviation;          // 均方根偏差
        public double BoundaryDeviation;     // 边界偏差（逐边缝隙的最大值）
        public double[] EdgeGaps = new double[4];  // 逐边缝隙（v=0 / u=1 / v=1 / u=0）
        public double MaxNormalAngleDeg = -1;      // G1 时与相邻面法向的最大夹角（度）
        public double MaxNormalAngleInnerDeg = -1; // 同上，但不含 4 个参数角点（诊断：角点是切分参数位置）
        public int G1Samples;                       // 参与 G1 统计的采样点数
        public double MaxSecondDiffAngleDeg = -1;   // G2：边界二阶差分（法曲率方向）与相邻面的最大夹角（度，-1 = 未测）
        public int G2Samples;                       // 参与 G2 统计的采样点数
        public int WorstAngleSide = -1;              // 最差夹角的位置（诊断：哪条边）
        public double WorstAngleF = -1;              // 最差夹角的位置（诊断：边上参数 0~1）
        public Vector3d WorstAngleNS = Vector3d.Zero; // 最差位置的结果面法向（诊断）
        public Vector3d WorstAngleNA = Vector3d.Zero; // 最差位置的相邻面法向（诊断）
        public Point3d WorstAngleP = Point3d.Origin;  // 最差位置的边界点（诊断）
        public int Rounds;                          // 残差迭代实际轮数
        public int AdaptiveKnots;                   // 残差驱动局部插结的数量（0 = 没插）
        public int FinalControlU, FinalControlV;    // 最后一轮（含插结）的控制网尺寸（对比 best 那一轮）
        public double FirstRoundDeviation;          // 第 0 轮（未迭代）的偏差 —— 自检里对比「迭代有没有用」
        public List<string> RoundLog = new List<string>();   // 逐轮：偏差 / 缝隙 / G1 夹角（报告里可见）
        public double Seconds;
        public int WarnFlags;                       // 位掩码告警（见 WarnText）
        public string WarnText = "";
        public bool HasReference;                   // 有参考几何（相邻面 / 外部参考）
        public int Folded;                          // 折叠单元数（相对基面法向翻转）
        public double CurvatureJump;                 // 曲率连续性近似：最外两排控制点「离散曲率」最大差
        public bool Trimmed;                        // 是否做了内孔修剪
        public int Holes;                           // 成功保留的内孔数
        public int HolesFound;                      // 检出的内孔环数
        public int Loops;                           // 输入边链化出的环总数
        public double Diag;                         // 包围盒对角线
        public string Note = "";
        public string Error = "";
        public List<Polyline> SourceBoundary = new List<Polyline>();   // 预览用：原边界

        public bool Ok { get { return Result != null && Result.Faces.Count > 0; } }
    }

    /// <summary>
    /// 多边补面核心：一圈边界（N ≥ 2 条边）→ 一张光滑 NURBS 面。
    /// 算法（自研，无第三方数值库）：
    ///   ① 边界链化成一圈 → 弧长密折线（带每条边的相邻面法向，G1 用）
    ///   ② 4 角三级回退（曲率 → 对角极值 → 弧长四等分）
    ///   ③ 3D 双线性 Coons 基面（边界点 = 原边界采样点，G0 天然精确）
    ///   ④ 夹持非均匀节点 + 加权最小二乘（XNURBS 路线：约束最小二乘 + 能量层）：
    ///      数据行(1) / 边界行(1e6) / G1 切线带(0.1) / G2 曲率带(30：双环位置行 + 二阶差分行) /
    ///      内部约束(10) + 薄板能量正则 λ + 面积压力（防塌缩充气，对应 VK_add_area_pressure）；
    ///      自己写稠密对称正定 Cholesky 解 (AᵀWA+λR)x = AᵀWb
    ///   ⑤ 残差驱动迭代（≤4 轮，节点向量不动）：误差大的地方补参考面采样点 → 重解；达标早退（T1）
    ///   ⑥ 输出一张 NURBS 面 + 逐边缝隙 / 偏差 / G1 夹角 / G2 二阶差分夹角 / 位掩码告警
    /// </summary>
    public static class PatchFillCore
    {
        internal const string LayerPatch = "多边补面";
        public const string EmptyTargetHint = "先选一圈边界：曲面边（可子物件选边）或曲线，首尾相连成闭环；可选内部曲线 / 点";

        internal const int Degree = 3;
        internal const int MinControl = 4;
        internal const int MaxControl = 40;

        // 约束行权重（照 XNURBS 分析给出的口径）
        internal const double WData = 1.0;        // 数据行（Coons 基面采样）
        internal const double WBoundary = 1e6;    // 边界行（硬约束）
        internal const double WTangent = 0.1;     // G1 切线带
        internal const double WInner = 10.0;      // 内部曲线 / 点采样

        // 告警位（照 VK_get_warning_message 的位掩码语义）
        internal const int WarnEdgeGap = 1;       // bit0 边界存在间隙且位置要求可能无法严格满足
        internal const int WarnTangent = 2;       // bit1 相邻几何切线方向不一致
        internal const int WarnNotConverged = 4;  // bit2 残差迭代未收敛
        internal const int WarnDegenerate = 8;    // bit3 结果退化（折叠 / 边界过短）
        internal const int WarnCurvature = 16;    // bit4 曲率（G2）与相邻面不一致


        // ============================================================ 目标解析

        /// <summary>把 ObjRef（曲线物件 / 曲面边 / 子物件边）解析成一条边界边</summary>
        internal static bool TryResolveEdge(ObjRef objRef, out PatchFillEdge edge, out string why)
        {
            edge = null; why = "";
            if (objRef == null) { why = "没有选中对象"; return false; }
            RhinoObject obj = objRef.Object();
            if (obj == null) { why = "对象已删除"; return false; }
            ComponentIndex ci = ComponentIndex.Unset;
            try { ci = objRef.GeometryComponentIndex; } catch { ci = ComponentIndex.Unset; }
            return ResolveEdgeFromComponent(obj, ci, out edge, out why);
        }

        /// <summary>
        /// 由「物件 + 子物件组件」解析一条边界边：
        /// 曲线物件 → 整条曲线；BrepEdge 子物件 → 那条边（带相邻面，G1 用）。
        /// </summary>
        internal static bool ResolveEdgeFromComponent(RhinoObject obj, ComponentIndex ci,
            out PatchFillEdge edge, out string why)
        {
            edge = null; why = "";
            if (obj == null) { why = "对象已删除"; return false; }

            // ① 子物件边（曲面边）
            if (ci.ComponentIndexType == ComponentIndexType.BrepEdge)
            {
                Brep host = null;
                try { host = obj.Geometry as Brep; } catch { host = null; }
                if (host == null)
                {
                    try { host = Brep.TryConvertBrep(obj.Geometry); } catch { host = null; }
                }
                if (host == null || ci.Index < 0 || ci.Index >= host.Edges.Count)
                {
                    why = "曲面边无效（索引越界或对象不是曲面）";
                    return false;
                }
                Curve c = null;
                try { c = host.Edges[ci.Index].DuplicateCurve(); } catch { c = null; }
                if (c == null || !c.IsValid) { why = "这条边的曲线无效"; return false; }
                int[] faces = null;
                try { faces = host.Edges[ci.Index].AdjacentFaces(); } catch { faces = null; }
                edge = new PatchFillEdge(c, host, ci.Index, faces,
                    Describe(obj) + " · 边" + (ci.Index + 1));
                return true;
            }

            // ② 整条曲线
            Curve gc = null;
            try { gc = obj.Geometry as Curve; } catch { gc = null; }
            if (gc == null) { why = "选到的不是曲线 / 曲面边（点、曲面、网格都不行；选曲面请点它的边）"; return false; }
            if (!gc.IsValid) { why = "曲线几何无效"; return false; }
            edge = new PatchFillEdge(gc, null, -1, null, Describe(obj) + "（独立曲线，无相邻面）");
            return true;
        }

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
                else if (g is Mesh) kind = "网格";
                else if (g is SubD) kind = "细分物件";
                else if (g is Rhino.Geometry.Point) kind = "点";
            }
            catch { }
            return string.IsNullOrEmpty(name) ? kind : (name + "（" + kind + "）");
        }

        // ============================================================ 环

        /// <summary>闭合环的弧长参数化折线（P[i] → P[i+1]，最后一段回 0）</summary>
        internal sealed class LoopData
        {
            public Point3d[] P;
            public double[] Cum;        // 长度 = P.Length + 1
            public double Length;
            public Vector3d[] N;        // 每个顶点处相邻面的法向（无相邻面 = 零向量）
            public int[] EdgeOf;        // 每个顶点来自哪条输入边（-1 = 未知）

            public static LoopData Build(List<Point3d> pts, List<Vector3d> normals, List<int> edgeOf)
            {
                if (pts == null || pts.Count < 3) return null;
                var ld = new LoopData();
                var list = new List<Point3d>(pts);
                var nlist = normals != null ? new List<Vector3d>(normals) : null;
                var elist = edgeOf != null ? new List<int>(edgeOf) : null;
                // 去掉首尾重复点
                while (list.Count > 3 && list[0].DistanceTo(list[list.Count - 1]) < 1e-9)
                {
                    list.RemoveAt(list.Count - 1);
                    if (nlist != null && nlist.Count > 0) nlist.RemoveAt(nlist.Count - 1);
                    if (elist != null && elist.Count > 0) elist.RemoveAt(elist.Count - 1);
                }
                if (list.Count < 3) return null;
                ld.P = list.ToArray();
                int cnt = ld.P.Length;
                ld.N = new Vector3d[cnt];
                ld.EdgeOf = new int[cnt];
                for (int i = 0; i < cnt; i++)
                {
                    ld.N[i] = (nlist != null && i < nlist.Count) ? nlist[i] : Vector3d.Zero;
                    ld.EdgeOf[i] = (elist != null && i < elist.Count) ? elist[i] : -1;
                }
                ld.Cum = new double[cnt + 1];
                ld.Cum[0] = 0;
                for (int i = 0; i < cnt; i++)
                    ld.Cum[i + 1] = ld.Cum[i] + ld.P[i].DistanceTo(ld.P[(i + 1) % cnt]);
                ld.Length = ld.Cum[cnt];
                if (!(ld.Length > 1e-12)) return null;
                return ld;
            }

            public int Next(int i) { return (i + 1) % P.Length; }
            public double Param(int i) { return Cum[i]; }

            /// <summary>第 i 段（P[i] → P[i+1]）的长度</summary>
            public double SegLen(int i) { return Cum[i + 1] - Cum[i]; }

            /// <summary>顶点索引 → 该顶点处的相邻面法向（最近的已知非零法向）</summary>
            public Vector3d NormalNear(int i)
            {
                if (N == null || N.Length == 0) return Vector3d.Zero;
                int cnt = N.Length;
                for (int k = 0; k < 12; k++)
                {
                    Vector3d a = N[((i + k) % cnt + cnt) % cnt];
                    if (a.IsValid && a.Length > 1e-12) return a;
                    Vector3d b = N[((i - k) % cnt + cnt) % cnt];
                    if (b.IsValid && b.Length > 1e-12) return b;
                }
                return Vector3d.Zero;
            }
        }

        /// <summary>环上的点（弧长参数，自动取模）</summary>
        internal static Point3d PointAtArc(LoopData ld, double target)
        {
            if (ld == null) return Point3d.Origin;
            double L = ld.Length;
            if (!(L > 1e-12)) return ld.P[0];
            double s = target % L;
            if (s < 0) s += L;
            int cnt = ld.P.Length;
            int lo = 0, hi = cnt;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (ld.Cum[mid + 1] < s) lo = mid + 1; else hi = mid;
            }
            int i = Math.Min(lo, cnt - 1);
            double segLen = ld.Cum[i + 1] - ld.Cum[i];
            double t = segLen > 1e-15 ? (s - ld.Cum[i]) / segLen : 0.0;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            Point3d a = ld.P[i], b = ld.P[(i + 1) % cnt];
            return new Point3d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }

        /// <summary>环上的相邻面法向（弧长参数，取最近顶点的法向）</summary>
        internal static Vector3d NormalAtArc(LoopData ld, double target)
        {
            if (ld == null || ld.N == null || ld.N.Length == 0) return Vector3d.Zero;
            double L = ld.Length;
            double s = target % L;
            if (s < 0) s += L;
            int cnt = ld.P.Length;
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < cnt; i++)
            {
                double d = Math.Abs(ld.Cum[i] - s);
                if (d < bd) { bd = d; best = i; }
            }
            return ld.NormalNear(best);
        }

        /// <summary>环上某点处的切向（弧长参数，中心差分）</summary>
        internal static Vector3d TangentAtArc(LoopData ld, double target)
        {
            double h = Math.Max(ld.Length * 1e-4, 1e-9);
            Point3d a = PointAtArc(ld, target - h);
            Point3d b = PointAtArc(ld, target + h);
            Vector3d t = b - a;
            if (t.Length > 1e-15) t.Unitize();
            return t;
        }

        /// <summary>
        /// 把若干条边按端点串成一圈（弧长密折线）。返回主环（面积最大）与其余环（内孔候选）。
        /// 不成圈 / 端点数不对时给出明确理由。
        /// </summary>
        internal static bool ChainLoops(List<PatchFillEdge> edges, double diag,
            out List<LoopData> loops, out List<int> edgeCount, out string why)
        {
            loops = null; edgeCount = null; why = "";
            if (edges == null || edges.Count == 0) { why = "没有边界边"; return false; }
            double tol = Math.Max(diag * 1e-6, 1e-9);
            double step = Math.Max(diag * 1.5e-3, 1e-9);

            var used = new bool[edges.Count];
            var result = new List<LoopData>();
            var counts = new List<int>();
            int guardLoops = 0;
            while (true)
            {
                int start = -1;
                for (int i = 0; i < edges.Count; i++) if (!used[i]) { start = i; break; }
                if (start < 0) break;
                if (++guardLoops > edges.Count + 2) break;

                var pts = new List<Point3d>();
                var nrm = new List<Vector3d>();
                var eof = new List<int>();
                int cur = start;
                Point3d chainEnd;
                AppendEdge(edges[cur], false, step, pts, nrm, eof, cur);
                chainEnd = pts[pts.Count - 1];
                used[cur] = true;
                int usedCount = 1;

                while (true)
                {
                    int found = -1; bool rev = false;
                    for (int i = 0; i < edges.Count; i++)
                    {
                        if (used[i]) continue;
                        Point3d a, b;
                        EdgeEnds(edges[i], out a, out b);
                        if (a.DistanceTo(chainEnd) <= tol) { found = i; rev = false; break; }
                        if (b.DistanceTo(chainEnd) <= tol) { found = i; rev = true; break; }
                    }
                    if (found < 0) break;
                    AppendEdge(edges[found], rev, step, pts, nrm, eof, found);
                    chainEnd = pts[pts.Count - 1];
                    used[found] = true;
                    usedCount++;
                    if (usedCount >= edges.Count) break;
                }

                // 闭环：最后一个点要回到起点
                bool closed = pts.Count >= 4 && pts[pts.Count - 1].DistanceTo(pts[0]) <= Math.Max(tol, step * 0.6);
                if (!closed)
                {
                    double gap = pts.Count > 0 ? pts[pts.Count - 1].DistanceTo(pts[0]) : 0;
                    why = string.Format(CultureInfo.InvariantCulture,
                        "边界不闭合：链了 {0} 条边后，末端点与起点相距 {1:0.####}（容差 {2:0.####}）。请检查每条边首尾相连、方向一致",
                        usedCount, gap, Math.Max(tol, step * 0.6));
                    return false;
                }
                if (pts.Count > 0) { pts.RemoveAt(pts.Count - 1); nrm.RemoveAt(nrm.Count - 1); eof.RemoveAt(eof.Count - 1); }

                LoopData ld = LoopData.Build(pts, nrm, eof);
                if (ld == null)
                {
                    why = "有一条环退化（点数不足 3 或周长≈0）";
                    return false;
                }
                result.Add(ld);
                counts.Add(usedCount);
            }

            if (result.Count == 0) { why = "没有解析出任何闭合边界"; return false; }
            loops = result; edgeCount = counts;
            return true;
        }

        static void EdgeEnds(PatchFillEdge e, out Point3d a, out Point3d b)
        {
            a = Point3d.Origin; b = Point3d.Origin;
            try
            {
                a = e.Curve.PointAtStart;
                b = e.Curve.PointAtEnd;
            }
            catch { }
        }

        /// <summary>把一条边按弧长采样追加进链（reverse = 反向）；每个采样点单独取相邻面法向（G1 用）</summary>
        static void AppendEdge(PatchFillEdge e, bool reverse, double step,
            List<Point3d> pts, List<Vector3d> nrm, List<int> eof, int edgeIndex)
        {
            Curve c = e.Curve;
            double len = 0;
            try { len = c.GetLength(); } catch { len = 0; }
            if (!(len > 1e-12)) return;
            int m = Math.Max(6, Math.Min(400, (int)Math.Ceiling(len / step)));
            int fi;
            BrepFace face = MainAdjacentFace(e, out fi);
            for (int k = 0; k <= m; k++)
            {
                double f = reverse ? (1.0 - k / (double)m) : (k / (double)m);
                Point3d p;
                try { p = c.PointAtLength(len * f); } catch { continue; }
                if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(p) < 1e-12) continue;   // 接缝去重
                pts.Add(p);
                nrm.Add(NormalOnFace(face, p));
                eof.Add(edgeIndex);
            }
        }

        /// <summary>面上某点的法向（相邻面在边界上法向是变化的：球面/圆柱面必须逐点取）</summary>
        internal static Vector3d NormalOnFace(BrepFace f, Point3d p)
        {
            if (f == null) return Vector3d.Zero;
            try
            {
                double u, v;
                if (!f.ClosestPoint(p, out u, out v)) return Vector3d.Zero;
                Vector3d n = f.NormalAt(u, v);
                if (!n.IsValid || n.Length < 1e-12) return Vector3d.Zero;
                n.Unitize();
                return n;
            }
            catch { return Vector3d.Zero; }
        }

        /// <summary>边的代表法向：相邻面里面积最大的那张面（G1 与参考几何都用它）</summary>
        internal static Vector3d FaceNormalFor(PatchFillEdge e)
        {
            BrepFace f = MainAdjacentFace(e, out _);
            if (f == null) return Vector3d.Zero;
            try
            {
                Point3d p = e.Curve.PointAt(e.Curve.Domain.Mid);
                double u, v;
                if (f.ClosestPoint(p, out u, out v))
                {
                    Vector3d n = f.NormalAt(u, v);
                    if (n.IsValid && n.Length > 1e-12) { n.Unitize(); return n; }
                }
            }
            catch { }
            return Vector3d.Zero;
        }

        /// <summary>相邻面里面积最大的那张（洞边往往贴着薄壁 + 主面，取主面）</summary>
        internal static BrepFace MainAdjacentFace(PatchFillEdge e, out int faceIndex)
        {
            faceIndex = -1;
            if (e == null || e.Host == null || e.AdjacentFaces == null || e.AdjacentFaces.Length == 0) return null;
            BrepFace best = null; double bestArea = -1;
            for (int i = 0; i < e.AdjacentFaces.Length; i++)
            {
                int fi = e.AdjacentFaces[i];
                if (fi < 0 || fi >= e.Host.Faces.Count) continue;
                BrepFace f = e.Host.Faces[fi];
                double a = 0;
                try { a = f.ToBrep().GetArea(); } catch { a = 0; }
                if (a > bestArea) { bestArea = a; best = f; faceIndex = fi; }
            }
            return best;
        }

        // ============================================================ 参考几何

        /// <summary>参考几何 = 相邻面的「未修剪底层曲面」+ 用户显式给的参考（用于偏差度量与残差迭代）</summary>
        internal sealed class Reference
        {
            public List<Brep> Faces = new List<Brep>();

            public bool Any { get { return Faces.Count > 0; } }

            public static Reference FromEdges(List<PatchFillEdge> edges)
            {
                var r = new Reference();
                var seen = new HashSet<string>();
                for (int i = 0; i < edges.Count; i++)
                {
                    int fi;
                    BrepFace f = MainAdjacentFace(edges[i], out fi);
                    if (f == null) continue;
                    string key = (edges[i].Host != null ? edges[i].Host.GetHashCode().ToString() : "x") + ":" + fi;
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    try
                    {
                        Surface us = f.UnderlyingSurface();
                        if (us == null) continue;
                        Brep b = Brep.CreateFromSurface(us);
                        if (b != null && b.Faces.Count > 0) r.Faces.Add(b);
                    }
                    catch { }
                }
                return r;
            }

            public void AddExplicit(GeometryBase g)
            {
                if (g == null) return;
                try
                {
                    if (g is Brep b) { if (b.Faces.Count > 0) Faces.Add(b); return; }
                    if (g is Surface s) { Brep nb = Brep.CreateFromSurface(s); if (nb != null) Faces.Add(nb); return; }
                    if (g is Mesh m) { Brep nb = Brep.CreateFromMesh(m, false); if (nb != null && nb.Faces.Count > 0) Faces.Add(nb); return; }
                }
                catch { }
            }

            /// <summary>
            /// 最近点（超过 cap 视为没命中）。atEdge = 最近点落在参考面的参数域边界上 ——
            /// 说明参考几何没有覆盖洞的内部（如「旋转出来的球带」其底层曲面只到纬线），
            /// 这种采样点不能当内部约束用（会把面拉回洞边、直接折掉）。
            /// </summary>
            public bool Closest(Point3d p, double cap, out Point3d q, out Vector3d n, out double dist)
            {
                bool atEdge;
                return Closest(p, cap, out q, out n, out dist, out atEdge);
            }

            public bool Closest(Point3d p, double cap, out Point3d q, out Vector3d n, out double dist, out bool atEdge)
            {
                q = p; n = Vector3d.Zero; dist = double.MaxValue; atEdge = false;
                bool any = false;
                int bestFace = -1;
                for (int i = 0; i < Faces.Count; i++)
                {
                    Point3d cq = Point3d.Origin; ComponentIndex ci = ComponentIndex.Unset; double s = 0, t = 0; Vector3d nn = Vector3d.Zero;
                    bool ok = false;
                    try { ok = Faces[i].ClosestPoint(p, out cq, out ci, out s, out t, cap, out nn); } catch { ok = false; }
                    if (!ok) continue;
                    double d = p.DistanceTo(cq);
                    if (d < dist)
                    {
                        dist = d; q = cq; n = nn; any = true; bestFace = i;
                        atEdge = IsAtDomainEdge(Faces[i], ci, s, t);
                    }
                }
                if (n.IsValid && n.Length > 1e-12) n.Unitize();
                return any;
            }

            /// <summary>最近点是否落在参数域边界（只在最近的那个面上判）</summary>
            static bool IsAtDomainEdge(Brep b, ComponentIndex ci, double s, double t)
            {
                try
                {
                    if (b == null || b.Faces.Count == 0) return false;
                    BrepFace f = null;
                    if (ci.ComponentIndexType == ComponentIndexType.BrepFace && ci.Index >= 0 && ci.Index < b.Faces.Count)
                        f = b.Faces[ci.Index];
                    if (f == null) f = b.Faces[0];
                    Surface us = f.UnderlyingSurface();
                    if (us == null) return false;
                    Interval du = us.Domain(0), dv = us.Domain(1);
                    double eu = (du.Max - du.Min) * 1e-9, ev = (dv.Max - dv.Min) * 1e-9;
                    if (s <= du.Min + eu || s >= du.Max - eu) return true;
                    if (t <= dv.Min + ev || t >= dv.Max - ev) return true;
                }
                catch { }
                return false;
            }
        }

        // ============================================================ 4 角

        internal static Point2d To2d(Plane pl, Point3d p)
        {
            Vector3d d = p - pl.Origin;
            return new Point2d(d * pl.XAxis, d * pl.YAxis);
        }

        internal static Plane FitLoopPlane(LoopData ld, Plane fallback)
        {
            if (ld == null || ld.P.Length < 3) return fallback;
            try
            {
                var pts = new List<Point3d>(ld.P.Length);
                for (int i = 0; i < ld.P.Length; i++) pts.Add(ld.P[i]);
                Plane fit;
                PlaneFitResult r = Plane.FitPlaneToPoints(pts, out fit);
                if (r != PlaneFitResult.Failure && fit.IsValid) return fit;
            }
            catch { }
            return fallback;
        }

        /// <summary>主轴坐标系：把外环的主方向当 X 轴，法向当 Z</summary>
        internal static Plane PrincipalFrame(LoopData outer, Plane fit)
        {
            double sxx = 0, sxy = 0, syy = 0, mx = 0, my = 0;
            for (int i = 0; i < outer.P.Length; i++)
            {
                Point2d q = To2d(fit, outer.P[i]);
                mx += q.X; my += q.Y;
            }
            mx /= outer.P.Length; my /= outer.P.Length;
            for (int i = 0; i < outer.P.Length; i++)
            {
                Point2d q = To2d(fit, outer.P[i]);
                double dx = q.X - mx, dy = q.Y - my;
                sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
            }
            double theta = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            Vector3d x = fit.XAxis * Math.Cos(theta) + fit.YAxis * Math.Sin(theta);
            Vector3d z = fit.ZAxis;
            Vector3d y = Vector3d.CrossProduct(z, x);
            if (!x.IsValid || !y.IsValid || x.Length < 1e-9 || y.Length < 1e-9) return fit;
            x.Unitize(); y.Unitize(); z.Unitize();
            Point3d origin = fit.Origin + fit.XAxis * mx + fit.YAxis * my;
            return new Plane(origin, x, y);
        }

        /// <summary>环的有向面积（平面坐标系，鞋带公式）</summary>
        internal static double LoopArea2d(LoopData ld, Plane pl)
        {
            double a = 0;
            for (int i = 0; i < ld.P.Length; i++)
            {
                Point2d p = To2d(pl, ld.P[i]);
                Point2d q = To2d(pl, ld.P[ld.Next(i)]);
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5;
        }

        /// <summary>
        /// 4 角（连续弧长参数 + 顶点索引）：
        /// ① 曲率优先（候选恰好 4 个、每段 ≥5% 周长）② 对角极值 ③ 弧长四等分。
        /// 曲率候选会做「窗口细化 + 折线顶点吸附」——多边形拐角落在折线顶点上时能取到精确角点。
        /// </summary>
        internal static bool CornerArcParams(LoopData ld, Plane frame, out double[] arc, out int[] idx,
            out string method, out bool fallback)
        {
            arc = new double[4]; idx = null; method = ""; fallback = false;
            int cnt = ld.P.Length;
            if (cnt < 4)
            {
                idx = QuarterByArc(ld);
                for (int k = 0; k < 4; k++) arc[k] = ld.Param(idx[k]);
                fallback = true; method = "弧长四等分";
                return true;
            }

            // ① 曲率优先
            var kk = CurvatureKinkParams(ld, 200, 15.0, 12);
            if (kk.Count == 4)
            {
                kk.Sort();
                bool ok = true;
                double minGap = ld.Length * 0.05;
                for (int k = 0; k < 4; k++)
                {
                    double p0 = kk[k], p1 = kk[(k + 1) % 4];
                    double gap = k == 3 ? (ld.Length - p0 + p1) : (p1 - p0);
                    if (gap < minGap) { ok = false; break; }
                }
                if (ok)
                {
                    for (int k = 0; k < 4; k++) arc[k] = kk[k];
                    idx = NearestVertexIndices(ld, arc);
                    method = "曲率";
                    return true;
                }
            }

            // ② 对角极值
            var cand = new int[4];
            double[] sx = { 1, -1, -1, 1 };
            double[] sy = { 1, 1, -1, -1 };
            for (int k = 0; k < 4; k++)
            {
                double best = double.MinValue; int bestI = 0;
                for (int i = 0; i < cnt; i++)
                {
                    Point2d q = To2d(frame, ld.P[i]);
                    double v = sx[k] * q.X + sy[k] * q.Y;
                    if (v > best) { best = v; bestI = i; }
                }
                cand[k] = bestI;
            }
            var list = new List<int>();
            for (int k = 0; k < 4; k++)
                if (!list.Contains(cand[k])) list.Add(cand[k]);
            if (list.Count == 4)
            {
                list.Sort(delegate (int a, int b) { return ld.Param(a).CompareTo(ld.Param(b)); });
                bool ok = true;
                double minGap = ld.Length * 0.05;
                for (int k = 0; k < 4; k++)
                {
                    double p0 = ld.Param(list[k]);
                    double p1 = ld.Param(list[(k + 1) % 4]);
                    double gap = k == 3 ? (ld.Length - p0 + p1) : (p1 - p0);
                    if (gap < minGap) { ok = false; break; }
                }
                if (ok)
                {
                    idx = list.ToArray();
                    for (int k = 0; k < 4; k++) arc[k] = ld.Param(idx[k]);
                    method = "对角极值";
                    return true;
                }
            }

            // ③ 弧长四等分
            idx = QuarterByArc(ld);
            for (int k = 0; k < 4; k++) arc[k] = ld.Param(idx[k]);
            fallback = true; method = "弧长四等分";
            return true;
        }

        /// <summary>把弧长参数映射到最近的折线顶点索引（报告/兼容用）</summary>
        internal static int[] NearestVertexIndices(LoopData ld, double[] arc)
        {
            var idx = new int[4];
            for (int k = 0; k < 4; k++)
            {
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < ld.P.Length; i++)
                {
                    double d = Math.Abs(ld.Param(i) - arc[k]);
                    if (d < bd) { bd = d; best = i; }
                }
                idx[k] = best;
            }
            return idx;
        }

        /// <summary>
        /// 4 角三级回退（顶点索引版，报告与自检用）
        /// </summary>
        internal static void Corners(LoopData ld, Plane frame, out int[] idx, out bool fallback, out string method)
        {
            double[] arc;
            CornerArcParams(ld, frame, out arc, out idx, out method, out fallback);
        }

        internal static int[] QuarterByArc(LoopData ld)
        {
            var res = new int[4];
            for (int k = 0; k < 4; k++)
            {
                double target = ld.Length * k / 4.0;
                int bestI = 0; double bestD = double.MaxValue;
                for (int i = 0; i < ld.P.Length; i++)
                {
                    double d = Math.Abs(ld.Param(i) - target);
                    if (d < bestD) { bestD = d; bestI = i; }
                }
                res[k] = bestI;
            }
            return res;
        }

        /// <summary>
        /// 全部拐角（局部转角极大且 &gt; minAngleDeg）的**弧长位置**（已细化 + 顶点吸附）。
        /// 拐角节点 multiplicity = 3 → 边界曲线能精确表达折线（多边形洞边界缝隙 = 0 的关键）。
        /// </summary>
        internal static List<double> CurvatureKinkParams(LoopData ld, int samples, double minAngleDeg, int maxCount)
        {
            var res = new List<double>();
            if (ld == null || ld.P.Length < 3 || !(ld.Length > 1e-12)) return res;
            if (samples < 32) samples = 32;
            double step = ld.Length / samples;
            var pt = new Point3d[samples];
            for (int k = 0; k < samples; k++) pt[k] = PointAtArc(ld, step * k);
            var ang = TurnAngles(pt, samples);
            var cand = new List<int>();
            for (int k = 0; k < samples; k++)
            {
                if (ang[k] <= minAngleDeg) continue;
                if (ang[k] > ang[(k + samples - 1) % samples] && ang[k] >= ang[(k + 1) % samples])
                    cand.Add(k);
            }
            var pos = new List<double>();
            var pow = new List<double>();
            for (int i = 0; i < cand.Count; i++)
            {
                double fine, fineAng;
                RefineKink(ld, step * cand[i], step, minAngleDeg, out fine, out fineAng);
                pos.Add(fine); pow.Add(fineAng);
            }
            var order = new List<int>();
            for (int i = 0; i < pos.Count; i++) order.Add(i);
            order.Sort(delegate (int a, int b) { return pow[b].CompareTo(pow[a]); });
            var keep = new List<double>();
            for (int i = 0; i < order.Count && keep.Count < maxCount; i++)
            {
                double s = pos[order[i]];
                bool near = false;
                for (int k = 0; k < keep.Count; k++)
                {
                    double d = Math.Abs(keep[k] - s);
                    d = Math.Min(d, ld.Length - d);
                    if (d < step) { near = true; break; }
                }
                if (!near) keep.Add(s);
            }
            keep.Sort();
            res = keep;
            return res;
        }

        /// <summary>
        /// 拐角位置细化：在 ±window/2 窗口里按转角最大化找精确位置，
        /// 若最近折线顶点本身也是拐角（转角 &gt; 阈值）就吸附到它 —— 多边形能取到**精确角点**。
        /// </summary>
        internal static void RefineKink(LoopData ld, double coarse, double window, double minAngleDeg,
            out double pos, out double angle)
        {
            pos = coarse; angle = 0;
            int m = 64;
            double best = double.MinValue, bestS = coarse, bestAng = 0;
            for (int k = 0; k <= m; k++)
            {
                double s = coarse + (k / (double)m - 0.5) * window;
                Point3d a = PointAtArc(ld, s - window / m);
                Point3d b = PointAtArc(ld, s);
                Point3d c = PointAtArc(ld, s + window / m);
                Vector3d u = b - a, v = c - b;
                if (u.Length < 1e-15 || v.Length < 1e-15) continue;
                u.Unitize(); v.Unitize();
                double dot = u * v;
                if (dot > 1) dot = 1; else if (dot < -1) dot = -1;
                double a2 = Math.Acos(dot) * 180.0 / Math.PI;
                if (a2 > best) { best = a2; bestS = s; bestAng = a2; }
            }
            int vi = -1; double vd = double.MaxValue;
            for (int i = 0; i < ld.P.Length; i++)
            {
                double d = Math.Abs(ld.Param(i) - bestS);
                d = Math.Min(d, ld.Length - d);
                if (d < vd) { vd = d; vi = i; }
            }
            if (vi >= 0 && vd <= window)
            {
                double va = VertexTurnAngle(ld, vi);
                if (va > minAngleDeg) { pos = ld.Param(vi); angle = va; return; }
            }
            pos = bestS; angle = bestAng;
        }

        /// <summary>折线顶点处的转角（度）</summary>
        internal static double VertexTurnAngle(LoopData ld, int i)
        {
            int cnt = ld.P.Length;
            if (cnt < 3) return 0;
            Vector3d a = ld.P[i] - ld.P[(i - 1 + cnt) % cnt];
            Vector3d b = ld.P[(i + 1) % cnt] - ld.P[i];
            if (a.Length < 1e-15 || b.Length < 1e-15) return 0;
            a.Unitize(); b.Unitize();
            double dot = a * b;
            if (dot > 1) dot = 1; else if (dot < -1) dot = -1;
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        static double[] TurnAngles(Point3d[] pt, int samples)
        {
            var ang = new double[samples];
            for (int k = 0; k < samples; k++)
            {
                Vector3d a = pt[k] - pt[(k + samples - 1) % samples];
                Vector3d b = pt[(k + 1) % samples] - pt[k];
                if (a.Length < 1e-12 || b.Length < 1e-12) { ang[k] = 0; continue; }
                a.Unitize(); b.Unitize();
                double c = a * b;
                if (c > 1.0) c = 1.0; else if (c < -1.0) c = -1.0;
                ang[k] = Math.Acos(c) * 180.0 / Math.PI;
            }
            return ang;
        }

        /// <summary>全部拐角的**弧长比例**（0~1）—— 用来插「拐角节点」</summary>
        internal static List<double> KinkParams(LoopData ld, double minAngleDeg, int maxCount)
        {
            var res = new List<double>();
            var kk = CurvatureKinkParams(ld, 400, minAngleDeg, maxCount);
            for (int i = 0; i < kk.Count; i++) res.Add(kk[i] / ld.Length);
            return res;
        }

        // ============================================================ 边 / 参数映射

        /// <summary>4 角在环上的弧长参数（升序）</summary>
        internal static double[] CornerParams(LoopData ld, int[] ci)
        {
            var p = new double[4];
            for (int k = 0; k < 4; k++) p[k] = ld.Param(ci[k]);
            Array.Sort(p);
            return p;
        }

        /// <summary>
        /// (side, param) → 环上的弧长位置。side：0 = v=0（C0→C1），1 = u=1（C1→C2），
        /// 2 = v=1（C3→C2，参数反向），3 = u=0（C0→C3，参数反向）。
        /// </summary>
        internal static double SideArc(LoopData ld, double[] p, int side, double param)
        {
            double L = ld.Length;
            switch (side)
            {
                case 0: return p[0] + param * (p[1] - p[0]);
                case 1: return p[1] + param * (p[2] - p[1]);
                case 2: return p[3] - param * (p[3] - p[2]);
                default: return p[0] - param * (p[0] + L - p[3]);
            }
        }

        /// <summary>side 上 param 处的点</summary>
        internal static Point3d SidePoint(LoopData ld, double[] p, int side, double param)
        {
            return PointAtArc(ld, SideArc(ld, p, side, param));
        }

        /// <summary>side 上的弧长（用来把 mm 偏移换算成参数偏移）</summary>
        internal static double SideLength(LoopData ld, double[] p, int side)
        {
            double L = ld.Length;
            switch (side)
            {
                case 0: return p[1] - p[0];
                case 1: return p[2] - p[1];
                case 2: return p[3] - p[2];
                default: return p[0] + L - p[3];
            }
        }

        /// <summary>
        /// 环上弧长位置 → (side, param)。用来把「拐角」分配到 u/v 节点向量上：
        /// side0/side2 → u 参数；side1/side3 → v 参数。
        /// </summary>
        internal static bool ArcToSideParam(LoopData ld, double[] p, double s, out int side, out double param)
        {
            side = -1; param = 0;
            double L = ld.Length;
            double q = s % L; if (q < 0) q += L;
            if (q >= p[0] && q <= p[1]) { side = 0; param = (q - p[0]) / Math.Max(p[1] - p[0], 1e-12); return true; }
            if (q > p[1] && q <= p[2]) { side = 1; param = (q - p[1]) / Math.Max(p[2] - p[1], 1e-12); return true; }
            if (q > p[2] && q <= p[3]) { side = 2; param = (p[3] - q) / Math.Max(p[3] - p[2], 1e-12); return true; }
            if (q > p[3] || q < p[0])
            {
                double qq = q > p[3] ? q : q + L;
                side = 3; param = (p[0] + L - qq) / Math.Max(p[0] + L - p[3], 1e-12); return true;
            }
            return false;
        }

        // ============================================================ Coons 基面

        /// <summary>
        /// 3D 双线性 Coons 基面在 (u,v) 处的点。四条边就是原边界的采样点 → 基面边界天然「完全逼近」原边界。
        /// 注意 Point3d − Point3d = Vector3d，所以逐分量算，别写成 a + b − c。
        /// </summary>
        internal static Point3d CoonsAt(LoopData ld, double[] p, double u, double v)
        {
            Point3d row0 = SidePoint(ld, p, 0, u);      // v = 0 边
            Point3d row1 = SidePoint(ld, p, 2, u);      // v = 1 边
            Point3d col0 = SidePoint(ld, p, 3, v);      // u = 0 边
            Point3d col1 = SidePoint(ld, p, 1, v);      // u = 1 边
            Point3d c00 = SidePoint(ld, p, 0, 0.0);
            Point3d c10 = SidePoint(ld, p, 0, 1.0);
            Point3d c01 = SidePoint(ld, p, 2, 0.0);
            Point3d c11 = SidePoint(ld, p, 2, 1.0);
            double a = (1 - u) * (1 - v), b = u * (1 - v), c = (1 - u) * v, d = u * v;
            return new Point3d(
                col0.X * (1 - u) + col1.X * u + row0.X * (1 - v) + row1.X * v - (c00.X * a + c10.X * b + c01.X * c + c11.X * d),
                col0.Y * (1 - u) + col1.Y * u + row0.Y * (1 - v) + row1.Y * v - (c00.Y * a + c10.Y * b + c01.Y * c + c11.Y * d),
                col0.Z * (1 - u) + col1.Z * u + row0.Z * (1 - v) + row1.Z * v - (c00.Z * a + c10.Z * b + c01.Z * c + c11.Z * d));
        }

        /// <summary>Coons 基面网格（n×n，点序 u 慢变）→ 插值出基面（内部约束求 (u,v) 用）</summary>
        internal static Point3d[,] CoonsGrid(LoopData ld, double[] p, int n)
        {
            var g = new Point3d[n, n];
            for (int i = 0; i < n; i++)
            {
                double u = n > 1 ? i / (double)(n - 1) : 0.0;
                for (int j = 0; j < n; j++)
                {
                    double v = n > 1 ? j / (double)(n - 1) : 0.0;
                    g[i, j] = CoonsAt(ld, p, u, v);
                }
            }
            return g;
        }

        /// <summary>网格 → NURBS（CreateThroughPoints 点序 u 慢变：points[i*vCount + j]）</summary>
        internal static NurbsSurface InterpolateGrid(Point3d[,] g, int nu, int nv)
        {
            if (nu < 2 || nv < 2) return null;
            var flat = new List<Point3d>(nu * nv);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++) flat.Add(g[i, j]);
            NurbsSurface srf = null;
            try { srf = NurbsSurface.CreateThroughPoints(flat, nu, nv, 3, 3, false, false); } catch { srf = null; }
            return srf;
        }

        // ============================================================ 基函数（Cox-de Boor）

        internal static int FindSpan(double[] U, int p, int n, double u)
        {
            if (u >= U[n]) return n - 1;
            if (u <= U[p]) return p;
            int low = p, high = n, mid = (low + high) / 2;
            while (u < U[mid] || u >= U[mid + 1])
            {
                if (u < U[mid]) high = mid; else low = mid;
                mid = (low + high) / 2;
            }
            return mid;
        }

        /// <summary>N[0..p] = 第 span-p .. span 个基函数在 u 处的值（NURBS Book A2.3）</summary>
        internal static void BasisFuns(double[] U, int p, int span, double u, double[] N)
        {
            var left = new double[p + 1];
            var right = new double[p + 1];
            N[0] = 1.0;
            for (int j = 1; j <= p; j++)
            {
                left[j] = u - U[span + 1 - j];
                right[j] = U[span + j] - u;
                double saved = 0.0;
                for (int r = 0; r < j; r++)
                {
                    double denom = right[r + 1] + left[j - r];
                    double temp = Math.Abs(denom) > 1e-300 ? N[r] / denom : 0.0;
                    N[r] = saved + right[r + 1] * temp;
                    saved = left[j - r] * temp;
                }
                N[j] = saved;
            }
        }

        /// <summary>把 (u,v) 处的非零基函数展开成控制点索引 + 系数（张量积，最多 16 项）</summary>
        internal static void TensorRow(double[] U, double[] V, int nu, int nv, double u, double v,
            int[] idx, double[] coef, out int nnz)
        {
            var Nu = new double[Degree + 1];
            var Nv = new double[Degree + 1];
            int su = FindSpan(U, Degree, nu, u);
            int sv = FindSpan(V, Degree, nv, v);
            BasisFuns(U, Degree, su, u, Nu);
            BasisFuns(V, Degree, sv, v, Nv);
            nnz = 0;
            for (int a = 0; a <= Degree; a++)
            {
                int i = su - Degree + a;
                if (i < 0 || i >= nu) continue;
                for (int b = 0; b <= Degree; b++)
                {
                    int j = sv - Degree + b;
                    if (j < 0 || j >= nv) continue;
                    double c = Nu[a] * Nv[b];
                    if (Math.Abs(c) < 1e-14) continue;
                    idx[nnz] = i * nv + j;
                    coef[nnz] = c;
                    nnz++;
                }
            }
        }

        /// <summary>
        /// 节点向量：夹持端点 + 拐角 multiplicity 3 + 局部插结（extra，multiplicity 1）+ 均匀补足
        /// （保证控制点数 = n + extra.Count）
        /// </summary>
        internal static double[] BuildKnots(int n, List<double> kinks)
        {
            return BuildKnots(n, kinks, null);
        }

        internal static double[] BuildKnots(int n, List<double> kinks, List<double> extra)
        {
            int p = Degree;
            int interiorCount = n - p - 1;
            if (interiorCount < 0) interiorCount = 0;

            // ① 拐角参数（去重 + 丢掉贴端点的：那里由夹持端覆盖）
            var kinkVals = new List<double>();
            if (kinks != null && kinks.Count > 0)
            {
                var sorted = new List<double>(kinks);
                sorted.Sort();
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i] < 2e-3 || sorted[i] > 1 - 2e-3) continue;
                    if (kinkVals.Count > 0 && sorted[i] - kinkVals[kinkVals.Count - 1] < 2e-3) continue;
                    kinkVals.Add(sorted[i]);
                }
            }
            int mult = 3;
            while (kinkVals.Count * mult > interiorCount && mult > 1) mult--;
            while (kinkVals.Count * mult > interiorCount) kinkVals.RemoveAt(kinkVals.Count - 1);
            int kinkSlots = kinkVals.Count * mult;

            // ② 局部插结（残差驱动）：multiplicity 1，避开拐角与两端
            var singles = new List<double>();
            if (extra != null && extra.Count > 0 && kinkSlots < interiorCount)
            {
                var sorted = new List<double>(extra);
                sorted.Sort();
                for (int i = 0; i < sorted.Count; i++)
                {
                    double t = sorted[i];
                    if (t < 5e-3 || t > 1 - 5e-3) continue;
                    bool near = false;
                    for (int k = 0; k < kinkVals.Count; k++) if (Math.Abs(t - kinkVals[k]) < 5e-3) near = true;
                    for (int k = 0; k < singles.Count; k++) if (Math.Abs(t - singles[k]) < 5e-3) near = true;
                    if (near) continue;
                    if (kinkSlots + singles.Count >= interiorCount) break;
                    singles.Add(t);
                }
                singles.Sort();
            }

            // ③ 均匀节点（避开拐角与插结点）：⚠ 必须**均匀铺满 [0,1] 全程**（t = k/(want+1)）——
            //    旧写法从固定公式里贪心取前 want 个，全部挤在参数域前半段（实测 8 个节点全在 [0.05,0.38]），
            //    后半段一根三次段硬扛 → 圆弧洞后半圈缝隙 0.28、G1 84°（直线边界恰好能被任何节点精确表达，
            //    所以 4 角多边形模式从没暴露过；盘状圆环一开就炸）。
            var uni = new List<double>();
            int want = interiorCount - kinkSlots - singles.Count;
            if (want > 0)
            {
                var all = new List<double>(kinkVals);
                for (int i = 0; i < singles.Count; i++) all.Add(singles[i]);
                all.Sort();
                // 第一遍：均匀铺满，strict 避让（间距 2e-3）
                for (int k = 1; k <= want; k++)
                {
                    double t = k / (double)(want + 1);
                    bool near = false;
                    for (int i = 0; i < all.Count; i++) if (Math.Abs(t - all[i]) < 2e-3) near = true;
                    for (int i = 0; i < uni.Count; i++) if (Math.Abs(t - uni[i]) < 2e-3) near = true;
                    if (near) continue;
                    uni.Add(t);
                }
                // 第二遍：被拐角/插结占掉位置后不够 → 在最大空档里补（保证 want 个、铺满全程）
                int guard = 0;
                while (uni.Count < want && guard++ < want * 4)
                {
                    var taken = new List<double>(uni);
                    taken.AddRange(all);
                    taken.Sort();
                    double bestGap = -1, at = 0.5, prev = 0;
                    for (int i = 0; i <= taken.Count; i++)
                    {
                        double nxt = i < taken.Count ? taken[i] : 1.0;
                        if (nxt - prev > bestGap) { bestGap = nxt - prev; at = (prev + nxt) * 0.5; }
                        prev = nxt;
                    }
                    bool dup = false;
                    for (int i = 0; i < uni.Count; i++) if (Math.Abs(at - uni[i]) < 1e-6) dup = true;
                    if (dup) break;
                    uni.Add(at);
                }
                uni.Sort();
            }

            var ks = new List<double>();
            for (int i = 0; i < kinkVals.Count; i++)
                for (int m = 0; m < mult; m++) ks.Add(kinkVals[i]);
            ks.AddRange(singles);
            ks.AddRange(uni);
            ks.Sort();
            // 严格递增（不同值撞车时微调，重节点本身保留）
            for (int i = 1; i < ks.Count; i++)
                if (ks[i] - ks[i - 1] < 1e-6 && Math.Abs(ks[i] - ks[i - 1]) > 0) ks[i] = ks[i - 1] + 1e-6;

            var U = new double[ks.Count + 2 * (p + 1)];
            for (int i = 0; i <= p; i++) U[i] = 0.0;
            for (int i = 0; i < ks.Count; i++) U[p + 1 + i] = ks[i];
            for (int i = 0; i < p + 1; i++) U[ks.Count + p + 1 + i] = 1.0;
            return U;
        }

        // ============================================================ 最小二乘（法方程 + 稠密 Cholesky）

        internal sealed class NormalEq
        {
            public readonly int N;
            public readonly double[] A;
            public readonly double[] Bx, By, Bz;
            // 变量消元映射：sol[i] = −1 表示变量 i 独立；>=0 表示变量 i 的解 = 变量 map[i] 的解（系数 1）。
            // 用途：盘状模式的 u 接缝（最后一列控制点 = 第一列控制点）——比罚行精确且无病态方向。
            public int[] Map = null;

            public NormalEq(int n)
            {
                N = n;
                A = new double[n * n];
                Bx = new double[n]; By = new double[n]; Bz = new double[n];
            }

            /// <summary>把消元变量 idx 折叠到它的目标变量（系数已含在调用方写入的行里）</summary>
            public void ApplyMap()
            {
                if (Map == null) return;
                for (int i = 0; i < N; i++)
                {
                    int m = Map[i];
                    if (m < 0) continue;
                    for (int k = 0; k < N; k++)
                    {
                        double aik = A[i * N + k];
                        if (aik != 0.0) { A[m * N + k] += aik; A[i * N + k] = 0.0; }
                        double aki = A[k * N + i];
                        if (aki != 0.0) { A[k * N + m] += aki; A[k * N + i] = 0.0; }
                    }
                    Bx[m] += Bx[i]; Bx[i] = 0;
                    By[m] += By[i]; By[i] = 0;
                    Bz[m] += Bz[i]; Bz[i] = 0;
                    A[i * N + i] = 0.0;
                }
            }

            /// <summary>消元变量的行不能有任何非零系数（否则映射不成立）——构建行时由 AddRow 过滤</summary>
            public void SetMap(int[] map) { Map = map; }

            /// <summary>累加一行 w·(Σ c_a P_a − target)² 的贡献</summary>
            public void AddRow(double w, int[] idx, double[] coef, int nnz, Point3d target)
            {
                if (nnz <= 0 || !(w > 0)) return;
                Substitute(idx, coef, ref nnz);
                for (int a = 0; a < nnz; a++)
                {
                    double wa = w * coef[a];
                    int ia = idx[a];
                    if (ia < 0 || ia >= N) continue;
                    Bx[ia] += wa * target.X;
                    By[ia] += wa * target.Y;
                    Bz[ia] += wa * target.Z;
                    for (int b = a; b < nnz; b++)
                    {
                        int ib = idx[b];
                        if (ib < 0 || ib >= N) continue;
                        double v = wa * coef[b];
                        A[ia * N + ib] += v;
                        if (ia != ib) A[ib * N + ia] += v;
                    }
                }
            }

            /// <summary>把消元变量的解从映射目标复制回来（盘状接缝：末列 = 首列）</summary>
            public void Unmap(double[] x)
            {
                if (Map == null || x == null || x.Length < N) return;
                for (int i = 0; i < N; i++)
                {
                    int m = Map[i];
                    if (m >= 0) { x[i] = x[m]; }
                }
            }

            /// <summary>
            /// 消元变量没有任何行（AddRow 已把它们的系数精确替换到映射目标上）→ 对角线为 0，
            /// Cholesky 会失败。封口：对角线置 1（解出 0），随后 Unmap 用映射目标的解覆盖。
            /// </summary>
            public void SealMapped()
            {
                if (Map == null) return;
                for (int i = 0; i < N; i++) if (Map[i] >= 0) A[i * N + i] = 1.0;
            }

            /// <summary>消元封口行的重解保护：再次把封口对角线归 1（Ridge 会加脏值）</summary>
            public void RidgeMapped(double rel)
            {
                if (Map == null) return;
                for (int i = 0; i < N; i++)
                {
                    if (Map[i] < 0) continue;
                    for (int k = 0; k < N; k++)
                    {
                        if (k != i && A[i * N + k] != 0.0) { A[i * N + k] = 0.0; A[k * N + i] = 0.0; }
                    }
                    A[i * N + i] = 1.0;
                    Bx[i] = 0; By[i] = 0; Bz[i] = 0;
                }
            }

            /// <summary>把一行里的消元变量精确替换成映射目标并合并同变量系数（P[mapped] ≡ P[target]）</summary>
            private void Substitute(int[] idx, double[] coef, ref int nnz)
            {
                if (Map == null) return;
                // 合并到临时表（行非零元最多 16 + 合并，规模极小）
                int[] tidx = new int[nnz];
                double[] tcoef = new double[nnz];
                int tn = 0;
                for (int a = 0; a < nnz; a++)
                {
                    int i = idx[a];
                    if (i >= 0 && i < N && Map[i] >= 0) i = Map[i];
                    double c = coef[a];
                    bool merged = false;
                    for (int b = 0; b < tn; b++)
                    {
                        if (tidx[b] == i) { tcoef[b] += c; merged = true; break; }
                    }
                    if (!merged) { tidx[tn] = i; tcoef[tn] = c; tn++; }
                }
                // 抵消掉的项（系数和为 0）丢掉
                int outN = 0;
                for (int b = 0; b < tn; b++)
                {
                    if (Math.Abs(tcoef[b]) < 1e-300) continue;
                    idx[outN] = tidx[b]; coef[outN] = tcoef[b]; outN++;
                }
                nnz = outN;
            }

            /// <summary>累加一行 w·(Σ c_a P_a − target)² 的贡献</summary>
            public void Ridge(double rel)
            {
                double tr = 0;
                for (int i = 0; i < N; i++) tr += A[i * N + i];
                if (!(tr > 0)) tr = 1.0;
                double eps = tr / N * rel;
                for (int i = 0; i < N; i++) A[i * N + i] += eps;
            }
        }

        /// <summary>稠密对称正定 Cholesky：A = L·Lᵀ（行主序 n×n）</summary>
        internal static bool Cholesky(double[] A, int n, out double[] L)
        {
            L = new double[n * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double s = A[i * n + j];
                    for (int k = 0; k < j; k++) s -= L[i * n + k] * L[j * n + k];
                    if (i == j)
                    {
                        if (!(s > 1e-300)) return false;
                        L[i * n + i] = Math.Sqrt(s);
                    }
                    else L[i * n + j] = s / L[j * n + j];
                }
            }
            return true;
        }

        /// <summary>用 Cholesky 因子解 L·Lᵀ·x = b（b 就地变成 x）</summary>
        internal static void CholSolve(double[] L, int n, double[] b)
        {
            for (int i = 0; i < n; i++)
            {
                double s = b[i];
                for (int k = 0; k < i; k++) s -= L[i * n + k] * b[k];
                b[i] = s / L[i * n + i];
            }
            for (int i = n - 1; i >= 0; i--)
            {
                double s = b[i];
                for (int k = i + 1; k < n; k++) s -= L[k * n + i] * b[k];
                b[i] = s / L[i * n + i];
            }
        }

        // ============================================================ 生成

        internal sealed class Ctx
        {
            public LoopData Loop;
            public double[] P;            // 4 角弧长参数
            public int[] Ci;
            public string CornerMethod;
            public bool CornerFallback;
            public NurbsSurface BaseSrf;
            public Reference Ref;
            public PatchFillSettings S;
            public double Diag;
            public double[] U, V;
            public int Nu, Nv;
            public List<GeometryBase> Inner;
            public double G1Offset;       // 模型单位
            public Vector3d CentroidDir;  // 洞中心方向（G1 兜底用）
            public Point3d Centroid;
            public List<double>[] KinksBySide = new List<double>[4];   // 每条边上的拐角（side 局部参数）
            public List<double> KinksU = new List<double>();          // u 方向的拐角节点参数
            public List<double> KinksV = new List<double>();          // v 方向的拐角节点参数
            public List<double> ExtraU = new List<double>();          // 残差驱动局部插结（u）
            public List<double> ExtraV = new List<double>();          // 残差驱动局部插结（v）
            public bool Disk;                                         // 径向盘状模式（无拐角闭合环：u 绕环、v 向内、中心退化）
            public Point3d Center;                                    // 盘状模式的中心
            public List<Point2d> RefCacheUv = null;                   // 首轮参考采样缓存（T2：后续轮不重算 Closest）
            public List<Point3d> RefCachePts = null;
            public BandCache[] Band = null;                           // 连续性带行军缓存（G1/G2 共用，一次性构建）
            public bool BandReady;
            public double BaseArea = -1;                              // 基面近似面积（面积压力用，惰性求一次）
            public double PressureLift;                               // 面积压力充气量（模型单位，0 = 不加压力行）
        }

        /// <summary>
        /// 盘状模式可行性：环要「凸得像盘」——所有边界点离中心的距离变化不过分（否则射线向内会自交），
        /// 且环近似落在一个平面附近（盘状基面是「边界→中心」的线性混合，强空间曲线会自交）。
        /// 不满足就走 4 角切分（多边形洞本来就该走那边）。
        /// </summary>
        static bool DiskFeasible(LoopData ld, Point3d center)
        {
            if (ld == null || ld.P.Length < 8 || !(ld.Length > 1e-9)) return false;
            // 平面度：点到最佳拟合平面的最大距离 ≤ 周长的 2%
            Plane pl = FitLoopPlane(ld, Plane.Unset);
            double flatTol = ld.Length * 0.02;
            double maxFlat = 0;
            for (int i = 0; i < ld.P.Length; i++)
            {
                double d = Math.Abs((ld.P[i] - pl.Origin) * pl.Normal);
                if (d > maxFlat) maxFlat = d;
            }
            if (maxFlat > flatTol) return false;
            // 半径均匀性：max(r)/min(r) ≤ 4（星形/月牙形不适合径向参数化）
            double rmin = double.MaxValue, rmax = 0;
            for (int i = 0; i < ld.P.Length; i++)
            {
                double r = ld.P[i].DistanceTo(center);
                if (r < rmin) rmin = r;
                if (r > rmax) rmax = r;
            }
            return rmin > 1e-9 && rmax / rmin <= 4.0;
        }

        /// <summary>
        /// 基面求值：4 角切分 → 3D 双线性 Coons；**无拐角闭合环 → 径向盘状**（边界弧 → 中心线性混合）。
        /// 盘状模式没有「4 个参数角点」→ 角点 G1 不可达的问题从根上消失（u 绕环、v 向内、中心退化）。
        /// </summary>
        internal static Point3d BaseAt(Ctx ctx, double u, double v)
        {
            if (ctx.Disk)
            {
                Point3d b = PointAtArc(ctx.Loop, u * ctx.Loop.Length);
                return new Point3d(b.X + (ctx.Center.X - b.X) * v,
                                   b.Y + (ctx.Center.Y - b.Y) * v,
                                   b.Z + (ctx.Center.Z - b.Z) * v);
            }
            return CoonsAt(ctx.Loop, ctx.P, u, v);
        }

        /// <summary>基面网格（盘状/Coons 通用；内部约束求 (u,v) 用）</summary>
        internal static Point3d[,] BaseGrid(Ctx ctx, int n)
        {
            var g = new Point3d[n, n];
            for (int i = 0; i < n; i++)
            {
                double u = n > 1 ? i / (double)(n - 1) : 0.0;
                for (int j = 0; j < n; j++)
                {
                    double v = n > 1 ? j / (double)(n - 1) : 0.0;
                    g[i, j] = BaseAt(ctx, u, v);
                }
            }
            return g;
        }

        /// <summary>
        /// 主入口：一圈边界（+ 可选内部曲线/点、可选外部参考几何）→ 一张 NURBS 面。
        /// </summary>
        public static PatchFillResult Generate(List<PatchFillEdge> edges, List<GeometryBase> inner,
            GeometryBase explicitRef, PatchFillSettings s, out string report)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new PatchFillResult();
            var sb = new StringBuilder();
            report = "";
            if (s == null) { res.Error = "参数为空"; return res; }
            if (edges == null || edges.Count == 0)
            {
                res.Error = "没有选到边界边：请选一圈首尾相连的曲线 / 曲面边";
                report = res.Error;
                return res;
            }

            // ---- 尺寸
            var bb = BoundingBox.Empty;
            for (int i = 0; i < edges.Count; i++)
            {
                try { bb.Union(edges[i].Curve.GetBoundingBox(true)); } catch { }
            }
            res.Diag = bb.IsValid ? bb.Diagonal.Length : 1.0;
            if (!(res.Diag > 1e-9)) res.Diag = 1.0;

            // ---- ① 链化成环
            List<LoopData> loops; List<int> loopEdgeCounts; string why;
            if (!ChainLoops(edges, res.Diag, out loops, out loopEdgeCounts, out why))
            {
                res.Error = why;
                report = why;
                return res;
            }
            res.Edges = edges.Count;

            // 外环 = 包围盒对角线最大的那个（洞口），其余 = 内孔候选
            int outer = 0;
            double bestSize = -1;
            for (int i = 0; i < loops.Count; i++)
            {
                var b1 = BoundingBox.Empty;
                for (int k = 0; k < loops[i].P.Length; k++) b1.Union(loops[i].P[k]);
                double d = b1.IsValid ? b1.Diagonal.Length : 0;
                if (d > bestSize) { bestSize = d; outer = i; }
            }
            LoopData loop = loops[outer];
            var holes = new List<LoopData>();
            for (int i = 0; i < loops.Count; i++) if (i != outer) holes.Add(loops[i]);
            res.HolesFound = holes.Count;
            res.Loops = loops.Count;

            // ---- ② 4 角 / 径向盘状 + 拐角节点
            // 环中心（盘状模式的中心 / G1 兜底方向 / 参考投影起点）
            Point3d sum = Point3d.Origin;
            for (int i = 0; i < loop.P.Length; i++) { sum = new Point3d(sum.X + loop.P[i].X, sum.Y + loop.P[i].Y, sum.Z + loop.P[i].Z); }
            Point3d loopCenter = new Point3d(sum.X / loop.P.Length, sum.Y / loop.P.Length, sum.Z / loop.P.Length);

            // 无拐角的闭合环（圆 / 椭圆 / 圆角环）：4 角切分是**人为**的 → 那 4 个参数角点上两条参数方向
            // 都被边界切向钉住、切平面恒等于边界平面 → 角点 G1 几何上不可达。
            // 这类环改用**径向盘状参数化**：u 绕整环、v 由边界向内、中心退化（无角点）→ G1 处处可达。
            var kinkPos = CurvatureKinkParams(loop, 400, 15.0, 10);
            // 径向盘状模式（无拐角闭合环）：u 绕整环、v 由边界向内、中心退化 → 没有 4 个参数角点，
            // 「角点切平面被两条边界弧同时钉住」的 G1 死结从根上消失。
            // 秩亏根因（上轮记录：控制网 z 跑到 -31、缝隙 0.29）= 接缝用罚行（首末两列控制点重合，
            // 权重 1e3 与第一列边界行 1e6 形成近奇异方向）→ 本轮改成**变量消元**：
            // 法方程里最后一列控制点的变量直接映射到第一列（NormalEq.Sub*），接缝 C0 精确成立且无病态方向；
            // 再补接缝 C1 行（跨缝导数连续）与极点收敛行（末排控制点收成一点 = 锥形尾）。
            bool diskMode = kinkPos.Count == 0 && DiskFeasible(loop, loopCenter);

            Plane fit = FitLoopPlane(loop, Plane.WorldXY);
            Plane frame = PrincipalFrame(loop, fit);
            double[] cp; int[] ci; bool fallback; string method;
            if (diskMode)
            {
                double L = loop.Length;
                cp = new double[] { 0, L, 2 * L, 3 * L };   // SideArc(0,f) = f·L → u 绕整环
                ci = new int[] { 0, 0, 0, 0 };
                method = "径向盘状（无角点）"; fallback = false;
            }
            else
            {
                CornerArcParams(loop, frame, out cp, out ci, out method, out fallback);
            }
            if (cp == null || cp.Length != 4)
            {
                res.Error = "无法把边界分成 4 段（角点解析失败）";
                report = res.Error;
                return res;
            }
            res.Corners = diskMode ? 0 : 4;
            res.CornerMethod = method;

            var ctx = new Ctx();
            ctx.Loop = loop; ctx.P = cp; ctx.Ci = ci; ctx.CornerMethod = method;
            ctx.CornerFallback = fallback; ctx.S = s; ctx.Diag = res.Diag; ctx.Inner = inner;
            ctx.Disk = diskMode; ctx.Center = loopCenter; ctx.Centroid = loopCenter;
            Vector3d cd = ctx.Centroid - loop.P[0];
            if (cd.Length > 1e-12) cd.Unitize();
            ctx.CentroidDir = cd;
            ctx.Ref = Reference.FromEdges(edges);
            if (explicitRef != null) ctx.Ref.AddExplicit(explicitRef);
            res.HasReference = ctx.Ref.Any;
            for (int i = 0; i < 4; i++) ctx.KinksBySide[i] = new List<double>();

            // 拐角 → u / v 参数（side0/side2 → u；side1/side3 → v）；盘状模式没有拐角
            var kinksU = new List<double>();
            var kinksV = new List<double>();
            if (!diskMode)
            {
                for (int i = 0; i < kinkPos.Count; i++)
                {
                    int side; double param;
                    if (!ArcToSideParam(loop, cp, kinkPos[i], out side, out param)) continue;
                    if (param < 1e-3 || param > 1 - 1e-3) continue;      // 落在 4 角上的由夹持端覆盖
                    ctx.KinksBySide[side].Add(param);
                    if (side == 0 || side == 2) kinksU.Add(param);
                    else kinksV.Add(param);
                }
            }
            res.KinkKnots = kinksU.Count + kinksV.Count;

            int n = Math.Max(MinControl, Math.Min(MaxControl, s.ControlCount));
            ctx.KinksU = kinksU; ctx.KinksV = kinksV;
            ctx.U = BuildKnots(n + ctx.ExtraU.Count, kinksU, ctx.ExtraU);
            ctx.V = BuildKnots(n + ctx.ExtraV.Count, kinksV, ctx.ExtraV);
            ctx.Nu = ctx.U.Length - Degree - 1;
            ctx.Nv = ctx.V.Length - Degree - 1;
            res.ControlU = ctx.Nu;
            res.ControlV = ctx.Nv;

            // 基面（内部约束求 (u,v) 用）。盘状模式有解析 DiskUvOf → 跳过插值基面
            // （T3：省 nd² 次基面求值 + 一次 CreateThroughPoints；盘状用例 ~15ms）
            int nd = Math.Max(8, Math.Min(41, 2 * n + 1));
            ctx.BaseSrf = diskMode ? null : InterpolateGrid(BaseGrid(ctx, nd), nd, nd);
            ctx.G1Offset = Math.Max(0.0, s.G1Distance * s.MmToModel);

            // ---- ③④⑤ 最小二乘 + 残差迭代
            double bestScore = double.MaxValue;
            double bestDev = double.MaxValue;
            NurbsSurface best = null; int bestRound = 0;
            Point3d[,] bestCp = null;
            int bestNu = 0, bestNv = 0;
            double[] bestGaps = null; double bestBoundary = 0, bestRms = 0;
            int bestFolded = 0; int bestG1Samples = 0; double bestAngle = -1;
            double bestAngleInner = -1;
            double bestSD = -1; int bestG2n = 0;
            int bestWorstSide = -1; double bestWorstF = -1;
            Vector3d bestWorstNS = Vector3d.Zero, bestWorstNA = Vector3d.Zero;
            Point3d bestWorstP = Point3d.Origin;

            var extraRef = new List<Point3d>();      // 残差迭代补的参考采样（3D 点 + 已求好的 (u,v)）
            var extraUv = new List<Point2d>();
            Point3d[,] lastCpNet = null;
            double lastDev = double.MaxValue;
            int maxRounds = 4;
            for (int round = 0; round < maxRounds; round++)
            {
                var rows = new NormalEq(ctx.Nu * ctx.Nv);
                if (ctx.Disk)
                {
                    // 盘状接缝（u=0 列与 u=1 列是同一列物理控制点）：**变量消元**而不是罚行。
                    // 罚行（上轮实测）与第一列边界行形成近奇异方向 → 秩亏（控制网 z 到 -31）；
                    // 消元后这些变量不出现在任何行里，系统严格正定，接缝 C0 精确成立。
                    var map = new int[ctx.Nu * ctx.Nv];
                    for (int k = 0; k < map.Length; k++) map[k] = -1;
                    for (int j = 0; j < ctx.Nv; j++) map[(ctx.Nu - 1) * ctx.Nv + j] = 0 * ctx.Nv + j;
                    rows.SetMap(map);
                }
                Assemble(ctx, rows, round == 0, extraUv, extraRef);
                rows.SealMapped();     // 消元变量没有行 → 对角线封 1（解为 0，Unmap 时用映射目标覆盖）
                if (ctx.Disk) rows.RidgeMapped(1e-6);   // 盘状：消元封口行不参与 ridge 之外的加权，单独加固

                double[] L;
                if (!Cholesky(rows.A, rows.N, out L))
                {
                    res.Error = "最小二乘法方程不是正定的（节点向量退化）";
                    break;
                }
                CholSolve(L, rows.N, rows.Bx);
                CholSolve(L, rows.N, rows.By);
                CholSolve(L, rows.N, rows.Bz);
                if (ctx.Disk) { rows.Unmap(rows.Bx); rows.Unmap(rows.By); rows.Unmap(rows.Bz); }

                var cpNet = new Point3d[ctx.Nu, ctx.Nv];
                for (int i = 0; i < ctx.Nu; i++)
                    for (int j = 0; j < ctx.Nv; j++)
                    {
                        int k = i * ctx.Nv + j;
                        cpNet[i, j] = new Point3d(rows.Bx[k], rows.By[k], rows.Bz[k]);
                    }

                NurbsSurface srf = BuildNurbs(cpNet, ctx.U, ctx.V, ctx.Nu, ctx.Nv);
                if (srf == null) { res.Error = "建面失败（" + (string.IsNullOrEmpty(LastBuildError) ? "未知" : LastBuildError) + "）"; break; }
                lastCpNet = cpNet;

                // 度量
                double maxDev, rmsDev, boundaryDev; double[] gaps;
                Measure(srf, ctx, out maxDev, out rmsDev, out boundaryDev, out gaps);
                int folded = CountFolded(srf, ctx);
                double angle = -1; int g1n = 0;
                if (s.G1Distance >= 0 && s.Continuity >= 1) angle = NormalAngle(srf, ctx, out g1n);
                // G2 度量（G1/G2 模式都测：G1 模式测出的角就是「不开 G2 行」的对照基线）
                double sd = -1; int g2n = 0;
                if (s.G1Distance >= 0 && s.Continuity >= 1 && ctx.Ref.Any) sd = SecondDiffAngle(srf, ctx, out g2n);

                if (round == 0) res.FirstRoundDeviation = maxDev;

                // 选「最好的一轮」：偏差为主，G1 时把夹角超差的那一轮打折（否则可能挑到切线不对的那版）
                double score = maxDev;
                if ((s.Continuity >= 1 && angle > 1.0) || (s.Continuity >= 2 && sd > 20.0))
                    score = maxDev * 1.5 + ctx.Diag * 1e-4;
                if (score <= bestScore)
                {
                    bestScore = score; bestDev = maxDev; best = srf; bestRound = round;
                    bestCp = cpNet;
                    bestNu = ctx.Nu; bestNv = ctx.Nv;
                    bestGaps = gaps; bestBoundary = boundaryDev; bestRms = rmsDev;
                    bestFolded = folded; bestAngle = angle; bestG1Samples = g1n;
                    bestSD = sd; bestG2n = g2n;
                    bestAngleInner = MaxNormalAngleInner;
                    bestWorstSide = LastWorstSide; bestWorstF = LastWorstF;
                    bestWorstNS = LastWorstNS; bestWorstNA = LastWorstNA; bestWorstP = LastWorstP;
                }
                res.RoundLog.Add(string.Format(CultureInfo.InvariantCulture,
                    "轮{0}：偏差 {1:0.######} · 逐边缝隙 {2:0.######} · G1 {3} · G2 {4} · 折叠 {5} · 新增约束 {6}",
                    round, maxDev, boundaryDev,
                    angle >= 0 ? (angle.ToString("0.###", CultureInfo.InvariantCulture) + "°") : "—",
                    sd >= 0 ? (sd.ToString("0.###", CultureInfo.InvariantCulture) + "°") : "—",
                    folded, extraUv.Count));

                // T1：达标早退（偏差 < 0.5% 对角线且 G1/G2 已达标 → 后续轮收益小于一轮成本）
                if (round >= 1 && maxDev < ctx.Diag * 0.005
                    && (angle < 0 || angle <= 1.0)
                    && (sd < 0 || sd <= 20.0))
                {
                    res.Rounds = round + 1;
                    res.RoundLog.Add(string.Format(CultureInfo.InvariantCulture,
                        "      ↳ 达标早退（T1）：偏差 {0:0.######} < 0.5% 对角线、G1/G2 达标 → 不再迭代", maxDev));
                    break;
                }

                // 面积压力（XNURBS「力/能量」层，对应 VK_add_area_pressure 的防塌缩语义）：
                // 结果面面积明显小于基面（欠约束塌缩）→ 下一轮沿基面法向充气；≥ 基面则不加（单向充气）
                {
                    int gA = 12;
                    if (ctx.BaseArea < 0) ctx.BaseArea = BaseAreaEst(ctx, gA);
                    double aCur = SurfaceAreaEst(srf, gA);
                    if (ctx.BaseArea > 1e-12 && aCur < ctx.BaseArea * 0.98)
                    {
                        double deficit = (ctx.BaseArea - aCur) / ctx.BaseArea;
                        ctx.PressureLift = Math.Min(ctx.Diag * 0.01, deficit * ctx.Diag * 0.1);
                        res.RoundLog.Add(string.Format(CultureInfo.InvariantCulture,
                            "      ↳ 面积压力：结果面面积 {0:0.######} < 基面 {1:0.######}（赤字 {2:0.0%}）→ 下轮充气 {3:0.######}",
                            aCur, ctx.BaseArea, deficit, ctx.PressureLift));
                    }
                    else ctx.PressureLift = 0;
                }

                // 局部自适应插结（残差驱动）：误差还大时在误差最大的参数位置插节点 → 局部加密，
                // 不整体抬控制点数（XNURBS 的 47 个导出里没有任何 knot/insert/refine，这是我们的差异点）
                // ⚠ G2 模式停用：权重 100 的曲率带已接管带内形状，插结会重建节点向量、让环带行的
                //   落点与二阶差分行错位（实测轮 3 偏差 2.08→4.40、G1 0.53°→1.35° 的振荡根源）
                if (ctx.Ref.Any && round >= 1 && maxDev > ctx.Diag * 0.002 && round < maxRounds - 1 && s.Continuity < 2)
                {
                    int ak = AddAdaptiveKnots(srf, ctx);
                    if (ak > 0)
                    {
                        res.AdaptiveKnots += ak;
                        res.ControlU = ctx.Nu; res.ControlV = ctx.Nv;
                        res.FinalControlU = ctx.Nu; res.FinalControlV = ctx.Nv;
                        res.RoundLog.Add(string.Format(CultureInfo.InvariantCulture,
                            "      ↳ 局部插结 {0} 个 → 控制网 {1}×{2}（在误差最大的参数位置插，不平摊）", ak, ctx.Nu, ctx.Nv));
                    }
                }

                // 残差驱动：误差大的地方补参考采样点
                bool improved = lastDev > 1e-300 && maxDev < lastDev * 0.95;
                double improve = lastDev > 1e-300 ? (lastDev - maxDev) / lastDev : 1.0;
                lastDev = maxDev;
                if (!ctx.Ref.Any) { res.Rounds = round + 1; break; }
                if (round >= maxRounds - 1)
                {
                    res.Rounds = round + 1;
                    // 只有「最后一轮还在明显下降」才算没收敛（相对 + 绝对双判据，避免小残差下抖动就报警）
                    if (improve > 0.05 && (lastDev - maxDev) > ctx.Diag * 1e-4) res.WarnFlags |= WarnNotConverged;
                    break;
                }
                int added = AddResidualRows(srf, ctx, extraUv, extraRef, maxDev);
                res.Rounds = round + 1;
                if (added == 0) break;
                if (round > 0 && !improved && improve < 0.02) break;
            }

            if (best == null)
            {
                if (string.IsNullOrEmpty(res.Error)) res.Error = "没能解出结果面";
                report = res.Error;
                return res;
            }

            res.Surface = best;
            res.MaxDeviation = bestDev;
            res.MaxDeviation = bestDev;
            res.RmsDeviation = bestRms;
            res.BoundaryDeviation = bestBoundary;
            res.EdgeGaps = bestGaps ?? new double[4];
            res.Folded = bestFolded;
            res.MaxNormalAngleDeg = bestAngle;
            res.MaxNormalAngleInnerDeg = bestAngleInner;
            res.G1Samples = bestG1Samples;
            res.MaxSecondDiffAngleDeg = bestSD;
            res.G2Samples = bestG2n;
            res.WorstAngleSide = bestWorstSide;
            res.WorstAngleF = bestWorstF;
            // 用「被选中的那一轮」的控制网尺寸算曲率连续性（插结后 ctx.Nu/Nv 会变，不能用当前值）
            if (bestNu > 0 && bestNv > 0)
            {
                res.ControlU = bestNu; res.ControlV = bestNv;
                res.CurvatureJump = CurvatureJumpOf(bestCp, bestNu, bestNv);
            }
            else res.CurvatureJump = CurvatureJumpOf(bestCp, ctx.Nu, ctx.Nv);
            res.WorstAngleNS = bestWorstNS;
            res.WorstAngleNA = bestWorstNA;
            res.WorstAngleP = bestWorstP;
            res.Rounds = Math.Max(1, bestRound + 1);

            Brep brep = null;
            try { brep = Brep.CreateFromSurface(best); } catch { brep = null; }
            if (brep == null || brep.Faces.Count == 0)
            {
                res.Error = "结果面转 Brep 失败";
                report = res.Error;
                return res;
            }

            // ---- 内孔修剪
            if (s.KeepHoles && holes.Count > 0)
            {
                int kept; string hwhy;
                Brep trimmed = TrimHoles(brep, best, holes, res.Diag, out kept, out hwhy);
                if (trimmed != null) { brep = trimmed; res.Trimmed = true; res.Holes = kept; }
                else if (!string.IsNullOrEmpty(hwhy)) res.Note = hwhy;
            }

            res.Result = brep;
            res.SourceBoundary.Add(new Polyline(new List<Point3d>(loop.P)));

            // ---- 位掩码告警
            double gapTol = Math.Max(res.Diag * 1e-3, 1e-6);
            if (res.BoundaryDeviation > gapTol) res.WarnFlags |= WarnEdgeGap;
            if (s.Continuity >= 1 && res.MaxNormalAngleDeg >= 0 && res.MaxNormalAngleDeg > 2.0) res.WarnFlags |= WarnTangent;
            if (s.Continuity >= 2 && res.MaxSecondDiffAngleDeg >= 0 && res.MaxSecondDiffAngleDeg > 25.0) res.WarnFlags |= WarnCurvature;
            if (res.Folded > 0) res.WarnFlags |= WarnDegenerate;
            res.WarnText = WarnText(res.WarnFlags);

            res.Seconds = sw.Elapsed.TotalSeconds;
            report = BuildReport(res, ctx, edges);
            return res;
        }

        // ============================================================ 装配

        static void Assemble(Ctx ctx, NormalEq ne, bool firstRound, List<Point2d> extraUv, List<Point3d> extraRef)
        {
            int n = Math.Max(ctx.Nu, ctx.Nv);
            int nd = Math.Max(10, Math.Min(41, 2 * n + 1));
            var idx = new int[16]; var coef = new double[16];

            // ---- 数据行（Coons 基面采样，权重 1）
            //      G1/G2 模式：跨向连续性带内（v < 2.2·du）的基面数据行降权 ×0.1 ——
            //      基面在带内与「相切/曲率延续」天然冲突（基面是平的、弦向的），不让位就会把带压平
            //      （实测：G2 环带行权重 30 都拉不动，环带残差 0.19mm、二阶差分角 89°；降权后带内
            //      由 G1 导数行 + G2 环带行 + 参考行接管）。G0 模式不受影响。
            bool downBand = ctx.S.Continuity >= 1 && ctx.G1Offset > 1e-12;
            for (int i = 0; i < nd; i++)
            {
                double u = i / (double)(nd - 1);
                for (int j = 0; j < nd; j++)
                {
                    double v = j / (double)(nd - 1);
                    double wData = WData;
                    if (downBand)
                    {
                        double duU = CrossDu(ctx, 0, u, ctx.G1Offset);    // side0 口径：v 向带宽
                        double dvV = CrossDu(ctx, 3, v, ctx.G1Offset);    // side3 口径：u 向带宽
                        double vv = Math.Min(v, 1.0 - v), uu = Math.Min(u, 1.0 - u);
                        bool inBand = ctx.Disk ? (v < 2.2 * duU) : (vv < 2.2 * duU || uu < 2.2 * dvV);
                        if (inBand) wData = WData * 0.1;
                    }
                    int nnz;
                    TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, u, v, idx, coef, out nnz);
                    ne.AddRow(wData, idx, coef, nnz, BaseAt(ctx, u, v));
                }
            }

            // ---- 盘状模式：接缝 C1 行（u=0 与 u=1 是同一列物理点 → 跨缝切向也要连续：
            //      (P[1,j] − P[0,j]) 与 (P[Nu−1,j] − P[Nu−2,j]) 应相等，否则接缝处法向跳变）
            //      C0 由变量消元保证（末列=首列），这里只补切向连续。
            if (ctx.Disk)
            {
                double wSeam = 1e2;
                for (int j = 0; j < ctx.Nv; j++)
                {
                    var ri = new int[4]; var rc = new double[4];
                    ri[0] = 1 * ctx.Nv + j; rc[0] = 1.0;
                    ri[1] = 0 * ctx.Nv + j; rc[1] = -1.0;
                    ri[2] = (ctx.Nu - 2) * ctx.Nv + j; rc[2] = 1.0;
                    ri[3] = (ctx.Nu - 1) * ctx.Nv + j; rc[3] = -1.0;
                    ne.AddRow(wSeam, ri, rc, 4, Point3d.Origin);
                }
            }

            // ---- 边界行（硬约束，权重 1e6）
            int nb = Math.Max(24, Math.Min(121, 4 * n + 1));
            int sideCount = ctx.Disk ? 1 : 4;      // 盘状模式只有 v=0 一条边界（u 绕整环）
            for (int side = 0; side < sideCount; side++)
            {
                for (int k = 0; k < nb; k++)
                {
                    double f = k / (double)(nb - 1);
                    int nnz;
                    double u, v;
                    UvOf(side, f, out u, out v);
                    TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, u, v, idx, coef, out nnz);
                    ne.AddRow(WBoundary, idx, coef, nnz, SidePoint(ctx.Loop, ctx.P, side, f));
                }
                // 两个端点（4 角）单独再加一行：折线拐角必须被精确插值
                for (int e = 0; e < 2; e++)
                {
                    double f = e;
                    int nnz;
                    double u, v;
                    UvOf(side, f, out u, out v);
                    TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, u, v, idx, coef, out nnz);
                    ne.AddRow(WBoundary, idx, coef, nnz, SidePoint(ctx.Loop, ctx.P, side, f));
                }
                // 拐角点也加硬约束行：折线拐角必须被精确插值（多边形边界缝隙 = 0 的关键）
                List<double> ks = ctx.KinksBySide != null ? ctx.KinksBySide[side] : null;
                if (ks != null)
                {
                    for (int k = 0; k < ks.Count; k++)
                    {
                        double f = ks[k];
                        int nnz;
                        double u, v;
                        UvOf(side, f, out u, out v);
                        TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, u, v, idx, coef, out nnz);
                        ne.AddRow(WBoundary, idx, coef, nnz, SidePoint(ctx.Loop, ctx.P, side, f));
                    }
                }
            }

            // ---- 盘状模式：极点收敛行（v=1 端整排控制点收成同一点）+ 锥形尾
            //      完全重合的点会让插值建面返回 null（surfaceunify 踩过的实测坑）→ 用「向极点收缩」：
            //      末排（v=Nv−1）拉向中心，倒数第二排只约束「不比末排更靠外」，面在极点自然收拢但不塌。
            if (ctx.Disk)
            {
                // 极点收拢行：末排控制点**互相重合**（相对约束），位置由数据/参考行决定 ——
                // 目标写成 P[0,Nv-1]（未知量！右端不能含未知量）→ 用差分行：P[i] − P[0] = 0。
                // 旧做法把目标钉死在 Center：球冠用例里中心真值在 z=10 而 Center 在 z=5，
                // 1e2 的行直接和参考行对撞（实测偏差 2.94 vs 断言 <2.5）；钉死位置还会压死内部点约束。
                // 相对约束 = 纯「收拢成一点」，与 G0/G1 目标无冲突。
                double wPole = 1e2;
                for (int i = 1; i < ctx.Nu - 1; i++)        // 末列是消元变量（= 首列），跳过
                {
                    var ri = new List<int>(); var rc = new List<double>();
                    ri.Add(i * ctx.Nv + (ctx.Nv - 1)); rc.Add(1.0);
                    ri.Add(0 * ctx.Nv + (ctx.Nv - 1)); rc.Add(-1.0);
                    ne.AddRow(wPole, ri.ToArray(), rc.ToArray(), 2, Point3d.Origin);
                    // 倒数第二排朝极点收一半（锥形尾）：P[i,Nv-2] − (P[i,Nv-1] + BaseAt)/2 → 拆成
                    // 相对行（P[i,Nv-2] − P[i,Nv-1]）+ 数据行（BaseAt 目标已有），这里只约束相对量的一半尺度
                    if (ctx.Nv >= 2)
                    {
                        var ri2 = new List<int>(); var rc2 = new List<double>();
                        ri2.Add(i * ctx.Nv + (ctx.Nv - 2)); rc2.Add(1.0);
                        ri2.Add(i * ctx.Nv + (ctx.Nv - 1)); rc2.Add(-0.5);
                        ri2.Add(0 * ctx.Nv + (ctx.Nv - 1)); rc2.Add(-0.5);
                        ne.AddRow(wPole * 0.25, ri2.ToArray(), rc2.ToArray(), 3, Point3d.Origin);
                    }
                }
            }

            // ---- 参考几何采样（贴合强度 = 权重比例；也是残差迭代追加的那一族）
            //      T2：首轮采样缓存 —— 同一输入下后续轮的行完全一致（确定性），省每轮 nc² 次 Closest
            double wRef = WInner * Clamp01(ctx.S.FitStrength);
            if (ctx.Ref.Any && wRef > 0)
            {
                if (ctx.RefCacheUv == null)
                {
                    ctx.RefCacheUv = new List<Point2d>();
                    ctx.RefCachePts = new List<Point3d>();
                    int nc = Math.Max(5, Math.Min(25, ctx.Nu));
                    double cap = ctx.Diag * 0.75;
                    for (int i = 0; i < nc; i++)
                    {
                        double u = i / (double)(nc - 1);
                        for (int j = 0; j < nc; j++)
                        {
                            double v = j / (double)(nc - 1);
                            Point3d b = BaseAt(ctx, u, v);
                            Point3d q; Vector3d nn; double d; bool atEdge;
                            if (!ctx.Ref.Closest(b, cap, out q, out nn, out d, out atEdge)) continue;
                            if (atEdge) continue;          // 参考几何没覆盖到洞里 → 不能当内部约束
                            ctx.RefCacheUv.Add(new Point2d(u, v));
                            ctx.RefCachePts.Add(q);
                        }
                    }
                }
                for (int k = 0; k < ctx.RefCacheUv.Count; k++)
                {
                    int nnz;
                    TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, ctx.RefCacheUv[k].X, ctx.RefCacheUv[k].Y, idx, coef, out nnz);
                    ne.AddRow(wRef, idx, coef, nnz, ctx.RefCachePts[k]);
                }
            }

            // ---- 残差迭代补的参考采样
            if (extraUv != null && extraRef != null && wRef > 0)
            {
                for (int k = 0; k < extraUv.Count && k < extraRef.Count; k++)
                {
                    int nnz;
                    TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, extraUv[k].X, extraUv[k].Y, idx, coef, out nnz);
                    ne.AddRow(wRef, idx, coef, nnz, extraRef[k]);
                }
            }

            // ---- 内部曲线 / 点（权重 10）
            AddInnerRows(ctx, ne, idx, coef);

            // ---- G1：线性化跨向导数约束（精确：夹持节点下 ∂S/∂n 只依赖最外两排控制点）
            // ---- G1/G2 共用的「连续性带」（XNURBS 宿主导数采样路线）：
            //      G1 = 环1 位置带（跨向速度换算参数 + 吸附到相邻面，权重 100）——
            //           取代旧的 0.1 弱切线带（实测：0.1 带压不住带内的基面数据行，G1 模式偏差 1.67）；
            //      G2 = 环1 + 环2 + 二阶差分行（曲率匹配）。
            if (ctx.S.Continuity >= 1)
            {
                BuildBand(ctx);
                AddG1DerivativeRows(ctx, ne);
                AddBandRows(ctx, ne, idx, coef);        // 环带行：G1=ring1，G2=ring1+ring2（一致参数化）
            }

            // ---- 薄板能量正则项
            AddRegularization(ctx, ne, idx, coef);

            // ---- 面积压力（XNURBS VK_add_area_pressure 语义：防塌缩充气；PressureLift=0 时不加行）
            if (ctx.PressureLift > 1e-12) AddPressureRows(ctx, ne, idx, coef);

            ne.Ridge(1e-10);
        }

        static void UvOf(int side, double f, out double u, out double v)
        {
            switch (side)
            {
                case 0: u = f; v = 0.0; break;
                case 1: u = 1.0; v = f; break;
                case 2: u = f; v = 1.0; break;
                default: u = 0.0; v = f; break;
            }
        }

        /// <summary>
        /// 盘状模式的解析 UV：点 p → (u, v)。
        /// u = p 的方位在边界环上的弧长位置（沿中心射线投到边界环上找最近点）；
        /// v = |中心−p| / |中心−对应边界点|（截断到 [0,1]）。
        /// 直接解析可逆（基面就是「边界点→中心」的线性混合），不依赖插值基面
        /// （v=1 全排重合会让插值基面退化，ClosestPoint 定位不可靠，实测踩到）。
        /// </summary>
        internal static Point2d DiskUvOf(Ctx ctx, Point3d p)
        {
            double bestD = double.MaxValue; double bestU = 0; double bestR = 0;
            Vector3d pv = p - ctx.Center;
            double pr = pv.Length;                       // p 离中心的距离（v 的分子）
            int cnt = ctx.Loop.P.Length;
            for (int i = 0; i < cnt; i++)
            {
                // 边界点 b_i 与中心 c：射线 c→b_i。u 由「p 在哪条射线的方位上」决定（横向偏差最小），
                // v = |p−c| / |b−c|（径向比例，与射线投影无关 —— 北极这类「正上方」的点投影 t=0，
                // 用投影距离当 v 会把极点错映射到边界 v=0，实测踩到）。
                Point3d b = ctx.Loop.P[i];
                Vector3d ray = b - ctx.Center;
                double rl = ray.Length;
                if (rl < 1e-12) continue;
                ray = new Vector3d(ray.X / rl, ray.Y / rl, ray.Z / rl);
                double t = pv * ray;
                double lateral = (pv - ray * t).Length;
                double d = lateral + Math.Abs(t - rl) * 0.05;    // 横向为主
                if (d < bestD)
                {
                    bestD = d;
                    bestU = ctx.Loop.Cum[i] / ctx.Loop.Length;
                    bestR = pr / rl;
                }
            }
            double vv = bestR;
            if (vv < 0) vv = 0; else if (vv > 1) vv = 1;
            return new Point2d(bestU, vv);
        }

        /// <summary>内部曲线 / 点约束：先投到基面求 (u,v)，再以权重 10 作为数据行</summary>
        static void AddInnerRows(Ctx ctx, NormalEq ne, int[] idx, double[] coef)
        {
            if (ctx.Inner == null || ctx.Inner.Count == 0) return;
            var pts = new List<Point3d>();
            for (int i = 0; i < ctx.Inner.Count; i++)
            {
                GeometryBase g = ctx.Inner[i];
                if (g == null) continue;
                if (g is Rhino.Geometry.Point p) { pts.Add(p.Location); continue; }
                if (g is PointCloud pc) { try { for (int k = 0; k < pc.Count; k++) pts.Add(pc[k].Location); } catch { } continue; }
                Curve c = g as Curve;
                if (c != null)
                {
                    double len = 0;
                    try { len = c.GetLength(); } catch { }
                    int m = Math.Max(8, Math.Min(200, (int)Math.Ceiling(Math.Max(len, 1e-9) / Math.Max(ctx.Diag * 2e-3, 1e-9))));
                    for (int k = 0; k <= m; k++)
                    {
                        try { pts.Add(c.PointAtLength(len * k / (double)m)); } catch { }
                    }
                }
            }
            for (int i = 0; i < pts.Count; i++)
            {
                double uu, vv;
                if (ctx.Disk)
                {
                    // 盘状：解析 UV（插值基面在极点退化，ClosestPoint 定位不可靠）
                    Point2d uv = DiskUvOf(ctx, pts[i]);
                    uu = uv.X; vv = uv.Y;
                }
                else
                {
                    if (ctx.BaseSrf == null) return;
                    double u, v;
                    if (!ctx.BaseSrf.ClosestPoint(pts[i], out u, out v)) continue;
                    // 基面参数域可能不是 [0,1] → 归一化到 [0,1]（否则张量基行会落到错的位置）
                    Interval bu = ctx.BaseSrf.Domain(0), bv = ctx.BaseSrf.Domain(1);
                    uu = bu.Max - bu.Min > 1e-12 ? (u - bu.Min) / (bu.Max - bu.Min) : u;
                    vv = bv.Max - bv.Min > 1e-12 ? (v - bv.Min) / (bv.Max - bv.Min) : v;
                    if (uu < -0.01 || uu > 1.01 || vv < -0.01 || vv > 1.01) continue;
                }
                int nnz;
                TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, Clamp01(uu), Clamp01(vv), idx, coef, out nnz);
                ne.AddRow(WInner, idx, coef, nnz, pts[i]);
            }
        }

        /// <summary>
        /// G1 的精确实现：把「边界跨向导数」写成线性行。
        /// 夹持节点下 ∂S/∂v|v=0 = (3/t1)·(P[i,1] − P[i,0])（t1 = 第一个非零节点区间），
        /// 把系数归一化（两边同乘 t1/3）→ 行系数 O(1)、右端 = (t1/3)·目标导数，权重 100/300。
        /// 目标导数 = 行军缓存的 D1 = (q1−p)/v1 —— 与 ring1 行**同一个映射**导出的速度：
        /// 「基面点走到参考面投影、弦距恰为 G1 采样距离」的平均速度。这样 G1 导数行与环带行、
        /// 参考采样行三者严格相容（旧口径用基面弦速当目标速度，球冠上与投影映射差 2 倍，互相拉扯）。
        /// </summary>
        static void AddG1DerivativeRows(Ctx ctx, NormalEq ne)
        {
            if (ctx.Band == null) return;
            // 权重取 100：比内部数据行（1）与参考采样（10）强 1~2 个量级 → 切线方向被显著拉正，
            // 又不至于把 4 个参数角点压成退化切平面（实测 1e4 会在角点附近折掉）。
            // 盘状模式没有参数角点（前任 1e4 折叠坑只存在于 4 角切分）→ 权重可以更狠（300）。
            double wG1 = ctx.Disk ? 300.0 : 100.0;
            var Nu = new double[Degree + 1];
            for (int side = 0; side < ctx.Band.Length; side++)
            {
                BandCache bc = ctx.Band[side];
                if (bc == null || bc.F.Count == 0) continue;
                bool vDir = (side == 0 || side == 2);
                // 跨向导数尺度：side0/side3 用「第一个节点区间」= U[p+1]-U[p]；
                // side1/side2 用「最后一个非退化区间」= U[n]-U[n-1]（n = 控制点数）。
                // ⚠ 别写成 U[n+p]-U[n+p-1] —— 夹持端最后 p+1 个节点全相等，会算出 0 → 目标恒为 0 → 那两条边被压平。
                bool atStart = (side == 0 || side == 3);
                double t1;
                if (vDir) t1 = atStart ? (ctx.V[Degree + 1] - ctx.V[Degree])
                                       : (ctx.V[ctx.Nv] - ctx.V[ctx.Nv - 1]);
                else t1 = atStart ? (ctx.U[Degree + 1] - ctx.U[Degree])
                                  : (ctx.U[ctx.Nu] - ctx.U[ctx.Nu - 1]);
                if (!(t1 > 1e-12)) continue;
                double spanScale = t1 / Degree;                  // 控制点差 = (t1/3)·导数
                for (int k = 0; k < bc.F.Count; k++)
                {
                    double f = bc.F[k];
                    Vector3d d1 = bc.D1[k];                      // (q1−p)/v1，含正确方向与量级
                    // 行：Σ_i N_i(f)·(P[i,row1] − P[i,row0])
                    // 注意：沿边界走的那个方向（v 边界 → u；u 边界 → v）才是「求基函数」的方向；
                    // t1（导数尺度）来自跨边界方向 —— 两者别搞混（搞混 = 约束落在错的位置）。
                    double[] KU = vDir ? ctx.U : ctx.V;
                    int KC = vDir ? ctx.Nu : ctx.Nv;
                    int span = FindSpan(KU, Degree, KC, f);
                    BasisFuns(KU, Degree, span, f, Nu);
                    var ri = new List<int>(16); var rc = new List<double>(16);
                    for (int a2 = 0; a2 <= Degree; a2++)
                    {
                        int i = span - Degree + a2;
                        if (i < 0 || i >= KC) continue;
                        double w = Nu[a2];
                        if (Math.Abs(w) < 1e-14) continue;
                        if (vDir)
                        {
                            int r1 = (side == 0) ? 1 : ctx.Nv - 1;
                            int r0 = (side == 0) ? 0 : ctx.Nv - 2;
                            ri.Add(i * ctx.Nv + r1); rc.Add(w);
                            ri.Add(i * ctx.Nv + r0); rc.Add(-w);
                        }
                        else
                        {
                            int c1 = (side == 3) ? 1 : ctx.Nu - 1;
                            int c0 = (side == 3) ? 0 : ctx.Nu - 2;
                            ri.Add(c1 * ctx.Nv + i); rc.Add(w);
                            ri.Add(c0 * ctx.Nv + i); rc.Add(-w);
                        }
                    }
                    int m = ri.Count;
                    if (m == 0) continue;
                    Point3d target = new Point3d(d1.X * spanScale, d1.Y * spanScale, d1.Z * spanScale);
                    ne.AddRow(wG1, ri.ToArray(), rc.ToArray(), m, target);
                }
            }
        }

        // （旧「G1 切线带」AddTangentRows 已删除：0.1 权重压不住带内的基面数据行（实测 G1 模式偏差
        //   1.67 vs 环1 机制 0.2 级），且参数换算（边长口径）与 G1 导数行不一致 —— 由 AddG2Rows 的
        //   ring1（跨向速度换算 + 吸附 + 权重 100）取代，G1/G2 共用。）

        // ============================================================ 连续性带（G1/G2 共用，XNURBS 路线）

        /// <summary>
        /// 连续性带的「行军」采样缓存（每条边一份，Generate 内一次性构建、各轮复用）。
        /// 关键设计：带内参数位置与目标点来自**同一个映射**（基面参数点 → 参考面最近点）——
        /// 与参考采样行完全一致。旧口径（基面弦速 CrossDu 换算参数位置）在球冠上与参考行的
        /// 隐含映射同参数差 1.3mm（球冠弦速 8.66 vs 投影弧速 17.3），权重 100 的环带行与
        /// 权重 10 的参考行在带内打结 → 形变、偏差恶化。行军采样从根上消掉这个冲突。
        /// </summary>
        internal sealed class BandCache
        {
            public List<double> F = new List<double>();        // 有效的 f 采样（已跳过角点 / 无相邻面）
            public List<double> V1 = new List<double>();       // ring1 的跨向参数位置
            public List<double> V2 = new List<double>();       // ring2 的跨向参数位置（G2）
            public List<Point3d> Q1 = new List<Point3d>();     // ring1 目标（参考面上的真实延续点）
            public List<Point3d> Q2 = new List<Point3d>();     // ring2 目标（G2）
            public List<Vector3d> D1 = new List<Vector3d>();   // (q1−p)/v1：G1 导数行的「一致」目标速度
        }

        /// <summary>带行军的基面点：跨向参数 vk 沿该边的洞内方向推进</summary>
        static Point3d BandBaseAt(Ctx ctx, int side, double f, double vk)
        {
            double u, v;
            BandUvOf(side, f, vk, out u, out v);
            return BaseAt(ctx, u, v);
        }

        static void BandUvOf(int side, double f, double vk, out double u, out double v)
        {
            switch (side)
            {
                case 0: u = f; v = vk; break;
                case 1: u = 1.0 - vk; v = f; break;
                case 2: u = f; v = 1.0 - vk; break;
                default: u = vk; v = f; break;
            }
        }

        /// <summary>
        /// 行军：跨向参数从 0 向洞内推进，返回「投影点离边界弦距 ≥ dist」的参数位置与目标点。
        /// 12 步粗进 + 3 次二分；参考面没覆盖到（atEdge / 未命中）→ 失败（该采样跳过）。
        /// </summary>
        static bool MarchBand(Ctx ctx, int side, double f, Point3d p, double dist, double cap,
            out double vHit, out Point3d qHit)
        {
            vHit = 0; qHit = p;
            double vmax = 0.45;
            int K = 12;
            double prevV = 0;
            Point3d prevQ = p;
            for (int k = 1; k <= K; k++)
            {
                double vk = vmax * k / K;
                Point3d q; Vector3d nn; double d; bool atEdge;
                if (!ctx.Ref.Closest(BandBaseAt(ctx, side, f, vk), cap, out q, out nn, out d, out atEdge) || atEdge) return false;
                if (q.DistanceTo(p) >= dist)
                {
                    double lo = prevV, hi = vk;
                    Point3d qlo = prevQ;
                    for (int it = 0; it < 3; it++)
                    {
                        double mid = 0.5 * (lo + hi);
                        Point3d qm; Vector3d nm; double dm; bool ae;
                        if (!ctx.Ref.Closest(BandBaseAt(ctx, side, f, mid), cap, out qm, out nm, out dm, out ae) || ae) break;
                        if (qm.DistanceTo(p) >= dist) hi = mid;
                        else { lo = mid; qlo = qm; }
                    }
                    vHit = hi;
                    Point3d qh; Vector3d nh; double dh; bool aeh;
                    if (ctx.Ref.Closest(BandBaseAt(ctx, side, f, vHit), cap, out qh, out nh, out dh, out aeh) && !aeh)
                        qHit = qh;
                    else qHit = qlo;
                    return true;
                }
                prevV = vk; prevQ = q;
            }
            return false;
        }

        /// <summary>构建连续性带缓存（一次性；G0 / 无参考 / 无采样距离 → 空带）</summary>
        static void BuildBand(Ctx ctx)
        {
            if (ctx.BandReady) return;
            ctx.BandReady = true;
            if (!ctx.Ref.Any || !(ctx.G1Offset > 1e-12)) return;
            int sideCount = ctx.Disk ? 1 : 4;      // 盘状模式只有 v=0 一条边界
            int n = Math.Max(ctx.Nu, ctx.Nv);
            int nb = Math.Max(16, Math.Min(81, 3 * n + 1));
            double cap = ctx.G1Offset * 8.0 + ctx.Diag * 1e-3;
            ctx.Band = new BandCache[sideCount];
            for (int side = 0; side < sideCount; side++)
            {
                double sideLen = SideLength(ctx.Loop, ctx.P, side);
                if (!(sideLen > 1e-9)) continue;
                var bc = new BandCache();
                ctx.Band[side] = bc;
                for (int k = 1; k < nb - 1; k++)     // 跳过角点（f=0/1），同边界行口径
                {
                    double f = k / (double)(nb - 1);
                    double arc = SideArc(ctx.Loop, ctx.P, side, f);
                    Point3d p = PointAtArc(ctx.Loop, arc);
                    Vector3d nrm = NormalAtArc(ctx.Loop, arc);
                    if (!nrm.IsValid || nrm.Length < 1e-12) continue;   // 无相邻面 → 退化 G0
                    nrm.Unitize();
                    if (!InwardDir(ctx, p, nrm).IsValid) continue;
                    double v1, v2; Point3d q1, q2;
                    if (!MarchBand(ctx, side, f, p, ctx.G1Offset, cap, out v1, out q1)) continue;
                    // 两圈都行军（ring2 的目标/位置缓存供 G2 行与 G2 度量共用；度量与曲率证据都要用它）
                    if (!MarchBand(ctx, side, f, p, 2.0 * ctx.G1Offset, cap, out v2, out q2)) continue;
                    bc.F.Add(f);
                    bc.V1.Add(v1); bc.Q1.Add(q1);
                    bc.V2.Add(v2); bc.Q2.Add(q2);
                    // G1 导数行的目标速度：弦向量**去掉法向分量**（切平面投影）再除以 v1。
                    // ⚠ 直接用弦方向会在弧线上倾斜 κ·d/2（球冠 1mm/10 = 2.86°——实测 G1 角恰好退化 2.95°）；
                    //   弦 = d·切向 − (d²κ/2)·法向，投影掉法向剩下的就是纯切向 × 弦长。
                    Vector3d chord = new Vector3d(q1.X - p.X, q1.Y - p.Y, q1.Z - p.Z);
                    double cn = chord * nrm;
                    bc.D1.Add(new Vector3d(chord.X - nrm.X * cn, chord.Y - nrm.Y * cn, chord.Z - nrm.Z * cn) / v1);
                }
            }
        }

        /// <summary>
        /// 环带位置行：ring1（G1/G2 都加，权重 100）+ ring2（仅 G2）。
        /// 参数位置与目标来自同一个「基面→参考面投影」映射（行军缓存）→ 与参考采样行天然相容；
        /// ring1+ring2 两圈位置 + G1 导数行一起，把边界外的 1~2 阶形状钉在相邻面上
        /// （有限差分意义的 G2 —— 两圈位置本身就编码了曲率延续，无需再写二阶差分行）。
        /// 权重 100：比内部数据行（1）强 2 个量级、比边界硬约束（1e6）弱 4 个量级
        /// （BENCH §8.3 条件数告警：1e2 档实测稳定，1e3 不取）。
        /// </summary>
        static void AddBandRows(Ctx ctx, NormalEq ne, int[] idx, double[] coef)
        {
            if (ctx.Band == null) return;
            bool g2 = ctx.S.Continuity >= 2;
            double wBand1 = 100.0;   // ring1：G1/G2 共用，与 G1 导数行同量级
            double wBand2 = 30.0;    // ring2：仅 G2。权重压到 30（100 实测会把带内挤到折叠），
                                     // 曲率方向对位置误差不敏感，30 足够钉住二阶形状
            for (int side = 0; side < ctx.Band.Length; side++)
            {
                BandCache bc = ctx.Band[side];
                if (bc == null) continue;
                for (int k = 0; k < bc.F.Count; k++)
                {
                    int nnz;
                    double u, v;
                    BandUvOf(side, bc.F[k], bc.V1[k], out u, out v);
                    TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, u, v, idx, coef, out nnz);
                    ne.AddRow(wBand1, idx, coef, nnz, bc.Q1[k]);
                    if (g2)
                    {
                        BandUvOf(side, bc.F[k], bc.V2[k], out u, out v);
                        TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, u, v, idx, coef, out nnz);
                        ne.AddRow(wBand2, idx, coef, nnz, bc.Q2[k]);
                    }
                }
            }
        }

        // G2 度量的最差位置（诊断）
        internal static int LastG2Side = -1;
        internal static double LastG2F = -1;
        // G2 度量的诊断统计（末次调用）：样本数 / 90° 分支（结果面近直）数 / 非直样本最大角 /
        // 环带目标残差（结果面在 δ/2δ 处离吸附目标的距离最大值 —— 判断环带行有没有被满足）
        internal static int LastG2Samples = 0;
        internal static int LastG2N90 = 0;
        internal static double LastG2MaxNon90 = 0;
        internal static double LastG2RingRes1 = 0;
        internal static double LastG2RingRes2 = 0;

        /// <summary>
        /// G2 度量：边界采样处「结果面二阶差分向量」与「相邻面二阶差分向量」的最大夹角（度）。
        /// 有限差分口径：结果面取行军缓存的跨向参数 0/v1/v2 三点二阶差分；相邻面真值 = 缓存的
        /// p/q1/q2（同一个「基面→参考面投影」映射）。二阶差分向量方向 = 法曲率向量方向
        /// （与参数化速度无关）→ 方向一致 = 有限差分意义的 G2。
        /// 相邻面二阶差分近零（平坦带）→ 无方向可比，跳过；结果面二阶差分 ≪ 相邻面（&lt;2%）→ 记 90°。
        /// </summary>
        internal static double SecondDiffAngle(NurbsSurface srf, Ctx ctx, out int samples)
        {
            samples = 0;
            LastG2Side = -1; LastG2F = -1;
            LastG2Samples = 0; LastG2N90 = 0; LastG2MaxNon90 = 0; LastG2RingRes1 = 0; LastG2RingRes2 = 0;
            if (srf == null || ctx.Band == null) return -1;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            double worst = 0;
            for (int side = 0; side < ctx.Band.Length; side++)
            {
                BandCache bc = ctx.Band[side];
                if (bc == null) continue;
                for (int k = 0; k < bc.F.Count; k++)
                {
                    double f = bc.F[k];
                    Point3d p = PointAtArc(ctx.Loop, SideArc(ctx.Loop, ctx.P, side, f));
                    Vector3d vecA = new Vector3d(bc.Q2[k].X - 2.0 * bc.Q1[k].X + p.X,
                                                 bc.Q2[k].Y - 2.0 * bc.Q1[k].Y + p.Y,
                                                 bc.Q2[k].Z - 2.0 * bc.Q1[k].Z + p.Z);
                    double la = vecA.Length;
                    if (la < ctx.Diag * 1e-6) continue;    // 相邻面此处近直（平坦带）→ 无曲率方向可比
                    double u0, v0; UvOf(side, f, out u0, out v0);
                    double u1, v1; BandUvOf(side, f, bc.V1[k], out u1, out v1);
                    double u2, v2; BandUvOf(side, f, bc.V2[k], out u2, out v2);
                    Point3d p0, pa, pb2;
                    try
                    {
                        p0 = srf.PointAt(du.ParameterAt(u0), dv.ParameterAt(v0));
                        pa = srf.PointAt(du.ParameterAt(u1), dv.ParameterAt(v1));
                        pb2 = srf.PointAt(du.ParameterAt(u2), dv.ParameterAt(v2));
                    }
                    catch { continue; }
                    Vector3d vecR = new Vector3d(pb2.X - 2.0 * pa.X + p0.X,
                                                 pb2.Y - 2.0 * pa.Y + p0.Y,
                                                 pb2.Z - 2.0 * pa.Z + p0.Z);
                    double lr = vecR.Length;
                    LastG2Samples++;
                    LastG2RingRes1 = Math.Max(LastG2RingRes1, pa.DistanceTo(bc.Q1[k]));
                    LastG2RingRes2 = Math.Max(LastG2RingRes2, pb2.DistanceTo(bc.Q2[k]));
                    double ang;
                    if (lr < la * 0.02)
                    {
                        ang = 90.0;                        // 结果面此处几乎不弯 → 曲率明显不匹配
                        LastG2N90++;
                    }
                    else
                    {
                        double c = Math.Abs(vecR * vecA) / (lr * la);
                        if (c > 1) c = 1;
                        ang = Math.Acos(c) * 180.0 / Math.PI;
                        if (ang > LastG2MaxNon90) LastG2MaxNon90 = ang;
                    }
                    if (ang > worst) { worst = ang; LastG2Side = side; LastG2F = f; }
                    samples++;
                }
            }
            return samples > 0 ? worst : -1;
        }

        // ============================================================ 面积压力（能量层，VK_add_area_pressure 对应物）

        /// <summary>基面近似面积：ng×ng 采样、跨向一阶差分叉积密度的均值（参数域 [0,1]² 面积 = 1）</summary>
        static double BaseAreaEst(Ctx ctx, int g)
        {
            if (g < 2) g = 2;
            double sum = 0;
            double h = 1e-3;
            for (int i = 0; i < g; i++)
                for (int j = 0; j < g; j++)
                {
                    double u = i / (g - 1.0), v = j / (g - 1.0);
                    double ua = Math.Max(0, u - h), ub = Math.Min(1, u + h);
                    double va = Math.Max(0, v - h), vb = Math.Min(1, v + h);
                    Point3d p00 = BaseAt(ctx, ua, va);
                    Point3d p10 = BaseAt(ctx, ub, va);
                    Point3d p01 = BaseAt(ctx, ua, vb);
                    Vector3d tu = p10 - p00, tv = p01 - p00;
                    double st = (ub - ua) * (vb - va);
                    if (st < 1e-18) continue;
                    Vector3d cr = Vector3d.CrossProduct(tu, tv);
                    sum += cr.Length / st;
                }
            return sum / (g * g);
        }

        /// <summary>结果面近似面积（与基面同口径：跨向一阶差分密度均值）</summary>
        static double SurfaceAreaEst(NurbsSurface srf, int g)
        {
            if (srf == null || g < 2) return 0;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            double sum = 0;
            double h = 1e-3;
            for (int i = 0; i < g; i++)
                for (int j = 0; j < g; j++)
                {
                    double u = i / (g - 1.0), v = j / (g - 1.0);
                    double ua = Math.Max(0, u - h), ub = Math.Min(1, u + h);
                    double va = Math.Max(0, v - h), vb = Math.Min(1, v + h);
                    Point3d p00, p10, p01;
                    try
                    {
                        p00 = srf.PointAt(du.ParameterAt(ua), dv.ParameterAt(va));
                        p10 = srf.PointAt(du.ParameterAt(ub), dv.ParameterAt(va));
                        p01 = srf.PointAt(du.ParameterAt(ua), dv.ParameterAt(vb));
                    }
                    catch { continue; }
                    Vector3d tu = p10 - p00, tv = p01 - p00;
                    double st = (ub - ua) * (vb - va);
                    if (st < 1e-18) continue;
                    Vector3d cr = Vector3d.CrossProduct(tu, tv);
                    sum += cr.Length / st;
                }
            return sum / (g * g);
        }

        /// <summary>
        /// 面积压力行（XNURBS VK_add_area_pressure 语义）：Generate 里按「面积赤字」算出
        /// PressureLift &gt; 0 时，把基面采样点沿基面法向向外抬 PressureLift 作为低权重位置行 ——
        /// 单向充气：只防收缩（欠约束塌缩），不压膨胀。
        /// </summary>
        static void AddPressureRows(Ctx ctx, NormalEq ne, int[] idx, double[] coef)
        {
            double wPress = 0.5;
            int ng = 12;
            double h = 1e-3;
            for (int i = 0; i < ng; i++)
                for (int j = 0; j < ng; j++)
                {
                    double u = i / (ng - 1.0), v = j / (ng - 1.0);
                    Point3d b = BaseAt(ctx, u, v);
                    Point3d bu1 = BaseAt(ctx, Math.Min(1, u + h), v), bu0 = BaseAt(ctx, Math.Max(0, u - h), v);
                    Point3d bv1 = BaseAt(ctx, u, Math.Min(1, v + h)), bv0 = BaseAt(ctx, u, Math.Max(0, v - h));
                    Vector3d nrm = Vector3d.CrossProduct(bu1 - bu0, bv1 - bv0);
                    double nl = nrm.Length;
                    if (!(nl > 1e-15)) continue;
                    nrm = new Vector3d(nrm.X / nl, nrm.Y / nl, nrm.Z / nl);
                    Point3d target = new Point3d(b.X + nrm.X * ctx.PressureLift,
                                                 b.Y + nrm.Y * ctx.PressureLift,
                                                 b.Z + nrm.Z * ctx.PressureLift);
                    int nnz;
                    TensorRow(ctx.U, ctx.V, ctx.Nu, ctx.Nv, u, v, idx, coef, out nnz);
                    ne.AddRow(wPress, idx, coef, nnz, target);
                }
        }

        /// <summary>
        /// 基面跨向「速度」尺度（每单位参数的长度）：用较大步长的割线（0.05）而不是 1e-4 的差分 ——
        /// 角点上微小差分退化成边界切向，割线朝洞内、非退化。退化时退回对角线尺度。
        /// </summary>
        static double CrossSpeed(Ctx ctx, double u, double v, bool vDir, int side)
        {
            double h2 = 0.05;
            Point3d a = BaseAt(ctx, u, v);
            double u2 = u, v2 = v;
            if (vDir) v2 = (side == 0) ? Math.Min(1.0, v + h2) : Math.Max(0.0, v - h2);
            else u2 = (side == 3) ? Math.Min(1.0, u + h2) : Math.Max(0.0, u - h2);
            Point3d b = BaseAt(ctx, u2, v2);
            double d = a.DistanceTo(b) / h2;
            if (!(d > 1e-9)) d = ctx.Diag;
            return d;
        }

        /// <summary>
        /// 把物理偏移距离换算成跨向参数步长（用基面跨向速度）：
        /// 保证「S(参数 +du) ≈ 边界点 + du·跨向速度」与 G1 导数行的线性含义一致，
        /// G2 环带行 / 二阶差分行 / G2 度量共用同一换算（du 与物理位置严格对应）。
        /// </summary>
        static double CrossDu(Ctx ctx, int side, double f, double dist)
        {
            double u, v;
            UvOf(side, f, out u, out v);
            bool vDir = (side == 0 || side == 2);
            double mag = CrossSpeed(ctx, u, v, vDir, side);
            if (!(mag > 1e-9)) mag = ctx.Diag;
            double du = dist / mag;
            if (du < 1e-4) du = 1e-4; else if (du > 0.3) du = 0.3;
            return du;
        }

        /// <summary>洞内方向：把「洞中心 − 边界点」投到相邻面切平面；退化时用基面内法向</summary>
        static Vector3d InwardDir(Ctx ctx, Point3d p, Vector3d nrm)
        {
            Vector3d d = ctx.Centroid - p;
            Vector3d t = d - nrm * (d * nrm);
            if (t.IsValid && t.Length > 1e-9) { t.Unitize(); return t; }
            return Vector3d.Zero;
        }

        /// <summary>薄板能量（二阶差分 / 13 点双调和模板；靠边一圈用一阶拉普拉斯）</summary>
        static void AddRegularization(Ctx ctx, NormalEq ne, int[] idx, double[] coef)
        {
            double lam = Clamp01(ctx.S.Smooth);
            lam = lam * lam;                     // 0..1
            if (!(lam > 1e-12)) return;
            double w = lam;
            int nu = ctx.Nu, nv = ctx.Nv;
            var ri = new List<int>(); var rj = new List<int>(); var rv = new List<double>();
            for (int i = 1; i <= nu - 2; i++)
            {
                for (int j = 1; j <= nv - 2; j++)
                {
                    bool bi = i >= 2 && i <= nu - 3 && j >= 2 && j <= nv - 3;
                    if (bi)
                    {
                        // 13 点双调和模板：20 P(i,j) − 8[P(i±1,j)+P(i,j±1)] + 2[P(i±1,j±1)] + [P(i±2,j)+P(i,j±2)]
                        ri.Clear(); rj.Clear(); rv.Clear();
                        Add(ri, rj, rv, i, j, 20.0);
                        Add(ri, rj, rv, i - 1, j, -8.0); Add(ri, rj, rv, i + 1, j, -8.0);
                        Add(ri, rj, rv, i, j - 1, -8.0); Add(ri, rj, rv, i, j + 1, -8.0);
                        Add(ri, rj, rv, i - 1, j - 1, 2.0); Add(ri, rj, rv, i + 1, j - 1, 2.0);
                        Add(ri, rj, rv, i - 1, j + 1, 2.0); Add(ri, rj, rv, i + 1, j + 1, 2.0);
                        Add(ri, rj, rv, i - 2, j, 1.0); Add(ri, rj, rv, i + 2, j, 1.0);
                        Add(ri, rj, rv, i, j - 2, 1.0); Add(ri, rj, rv, i, j + 2, 1.0);
                        Emit(ne, w, ri, rj, rv, nv);
                    }
                    else
                    {
                        ri.Clear(); rj.Clear(); rv.Clear();
                        Add(ri, rj, rv, i, j, -2.0);
                        Add(ri, rj, rv, i - 1, j, 1.0); Add(ri, rj, rv, i + 1, j, 1.0);
                        Emit(ne, w, ri, rj, rv, nv);

                        ri.Clear(); rj.Clear(); rv.Clear();
                        Add(ri, rj, rv, i, j, -2.0);
                        Add(ri, rj, rv, i, j - 1, 1.0); Add(ri, rj, rv, i, j + 1, 1.0);
                        Emit(ne, w, ri, rj, rv, nv);
                    }
                }
            }
        }

        static void Add(List<int> ri, List<int> rj, List<double> rv, int i, int j, double c)
        {
            ri.Add(i); rj.Add(j); rv.Add(c);
        }

        static void Emit(NormalEq ne, double w, List<int> ri, List<int> rj, List<double> rv, int nv)
        {
            int m = ri.Count;
            if (m == 0) return;
            var idx = new int[m]; var coef = new double[m];
            for (int k = 0; k < m; k++) { idx[k] = ri[k] * nv + rj[k]; coef[k] = rv[k]; }
            ne.AddRow(w, idx, coef, m, Point3d.Origin);
        }

        /// <summary>残差驱动局部插结：在误差最大的参数位置插 u/v 节点（每轮最多 1+1，控制点数不超过上限）</summary>
        static int AddAdaptiveKnots(NurbsSurface srf, Ctx ctx)
        {
            if (srf == null || !ctx.Ref.Any) return 0;
            int g = 21;
            double cap = ctx.Diag * 0.75;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            double worst = -1, bu = -1, bv = -1;
            for (int i = 0; i < g; i++)
                for (int j = 0; j < g; j++)
                {
                    Point3d p;
                    try { p = srf.PointAt(du.ParameterAt(i / (g - 1.0)), dv.ParameterAt(j / (g - 1.0))); } catch { continue; }
                    Point3d q; Vector3d nn; double d; bool atEdge;
                    if (!ctx.Ref.Closest(p, cap, out q, out nn, out d, out atEdge)) continue;
                    if (atEdge) continue;
                    if (d > worst) { worst = d; bu = i / (g - 1.0); bv = j / (g - 1.0); }
                }
            if (!(worst > 0) || bu < 0) return 0;
            int added = 0;
            if (ctx.Nu + ctx.ExtraU.Count < MaxControl && bu > 0.04 && bu < 0.96 && !NearKnot(ctx.U, bu)) { ctx.ExtraU.Add(bu); added++; }
            if (ctx.Nv + ctx.ExtraV.Count < MaxControl && bv > 0.04 && bv < 0.96 && !NearKnot(ctx.V, bv)) { ctx.ExtraV.Add(bv); added++; }
            if (added > 0)
            {
                int n = Math.Max(MinControl, Math.Min(MaxControl, ctx.S.ControlCount));
                // ⚠ 目标控制点数要 + 插结数量：BuildKnots 的 interiorCount = n - p - 1，
                // 只传原 n 会让「均匀节点让位给插结点」→ 控制点数不变（实测踩到）
                ctx.U = BuildKnots(n + ctx.ExtraU.Count, ctx.KinksU, ctx.ExtraU);
                ctx.V = BuildKnots(n + ctx.ExtraV.Count, ctx.KinksV, ctx.ExtraV);
                ctx.Nu = ctx.U.Length - Degree - 1;
                ctx.Nv = ctx.V.Length - Degree - 1;
            }
            return added;
        }

        static bool NearKnot(double[] U, double t)
        {
            if (U == null) return false;
            for (int i = 0; i < U.Length; i++) if (Math.Abs(U[i] - t) < 5e-3) return true;
            return false;
        }

        /// <summary>残差驱动：在误差大的地方补参考采样点（返回新增行数）</summary>
        static int AddResidualRows(NurbsSurface srf, Ctx ctx, List<Point2d> extraUv, List<Point3d> extraRef, double maxDev)
        {
            if (srf == null || !ctx.Ref.Any) return 0;
            // 残差轮网格：维持 v1 口径（min(61, 3·Nu)）—— 21×21 的 T2 压缩实测会让球冠迭代振荡
            //（轮 2→3 偏差 1.1→2.7、折叠 4）；T2 的提速由参考采样缓存 + 达标早退 + 盘状跳基面承担
            int g = Math.Max(15, Math.Min(61, 3 * ctx.Nu));
            // thr 绝对下限 diag*1e-5（旧 1e-4）：均匀节点修好后一轮就能到 0.0008 级，
            // 1e-4 的下限让迭代在「已经挺准」的洞上不再触发（实测轮数掉到 1）→ 下限收紧，
            // 让残差驱动继续把面往参考几何压（每轮毫秒级，代价可忽略）。
            double thr = Math.Max(maxDev * 0.35, ctx.Diag * 1e-5);
            double cap = ctx.Diag * 0.75;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            int added = 0;
            for (int i = 0; i < g; i++)
            {
                double u = i / (double)(g - 1);
                for (int j = 0; j < g; j++)
                {
                    double v = j / (double)(g - 1);
                    Point3d p;
                    try { p = srf.PointAt(du.ParameterAt(u), dv.ParameterAt(v)); } catch { continue; }
                    Point3d q; Vector3d nn; double d; bool atEdge;
                    if (!ctx.Ref.Closest(p, cap, out q, out nn, out d, out atEdge)) continue;
                    if (atEdge) continue;
                    if (d <= thr) continue;
                    // 连续性带内不放残差行（G1/G2）：带内由 G1 导数行 + 环带行（权重 100）接管，
                    // 权重 10 的残差行在带内只会和曲率延续的二阶形状打架（实测轮 3 振荡根源之一）
                    if (ctx.S.Continuity >= 1 && ctx.G1Offset > 1e-12)
                    {
                        double duU = CrossDu(ctx, 0, u, ctx.G1Offset);
                        double dvV = CrossDu(ctx, 3, v, ctx.G1Offset);
                        double vv = Math.Min(v, 1.0 - v), uu = Math.Min(u, 1.0 - u);
                        bool inBand = ctx.Disk ? (v < 2.2 * duU) : (vv < 2.2 * duU || uu < 2.2 * dvV);
                        if (inBand) continue;
                    }
                    // 太靠近已有补点就跳过（避免重复行把系统搞病态）
                    bool near = false;
                    for (int k = extraUv.Count - 1; k >= 0 && k > extraUv.Count - 60; k--)
                    {
                        if (Math.Abs(extraUv[k].X - u) < 0.02 && Math.Abs(extraUv[k].Y - v) < 0.02) { near = true; break; }
                    }
                    if (near) continue;
                    extraUv.Add(new Point2d(u, v));
                    extraRef.Add(q);
                    added++;
                }
            }
            return added;
        }

        // ============================================================ 建面

        /// <summary>建面失败时把原因写在这里（自检/报告用，别静默返回空）</summary>
        internal static string LastBuildError = "";

        internal static NurbsSurface BuildNurbs(Point3d[,] cp, double[] U, double[] V, int nu, int nv)
        {
            LastBuildError = "";
            NurbsSurface srf = null;
            try
            {
                // 注意：Create 的 order = degree + 1（dimension, isRational, order0, order1, count0, count1）
                srf = NurbsSurface.Create(3, false, Degree + 1, Degree + 1, nu, nv);
                if (srf == null) { LastBuildError = "Create 返回空"; return null; }
                if (srf.Points.CountU != nu || srf.Points.CountV != nv)
                {
                    LastBuildError = string.Format(CultureInfo.InvariantCulture, "控制点数不符（{0}×{1}，期望 {2}×{3}）",
                        srf.Points.CountU, srf.Points.CountV, nu, nv);
                    return null;
                }
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                        srf.Points.SetPoint(i, j, cp[i, j]);

                // openNURBS 的节点数组是「完整夹持节点向量去掉首尾各一个」→ 长度 = 控制点数 + order - 2
                int ku = srf.KnotsU.Count, kv = srf.KnotsV.Count;
                if (ku != U.Length - 2 || kv != V.Length - 2)
                {
                    LastBuildError = string.Format(CultureInfo.InvariantCulture,
                        "节点数量不匹配（曲面 {0}/{1}，期望 {2}/{3}）", ku, kv, U.Length - 2, V.Length - 2);
                    return null;
                }
                for (int k = 0; k < ku; k++) srf.KnotsU[k] = U[k + 1];
                for (int k = 0; k < kv; k++) srf.KnotsV[k] = V[k + 1];

                // 夹持节点的硬校验：4 个角点必须等于 4 个角控制点（映射错了立刻发现，不给错面）
                if (!CornersMatch(srf, cp, nu, nv))
                {
                    LastBuildError = "角点校验失败（节点映射不对）";
                    return null;
                }
            }
            catch (Exception ex)
            {
                LastBuildError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
            return srf;
        }

        /// <summary>4 个角点是否对上（夹持节点下角点 = 角控制点）</summary>
        internal static bool CornersMatch(NurbsSurface srf, Point3d[,] cp, int nu, int nv)
        {
            try
            {
                Interval du = srf.Domain(0), dv = srf.Domain(1);
                double tol = 1e-6 * (1.0 + cp[0, 0].DistanceTo(cp[nu - 1, nv - 1]));
                if (srf.PointAt(du.Min, dv.Min).DistanceTo(cp[0, 0]) > tol) return false;
                if (srf.PointAt(du.Max, dv.Min).DistanceTo(cp[nu - 1, 0]) > tol) return false;
                if (srf.PointAt(du.Min, dv.Max).DistanceTo(cp[0, nv - 1]) > tol) return false;
                if (srf.PointAt(du.Max, dv.Max).DistanceTo(cp[nu - 1, nv - 1]) > tol) return false;
                return true;
            }
            catch { return false; }
        }

        // ============================================================ 度量

        /// <summary>最大 / 均方根偏差（结果面 → 参考几何）+ 逐边缝隙</summary>
        internal static void Measure(NurbsSurface srf, Ctx ctx, out double maxDev, out double rmsDev,
            out double boundaryDev, out double[] gaps)
        {
            maxDev = 0; rmsDev = 0; boundaryDev = 0;
            gaps = new double[4];
            if (srf == null) return;
            Interval du = srf.Domain(0), dv = srf.Domain(1);

            if (ctx.Ref.Any)
            {
                double cap = ctx.Diag * 0.75;
                int g = 21;
                double sum = 0; int cnt = 0;
                for (int i = 0; i < g; i++)
                    for (int j = 0; j < g; j++)
                    {
                        Point3d p;
                        try { p = srf.PointAt(du.ParameterAt(i / (g - 1.0)), dv.ParameterAt(j / (g - 1.0))); } catch { continue; }
                        Point3d q; Vector3d nn; double d;
                        if (!ctx.Ref.Closest(p, cap, out q, out nn, out d)) continue;
                        if (d > maxDev) maxDev = d;
                        sum += d * d; cnt++;
                    }
                rmsDev = cnt > 0 ? Math.Sqrt(sum / cnt) : 0;
            }

            // 逐边缝隙：结果面参数边界密采样 → 原边界折线
            int m = Math.Max(64, Math.Min(201, 8 * Math.Max(ctx.Nu, ctx.Nv)));
            if (ctx.Disk)
            {
                // 盘状模式：整条边界在 v=0 → 按 u 的四等分报 4 个「边」值（保持报告结构一致）
                for (int side = 0; side < 4; side++)
                {
                    double worst = 0;
                    for (int k = 0; k < m; k++)
                    {
                        double u = (side + k / (double)(m - 1)) / 4.0;
                        Point3d p;
                        try { p = srf.PointAt(du.ParameterAt(u), dv.Min); } catch { continue; }
                        double d = DistToLoop(ctx.Loop, p);
                        if (d > worst) worst = d;
                    }
                    gaps[side] = worst;
                    if (worst > boundaryDev) boundaryDev = worst;
                }
            }
            else
            {
                for (int side = 0; side < 4; side++)
                {
                    double worst = 0;
                    for (int k = 0; k < m; k++)
                    {
                        double f = k / (double)(m - 1);
                        double u, v; UvOf(side, f, out u, out v);
                        Point3d p;
                        try { p = srf.PointAt(du.ParameterAt(u), dv.ParameterAt(v)); } catch { continue; }
                        double d = DistToLoop(ctx.Loop, p);
                        if (d > worst) worst = d;
                    }
                    gaps[side] = worst;
                    if (worst > boundaryDev) boundaryDev = worst;
                }
            }
        }

        internal static double DistToLoop(LoopData ld, Point3d p)
        {
            double best = double.MaxValue;
            int cnt = ld.P.Length;
            for (int i = 0; i < cnt; i++)
            {
                Point3d a = ld.P[i], b = ld.P[(i + 1) % cnt];
                Vector3d ab = b - a;
                double l2 = ab * ab;
                double t = l2 > 1e-30 ? ((p - a) * ab) / l2 : 0.0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                Point3d q = new Point3d(a.X + ab.X * t, a.Y + ab.Y * t, a.Z + ab.Z * t);
                double d = p.DistanceTo(q);
                if (d < best) best = d;
            }
            return best == double.MaxValue ? 0 : best;
        }

        /// <summary>相对基面法向翻转的单元数（折叠诊断）</summary>
        internal static int CountFolded(NurbsSurface srf, Ctx ctx)
        {
            int folded = 0;
            if (srf == null) return 0;
            int g = 13;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            try
            {
                for (int i = 0; i < g; i++)
                    for (int j = 0; j < g; j++)
                    {
                        double u = i / (g - 1.0), v = j / (g - 1.0);
                        Vector3d n1 = srf.NormalAt(du.ParameterAt(u), dv.ParameterAt(v));
                        if (!n1.IsValid || n1.Length < 1e-12) continue;
                        // 基面法向：中心差分
                        double h = 1e-3;
                        Point3d a = BaseAt(ctx, Math.Max(0, u - h), v);
                        Point3d b = BaseAt(ctx, Math.Min(1, u + h), v);
                        Point3d c = BaseAt(ctx, u, Math.Max(0, v - h));
                        Point3d d = BaseAt(ctx, u, Math.Min(1, v + h));
                        Vector3d tu = b - a, tv = d - c;
                        Vector3d n2 = Vector3d.CrossProduct(tu, tv);
                        if (!n2.IsValid || n2.Length < 1e-15) continue;
                        n1.Unitize(); n2.Unitize();
                        if (n1 * n2 < 0) folded++;
                    }
            }
            catch { }
            return folded;
        }

        /// <summary>G1：结果面边界法向与相邻面法向的最大夹角（度）。LastWorst* = 诊断用最差采样位置</summary>
        internal static int LastWorstSide = -1;
        internal static double LastWorstF = -1;
        internal static Vector3d LastWorstNS = Vector3d.Zero;
        internal static Vector3d LastWorstNA = Vector3d.Zero;
        internal static Point3d LastWorstP = Point3d.Origin;
        internal static double MaxNormalAngleInner = -1;

        internal static double NormalAngle(NurbsSurface srf, Ctx ctx, out int samples)
        {
            samples = 0;
            LastWorstSide = -1; LastWorstF = -1; LastWorstNS = Vector3d.Zero; LastWorstNA = Vector3d.Zero; LastWorstP = Point3d.Origin;
            if (srf == null) return -1;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            double worst = 0;
            double worstInner = 0;
            int m = Math.Max(24, Math.Min(81, 4 * Math.Max(ctx.Nu, ctx.Nv)));
            int sideCount = ctx.Disk ? 1 : 4;
            for (int side = 0; side < sideCount; side++)
            {
                for (int k = 0; k < m; k++)
                {
                    double f = k / (double)(m - 1);
                    double arc = SideArc(ctx.Loop, ctx.P, side, f);
                    Vector3d nAdj = NormalAtArc(ctx.Loop, arc);
                    if (!nAdj.IsValid || nAdj.Length < 1e-12) continue;
                    nAdj.Unitize();
                    double u, v; UvOf(side, f, out u, out v);
                    Vector3d nS;
                    try { nS = srf.NormalAt(du.ParameterAt(u), dv.ParameterAt(v)); } catch { continue; }
                    if (!nS.IsValid || nS.Length < 1e-12) continue;
                    nS.Unitize();
                    double c = Math.Abs(nS * nAdj);
                    if (c > 1) c = 1;
                    double ang = Math.Acos(c) * 180.0 / Math.PI;
                    if (ang > worst) { worst = ang; LastWorstSide = side; LastWorstF = f; LastWorstNS = nS; LastWorstNA = nAdj; LastWorstP = PointAtArc(ctx.Loop, arc); }
                    // 「中段」定义：每条边去掉两端各 15%（角点邻域，也就是切线行被跳过的区段）
                    if (f >= 0.15 && f <= 0.85 && ang > worstInner) worstInner = ang;
                    samples++;
                }
            }
            MaxNormalAngleInner = samples > 0 ? worstInner : -1;
            return samples > 0 ? worst : -1;
        }

        /// <summary>
        /// 曲率连续性近似（对标口径：相邻两排控制点的曲率差）：
        /// 最外两排控制点各自当折线算离散曲率（转角 / 段长），取两者差的最大值。
        /// 值越小 = 边界附近曲率过渡越顺（G2 越好）。
        /// </summary>
        internal static double CurvatureJumpOf(Point3d[,] cp, int nu, int nv)
        {
            if (cp == null || nu < 3 || nv < 3) return 0;
            double worst = 0;
            // 4 条边界各比「最外两排」：v 边界 → 沿 u 走的两排；u 边界 → 沿 v 走的两排
            worst = Math.Max(worst, RowJump(cp, nu, nv, true, 0, 1));
            worst = Math.Max(worst, RowJump(cp, nu, nv, true, nv - 1, nv - 2));
            worst = Math.Max(worst, RowJump(cp, nu, nv, false, 0, 1));
            worst = Math.Max(worst, RowJump(cp, nu, nv, false, nu - 1, nu - 2));
            return worst;
        }

        static double RowJump(Point3d[,] cp, int nu, int nv, bool alongU, int r0, int r1)
        {
            var k0 = DiscretCurv(cp, nu, nv, alongU, r0);
            var k1 = DiscretCurv(cp, nu, nv, alongU, r1);
            if (k0 == null || k1 == null) return 0;
            double worst = 0;
            int cnt = Math.Min(k0.Length, k1.Length);
            for (int i = 0; i < cnt; i++)
            {
                double d = Math.Abs(k0[i] - k1[i]);
                if (d > worst) worst = d;
            }
            return worst;
        }

        /// <summary>某排控制点（沿 u 或沿 v）的离散曲率：κ_i = 转角 / 平均段长</summary>
        static double[] DiscretCurv(Point3d[,] cp, int nu, int nv, bool alongU, int row)
        {
            int cnt = alongU ? nu : nv;
            if (cnt < 3) return null;
            var k = new double[cnt];
            for (int i = 1; i < cnt - 1; i++)
            {
                Point3d a = alongU ? cp[i - 1, row] : cp[row, i - 1];
                Point3d b = alongU ? cp[i, row] : cp[row, i];
                Point3d c = alongU ? cp[i + 1, row] : cp[row, i + 1];
                Vector3d u = b - a, v = c - b;
                double lu = u.Length, lv = v.Length;
                if (lu < 1e-12 || lv < 1e-12) { k[i] = 0; continue; }
                u.Unitize(); v.Unitize();
                double dot = u * v;
                if (dot > 1) dot = 1; else if (dot < -1) dot = -1;
                double ang = Math.Acos(dot);
                k[i] = ang / ((lu + lv) * 0.5);
            }
            return k;
        }

        // ============================================================ 内孔修剪

        internal static Brep TrimHoles(Brep brep, NurbsSurface srf, List<LoopData> holes, double diag,
            out int kept, out string why)
        {
            kept = 0; why = "";
            if (brep == null || srf == null || holes == null || holes.Count == 0) return null;
            Brep cur = brep;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            double tol = Math.Max(diag * 1e-6, 1e-9);
            for (int h = 0; h < holes.Count; h++)
            {
                LoopData ld = holes[h];
                var pts = new List<Point3d>();
                bool ok = true;
                for (int i = 0; i < ld.P.Length; i++)
                {
                    double u, v;
                    if (!srf.ClosestPoint(ld.P[i], out u, out v)) { ok = false; break; }
                    pts.Add(srf.PointAt(du.ParameterAt(u), dv.ParameterAt(v)));
                }
                if (!ok || pts.Count < 3) { why = "内孔投影到结果面失败，已跳过（未修剪）"; continue; }
                pts.Add(pts[0]);
                Curve cc = null;
                try { cc = new PolylineCurve(pts); } catch { cc = null; }
                if (cc == null) continue;
                Brep split = null;
                try
                {
                    BrepFace face = cur.Faces.Count > 0 ? cur.Faces[0] : null;
                    if (face == null) continue;
                    split = face.Split(new Curve[] { cc }, tol);
                }
                catch { split = null; }
                if (split == null || split.Faces.Count == 0)
                {
                    if (string.IsNullOrEmpty(why)) why = "内孔修剪失败（Split 返回空），已保留未修剪的结果面";
                    continue;
                }
                int best = -1; double bestArea = -1;
                for (int f = 0; f < split.Faces.Count; f++)
                {
                    double a = 0;
                    try { a = split.Faces[f].ToBrep().GetArea(); } catch { a = 0; }
                    if (a > bestArea) { bestArea = a; best = f; }
                }
                if (best < 0) continue;
                Brep single = null;
                try { single = split.Faces[best].DuplicateFace(false); } catch { single = null; }
                if (single == null || single.Faces.Count == 0) continue;
                cur = single;
                kept++;
            }
            return kept > 0 ? cur : null;
        }

        // ============================================================ 报告 / 诊断

        static string BuildReport(PatchFillResult r, Ctx ctx, List<PatchFillEdge> edges)
        {
            var sb = new StringBuilder();
            sb.AppendLine("输入：" + edges.Count + " 条边 · 4 角（" + r.CornerMethod + "）· 拐角节点 " + r.KinkKnots
                + " 个 · 控制网 " + r.ControlU + "×" + r.ControlV);
            if (ctx != null && ctx.Disk)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "参数化：径向盘状（无拐角闭合环）—— u 绕整环、v 向内、中心退化，没有人工角点 · 控制网 {0}×{1}",
                    r.ControlU, r.ControlV));
            }
            else if (ctx != null && ctx.P != null)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "4 角弧长参数：{0:0.###} / {1:0.###} / {2:0.###} / {3:0.###} · 4 边长 {4:0.###} / {5:0.###} / {6:0.###} / {7:0.###}",
                    ctx.P[0], ctx.P[1], ctx.P[2], ctx.P[3],
                    SideLength(ctx.Loop, ctx.P, 0), SideLength(ctx.Loop, ctx.P, 1),
                    SideLength(ctx.Loop, ctx.P, 2), SideLength(ctx.Loop, ctx.P, 3)));
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "偏差：最大 {0:0.####} · 平均 {1:0.####}（{2}）",
                r.MaxDeviation, r.RmsDeviation, r.HasReference ? "到相邻曲面" : "无参考几何，只统计边界"));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "逐边缝隙：v=0 {0:0.######} · u=1 {1:0.######} · v=1 {2:0.######} · u=0 {3:0.######}（最大 {4:0.######}）",
                r.EdgeGaps[0], r.EdgeGaps[1], r.EdgeGaps[2], r.EdgeGaps[3], r.BoundaryDeviation));
            if (r.MaxNormalAngleDeg >= 0)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "G1：与相邻面法向最大夹角 {0:0.####}°（{1} 个采样点；不含 4 个参数角点 {2:0.####}°）",
                    r.MaxNormalAngleDeg, r.G1Samples, r.MaxNormalAngleInnerDeg));
            else
                sb.AppendLine("G1：未启用（G0 位置连续）；" + (ctx != null && !ctx.Ref.Any
                    ? "边界是独立曲线、没有相邻面 → 就算选 G1 也只能退化成 G0"
                    : "需要选 G1 连续性"));
            if (r.MaxSecondDiffAngleDeg >= 0)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "G2：边界二阶差分（法曲率方向）与相邻面最大夹角 {0:0.####}°（{1} 个采样点；平坦带不计入）",
                    r.MaxSecondDiffAngleDeg, r.G2Samples));
            sb.AppendLine("残差迭代：" + r.Rounds + " 轮 · 局部插结 " + r.AdaptiveKnots + " 个 · 折叠单元 " + r.Folded
                + (r.Trimmed ? (" · 内孔修剪 " + r.Holes + "/" + r.HolesFound) : (r.HolesFound > 0 ? " · 检出内孔 " + r.HolesFound + "（未修剪）" : "")));
            for (int i = 0; i < r.RoundLog.Count; i++) sb.AppendLine("  " + r.RoundLog[i]);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "耗时：{0:0.000}s", r.Seconds));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "曲率连续性（最外两排控制点离散曲率最大差）：{0:0.######}", r.CurvatureJump));
            if (!string.IsNullOrEmpty(r.WarnText)) sb.AppendLine("告警：" + r.WarnText);
            if (!string.IsNullOrEmpty(r.Note)) sb.AppendLine("备注：" + r.Note);
            if (!string.IsNullOrEmpty(r.Error)) sb.AppendLine("错误：" + r.Error);
            return sb.ToString();
        }

        internal static string WarnText(int flags)
        {
            if (flags == 0) return "";
            var list = new List<string>();
            if ((flags & WarnEdgeGap) != 0) list.Add("边界缝隙超差(bit0)");
            if ((flags & WarnTangent) != 0) list.Add("切线不一致(bit1)");
            if ((flags & WarnNotConverged) != 0) list.Add("残差迭代未收敛(bit2)");
            if ((flags & WarnDegenerate) != 0) list.Add("结果退化/折叠(bit3)");
            if ((flags & WarnCurvature) != 0) list.Add("曲率不一致(bit4)");
            return string.Join(" · ", list.ToArray());
        }

        /// <summary>诊断（Probe 命令用）：环结构 / 4 角 / 相邻面 / 拐角</summary>
        internal static string Diagnose(List<PatchFillEdge> edges, List<GeometryBase> inner, PatchFillSettings s)
        {
            var sb = new StringBuilder();
            if (edges == null || edges.Count == 0) { sb.AppendLine("没有边界边"); return sb.ToString(); }
            var bb = BoundingBox.Empty;
            for (int i = 0; i < edges.Count; i++)
            {
                try { bb.Union(edges[i].Curve.GetBoundingBox(true)); } catch { }
                double len = 0;
                try { len = edges[i].Curve.GetLength(); } catch { }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  边 {0}：{1} · 长 {2:0.####} · 相邻面 {3}",
                    i + 1, edges[i].Source, len,
                    edges[i].AdjacentFaces == null ? "无" : edges[i].AdjacentFaces.Length.ToString()));
            }
            double diag = bb.IsValid ? bb.Diagonal.Length : 1.0;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "包围盒对角线：{0:0.####}", diag));
            List<LoopData> loops; List<int> counts; string why;
            if (!ChainLoops(edges, diag, out loops, out counts, out why))
            {
                sb.AppendLine("链化失败：" + why);
                return sb.ToString();
            }
            sb.AppendLine("环数：" + loops.Count);
            for (int i = 0; i < loops.Count; i++)
            {
                LoopData ld = loops[i];
                Plane fit = FitLoopPlane(ld, Plane.WorldXY);
                Plane frame = PrincipalFrame(ld, fit);
                int[] ci; bool fb; string method;
                Corners(ld, frame, out ci, out fb, out method);
                var kinks = KinkParams(ld, 15.0, 10);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  环 {0}：{1} 条边 · 周长 {2:0.####} · 面积 {3:0.####} · 4 角方式 {4}{5} · 拐角 {6} 个",
                    i + 1, counts[i], ld.Length, Math.Abs(LoopArea2d(ld, fit)), method,
                    fb ? "（兜底）" : "", kinks.Count));
                if (ci != null)
                {
                    var cp = CornerParams(ld, ci);
                    for (int k = 0; k < 4; k++)
                    {
                        Point3d p = PointAtArc(ld, cp[k]);
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "      角{0}: {1:0.###},{2:0.###},{3:0.###}", k, p.X, p.Y, p.Z));
                    }
                }
            }
            var r = Reference.FromEdges(edges);
            sb.AppendLine("参考几何（相邻面未修剪底层曲面）：" + (r.Any ? r.Faces.Count + " 张" : "无（独立曲线，G1 会退化成 G0）"));
            if (inner != null && inner.Count > 0) sb.AppendLine("内部约束：" + inner.Count + " 个");
            return sb.ToString();
        }

        internal static void AddToDocument(RhinoDoc doc, List<PatchFillResult> results, out int count)
        {
            count = 0;
            if (doc == null || results == null) return;
            int layer = EnsureLayer(doc, LayerPatch, Color.FromArgb(194, 96, 58));
            for (int i = 0; i < results.Count; i++)
            {
                PatchFillResult r = results[i];
                if (r == null || r.Result == null || r.Result.Faces.Count == 0) continue;
                var attrs = new ObjectAttributes();
                attrs.LayerIndex = layer;
                attrs.Name = string.Format(CultureInfo.InvariantCulture, "多边补面（偏差 {0:0.####}）", r.MaxDeviation);
                if (doc.Objects.AddBrep(r.Result, attrs) != Guid.Empty) count++;
            }
        }

        internal static int EnsureLayer(RhinoDoc doc, string name, Color color)
        {
            int idx = doc.Layers.FindByFullPath(name, -1);
            if (idx >= 0) return idx;
            var layer = new Layer { Name = name, Color = color };
            return doc.Layers.Add(layer);
        }

        internal static double Clamp01(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }
    }
}
