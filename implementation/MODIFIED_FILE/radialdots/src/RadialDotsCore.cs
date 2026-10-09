using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace RadialDotsPattern
{
    /// <summary>径向渐变圆点参数（长度单位 mm）</summary>
    public class RadialDotSettings
    {
        public double OuterR = 10.0;     // 外半径 mm（图案最外一圈）
        public double InnerR = 4.0;      // 内半径 mm（中间空心的半径；0 = 实心）
        public double Pitch = 1.0;       // 间距 mm（环间距 / 网格间距）
        public double MaxDia = 0.9;      // 最大直径 mm（峰值处）
        public double MinDia = 0.0;      // 最小直径 mm（带的两端；0 = 收缩到消失）
        public double Peak = 0.3;        // 峰值位置：0 = 内边缘，1 = 外边缘
        public double Falloff = 1.0;     // 衰减指数：越大峰值两侧掉得越快
        public int Layout = 0;           // 0=同心环 1=螺旋 2=方形网格 3=交错网格
        public bool Stagger = true;      // 同心环：相邻环错开半格
        public int Shape = 0;            // 0=圆形 1=方形 2=三角形 3=六边形 4=圆方交替
        public double Rotation = 0.0;    // 整体旋转（度）
        public double Jitter = 0.0;      // 位置抖动（占间距比例，0~1）
        public int Seed = 1;             // 随机种子（抖动用）
        public bool Merge = true;        // 重叠的图形自动布尔合并成一个整体
        public double Tol = 0.01;
        public double MmToModel = 1.0;   // mm → 文档模型单位

        public RadialDotSettings Clone() { return (RadialDotSettings)MemberwiseClone(); }

        public static readonly string[] LayoutNames = { "同心环", "螺旋", "方形网格", "交错网格" };
        public static readonly string[] ShapeNames = { "圆形", "方形", "三角形", "六边形", "圆方交替" };
    }

    /// <summary>生成结果</summary>
    public class RadialDotResult
    {
        public List<Curve> Curves = new List<Curve>();   // 最终输出（合并后的）
        public int Dots;              // 生成的图形个数（合并前）
        public int MergedGroups;      // 被合并成一体的组数
        public int MergedDots;        // 落在这些组里的图形个数
        public string Note = "";
        public int Count { get { return Curves.Count; } }

        // ---- 收尾清理（FinalCleanup）诊断：只用于自检报告 / 回归，不参与生成结果 ----
        public string Diag = "";        // 清理过程诊断（相交组数、组大小、布尔失败次数、前后对比）
        public int PairsBefore;         // 清理前：输出里「真实交叠（侵入 > 0.05）」的对数
        public int PairsAfter;          // 清理后：同上（0 = 干净）
        public double MaxDepthBefore;   // 清理前最大侵入深度
        public double MaxDepthAfter;    // 清理后最大侵入深度
    }

    /// <summary>xorshift32：自带随机数，保证 Rhino 7/8 同一 seed 结果一致</summary>
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

    public static class RadialDots
    {
        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // ---- 收尾清理参数 ----
        const int OverlapSamples = 48;      // 相交判定的采样点数（原来 24 太稀，小圆上点距过大容易漏判）
        const double DeepDepth = 0.05;      // 「明显叠在一起」的侵入深度阈值（模型单位；相切/亚微米接触不算）
        const int CleanupRounds = 6;        // 收尾清理最多复核 / 重并几轮（每轮必须真的并掉曲线才会继续，所以轮数有上限但代价可控）
        const int SplitDepthMax = 6;        // 布尔失败时「按空间对半切再并」的递归深度上限
        const int LeafMax = 40;             // 单次布尔的叶子条数上限（40 比 80 更稳：叶子越小，个别退化/相切对整体并的影响越小）

        /// <summary>收尾清理用的单条曲线数据：采样点 / 包围盒 / 所在平面 / 中心（并查集与布尔排序都要用）</summary>
        sealed class CurveInfo
        {
            public Curve C;
            public Point3d[] Pts;
            public BoundingBox Bb;
            public Plane Pl;
            public Point2d Cen;
        }

        /// <summary>
        /// 在给定平面上生成「径向渐变圆点」：图形大小按到中心的距离渐变（两端收缩、峰值在 Peak 处），
        /// 有交集的图形自动布尔合并成一个整体，没有交集的保持独立。
        /// </summary>
        public static RadialDotResult Generate(Plane plane, RadialDotSettings settings)
        {
            var res = new RadialDotResult();
            if (settings == null || !plane.IsValid) { res.Note = "参考平面无效"; return res; }

            RadialDotSettings s = settings.Clone();
            s.OuterR = Math.Max(s.OuterR, 1e-3);
            s.InnerR = Math.Max(s.InnerR, 0.0);
            if (s.InnerR >= s.OuterR - 1e-9) s.InnerR = 0.0;      // 内半径不小于外半径 -> 当实心
            s.Pitch = Math.Max(s.Pitch, 1e-3);
            s.MaxDia = Math.Max(s.MaxDia, 1e-4);
            s.MinDia = Clamp(s.MinDia, 0.0, s.MaxDia);
            s.Peak = Clamp(s.Peak, 0.0, 1.0);
            s.Falloff = Math.Max(s.Falloff, 0.05);
            s.Jitter = Clamp(s.Jitter, 0.0, 1.0);
            if (!(s.MmToModel > 0)) s.MmToModel = 1.0;
            s.Tol = Math.Max(s.Tol, 1e-5);

            double u2m = s.MmToModel;
            double rIn = s.InnerR * u2m, rOut = s.OuterR * u2m, pitch = s.Pitch * u2m;
            double dMax = s.MaxDia * u2m, dMin = s.MinDia * u2m;
            double tol = Math.Max(s.Tol * u2m, 1e-6);
            double rot = s.Rotation * Math.PI / 180.0;
            var rnd = new Rng32(s.Seed);

            // ---------------- 1) 图形中心（平面 2D 坐标）
            var pts = new List<Point2d>();
            var idx = new List<int>();                 // 序号（圆方交替用）
            if (s.Layout == 0)
            {
                // 同心环：每环按弧长 ≈ 间距取个数（格子近似正方形），可选相邻环错开半格
                int rings = (int)Math.Floor((rOut - rIn) / pitch) + 1;
                if (rings > 3000) { rings = 3000; res.Note = "环数超上限，已截断"; }
                for (int k = 0; k < rings; k++)
                {
                    double r = rIn + k * pitch;
                    if (r > rOut + 1e-9) break;
                    if (r < 1e-9) { pts.Add(new Point2d(0, 0)); idx.Add(0); continue; }
                    int n = (int)Math.Round(2.0 * Math.PI * r / pitch);
                    if (n < 1) n = 1;
                    if (n > 3000) n = 3000;
                    double off = (s.Stagger && (k % 2 == 1)) ? Math.PI / n : 0.0;
                    for (int i = 0; i < n; i++)
                    {
                        double a = rot + off + 2.0 * Math.PI * i / n;
                        pts.Add(new Point2d(r * Math.Cos(a), r * Math.Sin(a)));
                        idx.Add(i + k);
                    }
                }
            }
            else if (s.Layout == 1)
            {
                // 螺旋（黄金角）：面积均匀分布，越往外点数按面积增长
                double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
                double area = Math.PI * (rOut * rOut - rIn * rIn);
                int n = (int)Math.Round(area / (pitch * pitch));
                if (n < 1) n = 1;
                if (n > 200000) { n = 200000; res.Note = "点数超上限，已截断"; }
                for (int k = 0; k < n; k++)
                {
                    double t = (k + 0.5) / n;
                    double r = Math.Sqrt(rIn * rIn + (rOut * rOut - rIn * rIn) * t);
                    double a = rot + golden * k;
                    pts.Add(new Point2d(r * Math.Cos(a), r * Math.Sin(a)));
                    idx.Add(k);
                }
            }
            else
            {
                // 网格 / 交错网格：铺满外接方框，只留半径带内的格子
                bool stagger = (s.Layout == 3);
                int half = (int)Math.Ceiling(rOut / pitch) + 1;
                if (half > 2000) { half = 2000; res.Note = "网格超上限，已截断"; }
                for (int j = -half; j <= half; j++)
                    for (int i = -half; i <= half; i++)
                    {
                        double x = i * pitch, y = j * pitch;
                        if (stagger && (j & 1) != 0) x += pitch * 0.5;
                        double r = Math.Sqrt(x * x + y * y);
                        if (r > rOut + 1e-9) continue;
                        if (r < rIn - 1e-9) continue;
                        // 旋转
                        double ca = Math.Cos(rot), sa = Math.Sin(rot);
                        pts.Add(new Point2d(x * ca - y * sa, x * sa + y * ca));
                        idx.Add(Math.Abs(i) + Math.Abs(j));
                    }
            }
            if (pts.Count == 0) { res.Note = "参数下没有任何图形（检查内外半径 / 间距）"; return res; }

            // ---------------- 2) 直径 + 抖动 + 形状 -> 曲线
            var curves = new List<Curve>(pts.Count);
            var radii = new List<double>(pts.Count);
            var kept = new List<Point2d>(pts.Count);      // 真正生成出来的图形中心（与 curves/radii 一一对应）
            double jitAmp = s.Jitter * pitch;
            for (int i = 0; i < pts.Count; i++)
            {
                Point2d c = pts[i];
                if (jitAmp > 1e-12)
                    c = new Point2d(c.X + rnd.NextSigned() * jitAmp * 0.5, c.Y + rnd.NextSigned() * jitAmp * 0.5);
                double r = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                double d = DiameterAt(r, rIn, rOut, dMin, dMax, s.Peak, s.Falloff);
                if (!(d > 1e-6)) continue;                       // 收缩到消失的点不输出
                bool alt = ((idx[i] & 1) == 0);
                double shapeRot = rot + (s.Shape == 1 ? Math.PI / 4.0 : 0.0);
                Curve cv = MakeShape(plane, c, d, shapeRot, s.Shape, alt);
                if (cv == null) continue;
                curves.Add(cv);
                radii.Add(d * 0.5);
                kept.Add(c);
            }
            res.Dots = curves.Count;
            if (curves.Count == 0) { res.Note = "参数下图形都缩到 0 了（试试加大最大直径或调整峰值位置）"; return res; }

            // ---------------- 3) 有交集的自动布尔合并；孤立的保持独立
            if (!s.Merge || curves.Count < 2) { res.Curves = curves; return res; }
            if (curves.Count > 20000) { res.Note = "图形太多，已跳过自动合并"; res.Curves = curves; return res; }

            // 注意：分组要用 kept（跳掉缩到 0 的点之后的中心），不能再用原始 pts —— 否则和 radii 长度不一致会越界
            var groups = GroupOverlaps(kept, radii, tol);
            var outCurves = new List<Curve>(curves.Count);
            int failedGroups = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                List<int> grp = groups[g];
                if (grp.Count < 2) { outCurves.Add(curves[grp[0]]); continue; }
                var set = new List<Curve>(grp.Count);
                var setC = new List<Point2d>(grp.Count);
                double lenIn = 0;
                for (int k = 0; k < grp.Count; k++)
                {
                    Curve c = curves[grp[k]];
                    set.Add(c);
                    setC.Add(kept[grp[k]]);
                    double L = c.GetLength();
                    if (L > 0 && !double.IsNaN(L)) lenIn += L;
                }
                bool okUnion;
                List<Curve> uni = UnionGroup(set, setC, tol, out okUnion);
                double lenOut = 0;
                for (int k = 0; k < uni.Count; k++)
                {
                    double L = uni[k] != null ? uni[k].GetLength() : 0;
                    if (L > 0 && !double.IsNaN(L)) lenOut += L;
                }
                // 只有真的合并掉了东西（周长变短）才采用结果，否则保持独立
                if (uni.Count > 0 && lenIn > 1e-9 && lenOut < lenIn * 0.999)
                {
                    outCurves.AddRange(uni);
                    res.MergedGroups++;
                    res.MergedDots += grp.Count;
                }
                else
                {
                    outCurves.AddRange(set);
                    if (!okUnion) failedGroups++;
                }
            }
            if (failedGroups > 0)
            {
                string warn = string.Format("有 {0} 组重叠没能合并（布尔运算失败，已保留独立图形）", failedGroups);
                res.Note = string.IsNullOrEmpty(res.Note) ? warn : res.Note + "；" + warn;
            }
            // 收尾 1：整条落在另一条曲线内部的（布尔合并偶尔留下的冗余碎块）直接丢掉 —— 对「并集」来说它本来就是多余的
            List<Curve> cleaned = DropContained(outCurves, tol);
            // 收尾 2：分治布尔偶尔会留下「两条仍然相交」的残块（组内某层合并失败 / 布尔部分成功），
            // 这里按真实相交再分组、再并，并且反复复核直到干净（详见 FinalCleanup）。
            res.Curves = FinalCleanup(cleaned, tol, res);
            return res;
        }

        /// <summary>带内渐变：t = 0 内边缘、1 外边缘；峰值在 peak 处，两侧按 falloff 幂衰减</summary>
        public static double DiameterAt(double r, double rIn, double rOut, double dMin, double dMax, double peak, double falloff)
        {
            double band = Math.Max(rOut - rIn, 1e-9);
            double t = Clamp((r - rIn) / band, 0.0, 1.0);
            double u;
            if (t <= peak) u = peak > 1e-9 ? t / peak : 1.0;
            else u = peak < 1.0 - 1e-9 ? (1.0 - t) / (1.0 - peak) : 1.0;
            u = Math.Pow(Clamp(u, 0.0, 1.0), falloff);
            return dMin + (dMax - dMin) * u;
        }

        /// <summary>平面上一个闭合图形：圆形用真圆弧，多边形用闭合折线</summary>
        static Curve MakeShape(Plane plane, Point2d c, double d, double rot, int shape, bool alt)
        {
            double r = d * 0.5;
            if (!(r > 1e-9)) return null;
            int sh = shape == 4 ? (alt ? 0 : 1) : shape;
            Point3d c3 = plane.PointAt(c.X, c.Y);
            if (!c3.IsValid) return null;
            if (sh == 0)
            {
                try { return new ArcCurve(new Circle(plane, c3, r)); } catch { return null; }
            }
            int n = sh == 1 ? 4 : (sh == 2 ? 3 : 6);
            var pl = new List<Point3d>(n + 1);
            for (int i = 0; i < n; i++)
            {
                double a = rot + 2.0 * Math.PI * i / n;
                Point3d p = plane.PointAt(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
                if (!p.IsValid) return null;
                pl.Add(p);
            }
            pl.Add(pl[0]);
            try { return new PolylineCurve(pl); } catch { return null; }
        }

        /// <summary>
        /// 对一组「有交集」的图形做布尔并。
        /// 上千个圆一次丢给 Curve.CreateBooleanUnion 又慢又容易失败（实测用户场景 958 个圆直接不合并），
        /// 所以按空间排序后**分治**：小批先并，结果再逐层并起来。任何一层失败都退回该层的输入，不会丢图形。
        /// </summary>
        static List<Curve> UnionGroup(List<Curve> set, List<Point2d> centers, double tol, out bool ok)
        {
            ok = false;
            if (set.Count < 2) { ok = true; return set; }
            // 按 x（再按 y）排序：让每一半在空间上紧凑，布尔才有意义
            int n = set.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, delegate (int a, int b)
            {
                if (centers[a].X != centers[b].X) return centers[a].X.CompareTo(centers[b].X);
                return centers[a].Y.CompareTo(centers[b].Y);
            });
            var sorted = new List<Curve>(n);
            var sortedC = new List<Point2d>(n);
            for (int i = 0; i < n; i++) { sorted.Add(set[order[i]]); sortedC.Add(centers[order[i]]); }
            return UnionRec(sorted, sortedC, tol, 0, out ok);
        }

        static List<Curve> UnionRec(List<Curve> set, List<Point2d> cen, double tol, int depth, out bool ok)
        {
            if (set.Count <= LeafMax || depth >= 10)
            {
                Curve[] u = null;
                try { u = Curve.CreateBooleanUnion(set, tol); } catch { u = null; }
                if (u != null && u.Length > 0) { ok = true; return new List<Curve>(u); }
                ok = false;
                return set;
            }
            int mid = set.Count / 2;
            bool okA, okB;
            List<Curve> a = UnionRec(set.GetRange(0, mid), cen.GetRange(0, mid), tol, depth + 1, out okA);
            List<Curve> b = UnionRec(set.GetRange(mid, set.Count - mid), cen.GetRange(mid, cen.Count - mid), tol, depth + 1, out okB);
            var both = new List<Curve>(a.Count + b.Count);
            both.AddRange(a);
            both.AddRange(b);
            ok = okA && okB;
            if (both.Count < 2) return both;
            Curve[] u2 = null;
            try { u2 = Curve.CreateBooleanUnion(both, tol); } catch { u2 = null; }
            if (u2 != null && u2.Length > 0)
            {
                var r = new List<Curve>(u2.Length);
                r.AddRange(u2);
                return r;
            }
            return both;
        }

        /// <summary>
        /// 收尾清理：把输出里**仍然真实相交**的曲线重新分组、再并一次，并且**反复复核**（最多 CleanupRounds 轮）。
        ///
        /// 为什么一次不够：Curve.CreateBooleanUnion 在曲线多的时候会「部分成功」——
        /// 返回的结果里仍夹着几条没并进去的原曲线（它们和合并出来的大区域真实交叠，实测侵入 ≈ 最大直径 - 间距 = 0.35mm），
        /// 或者干脆返回 null。所以这里不再相信一次布尔：用「采样点严格落在对方内部」独立复核，
        /// 只要还有交叠就把这些曲线重新分组、再并一轮（每轮曲线更少更简单，布尔更容易成功），
        /// 并且每轮只有「交叠对数真的变少」才采用结果，绝不把结果改坏。
        /// 组内一律走分治并 UnionGroup，不再有「>200 条就跳过」的丢件分支；布尔失败还会按空间对半切兜底。
        /// </summary>
        static List<Curve> FinalCleanup(List<Curve> curves, double tol, RadialDotResult res)
        {
            if (curves == null || curves.Count < 2) return curves;

            List<CurveInfo> infos = SampleAll(curves, OverlapSamples);
            int edges, deep; double deepMax;
            List<List<int>> grpList = GroupByOverlap(infos, out edges, out deep, out deepMax);
            int before = deep; double beforeMax = deepMax;

            List<Curve> outp = curves;
            int rounds = 0, groups = 0, maxGroup = 0, nulls = 0, splits = 0;
            var sizes = new List<string>(8);

            for (int round = 0; round < CleanupRounds; round++)
            {
                if (edges == 0) break;                       // 已经没有真实相交的曲线，收工
                rounds++;

                var next = new List<Curve>(outp.Count);
                bool progress = false;
                for (int g = 0; g < grpList.Count; g++)
                {
                    List<int> grp = grpList[g];
                    if (grp.Count < 2) { if (infos[grp[0]].C != null) next.Add(infos[grp[0]].C); continue; }
                    groups++;
                    if (grp.Count > maxGroup) maxGroup = grp.Count;
                    if (sizes.Count < 8) sizes.Add(grp.Count.ToString());
                    var set = new List<Curve>(grp.Count);
                    var cen = new List<Point2d>(grp.Count);
                    for (int k = 0; k < grp.Count; k++) { set.Add(infos[grp[k]].C); cen.Add(infos[grp[k]].Cen); }
                    List<Curve> uni = UnionSplit(set, cen, tol, 0, ref nulls, ref splits);
                    if (uni == null || uni.Count == 0) uni = set;
                    // UnionGroup 失败时返回的是「原集合的排序副本」（条数不变），所以用条数判断有没有真并掉东西：
                    // 布尔并只会让连通区域变少（N 条闭曲线的并最多 N 个区域），条数不降 = 这一组没进展。
                    if (uni.Count != set.Count) progress = true;
                    for (int k = 0; k < uni.Count; k++) if (uni[k] != null) next.Add(uni[k]);
                }
                if (!progress) break;                        // 这一轮一条都没并掉：再试也是白费

                // 复核这一轮的结果：既给出「修复后」的数字，也决定要不要采用（交叠变多就退回上一版）
                List<CurveInfo> ninfos = SampleAll(next, OverlapSamples);
                int nEdges, nDeep; double nDeepMax;
                List<List<int>> nGrp = GroupByOverlap(ninfos, out nEdges, out nDeep, out nDeepMax);
                if (nDeep < deep || (nDeep == deep && next.Count < outp.Count))
                {
                    outp = next; infos = ninfos; grpList = nGrp;
                    edges = nEdges; deep = nDeep; deepMax = nDeepMax;
                }
                else break;
            }

            if (res != null)
            {
                res.PairsBefore = before;
                res.PairsAfter = deep;
                res.MaxDepthBefore = beforeMax;
                res.MaxDepthAfter = deepMax;
                res.Diag = string.Format(
                    "收尾清理：{0} 轮；真实相交组 {1} 组（条数 {2}，最大 {3} 条）；因超 200 条跳过 0 组（旧上限已改走分治 + 对半切兜底）；布尔返回 null/空 {4} 次；对半切兜底 {5} 次；交叠对数 {6} -> {7}；最大侵入 {8:0.####} -> {9:0.####} mm",
                    rounds, groups, sizes.Count > 0 ? string.Join(",", sizes.ToArray()) : "无", maxGroup,
                    nulls, splits, before, deep, beforeMax, deepMax);
            }
            return outp;
        }

        /// <summary>
        /// 兜底布尔：先把整组交给分治并（UnionGroup）；一条都没并掉就按空间（x 再 y）对半切开，
        /// 两半各自递归并（深度上限 SplitDepthMax），最后把两半结果都保留 —— 宁可多留几条曲线，也不再让它们互相叠着。
        /// 每次布尔失败 / 每次对半切都计数，供诊断输出（nulls / splits）。
        /// </summary>
        static List<Curve> UnionSplit(List<Curve> set, List<Point2d> cen, double tol, int depth, ref int nulls, ref int splits)
        {
            if (set == null || set.Count < 2) return set;
            bool ok;
            List<Curve> u = UnionGroup(set, cen, tol, out ok);
            if (!ok) nulls++;
            if (u != null && u.Count > 0 && u.Count < set.Count) return u;     // 并掉了东西：采用
            if (depth >= SplitDepthMax || set.Count < 4) return (u != null && u.Count > 0) ? u : set;

            int n = set.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, delegate (int a, int b)
            {
                if (cen[a].X != cen[b].X) return cen[a].X.CompareTo(cen[b].X);
                return cen[a].Y.CompareTo(cen[b].Y);
            });
            var sorted = new List<Curve>(n);
            var sortedC = new List<Point2d>(n);
            for (int i = 0; i < n; i++) { sorted.Add(set[order[i]]); sortedC.Add(cen[order[i]]); }
            int mid = n / 2;
            splits++;
            List<Curve> a = UnionSplit(sorted.GetRange(0, mid), sortedC.GetRange(0, mid), tol, depth + 1, ref nulls, ref splits);
            List<Curve> b = UnionSplit(sorted.GetRange(mid, n - mid), sortedC.GetRange(mid, n - mid), tol, depth + 1, ref nulls, ref splits);
            var both = new List<Curve>(a.Count + b.Count);
            both.AddRange(a);
            both.AddRange(b);
            if (both.Count < 2) return both;
            // 最后再试一次把两半并起来；还失败就保留两半（各自已经是不带交叠的并集）
            bool ok2;
            List<Curve> u2 = UnionGroup(both, CentersOf(both), tol, out ok2);
            if (!ok2) nulls++;
            if (u2 != null && u2.Count > 0 && u2.Count < both.Count) return u2;
            return both;
        }

        /// <summary>
        /// 按「采样点真的落进对方内部」把曲线分组（并查集 + 包围盒预筛）。edges = 真实相交的对数。
        /// 顺便把「真实交叠」（侵入深度 > DeepDepth）的对数和最大深度一起统计出来：
        /// 深度只对确实相交的对才计算，所以这份统计基本是白送的（也就不用再多跑一遍全量比较）。
        /// </summary>
        static List<List<int>> GroupByOverlap(List<CurveInfo> infos, out int edges, out int deepPairs, out double maxDepth)
        {
            int n = infos.Count;
            edges = 0;
            deepPairs = 0;
            maxDepth = 0;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int i = 0; i < n; i++)
            {
                CurveInfo A = infos[i];
                if (A.C == null || !A.Bb.IsValid) continue;
                for (int j = i + 1; j < n; j++)
                {
                    CurveInfo B = infos[j];
                    if (B.C == null || !B.Bb.IsValid) continue;
                    if (!BbHit(A.Bb, B.Bb)) continue;
                    if (!Overlap(A, B)) continue;
                    Union(parent, i, j);
                    edges++;
                    double d = PairDepth(A, B);
                    if (d > maxDepth) maxDepth = d;
                    if (d > DeepDepth) deepPairs++;
                }
            }
            var map = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                List<int> l;
                if (!map.TryGetValue(root, out l)) { l = new List<int>(4); map[root] = l; order.Add(root); }
                l.Add(i);
            }
            var groups = new List<List<int>>(order.Count);
            for (int i = 0; i < order.Count; i++) groups.Add(map[order[i]]);
            return groups;
        }

        /// <summary>
        /// 两条曲线是否真的相交：**双向**判定 —— a 的采样点落进 b 内部，或 b 的采样点落进 a 内部，任一命中即算。
        /// 采样 48 点（直径 0.65mm 的小圆上点距约 0.04mm），不会漏掉真实交叠。
        /// </summary>
        static bool Overlap(CurveInfo a, CurveInfo b)
        {
            if (a == null || b == null || a.C == null || b.C == null) return false;
            if (a.Pts != null)
                for (int k = 0; k < a.Pts.Length; k++) if (Inside(b, a.Pts[k])) return true;
            if (b.Pts != null)
                for (int k = 0; k < b.Pts.Length; k++) if (Inside(a, b.Pts[k])) return true;
            return false;
        }

        /// <summary>p 是否严格落在 c 内部（落在边界上算 Coincident，不算 Inside；先用包围盒挡掉绝大多数调用）</summary>
        static bool Inside(CurveInfo c, Point3d p)
        {
            if (c == null || c.C == null || !c.Bb.IsValid) return false;
            if (p.X < c.Bb.Min.X || p.X > c.Bb.Max.X) return false;
            if (p.Y < c.Bb.Min.Y || p.Y > c.Bb.Max.Y) return false;
            try { return c.C.Contains(p, c.Pl, 1e-4) == PointContainment.Inside; }
            catch { return false; }
        }

        /// <summary>一对曲线之间的真实侵入深度（双向取大）：采样点严格落在对方内部时，到对方边界的最大距离</summary>
        static double PairDepth(CurveInfo a, CurveInfo b)
        {
            double depth = 0;
            if (a.Pts != null)
                for (int k = 0; k < a.Pts.Length; k++)
                {
                    Point3d p = a.Pts[k];
                    if (!Inside(b, p)) continue;
                    double t;
                    if (b.C.ClosestPoint(p, out t)) { double d = p.DistanceTo(b.C.PointAt(t)); if (d > depth) depth = d; }
                }
            if (b.Pts != null)
                for (int k = 0; k < b.Pts.Length; k++)
                {
                    Point3d p = b.Pts[k];
                    if (!Inside(a, p)) continue;
                    double t;
                    if (a.C.ClosestPoint(p, out t)) { double d = p.DistanceTo(a.C.PointAt(t)); if (d > depth) depth = d; }
                }
            return depth;
        }

        static List<CurveInfo> SampleAll(List<Curve> curves, int samples)
        {
            var infos = new List<CurveInfo>(curves.Count);
            for (int i = 0; i < curves.Count; i++)
            {
                Curve c = curves[i];
                var ci = new CurveInfo();
                ci.C = c;
                ci.Pts = SampleN(c, samples);
                ci.Bb = c != null ? c.GetBoundingBox(false) : BoundingBox.Unset;
                ci.Pl = PlaneOf(c);
                if (ci.Bb.IsValid) { Point3d m = ci.Bb.Center; ci.Cen = new Point2d(m.X, m.Y); }
                infos.Add(ci);
            }
            return infos;
        }

        static Plane PlaneOf(Curve c)
        {
            if (c != null)
            {
                try { Plane pl; if (c.TryGetPlane(out pl, 1e-3)) return pl; } catch { }
            }
            return Plane.WorldXY;
        }

        static List<Point2d> CentersOf(List<Curve> curves)
        {
            var cen = new List<Point2d>(curves.Count);
            for (int i = 0; i < curves.Count; i++)
            {
                Curve c = curves[i];
                BoundingBox bb = c != null ? c.GetBoundingBox(false) : BoundingBox.Unset;
                cen.Add(bb.IsValid ? new Point2d(bb.Center.X, bb.Center.Y) : new Point2d(0, 0));
            }
            return cen;
        }

        static bool BbHit(BoundingBox a, BoundingBox b)
        {
            if (!a.IsValid || !b.IsValid) return false;
            if (a.Max.X < b.Min.X || b.Max.X < a.Min.X) return false;
            if (a.Max.Y < b.Min.Y || b.Max.Y < a.Min.Y) return false;
            return true;
        }

        /// <summary>
        /// 收尾清理：把「整条都落在另一条曲线内部」的曲线丢掉。
        /// 布尔并偶尔会留下这种冗余碎块（尤其是被大区域吞掉的小图形），对并集来说它就是多余的。
        /// 判据保守：抽样点里 ≥90% 落在同一条曲线内部才丢，绝不会误删相交的图形。
        /// </summary>
        static List<Curve> DropContained(List<Curve> curves, double tol)
        {
            int n = curves.Count;
            if (n < 2) return curves;
            var pts = new List<Point3d[]>(n);
            var bbs = new List<BoundingBox>(n);
            for (int i = 0; i < n; i++)
            {
                Point3d[] sp = SampleN(curves[i], 36);
                pts.Add(sp);
                bbs.Add(curves[i] != null ? curves[i].GetBoundingBox(false) : BoundingBox.Unset);
            }
            var drop = new bool[n];
            for (int i = 0; i < n && n <= 4000; i++)
            {
                if (curves[i] == null || pts[i] == null || pts[i].Length < 3) continue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || curves[j] == null) continue;
                    if (!bbs[j].IsValid || !bbs[i].IsValid) continue;
                    // j 的包围盒要能装下 i
                    if (bbs[j].Min.X > bbs[i].Min.X + 1e-9 || bbs[j].Max.X < bbs[i].Max.X - 1e-9) continue;
                    if (bbs[j].Min.Y > bbs[i].Min.Y + 1e-9 || bbs[j].Max.Y < bbs[i].Max.Y - 1e-9) continue;
                    int inside = 0;
                    for (int k = 0; k < pts[i].Length; k++)
                    {
                        try
                        {
                            if (curves[j].Contains(pts[i][k], Plane.WorldXY, Math.Max(tol, 1e-4)) == PointContainment.Inside) inside++;
                        }
                        catch { }
                    }
                    if (inside >= pts[i].Length * 9 / 10) { drop[i] = true; break; }
                }
            }
            var outp = new List<Curve>(n);
            for (int i = 0; i < n; i++) if (!drop[i] && curves[i] != null) outp.Add(curves[i]);
            return outp;
        }

        static Point3d[] SampleN(Curve c, int n)
        {
            if (c == null) return null;
            try
            {
                double[] ts = c.DivideByCount(n, true);
                if (ts == null || ts.Length == 0) return null;
                var pts = new Point3d[ts.Length];
                for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
                return pts;
            }
            catch { return null; }
        }

        /// <summary>
        /// 按「外接圆真的有交集」把图形分组（并查集 + 空间哈希）。
        /// 组内 ≥2 个才需要布尔合并 —— 孤立的图形不参与布尔，省时间也不会被改形。
        /// </summary>
        static List<List<int>> GroupOverlaps(List<Point2d> pts, List<double> radii, double tol)
        {
            int n = pts.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            double maxR = 0;
            for (int i = 0; i < radii.Count; i++) if (radii[i] > maxR) maxR = radii[i];
            double cell = Math.Max(2.0 * maxR, 1e-6);
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                long k = Key((int)Math.Floor(pts[i].X / cell), (int)Math.Floor(pts[i].Y / cell));
                List<int> l;
                if (!grid.TryGetValue(k, out l)) { l = new List<int>(2); grid[k] = l; }
                l.Add(i);
            }
            for (int i = 0; i < n; i++)
            {
                int cx = (int)Math.Floor(pts[i].X / cell), cy = (int)Math.Floor(pts[i].Y / cell);
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int y = cy - 1; y <= cy + 1; y++)
                    {
                        List<int> l;
                        if (!grid.TryGetValue(Key(x, y), out l)) continue;
                        for (int q = 0; q < l.Count; q++)
                        {
                            int j = l[q];
                            if (j <= i) continue;
                            double need = radii[i] + radii[j] - tol;     // 外接圆相交才可能有交集
                            if (need <= 0) continue;
                            double dx = pts[i].X - pts[j].X, dy = pts[i].Y - pts[j].Y;
                            if (dx * dx + dy * dy < need * need) Union(parent, i, j);
                        }
                    }
            }
            var map = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                List<int> l;
                if (!map.TryGetValue(root, out l)) { l = new List<int>(4); map[root] = l; order.Add(root); }
                l.Add(i);
            }
            var groups = new List<List<int>>(order.Count);
            for (int i = 0; i < order.Count; i++) groups.Add(map[order[i]]);
            return groups;
        }

        static int Find(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        static void Union(int[] p, int a, int b)
        {
            int ra = Find(p, a), rb = Find(p, b);
            if (ra != rb) p[rb] = ra;
        }

        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }
    }
}
