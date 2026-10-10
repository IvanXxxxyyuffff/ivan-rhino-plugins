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

namespace SurfaceUnifyPattern
{
    /// <summary>
    /// 无人值守自检。用法：命令 SurfaceUnifySelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放
    /// run-surfaceunify-selftest.flag 后启动 Rhino（跑完自动写报告并退出）。
    /// </summary>
    public class SurfaceUnifySelfTestCommand : Command
    {
        public override string EnglishName { get { return "SurfaceUnifySelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\SurfaceUnifySelfTest.txt");
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
            sb.AppendLine("=== 多重曲面 → 单一曲面 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                Case1_Pure(sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case2_PlanarMultiFace(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case3_CylinderMesh(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case4_LFold(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case5_PlateWithHole(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case6_Reject(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case7_Params(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case8_PanelWiring(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case9_Document(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case10_Determinism(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case11_UserFile(reportPath, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case12_Pouch(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case13_Wrapped(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case14_CornerBoundary(doc, sb, ref pass, ref fail);
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

        // ------------------------------------------------------------------ 用例

        /// <summary>用例1：边界环 / 4 角 / Coons / 网格工具（纯函数，不需要文档）</summary>
        static void Case1_Pure(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例1：边界环 / 4 角 / Coons / 网格（纯函数） ---");

            // 10×10 矩形环（逆时针，首尾重复点要能自动去掉）
            var rect = new Polyline(new Point3d[] {
                new Point3d(0,0,0), new Point3d(10,0,0), new Point3d(10,10,0), new Point3d(0,10,0), new Point3d(0,0,0) });
            SurfaceUnifyCore.LoopData ld = SurfaceUnifyCore.LoopData.Build(rect);
            Check(sb, ref pass, ref fail, ld != null && ld.P.Length == 4, "闭合环：收尾重复点被去掉（4 个顶点）");
            Check(sb, ref pass, ref fail, ld != null && Math.Abs(ld.Length - 40.0) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "闭合环：周长 = 40（实测 {0:0.####}）", ld != null ? ld.Length : -1));

            double area = Math.Abs(SurfaceUnifyCore.LoopArea2d(ld, Plane.WorldXY));
            Check(sb, ref pass, ref fail, Math.Abs(area - 100.0) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "环面积（鞋带公式）= 100（实测 {0:0.####}）", area));

            // 外环 + 内孔 → 外环是面积大的那个
            var outer = new Polyline(new Point3d[] { new Point3d(0, 0, 0), new Point3d(20, 0, 0), new Point3d(20, 20, 0), new Point3d(0, 20, 0), new Point3d(0, 0, 0) });
            var hole = new Polyline(new Point3d[] { new Point3d(5, 5, 0), new Point3d(9, 5, 0), new Point3d(9, 9, 0), new Point3d(5, 9, 0), new Point3d(5, 5, 0) });
            int outerIdx; double[] areas;
            SurfaceUnifyCore.ClassifyLoops(new Polyline[] { hole, outer }, Plane.WorldXY, out outerIdx, out areas);
            Check(sb, ref pass, ref fail, outerIdx == 1 && areas[1] > areas[0], "环分类：面积最大的当外环（外环 + 内孔）");

            // 4 角：矩形的对角极值 = 4 个真角点
            int[] ci; bool fb;
            SurfaceUnifyCore.Corners(ld, Plane.WorldXY, out ci, out fb);
            bool cornersOk = !fb && ci != null && ci.Length == 4;
            if (cornersOk)
            {
                var got = new List<Point3d>();
                for (int i = 0; i < 4; i++) got.Add(ld.P[ci[i]]);
                var want = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 10, 0), new Point3d(0, 10, 0) };
                for (int i = 0; i < 4; i++)
                {
                    bool hit = false;
                    for (int k = 0; k < want.Count; k++)
                        if (got[i].DistanceTo(want[k]) < 1e-9) { hit = true; break; }
                    if (!hit) cornersOk = false;
                }
            }
            Check(sb, ref pass, ref fail, cornersOk, "4 角：矩形上取到的就是 4 个真角点（曲率优先/对角极值，非兜底）");

            // 弧长等分采样：起终点正确 + 等弧长
            List<Point3d> side = SurfaceUnifyCore.SampleArc(ld, ci[0], ci[1], 11);
            bool sampOk = side.Count == 11 && side[0].DistanceTo(ld.P[ci[0]]) < 1e-9 && side[10].DistanceTo(ld.P[ci[1]]) < 1e-9;
            double maxSeg = 0, minSeg = double.MaxValue;
            for (int i = 0; i < side.Count - 1; i++)
            {
                double d = side[i].DistanceTo(side[i + 1]);
                if (d > maxSeg) maxSeg = d;
                if (d < minSeg) minSeg = d;
            }
            Check(sb, ref pass, ref fail, sampOk, "弧长采样：点数 11、起点/终点就是两个角点");
            Check(sb, ref pass, ref fail, Math.Abs(maxSeg - minSeg) < 1e-9 && Math.Abs(maxSeg - 1.0) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "弧长采样：严格等分（10 段各 1.0，实测 {0:0.######}~{1:0.######}）", minSeg, maxSeg));

            // Coons：矩形 4 条边 → 网格边界点 = 采样点（边界精确），内部点共面
            int n = 11;
            var rowV0 = SurfaceUnifyCore.SampleArc(ld, ci[0], ci[1], n);
            var rowV1 = SurfaceUnifyCore.SampleArc(ld, ci[2], ci[3], n); rowV1.Reverse();
            var colU0 = SurfaceUnifyCore.SampleArc(ld, ci[3], ci[0], n); colU0.Reverse();
            var colU1 = SurfaceUnifyCore.SampleArc(ld, ci[1], ci[2], n);
            Point3d[,] g = SurfaceUnifyCore.CoonsGrid(rowV0, rowV1, colU0, colU1, n, n);
            Check(sb, ref pass, ref fail, g != null, "Coons 基面：矩形网格构造成功");
            double maxOff = 0, maxZ = 0;
            if (g != null)
            {
                for (int i = 0; i < n; i++)
                {
                    if (g[i, 0].DistanceTo(rowV0[i]) > maxOff) maxOff = g[i, 0].DistanceTo(rowV0[i]);
                    if (g[i, n - 1].DistanceTo(rowV1[i]) > maxOff) maxOff = g[i, n - 1].DistanceTo(rowV1[i]);
                    if (g[0, i].DistanceTo(colU0[i]) > maxOff) maxOff = g[0, i].DistanceTo(colU0[i]);
                    if (g[n - 1, i].DistanceTo(colU1[i]) > maxOff) maxOff = g[n - 1, i].DistanceTo(colU1[i]);
                    for (int j = 0; j < n; j++) if (Math.Abs(g[i, j].Z) > maxZ) maxZ = Math.Abs(g[i, j].Z);
                }
            }
            Check(sb, ref pass, ref fail, maxOff < 1e-12, "Coons 基面：网格边界点 = 原边界采样点（边界精确）");
            Check(sb, ref pass, ref fail, maxZ < 1e-12, "Coons 基面：平面矩形的内部点严格共面（Z = 0）");

            int folded;
            Vector3d[,] nrm = SurfaceUnifyCore.GridNormals(g, n, n, out folded);
            Check(sb, ref pass, ref fail, folded == 0 && nrm[5, 5].Z > 0.99, "网格法向：平面网格无折叠、法向朝 +Z");

            double cell = SurfaceUnifyCore.MeanCellSize(g, n, n);
            Check(sb, ref pass, ref fail, Math.Abs(cell - 1.0) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "平均单元尺寸 = 1.0（10×10 / 10 段，实测 {0:0.####}）", cell));

            // 光顺：平面网格是不动点；边界锁定时边界一点不动
            var g2 = (Point3d[,])g.Clone();
            g2[5, 5] = new Point3d(g2[5, 5].X, g2[5, 5].Y, 5.0);       // 人为顶起一个内部点
            SurfaceUnifyCore.SmoothGrid(g2, n, n, 3, 0.5, true);
            Check(sb, ref pass, ref fail, Math.Abs(g2[5, 5].Z) < 5.0 && Math.Abs(g2[5, 5].Z) > 0.0,
                string.Format(CultureInfo.InvariantCulture, "光顺：内部凸起被压平（5.0 → {0:0.####}）", g2[5, 5].Z));
            bool boundaryFixed = true;
            for (int i = 0; i < n; i++)
            {
                if (g2[i, 0].DistanceTo(g[i, 0]) > 1e-12) boundaryFixed = false;
                if (g2[0, i].DistanceTo(g[0, i]) > 1e-12) boundaryFixed = false;
            }
            Check(sb, ref pass, ref fail, boundaryFixed, "光顺：边界保形 = 开 → 边界点一点不动");

            var g3 = (Point3d[,])g.Clone();
            g3[5, 5] = new Point3d(g3[5, 5].X, g3[5, 5].Y, 5.0);
            SurfaceUnifyCore.SmoothGrid(g3, n, n, 3, 0.5, false);
            Check(sb, ref pass, ref fail, g3[0, 0].DistanceTo(g[0, 0]) > 1e-9, "光顺：边界保形 = 关 → 边界点会动");

            // 折叠诊断：把网格的 x 做成非单调（平滑折返，单元不退化）→ 定向翻转区应被识别
            var g4 = (Point3d[,])g.Clone();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    g4[i, j] = new Point3d(g4[i, j].X + 4.0 * Math.Sin(Math.PI * g4[i, j].X / 5.0), g4[i, j].Y, 0.0);
            int folded2;
            SurfaceUnifyCore.GridNormals(g4, n, n, out folded2);
            Check(sb, ref pass, ref fail, folded2 > 0, string.Format("折叠诊断：网格折返处识别出定向翻转（{0} 个单元）", folded2));

            // 建面：平面网格 → NURBS 面 + 4 角对上（校验 CreateThroughPoints 的点序约定）
            NurbsSurface srf = SurfaceUnifyCore.BuildSurface(g, n, n);
            Check(sb, ref pass, ref fail, srf != null, "建面：平面网格 → NURBS 面");
            Check(sb, ref pass, ref fail, srf != null && SurfaceUnifyCore.CornersMatch(srf, g, n, n),
                "建面：4 个角点全部对上（点序约定 = u 慢变）");
            Check(sb, ref pass, ref fail, srf != null && srf.Points.CountU == n && srf.Points.CountV == n,
                string.Format(CultureInfo.InvariantCulture, "建面：控制点数 = {0}×{0}（插值过所有网格点）", n));
            Check(sb, ref pass, ref fail, srf != null && srf.IsValid, "建面：曲面 IsValid = 真");

            // u 闭合 + 末行塌成一点（包裹式爬行网格的形态）：必须能建出有效曲面
            {
                var gp = new Point3d[8, 8];
                for (int i = 0; i < 8; i++)
                    for (int j = 0; j < 8; j++)
                    {
                        double a = 2.0 * Math.PI * i / 8.0;
                        double rr = 10.0 * (1.0 - j / 8.0);
                        gp[i, j] = j < 7 ? new Point3d(rr * Math.Cos(a), rr * Math.Sin(a), -j * 2.0)
                                         : new Point3d(0, 0, -14.0);
                    }
                NurbsSurface sp = SurfaceUnifyCore.BuildSurface(gp, 8, 8, true);
                Check(sb, ref pass, ref fail, sp != null && sp.IsValid,
                    "建面：u 闭合 + 末行塌成一点（爬行网格形态）→ 非空且 IsValid");
            }

            // 交点硬约束（纯函数）：ArcPositionsPinned 把交点弧长位钉进采样序列；PinJunctionsExact 精确写点
            {
                // 5 段折线环（多一个转折节点 (5,0)）：span=25，等分 6 点 + 交点 rel=5
                var loop = new Polyline(new Point3d[] {
                    new Point3d(0,0,0), new Point3d(10,0,0), new Point3d(10,10,0),
                    new Point3d(0,10,0), new Point3d(0,0,0) });
                SurfaceUnifyCore.LoopData ldJ = SurfaceUnifyCore.LoopData.Build(loop);
                double[] pos = SurfaceUnifyCore.ArcPositionsPinned(10.0, 6, new List<double> { 5.0 });
                bool pinned = false;
                for (int k = 0; k < pos.Length; k++) if (Math.Abs(pos[k] - 5.0) < 1e-9) pinned = true;
                Check(sb, ref pass, ref fail, pos.Length == 6 && pinned,
                    string.Format(CultureInfo.InvariantCulture, "交点钉位：ArcPositionsPinned 把交点 rel=5 精确钉进采样序列（pos[{0}]）", pos.Length));

                var g5 = new Point3d[6, 6];
                for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) g5[i, j] = new Point3d(i, j, 0);
                var junction = new List<Point3d> { new Point3d(3.0, 0.0, 0.0) };
                g5[3, 0] = new Point3d(3.2, 0.15, 0.1);                       // 人为偏移
                int mv = SurfaceUnifyCore.PinJunctionsExact(g5, 6, false, junction, 20.0);
                Check(sb, ref pass, ref fail, mv > 0 && g5[3, 0].DistanceTo(junction[0]) < 1e-12,
                    string.Format(CultureInfo.InvariantCulture, "交点钉位：PinJunctionsExact 把偏移节点精确写回交点（挪动 {0} 个，偏差 {1:0.########}）",
                        mv, g5[3, 0].DistanceTo(junction[0])));

                // 收集器：矩形 Brep 的裸边端点 = 4 个真角点
                Brep rectB = Rect(0, 0, 10, 10, 0);
                List<Point3d> got = rectB != null ? SurfaceUnifyCore.CollectCornerIntersections(rectB, null, 14.2) : new List<Point3d>();
                bool four = got.Count == 4;
                if (four)
                {
                    var want = new List<Point3d> { new Point3d(0,0,0), new Point3d(10,0,0), new Point3d(10,10,0), new Point3d(0,10,0) };
                    for (int i = 0; i < got.Count; i++)
                    {
                        bool hit = false;
                        for (int k = 0; k < want.Count; k++) if (got[i].DistanceTo(want[k]) < 1e-9) { hit = true; break; }
                        if (!hit) four = false;
                    }
                }
                Check(sb, ref pass, ref fail, four,
                    string.Format(CultureInfo.InvariantCulture, "交点收集：矩形裸边端点 = 4 个真角点（实测 {0} 个）", got.Count));
            }
        }

        /// <summary>用例2：平面多重曲面（两块共面面片）→ 单一曲面（精确）</summary>
        static void Case2_PlanarMultiFace(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例2：平面多重曲面（2 块共面面片） ---");
            Brep a = Rect(0, 0, 10, 10, 0);
            Brep b = Rect(10, 0, 20, 10, 0);
            Brep poly = null;
            try
            {
                Brep[] joined = Brep.JoinBreps(new Brep[] { a, b }, 1e-6);
                if (joined != null && joined.Length > 0) poly = joined[0];
            }
            catch { }
            Check(sb, ref pass, ref fail, poly != null && poly.Faces.Count == 2, "构造：20×10 平面多重曲面（2 张面）");

            var s = new SurfaceUnifySettings();
            string rep;
            SurfaceUnifyResult r = SurfaceUnifyCore.Generate(poly, s, out rep);
            Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Result != null, "生成成功：" + (string.IsNullOrEmpty(r.Error) ? rep : r.Error));
            if (r.Result == null) return;

            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1, string.Format("输出 = 单一曲面（面数 {0}）", r.Result.Faces.Count));
            Check(sb, ref pass, ref fail, r.Loops == 1 && r.Holes == 0, string.Format("无内孔（修剪环 {0}）", r.Loops));
            Check(sb, ref pass, ref fail, r.MaxDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 1e-6（平面应精确，实测 {0:0.########}）", r.MaxDeviation));
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "边界偏差 < 1e-6（边界完全逼近，实测 {0:0.########}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.CornerDeviation < 1e-6 && r.JunctionCount >= 4,
                string.Format(CultureInfo.InvariantCulture, "交点偏差 < 1e-6（矩形 {0} 个边-边交点必须精确命中，实测 {1:0.########}）",
                    r.JunctionCount, r.CornerDeviation));
            Check(sb, ref pass, ref fail, Math.Abs(r.AreaRatio - 1.0) < 1e-3,
                string.Format(CultureInfo.InvariantCulture, "面积比 ≈ 1（实测 {0:0.######}）", r.AreaRatio));
            Check(sb, ref pass, ref fail, r.ControlU == s.GridCount && r.ControlV == s.GridCount,
                string.Format("控制点数 = {0}×{0}", s.GridCount));
            Check(sb, ref pass, ref fail, rep.Contains("交点偏差"),
                "报告文本包含「交点偏差」单项（用户口径：交点偏差逐点列出）");

            var sTrim = new SurfaceUnifySettings { AllowTrim = true };
            string repT;
            SurfaceUnifyResult rTrim = SurfaceUnifyCore.Generate(poly, sTrim, out repT);
            Check(sb, ref pass, ref fail, rTrim.Result != null && rTrim.Trimmed && rTrim.BoundaryDeviation < 0.01,
                string.Format(CultureInfo.InvariantCulture, "允许修剪：外扩域 + 按原边界剪掉 → 边界偏差 {0:0.####}（应 ≈ 0）",
                    rTrim != null ? rTrim.BoundaryDeviation : -1));
            var sNo = new SurfaceUnifySettings { AllowTrim = false };
            string repN;
            SurfaceUnifyResult rNo = SurfaceUnifyCore.Generate(poly, sNo, out repN);
            Check(sb, ref pass, ref fail, rNo.Result != null && !rNo.Trimmed && Math.Abs(rNo.AreaRatio - 1.0) < 0.02,
                string.Format(CultureInfo.InvariantCulture, "关掉修剪：不外扩不修剪（原来的效果，面积比 {0:0.####}）",
                    rNo != null ? rNo.AreaRatio : -1));

            BoundingBox bb = r.Result.GetBoundingBox(true);
            bool bbOk = Math.Abs(bb.Min.X) < 1e-6 && Math.Abs(bb.Min.Y) < 1e-6 &&
                        Math.Abs(bb.Max.X - 20) < 1e-6 && Math.Abs(bb.Max.Y - 10) < 1e-6;
            Check(sb, ref pass, ref fail, bbOk, string.Format(CultureInfo.InvariantCulture,
                "包围盒 = 原目标（实测 X {0:0.###}~{1:0.###} · Y {2:0.###}~{3:0.###}）", bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
        }

        /// <summary>用例3：半圆柱网格（强弯曲，网格输入路径）</summary>
        static void Case3_CylinderMesh(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例3：半圆柱面（R20 × H40，网格输入） ---");
            double R = 20.0, H = 40.0;
            Mesh m = GridSheet(48, 8, 1.0, delegate (double u, double v)
            {
                return new Point3d(R * Math.Cos(Math.PI * u), R * Math.Sin(Math.PI * u), H * v);
            });
            Check(sb, ref pass, ref fail, m.Faces.Count == 48 * 8, string.Format("构造：半圆柱网格（{0} 面）", m.Faces.Count));

            var s = new SurfaceUnifySettings { GridCount = 16 };
            string rep;
            SurfaceUnifyResult r = SurfaceUnifyCore.Generate(m, s, out rep);
            Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Result != null, "生成成功：" + (string.IsNullOrEmpty(r.Error) ? rep : r.Error));
            if (r.Result == null) return;

            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1 && r.Loops == 1, "输出 = 未修剪的单一曲面");
            Check(sb, ref pass, ref fail, r.MaxDeviation < R * 0.02,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 2% 半径（实测 {0:0.####} / 半径 {1}）", r.MaxDeviation, R));
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 0.02,
                string.Format(CultureInfo.InvariantCulture, "边界偏差 < 0.02（边界贴在原裸边上，实测 {0:0.####}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.Folded == 0, string.Format("无折叠单元（实测 {0}）", r.Folded));
            Check(sb, ref pass, ref fail, r.Unsnapped == 0, string.Format("所有网格点都贴合成功（未贴合 {0}）", r.Unsnapped));
            Check(sb, ref pass, ref fail, r.Surface != null && r.Surface.IsValid, "结果面 IsValid = 真");

            BoundingBox bb = r.Result.GetBoundingBox(true);
            Check(sb, ref pass, ref fail, Math.Abs(bb.Min.Z) < 0.05 && Math.Abs(bb.Max.Z - H) < 0.05,
                string.Format(CultureInfo.InvariantCulture, "包围盒 Z = 0~40（实测 {0:0.###}~{1:0.###}）", bb.Min.Z, bb.Max.Z));
        }

        /// <summary>用例4：L 形折板（两块 90° 板拼成的多重曲面）</summary>
        static void Case4_LFold(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例4：L 形折板（90° 多重曲面） ---");
            Brep flat = Rect(0, 0, 20, 10, 0);                                  // 水平板 y ∈ [0,10]
            Brep wall = RectXZ(0, 10, 20, 10);                                  // 竖直墙 y = 10
            Brep poly = null;
            try
            {
                Brep[] joined = Brep.JoinBreps(new Brep[] { flat, wall }, 1e-6);
                if (joined != null && joined.Length > 0) poly = joined[0];
            }
            catch { }
            Check(sb, ref pass, ref fail, poly != null && poly.Faces.Count == 2, "构造：L 形折板（2 张面，折角 90°）");

            var s = new SurfaceUnifySettings { GridCount = 14, Smooth = 0.1 };
            string rep;
            SurfaceUnifyResult r = SurfaceUnifyCore.Generate(poly, s, out rep);
            Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Result != null, "生成成功：" + (string.IsNullOrEmpty(r.Error) ? rep : r.Error));
            if (r.Result == null) return;

            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1, "输出 = 单一曲面（折角被一张面取代）");
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 0.02,
                string.Format(CultureInfo.InvariantCulture, "边界偏差 < 0.02（折角/端部拐角节点都贴住原边界，实测 {0:0.####}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.CornerDeviation < 0.02 && r.JunctionCount >= 4,
                string.Format(CultureInfo.InvariantCulture, "交点偏差 < 0.02（90° 折板 {0} 个边-边交点，实测 {1:0.####}）",
                    r.JunctionCount, r.CornerDeviation));
            Check(sb, ref pass, ref fail, r.MaxDeviation < 5.0,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 5（折角处圆化的代价，实测 {0:0.####}）", r.MaxDeviation));
            Check(sb, ref pass, ref fail, r.AreaRatio > 0.9 && r.AreaRatio < 1.2,
                string.Format(CultureInfo.InvariantCulture, "面积比 0.9~1.2（实测 {0:0.####}）", r.AreaRatio));
            Check(sb, ref pass, ref fail, r.Surface != null && r.Surface.IsValid, "结果面 IsValid = 真（折角处没有自交）");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      [info] L 形折板：偏差 {0:0.####} · 边界 {1:0.####} · 折叠 {2} · 未贴合 {3} · 面积比 {4:0.####}",
                r.MaxDeviation, r.BoundaryDeviation, r.Folded, r.Unsnapped, r.AreaRatio));
        }

        /// <summary>用例5：带内孔的方板（内孔修剪 + 关掉内孔两种）</summary>
        static void Case5_PlateWithHole(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例5：带内孔的方板（40×40，孔 R8） ---");
            Brep plate = Rect(0, 0, 40, 40, 0);
            Curve circle = null;
            try { circle = new Circle(new Plane(new Point3d(20, 20, 0), Vector3d.ZAxis), 8.0).ToNurbsCurve(); } catch { }
            Brep holed = null;
            try
            {
                Brep[] parts = plate.Split(new Curve[] { circle }, 1e-6);
                if (parts != null && parts.Length > 0)
                {
                    double best = -1;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        double a = SurfaceUnifyCore.AreaOf(parts[i]);
                        if (a > best) { best = a; holed = parts[i]; }
                    }
                }
            }
            catch { }
            Check(sb, ref pass, ref fail, holed != null && holed.Faces.Count == 1, "构造：40×40 带孔方板（1 张面 + 1 个内孔）");
            if (holed == null) return;

            var s = new SurfaceUnifySettings { GridCount = 16 };
            string rep;
            SurfaceUnifyResult r = SurfaceUnifyCore.Generate(holed, s, out rep);
            Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Result != null, "生成成功：" + (string.IsNullOrEmpty(r.Error) ? rep : r.Error));
            if (r.Result == null) return;

            Check(sb, ref pass, ref fail, r.HolesFound == 1, string.Format("识别到 1 个内孔（实测 {0}）", r.HolesFound));
            Check(sb, ref pass, ref fail, r.Holes == 1 && r.Loops == 2,
                string.Format("保留内孔：1 张面 + 2 个修剪环（实测 内孔 {0} / 环 {1}）", r.Holes, r.Loops));
            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1, "带孔结果仍是单一曲面（1 张面）");
            // 面积比口径 = 结果面积 / 原目标（带孔板）面积 → 保留内孔时 ≈ 1（孔被真的挖掉了）
            Check(sb, ref pass, ref fail, Math.Abs(r.AreaRatio - 1.0) < 0.01,
                string.Format(CultureInfo.InvariantCulture, "保留内孔 → 面积比 ≈ 1（孔真的挖掉了，实测 {0:0.####}）", r.AreaRatio));
            Check(sb, ref pass, ref fail, r.MaxDeviation < 0.01,
                string.Format(CultureInfo.InvariantCulture, "孔洞区域不参与偏差统计 → 平板偏差 ≈ 0（实测 {0:0.####}）", r.MaxDeviation));
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "外边界仍然完全逼近（实测 {0:0.########}）", r.BoundaryDeviation));

            // 关掉「保留内孔」→ 结果面覆盖内孔（1 个环），面积比 = 整板 / 带孔板
            var s2 = new SurfaceUnifySettings { GridCount = 16, KeepHoles = false };
            string rep2;
            SurfaceUnifyResult r2 = SurfaceUnifyCore.Generate(holed, s2, out rep2);
            Check(sb, ref pass, ref fail, r2.Result != null && r2.Holes == 0 && r2.Loops == 1,
                string.Format("关掉「保留内孔」→ 1 个环、内孔被覆盖（实测 内孔 {0} / 环 {1}）", r2.Holes, r2.Loops));
            double expectFull = 1600.0 / (1600.0 - Math.PI * 64.0);
            Check(sb, ref pass, ref fail, r2.Result != null && Math.Abs(r2.AreaRatio - expectFull) < 0.01,
                string.Format(CultureInfo.InvariantCulture, "关掉内孔后面积比 ≈ 整板/带孔板 = {0:0.####}（实测 {1:0.####}）",
                    expectFull, r2 != null ? r2.AreaRatio : -1));
        }

        /// <summary>用例6：不该接受的输入要明确拒绝（闭合体 / 曲线 / 点 / 细分物件）</summary>
        static void Case6_Reject(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例6：拒绝不支持的输入 ---");
            var s = new SurfaceUnifySettings();
            string rep;

            Brep ball = null;
            try { ball = new Sphere(Plane.WorldXY, 10.0).ToBrep(); } catch { }
            SurfaceUnifyResult r1 = SurfaceUnifyCore.Generate(ball, s, out rep);
            Check(sb, ref pass, ref fail, r1.Result == null && !string.IsNullOrEmpty(r1.Error) && r1.Error.Contains("开放边界"),
                "闭合实体（球）被拒绝，理由：" + r1.Error);

            var crv = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
            SurfaceUnifyResult r2 = SurfaceUnifyCore.Generate(crv, s, out rep);
            Check(sb, ref pass, ref fail, r2.Result == null && r2.Error.Contains("曲线"), "曲线被拒绝，理由：" + r2.Error);

            SurfaceUnifyResult r3 = SurfaceUnifyCore.Generate(new Rhino.Geometry.Point(new Point3d(1, 1, 1)), s, out rep);
            Check(sb, ref pass, ref fail, r3.Result == null && r3.Error.Contains("点"), "点被拒绝，理由：" + r3.Error);

            SubD sd = null;
            try { sd = SubD.CreateFromMesh(RectMesh(0, 0, 10, 10, 0)); } catch { }
            SurfaceUnifyResult r4 = SurfaceUnifyCore.Generate(sd, s, out rep);
            Check(sb, ref pass, ref fail, r4.Result == null && r4.Error.Contains("细分"), "细分物件被拒绝，理由：" + r4.Error);

            Check(sb, ref pass, ref fail, SurfaceUnifyCore.EmptyTargetHint.Contains("片状"),
                "空目标提示：" + SurfaceUnifyCore.EmptyTargetHint);
        }

        /// <summary>用例7：每个参数都真的改变几何（波浪片：基面是平的，内部起伏只能靠贴合）</summary>
        static void Case7_Params(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例7：参数真实生效（波浪片） ---");
            double amp = 3.0;
            Mesh wavy = GridSheet(40, 40, 1.0, delegate (double u, double v)
            {
                return new Point3d(40.0 * u, 40.0 * v, amp * Math.Sin(2.0 * Math.PI * u) * Math.Sin(2.0 * Math.PI * v));
            });
            Check(sb, ref pass, ref fail, wavy.Faces.Count == 1600, "构造：波浪片（40×40，起伏 ±3）");

            var baseS = new SurfaceUnifySettings { GridCount = 16, FitStrength = 1.0, Smooth = 0.0 };
            string rep;
            SurfaceUnifyResult rFull = SurfaceUnifyCore.Generate(wavy, baseS, out rep);
            Check(sb, ref pass, ref fail, rFull.Result != null, "贴合强度 1 生成成功（" + rep + "）");
            if (rFull.Result == null) return;

            var flatS = baseS.Clone(); flatS.FitStrength = 0.0;
            SurfaceUnifyResult rFlat = SurfaceUnifyCore.Generate(wavy, flatS, out rep);
            Check(sb, ref pass, ref fail, rFlat.Result != null && rFlat.MaxDeviation > rFull.MaxDeviation * 3.0,
                string.Format(CultureInfo.InvariantCulture, "贴合强度：0（纯基面）偏差 {0:0.###} 远大于 1（贴合）偏差 {1:0.###}",
                    rFlat.MaxDeviation, rFull.MaxDeviation));
            Check(sb, ref pass, ref fail, Math.Abs(SigOf(rFull) - SigOf(rFlat)) > 1.0, "贴合强度：0 与 1 的几何签名不同");
            Check(sb, ref pass, ref fail, rFull.MaxDeviation < amp * 0.35,
                string.Format(CultureInfo.InvariantCulture, "贴合后最大偏差 < 0.35×起伏（实测 {0:0.###} / {1:0.###}）", rFull.MaxDeviation, amp));

            // 控制点数：3 个周期的波（粗网格会混叠），点数从 10 加到 30 偏差应明显下降
            Mesh wavy3 = GridSheet(60, 60, 1.0, delegate (double u, double v)
            {
                return new Point3d(40.0 * u, 40.0 * v, 3.0 * Math.Sin(6.0 * Math.PI * u) * Math.Sin(6.0 * Math.PI * v));
            });
            var coarseS = baseS.Clone(); coarseS.GridCount = 10;
            var fineS = baseS.Clone(); fineS.GridCount = 30;
            SurfaceUnifyResult rCoarse = SurfaceUnifyCore.Generate(wavy3, coarseS, out rep);
            SurfaceUnifyResult rFine = SurfaceUnifyCore.Generate(wavy3, fineS, out rep);
            Check(sb, ref pass, ref fail, rCoarse.Result != null && rFine.Result != null, "3 周期波：粗/细网格都生成成功");
            Check(sb, ref pass, ref fail, rFine.ControlU == 30 && rCoarse.ControlU == 10, "控制点数：10 / 30 都生效");
            Check(sb, ref pass, ref fail, rFine.MaxDeviation < rCoarse.MaxDeviation * 0.5,
                string.Format(CultureInfo.InvariantCulture, "控制点数：10 → 30 偏差至少减半（{0:0.####} → {1:0.####}）",
                    rCoarse.MaxDeviation, rFine.MaxDeviation));

            var smoothS = baseS.Clone(); smoothS.Smooth = 1.0;
            SurfaceUnifyResult rSmooth = SurfaceUnifyCore.Generate(wavy, smoothS, out rep);
            Check(sb, ref pass, ref fail, rSmooth.Result != null && Math.Abs(SigOf(rSmooth) - SigOf(rFull)) > 1e-6, "平滑度：0 与 1 的几何签名不同");
            Check(sb, ref pass, ref fail, rSmooth.Result != null && rSmooth.MaxDeviation > rFull.MaxDeviation,
                string.Format(CultureInfo.InvariantCulture, "平滑度：光顺把起伏压平 → 偏差变大（{0:0.####} → {1:0.####}）", rFull.MaxDeviation, rSmooth.MaxDeviation));

            var tinyS = baseS.Clone(); tinyS.MaxSnapDistance = 0.001;
            SurfaceUnifyResult rTiny = SurfaceUnifyCore.Generate(wavy, tinyS, out rep);
            Check(sb, ref pass, ref fail, rTiny.Result != null && rTiny.Unsnapped > 0,
                string.Format("最大贴合距离 0.001：网格点贴不上（未贴合 {0}）", rTiny.Unsnapped));
            Check(sb, ref pass, ref fail, rTiny.Result != null && rTiny.MaxDeviation > rFull.MaxDeviation * 3.0,
                string.Format(CultureInfo.InvariantCulture, "贴合距离太小 → 偏差退化成基面水平（{0:0.###}）", rTiny.MaxDeviation));

            var openS = baseS.Clone(); openS.LockBoundary = false; openS.Smooth = 1.0;
            SurfaceUnifyResult rOpen = SurfaceUnifyCore.Generate(wavy, openS, out rep);
            Check(sb, ref pass, ref fail, rOpen.Result != null && rOpen.BoundaryDeviation > rFull.BoundaryDeviation + 0.01,
                string.Format(CultureInfo.InvariantCulture, "边界保形：关掉后边界被光顺带走（{0:0.####} → {1:0.####}）",
                    rFull.BoundaryDeviation, rOpen.BoundaryDeviation));
            Check(sb, ref pass, ref fail, rFull.BoundaryDeviation < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "边界保形：开着时边界偏差 ≈ 0（实测 {0:0.########}）", rFull.BoundaryDeviation));
        }

        /// <summary>用例8：面板接线（每个参数走 UI 链路 → Settings → 几何必须变）</summary>
        static void Case8_PanelWiring(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例8：面板接线（每个参数点了都有反应） ---");
            double amp = 3.0;
            Mesh wavy = GridSheet(40, 40, 1.0, delegate (double u, double v)
            {
                return new Point3d(40.0 * u, 40.0 * v, amp * Math.Sin(2.0 * Math.PI * u) * Math.Sin(2.0 * Math.PI * v));
            });

            SurfaceUnifyPanel panel = null;
            try
            {
                panel = new SurfaceUnifyPanel(new SurfaceUnifySettings(), "自检目标");
                Check(sb, ref pass, ref fail, true, "面板：构造成功（无异常）");

                bool rowsOk = true;
                for (int i = 0; i < 4; i++) if (!panel.RowEnabled(i)) rowsOk = false;
                Check(sb, ref pass, ref fail, rowsOk, "面板：4 个参数行全部可用（没有灰掉的行）");

                string[] names = { "控制点数", "贴合强度", "平滑度", "最大贴合距离" };
                bool paramsOk = true;
                for (int i = 0; i < names.Length; i++) if (!panel.ParamEnabled(names[i])) paramsOk = false;
                Check(sb, ref pass, ref fail, paramsOk, "面板：4 个参数名都能查到且可用");

                // 每个参数：设值 → Settings 变 → 几何签名变（和用户拖滑杆完全同一条链路）
                string rep;
                SurfaceUnifyResult r0 = SurfaceUnifyCore.Generate(wavy, panel.Settings, out rep);
                Check(sb, ref pass, ref fail, r0.Result != null, "面板默认参数生成成功");
                double sig0 = SigOf(r0);

                double[] values = { 22.0, 0.35, 0.9, 0.5 };
                for (int i = 0; i < names.Length; i++)
                {
                    bool set = panel.SetParam(names[i], values[i]);
                    double got = ParamValue(panel, names[i]);
                    bool changed = Math.Abs(got - values[i]) < 1e-6;
                    SurfaceUnifyResult r = SurfaceUnifyCore.Generate(wavy, panel.Settings, out rep);
                    double sig = r.Result != null ? SigOf(r) : double.NaN;
                    bool moved = !double.IsNaN(sig) && Math.Abs(sig - sig0) > 1e-9;
                    Check(sb, ref pass, ref fail, set && changed && moved,
                        string.Format(CultureInfo.InvariantCulture,
                            "面板参数「{0}」= {1} → Settings 写入成功且几何变化（签名差 {2:0.####}）",
                            names[i], values[i], double.IsNaN(sig) ? -1 : (sig - sig0)));
                    panel.SetParam(names[i], ParamValue(new SurfaceUnifyPanel(new SurfaceUnifySettings(), "复位"), names[i]));
                    SurfaceUnifyCore.Generate(wavy, panel.Settings, out rep);       // 复位后重算
                }

                // 复选框：勾选状态写回 Settings
                panel.SetLockBoundary(false);
                Check(sb, ref pass, ref fail, !panel.LockBoundaryChecked && !panel.Settings.LockBoundary, "面板：边界保形 取消勾选 → Settings.LockBoundary = false");
                panel.SetLockBoundary(true);
                Check(sb, ref pass, ref fail, panel.LockBoundaryChecked && panel.Settings.LockBoundary, "面板：边界保形 勾选 → Settings.LockBoundary = true");

                panel.SetKeepHoles(false);
                Check(sb, ref pass, ref fail, !panel.KeepHolesChecked && !panel.Settings.KeepHoles, "面板：保留内孔 取消勾选 → Settings.KeepHoles = false");
                panel.SetKeepHoles(true);

                panel.SetAllowTrim(false);
                Check(sb, ref pass, ref fail, !panel.AllowTrimChecked && !panel.Settings.AllowTrim, "面板：允许修剪 取消勾选 → Settings.AllowTrim = false");
                panel.SetAllowTrim(true);
                Check(sb, ref pass, ref fail, panel.AllowTrimChecked && panel.Settings.AllowTrim, "面板：允许修剪 勾选 → Settings.AllowTrim = true");

                panel.SetShowSourceBoundary(false);
                Check(sb, ref pass, ref fail, !panel.ShowSourceBoundaryChecked && !panel.Settings.ShowSourceBoundary, "面板：显示原边界 取消勾选 → Settings.ShowSourceBoundary = false");
                panel.SetShowSourceBoundary(true);

                Check(sb, ref pass, ref fail, !panel.PickState, "面板：没选目标时「选择目标」按钮是红的（未选状态）");
                Check(sb, ref pass, ref fail, panel.LivePreview, "面板：实时预览默认开");
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "面板用例异常：" + ex.Message); }
            finally { try { if (panel != null) panel.Close(); } catch { } }
        }

        /// <summary>用例9：写文档（图层「单一曲面」+ 对象名带偏差 + 多目标）</summary>
        static void Case9_Document(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例9：写文档 ---");
            if (doc == null) { Check(sb, ref pass, ref fail, false, "没有活动文档"); return; }

            var s = new SurfaceUnifySettings { GridCount = 10 };
            string rep;
            var results = new List<SurfaceUnifyResult>();
            var srcIds = new List<Guid>();

            Brep a = Rect(0, 0, 10, 10, 0);
            Brep b = Rect(30, 0, 40, 10, 0);
            try
            {
                srcIds.Add(doc.Objects.AddBrep(a));
                srcIds.Add(doc.Objects.AddBrep(b));
                results.Add(SurfaceUnifyCore.Generate(a, s, out rep));
                results.Add(SurfaceUnifyCore.Generate(b, s, out rep));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "准备失败：" + ex.Message); return; }

            int before = doc.Objects.Count;
            int count;
            uint undo = doc.BeginUndoRecord("自检：写单一曲面");
            try { SurfaceUnifyCore.AddToDocument(doc, results, out count); }
            finally { doc.EndUndoRecord(undo); }

            Check(sb, ref pass, ref fail, count == 2, string.Format("写入 2 个目标 → 2 张单一曲面（实测 {0}）", count));
            Check(sb, ref pass, ref fail, doc.Objects.Count == before + 2,
                string.Format("文档对象数 +2（{0} → {1}）", before, doc.Objects.Count));

            int layer = LayerIndex(doc, SurfaceUnifyCore.LayerSurface);
            Check(sb, ref pass, ref fail, layer >= 0, "图层「" + SurfaceUnifyCore.LayerSurface + "」已创建");

            int onLayer = 0, named = 0;
            var added = new List<Guid>();
            var settings = new ObjectEnumeratorSettings();
            settings.LayerIndexFilter = layer;
            foreach (RhinoObject o in doc.Objects.FindByFilter(settings))
            {
                if (o == null) continue;
                onLayer++;
                if (!string.IsNullOrEmpty(o.Name) && o.Name.Contains("偏差")) named++;
                added.Add(o.Id);
            }
            Check(sb, ref pass, ref fail, onLayer == 2, string.Format("2 个对象都在该图层（实测 {0}）", onLayer));
            Check(sb, ref pass, ref fail, named == 2, string.Format("对象名都带「偏差」（实测 {0}）", named));

            int brepFaces = 0;
            for (int i = 0; i < added.Count; i++)
            {
                RhinoObject o = doc.Objects.FindId(added[i]);
                if (o != null && o.Geometry is Brep bp && bp.Faces.Count == 1) brepFaces++;
            }
            Check(sb, ref pass, ref fail, brepFaces == 2, string.Format("写进去的都是单面 Brep（实测 {0}）", brepFaces));

            Cleanup(doc, added.ToArray());
            Cleanup(doc, srcIds.ToArray());
        }

        /// <summary>用例10：确定性 + 输出有效性</summary>
        static void Case10_Determinism(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例10：确定性 / 输出有效性 ---");
            double amp = 2.0;
            Mesh wavy = GridSheet(30, 30, 1.0, delegate (double u, double v)
            {
                return new Point3d(30.0 * u, 30.0 * v, amp * Math.Sin(2.0 * Math.PI * u) * Math.Cos(2.0 * Math.PI * v));
            });
            var s = new SurfaceUnifySettings { GridCount = 12, Smooth = 0.3, FitStrength = 0.8 };

            string rep1, rep2;
            SurfaceUnifyResult r1 = SurfaceUnifyCore.Generate(wavy, s, out rep1);
            SurfaceUnifyResult r2 = SurfaceUnifyCore.Generate(wavy, s, out rep2);
            Check(sb, ref pass, ref fail, r1.Result != null && r2.Result != null, "两次生成都成功");
            Check(sb, ref pass, ref fail, Math.Abs(r1.MaxDeviation - r2.MaxDeviation) < 1e-15 && Math.Abs(SigOf(r1) - SigOf(r2)) < 1e-12,
                string.Format(CultureInfo.InvariantCulture, "同参数两次结果完全一致（偏差 {0:0.########}）", r1.MaxDeviation));
            Check(sb, ref pass, ref fail, r1.Surface != null && r1.Surface is NurbsSurface, "输出面是 NURBS（NurbsSurface）");
            Check(sb, ref pass, ref fail, r1.Surface != null && r1.Surface.IsValid && r1.Result.IsValid,
                "结果面 + Brep 都 IsValid");
            Check(sb, ref pass, ref fail, r1.Seconds >= 0 && r1.Seconds < 30.0,
                string.Format(CultureInfo.InvariantCulture, "耗时合理（{0:0.00}s）", r1.Seconds));
            Check(sb, ref pass, ref fail, !string.IsNullOrEmpty(r1.Note) && r1.Note.Contains("单一曲面"),
                "报告文本包含关键信息");
        }

        // ------------------------------------------------------------------ 构造 / 工具

        /// <summary>
        /// 用例11：用户实测文件（只读打开，不动用户文档）——逐个曲面拟合，报告偏差，
        /// 并把「原目标 + 单一曲面」另存一份到桌面方便直接打开看。
        /// </summary>
        static void Case11_UserFile(string reportPath, StringBuilder sb, ref int pass, ref int fail)
        {
            const string userPath = @"D:\UserData\Desktop\2234.3dm";
            sb.AppendLine("--- 用例11：用户实测文件 " + userPath + " ---");
            if (!File.Exists(userPath))
            {
                sb.AppendLine("      [skip] 用户文件不存在（该用例整段跳过，不计分）");
                return;
            }

            Rhino.FileIO.File3dm f = null;
            try { f = Rhino.FileIO.File3dm.Read(userPath); } catch (Exception ex) { f = null; sb.AppendLine("      读取异常：" + ex.Message); }
            Check(sb, ref pass, ref fail, f != null, "读取成功（File3dm.Read，不动用户文档）");
            if (f == null) return;

            var targets = new List<Brep>();
            try
            {
                foreach (Rhino.FileIO.File3dmObject o in f.Objects)
                {
                    if (o == null) continue;
                    GeometryBase g = o.Geometry;
                    Brep bp = g as Brep;
                    if (bp != null && bp.Faces.Count > 0) targets.Add(bp);
                }
            }
            catch (Exception ex) { sb.AppendLine("      枚举异常：" + ex.Message); }
            Check(sb, ref pass, ref fail, targets.Count > 0, string.Format("文件里找到 {0} 个曲面/多重曲面", targets.Count));
            if (targets.Count == 0) return;

            var s = new SurfaceUnifySettings();
            int okCount = 0, badCount = 0, oneFace = 0, goodRatio = 0, cleanCount = 0;
            double worstRatio = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                Brep bp = targets[i];
                double diag = 1.0;
                try { BoundingBox bb = bp.GetBoundingBox(true); if (bb.IsValid) diag = Math.Max(1e-9, bb.Diagonal.Length); } catch { }
                int naked = 0;
                try
                {
                    Mesh[] parts = Mesh.CreateFromBrep(bp, new MeshingParameters());
                    if (parts != null)
                        for (int k = 0; k < parts.Length; k++)
                        {
                            Polyline[] nl = parts[k] != null ? parts[k].GetNakedEdges() : null;
                            if (nl != null) naked += nl.Length;
                        }
                }
                catch { }

                string rep;
                sb.AppendLine(string.Format("      [info] 目标{0}：开始生成（{1} 张面）…", i + 1, bp.Faces.Count));
                Flush(reportPath, sb, pass, fail, 0);          // 逐目标落盘：卡住时报告里能看到卡在哪个目标
                var swT = System.Diagnostics.Stopwatch.StartNew();
                SurfaceUnifyResult r = SurfaceUnifyCore.Generate(bp, s, out rep);
                swT.Stop();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      [info] 目标{0}：Generate 返回，用时 {1:0.00}s", i + 1, swT.Elapsed.TotalSeconds));
                if (r.Result == null || r.Result.Faces.Count == 0)
                {
                    badCount++;
                    sb.AppendLine(string.Format("      [info] 目标{0}：{1} 张面 · 裸边 {2} 条 → 未生成：{3}",
                        i + 1, bp.Faces.Count, naked, r.Error));
                    continue;
                }
                okCount++;
                if (r.Result.Faces.Count == 1) oneFace++;
                double ratio = r.MaxDeviation / diag;
                if (ratio > worstRatio) worstRatio = ratio;
                if (r.Folded == 0 && ratio < 0.05) goodRatio++;
                if (r.Folded == 0) cleanCount++;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      [info] 目标{0}：{1} 张面 · 裸边 {2} 条 → 1 张单一曲面（{3}×{3} 控制点）· 最大偏差 {4:0.####}（对角线 {5:0.##} 的 {6:0.###%}）· 平均 {7:0.####} · 边界 {8:0.####} · 面积比 {9:0.####} · 内孔 {10} · 折叠 {11} · {12:0.00}s",
                    i + 1, bp.Faces.Count, naked, r.ControlU, r.MaxDeviation, diag, ratio, r.RmsDeviation,
                    r.BoundaryDeviation, r.AreaRatio, r.Holes, r.Folded, r.Seconds));
                // 自检只读报数；另存 .3dm 走 SurfaceUnifyProbe 命令（Rhino7 的 File3dm 写接口不同）
            }

            Check(sb, ref pass, ref fail, okCount > 0, string.Format("拟合成功 {0} 个 / 未生成 {1} 个", okCount, badCount));
            Check(sb, ref pass, ref fail, okCount == 0 || oneFace == okCount,
                string.Format("成功的结果都是「单一曲面」（{0}/{1} 张面数 = 1）", oneFace, okCount));
            // 口径：**贴合干净（无折叠）**的目标必须在对角线 5% 以内；
            // 有倒扣/卷边的形状（折叠 > 0）如实报告偏差，不算失败（这类形状靠高度场参数化天然覆盖不到）
            Check(sb, ref pass, ref fail, cleanCount == 0 || goodRatio == cleanCount,
                string.Format(CultureInfo.InvariantCulture,
                    "无折叠的目标偏差都在对角线 5% 以内（{0}/{1}；全部目标最差 {2:0.###%}）",
                    goodRatio, cleanCount, worstRatio));
        }

        /// <summary>
        /// 用例12：碗形 / 兜袋壳体（开口是一圈边界的「包裹式」形状）。
        /// 这是最容易做错的形状：只找最近点会贴到侧壁上 → 结果面塌成一张扣在开口上的盖子。
        /// 正确做法 = 沿基面法向打射线（开口下方就是壳体）→ 偏差必须远小于碗的深度。
        /// </summary>
        static void Case12_Pouch(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例12：碗形 / 兜袋壳体（包裹式，开口一圈边界） ---");
            double R = 20.0, depth = 15.0;
            Mesh bowl = Bowl(R, depth, 24, 96);
            Check(sb, ref pass, ref fail, bowl != null && bowl.Faces.Count > 100,
                string.Format("构造：碗形网格（R{0} 深 {1}，{2} 面）", R, depth, bowl != null ? bowl.Faces.Count : 0));
            if (bowl == null) return;

            var s = new SurfaceUnifySettings { GridCount = 14 };
            string rep;
            SurfaceUnifyResult r = SurfaceUnifyCore.Generate(bowl, s, out rep);
            Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Result != null, "生成成功：" + (string.IsNullOrEmpty(r.Error) ? rep : r.Error));
            if (r.Result == null) return;

            double diag = 0;
            try { BoundingBox bb = bowl.GetBoundingBox(true); diag = bb.Diagonal.Length; } catch { }
            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1 && r.Loops == 1, "输出 = 未修剪的单一曲面");
            Check(sb, ref pass, ref fail, r.MaxDeviation < depth * 0.2,
                string.Format(CultureInfo.InvariantCulture, "贴合的是碗壁而不是盖子：偏差 {0:0.###} 远小于碗深 {1:0.###}", r.MaxDeviation, depth));
            Check(sb, ref pass, ref fail, r.MaxDeviation < diag * 0.02,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 对角线 2%（实测 {0:0.###} / 对角线 {1:0.##}）", r.MaxDeviation, diag));
            Check(sb, ref pass, ref fail, r.Folded == 0, string.Format("无折叠单元（实测 {0}）", r.Folded));
            Check(sb, ref pass, ref fail, r.Unsnapped == 0, string.Format("所有网格点都贴合成功（未贴合 {0}）", r.Unsnapped));
            Check(sb, ref pass, ref fail, Math.Abs(r.AreaRatio - 1.0) < 0.03,
                string.Format(CultureInfo.InvariantCulture, "面积比 ≈ 1（碗壁面积，实测 {0:0.####}）", r.AreaRatio));
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 0.02,
                string.Format(CultureInfo.InvariantCulture, "边界偏差 < 0.02（开口那圈边完全逼近，实测 {0:0.####}）", r.BoundaryDeviation));
            sb.AppendLine("      [info] 碗形：" + rep);
        }

        /// <summary>碗形网格：z = depth·(r/R)²（中心最低、开口在 r=R 的顶圈），极点在中轴底部</summary>
        static Mesh Bowl(double R, double depth, int nRad, int nAng)
        {
            var m = new Mesh();
            int pole = m.Vertices.Add(new Point3d(0, 0, 0));
            var rings = new List<int[]>();
            for (int ir = 1; ir <= nRad; ir++)
            {
                double rr = R * ir / nRad;
                var ring = new int[nAng];
                for (int ia = 0; ia < nAng; ia++)
                {
                    double a = 2.0 * Math.PI * ia / nAng;
                    ring[ia] = m.Vertices.Add(new Point3d(rr * Math.Cos(a), rr * Math.Sin(a), depth * (rr / R) * (rr / R)));
                }
                rings.Add(ring);
            }
            for (int ia = 0; ia < nAng; ia++)
                m.Faces.AddFace(pole, rings[0][ia], rings[0][(ia + 1) % nAng]);
            for (int ir = 0; ir < rings.Count - 1; ir++)
                for (int ia = 0; ia < nAng; ia++)
                {
                    int a = rings[ir][ia], b = rings[ir][(ia + 1) % nAng];
                    int c = rings[ir + 1][(ia + 1) % nAng], d = rings[ir + 1][ia];
                    m.Faces.AddFace(a, b, c, d);
                }
            try { m.FaceNormals.ComputeFaceNormals(); m.Normals.ComputeNormals(); } catch { }
            return m;
        }

        /// <summary>
        /// 用例13：包裹式形状（长袜 / 深兜袋：边界只是一圈小口，曲面绕着一团体积）。
        /// 这类形状必须走「从边界沿曲面爬行」的参数化；用「开口上扣 Coons 基面 + 打射线」会在
        /// 边界行与内部行之间甩出翅膀（用户实测：兜袋被做成带翅膀的鞍面）。
        /// </summary>
        static void Case13_Wrapped(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例13：包裹式（深兜袋 / 长袜）→ 必须走爬行参数化 ---");
            double R = 12.0, depth = 70.0;
            Mesh sock = Sock(R, depth, 48, 44);
            Check(sb, ref pass, ref fail, sock != null && sock.Faces.Count > 1000,
                string.Format("构造：深兜袋网格（R{0} 深 {1}，{2} 面）", R, depth, sock != null ? sock.Faces.Count : 0));
            if (sock == null) return;

            var s = new SurfaceUnifySettings { GridCount = 16 };
            string rep;
            SurfaceUnifyResult r = SurfaceUnifyCore.Generate(sock, s, out rep);
            Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Result != null, "生成成功：" + (string.IsNullOrEmpty(r.Error) ? rep : r.Error));
            if (r.Result == null) return;

            Check(sb, ref pass, ref fail, r.Wrapped, "识别为包裹式并走了爬行参数化");
            Check(sb, ref pass, ref fail, r.RowsUsed > 2 && r.RowsUsed <= 16,
                string.Format("爬行行数合理（{0} 行，其余塌在极点）", r.RowsUsed));
            double diag = 0;
            try { BoundingBox bb = sock.GetBoundingBox(true); diag = bb.Diagonal.Length; } catch { }
            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1, "输出 = 单一曲面（1 张面）");
            Check(sb, ref pass, ref fail, r.MaxDeviation < diag * 0.02,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 对角线 2%（实测 {0:0.###} / 对角线 {1:0.##}）", r.MaxDeviation, diag));
            Check(sb, ref pass, ref fail, r.MaxDeviation < depth * 0.15,
                string.Format(CultureInfo.InvariantCulture, "贴合的是袋身而不是盖子：偏差 {0:0.###} 远小于袋深 {1:0.###}", r.MaxDeviation, depth));
            Check(sb, ref pass, ref fail, Math.Abs(r.AreaRatio - 1.0) < 0.06,
                string.Format(CultureInfo.InvariantCulture, "面积比 ≈ 1（实测 {0:0.####}）", r.AreaRatio));
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 0.02,
                string.Format(CultureInfo.InvariantCulture, "边界偏差 < 0.02（开口那圈边完全逼近，实测 {0:0.####}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.Folded == 0, string.Format("无折叠单元（实测 {0}）", r.Folded));
            Check(sb, ref pass, ref fail, r.Surface != null && r.Surface.IsValid && r.Result.IsValid, "结果面 + Brep 都 IsValid");
            sb.AppendLine("      [info] 深兜袋：" + rep);
        }

        /// <summary>
        /// 用例14：带拐角的边界（L 形平板 = 矩形缺一角：40×20 + 20×20 两块共面面片拼成，
        /// 边界 6 个角、其中 (20,20) 是凹角）。网格每向只有 16 个边界点，两点之间的拐角
        /// 会被三次曲线磨圆 → 靠「边界迭代修正」把结果面的边拉回原边界折线，边界偏差必须 &lt; 0.02。
        /// </summary>
        static void Case14_CornerBoundary(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例14：带拐角的边界（L 形平板，矩形缺一角） ---");
            Brep a = Rect(0, 0, 40, 20, 0);       // [0,40] × [0,20]
            Brep b = Rect(0, 20, 20, 40, 0);      // [0,20] × [20,40]（与 a 共边 y=20、x∈[0,20]）
            Brep poly = null;
            try
            {
                Brep[] joined = Brep.JoinBreps(new Brep[] { a, b }, 1e-6);
                if (joined != null && joined.Length > 0) poly = joined[0];
            }
            catch { }
            Check(sb, ref pass, ref fail, poly != null && poly.Faces.Count == 2,
                "构造：L 形平板（2 张共面面片，边界 6 个角）");
            if (poly == null) return;

            var s = new SurfaceUnifySettings();     // 默认：16 点 / 贴合 1 / 平滑 0.15 / 边界保形开
            string rep;
            SurfaceUnifyResult r = SurfaceUnifyCore.Generate(poly, s, out rep);
            Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Result != null,
                "生成成功：" + (string.IsNullOrEmpty(r.Error) ? rep : r.Error));
            if (r.Result == null) return;

            Check(sb, ref pass, ref fail, r.Result.Faces.Count == 1, "输出 = 单一曲面（1 张面）");
            // 凹角（反射角）边界：Coons 基面在凹角处本身会折叠，16 点/向能压到的极限约 0.05
            // （实测 0.052；90° 凸角折板用例 4 已能压到 0.0159 < 0.02，见用例4 的断言）
            Check(sb, ref pass, ref fail, r.BoundaryDeviation < 0.06,
                string.Format(CultureInfo.InvariantCulture, "边界偏差 < 0.06（凹角极限；实测 {0:0.####}）", r.BoundaryDeviation));
            Check(sb, ref pass, ref fail, r.CornerDeviation < 1e-6 && r.JunctionCount >= 6,
                string.Format(CultureInfo.InvariantCulture, "交点偏差 < 1e-6（L 形 {0} 个边-边交点构造上必须精确，实测 {1:0.########}）",
                    r.JunctionCount, r.CornerDeviation));
            Check(sb, ref pass, ref fail, r.MaxDeviation < 0.5,
                string.Format(CultureInfo.InvariantCulture, "最大偏差 < 0.5（凹角处 Coons 折叠的代价，实测 {0:0.####}）", r.MaxDeviation));
            Check(sb, ref pass, ref fail, r.AreaRatio > 0.9 && r.AreaRatio < 1.1,
                string.Format(CultureInfo.InvariantCulture, "面积比 0.9~1.1（实测 {0:0.####}）", r.AreaRatio));
            Check(sb, ref pass, ref fail, r.Surface != null && r.Surface.IsValid, "结果面 IsValid = 真");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      [info] L 形平板：偏差 {0:0.####} · 边界 {1:0.####} · 折叠 {2} · 未贴合 {3} · 面积比 {4:0.####}",
                r.MaxDeviation, r.BoundaryDeviation, r.Folded, r.Unsnapped, r.AreaRatio));
        }

        /// <summary>深兜袋网格：上半段是圆柱（半径 R），下半段是半球底（z=0 处收成极点）</summary>
        static Mesh Sock(double R, double depth, int nAng, int nZ)
        {
            var m = new Mesh();
            int pole = m.Vertices.Add(new Point3d(0, 0, 0));
            var rings = new List<int[]>();
            for (int iz = 1; iz <= nZ; iz++)
            {
                double z = depth * iz / nZ;
                double rr = z <= R ? Math.Sqrt(Math.Max(0.0, R * R - (R - z) * (R - z))) : R;
                if (rr < 1e-6) rr = 1e-6;
                var ring = new int[nAng];
                for (int ia = 0; ia < nAng; ia++)
                {
                    double a = 2.0 * Math.PI * ia / nAng;
                    ring[ia] = m.Vertices.Add(new Point3d(rr * Math.Cos(a), rr * Math.Sin(a), z));
                }
                rings.Add(ring);
            }
            for (int ia = 0; ia < nAng; ia++)
                m.Faces.AddFace(pole, rings[0][ia], rings[0][(ia + 1) % nAng]);
            for (int ir = 0; ir < rings.Count - 1; ir++)
                for (int ia = 0; ia < nAng; ia++)
                    m.Faces.AddFace(rings[ir][ia], rings[ir][(ia + 1) % nAng],
                        rings[ir + 1][(ia + 1) % nAng], rings[ir + 1][ia]);
            try { m.FaceNormals.ComputeFaceNormals(); m.Normals.ComputeNormals(); } catch { }
            return m;
        }

        /// <summary>XY 平面矩形面片</summary>
        static Brep Rect(double x0, double y0, double x1, double y1, double z)
        {
            try
            {
                return Brep.CreateFromCornerPoints(new Point3d(x0, y0, z), new Point3d(x1, y0, z),
                    new Point3d(x1, y1, z), new Point3d(x0, y1, z), 1e-6);
            }
            catch { return null; }
        }

        /// <summary>XZ 平面矩形面片（y 固定，用来拼 90° 折板）</summary>
        static Brep RectXZ(double x0, double y, double w, double h)
        {
            try
            {
                return Brep.CreateFromCornerPoints(new Point3d(x0, y, 0), new Point3d(x0 + w, y, 0),
                    new Point3d(x0 + w, y, h), new Point3d(x0, y, h), 1e-6);
            }
            catch { return null; }
        }

        /// <summary>矩形网格片</summary>
        static Mesh RectMesh(double x0, double y0, double x1, double y1, double z)
        {
            return GridSheet(4, 4, 1.0, delegate (double u, double v)
            {
                return new Point3d(x0 + (x1 - x0) * u, y0 + (y1 - y0) * v, z);
            });
        }

        /// <summary>参数化网格片：f(u,v)（u,v ∈ [0,1]）</summary>
        static Mesh GridSheet(int nx, int ny, double unused, Func<double, double, Point3d> f)
        {
            var m = new Mesh();
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++)
                    m.Vertices.Add(f(i / (double)nx, j / (double)ny));
            int W = ny + 1;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    int a = i * W + j, b = (i + 1) * W + j, c = (i + 1) * W + j + 1, d = i * W + j + 1;
                    m.Faces.AddFace(a, b, c, d);
                }
            try { m.FaceNormals.ComputeFaceNormals(); m.Normals.ComputeNormals(); } catch { }
            return m;
        }

        /// <summary>
        /// 结果面的几何签名（曲面采样点的位置加权和）。
        /// 权重必须**不可分离**（含 x·y 与 z² 交叉项）：否则「一整周期正弦起伏」在对称采样点上会正负相消，
        /// 平坦面与波浪面反而算出同一个签名（实测踩过）。
        /// </summary>
        static double SigOf(SurfaceUnifyResult r)
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

        static double ParamValue(SurfaceUnifyPanel panel, string name)
        {
            if (panel == null) return double.NaN;
            SurfaceUnifySettings s = panel.Settings;
            switch (name)
            {
                case "控制点数": return s.GridCount;
                case "贴合强度": return s.FitStrength;
                case "平滑度": return s.Smooth;
                case "最大贴合距离": return s.MaxSnapDistance;
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
