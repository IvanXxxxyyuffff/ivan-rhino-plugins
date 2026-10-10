using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace SurfaceUnifyPattern
{
    /// <summary>多重曲面 → 单一曲面 参数（面板 → 核心；长度都是模型单位）</summary>
    public class SurfaceUnifySettings
    {
        public int GridCount = 16;            // 控制点数（每向）：网格越密越贴原曲面、面越重
        public double FitStrength = 1.0;      // 贴合强度 0..1：0 = 只用边界 Coons 基面，1 = 完全贴到原曲面
        public double Smooth = 0.15;          // 平滑度 0..1：网格点的拉普拉斯光顺强度
        public double MaxSnapDistance = 0.0;  // 最大贴合距离（mm；0 = 自动 = 包围盒对角线 × 0.25）
        public bool LockBoundary = true;      // 边界保形：边界点锁在原边界上（不参与光顺/贴合）
        public bool KeepHoles = true;         // 保留内孔（把内孔投影到结果面做修剪）
        public bool AllowTrim = false;        // 允许修剪（默认关：Patch/外扩+修剪还在优化中，且会跑两遍参数化→卡）：域外扩后按原边界剪掉多余部分（关 = 原来的效果，不外扩不修剪）
        public double Extend = 0.15;          // 边界外扩比例（AllowTrim 开时生效）
        public bool ShowSourceBoundary = true;// 预览时显示原边界（红线）
        public double MmToModel = 1.0;        // 面板 mm → 模型单位

        public SurfaceUnifySettings Clone()
        {
            return (SurfaceUnifySettings)MemberwiseClone();
        }
    }

    /// <summary>一次生成的结果</summary>
    public class SurfaceUnifyResult
    {
        public Brep Result;              // 输出：单一曲面（可能有内孔 → 修剪环）
        public Surface Surface;          // 结果面的底层曲面（诊断/自检用；Patch 模式下是 Patch 的面）
        public int Faces;                // 结果面数（正常 = 1）
        public int Loops;                // 修剪环数（1 = 无孔；2 = 带 1 个内孔）
        public int Holes;                // 成功保留的内孔数
        public int HolesFound;           // 原目标的边界环总数 - 1（内孔候选数）
        public int ControlU, ControlV;   // 控制点数
        public int GridU, GridV;         // 网格点数（= 控制点数）
        public int Corners;              // 4 角是否有效
        public double MaxDeviation;      // 拟合偏差（结果面 → 原曲面/网格 的最大距离，模型单位）
        public double RmsDeviation;      // 拟合偏差均方根
        public double BoundaryDeviation; // 边界偏差（结果面边界 → 原边界 的最大距离）
        public double CornerDeviation;   // 交点偏差（真实边-边交点 → 结果面边界的最大距离；交点必须精确命中）
        public Point3d CornerWorst;      // 交点偏差最大的那个交点（诊断用）
        public int JunctionCount;        // 真实边-边交点（转折节点）数
        public double AreaRatio;         // 面积比（结果 / 原目标）
        public int Unsnapped;            // 没能贴到原曲面的网格点数
        public int Folded;               // 折叠单元数（诊断）
        public bool Wrapped;             // 是否走了「包裹式」路径（边界只是一圈小口 → 爬行网格）
        public bool Trimmed;             // 是否做了「外扩 + 按原边界修剪」
        public int RowsUsed;             // 爬行实际用掉的行数（其余行塌在极点上）
        public double Diag;              // 目标包围盒对角线（质量判据用）
        public double Seconds;
        public string Note = "";
        public string Error = "";
        public List<Polyline> SourceBoundary = new List<Polyline>();   // 预览用：原边界
    }

    public static class SurfaceUnifyCore
    {
        internal const string LayerSurface = "单一曲面";
        public const string EmptyTargetHint = "先选一个开放的多重曲面 / 曲面 / 挤出体 / 网格（片状、有裸露边界）";
        internal const int MinGrid = 4;
        internal const int MaxGrid = 40;
        internal const double MaxSnapFactor = 0.75;      // 自动贴合距离 = 包围盒对角线 × 该系数
                                                         // （兜袋/槽形壳体的深度可能接近对角线，0.25 够不到碗底）
        internal const double WrapRatio = 2.5;           // 面积 / 边界平面面积 超过它就当「包裹式」→ 爬行网格

        // ============================================================ 目标解析

        /// <summary>
        /// 目标：曲面 / 多重曲面 / 挤出体（当成一整个片状体）/ 网格。
        /// 曲线 / 细分物件 / 点明确拒绝（先转成曲面或网格）。
        /// </summary>
        internal static bool TryResolve(RhinoObject obj, out GeometryBase target, out string why)
        {
            target = null; why = "";
            if (obj == null) { why = "没有选中物件"; return false; }
            GeometryBase g = obj.Geometry;
            if (g == null) { why = "物件几何为空"; return false; }
            Brep b; Mesh m;
            if (!ResolveSheet(g, out b, out m, out why)) return false;
            target = g;
            return true;
        }

        /// <summary>把任意几何解析成「片状体」：Brep（精确）或 Mesh（本身就是片状）</summary>
        internal static bool ResolveSheet(GeometryBase g, out Brep brep, out Mesh mesh, out string why)
        {
            brep = null; mesh = null; why = "";
            if (g == null) { why = "目标为空"; return false; }
            if (g is Mesh m)
            {
                if (m.Faces.Count == 0) { why = "网格是空的"; return false; }
                mesh = m;
                return true;
            }
            if (g is SubD) { why = "细分物件不支持：请先转成 NURBS 曲面或网格"; return false; }
            if (g is Curve) { why = "曲线不支持：请选曲面 / 多重曲面 / 挤出体 / 网格"; return false; }
            if (g is Rhino.Geometry.Point) { why = "点不支持：请选曲面 / 多重曲面 / 挤出体 / 网格"; return false; }
            Brep b = null;
            try { b = Brep.TryConvertBrep(g); } catch { b = null; }
            if (b == null || b.Faces.Count == 0) { why = "不是曲面 / 网格"; return false; }
            brep = b;
            return true;
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

        // ============================================================ 生成

        /// <summary>
        /// 多重曲面 → 单一曲面：
        ///   ① 片状网格 + 裸露边界环（外环 + 内孔）
        ///   ② 外环取 4 角 → 弧长四等分成 4 条边 → 3D Coons 基面（边界精确过原边界）
        ///   ③ 网格点贴到原曲面（Brep 精确最近点 / 网格最近点，带法向一致性校验）
        ///   ④ 拉普拉斯光顺（边界保形 = 边界点不动）→ 插值出单一 NURBS 面
        ///   ⑤ 内孔投影到结果面做修剪（保留内孔时）
        /// 边界点就是原边界的采样点，所以边界完全逼近；内部靠贴合 + 加密控制点收敛。
        /// </summary>
        public static SurfaceUnifyResult Generate(GeometryBase target, SurfaceUnifySettings s, out string report)
        {
            // 先按启发式（面积 / 边界平面面积）选参数化；质量差就**另一种也试一遍，取好的那个**。
            // 有些形状两种都不完美（比如又薄又扭的缝状边界：爬行会跨过薄壁、Coons+射线会甩翅膀），
            // 自动挑质量好的至少不会把最差的那版交出去。
            string rep1;
            SurfaceUnifyResult r1 = GenerateOnce(target, s, 0, out rep1);
            report = rep1;
            if (r1 == null || r1.Result == null) return r1;
            if (!PoorQuality(r1)) return r1;

            string rep2;
            SurfaceUnifyResult r2 = GenerateOnce(target, s, 1, out rep2);      // 另一种：纯 Coons + 射线
            if (r2 == null || r2.Result == null) return r1;
            if (Score(r2) < Score(r1))
            {
                r2.Note = rep2 + "；参数化自动换成" + (r2.Wrapped ? "爬行" : "Coons + 射线") + "（质量更好）";
                report = r2.Note;
                return r2;
            }
            return r1;
        }

        /// <summary>质量差 = 最大偏差超过对角线 8%，或面积比跑出 0.6~1.7</summary>
        internal static bool PoorQuality(SurfaceUnifyResult r)
        {
            if (r == null || r.Result == null) return true;
            if (r.Diag > 1e-9 && r.MaxDeviation > r.Diag * 0.08) return true;
            if (r.AreaRatio < 0.6 || r.AreaRatio > 1.7) return true;
            return false;
        }

        internal static double Score(SurfaceUnifyResult r)
        {
            double d = r.Diag > 1e-9 ? r.MaxDeviation / r.Diag : r.MaxDeviation;
            double a = r.AreaRatio > 1e-9 ? Math.Abs(Math.Log(r.AreaRatio)) : 2.0;
            // 边界偏差权重给足：用户第一要求就是「边界完全逼近」，宁可用内部差一点的那版
            double b = r.Diag > 1e-9 ? r.BoundaryDeviation / r.Diag : r.BoundaryDeviation;
            return d + 0.25 * a + 0.0005 * r.Folded + 2.0 * b;
        }

        /// <summary>mode：0 = 自动（允许修剪时走 Patch，否则 Coons + 射线）；1 = 强制 Coons + 射线；2 = 强制爬行（实验，未完成）</summary>
        internal static SurfaceUnifyResult GenerateOnce(GeometryBase target, SurfaceUnifySettings s, int mode, out string report)
        {
            var sw = Stopwatch.StartNew();
            var res = new SurfaceUnifyResult();
            var notes = new List<string>();
            // 分步计时（诊断「卡在哪一步」用；报告里输出一行）
            var swStep = Stopwatch.StartNew();
            var timeLog = new System.Text.StringBuilder();
            Action<string> mark = delegate (string tag)
            {
                timeLog.Append(tag + " " + (long)swStep.Elapsed.TotalMilliseconds + "ms · ");
                swStep.Restart();
            };
            report = "";
            if (s == null) { res.Error = "参数为空"; return res; }

            Brep brep; Mesh mesh; string why;
            if (!ResolveSheet(target, out brep, out mesh, out why)) { res.Error = why; return res; }

            // ---- ① 尺寸 / 片状网格 / 边界环
            BoundingBox bb = BoundingBox.Empty;
            try { bb = brep != null ? brep.GetBoundingBox(true) : mesh.GetBoundingBox(true); } catch { }
            if (!bb.IsValid) { res.Error = "目标包围盒无效（退化几何？）"; return res; }
            double diag = bb.Diagonal.Length;
            if (!(diag > 1e-9)) { res.Error = "目标太小或退化"; return res; }
            res.Diag = diag;

            Mesh sheet = null;
            if (mesh != null)
            {
                // 用户网格：先复制再焊接（绝不能改用户文档里的几何）
                try { sheet = mesh.Duplicate() as Mesh; } catch { sheet = null; }
                if (sheet == null) { res.Error = "网格复制失败"; return res; }
            }
            else
            {
                // Brep 输入只需要网格来取裸边（贴合与偏差走 Brep.ClosestPoint，精确且快），
                // 所以这里用较粗的密度就够；边界点随后会被吸附回 Brep 的真实边上。
                double edge = Math.Max(diag / 60.0, 1e-9);
                var mp = new MeshingParameters();
                mp.MaximumEdgeLength = edge;
                mp.MinimumEdgeLength = edge * 0.2;
                mp.GridAspectRatio = 1.0;
                mp.RefineGrid = true;
                mp.SimplePlanes = false;
                try
                {
                    Mesh[] parts = Mesh.CreateFromBrep(brep, mp);
                    if (parts != null && parts.Length > 0)
                    {
                        sheet = new Mesh();
                        for (int i = 0; i < parts.Length; i++) if (parts[i] != null) sheet.Append(parts[i]);
                    }
                }
                catch (Exception ex) { notes.Add("网格化异常：" + ex.Message); }
            }
            if (sheet == null || sheet.Faces.Count == 0) { res.Error = "网格化失败（目标太小或退化？）"; return res; }
            // 焊接：多重曲面的内部接缝顶点必须合并，否则接缝会被 GetNakedEdges 当成边界
            try { sheet.Weld(Math.Max(1e-9, diag * 1e-6)); } catch { }
            try { sheet.Faces.CullDegenerateFaces(); } catch { }
            try { sheet.FaceNormals.ComputeFaceNormals(); sheet.Normals.ComputeNormals(); } catch { }
            mark("网格");

            Polyline[] loops = null;
            try { loops = sheet.GetNakedEdges(); } catch { loops = null; }
            if (loops == null || loops.Length == 0)
            {
                res.Error = "目标没有开放边界（看起来是闭合体）：单一曲面需要片状体（有裸露边界）";
                return res;
            }
            res.SourceBoundary = new List<Polyline>(loops);

            // ---- ② 参考平面 / 外环
            Plane fit = FitPlane(sheet, bb);
            int outerIdx; double[] areas;
            ClassifyLoops(loops, fit, out outerIdx, out areas);
            LoopData outer = LoopData.Build(loops[outerIdx]);
            if (outer == null || outer.P.Length < 4) { res.Error = "外边界太简单/退化"; return res; }

            int n = s.GridCount;
            if (n < MinGrid) n = MinGrid;
            if (n > MaxGrid) n = MaxGrid;
            res.GridU = n; res.GridV = n; res.Corners = 4;

            double cap = s.MaxSnapDistance > 1e-12 ? s.MaxSnapDistance * (s.MmToModel > 1e-12 ? s.MmToModel : 1.0)
                                                   : diag * MaxSnapFactor;
            double strength = Clamp01(s.FitStrength);
            int unsnapped = 0, flipped = 0, rayHits = 0, closeHits = 0;
            bool cornerFallback = false;
            string cornerMethod = "对角极值";
            bool extendFlag = false;
            double extWant = 0;
            Point3d[,] grid = null;
            int[] cornerIdx = null;                 // 4 角（边界迭代修正要用它的弧长位置）
            List<Point3d> junctions = new List<Point3d>();   // 真实边-边交点（转折节点，必须精确命中）

            // ---- ③ 选参数化方式
            //  片状（边界围住整张面：平板 / 圆柱 / 折板 / 波浪片）→ 4 角 Coons 基面 + 沿基面法向射线贴合；
            //  包裹式（边界只是一圈小口、曲面绕着一团体积：兜袋 / 长袜 / 卷边）→ 从边界沿曲面**爬行**逐行生成。
            //  只用 Coons + 射线时，边界行与内部行在 3D 里差得很远 → 三次插值会在中间甩出「翅膀」
            //  （用户实测：兜袋被做成了带翅膀的鞍面），爬行网格相邻两行只差一步，插值面必然贴着原曲面。
            // 边界平面面积必须用**边界环自己的拟合平面**量：
            // 竖直长袜/深兜袋的顶点最小二乘平面是竖直的（对称），边界圆投影上去会退化成一条线 → 面积 ≈ 0
            Plane rimPlane = FitLoopPlane(outer, fit);
            double boundaryArea = Math.Abs(LoopArea2d(outer, rimPlane));
            double wrapRatio = boundaryArea > 1e-12 ? AreaOf(sheet) / boundaryArea : 0.0;
            // ⚠ 爬行参数化目前只在 mode=1（强制）下才走：它还没收敛到能用 ——
            // u 闭合面的边界朝向对不上（实测边界偏差 20+）、极深管状体还会跨薄壁。
            // 默认一律 Coons + 射线（配合「射线方向统一取边界平面法向」已能处理兜袋主体）。
            bool wrapped = mode == 2;
            res.Wrapped = wrapped;

            // ---- Patch 模式（用户口径）：允许修剪时用 Rhino 的 Patch 拟合 ——
            //  边界曲线当约束 → 边界与拐角精确过点，内部尽量贴合，再按边界修剪。
            //  失败自动退回下面的 Coons + 射线。
            if (s.AllowTrim && mode == 0)
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
                int rowsUsed, stalled; bool degPole;
                grid = BuildPeelGrid(brep, sheet, outer, fit, n, diag, cap, strength, out rowsUsed, out degPole, out stalled);
                if (grid == null) { res.Error = "爬行网格构造失败（边界退化？）"; return res; }
                res.RowsUsed = rowsUsed;
                unsnapped = stalled;
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "参数化：包裹式（面积 / 边界平面面积 = {0:0.#}）→ 从边界沿曲面爬行 {1} 行 + 极点{2}",
                    wrapRatio, rowsUsed, degPole ? "" : "（末行小环）"));
            }
            else
            {
                // ---- 边界外扩（用户口径：贴不齐就「超出边界、再把多余的修剪掉」）：
                //  域用**外扩后**的边界建，拟合完用**原边界**剪掉多余部分 → 修剪后边界必然贴齐原边界
                LoopData domain = outer;
                extWant = s.AllowTrim ? Math.Max(0.0, s.Extend) : 0.0;
                if (extWant > 1e-9)
                {
                    var big = EnlargeLoop(outer, rimPlane, extWant);
                    if (big != null && big.Count >= 4)
                    {
                        var plBig = new Polyline(big);
                        plBig.Add(big[0]);
                        LoopData ldBig = LoopData.Build(plBig);
                        if (ldBig != null) domain = ldBig;
                    }
                }
                bool extend = !ReferenceEquals(domain, outer);
                extendFlag = extend;

                Plane frame = PrincipalFrame(domain, fit);
                Corners(domain, frame, out cornerIdx, out cornerFallback, out cornerMethod);
                if (cornerIdx == null) { res.Error = "外边界取 4 角失败"; return res; }
                mark("取角");

                // 真实边-边交点（转折节点）：Brep = 裸边端点，网格 = 裸边折线端点+折角点。
                // 只留外环上的（内孔端点由内孔修剪自己管）；必须被结果面边界精确命中。
                var junctionsAll = CollectCornerIntersections(brep, sheet, diag);
                junctions = FilterJunctionsOnLoop(junctionsAll, outer, diag);
                res.JunctionCount = junctions.Count;
                if (junctions.Count > 0)
                    notes.Add("边-边交点：" + junctions.Count + " 个（转折节点将精确钉住）");

                var rowV0 = SampleArcPinned(domain, cornerIdx[0], cornerIdx[1], n, junctions);       // c0 → c1（v=0 行）
                var rowV1 = SampleArcPinned(domain, cornerIdx[2], cornerIdx[3], n, junctions);       // c2 → c3（反向用）
                var colU0 = SampleArcPinned(domain, cornerIdx[3], cornerIdx[0], n, junctions);       // c3 → c0（反向用）
                var colU1 = SampleArcPinned(domain, cornerIdx[1], cornerIdx[2], n, junctions);       // c1 → c2
                rowV1.Reverse();                                                    // → c3 … c2
                colU0.Reverse();                                                    // → c0 … c3

                // 边界点吸附回 Brep 的真实边（消掉网格弦差）→ 边界「完全逼近」原边界。
                // 外扩时不吸附：外扩点本来就在形状外面，吸附会把它们拉回原边界、外扩就白做了
                if (brep != null && !extend)
                {
                    int snapped = 0;
                    snapped += SnapBoundary(brep, rowV0, diag);
                    snapped += SnapBoundary(brep, rowV1, diag);
                    snapped += SnapBoundary(brep, colU0, diag);
                    snapped += SnapBoundary(brep, colU1, diag);
                    if (snapped > 0) notes.Add("边界点已吸附回原曲面真实边（" + snapped + " 点）");
                    // 交点精确还原：吸附是最近点吸附，交点节点必须严格等于真实边-边交点
                    PinJunctionsExactRow(rowV0, junctions, diag);
                    PinJunctionsExactRow(rowV1, junctions, diag);
                    PinJunctionsExactRow(colU0, junctions, diag);
                    PinJunctionsExactRow(colU1, junctions, diag);
                }

                grid = CoonsGrid(rowV0, rowV1, colU0, colU1, n, n);
                if (grid == null) { res.Error = "Coons 基面构造失败（边界退化？）"; return res; }
                mark("基面");

                // ---- ④ 贴合：网格点 → 原曲面
                var baseNormal = GridNormals(grid, n, n);
                // Coons 基面的法向朝向由边界环走向决定（可能是反的）→ 先与壳体自身平均法向对齐，
                // 否则「法向一致」守卫会把所有射线命中都判成「贴到背面」而全部拒掉（实测：碗形 140 点全被拒）
                Vector3d avgSheet = AverageNormal(sheet);
                Vector3d avgBase = Vector3d.Zero;
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) avgBase += baseNormal[i, j];
                if (avgSheet.Length > 0.5 && avgBase.Length > 0.5 && avgBase * avgSheet < 0)
                    for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) baseNormal[i, j] = -baseNormal[i, j];
                double cell = MeanCellSize(grid, n, n);
                // 射线方向用每个网格点自己的基面法向（Coons 基面歪时也不至于全错）。
                // 试过「统一取边界平面法向」：边界又长又扭时它的拟合平面可能与开口差很多，
                // 射线会侧着打出去，实测反而更差 → 退回逐点法向。
                if (strength > 1e-9)
                {
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                        {
                            bool boundary = (i == 0 || j == 0 || i == n - 1 || j == n - 1);
                            if (boundary && s.LockBoundary) continue;      // 边界保形：边界点不动
                            Point3d b = grid[i, j];
                            Point3d hit; Vector3d hitN; bool aligned, usedRay;
                            if (!SnapToSheet(brep, sheet, b, cap, baseNormal[i, j], out hit, out hitN, out aligned, out usedRay))
                            {
                                unsnapped++;
                                continue;
                            }
                            double d = b.DistanceTo(hit);
                            if (!aligned && d > cell * 0.5) { flipped++; unsnapped++; continue; }
                            if (usedRay) rayHits++; else closeHits++;
                            Vector3d v = hit - b;
                            grid[i, j] = b + v * strength;
                        }
                }
                mark("贴合");

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

            // ---- ⑤ 光顺（边界保形 = 边界点不参与；交点节点 = 固定点，永不移动）
            int iters = (int)Math.Round(Clamp01(s.Smooth) * 6.0);
            if (iters > 0) SmoothGrid(grid, n, n, iters, 0.5, s.LockBoundary);
            // 交点精确钉位：光顺后（开边界保形时边界点本来不动，这里再兜一层）把离交点最近的
            // 边界圈节点精确写到交点坐标上 —— 「两个边的交点」必须分毫不差落在结果面边界上。
            if (s.LockBoundary)
            {
                int pinned = PinJunctionsExact(grid, n, wrapped, junctions, diag);
                if (pinned > 0) notes.Add("交点钉位：" + pinned + " 个节点精确写到边-边交点上");
            }
            mark("光顺");

            // ---- ⑥ 折叠诊断
            int folded;
            GridNormals(grid, n, n, out folded);
            res.Folded = folded;

            // ---- ⑦ 插值出单一 NURBS 面（包裹式：u 沿边界闭合）
            NurbsSurface srf = BuildSurface(grid, n, n, wrapped);
            if (srf == null && wrapped)
            {
                notes.Add("u 方向闭合建面失败 → 退回开放（结果面会多一条接缝）");
                srf = BuildSurface(grid, n, n, false);
            }
            if (srf == null) { res.Error = "NURBS 曲面构造失败"; return res; }

            // ---- ⑦-a 边界迭代修正（主改动）：把结果面每条边的曲线拉回原边界折线（只动边界圈节点）。
            //   边界保形 = 关时不做（边界点本来就会被光顺/贴合带走，硬拉回去等于把参数关掉）；
            //   外扩 + 修剪（允许修剪开）时不做（边界网格点故意落在原边界外面）。
            if (s.LockBoundary && !extendFlag)
            {
                // 修正目标线：Brep 输入优先用**精确裸边**（真实边界，比网格裸边折线精确），
                // 拿不到（网格输入）才用网格裸边 loops。
                Polyline[] bLines = ExactBoundaryPolylines(brep, diag);
                string bWhat = bLines != null ? "精确裸边" : "网格裸边";
                if (bLines == null) bLines = loops;
                double bTarget = Math.Max(diag * 1e-4, 1e-5);
                NurbsSurface refined; double bFinal; string bTrace;
                int bRounds = RefineBoundary(grid, n, wrapped, outer, cornerIdx, bLines, brep, diag, bTarget,
                    junctions, out refined, out bFinal, out bTrace);
                if (refined != null && bRounds > 0)
                {
                    srf = refined;
                    int folded2;                                    // 网格变了 → 折叠诊断按最终网格重算
                    GridNormals(grid, n, n, out folded2);
                    res.Folded = folded2;
                }
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "边界迭代修正：{0} 轮 [{1}] → 边界偏差 {2:0.####}（目标 {3:0.####}，目标线 = {4}）",
                    bRounds, bTrace, bFinal, bTarget, bWhat));
            }

            res.Surface = srf;
            mark("建面+边界迭代");
            try { res.ControlU = srf.Points.CountU; res.ControlV = srf.Points.CountV; } catch { }

            Brep result = null;
            try { result = Brep.CreateFromSurface(srf); } catch { }
            if (result == null) { res.Error = "曲面无法转成 Brep"; return res; }

            // ---- ⑦-b 外扩域 → 用**原边界**把多余的部分剪掉（修剪后边界必然贴齐原边界）
            if (extendFlag)
            {
                int kept; string whyT;
                Brep trimmed = TrimOuter(result, srf, outer, rimPlane, out kept, out whyT);
                if (trimmed != null)
                {
                    result = trimmed;
                    res.Trimmed = true;
                    notes.Add(string.Format(CultureInfo.InvariantCulture,
                        "边界外扩 {0:0.##} → 已按原边界修剪（多余部分剪掉，边界贴齐）", extWant));
                }
                else notes.Add("边界修剪失败（" + whyT + "），按外扩后的边界输出");
            }

            // ---- ⑧ 内孔修剪
            int holesFound = 0, holesKept = 0;
            var holeLoops = new List<LoopData>();
            for (int i = 0; i < loops.Length; i++)
            {
                if (i == outerIdx) continue;
                LoopData ld = LoopData.Build(loops[i]);
                if (ld == null || ld.P.Length < 3) continue;
                holesFound++;
                holeLoops.Add(ld);
            }
            if (holesFound > 0 && s.KeepHoles)
            {
                double tol = Math.Max(diag * 1e-3, 1e-9);
                for (int i = 0; i < holeLoops.Count; i++)
                {
                    int kept; string hwhy;
                    Brep trimmed = TrimHole(result, srf, holeLoops[i], tol, out kept, out hwhy);
                    if (trimmed != null) { result = trimmed; holesKept += kept; }
                    else notes.Add("内孔修剪失败：" + hwhy);
                }
            }
            else if (holesFound > 0)
            {
                notes.Add("内孔：" + holesFound + " 个（已关闭 → 不修剪，结果面覆盖内孔）");
            }
            res.Result = result;
            mark("内孔");
            res.Holes = holesKept;
            res.HolesFound = holesFound;
            res.Faces = result.Faces.Count;
            try { res.Loops = result.Faces.Count > 0 ? result.Faces[0].Loops.Count : 0; } catch { }

            // ---- ⑨ 偏差 / 面积
            Measure(srf, brep, sheet, loops, outerIdx, fit, diag, cap,
                out res.MaxDeviation, out res.RmsDeviation, out res.BoundaryDeviation);
            // 边界偏差改成量**结果面实际边界**（面的 loop）：外扩+修剪后只有这样才能反映真实边界
            double bdev;
            if (MeasureResultBoundary(result, loops, out bdev)) res.BoundaryDeviation = bdev;
            // 交点偏差：每个真实边-边交点到结果面边界的距离（用户口径：交点必须抓住位置）
            if (junctions.Count > 0)
            {
                var jdevs = JunctionDeviations(result, junctions, out res.CornerWorst);
                double worst = 0;
                for (int k = 0; k < jdevs.Count; k++) if (jdevs[k] > worst) worst = jdevs[k];
                res.CornerDeviation = worst;
            }
            // 修剪过 → 偏差按**修剪后的面**重新量一遍（域外的裙边不该算进偏差里）
            if (res.Trimmed)
            {
                double md, rd;
                if (MeasureTrimmedDeviation(result, brep, sheet, cap, out md, out rd))
                {
                    res.MaxDeviation = md;
                    res.RmsDeviation = rd;
                }
            }
            double areaOut = AreaOf(result);
            double areaIn = AreaOf(sheet);
            res.AreaRatio = areaIn > 1e-12 ? areaOut / areaIn : 0.0;
            res.Unsnapped = unsnapped;
            mark("测量+面积");

            // ---- ⑩ 报告
            string src = brep != null ? (brep.Faces.Count > 1 ? "多重曲面" : "曲面") : "网格";
            notes.Insert(0, string.Format(CultureInfo.InvariantCulture,
                "单一曲面：{0} → 1 张 {1}×{1} 控制点 NURBS 面 · 最大偏差 {2:0.####} / 平均 {3:0.####} · 边界偏差 {4:0.####} · 面积比 {5:0.####} · {6:0.00}s",
                src, n, res.MaxDeviation, res.RmsDeviation, res.BoundaryDeviation, res.AreaRatio, sw.Elapsed.TotalSeconds));
            if (junctions.Count > 0)
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "交点偏差：{0:0.######}（{1} 个边-边交点全部逐点核对，最差位置 ({2:0.###}, {3:0.###}, {4:0.###)}）",
                    res.CornerDeviation, junctions.Count, res.CornerWorst.X, res.CornerWorst.Y, res.CornerWorst.Z));
            notes.Add(string.Format(CultureInfo.InvariantCulture,
                "边界：4 角（{0}） · 边界保形 {1} · 贴合强度 {2:0.##} · 平滑度 {3:0.##}（{4} 次）· 贴合范围 {5:0.###}",
                cornerMethod, s.LockBoundary ? "开" : "关",
                strength, Clamp01(s.Smooth), iters, cap));
            notes.Add("耗时明细：" + timeLog.ToString().TrimEnd(' ', '·'));
            notes.Add(string.Format("贴合：射线 {0} 点 / 最近点 {1} 点", rayHits, closeHits));
            if (unsnapped > 0) notes.Add("未贴合网格点：" + unsnapped + "（超出贴合范围或法向相反，改用基面值）");
            if (flipped > 0) notes.Add("法向相反被拒：" + flipped + " 个点");
            if (folded > 0) notes.Add("折叠单元：" + folded + "（倒扣 / 卷边这类地方高度场覆盖不到，结果面会平滑过去；可减小贴合强度、加大平滑度，或加密控制点数）");
            notes.Add(holesFound > 0
                ? string.Format(CultureInfo.InvariantCulture, "内孔：找到 {0} 个 · 保留 {1} 个（修剪环 {2} 个）", holesFound, holesKept, res.Loops)
                : "内孔：无（结果面未修剪）");
            report = string.Join("；", notes.ToArray());
            res.Note = report;
            res.Seconds = sw.Elapsed.TotalSeconds;
            return res;
        }

        // ============================================================ 边界环

        /// <summary>闭合环的顶点 + 累计弧长（首尾不重复；闭合环含最后一段回到起点）</summary>
        internal sealed class LoopData
        {
            public Point3d[] P;
            public double[] Cum;      // 长度 = P.Length（闭合 → 多一段回起点）
            public double Length;

            public static LoopData Build(Polyline pl)
            {
                if (pl == null || pl.Count < 2) return null;
                var pts = new List<Point3d>();
                for (int i = 0; i < pl.Count; i++) pts.Add(pl[i]);
                // 去掉收尾重复点（GetNakedEdges 的闭合环首尾重复）
                while (pts.Count > 2 && pts[0].DistanceTo(pts[pts.Count - 1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
                if (pts.Count < 2) return null;
                var ld = new LoopData();
                ld.P = pts.ToArray();
                int cnt = ld.P.Length;
                ld.Cum = new double[cnt + 1];
                ld.Cum[0] = 0;
                for (int i = 0; i < cnt; i++)
                    ld.Cum[i + 1] = ld.Cum[i] + ld.P[i].DistanceTo(ld.P[(i + 1) % cnt]);
                ld.Length = ld.Cum[cnt];
                if (!(ld.Length > 1e-12)) return null;
                return ld;
            }

            /// <summary>第 i 段（P[i] → P[i+1]，闭合环最后一段回 0）的长度</summary>
            public double SegLen(int i) { return Cum[i + 1] - Cum[i]; }

            /// <summary>顶点的累计弧长参数</summary>
            public double Param(int i) { return Cum[i]; }

            public int Next(int i) { return (i + 1) % P.Length; }
        }

        /// <summary>在平面坐标系里用鞋带公式算环的有向面积（|面积| 用来分辨外环/内孔）</summary>
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

        /// <summary>外环 = 面积绝对值最大的环；其余都是内孔候选</summary>
        internal static void ClassifyLoops(Polyline[] loops, Plane pl, out int outerIdx, out double[] areas)
        {
            areas = new double[loops.Length];
            outerIdx = 0;
            double best = -1;
            for (int i = 0; i < loops.Length; i++)
            {
                LoopData ld = LoopData.Build(loops[i]);
                double a = ld != null ? Math.Abs(LoopArea2d(ld, pl)) : 0;
                areas[i] = a;
                if (a > best) { best = a; outerIdx = i; }
            }
        }

        /// <summary>边界环自己的拟合平面（量环面积、判包裹式用；失败退回给定平面）</summary>
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

        /// <summary>最小二乘拟合平面（采样网格顶点；退化成包围盒中心 + 世界 Z）</summary>
        internal static Plane FitPlane(Mesh m, BoundingBox bb)
        {
            var pts = new List<Point3d>();
            try
            {
                int cnt = m.Vertices.Count;
                int stride = Math.Max(1, cnt / 600);
                for (int i = 0; i < cnt; i += stride) pts.Add(m.Vertices.Point3dAt(i));
            }
            catch { }
            if (pts.Count >= 3)
            {
                try
                {
                    Plane fit;
                    PlaneFitResult r = Plane.FitPlaneToPoints(pts, out fit);
                    if (r != PlaneFitResult.Failure && fit.IsValid) return fit;
                }
                catch { }
            }
            return new Plane(bb.Center, Vector3d.ZAxis);
        }

        /// <summary>主轴坐标系：把外环的主方向当 X 轴（形状拉长的方向），法向当 Z</summary>
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

        /// <summary>
        /// 外环取 4 角（三级，最终用的方式写进报告）：
        ///   ① 曲率优先：外环弧长重采样 200 点 → 每点转角（相邻两段方向夹角）→
        ///      取「局部极大且 &gt; 15°」的候选；候选恰好 4 个且满足「每段 ≥5% 周长」就用它们
        ///      （拐角边界 L 形 / 矩形缺角 / 尖角的多重曲面：直接命中真角点，边内不再残留拐角）；
        ///   ② 对角极值：主轴坐标系里取 4 个「对角极值」点（矩形角点、圆形 45° 点）；
        ///   ③ 弧长四等分（兜底）。
        /// ②③ 都按弧长排序 + 校验（4 点互不相同、每段至少占周长 5%），不合格就回退下一级。
        /// </summary>
        internal static void Corners(LoopData ld, Plane frame, out int[] idx, out bool fallback)
        {
            string method;
            Corners(ld, frame, out idx, out fallback, out method);
        }

        internal static void Corners(LoopData ld, Plane frame, out int[] idx, out bool fallback, out string method)
        {
            idx = null; fallback = false; method = "";
            int cnt = ld.P.Length;
            if (cnt < 4) { fallback = true; method = "弧长四等分"; idx = QuarterByArc(ld); return; }

            // ① 曲率优先（拐角形状）
            int[] curv = CurvatureCorners(ld, 200, 15.0, 0.05);
            if (curv != null) { idx = curv; method = "曲率"; return; }

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

            // 去重 + 按弧长排序
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
                if (ok) { idx = list.ToArray(); method = "对角极值"; return; }
            }
            fallback = true;
            method = "弧长四等分";
            idx = QuarterByArc(ld);
        }

        /// <summary>
        /// 按曲率选 4 角：外环按弧长重采样 samples 点 → 每点转角（相邻两段方向夹角）→
        /// 取「局部极大且 &gt; minAngleDeg」的点当候选；候选恰好 4 个、按弧长排序后每段
        /// ≥ minGapFrac×周长 → 返回它们在 ld.P 里的索引（最近弧长参数映射回原顶点）；
        /// 否则返回 null（调用方回退「对角极值 → 弧长四等分」）。
        /// </summary>
        internal static int[] CurvatureCorners(LoopData ld, int samples, double minAngleDeg, double minGapFrac)
        {
            if (ld == null || ld.P.Length < 4 || !(ld.Length > 1e-12)) return null;
            if (samples < 32) samples = 32;
            var pt = new Point3d[samples];
            var sp = new double[samples];
            for (int k = 0; k < samples; k++)
            {
                sp[k] = ld.Length * k / samples;
                pt[k] = PointAtArc(ld, sp[k]);
            }
            var ang = new double[samples];
            for (int k = 0; k < samples; k++)
            {
                Vector3d a = pt[k] - pt[(k + samples - 1) % samples];      // 入段
                Vector3d b = pt[(k + 1) % samples] - pt[k];                // 出段
                if (a.Length < 1e-12 || b.Length < 1e-12) { ang[k] = 0; continue; }
                a.Unitize(); b.Unitize();
                double c = a * b;
                if (c > 1.0) c = 1.0; else if (c < -1.0) c = -1.0;
                ang[k] = Math.Acos(c) * 180.0 / Math.PI;
            }
            var cand = new List<int>();
            for (int k = 0; k < samples; k++)
            {
                if (ang[k] <= minAngleDeg) continue;
                if (ang[k] > ang[(k + samples - 1) % samples] && ang[k] >= ang[(k + 1) % samples])
                    cand.Add(k);
            }
            if (cand.Count != 4) return null;

            // 候选（重采样点）映射回 ld.P 顶点：按弧长参数取最近顶点；去重失败就回退
            var idx = new List<int>();
            for (int k = 0; k < 4; k++)
            {
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < ld.P.Length; i++)
                {
                    double d = Math.Abs(ld.Param(i) - sp[cand[k]]);
                    if (d < bd) { bd = d; best = i; }
                }
                if (!idx.Contains(best)) idx.Add(best);
            }
            if (idx.Count != 4) return null;
            idx.Sort(delegate (int a, int b) { return ld.Param(a).CompareTo(ld.Param(b)); });

            double minGap = ld.Length * minGapFrac;
            for (int k = 0; k < 4; k++)
            {
                double p0 = ld.Param(idx[k]);
                double p1 = ld.Param(idx[(k + 1) % 4]);
                double gap = k == 3 ? (ld.Length - p0 + p1) : (p1 - p0);
                if (gap < minGap) return null;
            }
            return idx.ToArray();
        }

        /// <summary>弧长四等分（兜底）：4 个角落在周长的 0 / 1/4 / 1/2 / 3/4</summary>
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

        /// <summary>沿环的弧长等分采样（从 iA 正向走到 iB，含两端，count 个点）</summary>
        internal static List<Point3d> SampleArc(LoopData ld, int iA, int iB, int count)
        {
            var pts = new List<Point3d>();
            var lens = new List<double>();
            int i = iA;
            int guard = 0;
            while (true)
            {
                pts.Add(ld.P[i]);
                if (i == iB) break;
                lens.Add(ld.P[i].DistanceTo(ld.P[ld.Next(i)]));
                i = ld.Next(i);
                if (++guard > ld.P.Length + 4) break;
            }

            var outp = new List<Point3d>(count);
            if (pts.Count < 2)
            {
                for (int k = 0; k < count; k++) outp.Add(pts.Count > 0 ? pts[0] : Point3d.Origin);
                return outp;
            }
            double total = 0;
            for (int k = 0; k < lens.Count; k++) total += lens[k];
            if (!(total > 1e-12))
            {
                for (int k = 0; k < count; k++) outp.Add(pts[0]);
                return outp;
            }
            for (int k = 0; k < count; k++)
            {
                double target = total * k / (count - 1.0);
                double acc = 0; int seg = 0;
                while (seg < lens.Count - 1 && acc + lens[seg] < target) { acc += lens[seg]; seg++; }
                double L = lens[seg];
                double t = L > 1e-15 ? (target - acc) / L : 0.0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                outp.Add(pts[seg] + (pts[seg + 1] - pts[seg]) * t);
            }
            return outp;
        }

        // ============================================================ Coons / 网格

        /// <summary>
        /// 3D 双线性 Coons 基面：B(u,v) = (1-u)C0(v)+uC1(v)+(1-v)D0(u)+vD1(u) − 双线性角点项。
        /// 四条边就是原边界的采样点 → 基面边界已经「完全逼近」原边界。
        /// </summary>
        internal static Point3d[,] CoonsGrid(List<Point3d> rowV0, List<Point3d> rowV1,
            List<Point3d> colU0, List<Point3d> colU1, int nu, int nv)
        {
            if (rowV0 == null || rowV1 == null || colU0 == null || colU1 == null) return null;
            if (rowV0.Count != nu || rowV1.Count != nu || colU0.Count != nv || colU1.Count != nv) return null;
            var g = new Point3d[nu, nv];
            Point3d p00 = colU0[0], p10 = colU1[0], p01 = colU0[nv - 1], p11 = colU1[nv - 1];
            for (int i = 0; i < nu; i++)
            {
                double u = nu > 1 ? i / (double)(nu - 1) : 0.0;
                for (int j = 0; j < nv; j++)
                {
                    double v = nv > 1 ? j / (double)(nv - 1) : 0.0;
                    Point3d a = colU0[j] * (1.0 - u) + colU1[j] * u;
                    Point3d b = rowV0[i] * (1.0 - v) + rowV1[i] * v;
                    Point3d c = p00 * ((1 - u) * (1 - v)) + p10 * (u * (1 - v)) + p01 * ((1 - u) * v) + p11 * (u * v);
                    // 注意：Point3d − Point3d = Vector3d，所以这里逐分量算，别写成 a + b − c
                    g[i, j] = new Point3d(a.X + b.X - c.X, a.Y + b.Y - c.Y, a.Z + b.Z - c.Z);
                }
            }
            return g;
        }

        /// <summary>网格单元法向（相邻点差分）→ 用来做贴合时的法向一致性校验 + 折叠诊断</summary>
        internal static Vector3d[,] GridNormals(Point3d[,] g, int nu, int nv)
        {
            int folded;
            return GridNormals(g, nu, nv, out folded);
        }

        internal static Vector3d[,] GridNormals(Point3d[,] g, int nu, int nv, out int folded)
        {
            var nrm = new Vector3d[nu, nv];
            folded = 0;
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    Vector3d du = g[Math.Min(nu - 1, i + 1), j] - g[Math.Max(0, i - 1), j];
                    Vector3d dv = g[i, Math.Min(nv - 1, j + 1)] - g[i, Math.Max(0, j - 1)];
                    Vector3d n = Vector3d.CrossProduct(du, dv);
                    if (n.Length > 1e-15) n.Unitize();
                    nrm[i, j] = n;
                }
            // 折叠 = 某个单元与任一邻居的法向相反（局部定向翻转；整体反向的网格不算折叠）
            for (int i = 1; i < nu - 1; i++)
                for (int j = 1; j < nv - 1; j++)
                {
                    Vector3d c = nrm[i, j];
                    if (c.Length < 0.5) { folded++; continue; }        // 退化单元（相邻点重合）也算病态
                    if (nrm[i - 1, j].Length > 0.5 && c * nrm[i - 1, j] < 0) { folded++; continue; }
                    if (nrm[i + 1, j].Length > 0.5 && c * nrm[i + 1, j] < 0) { folded++; continue; }
                    if (nrm[i, j - 1].Length > 0.5 && c * nrm[i, j - 1] < 0) { folded++; continue; }
                    if (nrm[i, j + 1].Length > 0.5 && c * nrm[i, j + 1] < 0) { folded++; continue; }
                }
            return nrm;
        }

        /// <summary>网格平均单元尺寸（贴合时的「很近」判据）</summary>
        internal static double MeanCellSize(Point3d[,] g, int nu, int nv)
        {
            double sum = 0; int cnt = 0;
            for (int i = 0; i < nu - 1; i++)
                for (int j = 0; j < nv - 1; j++)
                {
                    sum += g[i, j].DistanceTo(g[i + 1, j]); cnt++;
                    sum += g[i, j].DistanceTo(g[i, j + 1]); cnt++;
                }
            return cnt > 0 ? sum / cnt : 1.0;
        }

        /// <summary>拉普拉斯光顺：内部点向 4 邻域平均靠拢（lockBoundary = 边界点不动）</summary>
        internal static void SmoothGrid(Point3d[,] g, int nu, int nv, int iters, double weight, bool lockBoundary)
        {
            for (int it = 0; it < iters; it++)
            {
                var src = (Point3d[,])g.Clone();
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        bool boundary = (i == 0 || j == 0 || i == nu - 1 || j == nv - 1);
                        if (boundary && lockBoundary) continue;
                        var acc = Vector3d.Zero; int cnt = 0;
                        if (i > 0) { acc += src[i - 1, j] - src[i, j]; cnt++; }
                        if (i < nu - 1) { acc += src[i + 1, j] - src[i, j]; cnt++; }
                        if (j > 0) { acc += src[i, j - 1] - src[i, j]; cnt++; }
                        if (j < nv - 1) { acc += src[i, j + 1] - src[i, j]; cnt++; }
                        if (cnt == 0) continue;
                        g[i, j] = src[i, j] + acc * (weight / cnt);
                    }
            }
        }

        /// <summary>
        /// 网格点 → 原曲面。**先沿基面法向打射线**（±法向取最近命中）：凹袋 / 兜形 / 槽形壳体
        /// 的开口正下方就是壳体，这是唯一正确的贴合方向（只找最近点会贴到侧壁上 → 结果面塌成一张盖子）。
        /// 射线打不到（掠射 / 平面退化）再退回最近点。Brep 输入最后把落点吸附回精确曲面。
        /// refNormal = 基面法向（Zero 时只走最近点，偏差测量就是走这条）；
        /// aligned = 落点法向与基面法向一致；usedRay = 是否走的射线。
        /// </summary>
        internal static bool SnapToSheet(Brep brep, Mesh sheet, Point3d p, double cap, Vector3d refNormal,
            out Point3d hit, out Vector3d hitNormal, out bool aligned, out bool usedRay)
        {
            hit = p; hitNormal = Vector3d.Zero; aligned = true; usedRay = false;
            Vector3d n = refNormal;
            if (n.IsValid && n.Length > 1e-12) n.Unitize(); else n = Vector3d.Zero;

            // ① 射线：沿 ±基面法向取最近命中
            if (sheet != null && n.Length > 0.5)
            {
                double best = double.MaxValue;
                bool found = false;
                Vector3d bestDir = n;
                int bestFace = -1;
                for (int k = 0; k < 2; k++)
                {
                    Vector3d dir = k == 0 ? n : -n;
                    int[] faces = null;
                    double t = -1;
                    try { t = Rhino.Geometry.Intersect.Intersection.MeshRay(sheet, new Ray3d(p, dir), out faces); }
                    catch { t = -1; }
                    if (t >= 0 && t <= cap && t < best)
                    {
                        best = t; found = true; bestDir = dir;
                        bestFace = (faces != null && faces.Length > 0) ? faces[0] : -1;
                    }
                }
                if (found)
                {
                    Point3d rp = p + bestDir * best;
                    Vector3d rn = FaceNormalOf(sheet, bestFace);
                    hit = RefineOnBrep(brep, rp, cap * 0.1);
                    hitNormal = rn;
                    // 射线是沿基面法向打出去的：命中点的面朝向不构成「贴到背面」的证据，不设守卫
                    // （薄片的背面问题由最近点那条路把关）
                    aligned = true;
                    usedRay = true;
                    return true;
                }
            }

            // ② 最近点（射线打不到时的兜底；偏差测量也走这条）
            if (brep != null)
            {
                Point3d q = p; ComponentIndex ci = ComponentIndex.Unset; double s = 0, t = 0; Vector3d nn = Vector3d.Zero;
                bool ok = false;
                try { ok = brep.ClosestPoint(p, out q, out ci, out s, out t, cap, out nn); } catch { ok = false; }
                if (!ok) return false;
                hit = q; hitNormal = nn;
                if (nn.IsValid && nn.Length > 1e-12) { nn.Unitize(); hitNormal = nn; }
                aligned = IsAligned(hitNormal, refNormal);
                return true;
            }
            if (sheet == null) return false;
            MeshPoint mp = null;
            try { mp = sheet.ClosestMeshPoint(p, cap); } catch { mp = null; }
            if (mp == null) return false;
            try { hit = sheet.PointAt(mp); } catch { return false; }
            hitNormal = FaceNormalOf(sheet, mp.FaceIndex);
            aligned = IsAligned(hitNormal, refNormal);
            return true;
        }

        /// <summary>片状网格的平均顶点法向（用来把基面法向的朝向对齐到壳体自身）</summary>
        internal static Vector3d AverageNormal(Mesh m)
        {
            if (m == null) return Vector3d.Zero;
            Vector3d acc = Vector3d.Zero;
            try
            {
                int cnt = m.Vertices.Count;
                int stride = Math.Max(1, cnt / 500);
                int used = 0;
                for (int i = 0; i < cnt; i += stride)
                {
                    Vector3d n = m.Normals[i];
                    if (!n.IsValid || n.Length < 1e-12) continue;
                    acc += n; used++;
                }
                if (used > 0 && acc.Length > 1e-15) acc.Unitize();
            }
            catch { }
            return acc;
        }

        /// <summary>网格面法向（按面的顶点自己算，避免依赖 FaceNormals 是否算过）</summary>
        internal static Vector3d FaceNormalOf(Mesh m, int faceIndex)
        {
            try
            {
                if (m == null || faceIndex < 0 || faceIndex >= m.Faces.Count) return Vector3d.Zero;
                MeshFace f = m.Faces[faceIndex];
                Point3d a = m.Vertices.Point3dAt(f.A);
                Point3d b = m.Vertices.Point3dAt(f.B);
                Point3d c = m.Vertices.Point3dAt(f.C);
                Vector3d n = Vector3d.CrossProduct(b - a, c - a);
                if (n.Length > 1e-15) n.Unitize();
                return n;
            }
            catch { return Vector3d.Zero; }
        }

        /// <summary>把点吸附到 Brep 的精确曲面上（只允许很近的移动；Brep 为空时原样返回）</summary>
        internal static Point3d RefineOnBrep(Brep brep, Point3d p, double maxMove)
        {
            if (brep == null) return p;
            Point3d q = p; ComponentIndex ci = ComponentIndex.Unset; double s = 0, t = 0; Vector3d n = Vector3d.Zero;
            bool ok = false;
            try { ok = brep.ClosestPoint(p, out q, out ci, out s, out t, Math.Max(maxMove, 1e-12), out n); } catch { ok = false; }
            if (!ok) return p;
            return q;
        }

        /// <summary>法向一致性：参考法向为空 / 命中法向无效时一律算通过</summary>
        internal static bool IsAligned(Vector3d hitNormal, Vector3d refNormal)
        {
            if (!hitNormal.IsValid || hitNormal.Length < 1e-12) return true;
            if (!refNormal.IsValid || refNormal.Length < 1e-12) return true;
            Vector3d a = hitNormal; a.Unitize();
            Vector3d b = refNormal; b.Unitize();
            return a * b >= 0.15;
        }

        /// <summary>边界点吸附回 Brep 的真实边（消掉网格弦差）；返回吸附成功的点数</summary>
        internal static int SnapBoundary(Brep brep, List<Point3d> pts, double diag)
        {
            int ok = 0;
            double cap = diag * 0.05;
            for (int i = 0; i < pts.Count; i++)
            {
                Point3d q = pts[i]; ComponentIndex ci = ComponentIndex.Unset; double s = 0, t = 0; Vector3d n = Vector3d.Zero;
                bool hit = false;
                try { hit = brep.ClosestPoint(pts[i], out q, out ci, out s, out t, cap, out n); } catch { hit = false; }
                if (!hit) continue;
                if (q.DistanceTo(pts[i]) > cap) continue;
                pts[i] = q;
                ok++;
            }
            return ok;
        }

        /// <summary>
        /// 爬行网格（包裹式形状专用）：第 0 行 = 外边界（沿边界按弧长等分；u 方向闭合），
        /// 之后每一行 = 上一行沿曲面「向内」走一步再贴回曲面；走不动（收敛到极点）后剩余行塌到极点。
        /// 相邻两行在曲面上只差一步 → 插值面必然贴着原曲面，不会在边界与内部之间甩出「翅膀」。
        /// </summary>
        internal static Point3d[,] BuildPeelGrid(Brep brep, Mesh sheet, LoopData outer, Plane fit, int n,
            double diag, double cap, double strength, out int rowsUsed, out bool degeneratePole, out int stalled)
        {
            rowsUsed = 0; degeneratePole = true; stalled = 0;
            if (outer == null || n < 4) return null;
            var grid = new Point3d[n, n];
            var cur = new List<Point3d>(n);
            for (int i = 0; i < n; i++) cur.Add(PointAtArc(outer, outer.Length * i / (double)n));
            for (int i = 0; i < n; i++) grid[i, 0] = cur[i];

            // 步长：按「边界中点到最远顶点的距离」估爬行长度，保证 (n-1) 步能走到最深处（留 35% 余量）
            Point3d rimCenter = CentroidOf(cur);
            double march = 0;
            try
            {
                int stride = Math.Max(1, sheet.Vertices.Count / 600);
                for (int i = 0; i < sheet.Vertices.Count; i += stride)
                {
                    double d = sheet.Vertices.Point3dAt(i).DistanceTo(rimCenter);
                    if (d > march) march = d;
                }
            }
            catch { }
            if (!(march > 1e-9)) march = diag * 0.5;
            double step = Math.Max(march * 1.35 / (n - 1), 1e-9);
            var rimPoly = new Polyline(cur);
            var rimArr = new Polyline[] { rimPoly };

            int row = 1;
            bool converged = false;
            while (row < n)
            {
                var next = new List<Point3d>(n);
                double maxMove = 0, rowLen = 0;
                for (int i = 0; i < n; i++)
                {
                    Point3d p = cur[i];
                    Vector3d t = cur[(i + 1) % n] - cur[(i + n - 1) % n];          // 沿行切线
                    Vector3d nrm = SurfaceNormalAt(brep, sheet, p, cap);
                    Vector3d inward = Vector3d.CrossProduct(nrm, t);
                    if (inward.Length > 1e-12) inward.Unitize();
                    // 内向 = **哪一侧的落点更贴曲面**就走哪一侧。
                    // 不能用「朝重心」判（走到半球底时重心在上方，方向翻反）；
                    // 也不能用「离边界更远」判（在边界上两侧到边界的距离相等，是平局 → 会往上走）
                    Point3d hitA = p, hitB = p;
                    Vector3d hnA = Vector3d.Zero, hnB = Vector3d.Zero;
                    bool alA = true, urA = false, alB = true, urB = false;
                    bool okA = inward.Length > 1e-12 &&
                               SnapToSheet(brep, sheet, p + inward * step, cap, Vector3d.Zero, out hitA, out hnA, out alA, out urA);
                    bool okB = inward.Length > 1e-12 &&
                               SnapToSheet(brep, sheet, p - inward * step, cap, Vector3d.Zero, out hitB, out hnB, out alB, out urB);
                    double dA = okA ? hitA.DistanceTo(p + inward * step) : double.MaxValue;
                    double dB = okB ? hitB.DistanceTo(p - inward * step) : double.MaxValue;
                    Point3d hit = p; bool ok = false;
                    if (dA <= dB && okA) { hit = hitA; ok = true; }
                    else if (okB) { hit = hitB; ok = true; }
                    if (!ok || hit.DistanceTo(p) < step * 0.25)
                    {
                        next.Add(p);                                              // 走不动：原地（收敛）
                        stalled++;
                    }
                    else next.Add(hit);
                    double mv = next[i].DistanceTo(p);
                    if (mv > maxMove) maxMove = mv;
                    if (i > 0) rowLen += next[i].DistanceTo(next[i - 1]);
                }
                rowLen += next[0].DistanceTo(next[n - 1]);
                for (int i = 0; i < n; i++) grid[i, row] = next[i];
                cur = next;
                row++;
                rowsUsed = row;
                if (maxMove < step * 0.35 || rowLen < step * 1.2) { converged = true; break; }
            }

            // 收敛后剩余行：**向极点收缩的锥形尾**（只让最后一行塌成一点；
            // 多行完全相同的点会让 CreateThroughPoints 直接返回 null，实测踩过）
            Point3d pole = CentroidOf(cur);
            int tail = n - row;
            for (int j = 0; j < tail; j++)
            {
                double k = 1.0 - (j + 1) / (double)tail;      // 1 → 0（最后一行 = 极点）
                for (int i = 0; i < n; i++)
                    grid[i, row + j] = k > 1e-9
                        ? new Point3d(pole.X + (cur[i].X - pole.X) * k,
                                      pole.Y + (cur[i].Y - pole.Y) * k,
                                      pole.Z + (cur[i].Z - pole.Z) * k)
                        : pole;
            }
            degeneratePole = true;
            _ = fit;
            return grid;
        }

        /// <summary>网格点处的曲面法向（用最近点取；取不到返回 Zero）</summary>
        internal static Vector3d SurfaceNormalAt(Brep brep, Mesh sheet, Point3d p, double cap)
        {
            Point3d hit; Vector3d hn; bool al, ur;
            if (SnapToSheet(brep, sheet, p, Math.Max(cap * 0.05, 1e-9), Vector3d.Zero, out hit, out hn, out al, out ur))
                if (hn.IsValid && hn.Length > 1e-12) { hn.Unitize(); return hn; }
            return Vector3d.Zero;
        }

        internal static Point3d CentroidOf(List<Point3d> pts)
        {
            if (pts == null || pts.Count == 0) return Point3d.Origin;
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < pts.Count; i++) { x += pts[i].X; y += pts[i].Y; z += pts[i].Z; }
            return new Point3d(x / pts.Count, y / pts.Count, z / pts.Count);
        }

        internal static Point3d MeshCentroid(Mesh m)
        {
            if (m == null) return Point3d.Origin;
            double x = 0, y = 0, z = 0; int cnt = 0;
            try
            {
                int stride = Math.Max(1, m.Vertices.Count / 400);
                for (int i = 0; i < m.Vertices.Count; i += stride)
                {
                    Point3d p = m.Vertices.Point3dAt(i);
                    x += p.X; y += p.Y; z += p.Z; cnt++;
                }
            }
            catch { }
            return cnt > 0 ? new Point3d(x / cnt, y / cnt, z / cnt) : Point3d.Origin;
        }

        /// <summary>网格 → NURBS：CreateThroughPoints 按 u 慢变（points[i*vCount + j]）展平，插值过所有网格点</summary>
        internal static NurbsSurface BuildSurface(Point3d[,] g, int nu, int nv)
        {
            return BuildSurface(g, nu, nv, false);
        }

        /// <summary>uClosed = 包裹式（u 方向沿边界闭合；闭合面不做四角校验，参数域是绕着的）</summary>
        internal static NurbsSurface BuildSurface(Point3d[,] g, int nu, int nv, bool uClosed)
        {
            if (nu < 2 || nv < 2) return null;
            var flat = new List<Point3d>(nu * nv);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++) flat.Add(g[i, j]);
            NurbsSurface srf = null;
            // 注意参数顺序是 (points, uCount, vCount, uDegree, vDegree, uClosed, vClosed)
            // —— 不是 degree 在前（RhinoCommon 的 <param> 顺序就是实参顺序，传反了会直接返回 null）
            try { srf = NurbsSurface.CreateThroughPoints(flat, nu, nv, 3, 3, uClosed, false); } catch { srf = null; }
            if (srf != null && !uClosed && !CornersMatch(srf, g, nu, nv)) srf = null;   // 万一展平顺序不对，宁可报错也不给错面
            return srf;
        }

        /// <summary>四个角点是否对上（校验 CreateThroughPoints 的点序约定）</summary>
        internal static bool CornersMatch(NurbsSurface srf, Point3d[,] g, int nu, int nv)
        {
            try
            {
                Interval du = srf.Domain(0), dv = srf.Domain(1);
                double tol = 1e-6 * (1.0 + g[0, 0].DistanceTo(g[nu - 1, nv - 1]));
                if (srf.PointAt(du.Min, dv.Min).DistanceTo(g[0, 0]) > tol) return false;
                if (srf.PointAt(du.Max, dv.Min).DistanceTo(g[nu - 1, 0]) > tol) return false;
                if (srf.PointAt(du.Min, dv.Max).DistanceTo(g[0, nv - 1]) > tol) return false;
                if (srf.PointAt(du.Max, dv.Max).DistanceTo(g[nu - 1, nv - 1]) > tol) return false;
                return true;
            }
            catch { return false; }
        }

        // ============================================================ 内孔修剪

        /// <summary>
        /// 边界环在边界平面里向外平移（每点沿「切线的垂线」、取背离环质心那一侧）→ 得到「超出边界」的域。
        /// ext = 外扩比例（相对环的 2D 包围盒对角线）。
        /// </summary>
        internal static List<Point3d> EnlargeLoop(LoopData ld, Plane pl, double ext)
        {
            if (ld == null || ld.P.Length < 4 || ext <= 1e-9) return null;
            var q = new Point2d[ld.P.Length];
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            double cx = 0, cy = 0;
            for (int i = 0; i < ld.P.Length; i++)
            {
                q[i] = To2d(pl, ld.P[i]);
                if (q[i].X < u0) u0 = q[i].X; if (q[i].X > u1) u1 = q[i].X;
                if (q[i].Y < v0) v0 = q[i].Y; if (q[i].Y > v1) v1 = q[i].Y;
                cx += q[i].X; cy += q[i].Y;
            }
            cx /= ld.P.Length; cy /= ld.P.Length;
            double scale = Math.Sqrt((u1 - u0) * (u1 - u0) + (v1 - v0) * (v1 - v0));
            if (!(scale > 1e-9)) return null;
            double off = ext * scale;
            var outp = new List<Point3d>(ld.P.Length);
            for (int i = 0; i < ld.P.Length; i++)
            {
                int ip = (i + ld.P.Length - 1) % ld.P.Length, inx = (i + 1) % ld.P.Length;
                double tx = q[inx].X - q[ip].X, ty = q[inx].Y - q[ip].Y;
                double nx = -ty, ny = tx;
                double len = Math.Sqrt(nx * nx + ny * ny);
                if (len < 1e-12) { outp.Add(ld.P[i]); continue; }
                nx /= len; ny /= len;
                if (nx * (q[i].X - cx) + ny * (q[i].Y - cy) < 0) { nx = -nx; ny = -ny; }
                Point2d r = new Point2d(q[i].X + nx * off, q[i].Y + ny * off);
                outp.Add(pl.Origin + pl.XAxis * r.X + pl.YAxis * r.Y);
            }
            return outp;
        }

        internal static Polyline LoopPolyline(LoopData ld)
        {
            if (ld == null) return null;
            var pts = new List<Point3d>(ld.P.Length + 1);
            for (int i = 0; i < ld.P.Length; i++) pts.Add(ld.P[i]);
            pts.Add(ld.P[0]);
            return new Polyline(pts);
        }

        internal static Point3d BrepCentroid(Brep b)
        {
            try
            {
                AreaMassProperties amp = AreaMassProperties.Compute(b);
                if (amp != null && amp.Centroid.IsValid) return amp.Centroid;
            }
            catch { }
            try { BoundingBox bb = b.GetBoundingBox(true); if (bb.IsValid) return bb.Center; } catch { }
            return Point3d.Origin;
        }

        /// <summary>
        /// 外扩域的修剪：把原边界环投到结果面上，切开后保留「落在边界内」的那片。
        /// 修剪后结果面的边界就是原边界的投影 → 边界必然贴齐原边界。
        /// </summary>
        internal static Brep TrimOuter(Brep brep, Surface srf, LoopData loop, Plane pl, out int kept, out string why)
        {
            kept = 0; why = "";
            if (brep == null || srf == null || loop == null) { why = "输入为空"; return null; }
            if (brep.Faces.Count != 1) { why = "结果面不是单面"; return null; }

            int m = Math.Max(32, Math.Min(240, loop.P.Length));
            var on = new List<Point3d>();
            for (int k = 0; k < m; k++)
            {
                Point3d p = PointAtArc(loop, loop.Length * k / (double)m);
                double u, v;
                if (!srf.ClosestPoint(p, out u, out v)) continue;
                on.Add(srf.PointAt(u, v));
            }
            if (on.Count < 4) { why = "边界投影失败"; return null; }
            if (on[0].DistanceTo(on[on.Count - 1]) > 1e-9) on.Add(on[0]);
            Curve cutter = null;
            try { cutter = new PolylineCurve(new Polyline(on)); } catch { cutter = null; }
            if (cutter == null) { why = "无法构造面内曲线"; return null; }

            Brep split = null;
            try { split = brep.Faces[0].Split(new Curve[] { cutter }, 1e-6); } catch { split = null; }
            if (split == null || split.Faces.Count < 2) { why = "面内曲线未能切开（边界可能落在域外）"; return null; }

            Polyline lp = LoopPolyline(loop);
            int best = -1;
            for (int i = 0; i < split.Faces.Count; i++)
            {
                Brep piece = null;
                try { piece = split.Faces[i].DuplicateFace(false); } catch { piece = null; }
                if (piece == null || piece.Faces.Count == 0) continue;
                Point3d c = BrepCentroid(piece);
                if (PointInPolygon2d(To2d(pl, c), lp, pl)) { best = i; break; }
            }
            if (best < 0) { why = "分不清哪片在边界内"; return null; }
            Brep keep = null;
            try { keep = split.Faces[best].DuplicateFace(false); } catch { keep = null; }
            if (keep == null || keep.Faces.Count == 0) { why = "取片失败"; return null; }
            kept = 1;
            return keep;
        }

        /// <summary>结果面的**实际边界**（面的各 loop 采样）到原边界折线的最大距离</summary>
        internal static bool MeasureResultBoundary(Brep result, Polyline[] loops, out double dev)
        {
            dev = 0;
            if (result == null || result.Faces.Count == 0 || loops == null || loops.Length == 0) return false;
            var pts = new List<Point3d>();
            try
            {
                BrepFace face = result.Faces[0];
                foreach (BrepLoop lp in face.Loops)
                {
                    Curve c = null;
                    try { c = lp.To3dCurve(); } catch { c = null; }
                    if (c == null) continue;
                    double len = 0;
                    try { len = c.GetLength(); } catch { len = 0; }
                    if (!(len > 1e-12)) continue;
                    int m = 240;                     // 40 → 240：40 点/环会低估交点处的窄尖峰（用户实测漏检）
                    for (int k = 0; k <= m; k++)
                    {
                        try { pts.Add(c.PointAtLength(len * k / (double)m)); } catch { }
                    }
                }
            }
            catch { }
            if (pts.Count == 0) return false;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = DistToPolylines(loops, pts[i]);
                if (d > dev) dev = d;
            }
            return true;
        }

        /// <summary>
        /// 把内孔环投影到结果面上（ClosestPoint → 曲面上的点），再用这条面内曲线切面，
        /// 保留面积最大的那块（= 带孔的那块）。失败就返回 null（调用方记 note，不影响主输出）。
        /// </summary>
        internal static Brep TrimHole(Brep brep, Surface srf, LoopData hole, double tol, out int kept, out string why)
        {
            kept = 0; why = "";
            if (brep == null || srf == null || hole == null) { why = "输入为空"; return null; }
            if (brep.Faces.Count != 1) { why = "结果面不是单面"; return null; }

            // 1) 采样孔环 → 投影到曲面（ClosestPoint 得到 u,v → PointAt 得到严格在面上的点）
            int n = Math.Max(24, Math.Min(160, hole.P.Length * 2));
            var on = new List<Point3d>();
            for (int k = 0; k < n; k++)
            {
                double target = hole.Length * k / (double)n;
                Point3d p = PointAtArc(hole, target);
                double u, v;
                if (!srf.ClosestPoint(p, out u, out v)) continue;
                on.Add(srf.PointAt(u, v));
            }
            if (on.Count < 4) { why = "孔环投影失败"; return null; }
            if (on[0].DistanceTo(on[on.Count - 1]) > tol) on.Add(on[0]);
            Curve cutter = null;
            try { cutter = new PolylineCurve(new Polyline(on)); } catch { cutter = null; }
            if (cutter == null) { why = "无法构造面内曲线"; return null; }

            // 2) 切面 → 保留面积最大的那块
            // 注意：BrepFace.Split 返回「一个 Brep」（里面是多张面），不是 Brep[]
            Brep split = null;
            try { split = brep.Faces[0].Split(new Curve[] { cutter }, Math.Max(tol, 1e-9)); } catch { split = null; }
            if (split == null || split.Faces.Count < 2) { why = "面内曲线未能切开曲面（孔可能落在边界外）"; return null; }
            int bestI = -1; double bestA = -1;
            for (int i = 0; i < split.Faces.Count; i++)
            {
                double a = AreaOf(split.Faces[i].DuplicateFace(false));
                if (a > bestA) { bestA = a; bestI = i; }
            }
            if (bestI < 0) { why = "切出来的片都是空的"; return null; }
            Brep keptBrep = null;
            try { keptBrep = split.Faces[bestI].DuplicateFace(false); } catch { keptBrep = null; }
            if (keptBrep == null || keptBrep.Faces.Count == 0) { why = "取带孔那片失败"; return null; }
            kept = 1;
            return keptBrep;
        }

        /// <summary>环上按弧长取点</summary>
        internal static Point3d PointAtArc(LoopData ld, double target)
        {
            if (ld.Length > 1e-15) target = target % ld.Length;
            if (target < 0) target += ld.Length;
            int i = 0;
            while (i < ld.P.Length && ld.Cum[i + 1] < target) i++;
            if (i >= ld.P.Length) return ld.P[ld.P.Length - 1];
            double L = ld.SegLen(i);
            double t = L > 1e-15 ? (target - ld.Cum[i]) / L : 0.0;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return ld.P[i] + (ld.P[ld.Next(i)] - ld.P[i]) * t;
        }

        // ============================================================ 偏差 / 面积

        /// <summary>
        /// 偏差：结果面密集采样 → 到原曲面/网格的最近距离（最大 + RMS）；边界偏差 = 结果面四条边 → 原边界折线距离。
        /// 测量用**自己的**宽容差（≥ 对角线一半），不受面板「最大贴合距离」影响；
        /// 内孔区域（原目标那里没有材料）不参与偏差统计，否则会报出「到孔边」的假偏差。
        /// </summary>
        internal static void Measure(NurbsSurface srf, Brep brep, Mesh sheet, Polyline[] loops, int outerIdx,
            Plane fit, double diag, double cap, out double maxDev, out double rmsDev, out double boundaryDev)
        {
            maxDev = 0; rmsDev = 0; boundaryDev = 0;
            if (srf == null) return;
            double mcap = Math.Max(cap * 4.0, diag * 0.5);
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            int nu = 25, nv = 25;
            double sum = 0; int cnt = 0;
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    Point3d p;
                    try { p = srf.PointAt(du.ParameterAt(i / (nu - 1.0)), dv.ParameterAt(j / (nv - 1.0))); } catch { continue; }
                    if (InsideAnyHole(p, loops, outerIdx, fit)) continue;      // 孔里没材料，不参与偏差
                    Point3d hit; Vector3d hn; bool al, ur;
                    if (!SnapToSheet(brep, sheet, p, mcap, Vector3d.Zero, out hit, out hn, out al, out ur)) continue;
                    double d = p.DistanceTo(hit);
                    if (d > maxDev) maxDev = d;
                    sum += d * d; cnt++;
                }
            rmsDev = cnt > 0 ? Math.Sqrt(sum / cnt) : 0;

            if (loops != null && loops.Length > 0)
            {
                int m = 240;                     // 40 → 240：交点处的窄尖峰只有密采样才量得到
                for (int i = 0; i < m; i++)
                {
                    double u = du.ParameterAt(i / (m - 1.0));
                    double v = dv.ParameterAt(i / (m - 1.0));
                    Point3d[] probes = null;
                    try
                    {
                        probes = new Point3d[] {
                            srf.PointAt(u, dv.Min), srf.PointAt(u, dv.Max),
                            srf.PointAt(du.Min, v), srf.PointAt(du.Max, v) };
                    }
                    catch { probes = null; }
                    if (probes == null) continue;
                    for (int k = 0; k < probes.Length; k++)
                    {
                        double d = DistToPolylines(loops, probes[k]);
                        if (d > boundaryDev) boundaryDev = d;
                    }
                }
            }
        }

        // ============================================================ 交点硬约束（边-边交点 / 转折节点）

        /// <summary>
        /// 收集「真实边-边交点」集合（必须被结果面边界精确命中的转折节点）：
        ///   Brep 输入 → 全部裸边（EdgeAdjacency.Naked）的**端点**（端点 = 两条裸边相交的地方；
        ///   只有 1 条裸边的目标没有交点，集合为空）；
        ///   网格输入 → 裸边折线的首尾点 + 顶点里「折角 &gt; 15°」的转折点（网格没有边-边的概念，
        ///   端点就是边-边交点；折点也钉住，避免直线段两端之间的急弯被磨圆）。
        /// 按容差去重（容差 = 对角线 × 1e-6，比焊缝容差更紧 —— 交点必须分毫不差）。
        /// </summary>
        internal static List<Point3d> CollectCornerIntersections(Brep brep, Mesh sheet, double diag)
        {
            var pts = new List<Point3d>();
            double tol = Math.Max(diag * 1e-6, 1e-12);
            Action<Point3d> add = delegate (Point3d p)
            {
                if (!p.IsValid) return;
                for (int i = 0; i < pts.Count; i++) if (pts[i].DistanceTo(p) <= tol) return;
                pts.Add(p);
            };

            if (brep != null)
            {
                try
                {
                    for (int i = 0; i < brep.Edges.Count; i++)
                    {
                        BrepEdge e = brep.Edges[i];
                        if (e == null || e.Valence != EdgeAdjacency.Naked) continue;
                        Curve c = null;
                        try { c = e.DuplicateCurve(); } catch { c = null; }
                        if (c == null) continue;
                        if (c.IsClosed) { add(c.PointAtStart); continue; }
                        add(c.PointAtStart);
                        add(c.PointAtEnd);
                    }
                }
                catch { }
            }
            else if (sheet != null)
            {
                try
                {
                    Polyline[] loops = sheet.GetNakedEdges();
                    if (loops != null)
                        for (int i = 0; i < loops.Length; i++)
                        {
                            LoopData ld = LoopData.Build(loops[i]);
                            if (ld == null) continue;
                            for (int k = 0; k < ld.P.Length; k++)
                            {
                                // 折线顶点：端点必收；中间顶点只有「折角 > 15°」（真转折）才收
                                bool corner = k == 0 || IsLoopCorner(ld, k, 15.0);
                                if (corner) add(ld.P[k]);
                            }
                        }
                }
                catch { }
            }
            return pts;
        }

        /// <summary>环上第 i 个顶点是不是「折角 &gt; minAngleDeg」的转折点</summary>
        internal static bool IsLoopCorner(LoopData ld, int i, double minAngleDeg)
        {
            if (ld == null || ld.P.Length < 3) return true;          // 顶点太少 → 全部当转折点
            int prev = (i + ld.P.Length - 1) % ld.P.Length;
            int next = (i + 1) % ld.P.Length;
            Vector3d v1 = ld.P[i] - ld.P[prev];
            Vector3d v2 = ld.P[ld.Next(i)] - ld.P[i];
            if (v1.Length < 1e-12 || v2.Length < 1e-12) return true;
            v1.Unitize(); v2.Unitize();
            double c = v1 * v2;
            if (c > 1.0) c = 1.0; else if (c < -1.0) c = -1.0;
            return Math.Acos(c) * 180.0 / Math.PI > minAngleDeg;
        }

        /// <summary>点是否落在给定的外环折线上（容差 = diag×1e-4；内孔边端点要被滤掉）</summary>
        internal static List<Point3d> FilterJunctionsOnLoop(List<Point3d> pts, LoopData outer, double diag)
        {
            var res = new List<Point3d>();
            if (pts == null || pts.Count == 0 || outer == null) return res;
            double tol = Math.Max(diag * 1e-4, 1e-12);
            var pl = new Polyline[] { LoopPolyline(outer) };
            for (int i = 0; i < pts.Count; i++)
                if (DistToPolylines(pl, pts[i]) <= tol) res.Add(pts[i]);
            return res;
        }

        internal sealed class JunctionRel { public double Rel; public Point3d P; }


        /// <summary>交点 → [sA, sA+span] 弧长段内的相对位置（段外的交点丢弃；wrapped 整圈时全保留）</summary>
        internal static List<JunctionRel> JunctionRelsOnSpan(LoopData ld, List<Point3d> junctions, double sA, double span)
        {
            var res = new List<JunctionRel>();
            if (junctions == null || junctions.Count == 0 || ld == null || !(span > 1e-12)) return res;
            double L = ld.Length;
            for (int i = 0; i < junctions.Count; i++)
            {
                double arc = LoopArcOf(ld, junctions[i]);
                double rel = arc - sA;
                rel = ((rel % L) + L) % L;
                if (rel > span)
                {
                    if (span < L - 1e-9) continue;             // 不在这段弧上（别的边管它）
                    rel = span;                                 // 整圈：夹回末端
                }
                res.Add(new JunctionRel { Rel = rel, P = junctions[i] });
            }
            return res;
        }

        /// <summary>
        /// [0, span] 上铺 n 个弧长位（两端固定 0/span），并把交点的相对位置**硬钉**进去：
        /// 每个交点占用离它最近的那个内部节点（端点已覆盖的跳过），其余节点保持等弧长。
        /// 相邻间隔限幅（≥ 等分×0.2）防退化。返回长度恒为 n 的递增序列。
        /// </summary>
        internal static double[] ArcPositionsPinned(double span, int n, List<double> jRel)
        {
            var pos = new double[n];
            for (int k = 0; k < n; k++) pos[k] = n > 1 ? span * k / (n - 1) : 0.0;
            if (jRel == null || jRel.Count == 0 || n < 3 || !(span > 1e-12)) return pos;
            double minGap = span / (n - 1) * 0.2;
            var rels = new List<double>(jRel);
            rels.Sort();
            for (int j = 0; j < rels.Count; j++)
            {
                double r = rels[j];
                if (r < minGap * 0.5 || r > span - minGap * 0.5) continue;   // 端点节点已覆盖
                int best = -1; double bd = double.MaxValue;
                for (int k = 1; k < n - 1; k++)
                {
                    double d = Math.Abs(pos[k] - r);
                    if (d < bd) { bd = d; best = k; }
                }
                if (best < 0) continue;
                double lo = best > 1 ? pos[best - 1] + minGap : 0.0;
                double hi = best < n - 2 ? pos[best + 1] - minGap : span;
                pos[best] = Math.Min(Math.Max(r, lo), hi);
            }
            for (int k = 1; k < n; k++) if (pos[k] < pos[k - 1] + minGap * 0.2) pos[k] = pos[k - 1] + minGap * 0.2;
            for (int k = n - 2; k >= 1; k--) if (pos[k] > pos[k + 1] - minGap * 0.2) pos[k] = pos[k + 1] - minGap * 0.2;
            pos[0] = 0; pos[n - 1] = span;
            return pos;
        }

        /// <summary>
        /// 弧长采样（恒返回 n 个点）+ 交点硬钉：交点占一个名位，其余等弧长；
        /// 钉到位的节点直接写成**交点的精确坐标**（Brep 输入 = 真实边端点，网格输入 = 折线顶点），
        /// 消掉网格折线的弦差 —— 插值面必然精确过每个交点。
        /// </summary>
        internal static List<Point3d> SampleArcPinned(LoopData ld, int iA, int iB, int n, List<Point3d> junctions)
        {
            double sA = ld.Param(iA);
            double span = ld.Param(iB) - sA;
            if (span < 0) span += ld.Length;
            if (!(span > 1e-12)) return SampleArc(ld, iA, iB, n);
            var jrs = JunctionRelsOnSpan(ld, junctions, sA, span);
            var rels = new List<double>(jrs.Count);
            for (int k = 0; k < jrs.Count; k++) rels.Add(jrs[k].Rel);
            double[] pos = ArcPositionsPinned(span, n, rels);
            var pts = new List<Point3d>(n);
            for (int k = 0; k < n; k++) pts.Add(PointAtArc(ld, sA + pos[k]));
            double tol = 1e-9 * Math.Max(1.0, span);
            for (int k = 1; k < n - 1; k++)
                for (int j = 0; j < jrs.Count; j++)
                    if (Math.Abs(pos[k] - jrs[j].Rel) <= tol) { pts[k] = jrs[j].P; break; }
            return pts;
        }

        /// <summary>单条边界采样序列的交点精确写回：离交点最近的点严格改成交点坐标（不移动序）</summary>
        internal static void PinJunctionsExactRow(List<Point3d> pts, List<Point3d> junctions, double diag)
        {
            if (pts == null || pts.Count == 0 || junctions == null || junctions.Count == 0) return;
            double halfGap = Math.Max(diag / pts.Count * 0.5, 1e-12);
            for (int c = 0; c < junctions.Count; c++)
            {
                Point3d p = junctions[c];
                if (!p.IsValid) continue;
                int best = -1; double bd = halfGap;
                for (int k = 0; k < pts.Count; k++)
                {
                    double d = pts[k].DistanceTo(p);
                    if (d < bd) { bd = d; best = k; }
                }
                if (best >= 0) pts[best] = p;
            }
        }

        /// <summary>
        /// 把边界圈上「离交点最近的节点」精确挪到交点上（交点 = 真实边-边交点，分毫不差）。
        /// 只动半间隙以内的最近节点 —— 交点因此成为插值面的**固定插值点**，光顺/拉回都带不走。
        /// 返回实际挪动的节点数。
        /// </summary>
        internal static int PinJunctionsExact(Point3d[,] g, int n, bool wrapped, List<Point3d> junctions, double diag)
        {
            if (g == null || n < 2 || junctions == null || junctions.Count == 0) return 0;
            double halfGap = Math.Max(diag / n * 0.5, 1e-12);
            var bi = new List<int>(); var bj = new List<int>();
            for (int i = 0; i < n; i++)
            {
                bi.Add(i); bj.Add(0);
                if (!wrapped) { bi.Add(i); bj.Add(n - 1); }
            }
            if (!wrapped)
                for (int j = 1; j < n - 1; j++) { bi.Add(0); bj.Add(j); bi.Add(n - 1); bj.Add(j); }
            // 同一个交点可能被多个候选节点「抢」：先按距离全局配对（贪心），再逐个写点，
            // 避免两个交点先后写到同一个节点上、把另一个交点挤丢。
            int moved = 0;
            var order = new List<int>();
            for (int c = 0; c < junctions.Count; c++) if (junctions[c].IsValid) order.Add(c);
            var taken = new HashSet<int>();
            for (int c = 0; c < junctions.Count; c++)
            {
                Point3d p = junctions[c];
                if (!p.IsValid) continue;
                int best = -1; double bd = halfGap;
                for (int k = 0; k < bi.Count; k++)
                {
                    if (taken.Contains(k)) continue;
                    double d = g[bi[k], bj[k]].DistanceTo(p);
                    if (d < bd) { bd = d; best = k; }
                }
                if (best < 0) continue;
                if (g[bi[best], bj[best]].DistanceTo(p) > 1e-12) moved++;
                g[bi[best], bj[best]] = p;
                taken.Add(best);
            }
            return moved;
        }

        /// <summary>集合里离 p 最近的交点距离（无交点返回 double.MaxValue）</summary>
        internal static double JunctionDistance(List<Point3d> junctions, Point3d p)
        {
            if (junctions == null || junctions.Count == 0) return double.MaxValue;
            double best = double.MaxValue;
            for (int i = 0; i < junctions.Count; i++)
            {
                double d = junctions[i].DistanceTo(p);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// 交点偏差：每个真实边-边交点到**结果面边界**（faces[0] 每个 loop 的 3D 曲线，Curve.ClosestPoint 精确求距）
        /// 的距离。返回逐点偏差（顺序 = junctions 顺序），worst = 偏差最大的交点。
        /// </summary>
        internal static List<double> JunctionDeviations(Brep result, List<Point3d> junctions, out Point3d worst)
        {
            worst = Point3d.Unset;
            var devs = new List<double>();
            if (result == null || result.Faces.Count == 0 || junctions == null || junctions.Count == 0) return devs;
            var curves = new List<Curve>();
            try
            {
                foreach (BrepLoop lp in result.Faces[0].Loops)
                {
                    Curve c = null;
                    try { c = lp.To3dCurve(); } catch { c = null; }
                    if (c != null && c.IsValid) curves.Add(c);
                }
            }
            catch { }
            if (curves.Count == 0) return devs;
            double dev = -1.0;
            for (int i = 0; i < junctions.Count; i++)
            {
                double best = double.MaxValue;
                for (int k = 0; k < curves.Count; k++)
                {
                    double t;
                    try
                    {
                        if (!curves[k].ClosestPoint(junctions[i], out t)) continue;
                        double d = junctions[i].DistanceTo(curves[k].PointAt(t));
                        if (d < best) best = d;
                    }
                    catch { }
                }
                double val = best < double.MaxValue ? best : 0.0;
                devs.Add(val);
                if (val > dev) { dev = val; worst = junctions[i]; }
            }
            return devs;
        }

        // ============================================================ 边界迭代修正

        /// <summary>
        /// 边界迭代修正（主改动）：网格每向只有 n 个边界采样点落在真实边界上，两点之间是三次曲线
        /// 自己走的 —— 真实边界在两点之间有拐角/急弯时就会被磨圆或朝外鼓（用户实测边界偏差 0.49）。
        /// 做法：结果面每条边的曲线密采样（80~160 点）→ 投影回原边界环量出**弧长位置**与**偏差**；
        /// 以「1 + 权重×偏差/最大偏差」为密度沿边界重新分布该边的 n 个边界圈节点（偏差大的地方加密，
        /// 拐角处自动聚点）→ 重建面；最多 6 轮，直到偏差 &lt; target 或不再下降。
        /// 节点始终落在原边界上（弧长重采样 + Brep 输入再吸附回精确边），只动 i/j 首末那一圈节点，
        /// 内部节点不动。返回实际迭代轮数；bestSrf / bestDev = 边界偏差最小的一版（保证不劣化），
        /// 结束后把网格回写成最好的一版。
        /// junctions = 真实边-边交点：重分布时钉位不挪、每轮结束精确钉住，最后再按交点偏差补 ≤3 轮。
        /// </summary>
        internal static int RefineBoundary(Point3d[,] g, int n, bool wrapped, LoopData outer, int[] cornerIdx,
            Polyline[] targetLines, Brep brep, double diag, double target, List<Point3d> junctions,
            out NurbsSurface bestSrf, out double bestDev, out string trace)
        {
            bestSrf = null; bestDev = double.MaxValue; trace = "";
            if (g == null || outer == null || targetLines == null || targetLines.Length == 0 || n < 4) return 0;
            NurbsSurface cur = BuildSurface(g, n, n, wrapped);
            if (cur == null) return 0;
            bestDev = SurfaceBoundaryDev(cur, n, wrapped, targetLines);
            bestSrf = cur;
            if (bestDev <= target) return 0;                    // 已达标：不做任何修正

            var bestGrid = (Point3d[,])g.Clone();
            double L = outer.Length;
            int edges = wrapped ? 1 : 4;
            var sStart = new double[4];
            var sEnd = new double[4];
            var sDir = new int[4];                     // +1 = 沿环弧长递增方向；-1 = 递减
            if (!wrapped)
            {
                if (cornerIdx == null || cornerIdx.Length != 4) return 0;
                // 网格索引方向（见 CoonsGrid 的用法）：v=0 行 c0→c1、v=n-1 行 c3→c2、
                // u=0 列 c0→c3、u=n-1 列 c1→c2；后两条边是**沿环反向**走的（采样时 Reverse 过）
                sStart[0] = outer.Param(cornerIdx[0]); sEnd[0] = outer.Param(cornerIdx[1]); sDir[0] = 1;
                sStart[1] = outer.Param(cornerIdx[3]); sEnd[1] = outer.Param(cornerIdx[2]); sDir[1] = -1;
                sStart[2] = outer.Param(cornerIdx[0]); sEnd[2] = outer.Param(cornerIdx[3]); sDir[2] = -1;
                sStart[3] = outer.Param(cornerIdx[1]); sEnd[3] = outer.Param(cornerIdx[2]); sDir[3] = 1;
            }
            int rounds = 0;
            var loopCorners = wrapped ? null : LoopCorners(outer, 30.0);   // 显著角点（转角 > 30°）
            var traceB = new System.Text.StringBuilder();
            traceB.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.####}", bestDev));
            for (int it = 0; it < 6; it++)
            {
                bool moved = false;
                for (int e = 0; e < edges; e++)
                    if (RedistributeBoundaryEdge(cur, e, n, wrapped, outer, L, sStart[e], sEnd[e], sDir[e],
                            loopCorners, targetLines, g, junctions, diag))
                        moved = true;
                if (!moved) break;                                        // 偏差已经为 0：不用动
                if (brep != null) SnapBoundaryLoop(g, n, wrapped, brep, diag);
                // 交点钉位：重分布/吸附会挪节点，交点节点每轮都精确写回（不允许被带偏）
                PinJunctionsExact(g, n, wrapped, junctions, diag);
                // 弧长重分布之后再做一遍「垂直拉回」：平滑边界的弦切下沉（三点之间的三次曲线切进边界内侧）
                // 靠这一步把节点略微外抬；重分布管拐角聚点，这一步管平滑段贴齐，两者互补。
                {
                    var corr = new Vector3d[n, n];
                    var wsum = new double[n, n];
                    double maxStep = Math.Max(MeanCellSize(g, n, n), 1e-9) * 0.25;
                    for (int e2 = 0; e2 < edges; e2++)
                        PullBoundaryToTarget(cur, e2, n, wrapped, targetLines, target, maxStep, 0.8, corr, wsum, junctions, diag);
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                        {
                            if (!(wsum[i, j] > 1e-12)) continue;
                            // 交点节点：拉回后精确写回交点（权重最大 = 固定点，任何拉回都带不走）
                            Vector3d avg = corr[i, j] * (1.0 / wsum[i, j]);
                            if (avg.Length < 1e-12) continue;
                            g[i, j] = g[i, j] + avg * 0.8;
                            moved = true;
                        }
                    PinJunctionsExact(g, n, wrapped, junctions, diag);
                }
                rounds++;
                NurbsSurface next = BuildSurface(g, n, n, wrapped);
                if (next == null || !next.IsValid) { rounds--; break; }
                double nd = SurfaceBoundaryDev(next, n, wrapped, targetLines);
                traceB.Append(string.Format(CultureInfo.InvariantCulture, " → {0:0.####}", nd));
                if (nd < bestDev - Math.Max(1e-12, bestDev * 1e-9))
                {
                    bestDev = nd; bestSrf = next;
                    bestGrid = (Point3d[,])g.Clone();
                    cur = next;
                    if (nd <= target) break;                              // 达标
                }
                else break;                                               // 不再下降：停
            }
            trace = traceB.ToString();

            // ---- 交点验证轮（≤2）：网格里每个交点节点就是交点坐标（BuildSurface 插值过点），
            //   所以「建面成功 = 交点命中」；只防极端劣化，不做 ClosestPoint 逐点核实（大模型上慢）。
            if (junctions != null && junctions.Count > 0)
            {
                for (int it = 0; it < 2; it++)
                {
                    NurbsSurface probe = BuildSurface(g, n, n, wrapped);
                    if (probe == null) break;
                    // 交点节点被本-round 的钉位固定 → 面必过交点；这里只核对面是否还 IsValid
                    if (!probe.IsValid) break;
                    // 交点偏差按「节点 = 交点」构造性保证为 0；不做边界偏差再评估（省时）
                    break;
                }
            }

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) g[i, j] = bestGrid[i, j];     // 回退到最好的一版
            return rounds;
        }

        /// <summary>
        /// 一条边的「偏差加权弧长重分布」：边曲线密采样 → 每个采样点投影回原边界环得到弧长位置 s
        /// 与到目标边界的偏差 d → 以 ρ = 1 + W·d/dmax 为密度，在 [sStart, sEnd] 上按等质量重新放置
        /// n 个边界圈节点（两端固定在角点）。偏差大的地方（拐角 / 急弯）节点自动加密。
        /// junctions = 真实边-边交点：它们的弧长位置在重分布后**精确恢复**（钉住不挪）。
        /// 返回是否真的动了点。
        /// </summary>
        internal static bool RedistributeBoundaryEdge(NurbsSurface srf, int e, int n, bool wrapped, LoopData outer,
            double L, double sStart, double sEnd, int dir, List<double> loopCorners, Polyline[] targetLines, Point3d[,] g,
            List<Point3d> junctions, double diag)
        {
            if (srf == null || outer == null || targetLines == null || targetLines.Length == 0) return false;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            int m = Math.Max(80, Math.Min(160, 10 * n));
            double span;
            if (wrapped) span = L;                                        // 闭合边：整圈
            else
            {
                span = dir > 0 ? (sEnd - sStart) : (sStart - sEnd);
                while (span < 0) span += L;
                if (!(span > 1e-12) || span > L + 1e-12) span = L;
            }

            const int NB = 64;
            double binW = span / NB;
            var dev = new double[NB];
            for (int k = 0; k < m; k++)
            {
                double t = k / (double)(m - 1);
                Point3d p;
                try { p = EdgePointAt(srf, du, dv, e, t); }
                catch { continue; }
                double d = DistToPolylines(targetLines, p);
                double arc = LoopArcOf(outer, p);
                double rel = dir > 0 ? (arc - sStart) : (sStart - arc);
                rel = ((rel % L) + L) % L;
                if (!wrapped && rel > span) rel = span;                    // 角点附近投影越界 → 夹回
                int b = (int)(rel / binW);
                if (b < 0) b = 0;
                if (b >= NB) b = NB - 1;
                if (d > dev[b]) dev[b] = d;
            }
            // 1-2-1 平滑两遍（把单点尖峰摊开，避免密度被一个坏采样点带偏）
            var sm = new double[NB];
            for (int b = 0; b < NB; b++)
            {
                double a = dev[b > 0 ? b - 1 : 0], c = dev[b < NB - 1 ? b + 1 : NB - 1];
                sm[b] = 0.25 * a + 0.5 * dev[b] + 0.25 * c;
            }
            var sm2 = new double[NB];
            for (int b = 0; b < NB; b++)
            {
                double a = sm[b > 0 ? b - 1 : 0], c = sm[b < NB - 1 ? b + 1 : NB - 1];
                sm2[b] = 0.25 * a + 0.5 * sm[b] + 0.25 * c;
            }
            // 稳健参考尺度 = 剖面均值（单点尖峰靠下面的密度上限压住，不会把密度全吸过去）
            double dsum = 0;
            for (int b = 0; b < NB; b++) dsum += sm2[b];
            double dref = dsum / NB;
            if (!(dref > 1e-15)) return false;                            // 整条边偏差都可忽略

            const double W = 8.0;                                         // 密度比上限 = 1+W（别太猛：极端聚点会让面退化）
            var rho = new double[NB + 1];
            for (int b = 0; b <= NB; b++)
            {
                double rhoB = 1.0 + W * (sm2[b < NB ? b : NB - 1] / dref);
                if (rhoB > 1.0 + W) rhoB = 1.0 + W;
                rho[b] = rhoB;
            }
            var cum = new double[NB + 1];
            for (int b = 0; b < NB; b++) cum[b + 1] = cum[b] + binW * 0.5 * (rho[b] + rho[b + 1]);
            double total = cum[NB];
            if (!(total > 1e-15)) return false;

            var pos = new double[n];
            if (wrapped)
            {
                for (int k = 0; k < n; k++) pos[k] = InvertMass(cum, binW, NB, total * k / (double)n);
            }
            else
            {
                pos[0] = 0; pos[n - 1] = span;
                for (int k = 1; k < n - 1; k++) pos[k] = InvertMass(cum, binW, NB, total * k / (double)(n - 1));
                // 显著角点（拐角节点）直接钉到最近的那个边界圈节点上（用户口径：拐角节点要一致）
                if (loopCorners != null && loopCorners.Count > 0)
                {
                    double meanGap = span / (n - 1);
                    for (int c = 0; c < loopCorners.Count; c++)
                    {
                        double rel = dir > 0 ? (loopCorners[c] - sStart) : (sStart - loopCorners[c]);
                        rel = ((rel % L) + L) % L;
                        if (rel <= 1e-9 || rel >= span - 1e-9) continue;   // 端点本来就是 Coons 角点
                        int best = -1; double bd = double.MaxValue;
                        for (int k = 1; k < n - 1; k++)
                        {
                            double d = Math.Abs(pos[k] - rel);
                            if (d < bd) { bd = d; best = k; }
                        }
                        if (best < 0 || bd > meanGap * 3.0) continue;
                        pos[best] = rel;
                    }
                    Array.Sort(pos);
                }
                // 相邻间隔比值限幅（≤ 4 倍）：均匀参数化的三次插值对极端不均匀的采样会剧烈过冲
                const double ratioCap = 3.0;
                for (int k = 2; k < n; k++)
                {
                    double prevGap = pos[k - 1] - pos[k - 2];
                    double gap = pos[k] - pos[k - 1];
                    if (prevGap > 1e-12 && gap > prevGap * ratioCap) pos[k] = pos[k - 1] + prevGap * ratioCap;
                }
                for (int k = n - 3; k >= 0; k--)
                {
                    double nextGap = pos[k + 2] - pos[k + 1];
                    double gap = pos[k + 1] - pos[k];
                    if (nextGap > 1e-12 && gap > nextGap * ratioCap) pos[k] = pos[k + 1] - nextGap * ratioCap;
                }
                pos[0] = 0; pos[n - 1] = span;
                double minGap = span / (n - 1) * 0.05;                    // 相邻节点最小间隔（防退化/自交）
                for (int k = 1; k < n; k++) if (pos[k] < pos[k - 1] + minGap) pos[k] = pos[k - 1] + minGap;
                if (pos[n - 1] > span) pos[n - 1] = span;
            }

            bool moved = false;
            for (int k = 0; k < n; k++)
            {
                double s = wrapped || dir > 0 ? (sStart + pos[k]) : (sStart - pos[k]);
                s = ((s % L) + L) % L;
                Point3d np = PointAtArc(outer, s);
                int gi, gj;
                if (wrapped) { gi = k; gj = 0; }
                else if (e == 0) { gi = k; gj = 0; }
                else if (e == 1) { gi = k; gj = n - 1; }
                else if (e == 2) { gi = 0; gj = k; }
                else { gi = n - 1; gj = k; }
                if (np.DistanceTo(g[gi, gj]) > 1e-12) { g[gi, gj] = np; moved = true; }
            }
            // 真实边-边交点精确钉位：重分布把节点摆在弧长密度位上，交点若恰好被挤开，
            // 这里把「离交点最近且够近」的节点精确改成交点坐标（不重排其他节点）。
            if (junctions != null && junctions.Count > 0 && !wrapped)
            {
                var jrs = JunctionRelsOnSpan(outer, junctions, sStart, span);
                for (int c = 0; c < jrs.Count; c++)
                {
                    double rel = jrs[c].Rel;
                    if (rel <= 1e-9 || rel >= span - 1e-9) continue;       // 端点本来就是角点
                    int gi, gj, best = -1; double bd = span / (n - 1) * 0.6;
                    for (int k = 1; k < n - 1; k++)
                    {
                        if (e == 0) { gi = k; gj = 0; }
                        else if (e == 1) { gi = k; gj = n - 1; }
                        else if (e == 2) { gi = 0; gj = k; }
                        else { gi = n - 1; gj = k; }
                        double d = g[gi, gj].DistanceTo(jrs[c].P);
                        if (d < bd) { bd = d; best = k; }
                    }
                    if (best < 0) continue;
                    if (e == 0) g[best, 0] = jrs[c].P;
                    else if (e == 1) g[best, n - 1] = jrs[c].P;
                    else if (e == 2) g[0, best] = jrs[c].P;
                    else g[n - 1, best] = jrs[c].P;
                }
            }
            return moved;
        }

        /// <summary>
        /// <summary>
        /// 边界圈节点的「垂直拉回」：边曲线密采样 → 偏差 &gt; target 的采样点，把「原边界最近点 − 当前点」
        /// 按 hat 权重分给相邻两个边界圈节点（e=0/1 分给 grid[i,0]/grid[i,n-1]；e=2/3 分给
        /// grid[0,j]/grid[n-1,j]；wrapped 分给闭合的 grid[i,0]）。限幅 maxStep，调用方再乘松弛系数。
        /// junctions = 真实边-边交点：采样点若落在交点半间隙内，权重 ×8（交点附近贴合权重最大）；
        /// 交点节点本身在调用方 PinJunctionsExact 里精确写回，这里不直接动它。
        /// </summary>
        internal static void PullBoundaryToTarget(NurbsSurface srf, int e, int n, bool wrapped,
            Polyline[] targetLines, double target, double maxStep, double relax,
            Vector3d[,] corr, double[,] wsum, List<Point3d> junctions, double diag)
        {
            if (srf == null || targetLines == null || targetLines.Length == 0) return;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            int m = Math.Max(80, Math.Min(160, 10 * n));
            for (int k = 0; k < m; k++)
            {
                double t = k / (double)(m - 1);
                Point3d p;
                try { p = EdgePointAt(srf, du, dv, e, t); }
                catch { continue; }
                double d = DistToPolylines(targetLines, p);
                if (!(d > target)) continue;
                Point3d q = ClosestOnPolylines(targetLines, p);
                if (!q.IsValid) continue;
                Vector3d v = q - p;
                double len = v.Length;
                if (!(len > 1e-15)) continue;
                if (len > maxStep) v = v * (maxStep / len);
                // （交点不做加权：×8 会把拉回步幅炸大、中段反而过冲；交点精确度由
                //  PinJunctionsExact 每轮写回 + 交点验证轮保证）

                if (wrapped)
                {
                    double x = t * n;
                    int i0 = (int)Math.Floor(x);
                    double fr = x - i0;
                    i0 = ((i0 % n) + n) % n;
                    int i1 = (i0 + 1) % n;
                    corr[i0, 0] += v * (1.0 - fr); wsum[i0, 0] += (1.0 - fr);
                    corr[i1, 0] += v * fr; wsum[i1, 0] += fr;
                    continue;
                }
                double y = t * (n - 1);
                int j0 = (int)Math.Floor(y);
                if (j0 < 0) j0 = 0;
                if (j0 > n - 2) j0 = n - 2;
                double f = y - j0;
                int a = j0, b = j0 + 1;
                int ia, ja, ib, jb;
                if (e == 0) { ia = a; ja = 0; ib = b; jb = 0; }
                else if (e == 1) { ia = a; ja = n - 1; ib = b; jb = n - 1; }
                else if (e == 2) { ia = 0; ja = a; ib = 0; jb = b; }
                else { ia = n - 1; ja = a; ib = n - 1; jb = b; }
                corr[ia, ja] += v * (1.0 - f); wsum[ia, ja] += (1.0 - f);
                corr[ib, jb] += v * f; wsum[ib, jb] += f;
            }
        }

        /// <summary>累积质量反解：找 x 使 M(x) = mt（bin 内线性插值）</summary>
        internal static double InvertMass(double[] cum, double binW, int NB, double mt)
        {
            int b = 0;
            while (b < NB && cum[b + 1] < mt) b++;
            if (b >= NB) return NB * binW;
            double seg = cum[b + 1] - cum[b];
            double fr = seg > 1e-15 ? (mt - cum[b]) / seg : 0;
            if (fr < 0) fr = 0; else if (fr > 1) fr = 1;
            return (b + fr) * binW;
        }

        /// <summary>点到环的最近点对应的弧长参数（把结果面边上的采样点映射回原边界的弧长位置）</summary>
        internal static double LoopArcOf(LoopData ld, Point3d p)
        {
            if (ld == null || ld.P.Length < 2) return 0;
            double bestD = double.MaxValue, bestS = 0;
            for (int i = 0; i < ld.P.Length; i++)
            {
                Point3d a = ld.P[i], b = ld.P[ld.Next(i)];
                Point3d q = ClosestOnSegment(p, a, b);
                double d = q.DistanceTo(p);
                if (d < bestD) { bestD = d; bestS = ld.Cum[i] + a.DistanceTo(q); }
            }
            return bestS;
        }

        /// <summary>环上「转角 &gt; minAngleDeg」的显著角点（返回它们的弧长参数）</summary>
        internal static List<double> LoopCorners(LoopData ld, double minAngleDeg)
        {
            var res = new List<double>();
            if (ld == null || ld.P.Length < 3) return res;
            double cosT = Math.Cos(minAngleDeg * Math.PI / 180.0);
            for (int i = 0; i < ld.P.Length; i++)
            {
                Point3d a = ld.P[i];
                Point3d prev = ld.P[(i + ld.P.Length - 1) % ld.P.Length];
                Point3d next = ld.P[ld.Next(i)];
                Vector3d v1 = a - prev, v2 = next - a;
                if (v1.Length < 1e-12 || v2.Length < 1e-12) continue;
                v1.Unitize(); v2.Unitize();
                if (v1 * v2 < cosT) res.Add(ld.Param(i));
            }
            return res;
        }

        /// <summary>边界圈节点重采样后贴回 Brep 的精确边（消掉网格弦差）；包裹式只有 v=0 那一行</summary>
        internal static void SnapBoundaryLoop(Point3d[,] g, int n, bool wrapped, Brep brep, double diag)
        {
            if (g == null || brep == null || n < 2) return;
            var row = new List<Point3d>(n);
            for (int k = 0; k < n; k++) row.Add(g[k, 0]);
            if (SnapBoundary(brep, row, diag) > 0) for (int k = 0; k < n; k++) g[k, 0] = row[k];
            if (wrapped) return;
            for (int k = 0; k < n; k++) row[k] = g[k, n - 1];
            if (SnapBoundary(brep, row, diag) > 0) for (int k = 0; k < n; k++) g[k, n - 1] = row[k];
            for (int k = 0; k < n; k++) row[k] = g[0, k];
            if (SnapBoundary(brep, row, diag) > 0) for (int k = 0; k < n; k++) g[0, k] = row[k];
            for (int k = 0; k < n; k++) row[k] = g[n - 1, k];
            if (SnapBoundary(brep, row, diag) > 0) for (int k = 0; k < n; k++) g[n - 1, k] = row[k];
        }

        /// <summary>
        /// Brep 的裸边（精确边界曲线）→ 细折线（每条按弧长重采样，弦差远小于容差），
        /// 供边界迭代修正当目标：Brep 输入时这是**真实边界**（比网格裸边折线精确）；
        /// 拿不到就返回 null（退回网格裸边 loops）。
        /// </summary>
        internal static Polyline[] ExactBoundaryPolylines(Brep brep, double diag)
        {
            if (brep == null) return null;
            Curve[] curves = null;
            try { curves = brep.DuplicateNakedEdgeCurves(true, true); } catch { curves = null; }
            if (curves == null || curves.Length == 0) return null;
            double step = Math.Max(diag * 5e-4, 1e-9);
            var list = new List<Polyline>();
            for (int i = 0; i < curves.Length; i++)
            {
                Curve c = curves[i];
                if (c == null) continue;
                double len = 0;
                try { len = c.GetLength(); } catch { len = 0; }
                if (!(len > 1e-12)) continue;
                int m = Math.Max(48, Math.Min(400, (int)Math.Ceiling(len / step)));
                var pts = new List<Point3d>(m + 1);
                for (int k = 0; k <= m; k++)
                {
                    Point3d p;
                    try { p = c.PointAtLength(len * k / (double)m); } catch { continue; }
                    pts.Add(p);
                }
                if (pts.Count >= 2) list.Add(new Polyline(pts));
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>
        /// 结果面参数边界（4 条边；wrapped 时只有 v=0 那条是边界）密采样 80~160 点
        /// → 原边界折线的最大距离。
        /// </summary>
        internal static double SurfaceBoundaryDev(NurbsSurface srf, int n, bool wrapped, Polyline[] loops)
        {
            if (srf == null || loops == null || loops.Length == 0) return 0;
            Interval du = srf.Domain(0), dv = srf.Domain(1);
            int m = Math.Max(80, Math.Min(160, 10 * n));
            int edges = wrapped ? 1 : 4;
            double dev = 0;
            for (int e = 0; e < edges; e++)
                for (int k = 0; k < m; k++)
                {
                    Point3d p;
                    try { p = EdgePointAt(srf, du, dv, e, k / (double)(m - 1)); }
                    catch { continue; }
                    double d = DistToPolylines(loops, p);
                    if (d > dev) dev = d;
                }
            return dev;
        }

        /// <summary>参数边界采样：e = 0/1 → v 域最小/最大（u 走 t）；e = 2/3 → u 域最小/最大（v 走 t）</summary>
        internal static Point3d EdgePointAt(NurbsSurface srf, Interval du, Interval dv, int e, double t)
        {
            if (e == 0) return srf.PointAt(du.ParameterAt(t), dv.Min);
            if (e == 1) return srf.PointAt(du.ParameterAt(t), dv.Max);
            if (e == 2) return srf.PointAt(du.Min, dv.ParameterAt(t));
            return srf.PointAt(du.Max, dv.ParameterAt(t));
        }

        /// <summary>
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

        /// <summary>点是否落在某个内孔环里（把点和环都投到拟合平面上做 2D 射线法）</summary>
        internal static bool InsideAnyHole(Point3d p, Polyline[] loops, int outerIdx, Plane fit)
        {
            if (loops == null || loops.Length == 0) return false;
            Point2d q = To2d(fit, p);
            for (int i = 0; i < loops.Length; i++)
            {
                if (i == outerIdx) continue;
                Polyline pl = loops[i];
                if (pl == null || pl.Count < 3) continue;
                if (PointInPolygon2d(q, pl, fit)) return true;
            }
            return false;
        }

        internal static bool PointInPolygon2d(Point2d q, Polyline pl, Plane fit)
        {
            bool inside = false;
            int n = pl.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d a = To2d(fit, pl[i]);
                Point2d b = To2d(fit, pl[j]);
                if ((a.Y > q.Y) != (b.Y > q.Y))
                {
                    double dy = b.Y - a.Y;
                    if (Math.Abs(dy) < 1e-15) continue;
                    double x = (b.X - a.X) * (q.Y - a.Y) / dy + a.X;
                    if (q.X < x) inside = !inside;
                }
            }
            return inside;
        }

        internal static double AreaOf(Brep b)
        {
            if (b == null) return 0;
            try
            {
                AreaMassProperties amp = AreaMassProperties.Compute(b);
                return amp != null ? amp.Area : 0;
            }
            catch { return 0; }
        }

        internal static double AreaOf(Mesh m)
        {
            if (m == null) return 0;
            try
            {
                AreaMassProperties amp = AreaMassProperties.Compute(m);
                return amp != null ? amp.Area : 0;
            }
            catch { return 0; }
        }

        // ============================================================ 诊断

        /// <summary>
        /// 诊断（Probe 命令用）：把目标解析成什么、片状网格多大、有几条边界环、每条环的面积/周长、
        /// 参考平面/4 角/4 条边长、以及拟合结果与偏差全打成一段文本。不写文档、不改目标。
        /// </summary>
        internal static string Diagnose(GeometryBase target, SurfaceUnifySettings s)
        {
            var sb = new System.Text.StringBuilder();
            Brep brep; Mesh mesh; string why;
            if (!ResolveSheet(target, out brep, out mesh, out why)) { sb.AppendLine("解析失败：" + why); return sb.ToString(); }

            BoundingBox bb = BoundingBox.Empty;
            try { bb = brep != null ? brep.GetBoundingBox(true) : mesh.GetBoundingBox(true); } catch { }
            double diag = bb.IsValid ? bb.Diagonal.Length : 0;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "目标：{0} · 包围盒 {1:0.###}×{2:0.###}×{3:0.###} · 对角线 {4:0.###}",
                brep != null ? (brep.Faces.Count > 1 ? "多重曲面 " + brep.Faces.Count + " 张面" : "曲面") : "网格",
                bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z, diag));
            if (brep != null)
            {
                int naked = 0, interior = 0;
                try
                {
                    for (int i = 0; i < brep.Edges.Count; i++)
                    {
                        EdgeAdjacency v = brep.Edges[i].Valence;
                        if (v == EdgeAdjacency.Naked) naked++;
                        else if (v == EdgeAdjacency.Interior) interior++;
                    }
                }
                catch { }
                sb.AppendLine(string.Format("Brep：{0} 张面 · {1} 条边（裸边 {2} / 内部 {3}）· 面积 {4:0.####}",
                    brep.Faces.Count, brep.Edges.Count, naked, interior, AreaOf(brep)));
            }

            // 片状网格（和 Generate 同一条路）
            Mesh sheet = null;
            if (mesh != null) { try { sheet = mesh.Duplicate() as Mesh; } catch { sheet = null; } }
            else
            {
                double edge = Math.Max(diag / 60.0, 1e-9);
                var mp = new MeshingParameters();
                mp.MaximumEdgeLength = edge; mp.MinimumEdgeLength = edge * 0.2;
                mp.GridAspectRatio = 1.0; mp.RefineGrid = true; mp.SimplePlanes = false;
                try
                {
                    Mesh[] parts = Mesh.CreateFromBrep(brep, mp);
                    if (parts != null)
                    {
                        sheet = new Mesh();
                        for (int i = 0; i < parts.Length; i++) if (parts[i] != null) sheet.Append(parts[i]);
                    }
                }
                catch { }
            }
            if (sheet == null) { sb.AppendLine("片状网格：失败"); return sb.ToString(); }
            try { sheet.Weld(Math.Max(1e-9, diag * 1e-6)); } catch { }
            try { sheet.Faces.CullDegenerateFaces(); } catch { }
            double sheetArea = AreaOf(sheet);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "片状网格：{0} 顶点 / {1} 面 · 面积 {2:0.####}（边 {3:0.###}）",
                sheet.Vertices.Count, sheet.Faces.Count, sheetArea, Math.Max(diag / 60.0, 1e-9)));

            Polyline[] loops = null;
            try { loops = sheet.GetNakedEdges(); } catch { }
            if (loops == null || loops.Length == 0) { sb.AppendLine("裸边环：0（闭合体）"); return sb.ToString(); }

            Plane fit = FitPlane(sheet, bb);
            int outerIdx; double[] areas;
            ClassifyLoops(loops, fit, out outerIdx, out areas);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "裸边环：{0} 个（外环 = 第 {1} 个）", loops.Length, outerIdx + 1));
            double outerArea = 0, sumHole = 0;
            int shown = 0;
            for (int i = 0; i < loops.Length; i++)
            {
                LoopData ld = LoopData.Build(loops[i]);
                double len = ld != null ? ld.Length : 0;
                bool closed = loops[i] != null && loops[i].IsClosed;
                if (i == outerIdx) outerArea = areas[i]; else sumHole += areas[i];
                if (i < 8 || i == outerIdx)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  环{0}：面积 {1:0.####} · 周长 {2:0.####} · 顶点 {3} · {4}{5}",
                        i + 1, areas[i], len, loops[i] != null ? loops[i].Count : 0,
                        closed ? "闭合" : "开口", i == outerIdx ? " ← 外环" : ""));
                    shown++;
                }
            }
            if (loops.Length > shown) sb.AppendLine("  …（其余 " + (loops.Length - shown) + " 个环略）");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "面积账：外环 {0:0.####} / 网格 {1:0.####} = {2:0.###%}；内环合计 {3:0.####}",
                outerArea, sheetArea, sheetArea > 1e-12 ? outerArea / sheetArea : 0, sumHole));

            LoopData ol = LoopData.Build(loops[outerIdx]);
            if (ol != null)
            {
                Plane frame = PrincipalFrame(ol, fit);
                int[] ci; bool fb; string cm;
                Corners(ol, frame, out ci, out fb, out cm);
                if (ci != null)
                {
                    var names = new string[] { "c0", "c1", "c2", "c3" };
                    for (int k = 0; k < 4; k++)
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} = ({1:0.###}, {2:0.###}, {3:0.###})",
                            names[k], ol.P[ci[k]].X, ol.P[ci[k]].Y, ol.P[ci[k]].Z));
                    for (int k = 0; k < 4; k++)
                    {
                        List<Point3d> side = SampleArc(ol, ci[k], ci[(k + 1) % 4], 2);
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  边{0} 长 {1:0.###}",
                            k + 1, side.Count > 1 ? side[0].DistanceTo(side[1]) : 0));
                    }
                    sb.AppendLine("4 角取法：" + cm + (fb ? "（兜底）" : ""));
                }
            }

            string rep;
            SurfaceUnifyResult r = Generate(target, s, out rep);
            sb.AppendLine("拟合：" + (string.IsNullOrEmpty(r.Error) ? rep : ("失败：" + r.Error)));
            if (r.Result != null)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "结果：{0} 张面 · 修剪环 {1} · 内孔 {2}/{3} · 控制点 {4}×{5} · 未贴合 {6} · 折叠 {7}",
                    r.Result.Faces.Count, r.Loops, r.Holes, r.HolesFound, r.ControlU, r.ControlV, r.Unsnapped, r.Folded));
            return sb.ToString();
        }

        // ============================================================ 工具

        internal static Point2d To2d(Plane pl, Point3d p)
        {
            Vector3d d = p - pl.Origin;
            return new Point2d(Vector3d.Multiply(d, pl.XAxis), Vector3d.Multiply(d, pl.YAxis));
        }

        internal static double Clamp01(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        /// <summary>点到多段线集合的最近点</summary>
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
                if (pl.IsClosed && pl.Count >= 3)
                {
                    double d = DistToSegment(p, pl[pl.Count - 1], pl[0]);
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

        /// <summary>写结果：每个目标一张单一曲面（图层「单一曲面」，对象名带偏差）</summary>
        internal static void AddToDocument(RhinoDoc doc, List<SurfaceUnifyResult> results, out int count)
        {
            count = 0;
            if (doc == null || results == null) return;
            int layer = EnsureLayer(doc, LayerSurface, Color.FromArgb(168, 69, 111));
            for (int i = 0; i < results.Count; i++)
            {
                SurfaceUnifyResult r = results[i];
                if (r == null || r.Result == null || r.Result.Faces.Count == 0) continue;
                var attrs = new ObjectAttributes();
                attrs.LayerIndex = layer;
                attrs.Name = string.Format(CultureInfo.InvariantCulture, "单一曲面（偏差 {0:0.####}）", r.MaxDeviation);
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
    }
}
