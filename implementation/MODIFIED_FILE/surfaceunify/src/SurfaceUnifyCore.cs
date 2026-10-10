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
        public NurbsSurface Surface;     // 未修剪的 NURBS 面（诊断/自检用）
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
        public double AreaRatio;         // 面积比（结果 / 原目标）
        public int Unsnapped;            // 没能贴到原曲面的网格点数
        public int Folded;               // 折叠单元数（诊断）
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
            var sw = Stopwatch.StartNew();
            var res = new SurfaceUnifyResult();
            var notes = new List<string>();
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

            Polyline[] loops = null;
            try { loops = sheet.GetNakedEdges(); } catch { loops = null; }
            if (loops == null || loops.Length == 0)
            {
                res.Error = "目标没有开放边界（看起来是闭合体）：单一曲面需要片状体（有裸露边界）";
                return res;
            }
            res.SourceBoundary = new List<Polyline>(loops);

            // ---- ② 参考平面 / 外环 / 4 角
            Plane fit = FitPlane(sheet, bb);
            int outerIdx; double[] areas;
            ClassifyLoops(loops, fit, out outerIdx, out areas);
            LoopData outer = LoopData.Build(loops[outerIdx]);
            if (outer == null || outer.P.Length < 4) { res.Error = "外边界太简单/退化，无法取 4 角"; return res; }

            Plane frame = PrincipalFrame(outer, fit);
            int[] cornerIdx;
            bool cornerFallback;
            Corners(outer, frame, out cornerIdx, out cornerFallback);
            if (cornerIdx == null) { res.Error = "外边界取 4 角失败"; return res; }

            int n = s.GridCount;
            if (n < MinGrid) n = MinGrid;
            if (n > MaxGrid) n = MaxGrid;
            res.GridU = n; res.GridV = n; res.Corners = 4;

            var rowV0 = SampleArc(outer, cornerIdx[0], cornerIdx[1], n);        // c0 → c1（v=0 行）
            var rowV1 = SampleArc(outer, cornerIdx[2], cornerIdx[3], n);        // c2 → c3（反向用）
            var colU0 = SampleArc(outer, cornerIdx[3], cornerIdx[0], n);        // c3 → c0（反向用）
            var colU1 = SampleArc(outer, cornerIdx[1], cornerIdx[2], n);        // c1 → c2
            rowV1.Reverse();                                                    // → c3 … c2
            colU0.Reverse();                                                    // → c0 … c3

            // 边界点吸附回 Brep 的真实边（消掉网格弦差）→ 边界「完全逼近」原边界
            if (brep != null)
            {
                int snapped = 0;
                snapped += SnapBoundary(brep, rowV0, diag);
                snapped += SnapBoundary(brep, rowV1, diag);
                snapped += SnapBoundary(brep, colU0, diag);
                snapped += SnapBoundary(brep, colU1, diag);
                if (snapped > 0) notes.Add("边界点已吸附回原曲面真实边（" + snapped + " 点）");
            }

            // ---- ③ 3D Coons 基面
            var grid = CoonsGrid(rowV0, rowV1, colU0, colU1, n, n);
            if (grid == null) { res.Error = "Coons 基面构造失败（边界退化？）"; return res; }

            // ---- ④ 贴合：网格点 → 原曲面
            double cap = s.MaxSnapDistance > 1e-12 ? s.MaxSnapDistance * (s.MmToModel > 1e-12 ? s.MmToModel : 1.0)
                                                   : diag * MaxSnapFactor;
            var baseNormal = GridNormals(grid, n, n);
            // Coons 基面的法向朝向由边界环走向决定（可能是反的）→ 先与壳体自身平均法向对齐，
            // 否则「法向一致」守卫会把所有射线命中都判成「贴到背面」而全部拒掉（实测：碗形 140 点全被拒）
            {
                Vector3d avgSheet = AverageNormal(sheet);
                Vector3d avgBase = Vector3d.Zero;
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) avgBase += baseNormal[i, j];
                if (avgSheet.Length > 0.5 && avgBase.Length > 0.5 && avgBase * avgSheet < 0)
                    for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) baseNormal[i, j] = -baseNormal[i, j];
            }
            double cell = MeanCellSize(grid, n, n);
            int unsnapped = 0, flipped = 0, rayHits = 0, closeHits = 0;
            double strength = Clamp01(s.FitStrength);
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

            // ---- ⑤ 光顺（边界保形 = 边界点不参与）
            int iters = (int)Math.Round(Clamp01(s.Smooth) * 6.0);
            if (iters > 0) SmoothGrid(grid, n, n, iters, 0.5, s.LockBoundary);

            // ---- ⑥ 折叠诊断
            int folded;
            GridNormals(grid, n, n, out folded);
            res.Folded = folded;

            // ---- ⑦ 插值出单一 NURBS 面
            NurbsSurface srf = BuildSurface(grid, n, n);
            if (srf == null) { res.Error = "NURBS 曲面构造失败"; return res; }
            res.Surface = srf;
            try { res.ControlU = srf.Points.CountU; res.ControlV = srf.Points.CountV; } catch { }

            Brep result = null;
            try { result = Brep.CreateFromSurface(srf); } catch { }
            if (result == null) { res.Error = "曲面无法转成 Brep"; return res; }

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
            res.Holes = holesKept;
            res.HolesFound = holesFound;
            res.Faces = result.Faces.Count;
            try { res.Loops = result.Faces.Count > 0 ? result.Faces[0].Loops.Count : 0; } catch { }

            // ---- ⑨ 偏差 / 面积
            Measure(srf, brep, sheet, loops, outerIdx, fit, diag, cap,
                out res.MaxDeviation, out res.RmsDeviation, out res.BoundaryDeviation);
            double areaOut = AreaOf(result);
            double areaIn = AreaOf(sheet);
            res.AreaRatio = areaIn > 1e-12 ? areaOut / areaIn : 0.0;
            res.Unsnapped = unsnapped;

            // ---- ⑩ 报告
            string src = brep != null ? (brep.Faces.Count > 1 ? "多重曲面" : "曲面") : "网格";
            notes.Insert(0, string.Format(CultureInfo.InvariantCulture,
                "单一曲面：{0} → 1 张 {1}×{1} 控制点 NURBS 面 · 最大偏差 {2:0.####} / 平均 {3:0.####} · 边界偏差 {4:0.####} · 面积比 {5:0.####} · {6:0.00}s",
                src, n, res.MaxDeviation, res.RmsDeviation, res.BoundaryDeviation, res.AreaRatio, sw.Elapsed.TotalSeconds));
            notes.Add(string.Format(CultureInfo.InvariantCulture,
                "边界：4 角{0} · 边界保形 {1} · 贴合强度 {2:0.##} · 平滑度 {3:0.##}（{4} 次）· 贴合范围 {5:0.###}",
                cornerFallback ? "（弧长四等分）" : "（对角极值）", s.LockBoundary ? "开" : "关",
                strength, Clamp01(s.Smooth), iters, cap));
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
        /// 外环取 4 角：先在主轴坐标系里取 4 个「对角极值」点（矩形的角点、圆形的 45° 点），
        /// 再按弧长排序 + 校验（4 点互不相同、每段至少占周长 5%）；不合格 → 退化成弧长四等分。
        /// </summary>
        internal static void Corners(LoopData ld, Plane frame, out int[] idx, out bool fallback)
        {
            idx = null; fallback = false;
            int cnt = ld.P.Length;
            if (cnt < 4) { fallback = true; idx = QuarterByArc(ld); return; }

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
                if (ok) { idx = list.ToArray(); return; }
            }
            fallback = true;
            idx = QuarterByArc(ld);
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

        /// <summary>网格 → NURBS：CreateThroughPoints 按 u 慢变（points[i*vCount + j]）展平，插值过所有网格点</summary>
        internal static NurbsSurface BuildSurface(Point3d[,] g, int nu, int nv)
        {
            if (nu < 2 || nv < 2) return null;
            var flat = new List<Point3d>(nu * nv);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++) flat.Add(g[i, j]);
            // 注意参数顺序是 (points, uCount, vCount, uDegree, vDegree, uClosed, vClosed)
            // —— 不是 degree 在前（RhinoCommon 的 <param> 顺序就是实参顺序，传反了会直接返回 null）
            NurbsSurface srf = null;
            try { srf = NurbsSurface.CreateThroughPoints(flat, nu, nv, 3, 3, false, false); } catch { srf = null; }
            if (srf != null && !CornersMatch(srf, g, nu, nv)) srf = null;   // 万一展平顺序不对，宁可报错也不给错面
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
                int m = 40;
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
                int[] ci; bool fb;
                Corners(ol, frame, out ci, out fb);
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
                    sb.AppendLine("4 角取法：" + (fb ? "弧长四等分（兜底）" : "对角极值"));
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
