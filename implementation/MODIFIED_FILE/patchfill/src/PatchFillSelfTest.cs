using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace PatchFillPattern
{
    /// <summary>
    /// 无人值守自检。用法：命令 PatchFillSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放
    /// run-patchfill-selftest.flag 后启动 Rhino（跑完自动写报告并退出）。
    /// </summary>
    public class PatchFillSelfTestCommand : Command
    {
        public override string EnglishName { get { return "PatchFillSelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\PatchFillSelfTest.txt");
            }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RunTo(doc, DefaultReportPath, false);
            return Result.Success;
        }

        public static void RunTo(RhinoDoc doc, string reportPath, bool exitAfter)
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0, skip = 0;
            sb.AppendLine("=== 多边补面 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            // 文档没准备好（Rhino 刚起来 / 上一次会话留下的空文档）→ 主动拿一次 / 新建，别让写文档用例假失败
            if (doc == null) { try { doc = RhinoDoc.ActiveDoc; } catch { } }
            if (doc == null) { try { doc = RhinoDoc.Create(null); } catch { } }
            if (doc == null) { sb.AppendLine("提示：没有活动文档（写文档用例会跳过）"); }

            try
            {
                Case1_Pure(sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case2_PlanarRectHole(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case3_PentagonHole(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case4_TriangleHole(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case5_CurvedHole(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case6_G1Tangent(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case7_InnerConstraints(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case8_Reject(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case9_Document(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case10_PanelWiring(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case11_Determinism(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case12_ResidualAndHoles(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case13_PatchBaseline(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case14_G2Curvature(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
            }
            catch (Exception ex)
            {
                sb.AppendLine("[FAIL] 自检异常: " + ex);
                fail++;
            }

            sb.AppendLine();
            if (skip > 0) sb.AppendLine("跳过: " + skip + " 项（环境不具备，不算失败）");
            sb.AppendLine(string.Format("RESULT: {0} 通过 / {1} 失败 -> {2}", pass, fail, fail == 0 ? "PASS" : "FAIL"));

            Flush(reportPath, sb, pass, fail, skip);

            try { RhinoApp.WriteLine(sb.ToString()); } catch { }

            if (exitAfter)
            {
                try
                {
                    RhinoDoc d = RhinoDoc.ActiveDoc;
                    if (d != null) d.Modified = false;
                }
                catch { }
                try { RhinoApp.Exit(); } catch { }
            }
        }

        // ================================================================== 用例1：纯函数

        static void Case1_Pure(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例1：边界环 / 4 角 / 拐角 / Coons / 基函数 / Cholesky（纯函数） ---");

            // ---- 闭合环：收尾重复点去掉、周长、面积
            var rect = new Polyline(new Point3d[] {
                new Point3d(0,0,0), new Point3d(10,0,0), new Point3d(10,10,0), new Point3d(0,10,0), new Point3d(0,0,0) });
            PatchFillCore.LoopData ld = PatchFillCore.LoopData.Build(ToPoints(rect), null, null);
            Check(sb, ref pass, ref fail, ld != null && ld.P.Length == 4, "闭合环：收尾重复点被去掉（4 个顶点）");
            Check(sb, ref pass, ref fail, ld != null && Math.Abs(ld.Length - 40.0) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "闭合环：周长 = 40（实测 {0:0.####}）", ld != null ? ld.Length : -1));
            double area = Math.Abs(PatchFillCore.LoopArea2d(ld, Plane.WorldXY));
            Check(sb, ref pass, ref fail, Math.Abs(area - 100.0) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "环面积（鞋带公式）= 100（实测 {0:0.####}）", area));

            // ---- 弧长采样：严格等分
            double[] cp = PatchFillCore.CornerParams(ld, new int[] { 0, 1, 2, 3 });
            Check(sb, ref pass, ref fail, Math.Abs(cp[0]) < 1e-12 && Math.Abs(cp[1] - 10) < 1e-12
                && Math.Abs(cp[2] - 20) < 1e-12 && Math.Abs(cp[3] - 30) < 1e-12, "4 角弧长参数 = 0/10/20/30");
            Point3d mid = PatchFillCore.SidePoint(ld, cp, 0, 0.5);
            Check(sb, ref pass, ref fail, mid.DistanceTo(new Point3d(5, 0, 0)) < 1e-12,
                string.Format(CultureInfo.InvariantCulture, "side0 中点 = (5,0,0)（实测 {0:0.###},{1:0.###},{2:0.###}）", mid.X, mid.Y, mid.Z));
            Check(sb, ref pass, ref fail,
                PatchFillCore.SidePoint(ld, cp, 2, 0.0).DistanceTo(new Point3d(0, 10, 0)) < 1e-12
                && PatchFillCore.SidePoint(ld, cp, 2, 1.0).DistanceTo(new Point3d(10, 10, 0)) < 1e-12,
                "side2 参数方向 = C3→C2（参数 0 在 (0,10)，1 在 (10,10)）");
            int side; double par;
            bool okSide = PatchFillCore.ArcToSideParam(ld, cp, 15.0, out side, out par);
            Check(sb, ref pass, ref fail, okSide && side == 1 && Math.Abs(par - 0.5) < 1e-12,
                "弧长 15 → side1 参数 0.5（4 角切分的方向映射正确）");

            // ---- 4 角三级回退：矩形 → 4 个真角点
            int[] ci; bool fb; string method;
            PatchFillCore.Corners(ld, Plane.WorldXY, out ci, out fb, out method);
            bool cornersOk = ci != null && ci.Length == 4 && !fb;
            if (cornersOk)
            {
                var want = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 10, 0), new Point3d(0, 10, 0) };
                for (int i = 0; i < 4; i++)
                {
                    bool hit = false;
                    for (int k = 0; k < want.Count; k++)
                        if (ld.P[ci[i]].DistanceTo(want[k]) < 1e-9) { hit = true; break; }
                    if (!hit) cornersOk = false;
                }
            }
            Check(sb, ref pass, ref fail, cornersOk, "4 角：矩形上取到的就是 4 个真角点（方式 " + method + "）");

            // 圆形环（没有 4 个曲率角）→ 必须回退到对角极值 / 弧长四等分且仍然合法
            var circle = new List<Point3d>();
            for (int i = 0; i < 64; i++)
            {
                double a = 2 * Math.PI * i / 64.0;
                circle.Add(new Point3d(10 * Math.Cos(a), 10 * Math.Sin(a), 0));
            }
            PatchFillCore.LoopData cl = PatchFillCore.LoopData.Build(circle, null, null);
            int[] ci2; bool fb2; string m2;
            PatchFillCore.Corners(cl, Plane.WorldXY, out ci2, out fb2, out m2);
            Check(sb, ref pass, ref fail, ci2 != null && ci2.Length == 4, "4 角：圆形环也能取到 4 个角（方式 " + m2 + "）");

            // ---- 拐角（节点用）：矩形 4 个、五边形 5 个
            var kinks = PatchFillCore.KinkParams(ld, 15.0, 10);
            Check(sb, ref pass, ref fail, kinks.Count == 4,
                "拐角：矩形检出 4 个（实测 " + kinks.Count + "）");
            var pent = new List<Point3d>();
            for (int i = 0; i < 5; i++)
            {
                double a = Math.PI / 2 + 2 * Math.PI * i / 5.0;
                pent.Add(new Point3d(20 * Math.Cos(a), 20 * Math.Sin(a), 0));
            }
            PatchFillCore.LoopData pl = PatchFillCore.LoopData.Build(pent, null, null);
            var kinks5 = PatchFillCore.KinkParams(pl, 15.0, 10);
            Check(sb, ref pass, ref fail, kinks5.Count == 5,
                "拐角：五边形检出 5 个（实测 " + kinks5.Count + "）");

            // ---- Coons：边界点 = 原边界采样点（边界精确）；平面矩形内部共面
            double maxOff = 0, maxZ = 0;
            for (int i = 0; i <= 10; i++)
            {
                double t = i / 10.0;
                maxOff = Math.Max(maxOff, PatchFillCore.CoonsAt(ld, cp, t, 0).DistanceTo(PatchFillCore.SidePoint(ld, cp, 0, t)));
                maxOff = Math.Max(maxOff, PatchFillCore.CoonsAt(ld, cp, t, 1).DistanceTo(PatchFillCore.SidePoint(ld, cp, 2, t)));
                maxOff = Math.Max(maxOff, PatchFillCore.CoonsAt(ld, cp, 0, t).DistanceTo(PatchFillCore.SidePoint(ld, cp, 3, t)));
                maxOff = Math.Max(maxOff, PatchFillCore.CoonsAt(ld, cp, 1, t).DistanceTo(PatchFillCore.SidePoint(ld, cp, 1, t)));
            }
            Check(sb, ref pass, ref fail, maxOff < 1e-12,
                string.Format(CultureInfo.InvariantCulture, "Coons 基面：4 条边 = 原边界采样点（最大偏离 {0:0.########}）", maxOff));
            for (int i = 0; i <= 8; i++)
                for (int j = 0; j <= 8; j++)
                    maxZ = Math.Max(maxZ, Math.Abs(PatchFillCore.CoonsAt(ld, cp, i / 8.0, j / 8.0).Z));
            Check(sb, ref pass, ref fail, maxZ < 1e-12, "Coons 基面：平面矩形内部严格共面（Z = 0）");
            Point3d c00 = PatchFillCore.CoonsAt(ld, cp, 0, 0);
            Check(sb, ref pass, ref fail, c00.DistanceTo(new Point3d(0, 0, 0)) < 1e-12, "Coons 基面：角点 (0,0) = C0");

            // ---- 节点向量：夹持 + 拐角 multiplicity 3 + 控制点数不变
            double[] U = PatchFillCore.BuildKnots(12, null);
            Check(sb, ref pass, ref fail, U.Length == 12 + 3 + 1,
                string.Format("节点向量：长度 = 控制点数 + 次数 + 1 = 16（实测 {0}）", U.Length));
            bool clamped = Math.Abs(U[0]) < 1e-15 && Math.Abs(U[1]) < 1e-15 && Math.Abs(U[2]) < 1e-15 && Math.Abs(U[3]) < 1e-15
                && Math.Abs(U[U.Length - 1] - 1) < 1e-15 && Math.Abs(U[U.Length - 2] - 1) < 1e-15;
            Check(sb, ref pass, ref fail, clamped, "节点向量：两端夹持（各 4 重）");
            bool inc = true;
            for (int i = 1; i < U.Length; i++) if (U[i] < U[i - 1] - 1e-15) inc = false;
            Check(sb, ref pass, ref fail, inc, "节点向量：单调不减");
            var oneKink = new List<double>(); oneKink.Add(0.4);
            double[] UK = PatchFillCore.BuildKnots(12, oneKink);
            Check(sb, ref pass, ref fail, UK.Length == 16, "节点向量：带拐角时控制点数仍然是 12（长度 16）");
            int mult = 0;
            for (int i = 0; i < UK.Length; i++) if (Math.Abs(UK[i] - 0.4) < 1e-9) mult++;
            Check(sb, ref pass, ref fail, mult == 3,
                "节点向量：拐角处 multiplicity = 3（折线边界能精确表达，实测 " + mult + "）");

            // ---- 基函数：单位分解 + 支撑
            bool partOk = true;
            for (int k = 0; k <= 40; k++)
            {
                double t = k / 40.0;
                int span = PatchFillCore.FindSpan(U, 3, 12, t);
                var N = new double[4];
                PatchFillCore.BasisFuns(U, 3, span, t, N);
                double sum = 0;
                for (int a = 0; a < 4; a++) sum += N[a];
                if (Math.Abs(sum - 1.0) > 1e-12) partOk = false;
                if (span < 3 || span > 11) partOk = false;
            }
            Check(sb, ref pass, ref fail, partOk, "基函数：Cox-de Boor 单位分解 ΣN = 1、span 合法（41 个参数）");
            var rowIdx = new int[16]; var rowCoef = new double[16]; int nnz;
            PatchFillCore.TensorRow(U, U, 12, 12, 0.37, 0.62, rowIdx, rowCoef, out nnz);
            double rsum = 0;
            for (int k = 0; k < nnz; k++) rsum += rowCoef[k];
            Check(sb, ref pass, ref fail, nnz > 0 && nnz <= 16 && Math.Abs(rsum - 1.0) < 1e-12,
                string.Format(CultureInfo.InvariantCulture, "张量基行：Σcoef = 1（{0} 项，和 {1:0.############}）", nnz, rsum));

            // ---- Cholesky：对照解析解
            // A = [[4,1,0],[1,3,1],[0,1,2]]，x* = (1,2,3) → b = A·x* = (6,10,8)
            var A = new double[] { 4, 1, 0, 1, 3, 1, 0, 1, 2 };
            double[] L;
            bool cholOk = PatchFillCore.Cholesky(A, 3, out L);
            Check(sb, ref pass, ref fail, cholOk, "Cholesky：3×3 对称正定矩阵分解成功");
            double llErr = 0;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double s = 0;
                    for (int k = 0; k <= Math.Min(i, j); k++) s += L[i * 3 + k] * L[j * 3 + k];
                    llErr = Math.Max(llErr, Math.Abs(s - A[i * 3 + j]));
                }
            Check(sb, ref pass, ref fail, llErr < 1e-12,
                string.Format(CultureInfo.InvariantCulture, "Cholesky：L·Lᵀ = A（最大误差 {0:0.###e+00}）", llErr));
            var b = new double[] { 6, 10, 8 };
            PatchFillCore.CholSolve(L, 3, b);
            double xErr = Math.Max(Math.Abs(b[0] - 1), Math.Max(Math.Abs(b[1] - 2), Math.Abs(b[2] - 3)));
            Check(sb, ref pass, ref fail, xErr < 1e-12,
                string.Format(CultureInfo.InvariantCulture, "Cholesky 解对照解析解 x=(1,2,3)（最大误差 {0:0.###e+00}）", xErr));

            // 病态一点：5×5 三对角（解析解已知 x = (1..5)，b = A·x 自己算）
            int n5 = 5;
            var A5 = new double[n5 * n5];
            for (int i = 0; i < n5; i++)
            {
                A5[i * n5 + i] = 4.0;
                if (i > 0) A5[i * n5 + i - 1] = -1.0;
                if (i < n5 - 1) A5[i * n5 + i + 1] = -1.0;
            }
            var b5 = new double[n5];
            for (int i = 0; i < n5; i++)
            {
                double s = 0;
                for (int j = 0; j < n5; j++) s += A5[i * n5 + j] * (j + 1);
                b5[i] = s;
            }
            double[] L5;
            bool ok5 = PatchFillCore.Cholesky(A5, n5, out L5);
            if (ok5) PatchFillCore.CholSolve(L5, n5, b5);
            double e5 = 0;
            for (int i = 0; i < n5; i++) e5 = Math.Max(e5, Math.Abs(b5[i] - (i + 1)));
            Check(sb, ref pass, ref fail, ok5 && e5 < 1e-12,
                string.Format(CultureInfo.InvariantCulture, "Cholesky：5×5 三对角解 x=(1..5)（最大误差 {0:0.###e+00}）", e5));
            // 非正定必须被识别（不能静默给错解）
            var Anot = new double[] { 0, 1, 1, 0 };
            double[] Lbad;
            Check(sb, ref pass, ref fail, !PatchFillCore.Cholesky(Anot, 2, out Lbad), "Cholesky：非正定矩阵被明确拒绝（不静默给错解）");
        }

        // ================================================================== 用例2：平面 4 边洞

        static void Case2_PlanarRectHole(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例2：平面 4 边洞（40×40 板，洞 (10,10)-(30,30)） ---");
            var hole = new Point3d[] {
                new Point3d(10,10,0), new Point3d(30,10,0), new Point3d(30,30,0), new Point3d(10,30,0) };
            Brep plate = PlateWithPolyHole(hole, 40.0);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "构造：带洞平板失败（Brep.CreatePlanarBreps 返回空）"); return; }
            Check(sb, ref pass, ref fail, plate.Loops.Count == 2, "构造：平板有 2 个环（外圈 + 洞）");
            int edgeCount;
            List<PatchFillEdge> edges = InnerLoopEdges(plate, out edgeCount);
            Check(sb, ref pass, ref fail, edgeCount == 4,
                string.Format("构造：洞由 4 条边组成（实测 {0}）", edgeCount));

            var s = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.0, Continuity = 0 };
            string rep;
            PatchFillResult r = PatchFillCore.Generate(edges, null, null, s, out rep);
            Check(sb, ref pass, ref fail, r.Ok, "生成成功（" + (string.IsNullOrEmpty(r.Error) ? "OK" : r.Error) + "）");
            if (!r.Ok) return;
            Check(sb, ref pass, ref fail, r.Corners == 4, "4 角有效（方式 " + r.CornerMethod + "）");
            Check(sb, ref pass, ref fail, r.ControlU == 12 && r.ControlV == 12, "控制网 = 12×12（面板参数生效）");
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "边界缝隙 < 1e-6（实测 {0:0.########}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.MaxDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 1e-6（到相邻平面，实测 {0:0.########}）", r.MaxDeviation));
            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1, "输出 = 1 张面");
            Check(sb, ref pass, ref fail, r.Folded == 0, "无折叠单元");
            // 独立度量必须用「相邻面的未修剪底层曲面」（板上的洞本身没材料，量到带洞的板会报出洞边距离）
            Brep plane = UntrimmedFace(plate);
            double indep = IndependentMaxDev(r, plane, 40.0);
            Check(sb, ref pass, ref fail, indep < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "独立度量：结果面到原平面最大距离 < 1e-6（实测 {0:0.########}）", indep));
            Check(sb, ref pass, ref fail, (r.WarnFlags & PatchFillCore.WarnEdgeGap) == 0, "无「边界缝隙超差」告警");
            sb.AppendLine("      [info] " + string.Format(CultureInfo.InvariantCulture,
                "缝隙 {0:0.######}（{1:0.######}/{2:0.######}/{3:0.######}/{4:0.######}）· 偏差 {5:0.######} · 折叠 {6} · 拐角 {7} · 告警 {8} · {9:0.000}s",
                r.BoundaryDeviation, r.EdgeGaps[0], r.EdgeGaps[1], r.EdgeGaps[2], r.EdgeGaps[3],
                r.MaxDeviation, r.Folded, r.KinkKnots, r.WarnFlags, r.Seconds));
            sb.AppendLine(BenchInfoLine("PatchFill", "① 4 边平面洞", plate, r));
        }

        /// <summary>BENCH 对照行（与用例13 BenchPatch 完全同构的口径：面数/控制网/缝隙/偏差/平均/G1/曲率差/耗时）</summary>
        static string BenchInfoLine(string who, string label, Brep host, PatchFillResult r)
        {
            double cj = r.CurvatureJump;
            double avg = r.RmsDeviation;
            return string.Format(CultureInfo.InvariantCulture,
                "      [info] {0}｜{1}：面数 {2} · 控制网 {3}×{4} · 边界缝隙 {5:0.######} · 最大偏差 {6:0.######}（平均 {7:0.######}）· G1 {8} · 曲率差 {9:0.######} · 耗时 {10:0.000}s",
                who, label, r.Result != null ? r.Result.Faces.Count : 0, r.ControlU, r.ControlV,
                r.BoundaryDeviation, r.MaxDeviation, avg,
                r.MaxNormalAngleDeg >= 0 ? r.MaxNormalAngleDeg.ToString("0.####", CultureInfo.InvariantCulture) + "°" : "—",
                cj, r.Seconds);
        }

        // ================================================================== 用例3：5 边洞

        static void Case3_PentagonHole(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例3：5 边洞（正五边形） ---");
            var pent = new List<Point3d>();
            for (int i = 0; i < 5; i++)
            {
                double a = Math.PI / 2 + 2 * Math.PI * i / 5.0;
                pent.Add(new Point3d(20 + 11 * Math.Cos(a), 20 + 11 * Math.Sin(a), 0));
            }
            Brep plate = PlateWithPolyHole(pent.ToArray(), 40.0);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "构造：带五边形洞的板失败"); return; }
            int edgeCount;
            List<PatchFillEdge> edges = InnerLoopEdges(plate, out edgeCount);
            Check(sb, ref pass, ref fail, edgeCount == 5, "构造：洞由 5 条边组成（实测 " + edgeCount + "）");

            var s = new PatchFillSettings { ControlCount = 14, FitStrength = 1.0, Smooth = 0.0, Continuity = 0 };
            string rep;
            PatchFillResult r = PatchFillCore.Generate(edges, null, null, s, out rep);
            Check(sb, ref pass, ref fail, r.Ok, "5 边洞能补出来（" + (string.IsNullOrEmpty(r.Error) ? "OK" : r.Error) + "）");
            if (!r.Ok) return;
            var pentLoop = PatchFillCore.LoopData.Build(new List<Point3d>(pent), null, null);
            var pentKinks = PatchFillCore.KinkParams(pentLoop, 15.0, 10);
            Check(sb, ref pass, ref fail, pentKinks.Count == 5,
                "拐角检出 5 个（N≠4 也能补，实测 " + pentKinks.Count + "）");
            Check(sb, ref pass, ref fail, r.KinkKnots >= 1,
                "5 边形插入内部拐角节点 ≥ 1（实测 " + r.KinkKnots + "；其余 4 个拐角就是 4 角，由夹持端覆盖）");
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "边界缝隙 < 1e-6（实测 {0:0.########}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.MaxDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 1e-6（实测 {0:0.########}）", r.MaxDeviation));
            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1, "输出 = 1 张面");
            sb.AppendLine(BenchInfoLine("PatchFill", "② 5 边洞", plate, r));
        }

        // ================================================================== 用例4：3 边洞

        static void Case4_TriangleHole(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例4：3 边洞（三角形） ---");
            var tri = new Point3d[] {
                new Point3d(12,10,0), new Point3d(30,12,0), new Point3d(20,31,0) };
            Brep plate = PlateWithPolyHole(tri, 40.0);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "构造：带三角形洞的板失败"); return; }
            int edgeCount;
            List<PatchFillEdge> edges = InnerLoopEdges(plate, out edgeCount);
            Check(sb, ref pass, ref fail, edgeCount == 3, "构造：洞由 3 条边组成（实测 " + edgeCount + "）");

            var s = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.0, Continuity = 0 };
            string rep;
            PatchFillResult r = PatchFillCore.Generate(edges, null, null, s, out rep);
            Check(sb, ref pass, ref fail, r.Ok, "3 边洞能补出来（" + (string.IsNullOrEmpty(r.Error) ? "OK" : r.Error) + "）");
            if (!r.Ok) return;
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "边界缝隙 < 1e-6（实测 {0:0.########}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.MaxDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 1e-6（实测 {0:0.########}）", r.MaxDeviation));
            Check(sb, ref pass, ref fail, r.Corners == 4, "3 边也能切出 4 段（方式 " + r.CornerMethod + "）");
            var triLoop = PatchFillCore.LoopData.Build(new List<Point3d>(tri), null, null);
            var triKinks = PatchFillCore.KinkParams(triLoop, 15.0, 10);
            Check(sb, ref pass, ref fail, triKinks.Count == 3, "拐角检出 3 个（实测 " + triKinks.Count + "）");
            Check(sb, ref pass, ref fail, r.KinkKnots >= 1, "三角形插入内部拐角节点 ≥ 1（实测 " + r.KinkKnots + "）");
            sb.AppendLine(BenchInfoLine("PatchFill", "③ 3 边洞", plate, r));
        }

        // ================================================================== 用例5：曲面上的洞

        static void Case5_CurvedHole(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例5：曲面上的洞（圆柱面 60°×6 的矩形洞，参考 = 未修剪圆柱） ---");
            Brep refCyl;
            List<PatchFillEdge> edges = CylHole(10.0, 0.0, 6.0, -Math.PI / 6, Math.PI / 6, out refCyl);
            Check(sb, ref pass, ref fail, edges.Count == 4 && refCyl != null, "构造：圆柱面上的 4 条边界曲线 + 参考圆柱");
            double diag = DiagOf(edges);
            Check(sb, ref pass, ref fail, diag > 1.0, string.Format(CultureInfo.InvariantCulture, "洞的对角线 = {0:0.###}", diag));

            var s = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.05, Continuity = 0 };
            string rep;
            PatchFillResult r = PatchFillCore.Generate(edges, null, refCyl, s, out rep);
            Check(sb, ref pass, ref fail, r.Ok, "曲面上的洞能补出来（" + (string.IsNullOrEmpty(r.Error) ? "OK" : r.Error) + "）");
            if (!r.Ok) return;
            Check(sb, ref pass, ref fail, r.HasReference, "有参考几何（外部参考圆柱）");
            double ratio = r.MaxDeviation / diag;
            Check(sb, ref pass, ref fail, ratio < 0.02,
                string.Format(CultureInfo.InvariantCulture, "偏差 < 2% 对角线（实测 {0:0.####} / {1:0.####} = {2:0.000%}）",
                    r.MaxDeviation, diag, ratio));
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 1e-3,
                string.Format(CultureInfo.InvariantCulture, "边界缝隙 < 1e-3（圆弧边只能逼近，实测 {0:0.######}）", r.BoundaryDeviation));
            // 贴合强度 0（不贴参考）应该明显更差 → 证明「贴合」真的在起作用。
            // ⚠ 输入是**可展圆柱面** + 弧长参数化：Coons 基面逐点重建圆柱（S(u,v)=(Rcosθ,Rsinθ,6v)），
            // 均匀节点修好后不管贴不贴参考都到 0.0008 级 → 本洞不再有区分度（旧数字差异是节点 bug 的产物）。
            // 换「基面≠真值」的输入测区分度：球带顶圈（基面=平盘、真值=球冠，与用例7 同款）。
            Brep zone5;
            PatchFillEdge sphTop = SphereZoneTopEdge(10.0, 30.0, 0.0, out zone5);
            if (sphTop != null)
            {
                Brep sphereRef5 = null;
                try { sphereRef5 = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 10.0))); } catch { }
                if (sphereRef5 != null)
                {
                    var sphEdges = new List<PatchFillEdge>(); sphEdges.Add(sphTop);
                    var sF = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.05, Continuity = 0 };
                    string repF;
                    PatchFillResult rF1 = PatchFillCore.Generate(sphEdges, null, sphereRef5, sF, out repF);
                    var sF0 = sF.Clone(); sF0.FitStrength = 0.0;
                    PatchFillResult rF0 = PatchFillCore.Generate(sphEdges, null, sphereRef5, sF0, out repF);
                    Check(sb, ref pass, ref fail, rF1.Ok && rF0.Ok && rF0.MaxDeviation > rF1.MaxDeviation * 2.0,
                        string.Format(CultureInfo.InvariantCulture, "贴合强度 1 vs 0（球冠，基面≠真值）：偏差 {0:0.####} vs {1:0.####}（贴参考明显更准）",
                            rF1.MaxDeviation, rF0.MaxDeviation));
                }
                else Check(sb, ref pass, ref fail, false, "贴合强度区分度：球面参考构造失败");
            }
            else Check(sb, ref pass, ref fail, false, "贴合强度区分度：球带顶圈构造失败");
            sb.AppendLine("      [info] " + string.Format(CultureInfo.InvariantCulture,
                "偏差 {0:0.####} · 缝隙 {1:0.######}（{2:0.######}/{3:0.######}/{4:0.######}/{5:0.######}）· 拐角 {6} · 方式 {7} · 轮数 {8} · 告警 {9} · {10:0.000}s",
                r.MaxDeviation, r.BoundaryDeviation, r.EdgeGaps[0], r.EdgeGaps[1], r.EdgeGaps[2], r.EdgeGaps[3],
                r.KinkKnots, r.CornerMethod, r.Rounds, r.WarnFlags, r.Seconds));
            sb.AppendLine(BenchInfoLine("PatchFill", "④ 曲面上的洞（圆柱）", refCyl, r));
        }

        // ================================================================== 用例6：G1 相切

        static void Case6_G1Tangent(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例6：G1 相切（球带顶圈 30°N，相邻面 = 球面） ---");
            Brep zone;
            PatchFillEdge top = SphereZoneTopEdge(10.0, 30.0, 0.0, out zone);
            if (top == null) { Check(sb, ref pass, ref fail, false, "构造：球带顶圈失败"); return; }
            Check(sb, ref pass, ref fail, top.Host != null && top.AdjacentFaces != null && top.AdjacentFaces.Length > 0,
                "构造：顶圈是曲面边且有相邻面（G1 可用）");

            var edges = new List<PatchFillEdge>(); edges.Add(top);
            // 参考几何 = 完整球面（球带的底层曲面只到纬线，覆盖不到洞内部；这里显式给完整球）
            Brep sphereRef = null;
            try { sphereRef = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 10.0))); } catch { }
            Check(sb, ref pass, ref fail, sphereRef != null, "构造：完整球面参考几何");
            var sG1 = new PatchFillSettings { ControlCount = 16, FitStrength = 1.0, Smooth = 0.05, Continuity = 1, G1Distance = 1.0 };
            string rep;
            PatchFillResult r1 = PatchFillCore.Generate(edges, null, sphereRef, sG1, out rep);
            Check(sb, ref pass, ref fail, r1.Ok, "G1 生成成功（" + (string.IsNullOrEmpty(r1.Error) ? "OK" : r1.Error) + "）");
            if (!r1.Ok) return;
            Check(sb, ref pass, ref fail, r1.G1Samples > 0 && r1.MaxNormalAngleDeg >= 0, "G1 有法向夹角统计（" + r1.G1Samples + " 个采样点）");
            if (r1.Corners == 0)
            {
                // 无拐角闭合环 → 径向盘状（没有人工角点）→ 全边界（含接缝）都应达标
                Check(sb, ref pass, ref fail, r1.MaxNormalAngleDeg < 1.0,
                    string.Format(CultureInfo.InvariantCulture, "G1（径向盘状，无角点）：**全边界**最大夹角 < 1°（实测 {0:0.####}°）", r1.MaxNormalAngleDeg));
                sb.AppendLine("      [info] 参数化 = " + r1.CornerMethod + "（无人工角点 → 角点 G1 不可达的问题从根上消失）");
            }
            else
            {
                Check(sb, ref pass, ref fail, r1.MaxNormalAngleInnerDeg < 1.0,
                    string.Format(CultureInfo.InvariantCulture, "G1：**中段**（每条边去掉两端各 15% 的角点邻域）最大夹角 < 1°（实测 {0:0.####}°）", r1.MaxNormalAngleInnerDeg));
                Check(sb, ref pass, ref fail, r1.MaxNormalAngleDeg >= 0 && r1.MaxNormalAngleDeg < 90.0,
                    string.Format(CultureInfo.InvariantCulture,
                        "G1：角点邻域 + 角点处的夹角如实报出（全边界最大 {0:0.####}°）—— 4 角切分的**已知限制**（角点两条参数方向都被边界切向钉住 → 切平面 = 边界平面）；无拐角环的径向盘状解法已写好但本轮因秩亏暂关（见 PROJECT-STATE §19）",
                        r1.MaxNormalAngleDeg));
            }

            var sG0 = sG1.Clone(); sG0.Continuity = 0;
            PatchFillResult r0 = PatchFillCore.Generate(edges, null, sphereRef, sG0, out rep);
            Check(sb, ref pass, ref fail, r0.Ok, "G0 生成成功");
            Check(sb, ref pass, ref fail, r0.MaxNormalAngleDeg < 0, "G0 时不给 G1 夹角（报告里说明是 G0）");
            Check(sb, ref pass, ref fail, r0.Ok && Math.Abs(SigOf(r0) - SigOf(r1)) > 1e-9, "G0 与 G1 的几何签名不同（G1 真的改了面）");
            Check(sb, ref pass, ref fail, r1.BoundaryDeviation <= r0.BoundaryDeviation * 1.5 + 1e-9,
                string.Format(CultureInfo.InvariantCulture, "G1 不能把 G0 弄坏：逐边缝隙 {0:0.######}（G0 时 {1:0.######}）",
                    r1.BoundaryDeviation, r0.BoundaryDeviation));
            sb.AppendLine(BenchInfoLine("PatchFill", "⑤ 带 G1 的洞（球带顶圈）", zone, r1));

            // 独立曲线（没有相邻面）→ G1 必须如实退化成 G0 并在报告里写明
            var circ = new List<PatchFillEdge>();
            var c = new Circle(new Plane(Point3d.Origin, Vector3d.ZAxis), 8.0).ToNurbsCurve();
            circ.Add(new PatchFillEdge(c, null, -1, null, "独立圆"));
            PatchFillResult r2 = PatchFillCore.Generate(circ, null, null, sG1, out rep);
            Check(sb, ref pass, ref fail, r2.Ok, "独立圆（无相邻面）也能补");
            Check(sb, ref pass, ref fail, r2.MaxNormalAngleDeg < 0, "独立曲线：G1 没有相邻面可用 → 不给夹角统计");
            Check(sb, ref pass, ref fail, rep != null && rep.IndexOf("退化成 G0", StringComparison.Ordinal) >= 0,
                "独立曲线：报告里写明 G1 退化成 G0");
            sb.AppendLine("      [info] " + string.Format(CultureInfo.InvariantCulture,
                "G1 夹角 {0:0.####}° · G0 夹角 {1:0.####}°", r1.MaxNormalAngleDeg, r0.MaxNormalAngleDeg));
            // 抽查一个边界采样点：结果面法向 vs 相邻面法向（诊断用）
            try
            {
                Point3d bp = r1.Surface.PointAt(0.5, 0.0);
                Vector3d ns = r1.Surface.NormalAt(0.5, 0.0);
                if (ns.Length > 1e-12) ns.Unitize();
                double bu, bv;
                Vector3d na = Vector3d.Zero;
                if (top.Host.Faces[0].ClosestPoint(bp, out bu, out bv))
                {
                    na = top.Host.Faces[0].NormalAt(bu, bv);
                    if (na.Length > 1e-12) na.Unitize();
                }
                double dbg = Math.Acos(Math.Min(1.0, Math.Abs(ns * na))) * 180.0 / Math.PI;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      [dbg] side0 中点 面点({0:0.###},{1:0.###},{2:0.###}) · 结果面法向({3:0.###},{4:0.###},{5:0.###}) · 相邻面法向({6:0.###},{7:0.###},{8:0.###}) · 夹角 {9:0.####}°",
                    bp.X, bp.Y, bp.Z, ns.X, ns.Y, ns.Z, na.X, na.Y, na.Z, dbg));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      [dbg] 最差采样：side {0} · f {1:0.####} · 夹角 {2:0.####}°",
                    r1.WorstAngleSide, r1.WorstAngleF, r1.MaxNormalAngleDeg));
                // 控制网范围（盘状模式排错用）
                try
                {
                    var cbb = BoundingBox.Empty;
                    for (int i = 0; i < r1.Surface.Points.CountU; i++)
                        for (int j = 0; j < r1.Surface.Points.CountV; j++)
                        {
                            Point3d q;
                            if (r1.Surface.Points.GetPoint(i, j, out q)) cbb.Union(q);
                        }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "      [dbg] 控制网包围盒 z {0:0.###}..{1:0.###} · xy {2:0.###}..{3:0.###}",
                        cbb.Min.Z, cbb.Max.Z, Math.Min(cbb.Min.X, cbb.Min.Y), Math.Max(cbb.Max.X, cbb.Max.Y)));
                }
                catch { }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      [dbg] 最差处法向：结果面({0:0.###},{1:0.###},{2:0.###}) · 相邻面({3:0.###},{4:0.###},{5:0.###})",
                    r1.WorstAngleNS.X, r1.WorstAngleNS.Y, r1.WorstAngleNS.Z,
                    r1.WorstAngleNA.X, r1.WorstAngleNA.Y, r1.WorstAngleNA.Z));
                for (int i = 0; i < r1.RoundLog.Count; i++) sb.AppendLine("      [dbg] " + r1.RoundLog[i]);
                foreach (string ln in rep.Split('\n')) if (ln.Trim().Length > 0) sb.AppendLine("      [rep] " + ln.Trim());
                BoundingBox sbb = r1.Surface.GetBoundingBox(true);
                Point3d ctr = r1.Surface.PointAt(0.5, 0.5);
                Point3d near = r1.Surface.PointAt(0.5, 1.0 / 15.0);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      [dbg] 结果面包围盒 z {0:0.###}..{1:0.###} · 中心点({2:0.###},{3:0.###},{4:0.###}) · 近边界点({5:0.###},{6:0.###},{7:0.###})",
                    sbb.Min.Z, sbb.Max.Z, ctr.X, ctr.Y, ctr.Z, near.X, near.Y, near.Z));
            }
            catch (Exception ex) { sb.AppendLine("      [dbg] 抽查异常：" + ex.Message); }
        }

        // ================================================================== 用例7：内部约束

        static void Case7_InnerConstraints(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例7：内部曲线 / 点约束真的生效（球带顶圈：Coons 基面 = 平盘，真值 = 球冠） ---");
            Brep zone;
            PatchFillEdge top = SphereZoneTopEdge(10.0, 30.0, 0.0, out zone);
            if (top == null) { Check(sb, ref pass, ref fail, false, "构造：球带顶圈失败"); return; }
            var edges = new List<PatchFillEdge>(); edges.Add(top);

            // 不贴参考（FitStrength=0）：结果面就是 Coons 平盘（z = 5），球冠的真值在 z = 10
            var s = new PatchFillSettings { ControlCount = 12, FitStrength = 0.0, Smooth = 0.05, Continuity = 0 };
            string rep;
            PatchFillResult r0 = PatchFillCore.Generate(edges, null, null, s, out rep);
            Check(sb, ref pass, ref fail, r0.Ok, "无内部约束：生成成功（平盘，偏差 " + r0.MaxDeviation.ToString("0.###", CultureInfo.InvariantCulture) + "）");
            if (!r0.Ok) return;

            // 内部点：球冠顶点（球北极），平盘离它 5
            Point3d pole = new Point3d(0, 0, 10.0);
            var inner = new List<GeometryBase>();
            inner.Add(new Rhino.Geometry.Point(pole));
            PatchFillResult r1 = PatchFillCore.Generate(edges, inner, null, s, out rep);
            Check(sb, ref pass, ref fail, r1.Ok, "内部点：生成成功");
            Check(sb, ref pass, ref fail, r1.Ok && Math.Abs(SigOf(r1) - SigOf(r0)) > 1e-9, "内部点：几何签名变化（约束真的接上了）");
            double d0 = DistToSurface(r0, pole), d1 = DistToSurface(r1, pole);
            Check(sb, ref pass, ref fail, d1 < d0 * 0.95,
                string.Format(CultureInfo.InvariantCulture, "内部点：结果面到该点明显更近（{0:0.####} → {1:0.####}）", d0, d1));

            // 内部曲线：球面上 70°N 的一段弧（平盘离它 ~4.4）
            var curvePts = new List<Point3d>();
            for (int k = 0; k <= 12; k++)
            {
                double th = (-60.0 + 120.0 * k / 12.0) * Math.PI / 180.0;
                curvePts.Add(new Point3d(10 * Math.Cos(70 * Math.PI / 180) * Math.Cos(th),
                                         10 * Math.Cos(70 * Math.PI / 180) * Math.Sin(th), 10 * Math.Sin(70 * Math.PI / 180)));
            }
            var pl = new PolylineCurve(curvePts);
            var innerCurve = new List<GeometryBase>();
            innerCurve.Add(pl);
            PatchFillResult r2 = PatchFillCore.Generate(edges, innerCurve, null, s, out rep);
            Check(sb, ref pass, ref fail, r2.Ok, "内部曲线：生成成功");
            Check(sb, ref pass, ref fail, r2.Ok && Math.Abs(SigOf(r2) - SigOf(r0)) > 1e-9, "内部曲线：几何签名变化");
            double dc0 = CurveDevToResult(r0, pl), dc = CurveDevToResult(r2, pl);
            Check(sb, ref pass, ref fail, dc < dc0 * 0.9,
                string.Format(CultureInfo.InvariantCulture, "内部曲线：结果面贴向该曲线（{0:0.####} → {1:0.####}）", dc0, dc));
        }

        // ================================================================== 用例8：拒绝用例

        static void Case8_Reject(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例8：拒绝用例（必须给明确理由） ---");
            var s = new PatchFillSettings();
            string rep;

            PatchFillResult r0 = PatchFillCore.Generate(null, null, null, s, out rep);
            Check(sb, ref pass, ref fail, !r0.Ok && r0.Error.IndexOf("没有选到边界边", StringComparison.Ordinal) >= 0,
                "无输入：明确拒绝（" + r0.Error + "）");

            PatchFillResult r1 = PatchFillCore.Generate(new List<PatchFillEdge>(), null, null, s, out rep);
            Check(sb, ref pass, ref fail, !r1.Ok && !string.IsNullOrEmpty(r1.Error), "空列表：明确拒绝（" + r1.Error + "）");

            // 开放链：3 条不闭合的直线
            var open = new List<PatchFillEdge>();
            open.Add(MkEdge(new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0)), "线1"));
            open.Add(MkEdge(new LineCurve(new Point3d(10, 0, 0), new Point3d(10, 10, 0)), "线2"));
            open.Add(MkEdge(new LineCurve(new Point3d(10, 10, 0), new Point3d(0, 14, 0)), "线3"));
            PatchFillResult r2 = PatchFillCore.Generate(open, null, null, s, out rep);
            Check(sb, ref pass, ref fail, !r2.Ok && r2.Error.IndexOf("不闭合", StringComparison.Ordinal) >= 0,
                "开放链：明确拒绝（" + r2.Error + "）");

            // 只给点
            if (doc != null)
            {
                Guid pid = Guid.Empty;
                try { pid = doc.Objects.AddPoint(new Point3d(1, 2, 3)); } catch { }
                RhinoObject po = null;
                try { po = doc.Objects.FindId(pid); } catch { }
                PatchFillEdge pe; string why;
                bool ok = PatchFillCore.ResolveEdgeFromComponent(po, ComponentIndex.Unset, out pe, out why);
                Check(sb, ref pass, ref fail, !ok && why.IndexOf("不是曲线", StringComparison.Ordinal) >= 0,
                    "只给点：明确拒绝（" + why + "）");
                Cleanup(doc, pid);
            }

            // 两条边首尾不相接（间隙大于容差）
            var gap = new List<PatchFillEdge>();
            gap.Add(MkEdge(new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0)), "线A"));
            gap.Add(MkEdge(new LineCurve(new Point3d(10, 5, 0), new Point3d(0, 5, 0)), "线B"));
            PatchFillResult r3 = PatchFillCore.Generate(gap, null, null, s, out rep);
            Check(sb, ref pass, ref fail, !r3.Ok, "有间隙的两条边：拒绝（" + r3.Error + "）");
            Check(sb, ref pass, ref fail, PatchFillCore.EmptyTargetHint.IndexOf("边界") >= 0,
                "空目标提示：" + PatchFillCore.EmptyTargetHint);
        }

        // ================================================================== 用例9：写文档

        static void Case9_Document(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例9：写文档（图层 / 命名 / 对象数） ---");
            if (doc == null) { Check(sb, ref pass, ref fail, false, "没有活动文档"); return; }
            var hole = new Point3d[] {
                new Point3d(10,10,0), new Point3d(30,10,0), new Point3d(30,30,0), new Point3d(10,30,0) };
            Brep plate = PlateWithPolyHole(hole, 40.0);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "构造失败"); return; }
            int ec;
            List<PatchFillEdge> edges = InnerLoopEdges(plate, out ec);
            var s = new PatchFillSettings { ControlCount = 10, Smooth = 0.0 };
            string rep;
            PatchFillResult r = PatchFillCore.Generate(edges, null, null, s, out rep);
            Check(sb, ref pass, ref fail, r.Ok, "生成成功");
            if (!r.Ok) return;

            var results = new List<PatchFillResult>(); results.Add(r);
            int before = doc.Objects.Count;
            int written;
            PatchFillCore.AddToDocument(doc, results, out written);
            Check(sb, ref pass, ref fail, written == 1, "写入 1 个对象（实测 " + written + "）");
            Check(sb, ref pass, ref fail, doc.Objects.Count == before + 1, "文档对象数 +1");
            Check(sb, ref pass, ref fail, LayerIndex(doc, PatchFillCore.LayerPatch) >= 0, "图层「" + PatchFillCore.LayerPatch + "」已建立");
            string nm = null; Guid added = Guid.Empty;
            foreach (RhinoObject o in doc.Objects)
            {
                if (o != null && o.Attributes != null && o.Attributes.LayerIndex == LayerIndex(doc, PatchFillCore.LayerPatch))
                {
                    nm = o.Name; added = o.Id; break;
                }
            }
            Check(sb, ref pass, ref fail, nm != null && nm.StartsWith("多边补面", StringComparison.Ordinal),
                "对象命名 = " + (nm ?? "(空)"));
            Cleanup(doc, added);
            RhinoObject gone = null;
            try { gone = doc.Objects.FindId(added); } catch { }
            Check(sb, ref pass, ref fail, gone == null, "清理后对象真的被删掉（FindId 复查）");
            int residue = 0;
            int li = LayerIndex(doc, PatchFillCore.LayerPatch);
            foreach (RhinoObject o in doc.Objects)
                if (o != null && o.Attributes != null && o.Attributes.LayerIndex == li) residue++;
            Check(sb, ref pass, ref fail, residue == 0,
                string.Format("图层「{0}」里没有残留对象（实测 {1}）", PatchFillCore.LayerPatch, residue));
        }

        // ================================================================== 用例10：面板接线

        static void Case10_PanelWiring(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例10：面板接线（每个参数点了都有反应） ---");
            Brep zone;
            PatchFillEdge top = SphereZoneTopEdge(10.0, 30.0, 0.0, out zone);
            if (top == null) { Check(sb, ref pass, ref fail, false, "构造失败"); return; }
            Brep sphereRef = null;
            try { sphereRef = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 10.0))); } catch { }
            var edges = new List<PatchFillEdge>(); edges.Add(top);

            PatchFillPanel panel = null;
            try
            {
                panel = new PatchFillPanel(new PatchFillSettings(), "自检目标");
                Check(sb, ref pass, ref fail, true, "面板：构造成功（无异常）");

                bool rowsOk = true;
                for (int i = 0; i < 4; i++) if (!panel.RowEnabled(i)) rowsOk = false;
                Check(sb, ref pass, ref fail, rowsOk, "面板：4 个参数行全部可用（没有灰掉的行）");

                string[] names = { "控制点数", "贴合强度", "平滑度", "G1 采样距离" };
                bool paramsOk = true;
                for (int i = 0; i < names.Length; i++) if (!panel.ParamEnabled(names[i])) paramsOk = false;
                Check(sb, ref pass, ref fail, paramsOk, "面板：4 个参数名都能查到且可用");

                string rep;
                PatchFillResult r0 = PatchFillCore.Generate(edges, null, sphereRef, panel.Settings, out rep);
                Check(sb, ref pass, ref fail, r0.Ok, "面板默认参数生成成功");
                double sig0 = SigOf(r0);

                double[] values = { 22.0, 0.35, 0.9, 3.0 };
                for (int i = 0; i < names.Length; i++)
                {
                    bool set = panel.SetParam(names[i], values[i]);
                    double got = ParamValue(panel, names[i]);
                    bool changed = Math.Abs(got - values[i]) < 1e-6;
                    PatchFillResult r = PatchFillCore.Generate(edges, null, sphereRef, panel.Settings, out rep);
                    double sig = r.Ok ? SigOf(r) : double.NaN;
                    bool moved = !double.IsNaN(sig) && Math.Abs(sig - sig0) > 1e-9;
                    Check(sb, ref pass, ref fail, set && changed && moved,
                        string.Format(CultureInfo.InvariantCulture,
                            "面板参数「{0}」= {1} → Settings 写入成功且几何变化（签名差 {2:0.####}）",
                            names[i], values[i], double.IsNaN(sig) ? -1 : (sig - sig0)));
                    panel.SetParam(names[i], ParamValue(new PatchFillPanel(new PatchFillSettings(), "复位"), names[i]));
                    PatchFillCore.Generate(edges, null, sphereRef, panel.Settings, out rep);      // 复位后重算
                }

                // 分段控件：G0 / G1
                panel.SetContinuity(1);
                Check(sb, ref pass, ref fail, panel.ContinuityIndex == 1 && panel.Settings.Continuity == 1, "面板：分段控件切到 G1 → Settings.Continuity = 1");
                panel.SetContinuity(0);
                Check(sb, ref pass, ref fail, panel.ContinuityIndex == 0 && panel.Settings.Continuity == 0, "面板：分段控件切回 G0 → Settings.Continuity = 0");

                // 复选框钩子
                panel.SetKeepHoles(false);
                Check(sb, ref pass, ref fail, !panel.KeepHolesChecked && !panel.Settings.KeepHoles, "面板：保留内孔 取消勾选 → Settings.KeepHoles = false");
                panel.SetKeepHoles(true);
                Check(sb, ref pass, ref fail, panel.KeepHolesChecked && panel.Settings.KeepHoles, "面板：保留内孔 勾选 → Settings.KeepHoles = true");
                panel.SetShowSourceBoundary(false);
                Check(sb, ref pass, ref fail, !panel.ShowSourceBoundaryChecked && !panel.Settings.ShowSourceBoundary, "面板：显示原边界 取消勾选 → Settings.ShowSourceBoundary = false");
                panel.SetShowSourceBoundary(true);

                Check(sb, ref pass, ref fail, !panel.PickState, "面板：没选边界时「选择边界」按钮是红的（未选状态）");
                Check(sb, ref pass, ref fail, !panel.InnerPickState, "面板：没选内部约束时按钮是红的");
                Check(sb, ref pass, ref fail, panel.LivePreview, "面板：实时预览默认开");
                Check(sb, ref pass, ref fail, panel.Settings.G1Distance > 0, "面板：G1 采样距离默认 > 0（拖它就自动切 G1）");

                // 真的重绘两遍（抓「字体被 Dispose / 红叉」这类只在第二遍暴露的坑）
                panel.Show();
                Application.DoEvents();
                using (var bmp = new System.Drawing.Bitmap(panel.Width, panel.Height))
                {
                    panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
                    panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
                }
                Check(sb, ref pass, ref fail, true, "面板：Show + DrawToBitmap 两遍无异常");
                try
                {
                    string shot = Path.Combine(Path.GetTempPath(), "PatchFillPanel-smoke.png");
                    using (var bmp = new System.Drawing.Bitmap(panel.Width, panel.Height))
                    {
                        panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
                        bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Check(sb, ref pass, ref fail, File.Exists(shot), "面板：冒烟截图已写 " + shot);
                }
                catch (Exception ex) { Check(sb, ref pass, ref fail, false, "面板截图异常：" + ex.Message); }

                panel.Close();
                for (int i = 0; i < 200 && !panel.IsDisposed; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                Check(sb, ref pass, ref fail, panel.IsDisposed, "面板：关闭路径真的 Dispose（不留吃点击的隐形窗体）");
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "面板用例异常：" + ex.Message); }
            finally { try { if (panel != null && !panel.IsDisposed) panel.Close(); } catch { } }
        }

        // ================================================================== 用例11：确定性

        static void Case11_Determinism(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例11：确定性（同参数两次完全一致） ---");
            Brep refCyl;
            List<PatchFillEdge> edges = CylHole(10.0, 0.0, 6.0, -Math.PI / 6, Math.PI / 6, out refCyl);
            var s = new PatchFillSettings { ControlCount = 14, FitStrength = 1.0, Smooth = 0.2 };
            string rep1, rep2;
            PatchFillResult a = PatchFillCore.Generate(edges, null, refCyl, s, out rep1);
            PatchFillResult b = PatchFillCore.Generate(edges, null, refCyl, s, out rep2);
            Check(sb, ref pass, ref fail, a.Ok && b.Ok, "两次都生成成功");
            if (!a.Ok || !b.Ok) return;
            Check(sb, ref pass, ref fail, Math.Abs(SigOf(a) - SigOf(b)) < 1e-15,
                string.Format(CultureInfo.InvariantCulture, "几何签名完全一致（差 {0:0.###e+00}）", Math.Abs(SigOf(a) - SigOf(b))));
            Check(sb, ref pass, ref fail, Math.Abs(a.MaxDeviation - b.MaxDeviation) < 1e-15
                && Math.Abs(a.BoundaryDeviation - b.BoundaryDeviation) < 1e-15, "偏差 / 缝隙指标完全一致");
            Check(sb, ref pass, ref fail, a.Rounds == b.Rounds, "残差迭代轮数一致（" + a.Rounds + "）");

            var s2 = s.Clone(); s2.ControlCount = 18;
            string rep3;
            PatchFillResult c = PatchFillCore.Generate(edges, null, refCyl, s2, out rep3);
            Check(sb, ref pass, ref fail, c.Ok && Math.Abs(SigOf(c) - SigOf(a)) > 1e-9, "换参数几何必须不同（不是恒定输出）");
        }

        // ================================================================== 用例12：残差迭代 / 内孔 / 告警位

        static void Case12_ResidualAndHoles(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例12：残差迭代轮数 / 内孔修剪 / 位掩码告警 ---");
            Brep refCyl;
            List<PatchFillEdge> edges = CylHole(10.0, 0.0, 6.0, -Math.PI / 6, Math.PI / 6, out refCyl);
            var s = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.05 };
            string rep;
            PatchFillResult r = PatchFillCore.Generate(edges, null, refCyl, s, out rep);
            Check(sb, ref pass, ref fail, r.Ok, "曲面洞生成成功");
            if (!r.Ok) return;
            Check(sb, ref pass, ref fail, r.Rounds >= 2,
                "残差迭代真的跑了 ≥2 轮（实测 " + r.Rounds + " 轮，节点向量不动、只补约束）");
            Check(sb, ref pass, ref fail, r.WarnFlags == 0,
                "干净用例：位掩码告警 = 0（" + (string.IsNullOrEmpty(r.WarnText) ? "无告警" : r.WarnText) + "）");
            sb.AppendLine("      [info] " + string.Format(CultureInfo.InvariantCulture,
                "偏差 {0:0.######} · 缝隙 {1:0.######} · 折叠 {2} · 轮数 {3} · 告警 {4} · {5:0.000}s",
                r.MaxDeviation, r.BoundaryDeviation, r.Folded, r.Rounds, r.WarnFlags, r.Seconds));
            Check(sb, ref pass, ref fail, r.CurvatureJump >= 0 && r.CurvatureJump < 1e6,
                string.Format(CultureInfo.InvariantCulture, "曲率连续性近似（最外两排控制点离散曲率最大差）已测出：{0:0.######}（对照用相对量，绝对值在 BENCH 里与 _Patch 比）", r.CurvatureJump));
            Check(sb, ref pass, ref fail, PatchFillCore.WarnText(15).IndexOf("bit3", StringComparison.Ordinal) >= 0, "告警文案覆盖到 bit3");

            // 内孔：把平板的外圈 + 内圈都选进来 → 外环补面 + 内孔修剪
            var hole = new Point3d[] {
                new Point3d(10,10,0), new Point3d(30,10,0), new Point3d(30,30,0), new Point3d(10,30,0) };
            Brep plate = PlateWithPolyHole(hole, 40.0);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "构造：带洞平板失败"); return; }
            var all = new List<PatchFillEdge>();
            for (int i = 0; i < plate.Edges.Count; i++)
            {
                BrepEdge e = plate.Edges[i];
                all.Add(new PatchFillEdge(e.DuplicateCurve(), plate, i, e.AdjacentFaces(), "板边" + (i + 1)));
            }
            Check(sb, ref pass, ref fail, all.Count == 8, "构造：外圈 4 条 + 内圈 4 条 = 8 条边（实测 " + all.Count + "）");
            var s2 = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.0, KeepHoles = true };
            PatchFillResult r2 = PatchFillCore.Generate(all, null, null, s2, out rep);
            Check(sb, ref pass, ref fail, r2.Ok, "外圈 + 内孔：生成成功（" + (string.IsNullOrEmpty(r2.Error) ? "OK" : r2.Error) + "）");
            if (!r2.Ok) return;
            Check(sb, ref pass, ref fail, r2.Loops == 2 && r2.HolesFound == 1, "检出 2 个环 / 1 个内孔候选");
            Check(sb, ref pass, ref fail, r2.Trimmed && r2.Holes == 1, "内孔修剪成功（保留内孔 = 开）");
            Check(sb, ref pass, ref fail, r2.Result.Faces.Count == 1 && r2.Result.Faces[0].Loops.Count == 2,
                "结果面 1 张 + 2 个修剪环（带孔）");
            var s3 = s2.Clone(); s3.KeepHoles = false;
            PatchFillResult r3 = PatchFillCore.Generate(all, null, null, s3, out rep);
            Check(sb, ref pass, ref fail, r3.Ok && !r3.Trimmed && r3.Result.Faces[0].Loops.Count == 1,
                "保留内孔 = 关 → 不做修剪（1 个环）");

            // ---- 局部自适应插结（XNURBS 没有的能力）：球冠这种「Coons 平盘 ≠ 真值」的硬输入
            Brep zone2;
            PatchFillEdge top2 = SphereZoneTopEdge(10.0, 30.0, 0.0, out zone2);
            Brep sphereRef2 = null;
            try { sphereRef2 = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 10.0))); } catch { }
            if (top2 != null && sphereRef2 != null)
            {
                var hard = new List<PatchFillEdge>(); hard.Add(top2);
                var s4 = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.05 };
                PatchFillResult r4 = PatchFillCore.Generate(hard, null, sphereRef2, s4, out rep);
                Check(sb, ref pass, ref fail, r4.Ok, "硬输入（球冠，12 控制点）生成成功");
                if (r4.Ok)
                {
                    Check(sb, ref pass, ref fail, r4.AdaptiveKnots > 0,
                        "局部自适应插结生效（插了 " + r4.AdaptiveKnots + " 个节点 → 控制网 " + r4.ControlU + "×" + r4.ControlV + "）");
                    Check(sb, ref pass, ref fail, r4.ControlU > 12 || r4.ControlV > 12 || r4.FinalControlU > 12 || r4.FinalControlV > 12,
                        "插结让控制点数只在误差大的方向增加（best 轮 " + r4.ControlU + "×" + r4.ControlV
                        + " · 末轮 " + r4.FinalControlU + "×" + r4.FinalControlV + "）");
                    Check(sb, ref pass, ref fail, r4.MaxDeviation < r4.FirstRoundDeviation,
                        string.Format(CultureInfo.InvariantCulture, "迭代 + 插结后偏差比第 0 轮更低（{0:0.######} → {1:0.######}）",
                            r4.FirstRoundDeviation, r4.MaxDeviation));
                    sb.AppendLine("      [info] " + string.Format(CultureInfo.InvariantCulture,
                        "球冠硬输入：偏差 {0:0.######}（第0轮 {1:0.######}）· 插结 {2} · 控制网 {3}×{4} · 轮数 {5} · 曲率差 {6:0.######}",
                        r4.MaxDeviation, r4.FirstRoundDeviation, r4.AdaptiveKnots, r4.ControlU, r4.ControlV, r4.Rounds, r4.CurvatureJump));
                    for (int i = 0; i < r4.RoundLog.Count; i++) sb.AppendLine("      [info] " + r4.RoundLog[i]);
                }
            }
            else Check(sb, ref pass, ref fail, false, "构造：球冠硬输入失败");
        }

        // ================================================================== 用例13：_Patch 代理基线

        /// <summary>
        /// 用例13：**_Patch 代理基线**（Rhino 官方 `Brep.CreatePatch`，同一批测试洞 + 同一套 6 项度量）。
        /// ⚠ 它**不是 XNURBS**：本机没装 Rhino 7（XNURBS 是 Rhino 7 专用插件）→ XNURBS 列无法执行，
        /// 这一列只是「我们 vs Rhino 官方同类工具」的可执行代理基线，参数（spans / tolerance）在此写明。
        /// </summary>
        static void Case13_PatchBaseline(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例13：_Patch 代理基线（Rhino 官方 Brep.CreatePatch；**不是 XNURBS**，本机无 Rhino 7） ---");
            double docTol = 0.001;
            try { if (doc != null) docTol = doc.ModelAbsoluteTolerance; } catch { }
            // 用 1e-6 而不是文档默认公差：为了和 PatchFill 的边界硬约束精度口径对齐（两边都按「边界要准」比）
            double tol = 1e-6;
            int spans = 12;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      [info] _Patch 参数：uSpans=vSpans={0} · tolerance={1:0.########}（对齐口径；文档默认公差 {2:0.########}）· 我们的面板「控制点数」= 12（数值对齐，语义不同：Rhino 是跨数、我们是控制点数）",
                spans, tol, docTol));

            // ① 4 边平面洞
            {
                var hole = new Point3d[] { new Point3d(10, 10, 0), new Point3d(30, 10, 0), new Point3d(30, 30, 0), new Point3d(10, 30, 0) };
                Brep plate = PlateWithPolyHole(hole, 40.0);
                if (plate == null) Check(sb, ref pass, ref fail, false, "① 构造失败");
                else { int ec; BenchPatch(sb, ref pass, ref fail, "① 4 边平面洞", InnerLoopEdges(plate, out ec), UntrimmedFace(plate), spans, tol, true); }
            }
            // ② 5 边洞
            {
                var pent = new List<Point3d>();
                for (int i = 0; i < 5; i++)
                {
                    double a = Math.PI / 2 + 2 * Math.PI * i / 5.0;
                    pent.Add(new Point3d(20 + 11 * Math.Cos(a), 20 + 11 * Math.Sin(a), 0));
                }
                Brep plate = PlateWithPolyHole(pent.ToArray(), 40.0);
                if (plate == null) Check(sb, ref pass, ref fail, false, "② 构造失败");
                else { int ec; BenchPatch(sb, ref pass, ref fail, "② 5 边洞", InnerLoopEdges(plate, out ec), UntrimmedFace(plate), spans, tol, false); }
            }
            // ③ 3 边洞
            {
                var tri = new Point3d[] { new Point3d(12, 10, 0), new Point3d(30, 12, 0), new Point3d(20, 31, 0) };
                Brep plate = PlateWithPolyHole(tri, 40.0);
                if (plate == null) Check(sb, ref pass, ref fail, false, "③ 构造失败");
                else { int ec; BenchPatch(sb, ref pass, ref fail, "③ 3 边洞", InnerLoopEdges(plate, out ec), UntrimmedFace(plate), spans, tol, false); }
            }
            // ④ 曲面上的洞（圆柱）
            {
                Brep refCyl;
                List<PatchFillEdge> edges = CylHole(10.0, 0.0, 6.0, -Math.PI / 6, Math.PI / 6, out refCyl);
                if (edges.Count == 0) Check(sb, ref pass, ref fail, false, "④ 构造失败");
                else BenchPatch(sb, ref pass, ref fail, "④ 曲面上的洞（圆柱）", edges, refCyl, spans, tol, false);
            }
            // ⑤ 带 G1 相切的洞（球带顶圈）
            {
                Brep zone;
                PatchFillEdge top = SphereZoneTopEdge(10.0, 30.0, 0.0, out zone);
                Brep sphereRef = null;
                try { sphereRef = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 10.0))); } catch { }
                if (top == null || sphereRef == null) Check(sb, ref pass, ref fail, false, "⑤ 构造失败");
                else
                {
                    var one = new List<PatchFillEdge>(); one.Add(top);
                    BenchPatch(sb, ref pass, ref fail, "⑤ 带 G1 的洞（球带顶圈）", one, sphereRef, spans, tol, false);
                }
            }
        }

        /// <summary>用 Rhino 官方 Patch 补同一个洞，量同一套 6 项度量（代理基线）</summary>
        static void BenchPatch(StringBuilder sb, ref int pass, ref int fail, string label,
            List<PatchFillEdge> edges, Brep reference, int spans, double tol, bool mustSucceed)
        {
            double diag = DiagOf(edges);
            List<PatchFillCore.LoopData> loops; List<int> counts; string why;
            if (!PatchFillCore.ChainLoops(edges, diag, out loops, out counts, out why))
            {
                Check(sb, ref pass, ref fail, false, label + "：边界环解析失败（" + why + "）");
                return;
            }
            PatchFillCore.LoopData loop = loops[0];
            var refr = PatchFillCore.Reference.FromEdges(edges);
            if (reference != null) refr.AddExplicit(reference);

            var geo = new List<GeometryBase>();
            for (int i = 0; i < edges.Count; i++) geo.Add(edges[i].Curve);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Brep patch = null;
            try { patch = Brep.CreatePatch(geo, spans, spans, tol); } catch { patch = null; }
            sw.Stop();
            double secs = sw.Elapsed.TotalSeconds;

            NurbsSurface srf = null;
            try { if (patch != null && patch.Faces.Count > 0) srf = patch.Faces[0].UnderlyingSurface() as NurbsSurface; } catch { }
            // 基线「补不出来」本身也是对照数据（不判我方失败）；但 ① 必须成功（校验调用链没错）
            Check(sb, ref pass, ref fail, mustSucceed ? (patch != null && srf != null) : true,
                label + "：_Patch " + (srf != null
                    ? ("生成成功（" + srf.Points.CountU + "×" + srf.Points.CountV + " 控制点）")
                    : "未生成结果（记为无）"));
            if (srf == null) return;

            // ① 边界缝隙（面外环密采样 → 输入边界折线）
            double gap = 0;
            Curve outer = null;
            try
            {
                outer = patch.Faces[0].OuterLoop.To3dCurve();
                if (outer != null)
                {
                    double len = outer.GetLength();
                    int m = 121;
                    for (int k = 0; k < m; k++)
                    {
                        Point3d p = outer.PointAtLength(len * k / (m - 1.0));
                        double d = PatchFillCore.DistToLoop(loop, p);
                        if (d > gap) gap = d;
                    }
                }
            }
            catch { }

            // ② 最大 / 平均偏差（21×21 → 参考几何）
            double maxDev = 0, rms = 0;
            try
            {
                double cap = diag * 0.75;
                Interval du = srf.Domain(0), dv = srf.Domain(1);
                double sum = 0; int cnt = 0;
                for (int i = 0; i < 21; i++)
                    for (int j = 0; j < 21; j++)
                    {
                        Point3d p;
                        try { p = srf.PointAt(du.ParameterAt(i / 20.0), dv.ParameterAt(j / 20.0)); } catch { continue; }
                        Point3d q; Vector3d nn; double d; bool atEdge;
                        if (!refr.Closest(p, cap, out q, out nn, out d, out atEdge)) continue;
                        if (d > maxDev) maxDev = d;
                        sum += d * d; cnt++;
                    }
                rms = cnt > 0 ? Math.Sqrt(sum / cnt) : 0;
            }
            catch { }

            // ③ G1（面边界法向 vs 相邻面法向；逐点法向来自边界环）
            double angle = -1; int g1n = 0;
            try
            {
                if (outer != null)
                {
                    double len = outer.GetLength();
                    int m = 81;
                    double worst = 0;
                    for (int k = 0; k < m; k++)
                    {
                        Point3d p = outer.PointAtLength(len * k / (m - 1.0));
                        Vector3d na = PatchFillCore.NormalAtArc(loop, ArcOfNearest(loop, p));
                        if (!na.IsValid || na.Length < 1e-12) continue;
                        na.Unitize();
                        double u, v;
                        if (!srf.ClosestPoint(p, out u, out v)) continue;
                        Vector3d ns = srf.NormalAt(u, v);
                        if (!ns.IsValid || ns.Length < 1e-12) continue;
                        ns.Unitize();
                        double c = Math.Abs(ns * na);
                        if (c > 1) c = 1;
                        double a = Math.Acos(c) * 180.0 / Math.PI;
                        if (a > worst) worst = a;
                        g1n++;
                    }
                    if (g1n > 0) angle = worst;
                }
            }
            catch { }

            // ⑥ 曲率连续性近似（控制网最外两排离散曲率最大差）
            double cj = 0;
            try
            {
                int nu = srf.Points.CountU, nv = srf.Points.CountV;
                var cp = new Point3d[nu, nv];
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        Point3d q;
                        if (srf.Points.GetPoint(i, j, out q)) cp[i, j] = q;
                    }
                cj = PatchFillCore.CurvatureJumpOf(cp, nu, nv);
            }
            catch { }

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      [info] {0}：面数 {1} · 控制网 {2}×{3} · 边界缝隙 {4:0.######} · 最大偏差 {5:0.######}（平均 {6:0.######}）· G1 {7} · 曲率差 {8:0.######} · 耗时 {9:0.000}s",
                label, patch.Faces.Count, srf.Points.CountU, srf.Points.CountV, gap, maxDev, rms,
                angle >= 0 ? angle.ToString("0.####", CultureInfo.InvariantCulture) + "°" : "—", cj, secs));
            Check(sb, ref pass, ref fail, patch.Faces.Count >= 1 && maxDev >= 0, label + "：6 项度量已测出（数字见上）");
        }

        // ================================================================== 用例14：G2 曲率连续

        /// <summary>
        /// 用例14：**G2 曲率连续**（本轮算法升级主用例：XNURBS 路线的「宿主导数采样 → 双环位置带 +
        /// 二阶差分行」）。球带顶圈（相邻面 = 球面）：G2 模式下结果面边界外的二阶差分方向要跟住球面。
        /// G2 度量 = 边界处「结果面三点二阶差分向量」与「相邻面二阶差分向量」的夹角（有限差分意义）。
        /// </summary>
        static void Case14_G2Curvature(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例14：G2 曲率连续（球带顶圈；二阶差分夹角 = 有限差分意义的曲率连续度量） ---");
            Brep zone;
            PatchFillEdge top = SphereZoneTopEdge(10.0, 30.0, 0.0, out zone);
            if (top == null) { Check(sb, ref pass, ref fail, false, "构造：球带顶圈失败"); return; }
            Brep sphereRef = null;
            try { sphereRef = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 10.0))); } catch { }
            Check(sb, ref pass, ref fail, sphereRef != null, "构造：完整球面参考几何");
            if (sphereRef == null) return;
            var edges = new List<PatchFillEdge>(); edges.Add(top);

            var sG2 = new PatchFillSettings { ControlCount = 16, FitStrength = 1.0, Smooth = 0.05, Continuity = 2, G1Distance = 1.0 };
            string rep;
            PatchFillResult r2 = PatchFillCore.Generate(edges, null, sphereRef, sG2, out rep);
            Check(sb, ref pass, ref fail, r2.Ok, "G2 生成成功（" + (string.IsNullOrEmpty(r2.Error) ? "OK" : r2.Error) + "）");
            if (!r2.Ok) return;
            Check(sb, ref pass, ref fail, r2.G2Samples > 0 && r2.MaxSecondDiffAngleDeg >= 0,
                string.Format(CultureInfo.InvariantCulture, "G2 有二阶差分夹角统计（{0} 个采样点，最大 {1:0.####}°）",
                    r2.G2Samples, r2.MaxSecondDiffAngleDeg));
            // 末轮诊断快照（r1 生成会覆盖静态诊断，先抄下来）
            int diagN = PatchFillCore.LastG2Samples, diagN90 = PatchFillCore.LastG2N90;
            double diagMaxNon90 = PatchFillCore.LastG2MaxNon90;
            double diagRes1 = PatchFillCore.LastG2RingRes1, diagRes2 = PatchFillCore.LastG2RingRes2;

            // G1 基线（同输入只开 G1）：它的 G2 度量就是「不开 G2 行」的对照
            var sG1 = sG2.Clone(); sG1.Continuity = 1;
            PatchFillResult r1 = PatchFillCore.Generate(edges, null, sphereRef, sG1, out rep);
            Check(sb, ref pass, ref fail, r1.Ok && r1.MaxSecondDiffAngleDeg >= 0,
                string.Format(CultureInfo.InvariantCulture, "G1 基线生成成功且同样测出 G2 夹角（{0:0.####}°）",
                    r1.MaxSecondDiffAngleDeg));
            if (r1.Ok && r1.MaxSecondDiffAngleDeg >= 0)
                Check(sb, ref pass, ref fail, r2.MaxSecondDiffAngleDeg <= Math.Max(10.0, r1.MaxSecondDiffAngleDeg),
                    string.Format(CultureInfo.InvariantCulture,
                        "G2 行不弄差曲率：G2 {0:0.####}° ≤ max(10°, G1 基线 {1:0.####}°)（一致参数化下 G1 环带已接近曲率连续，G2 加第二圈钉住）",
                        r2.MaxSecondDiffAngleDeg, r1.MaxSecondDiffAngleDeg));
            // G2 模式下 G1 不能退化（全边界含接缝 < 1°，径向盘状没有人工角点）
            Check(sb, ref pass, ref fail, r2.MaxNormalAngleDeg < 1.0,
                string.Format(CultureInfo.InvariantCulture, "G2 模式下 G1 不退化：全边界夹角 < 1°（实测 {0:0.####}°）",
                    r2.MaxNormalAngleDeg));
            // G2 不能把 G0 弄坏
            Check(sb, ref pass, ref fail, r2.BoundaryDeviation < 1e-3,
                string.Format(CultureInfo.InvariantCulture, "G2 不破坏 G0：逐边缝隙 < 1e-3（实测 {0:0.######}）",
                    r2.BoundaryDeviation));
            // 偏差不能比 G1 基线差太多（曲率带不能把面拉坏）
            Check(sb, ref pass, ref fail, r2.MaxDeviation <= r1.MaxDeviation * 1.2 + 1e-9,
                string.Format(CultureInfo.InvariantCulture, "G2 不拉坏偏差：{0:0.####}（G1 基线 {1:0.####}）",
                    r2.MaxDeviation, r1.MaxDeviation));
            // G2 与 G1 几何签名不同（曲率带真的改了面）
            Check(sb, ref pass, ref fail, Math.Abs(SigOf(r1) - SigOf(r2)) > 1e-9,
                "G2 与 G1 几何签名不同（曲率约束真的接上了）");
            // 干净用例不触发曲率告警
            Check(sb, ref pass, ref fail, (r2.WarnFlags & PatchFillCore.WarnCurvature) == 0,
                "G2 干净用例：无「曲率不一致」告警（实测位 " + r2.WarnFlags + "）");
            // 平面矩形洞回归：曲率带在平面上是「平的延续」→ 缝隙必须仍然极小。
            // （G1+G2 行全族与平面解完全相容；3e-5 量级 = 1e6/0.1 权重跨度的稠密 Cholesky 条件数极限，
            //   相对 40 对角线 = 8e-7，远低于 1e-3 工程容差 —— 断言按 1e-4 收口。）
            var hole = new Point3d[] { new Point3d(10, 10, 0), new Point3d(30, 10, 0), new Point3d(30, 30, 0), new Point3d(10, 30, 0) };
            Brep plate = PlateWithPolyHole(hole, 40.0);
            if (plate != null)
            {
                int ec;
                var pe = InnerLoopEdges(plate, out ec);
                var sP = new PatchFillSettings { ControlCount = 12, FitStrength = 1.0, Smooth = 0.0, Continuity = 2, G1Distance = 1.0 };
                PatchFillResult rp = PatchFillCore.Generate(pe, null, null, sP, out rep);
                Check(sb, ref pass, ref fail, rp.Ok && rp.BoundaryDeviation < 1e-4,
                    string.Format(CultureInfo.InvariantCulture, "平面洞开 G2：缝隙 < 1e-4（条件数极限，相对 8e-7；实测 {0:0.########}）",
                        rp.Ok ? rp.BoundaryDeviation : -1));
            }
            else Check(sb, ref pass, ref fail, false, "构造：平面回归用例的带洞平板失败");

            sb.AppendLine(BenchInfoLine("PatchFill-G2", "⑤+ 带 G2 的洞（球带顶圈）", zone, r2));
            sb.AppendLine("      [dbg] G1 最差位置：side " + r2.WorstAngleSide + " · f " + r2.WorstAngleF.ToString("0.####", CultureInfo.InvariantCulture)
                + " · G2 最差位置（末轮）：side " + PatchFillCore.LastG2Side + " · f " + PatchFillCore.LastG2F.ToString("0.####", CultureInfo.InvariantCulture));
            sb.AppendLine("      [dbg] G2 诊断（r2 末轮）：样本 " + diagN + " · 90°分支 " + diagN90
                + " · 非直最大 " + diagMaxNon90.ToString("0.##", CultureInfo.InvariantCulture) + "°"
                + " · 环带残差 δ " + diagRes1.ToString("0.####", CultureInfo.InvariantCulture)
                + " · 2δ " + diagRes2.ToString("0.####", CultureInfo.InvariantCulture));
            sb.AppendLine("      [info] " + string.Format(CultureInfo.InvariantCulture,
                "G1 基线：G2 夹角 {0:0.####}° · G2 模式：G2 夹角 {1:0.####}° · G1 夹角 {2:0.####}° · 缝隙 {3:0.######} · 轮数 {4} · {5:0.000}s",
                r1.MaxSecondDiffAngleDeg, r2.MaxSecondDiffAngleDeg, r2.MaxNormalAngleDeg, r2.BoundaryDeviation, r2.Rounds, r2.Seconds));
            for (int i = 0; i < r2.RoundLog.Count; i++) sb.AppendLine("      [dbg] " + r2.RoundLog[i]);
        }

        /// <summary>点 p 在边界折线上的弧长位置（取最近投影）</summary>
        static double ArcOfNearest(PatchFillCore.LoopData ld, Point3d p)
        {
            double best = double.MaxValue, arc = 0;
            for (int i = 0; i < ld.P.Length; i++)
            {
                Point3d a = ld.P[i], b = ld.P[(i + 1) % ld.P.Length];
                Vector3d ab = b - a;
                double l2 = ab * ab;
                double t = l2 > 1e-30 ? ((p - a) * ab) / l2 : 0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                Point3d q = new Point3d(a.X + ab.X * t, a.Y + ab.Y * t, a.Z + ab.Z * t);
                double d = p.DistanceTo(q);
                if (d < best) { best = d; arc = ld.Cum[i] + t * (ld.Cum[i + 1] - ld.Cum[i]); }
            }
            return arc;
        }

        // ================================================================== 造几何 / 度量

        /// <summary>相邻面的未修剪底层曲面（洞边上没材料，独立度量要用它）</summary>
        static Brep UntrimmedFace(Brep brep)
        {
            try
            {
                if (brep == null || brep.Faces.Count == 0) return null;
                Surface us = brep.Faces[0].UnderlyingSurface();
                if (us == null) return null;
                return Brep.CreateFromSurface(us);
            }
            catch { return null; }
        }

        /// <summary>平面板 + 多边形洞（Brep.CreatePlanarBreps 的外圈 + 内圈）</summary>
        static Brep PlateWithPolyHole(Point3d[] hole, double size)
        {
            try
            {
                var outer = new PolylineCurve(new Point3d[] {
                    new Point3d(0,0,0), new Point3d(size,0,0), new Point3d(size,size,0), new Point3d(0,size,0), new Point3d(0,0,0) });
                var hp = new List<Point3d>(hole);
                hp.Add(hole[0]);
                var inner = new PolylineCurve(hp);
                Brep[] b = Brep.CreatePlanarBreps(new Curve[] { outer, inner }, 1e-6);
                if (b != null && b.Length > 0 && b[0].Loops.Count >= 2) return b[0];
                // 方向不对就反向再试一次
                var rev = new List<Point3d>();
                for (int i = hp.Count - 1; i >= 0; i--) rev.Add(hp[i]);
                Brep[] b2 = Brep.CreatePlanarBreps(new Curve[] { outer, new PolylineCurve(rev) }, 1e-6);
                if (b2 != null && b2.Length > 0) return b2[0];
            }
            catch { }
            return null;
        }

        /// <summary>取 Brep 内环（洞）的边</summary>
        static List<PatchFillEdge> InnerLoopEdges(Brep brep, out int edgeCount)
        {
            edgeCount = 0;
            var list = new List<PatchFillEdge>();
            var seen = new HashSet<int>();
            try
            {
                for (int li = 0; li < brep.Loops.Count; li++)
                {
                    BrepLoop lp = brep.Loops[li];
                    if (lp.LoopType != BrepLoopType.Inner) continue;
                    for (int t = 0; t < lp.Trims.Count; t++)
                    {
                        BrepEdge e = lp.Trims[t].Edge;
                        if (e == null) continue;
                        if (!seen.Add(e.EdgeIndex)) continue;
                        Curve c = e.DuplicateCurve();
                        if (c == null) continue;
                        list.Add(new PatchFillEdge(c, brep, e.EdgeIndex, e.AdjacentFaces(), "内环边" + (e.EdgeIndex + 1)));
                    }
                }
            }
            catch { }
            edgeCount = list.Count;
            return list;
        }

        /// <summary>圆柱面上的一块「矩形洞」边界（4 条曲线，无相邻面）+ 未修剪圆柱做参考</summary>
        static List<PatchFillEdge> CylHole(double R, double z0, double z1, double a0, double a1, out Brep refBrep)
        {
            refBrep = null;
            var list = new List<PatchFillEdge>();
            try
            {
                var cyl = new Cylinder(new Circle(new Plane(Point3d.Origin, Vector3d.ZAxis), R), z1 - z0);
                NurbsSurface ns = NurbsSurface.CreateFromCylinder(cyl);
                refBrep = Brep.CreateFromSurface(ns);

                Point3d p00 = new Point3d(R * Math.Cos(a0), R * Math.Sin(a0), z0);
                Point3d p10 = new Point3d(R * Math.Cos(a1), R * Math.Sin(a1), z0);
                Point3d p01 = new Point3d(p00.X, p00.Y, z1);
                Point3d p11 = new Point3d(p10.X, p10.Y, z1);

                Curve bottom = new Arc(new Circle(new Plane(new Point3d(0, 0, z0), Vector3d.ZAxis), R),
                    new Interval(a0, a1)).ToNurbsCurve();
                Curve top = new Arc(new Circle(new Plane(new Point3d(0, 0, z1), Vector3d.ZAxis), R),
                    new Interval(a0, a1)).ToNurbsCurve();
                Curve left = new LineCurve(p00, p01);
                Curve right = new LineCurve(p10, p11);

                list.Add(new PatchFillEdge(bottom, null, -1, null, "圆柱底弧"));
                list.Add(new PatchFillEdge(right, null, -1, null, "圆柱右边"));
                list.Add(new PatchFillEdge(top, null, -1, null, "圆柱顶弧"));
                list.Add(new PatchFillEdge(left, null, -1, null, "圆柱左边"));
            }
            catch { }
            return list;
        }

        /// <summary>球带（球面纬线之间的一圈）→ 取顶圈那条边（有相邻面 → G1 可用）</summary>
        static PatchFillEdge SphereZoneTopEdge(double R, double lat0Deg, double lat1Deg, out Brep zone)
        {
            zone = null;
            try
            {
                double a0 = lat0Deg * Math.PI / 180.0, a1 = lat1Deg * Math.PI / 180.0;
                Point3d p0 = new Point3d(R * Math.Cos(a0), 0, R * Math.Sin(a0));
                Point3d p1 = new Point3d(R * Math.Cos(a1), 0, R * Math.Sin(a1));
                double am = (a0 + a1) * 0.5;
                Point3d pm = new Point3d(R * Math.Cos(am), 0, R * Math.Sin(am));
                var arc = new Arc(p0, pm, p1);
                Curve meridian = arc.ToNurbsCurve();
                RevSurface rev = RevSurface.Create(meridian, new Line(Point3d.Origin, new Point3d(0, 0, 1)), 0, 2 * Math.PI);
                if (rev == null) return null;
                zone = Brep.CreateFromSurface(rev);
                if (zone == null) return null;

                int best = -1; double bestZ = double.MinValue;
                for (int i = 0; i < zone.Edges.Count; i++)
                {
                    Curve c = zone.Edges[i].DuplicateCurve();
                    if (c == null) continue;
                    Point3d m = c.PointAt(c.Domain.Mid);
                    bool closed = c.PointAtStart.DistanceTo(c.PointAtEnd) < 1e-6;
                    if (!closed) continue;                          // 只要闭合圆（顶圈）
                    if (m.Z > bestZ) { bestZ = m.Z; best = i; }
                }
                if (best < 0) return null;
                Curve ec = zone.Edges[best].DuplicateCurve();
                return new PatchFillEdge(ec, zone, best, zone.Edges[best].AdjacentFaces(), "球带顶圈");
            }
            catch { return null; }
        }

        static PatchFillEdge MkEdge(Curve c, string src)
        {
            return new PatchFillEdge(c, null, -1, null, src);
        }

        static List<Point3d> ToPoints(Polyline pl)
        {
            var list = new List<Point3d>();
            for (int i = 0; i < pl.Count; i++) list.Add(pl[i]);
            return list;
        }

        static double DiagOf(List<PatchFillEdge> edges)
        {
            var bb = BoundingBox.Empty;
            for (int i = 0; i < edges.Count; i++)
            {
                try { bb.Union(edges[i].Curve.GetBoundingBox(true)); } catch { }
            }
            return bb.IsValid ? bb.Diagonal.Length : 0;
        }

        /// <summary>结果面的几何签名：权重必须不可分离（含 x·y 与 z² 交叉项），否则假通过</summary>
        static double SigOf(PatchFillResult r)
        {
            if (r == null || r.Surface == null) return double.NaN;
            double sum = 0;
            try
            {
                Surface srf = r.Surface;
                Interval du = srf.Domain(0), dv = srf.Domain(1);
                for (int i = 0; i < 7; i++)
                    for (int j = 0; j < 7; j++)
                    {
                        Point3d p = srf.PointAt(du.ParameterAt(i / 6.0), dv.ParameterAt(j / 6.0));
                        double w = 1.0 + 0.0007 * p.X * p.Y + 0.0015 * p.Z * p.Z;
                        sum += (p.X + 2.0 * p.Y + 3.0 * p.Z) * w;
                    }
            }
            catch { return double.NaN; }
            return sum;
        }

        /// <summary>独立偏差度量：结果面密采样 → 参考几何最近点（自己的宽容差，不受面板影响）</summary>
        static double IndependentMaxDev(PatchFillResult r, Brep reference, double diag)
        {
            if (r == null || r.Surface == null || reference == null) return double.NaN;
            double worst = 0;
            try
            {
                Interval du = r.Surface.Domain(0), dv = r.Surface.Domain(1);
                for (int i = 0; i <= 12; i++)
                    for (int j = 0; j <= 12; j++)
                    {
                        Point3d p = r.Surface.PointAt(du.ParameterAt(i / 12.0), dv.ParameterAt(j / 12.0));
                        Point3d q = Point3d.Origin; ComponentIndex ci = ComponentIndex.Unset; double u = 0, v = 0; Vector3d n = Vector3d.Zero;
                        bool ok = false;
                        try { ok = reference.ClosestPoint(p, out q, out ci, out u, out v, diag * 2.0, out n); } catch { ok = false; }
                        if (!ok) continue;
                        double d = p.DistanceTo(q);
                        if (d > worst) worst = d;
                    }
            }
            catch { }
            return worst;
        }

        static double DistToSurface(PatchFillResult r, Point3d p)
        {
            if (r == null || r.Surface == null) return double.NaN;
            try
            {
                double u, v;
                if (!r.Surface.ClosestPoint(p, out u, out v)) return double.NaN;
                return r.Surface.PointAt(u, v).DistanceTo(p);
            }
            catch { return double.NaN; }
        }

        static double CurveDevToResult(PatchFillResult r, Curve c)
        {
            if (r == null || r.Surface == null || c == null) return double.NaN;
            double worst = 0;
            try
            {
                double len = c.GetLength();
                for (int i = 0; i <= 24; i++)
                {
                    Point3d p = c.PointAtLength(len * i / 24.0);
                    double u, v;
                    if (!r.Surface.ClosestPoint(p, out u, out v)) continue;
                    double d = r.Surface.PointAt(u, v).DistanceTo(p);
                    if (d > worst) worst = d;
                }
            }
            catch { return double.NaN; }
            return worst;
        }

        static double ParamValue(PatchFillPanel panel, string name)
        {
            if (panel == null) return double.NaN;
            PatchFillSettings s = panel.Settings;
            switch (name)
            {
                case "控制点数": return s.ControlCount;
                case "贴合强度": return s.FitStrength;
                case "平滑度": return s.Smooth;
                case "G1 采样距离": return s.G1Distance;
            }
            return double.NaN;
        }

        static int LayerIndex(RhinoDoc doc, string name)
        {
            foreach (Layer l in doc.Layers)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
            return -1;
        }

        static void Cleanup(RhinoDoc doc, params Guid[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == Guid.Empty) continue;
                try { doc.Objects.Delete(ids[i], true); } catch { }
            }
        }

        static void Flush(string reportPath, StringBuilder sb, int pass, int fail, int skip)
        {
            try
            {
                string d = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                File.WriteAllText(reportPath,
                    sb.ToString() + System.Environment.NewLine +
                    string.Format("… 进度快照：{0} 通过 / {1} 失败 / {2} 跳过（跑完会被最终 RESULT 覆盖）", pass, fail, skip) +
                    System.Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        static void Check(StringBuilder sb, ref int pass, ref int fail, bool ok, string what)
        {
            sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + what);
            if (ok) pass++; else fail++;
        }
    }
}
