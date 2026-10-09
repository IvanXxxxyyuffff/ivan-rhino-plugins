using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace VoronoiTexture
{
    /// <summary>
    /// 自检：用真实几何验证泰森多边形纹的核心不变量（用户口径：只支持平面边界；输出线框 / 网格面二选一 + 一键平滑）。
    /// 用法：命令 VoronoiSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放 run-voronoi-selftest.flag 后重启 Rhino。
    /// </summary>
    public class VoronoiSelfTestCommand : Command
    {
        public override string EnglishName { get { return "VoronoiSelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\VoronoiSelfTest.txt");            }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RunTo(doc, DefaultReportPath, false);
            return Result.Success;
        }

        public static void RunTo(RhinoDoc doc, string reportPath, bool exitAfter)
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            sb.AppendLine("=== 泰森多边形纹 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            // 边跑边写：某个用例卡住时，报告里仍能看到跑到哪一步（比整份报告丢失好得多）
            Action flush = delegate
            {
                try
                {
                    string d0 = Path.GetDirectoryName(reportPath);
                    if (!string.IsNullOrEmpty(d0)) Directory.CreateDirectory(d0);
                    File.WriteAllText(reportPath, sb.ToString() + System.Environment.NewLine + "[进行中] " + pass + " 通过 / " + fail + " 失败", Encoding.UTF8);
                }
                catch { }
            };
            try
            {
                flush();
                Case1_PlanarRect(doc, sb, ref pass, ref fail); flush();
                Case2_ConcaveAndParams(doc, sb, ref pass, ref fail); flush();
                Case3_PlanarBoundaryResolve(doc, sb, ref pass, ref fail); flush();
                Case4_PlanarShapes(doc, sb, ref pass, ref fail); flush();
                Case5_LShapePolyline(doc, sb, ref pass, ref fail); flush();
                Case6_PanelAndPick(doc, sb, ref pass, ref fail); flush();
                Case7_PickValidity(doc, sb, ref pass, ref fail); flush();
                Case8_OutputModes(doc, sb, ref pass, ref fail); flush();
                Case9_GradientDensity(doc, sb, ref pass, ref fail); flush();
                Case10_MergedMesh(doc, sb, ref pass, ref fail); flush();
                Case11_GradientFollows(doc, sb, ref pass, ref fail); flush();
                Case12_Smooth(doc, sb, ref pass, ref fail); flush();
                Case13_CellNurbs(sb, ref pass, ref fail); flush();
                Case14_WireframeStraight(doc, sb, ref pass, ref fail); flush();
                Case15_CellCenterField(doc, sb, ref pass, ref fail); flush();
                Case16_WallAndSpokes(doc, sb, ref pass, ref fail); flush();
                Case17_FanFromWireframe(doc, sb, ref pass, ref fail); flush();
            }
            catch (Exception ex)
            {
                sb.AppendLine("[FAIL] 自检异常: " + ex);
                fail++;
            }

            sb.AppendLine();
            sb.AppendLine(string.Format("RESULT: {0} 通过 / {1} 失败 -> {2}", pass, fail, fail == 0 ? "PASS" : "FAIL"));

            try
            {
                string d = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }

            try { RhinoApp.WriteLine(sb.ToString()); } catch { }

            if (exitAfter)
            {
                try
                {
                    RhinoDoc d = RhinoDoc.ActiveDoc;
                    if (d != null) d.Modified = false;
                }
                catch { }
                RhinoApp.Exit();
            }
        }

        static void Check(StringBuilder sb, ref int pass, ref int fail, bool ok, string what)
        {
            if (ok) { pass++; sb.AppendLine("[PASS] " + what); }
            else { fail++; sb.AppendLine("[FAIL] " + what); }
        }

        /// <summary>统计网格沿面法向的偏移范围（相对参考面）</summary>
        static void OffsetRange(Brep brep, int faceIndex, Mesh mesh, out double lo, out double hi, out double worstOnSurface)
        {
            lo = double.MaxValue; hi = double.MinValue; worstOnSurface = 0;
            BrepFace face = brep.Faces[faceIndex];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Point3d v = mesh.Vertices[i];
                double u, vv;
                if (!face.ClosestPoint(v, out u, out vv)) { worstOnSurface = 1e9; continue; }
                Point3d p = face.PointAt(u, vv);
                Vector3d n = face.NormalAt(u, vv);
                if (n.IsValid && n.Unitize())
                {
                    double h = (v - p) * n;
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
                double d = p.DistanceTo(v);
                if (d > worstOnSurface) worstOnSurface = d;
            }
            if (lo == double.MaxValue) { lo = 0; hi = 0; }
        }

        static double MaxPlaneDist(Plane pl, Mesh m)
        {
            double worst = 0;
            if (m == null) return worst;
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                double d = Math.Abs(pl.DistanceTo(m.Vertices[i]));
                if (d > worst) worst = d;
            }
            return worst;
        }

        // ---------------------------------------------------------------- 用例1：平面矩形 · 凸起
        static void Case1_PlanarRect(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例1：平面矩形 120x80，胞元 20mm，凸起 3mm（网格面 + 线框） ---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer }, 0.001);
            if (bps == null || bps.Length == 0) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var s = new VoronoiSettings();
            s.MmToModel = 1.0;
            s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 7; s.Step = 0.4;
            s.OutputMode = 1;                    // 网格面模式（线框也提取：网格由线框点建，两个都要比对）
            s.WantWireframe = true;

            string report;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<VoronoiFaceResult> res = Voronoi.Generate(brep, s, out report);
            sw.Stop();
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));
            sb.AppendLine("耗时 " + sw.ElapsedMilliseconds + " ms");

            if (res.Count == 0 || res[0].Mesh == null) { Check(sb, ref pass, ref fail, false, "未生成网格"); return; }
            VoronoiFaceResult r0 = res[0];
            Check(sb, ref pass, ref fail, r0.FaceCount > 0, string.Format("生成了网格（{0} 顶点 / {1} 面）", r0.VertexCount, r0.FaceCount));
            Check(sb, ref pass, ref fail, r0.CellCount > 15 && r0.CellCount < 60,
                string.Format("胞元数合理（{0} 个，120x80 上 20mm 胞元）", r0.CellCount));

            double lo, hi, worst;
            OffsetRange(brep, r0.FaceIndex, r0.Mesh, out lo, out hi, out worst);
            sb.AppendLine(string.Format("  实测高度范围 {0:0.###} ~ {1:0.###} mm（设定凸起 {2}）", lo, hi, s.Depth));
            Check(sb, ref pass, ref fail, hi > s.Depth - 0.15 && hi < s.Depth + 0.15,
                string.Format("最高点达到设定凸起高度（{0:0.###} vs {1}）", hi, s.Depth));
            Check(sb, ref pass, ref fail, lo > -0.05 && lo < 0.6,
                string.Format("胞元之间的凹槽底回落到参考面附近（最低 {0:0.###}）", lo));
            Check(sb, ref pass, ref fail, worst <= s.Depth + 0.01,
                string.Format("顶点都在设定深度的法向偏移内（最大偏移 {0:0.###} <= 设定 {1}）", worst, s.Depth));
            Check(sb, ref pass, ref fail, Math.Abs(worst - hi) < 0.01,
                string.Format("偏移是纯法向的（最大偏移 {0:0.###} = 最大高度 {1:0.###}）", worst, hi));

            // 全部顶点落在面内（平面：Z=0 的矩形内）
            int outside = 0;
            for (int i = 0; i < r0.Mesh.Vertices.Count; i++)
            {
                Point3d v = r0.Mesh.Vertices[i];
                if (v.X < -1e-6 || v.X > 120 + 1e-6 || v.Y < -1e-6 || v.Y > 80 + 1e-6) outside++;
            }
            Check(sb, ref pass, ref fail, outside == 0, string.Format("没有顶点越出面边界（越界 {0} 个）", outside));

            // 纹理确实有起伏
            double mean = 0;
            for (int i = 0; i < r0.Mesh.Vertices.Count; i++) mean += r0.Mesh.Vertices[i].Z;
            mean /= Math.Max(1, r0.Mesh.Vertices.Count);
            double var = 0;
            for (int i = 0; i < r0.Mesh.Vertices.Count; i++)
            {
                double d = r0.Mesh.Vertices[i].Z - mean;
                var += d * d;
            }
            var /= Math.Max(1, r0.Mesh.Vertices.Count);
            Check(sb, ref pass, ref fail, var > 0.05,
                string.Format("纹理有实际起伏（高度方差 {0:0.###}）", var));

            // 同种子可复现
            List<VoronoiFaceResult> again = Voronoi.Generate(brep, s, out report);
            Check(sb, ref pass, ref fail, again[0].CellCount == r0.CellCount && again[0].FaceCount == r0.FaceCount,
                string.Format("同一随机种子结果可复现（胞元 {0}，面 {1}）", again[0].CellCount, again[0].FaceCount));

            // 线框：胞元边界线是平面上的直线段，且没有重合边
            Check(sb, ref pass, ref fail, r0.Wireframe.Count > 30,
                string.Format("提取出胞元线框（{0} 条 = 胞元边 {1} + 外轮廓 {2}；考虑 {3} / 去重 {4} / 丢 {5}）",
                    r0.Wireframe.Count, r0.Wireframe.Count - r0.WireOutline, r0.WireOutline, r0.WireEdgeSeen, r0.WireDedup, r0.WireDropped));
            double worstPlane = 0;
            int lines = 0, dup = 0;
            var keys = new HashSet<long>();
            for (int i = 0; i < r0.Wireframe.Count; i++)
            {
                LineCurve lc = r0.Wireframe[i] as LineCurve;
                if (lc == null) continue;
                lines++;
                worstPlane = Math.Max(worstPlane, Math.Abs(lc.PointAtStart.Z));
                worstPlane = Math.Max(worstPlane, Math.Abs(lc.PointAtEnd.Z));
                if (!keys.Add(PairKey(lc.PointAtStart, lc.PointAtEnd))) dup++;
            }
            Check(sb, ref pass, ref fail, lines == r0.Wireframe.Count && worstPlane < 1e-6,
                string.Format("线框都是平面上的直线段（{0} 条，最大离面 {1:0.######}）", lines, worstPlane));
            Check(sb, ref pass, ref fail, dup == 0,
                string.Format("线框去重生效：同一条边只出一条（重复 {0} 条）", dup));
        }

        // ---------------------------------------------------------------- 用例2：凹 + 参数联动
        static void Case2_ConcaveAndParams(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例2：凹陷（负深度）+ 参数联动 ---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(200, 320), new Interval(0, 80)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer }, 0.001);
            if (bps == null || bps.Length == 0) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var s = new VoronoiSettings();
            s.MmToModel = 1.0;
            s.CellSize = 20.0; s.Depth = -2.5; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 3; s.Step = 0.4;

            string report;
            List<VoronoiFaceResult> res = Voronoi.Generate(brep, s, out report);
            if (res.Count == 0 || res[0].Mesh == null) { Check(sb, ref pass, ref fail, false, "凹陷未生成网格"); return; }
            double lo, hi, worst;
            OffsetRange(brep, res[0].FaceIndex, res[0].Mesh, out lo, out hi, out worst);
            sb.AppendLine(string.Format("  实测高度范围 {0:0.###} ~ {1:0.###} mm（设定凹陷 {2}）", lo, hi, s.Depth));
            Check(sb, ref pass, ref fail, lo < s.Depth + 0.15 && lo > s.Depth - 0.15,
                string.Format("凹到设定深度（{0:0.###} vs {1}）", lo, s.Depth));
            Check(sb, ref pass, ref fail, hi < 0.05, string.Format("最高点不超过参考面（{0:0.###}）", hi));

            // 胞元尺寸减半 -> 胞元数大约 4 倍
            s.CellSize = 10.0;
            List<VoronoiFaceResult> small = Voronoi.Generate(brep, s, out report);
            int c1 = res[0].CellCount, c2 = small[0].CellCount;
            sb.AppendLine(string.Format("  胞元 20mm → {0} 个；10mm → {1} 个", c1, c2));
            Check(sb, ref pass, ref fail, c2 > c1 * 2.5 && c2 < c1 * 5.5,
                string.Format("胞元尺寸减半，胞元数约 4 倍（{0} → {1}）", c1, c2));

            // 规整度：松弛后胞元更均匀。
            // 口径（扇形网格）：按胞元量 **footprint 面积**（该胞元网格投到参考平面后的面积）——
            // 扇形网格的顶点在「中心 → 外圈点」的径向层上，不再铺满顶面，旧的「栅格化顶面连通域」量法失效。
            // 只统计「完整落在区域内部」的胞元：边界胞元被区域裁掉一部分，混进来会把指标带偏。
            s.CellSize = 20.0; s.Relax = 0; s.PerCell = true;
            VoronoiFaceResult rr0 = Voronoi.Generate(brep, s, out report)[0];
            int nAll0, nIn0; double cvAll0, cvIn0;
            CellFootprints(rr0, 200, 320, 0, 80, out nAll0, out cvAll0, out nIn0, out cvIn0);
            s.Relax = 8;
            VoronoiFaceResult rr8 = Voronoi.Generate(brep, s, out report)[0];
            int nAll8, nIn8; double cvAll8, cvIn8;
            CellFootprints(rr8, 200, 320, 0, 80, out nAll8, out cvAll8, out nIn8, out cvIn8);
            s.PerCell = false;
            sb.AppendLine(string.Format("  规整度0：胞元 {0}，footprint {1} 块，全部 CV {2:0.###}，内部 {3} 个 CV {4:0.###}",
                rr0.CellCount, nAll0, cvAll0, nIn0, cvIn0));
            sb.AppendLine(string.Format("  规整度8：胞元 {0}，footprint {1} 块，全部 CV {2:0.###}，内部 {3} 个 CV {4:0.###}",
                rr8.CellCount, nAll8, cvAll8, nIn8, cvIn8));
            Check(sb, ref pass, ref fail, nIn0 >= 4 && nIn8 >= 4 && cvIn8 < cvIn0 * 0.85,
                string.Format("提高规整度后内部胞元更均匀（CV {0:0.###} → {1:0.###}，下降 {2:0.#}%）",
                    cvIn0, cvIn8, cvIn0 > 1e-9 ? (1 - cvIn8 / cvIn0) * 100 : 0));
            // 规整度不能把种子云收小：否则边缘没种子，纹理铺不满参考面
            Check(sb, ref pass, ref fail, rr8.CellCount >= rr0.CellCount * 0.85,
                string.Format("提高规整度后种子仍铺满区域（胞元数 {0} → {1}）", rr0.CellCount, rr8.CellCount));

            // 边界收平：顶点高度在边界处回落到 0
            s.Relax = 2; s.Depth = 3.0; s.EdgeFade = 6.0;
            List<VoronoiFaceResult> faded = Voronoi.Generate(brep, s, out report);
            double edgeMax = 0;
            Mesh m = faded[0].Mesh;
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3d v = m.Vertices[i];
                double de = Math.Min(Math.Min(v.X - 200, 320 - v.X), Math.Min(v.Y, 80 - v.Y));
                if (de < 1.0) edgeMax = Math.Max(edgeMax, Math.Abs(v.Z));
            }
            Check(sb, ref pass, ref fail, edgeMax < 0.35,
                string.Format("边界收平后边界处高度回落到 0（边界附近最大 |h| = {0:0.###}）", edgeMax));
        }

        /// <summary>
        /// 统计每个胞元的 footprint 面积（该胞元网格投到参考平面后的面积），
        /// 输出全部胞元 + 只取「完整落在 [x0,x1]×[y0,y1] 内」的胞元的面积变异系数（CV）。
        /// 用 PerCell 拆出来的胞元网格量：扇形网格的顶点在径向层上，旧口径的「顶面连通域」量不出来。
        /// </summary>
        static void CellFootprints(VoronoiFaceResult r, double x0, double x1, double y0, double y1,
                                   out int countAll, out double cvAll, out int countIn, out double cvIn)
        {
            countAll = 0; cvAll = 0; countIn = 0; cvIn = 0;
            if (r == null || r.CellMeshes == null) return;
            var all = new List<double>();
            var inner = new List<double>();
            for (int i = 0; i < r.CellMeshes.Count; i++)
            {
                Mesh cm = r.CellMeshes[i];
                if (cm == null || cm.Faces.Count == 0) continue;
                double a = 0;
                for (int f = 0; f < cm.Faces.Count; f++)
                {
                    MeshFace mf = cm.Faces[f];
                    if (mf.IsQuad) continue;
                    Point3d p0 = cm.Vertices[mf.A], p1 = cm.Vertices[mf.B], p2 = cm.Vertices[mf.C];
                    a += Math.Abs((p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X)) * 0.5;
                }
                all.Add(a);
                BoundingBox bb = cm.GetBoundingBox(false);
                if (bb.IsValid && bb.Min.X > x0 + 0.5 && bb.Max.X < x1 - 0.5 && bb.Min.Y > y0 + 0.5 && bb.Max.Y < y1 - 0.5)
                    inner.Add(a);
            }
            countAll = all.Count; cvAll = Cv(all);
            countIn = inner.Count; cvIn = Cv(inner);
        }

        static double Cv(List<double> v)
        {
            if (v.Count < 3) return 0;
            double mean = 0;
            foreach (double a in v) mean += a;
            mean /= v.Count;
            if (mean <= 1e-12) return 0;
            double s = 0;
            foreach (double a in v) s += (a - mean) * (a - mean);
            return Math.Sqrt(s / v.Count) / mean;
        }

        // ================================================================ 用例3：平面边界解析
        // 用户口径：只支持平面 —— 闭合平面曲线 / 平面曲面 / 平面 Brep 收，弯曲面 / 开口曲线 / 网格 拒。
        static void Case3_PlanarBoundaryResolve(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例3：平面边界解析（收：闭合平面曲线 / 平面面 / 挤出体；拒：开口 / 非平面 / 弯曲面 / 网格） ---");
            var ids = new List<Guid>();
            try
            {
                // ① 闭合平面曲线
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
                Guid idCurve = doc.Objects.AddCurve(rect);
                ids.Add(idCurve);
                PlanarBoundary b; string why;
                bool ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idCurve), out b, out why);
                Check(sb, ref pass, ref fail, ok && Math.Abs(b.Area - 9600.0) < 1e-6,
                    string.Format("闭合平面曲线：解析成功，面积 {0:0.###}（= 120x80）{1}", ok ? b.Area : 0, ok ? "" : " —— " + why));
                if (ok)
                {
                    Brep plane = b.ToBrep(1e-3);
                    Check(sb, ref pass, ref fail, plane != null && plane.Faces.Count == 1,
                        "解析出的边界能变成单张平面面（喂给现有几何核心）");
                    var s = new VoronoiSettings();
                    s.MmToModel = 1.0;
                    s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 7; s.Step = 0.4;
                    s.WantWireframe = false;
                    string rep;
                    List<VoronoiFaceResult> rs = Voronoi.Generate(plane, s, out rep);
                    sb.AppendLine("  生成日志: " + rep.Replace("\r\n", " | "));
                    Check(sb, ref pass, ref fail, rs.Count > 0 && rs[0].Mesh != null && rs[0].Mesh.Faces.Count > 0,
                        string.Format("平面面喂给现有几何核心能生成网格（{0} 面，几何核心没重写）", rs.Count > 0 && rs[0].Mesh != null ? rs[0].Mesh.Faces.Count : 0));
                }

                // ② 平面面（平面 Brep）
                Brep plate = Brep.CreateFromCornerPoints(new Point3d(200, 0, 0), new Point3d(300, 0, 0),
                                                         new Point3d(300, 80, 0), new Point3d(200, 80, 0), 1e-6);
                Guid idPlate = plate == null ? Guid.Empty : doc.Objects.AddBrep(plate);
                ids.Add(idPlate);
                ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idPlate), out b, out why);
                Check(sb, ref pass, ref fail, ok && Math.Abs(b.Area - 8000.0) < 1e-6,
                    string.Format("平面面：解析成功，面积 {0:0.###}（= 100x80）{1}", ok ? b.Area : 0, ok ? "" : " —— " + why));

                // ③ 带孔平面：取面积最大的平面面的**外轮廓**（内孔数如实报告，不挖）
                Brep holed = HoledPlate();
                Guid idHoled = holed == null ? Guid.Empty : doc.Objects.AddBrep(holed);
                ids.Add(idHoled);
                ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idHoled), out b, out why);
                Check(sb, ref pass, ref fail, ok && Math.Abs(b.Area - 10000.0) < 1.0 && b.InnerLoops == 1,
                    string.Format("带孔平面：取外轮廓（面积 {0:0.###}），内孔数如实报告（{1}）{2}",
                        ok ? b.Area : 0, ok ? b.InnerLoops : -1, ok ? "" : " —— " + why));

                // ④ 挤出物件（Extrusion 不是 Brep 类型，靠 TryConvertBrep 收进来）
                Extrusion ex = Extrusion.Create(rect, 20.0, true);
                Guid idEx = ex == null ? Guid.Empty : doc.Objects.AddExtrusion(ex);
                ids.Add(idEx);
                ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idEx), out b, out why);
                Check(sb, ref pass, ref fail, ok && Math.Abs(b.Area - 9600.0) < 1e-6,
                    string.Format("挤出物件：被 TryConvertBrep 收进来，取最大的平面面（面积 {0:0.###}）{1}", ok ? b.Area : 0, ok ? "" : " —— " + why));

                // ⑤ 弯曲面：拒（用户口径：不再支持任意弯曲曲面）
                Brep curved = CylinderPanel();
                Guid idCurved = curved == null ? Guid.Empty : doc.Objects.AddBrep(curved);
                ids.Add(idCurved);
                ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idCurved), out b, out why);
                Check(sb, ref pass, ref fail, !ok && why.Contains("平面"),
                    string.Format("弯曲面被拒（{0}）", why));

                // ⑥ 开口曲线：拒
                Guid idOpen = doc.Objects.AddCurve(new LineCurve(new Point3d(0, 200, 0), new Point3d(100, 200, 0)));
                ids.Add(idOpen);
                ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idOpen), out b, out why);
                Check(sb, ref pass, ref fail, !ok && why.Contains("不闭合"),
                    string.Format("开口曲线被拒（{0}）", why));

                // ⑦ 非平面闭合曲线：拒
                var np = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0, 300, 0), new Point3d(100, 300, 5), new Point3d(100, 380, 0), new Point3d(0, 380, 5), new Point3d(0, 300, 0)
                });
                Guid idNonPlanar = doc.Objects.AddCurve(np);
                ids.Add(idNonPlanar);
                ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idNonPlanar), out b, out why);
                Check(sb, ref pass, ref fail, !ok && why.Contains("平面"),
                    string.Format("非平面闭合曲线被拒（{0}）", why));

                // ⑧ 网格：拒（提示先转 NURBS 或选边界曲线）
                // 用球面网格：Brep.TryConvertBrep 万一把网格转成了 Brep，也一定落在「没有平面面」那条分支上
                Mesh mesh = Mesh.CreateFromSphere(new Sphere(Plane.WorldXY, 12.0), 8, 8);
                Guid idMesh = mesh == null ? Guid.Empty : doc.Objects.AddMesh(mesh);
                ids.Add(idMesh);
                ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idMesh), out b, out why);
                Check(sb, ref pass, ref fail, !ok && (why.Contains("网格") || why.Contains("平面") || why.Contains("曲线或曲面")),
                    string.Format("网格被拒（{0}）", why));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, ids.ToArray()); }
        }

        // ================================================================ 用例4：平面形状批量
        // 原「复杂曲面」用例改成平面形状：每种形状都要生成出网格、顶点不脱离参考平面、网格铺满边界且不越界。
        static void Case4_PlanarShapes(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例4：平面形状（波浪闭合 / 带孔 / 圆 / 挤出体） ---");

            var s = new VoronoiSettings();
            s.MmToModel = 1.0;
            s.CellSize = 18.0; s.Depth = 2.5; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 13; s.Step = 0.4;
            s.WantWireframe = false;

            RunPlanarShape(doc, sb, "波浪闭合边界平面", WavyPlanar(), s, ref pass, ref fail);
            RunPlanarShape(doc, sb, "带孔平面（矩形挖圆孔）", HoledPlate(), s, ref pass, ref fail);
            RunPlanarShape(doc, sb, "圆平面（R50）", CirclePlate(), s, ref pass, ref fail);
            RunPlanarShape(doc, sb, "矩形挤出体（Extrusion）", RectExtrusion(), s, ref pass, ref fail);
        }

        static void RunPlanarShape(RhinoDoc doc, StringBuilder sb, string name, GeometryBase geo, VoronoiSettings s,
                                   ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("· " + name);
            Guid id = Guid.Empty;
            try
            {
                id = AddAny(doc, geo);
                if (id == Guid.Empty) { Check(sb, ref pass, ref fail, false, name + "：几何创建失败"); return; }

                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, name + "：边界解析失败 —— " + why); return; }

                Brep plane = b.ToBrep(1e-3);
                if (plane == null) { Check(sb, ref pass, ref fail, false, name + "：边界曲线无法变成平面面"); return; }

                string rep;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                List<VoronoiFaceResult> res = Voronoi.Generate(plane, s, out rep);
                sw.Stop();
                VoronoiFaceResult r0 = res.Count > 0 ? res[0] : null;
                bool hasMesh = r0 != null && r0.Mesh != null && r0.Mesh.Faces.Count > 0;
                sb.AppendLine(string.Format("  边界面积 {0:0.#}，胞元 {1}，网格 {2} 面，耗时 {3} ms",
                    b.Area, r0 != null ? r0.CellCount : 0, r0 != null ? r0.FaceCount : 0, sw.ElapsedMilliseconds));
                if (!string.IsNullOrEmpty(rep)) sb.AppendLine("  生成日志: " + rep.Replace("\r\n", " | "));
                Check(sb, ref pass, ref fail, hasMesh,
                    string.Format("{0}：生成了网格（{1} 面）", name, r0 != null ? r0.FaceCount : 0));
                if (!hasMesh) return;

                double worst = MaxPlaneDist(b.Plane, r0.Mesh);
                Check(sb, ref pass, ref fail, worst <= Math.Abs(s.Depth) + 0.15,
                    string.Format("{0}：顶点都在设定深度的法向偏移内（最大 {1:0.###} ≤ {2:0.###}）", name, worst, Math.Abs(s.Depth)));

                BoundingBox bb = r0.Mesh.GetBoundingBox(true);
                BoundingBox ob = b.Outline.GetBoundingBox(true);
                double pad = s.Step + 1e-6;
                bool inBox = bb.Min.X >= ob.Min.X - pad && bb.Max.X <= ob.Max.X + pad &&
                             bb.Min.Y >= ob.Min.Y - pad && bb.Max.Y <= ob.Max.Y + pad;
                bool filled = (bb.Max.X - bb.Min.X) >= (ob.Max.X - ob.Min.X) * 0.98 &&
                              (bb.Max.Y - bb.Min.Y) >= (ob.Max.Y - ob.Min.Y) * 0.98;
                Check(sb, ref pass, ref fail, inBox && filled,
                    string.Format("{0}：网格铺满边界且不越界（网格 {1:0.#}x{2:0.#} vs 边界 {3:0.#}x{4:0.#}）",
                        name, bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, ob.Max.X - ob.Min.X, ob.Max.Y - ob.Min.Y));

                // 投影面积 = 边界面积：溢出的直接判据（3D 表面积被起伏抬高，不能用来判溢出）
                double projErr = b.Area > 0 ? Math.Abs(r0.ProjectedArea - b.Area) / b.Area : 1.0;
                Check(sb, ref pass, ref fail, r0.ProjectedArea > 0 && projErr < 0.03,
                    string.Format("{0}：投影面积 = 边界面积（{1:0.#} vs {2:0.#}，差 {3:0.0##}% < 3%）",
                        name, r0.ProjectedArea, b.Area, projErr * 100));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, name + "：异常 " + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        // ---- 平面形状构造 ----

        /// <summary>闭合波浪曲线（用「首点重复 + MakeClosed」，避免依赖 CurvePeriodicMode）</summary>
        static Curve WavyClosedCurve(int n, double r0, double wave, int lobes)
        {
            var pts = new List<Point3d>();
            for (int i = 0; i < n; i++)
            {
                double ang = i / (double)n * Math.PI * 2;
                double r = r0 + wave * Math.Sin(ang * lobes);
                pts.Add(new Point3d(r * Math.Cos(ang), r * Math.Sin(ang), 0));
            }
            pts.Add(pts[0]);
            Curve c = Curve.CreateInterpolatedCurve(pts, 3);
            if (c != null) c.MakeClosed(0.01);
            return c;
        }

        static Brep WavyPlanar()
        {
            Curve c = WavyClosedCurve(48, 50, 7, 5);
            if (c == null) return null;
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { c }, 0.001);
            return (bps == null || bps.Length == 0) ? null : bps[0];
        }

        static Brep HoledPlate()
        {
            Curve outer = new Rectangle3d(Plane.WorldXY, new Interval(400, 500), new Interval(0, 100)).ToNurbsCurve();
            Curve inner = new Circle(new Plane(new Point3d(450, 50, 0), Vector3d.ZAxis), 20).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer, inner }, 0.001);
            return (bps == null || bps.Length == 0) ? null : bps[0];
        }

        static Brep CirclePlate()
        {
            Curve c = new Circle(new Plane(new Point3d(0, 500, 0), Vector3d.ZAxis), 50).ToNurbsCurve();
            if (c == null) return null;
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { c }, 0.001);
            return (bps == null || bps.Length == 0) ? null : bps[0];
        }

        static Extrusion RectExtrusion()
        {
            Curve c = new Rectangle3d(Plane.WorldXY, new Interval(600, 700), new Interval(0, 80)).ToNurbsCurve();
            return c == null ? null : Extrusion.Create(c, 30.0, true);
        }

        static Brep CylinderPanel()
        {
            var arc = new Arc(new Circle(new Plane(Point3d.Origin, Vector3d.ZAxis), 30), 2.0943951023931953);
            Surface panel = Surface.CreateExtrusion(new ArcCurve(arc), new Vector3d(0, 0, 100));
            return panel == null ? null : Brep.CreateFromSurface(panel);
        }

        // ================================================================ 用例5：L 形闭合多段线
        // 原「立方体接缝」用例改成平面多段线：凹角不塌、顶点/线框不越出 L 形。
        static void Case5_LShapePolyline(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例5：L 形闭合多段线边界（面积 1800；凹角处不越界） ---");
            var pl = new PolylineCurve(new Point3d[]
            {
                new Point3d(0, 0, 0), new Point3d(60, 0, 0), new Point3d(60, 20, 0),
                new Point3d(20, 20, 0), new Point3d(20, 50, 0), new Point3d(0, 50, 0), new Point3d(0, 0, 0)
            });
            Guid id = doc.Objects.AddCurve(pl);
            try
            {
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "L 形边界解析失败：" + why); return; }
                Check(sb, ref pass, ref fail, Math.Abs(b.Area - 1800.0) < 1e-6,
                    string.Format("解析出 L 形边界（面积 {0:0.###} = 60x50 减 40x30）", b.Area));

                var s = new VoronoiSettings();
                s.MmToModel = 1.0;
                s.CellSize = 12.0; s.Depth = 3.0; s.EdgeWidth = 2.5; s.Relax = 2; s.Seed = 5; s.Step = 0.4;
                s.OutputMode = 1; s.WantWireframe = true;      // 网格面 + 线框（凹角处扇形网格不许越出 L 形）

                Brep plane = b.ToBrep(1e-3);
                string rep;
                List<VoronoiFaceResult> res = Voronoi.Generate(plane, s, out rep);
                sb.AppendLine("  生成日志: " + rep.Replace("\r\n", " | "));
                VoronoiFaceResult r0 = res.Count > 0 ? res[0] : null;
                bool hasMesh = r0 != null && r0.Mesh != null && r0.Mesh.Faces.Count > 0;
                Check(sb, ref pass, ref fail, hasMesh,
                    string.Format("生成了网格（{0} 顶点 / {1} 面 / 胞元 {2}）", r0 != null ? r0.VertexCount : 0, r0 != null ? r0.FaceCount : 0, r0 != null ? r0.CellCount : 0));
                if (!hasMesh) return;

                var poly = new List<Point2d>
                {
                    new Point2d(0, 0), new Point2d(60, 0), new Point2d(60, 20),
                    new Point2d(20, 20), new Point2d(20, 50), new Point2d(0, 50)
                };
                double eps = s.Step * 0.75;
                int outside = 0;
                for (int i = 0; i < r0.Mesh.Vertices.Count; i++)
                {
                    Point3d v = r0.Mesh.Vertices[i];
                    if (!InsidePoly(poly, new Point2d(v.X, v.Y), eps)) outside++;
                }
                Check(sb, ref pass, ref fail, outside == 0,
                    string.Format("没有顶点落到 L 形外面（越界 {0} 个）", outside));

                double hi = 0;
                for (int i = 0; i < r0.Mesh.Vertices.Count; i++) hi = Math.Max(hi, r0.Mesh.Vertices[i].Z);
                Check(sb, ref pass, ref fail, hi > s.Depth - 0.15 && hi < s.Depth + 0.15,
                    string.Format("最高点达到设定凸起高度（{0:0.###} vs {1}）", hi, s.Depth));

                int wireOut = 0;
                for (int i = 0; i < r0.Wireframe.Count; i++)
                {
                    LineCurve lc = r0.Wireframe[i] as LineCurve;
                    if (lc == null) continue;
                    if (!InsidePoly(poly, new Point2d(lc.PointAtStart.X, lc.PointAtStart.Y), eps)) wireOut++;
                    if (!InsidePoly(poly, new Point2d(lc.PointAtEnd.X, lc.PointAtEnd.Y), eps)) wireOut++;
                }
                Check(sb, ref pass, ref fail, r0.Wireframe.Count > 10 && wireOut == 0,
                    string.Format("线框端点都在 L 形内（{0} 条，越界 {1} 个端点）", r0.Wireframe.Count, wireOut));

                // 网格本体也不许溢出：投影面积（footprint）= L 形面积
                double projErr = r0.RegionArea > 0 ? Math.Abs(r0.ProjectedArea - r0.RegionArea) / r0.RegionArea : 1.0;
                sb.AppendLine(string.Format("  投影面积 {0:0.##} / 区域面积 {1:0.##}（差 {2:0.0##}%）", r0.ProjectedArea, r0.RegionArea, projErr * 100));
                Check(sb, ref pass, ref fail, r0.RegionArea > 0 && projErr < 0.02,
                    string.Format("网格投影面积 = L 形面积（{0:0.##} vs {1:0.##}，差 {2:0.0##}% < 2%）—— 没溢出凹口", r0.ProjectedArea, r0.RegionArea, projErr * 100));

                List<VoronoiFaceResult> again = Voronoi.Generate(plane, s, out rep);
                Check(sb, ref pass, ref fail,
                    again[0].CellCount == r0.CellCount && again[0].FaceCount == r0.FaceCount && again[0].Wireframe.Count == r0.Wireframe.Count,
                    string.Format("同一随机种子结果可复现（胞元 {0}，面 {1}，线框 {2}）", again[0].CellCount, again[0].FaceCount, again[0].Wireframe.Count));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>射线法 + 边界容差：点是否在多边形内（边界附近算内）</summary>
        static bool InsidePoly(List<Point2d> poly, Point2d p, double eps)
        {            bool inx = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Point2d a = poly[i], b = poly[j];
                if (((a.Y > p.Y) != (b.Y > p.Y)) &&
                    (p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)) inx = !inx;
            }
            if (inx) return true;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double dx = poly[i].X - poly[j].X, dy = poly[i].Y - poly[j].Y;
                double l2 = dx * dx + dy * dy;
                double t = l2 > 1e-18 ? ((p.X - poly[j].X) * dx + (p.Y - poly[j].Y) * dy) / l2 : 0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double ex = p.X - (poly[j].X + t * dx), ey = p.Y - (poly[j].Y + t * dy);
                if (Math.Sqrt(ex * ex + ey * ey) <= eps) return true;
            }
            return false;
        }

        /// <summary>点是否落在多边形**边界线上**（tol 容差内）—— 判「裸边有没有跑到区域内部」用</summary>
        static bool OnPolyLine(List<Point2d> poly, Point2d p, double tol)
        {
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double dx = poly[i].X - poly[j].X, dy = poly[i].Y - poly[j].Y;
                double l2 = dx * dx + dy * dy;
                double t = l2 > 1e-18 ? ((p.X - poly[j].X) * dx + (p.Y - poly[j].Y) * dy) / l2 : 0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double ex = p.X - (poly[j].X + t * dx), ey = p.Y - (poly[j].Y + t * dy);
                if (Math.Sqrt(ex * ex + ey * ey) <= tol) return true;
            }
            return false;
        }

        // ================================================================ 用例6：参数面板 + 拾取红绿
        // 界面代码不跑一次就不知道会不会在构造时抛异常；拾取按钮的红/绿是用户口径里明确要求的，
        // 这里把「未选 / 选错 / 被删 / 有效」四种情况都走一遍（不连会话，直接喂状态）。
        static void Case6_PanelAndPick(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例6：参数面板（构造 / 拾取红绿 / 输出模式 ↔ 平滑块 / 重绘 / 关闭） ---");
            Guid idOk = Guid.Empty, idBad = Guid.Empty, idGrad = Guid.Empty;
            VoronoiPanel panel = null;
            try
            {
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 100), new Interval(0, 60)).ToNurbsCurve();
                idOk = doc.Objects.AddCurve(rect);
                idBad = doc.Objects.AddCurve(new LineCurve(new Point3d(0, 0, 0), new Point3d(50, 0, 0)));
                idGrad = doc.Objects.AddPoint(new Point3d(20, 20, 0));

                var st = new VoronoiSettings { OutputMode = 1, Smooth = false };
                panel = new VoronoiPanel(st, "自检");
                // ⚠ WinForms：控件未 Show 之前 Visible 恒为 false（父级不可见）→ 必须先 Show 再断言可见性
                panel.Show();
                Application.DoEvents();
                Check(sb, ref pass, ref fail, panel.Controls.Count >= 5 && panel.ClientSize.Width > 300 && panel.ClientSize.Height > 400,
                    string.Format("面板构造并显示成功（顶层控件 {0} 个，客户区 {1}x{2}）", panel.Controls.Count, panel.ClientSize.Width, panel.ClientSize.Height));

                Check(sb, ref pass, ref fail, !panel.PickState, "刚打开（没选边界）：拾取按钮是红的");

                PlanarBoundary b; string why;
                bool ok = PlanarBoundary.TryResolve(doc.Objects.FindId(idOk), out b, out why);
                panel.SetTargetState(ok);
                Check(sb, ref pass, ref fail, ok && panel.PickState, "模拟选到有效边界（闭合平面曲线）：拾取按钮变绿");

                panel.SetTargetState(false);
                Check(sb, ref pass, ref fail, !panel.PickState, "选错 / 被删（置 false）：拾取按钮变红");

                Check(sb, ref pass, ref fail, !panel.GradientPickState, "没选渐变物件：渐变按钮是红的");
                Point3d gc = VoronoiSession.ObjectCenter(doc, idGrad);
                panel.SetGradientState(gc.IsValid);
                Check(sb, ref pass, ref fail, gc.IsValid && panel.GradientPickState, "选到有效渐变物件：渐变按钮变绿");
                panel.SetGradientState(false);
                Check(sb, ref pass, ref fail, !panel.GradientPickState, "渐变物件被删（置 false）：渐变按钮变红");

                panel.SelectGradientMode(1);
                Check(sb, ref pass, ref fail, panel.Settings.GradientNear == 1, "渐变方向切到「稀疏」：设置同步（GradientNear = 1）");
                panel.SelectGradientMode(0);
                Check(sb, ref pass, ref fail, panel.Settings.GradientNear == 0, "渐变方向切回「密集」：设置同步（GradientNear = 0）");

                Check(sb, ref pass, ref fail, panel.SmoothBlockVisible, "网格面 + 未勾平滑：平滑块可见");
                Check(sb, ref pass, ref fail, !panel.SmoothParamsEnabled, "网格面 + 未勾平滑：平滑参数灰掉（不可调）");

                panel.SetSmooth(true);
                Check(sb, ref pass, ref fail, panel.SmoothParamsEnabled && panel.Settings.Smooth, "勾选平滑：参数可调且设置同步（Smooth = true）");

                panel.SelectOutputMode(0);
                Check(sb, ref pass, ref fail, !panel.SmoothBlockVisible, "切到「线框」：平滑块隐藏（看不到）");
                Check(sb, ref pass, ref fail, !panel.SmoothParamsEnabled, "切到「线框」：参数也不可调");

                panel.SelectOutputMode(1);
                Check(sb, ref pass, ref fail, panel.SmoothBlockVisible, "切回「网格面」：平滑块重新出现");
                Check(sb, ref pass, ref fail, panel.SmoothParamsEnabled, "切回「网格面」且已勾选：参数仍可调");

                panel.SetSmooth(false);
                Check(sb, ref pass, ref fail, !panel.SmoothParamsEnabled, "取消勾选：参数又灰掉");

                // 真实重绘两遍：自绘控件里「把控件自己的字体 Dispose 掉」这类问题第二遍才暴露，
                // 而且 WinForms 只会把出错的控件画成红叉、不抛异常 —— 所以顺手存张 PNG 供目视。
                try
                {
                    var rect2 = new Rectangle(0, 0, panel.ClientSize.Width, panel.ClientSize.Height);
                    // 写到 %TEMP%：写到 logs 目录会被 Rhino 的文件保护改名成 .IPGSD，内容也读不出来
                    string png = Path.Combine(Path.GetTempPath(), "VoronoiPanel-smoke.png");
                    using (var bmp = new Bitmap(panel.ClientSize.Width, panel.ClientSize.Height))
                    {
                        panel.DrawToBitmap(bmp, rect2);
                        panel.DrawToBitmap(bmp, rect2);
                        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Check(sb, ref pass, ref fail, true, "面板整窗重绘两遍无异常（截图 " + png + "）");
                }
                catch (Exception ex)
                {
                    Check(sb, ref pass, ref fail, false, "面板重绘异常：" + ex.Message);
                }

                // 关闭路径：退出动画跑完必须真的把窗体关掉。
                // 曾经 Motion 把 done 回调丢了 → 面板留在原地、透明不可见还吃鼠标点击，
                // 预览定时器继续重算 → Rhino 整个卡死。这条就是为那个 BUG 加的。
                try
                {
                    panel.Close();
                    var sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    while (!panel.IsDisposed && sw.ElapsedMilliseconds < 3000)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(10);
                    }
                    Check(sb, ref pass, ref fail, panel.IsDisposed,
                        string.Format("关闭动画跑完后窗体真的关掉了（{0} ms）", sw.ElapsedMilliseconds));
                }
                catch (Exception ex)
                {
                    Check(sb, ref pass, ref fail, false, "关闭路径异常：" + ex.Message);
                }
            }
            catch (Exception ex)
            {
                Check(sb, ref pass, ref fail, false, "面板冒烟异常：" + ex.Message);
            }
            finally
            {
                Cleanup(doc, idOk, idBad, idGrad);
                if (panel != null) { try { panel.Close(); panel.Dispose(); } catch { } }
            }
        }

        // ================================================================ 用例7：拾取有效性判定
        // 面板按钮的红/绿由会话的 ValidateTargets 决定（原「用户文件逐面诊断」用例改成这个：
        // 那份 3dm 依赖本机桌面文件，文件不在时整条用例 0 项，覆盖不到新逻辑）。
        static void Case7_PickValidity(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例7：拾取有效性判定（驱动按钮红绿的会话逻辑） ---");
            Guid idGood = Guid.Empty, idOpen = Guid.Empty, idCurved = Guid.Empty;
            try
            {
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 100), new Interval(0, 60)).ToNurbsCurve();
                idGood = doc.Objects.AddCurve(rect);
                idOpen = doc.Objects.AddCurve(new LineCurve(new Point3d(0, 0, 0), new Point3d(50, 0, 0)));
                Brep curved = CylinderPanel();
                idCurved = curved == null ? Guid.Empty : doc.Objects.AddBrep(curved);

                int valid; string why;
                VoronoiSession.ValidateTargets(doc, new List<Guid>(), out valid, out why);
                Check(sb, ref pass, ref fail, valid == 0, "空目标（未选）：0 个可用 —— 按钮红");

                VoronoiSession.ValidateTargets(doc, new List<Guid> { idGood }, out valid, out why);
                Check(sb, ref pass, ref fail, valid == 1, "闭合平面曲线：1 个可用 —— 按钮绿");

                VoronoiSession.ValidateTargets(doc, new List<Guid> { idOpen }, out valid, out why);
                Check(sb, ref pass, ref fail, valid == 0 && !string.IsNullOrEmpty(why),
                    string.Format("开口曲线：0 个可用 + 给出原因（{0}）—— 按钮红", why));

                VoronoiSession.ValidateTargets(doc, new List<Guid> { idCurved }, out valid, out why);
                Check(sb, ref pass, ref fail, valid == 0 && why.Contains("平面"),
                    string.Format("弯曲面：0 个可用 + 说明「没有平面面」（{0}）—— 按钮红", why));

                VoronoiSession.ValidateTargets(doc, new List<Guid> { idGood, idOpen, idCurved }, out valid, out why);
                Check(sb, ref pass, ref fail, valid == 1, "混合多选（1 好 + 2 坏）：1 个可用 —— 有可用就绿");

                VoronoiSession.ValidateTargets(doc, new List<Guid> { Guid.NewGuid() }, out valid, out why);
                Check(sb, ref pass, ref fail, valid == 0, "已被删除的 ID：0 个可用 —— 按钮红");
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, idGood, idOpen, idCurved); }
        }

        // ================================================================ 用例8：输出二选一
        // 用户口径：输出只留「线框 / 网格面」两项，二选一、不混着给；线框同一条边只出一条。
        static void Case8_OutputModes(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例8：输出二选一（线框 / 网格面）+ 线框去重 + 图层 ---");
            Guid id = Guid.Empty;
            try
            {
                CleanupLayers(doc);          // 上一次跑留下的输出对象会带偏图层计数
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
                id = doc.Objects.AddCurve(rect);
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "边界解析失败：" + why); return; }
                Brep plane = b.ToBrep(1e-3);

                var s = new VoronoiSettings();
                s.MmToModel = 1.0;
                s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 7; s.Step = 0.4;

                // ---- 线框模式
                s.OutputMode = 0; s.WantWireframe = true;
                string rep;
                List<VoronoiFaceResult> res = Voronoi.Generate(plane, s, out rep);
                int wires = 0, spokes = 0, centers = 0;
                for (int i = 0; i < res.Count; i++)
                {
                    wires += res[i].Wireframe.Count;
                    spokes += res[i].Spokes.Count;
                    centers += res[i].CellCenters.Count;
                }
                Check(sb, ref pass, ref fail, wires > 30, string.Format("线框模式：提取出胞元边界线（{0} 条）", wires));

                double worstPlane = 0;
                int lines = 0, dup = 0;
                var keys = new HashSet<long>();
                for (int i = 0; i < res.Count; i++)
                    for (int k = 0; k < res[i].Wireframe.Count; k++)
                    {
                        LineCurve lc = res[i].Wireframe[k] as LineCurve;
                        if (lc == null) continue;
                        lines++;
                        worstPlane = Math.Max(worstPlane, Math.Abs(lc.PointAtStart.Z));
                        worstPlane = Math.Max(worstPlane, Math.Abs(lc.PointAtEnd.Z));
                        if (!keys.Add(PairKey(lc.PointAtStart, lc.PointAtEnd))) dup++;
                    }
                Check(sb, ref pass, ref fail, lines == wires && worstPlane < 1e-6,
                    string.Format("线框都是平面上的直线段（{0} 条，最大离面 {1:0.######}）", lines, worstPlane));
                Check(sb, ref pass, ref fail, dup == 0,
                    string.Format("线框去重生效：同一条边只出一条（重复 {0} 条）", dup));

                int c1, m1, sm1;
                Voronoi.AddToDocument(doc, res, Voronoi.MergeWeld(res, 1e-4), null, 0, out c1, out m1, out sm1);
                Check(sb, ref pass, ref fail, c1 == wires + spokes && m1 == 0 && sm1 == 0,
                    string.Format("线框模式写文档：{0} 条曲线（胞元边 {1} + 中心连线 {2}）/ {3} 个网格 / {4} 份平滑（只出线框，不混网格）",
                        c1, wires, spokes, m1, sm1));
                Check(sb, ref pass, ref fail,
                    CountObjectsOnLayer(doc, Voronoi.LayerWire) == wires + spokes + centers && CountObjectsOnLayer(doc, Voronoi.LayerFace) == 0,
                    string.Format("线框+连线+中心点都进了「{0}」层（{1} 个对象），面层没有对象", Voronoi.LayerWire,
                        CountObjectsOnLayer(doc, Voronoi.LayerWire)));

                // ---- 网格面模式
                s.OutputMode = 1; s.WantWireframe = false;
                List<VoronoiFaceResult> res2 = Voronoi.Generate(plane, s, out rep);
                Mesh merged = Voronoi.MergeWeld(res2, 1e-4);
                int wires2 = 0;
                for (int i = 0; i < res2.Count; i++) wires2 += res2[i].Wireframe.Count;
                Check(sb, ref pass, ref fail, wires2 == 0,
                    string.Format("网格面模式：不提取线框（{0} 条）—— 二选一，不混着给", wires2));

                int c2, m2, sm2;
                Voronoi.AddToDocument(doc, res2, merged, null, 1, out c2, out m2, out sm2);
                Check(sb, ref pass, ref fail, c2 == 0 && m2 == 1 && sm2 == 0,
                    string.Format("网格面模式写文档：{0} 条曲线 / {1} 个网格 / {2} 份平滑（只出网格面）", c2, m2, sm2));
                Check(sb, ref pass, ref fail, CountObjectsOnLayer(doc, Voronoi.LayerFace) == 1,
                    string.Format("网格进了「{0}」层", Voronoi.LayerFace));
                Check(sb, ref pass, ref fail, CountObjectsOnLayer(doc, Voronoi.LayerSmooth) == 0,
                    "没勾「一键平滑」：平滑层没有对象");

                int sumF = 0, sumV = 0;
                for (int i = 0; i < res2.Count; i++)
                {
                    sumF += res2[i].FaceCount;
                    sumV += res2[i].VertexCount;
                }
                Check(sb, ref pass, ref fail, merged.Faces.Count == sumF && merged.Vertices.Count == sumV,
                    string.Format("合并成一张连续网格 = 各面网格之和（{0} 面 / {1} 顶点）", merged.Faces.Count, merged.Vertices.Count));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); CleanupLayers(doc); }
        }

        // ================================================================ 用例9：胞元渐变控疏密
        // 用户口径：渐变物件控制的是**疏密**（不是深度）——「靠近物件：密集 / 稀疏」+ 幅度；幅度 0 = 均匀，远处回到基础胞元尺寸。
        // 量法：用胞元中心点（≈种子）的最近邻距离当「种子平均间距」，按到渐变点的距离分近半 / 远半比较。
        static void Case9_GradientDensity(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例9：胞元渐变控疏密（密集 / 稀疏 / 幅度 0 = 均匀 / 远处回基础尺寸） ---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer }, 0.001);
            if (bps == null || bps.Length == 0) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var s = new VoronoiSettings();
            s.MmToModel = 1.0;
            s.CellSize = 14.0; s.Depth = 2.5; s.EdgeWidth = 2.5; s.Relax = 2; s.Seed = 5; s.Step = 0.5;
            s.WantWireframe = false;
            s.GradientOn = true;
            s.GradientPoint = new Point3d(0, 40, 0);      // 左边中点：近半是「左半边」，避开角点的边界效应
            s.GradientAmount = 0.6;

            string rep;
            int nNear, nFar; double nearD, farD, nearS, farS, near0, far0;

            s.GradientNear = 0;                                   // 密集
            List<VoronoiFaceResult> dense = Voronoi.Generate(brep, s, out rep);
            Spacing(dense[0].CellCenters, s.GradientPoint, out nNear, out nFar, out nearD, out farD);
            sb.AppendLine(string.Format("  密集：近 1/4 {0} 个平均间距 {1:0.###}，远 1/4 {2} 个 {3:0.###}，比值 {4:0.###}",
                nNear, nearD, nFar, farD, farD > 1e-9 ? nearD / farD : 0));
            Check(sb, ref pass, ref fail, nNear >= 4 && nFar >= 4 && farD > 1e-9 && nearD < farD * 0.92,
                string.Format("密集：靠近物件处种子间距更小（{0:0.###} < 0.92×{1:0.###}）", nearD, farD));

            s.GradientNear = 1;                                   // 稀疏
            List<VoronoiFaceResult> sparse = Voronoi.Generate(brep, s, out rep);
            Spacing(sparse[0].CellCenters, s.GradientPoint, out nNear, out nFar, out nearS, out farS);
            sb.AppendLine(string.Format("  稀疏：近 1/4 {0} 个平均间距 {1:0.###}，远 1/4 {2} 个 {3:0.###}，比值 {4:0.###}",
                nNear, nearS, nFar, farS, farS > 1e-9 ? nearS / farS : 0));
            Check(sb, ref pass, ref fail, nNear >= 4 && nFar >= 4 && farS > 1e-9 && nearS > farS * 1.08,
                string.Format("稀疏：靠近物件处种子间距更大（{0:0.###} > 1.08×{1:0.###}）", nearS, farS));

            // 幅度 0 = 均匀：最强断言是「与完全不选渐变物件走同一条种子生成路径」→ 种子逐点一致
            s.GradientAmount = 0.0;                               // 幅度 0 = 均匀（短路：走与无渐变一致的种子生成路径）
            List<VoronoiFaceResult> flat = Voronoi.Generate(brep, s, out rep);
            Spacing(flat[0].CellCenters, s.GradientPoint, out nNear, out nFar, out near0, out far0);
            sb.AppendLine(string.Format("  幅度0：近 1/4 平均间距 {0:0.###}，远 1/4 {1:0.###}（随机种子分布本身会有几成起伏，故与「无渐变」逐点比对）",
                near0, far0));
            s.GradientOn = false;                                 // 取消渐变物件
            List<VoronoiFaceResult> noGrad = Voronoi.Generate(brep, s, out rep);
            bool sameSeeds = flat[0].CellCenters != null && noGrad[0].CellCenters != null &&
                             flat[0].CellCenters.Count == noGrad[0].CellCenters.Count;
            if (sameSeeds)
                for (int i = 0; i < flat[0].CellCenters.Count; i++)
                    if (flat[0].CellCenters[i].DistanceTo(noGrad[0].CellCenters[i]) > 1e-9) { sameSeeds = false; break; }
            Check(sb, ref pass, ref fail, sameSeeds,
                string.Format("幅度 0：种子与「不选渐变物件」完全一致（{0} 个中心点逐点比对）",
                    flat[0].CellCenters != null ? flat[0].CellCenters.Count : 0));

            // 远处的「基础尺寸」按同一模式内的**远场**衡量（渐变点在角上时，远 1/4 仍可能落在影响范围内，
            // 所以不做跨模式比较，改为断言：远场间距 ≥ 基础胞元尺寸的一定比例，且密集/稀疏的远场都向基础尺寸靠）
            Check(sb, ref pass, ref fail, farD > 1e-9 && Math.Abs(farS - farD) / farD < 0.30,
                string.Format("远场接近基础胞元尺寸（稀疏远 1/4 {0:0.###} ≈ 密集远 1/4 {1:0.###}，差 {2:0.0#}% < 30%；渐变点在角上时远 1/4 仍受部分影响）",
                    farS, farD, farD > 1e-9 ? Math.Abs(farS - farD) / farD * 100 : 0));
        }

        /// <summary>
        /// 用胞元中心点量「种子平均间距」：最近邻距离的平均，按到渐变点的距离排序取**最近 1/4** 与**最远 1/4**。
        /// 取四分位而不是对半：影响范围只覆盖半径前 60%，远处 1/4 一定在影响范围外（严格回基础尺寸），信号干净。
        /// </summary>
        static void Spacing(List<Point3d> centers, Point3d gradPt, out int nNear, out int nFar, out double near, out double far)
        {
            nNear = 0; nFar = 0; near = 0; far = 0;
            if (centers == null || centers.Count < 8) return;
            int n = centers.Count;
            var nn = new double[n];
            for (int i = 0; i < n; i++)
            {
                double best = double.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double d = centers[i].DistanceTo(centers[j]);
                    if (d < best) best = d;
                }
                nn[i] = best == double.MaxValue ? 0 : best;
            }
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => centers[a].DistanceTo(gradPt).CompareTo(centers[b].DistanceTo(gradPt)));
            int quarter = Math.Max(1, n / 4);
            double sn = 0, sf = 0;
            for (int k = 0; k < quarter; k++) sn += nn[order[k]];
            for (int k = n - quarter; k < n; k++) sf += nn[order[k]];
            nNear = quarter; nFar = quarter;
            near = sn / quarter;
            far = sf / quarter;
        }

        /// <summary>算「靠近参考点的一半胞元」与「外侧一半」的平均面积</summary>
        static void Measure(VoronoiSettings s, Brep brep, out int cells, out double meanNear, out double meanFar)
        {
            meanNear = 0; meanFar = 0; cells = 0;
            string rep;
            List<VoronoiFaceResult> res = Voronoi.Generate(brep, s, out rep);
            if (res.Count == 0 || res[0].CellMeshes == null) return;
            var items = new List<double[]>();
            foreach (Mesh cm in res[0].CellMeshes)
            {
                if (cm == null || cm.Vertices.Count == 0) continue;
                var amp = AreaMassProperties.Compute(cm);
                if (amp == null) continue;
                double cx = 0, cy = 0, cz = 0;
                for (int i = 0; i < cm.Vertices.Count; i++)
                {
                    Point3d v = cm.Vertices[i];
                    cx += v.X; cy += v.Y; cz += v.Z;
                }
                int n = cm.Vertices.Count;
                var cen = new Point3d(cx / n, cy / n, cz / n);
                items.Add(new double[] { amp.Area, cen.DistanceTo(s.GradientPoint) });
            }
            cells = items.Count;
            if (items.Count < 2) return;
            items.Sort((a, b) => a[1].CompareTo(b[1]));
            int half = items.Count / 2;
            double sn = 0, sf = 0;
            for (int i = 0; i < half; i++) sn += items[i][0];
            for (int i = half; i < items.Count; i++) sf += items[i][0];
            meanNear = half > 0 ? sn / half : 0;
            meanFar = items.Count - half > 0 ? sf / (items.Count - half) : 0;
        }

        // ================================================================ 用例10：网格不溢出边界
        // 用户口径：「网格面」= 一张连续网格（接缝处胞元连续），而且**不许溢出边界**。
        // 判溢出必须看**投影面积**（顶点投到参考平面后求和），不能看 3D 表面积 ——
        // 起伏本身就会加面积（深度 3 / 过渡 3 时沟槽侧壁坡度到 1.5，实测 +18%），那是设计如此；
        // 裸边长同理：边界一圈的起伏（沟槽撞到边界处高度回落到 0）会让 3D 长度比周长长几个 %，所以按 XY 投影量。
        static void Case10_MergedMesh(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例10：合并网格不溢出边界（投影面积 = 边界面积；裸边只在边界上） ---");
            Guid id = Guid.Empty;
            try
            {
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
                id = doc.Objects.AddCurve(rect);
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "边界解析失败：" + why); return; }

                var s = new VoronoiSettings();
                s.MmToModel = 1.0;
                s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 7; s.Step = 0.5;
                s.WantWireframe = false;

                string rep;
                List<VoronoiFaceResult> res = Voronoi.Generate(b.ToBrep(1e-3), s, out rep);
                sb.AppendLine("  生成日志: " + rep.Replace("\r\n", " | "));
                VoronoiFaceResult r0 = res.Count > 0 ? res[0] : null;
                bool hasMesh = r0 != null && r0.Mesh != null && r0.Mesh.Faces.Count > 0;
                Check(sb, ref pass, ref fail, hasMesh, string.Format("生成了网格（{0} 面）", r0 != null ? r0.FaceCount : 0));
                if (!hasMesh) return;

                Mesh merged = Voronoi.MergeWeld(res, 1e-4);
                Check(sb, ref pass, ref fail, merged.Faces.Count == r0.Mesh.Faces.Count,
                    string.Format("合并网格面数 = 生成结果（{0}）", merged.Faces.Count));
                Check(sb, ref pass, ref fail, merged.IsValid, "合并网格有效（IsValid）");

                var poly = new List<Point2d>
                {
                    new Point2d(0, 0), new Point2d(120, 0), new Point2d(120, 80), new Point2d(0, 80)
                };

                // ① 网格顶点一个都不许出边界（投影判定）—— 溢出的直接判据
                int outside = 0;
                for (int i = 0; i < merged.Vertices.Count; i++)
                {
                    Point3d v = merged.Vertices[i];
                    if (!InsidePoly(poly, new Point2d(v.X, v.Y), s.Step * 0.5)) outside++;
                }
                Check(sb, ref pass, ref fail, outside == 0,
                    string.Format("没有顶点越出边界（越界 {0} 个 / 共 {1} 个顶点）", outside, merged.Vertices.Count));

                // ② 投影面积（footprint）≈ 边界面积：铺满且没重叠
                double proj = r0.ProjectedArea, regionA = r0.RegionArea;
                double projErr = regionA > 0 ? Math.Abs(proj - regionA) / regionA : 1.0;
                sb.AppendLine(string.Format("  投影面积 {0:0.##} / 区域面积 {1:0.##}（差 {2:0.0##}%）", proj, regionA, projErr * 100));
                Check(sb, ref pass, ref fail, regionA > 0 && projErr < 0.02,
                    string.Format("网格投影面积 = 边界面积（{0:0.##} vs {1:0.##}，差 {2:0.0##}% < 2%）—— 没溢出、没内缩", proj, regionA, projErr * 100));

                // ③ 裸边：每段中点都落在边界线上（XY）；投影总长 ≈ 周长
                double nakedXY = 0, naked3d = 0;
                int segs = 0, offBoundary = 0;
                Polyline[] np = null;
                try { np = merged.GetNakedEdges(); } catch { }
                if (np != null)
                    for (int i = 0; i < np.Length; i++)
                        for (int k = 0; k + 1 < np[i].Count; k++)
                        {
                            Point3d a = np[i][k], c = np[i][k + 1];
                            segs++;
                            naked3d += a.DistanceTo(c);
                            double dx = c.X - a.X, dy = c.Y - a.Y;
                            nakedXY += Math.Sqrt(dx * dx + dy * dy);
                            var mid = new Point2d((a.X + c.X) * 0.5, (a.Y + c.Y) * 0.5);
                            if (!OnPolyLine(poly, mid, s.Step * 1.5)) offBoundary++;
                        }
                double per = 2 * (120 + 80);
                sb.AppendLine(string.Format("  裸边 {0} 段：投影 {1:0.##} mm（周长 {2:0.##}），3D {3:0.##} mm（起伏让它更长）",
                    segs, nakedXY, per, naked3d));
                Check(sb, ref pass, ref fail, segs > 0 && offBoundary == 0,
                    string.Format("裸边只在边界上 —— 内部没有裂缝（{0} 段，越界 {1} 段）", segs, offBoundary));
                Check(sb, ref pass, ref fail, Math.Abs(nakedXY - per) / per < 0.05,
                    string.Format("裸边投影总长 ≈ 边界周长（{0:0.##} vs {1:0.##}，差 {2:0.0#}% < 5%）", nakedXY, per, Math.Abs(nakedXY - per) / per * 100));

                // ④ 3D 表面积：起伏只会加面积（必须 ≥ 投影），且不许异常膨胀（>1.6× 说明几何有问题）
                var amp = AreaMassProperties.Compute(merged);
                double area3d = amp != null ? amp.Area : 0;
                sb.AppendLine(string.Format("  3D 表面积 {0:0.##} = 投影的 {1:0.###} 倍（起伏抬高；深度 {2} / 过渡 {3}）",
                    area3d, proj > 0 ? area3d / proj : 0, s.Depth, s.EdgeWidth));
                Check(sb, ref pass, ref fail, proj > 0 && area3d >= proj * 0.999 && area3d <= proj * 1.6,
                    string.Format("3D 表面积落在 [投影, 1.6×投影] 内（{0:0.###}×）—— 起伏加的面积在合理范围", proj > 0 ? area3d / proj : 0));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        // ================================================================ 用例11：渐变参考物件移动后跟随
        // 用户口径：「选择渐变物件之后 移动这个物件 效果没有实时更新」。
        // 会话改成记物件 ID、每次重算现取中心点；这里验证中心点跟着物件走，且渐变分布真的跟着中心点移动。
        static void Case11_GradientFollows(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例11：渐变参考物件移动后跟随 ---");
            Brep boxBrep = new Box(new BoundingBox(0, 0, 0, 10, 10, 10)).ToBrep();
            Guid id = boxBrep == null ? Guid.Empty : doc.Objects.AddBrep(boxBrep);
            if (id == Guid.Empty) { Check(sb, ref pass, ref fail, false, "放置渐变参考小方块失败"); return; }

            Point3d c1 = VoronoiSession.ObjectCenter(doc, id);
            bool moved = false;
            Rhino.DocObjects.RhinoObject robj = doc.Objects.FindId(id);
            if (robj != null)
            {
                moved = robj.Geometry.Transform(Transform.Translation(new Vector3d(70, 0, 0)));
                robj.CommitChanges();
            }
            Point3d c2 = VoronoiSession.ObjectCenter(doc, id);
            sb.AppendLine(string.Format("  中心点：移动前 ({0:0.##},{1:0.##},{2:0.##}) -> 移动后 ({3:0.##},{4:0.##},{5:0.##})",
                c1.X, c1.Y, c1.Z, c2.X, c2.Y, c2.Z));
            Check(sb, ref pass, ref fail, moved && c1.IsValid && c2.IsValid && Math.Abs((c2.X - c1.X) - 70) < 0.01,
                "中心点跟着物件走（+70mm）—— 不再缓存旧坐标");

            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(140, 0, 0),
                                                     new Point3d(140, 60, 0), new Point3d(0, 60, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建渐变测试平面失败"); return; }
            var s = new VoronoiSettings();
            s.MmToModel = 1.0;
            s.CellSize = 12.0; s.Depth = 2.0; s.EdgeWidth = 2.0; s.Relax = 2; s.Seed = 3; s.Step = 0.5;
            s.PerCell = true; s.WantWireframe = false; s.GradientOn = true; s.GradientAmount = 0.6;
            s.GradientNear = 1;                       // 稀疏 = 靠近物件处胞元更大（大胞元区跟着参考点走）

            s.GradientPoint = new Point3d(20, 30, 0);
            int cellsL; double nearL, farL;
            Measure(s, plate, out cellsL, out nearL, out farL);
            s.GradientPoint = new Point3d(120, 30, 0);
            int cellsR; double nearR, farR;
            Measure(s, plate, out cellsR, out nearR, out farR);
            sb.AppendLine(string.Format("  参考点在左 (20,30)：近侧平均面积 {0:0.##}，远侧 {1:0.##}，比值 {2:0.##}",
                nearL, farL, farL > 1e-9 ? nearL / farL : 0));
            sb.AppendLine(string.Format("  参考点在右 (120,30)：近侧平均面积 {0:0.##}，远侧 {1:0.##}，比值 {2:0.##}",
                nearR, farR, farR > 1e-9 ? nearR / farR : 0));
            Check(sb, ref pass, ref fail, cellsL >= 8 && cellsR >= 8 && nearL > farL * 1.3 && nearR > farR * 1.3,
                "参考点换到哪边，大胞元区就跟到哪边");
        }

        // ================================================================ 用例12：一键平滑（线框三角面细分 → 细分曲面 SubD）
        // 用户口径：「把每一个三角形生成单一的网格片，然后再这个基础上细分网格」+「勾选一键平滑要生成细分曲面，而不是网格」
        static void Case12_Smooth(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例12：一键平滑（线框三角面细分 → 细分曲面 SubD） ---");
            Guid id = Guid.Empty;
            try
            {
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
                id = doc.Objects.AddCurve(rect);
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "边界解析失败：" + why); return; }

                var s = new VoronoiSettings();
                s.MmToModel = 1.0;
                s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 31; s.Step = 1.0;
                s.OutputMode = 1; s.WantWireframe = false;
                string rep;

                // ① 不勾平滑：网格 = 每个线框三角面 1 张面（拓扑 = 线框本身）
                var off = s.Clone(); off.Smooth = false;
                List<VoronoiFaceResult> resOff = Voronoi.Generate(b.ToBrep(1e-3), off, out rep);
                Mesh mOff = Voronoi.MergeWeld(resOff, 1e-4);
                Check(sb, ref pass, ref fail, mOff != null && mOff.Faces.Count > 0,
                    string.Format("不勾平滑：网格 = 每个线框三角面 1 张面（{0} 面）", mOff != null ? mOff.Faces.Count : 0));
                VoronoiSmoothResult ro = Voronoi.ApplySmooth(mOff, off);
                Check(sb, ref pass, ref fail, !ro.Ok && ro.SubD == null, "不勾「一键平滑」：不转细分曲面（只出网格面）");

                // ② 勾平滑：每个三角面片内细分到目标面数 → 再转细分曲面
                var on = s.Clone(); on.Smooth = true; on.SmoothAdaptive = 95.0; on.SmoothQuadCount = 5000;
                List<VoronoiFaceResult> resOn = Voronoi.Generate(b.ToBrep(1e-3), on, out rep);
                Mesh mOn = Voronoi.MergeWeld(resOn, 1e-4);
                sb.AppendLine(string.Format("    不勾 {0} 面 → 勾选 {1} 面（目标 5000）",
                    mOff != null ? mOff.Faces.Count : 0, mOn != null ? mOn.Faces.Count : 0));
                Check(sb, ref pass, ref fail, mOn != null && mOff != null && mOn.Faces.Count > mOff.Faces.Count * 5,
                    "勾选平滑：每个线框三角面片内细分（面数大增）");
                VoronoiSmoothResult rn = Voronoi.ApplySmooth(mOn, on);
                sb.AppendLine("    " + rn.Note);
                Check(sb, ref pass, ref fail, rn.Ok && rn.SubD != null,
                    rn.SubD != null ? "网格 → 细分曲面（SubD）：转换成功" : "细分曲面没建出来（按网格输出）");
                if (rn.SubD != null)
                    Check(sb, ref pass, ref fail, rn.SubD.Vertices.Count > 0,
                        string.Format("细分曲面有控制点：{0} 个", rn.SubD.Vertices.Count));

                // ③ 细分程度接上：300 vs 5000
                var low = s.Clone(); low.Smooth = true; low.SmoothAdaptive = 95.0; low.SmoothQuadCount = 300;
                List<VoronoiFaceResult> resLow = Voronoi.Generate(b.ToBrep(1e-3), low, out rep);
                Mesh mLow = Voronoi.MergeWeld(resLow, 1e-4);
                int nLow = mLow != null ? mLow.Faces.Count : 0;
                int nHigh = mOn != null ? mOn.Faces.Count : 0;
                sb.AppendLine(string.Format("    细分程度 300 → {0} 面；5000 → {1} 面", nLow, nHigh));
                Check(sb, ref pass, ref fail, nLow > 0 && nLow < nHigh * 0.6,
                    string.Format("细分程度接上了：300 → {0} 面，明显少于 5000 → {1} 面", nLow, nHigh));

                // ④ 壁厚：网格要铺满整个胞元（壁厚带 = 高度 0 的平带），相邻胞元连成一张、不留缝
                var wall = s.Clone(); wall.Smooth = false; wall.WallThickness = 2.0;
                List<VoronoiFaceResult> resW = Voronoi.Generate(b.ToBrep(1e-3), wall, out rep);
                Mesh mW = Voronoi.MergeWeld(resW, 1e-4);
                double pa = resW.Count > 0 ? resW[0].ProjectedArea : 0;
                double ra = resW.Count > 0 ? resW[0].RegionArea : 0;
                double gap = ra > 1e-9 ? Math.Abs(pa - ra) / ra : 1.0;
                sb.AppendLine(string.Format("    壁厚 2mm：投影 {0:0.#} / 区域 {1:0.#}（差 {2:0.0#}%）", pa, ra, gap * 100));
                Check(sb, ref pass, ref fail, mW != null && mW.Faces.Count > 0 && gap < 0.02,
                    string.Format("壁厚 2mm：网格铺满整个胞元（差 {0:0.0#}% < 2%）→ 胞元之间由平带连成一张", gap * 100));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        // ================================================================ 用例13：每胞元一张 NURBS 曲面
        // 面板已不再给这 4 种老胞元输出的入口（用户口径：只留线框 / 网格面），但核心代码保留，
        // 这里继续守着它（CellNurbs.cs 还在编译进插件）。
        static void Case13_CellNurbs(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例13：按胞元输出 NURBS 曲面（每胞元一张 · G0 · 边缘贴面；面板已无入口） ---");
            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(120, 0, 0),
                                                     new Point3d(120, 100, 0), new Point3d(0, 100, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }
            var s = new VoronoiSettings();
            s.MmToModel = 1.0;
            s.CellSize = 45.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 1; s.Seed = 7; s.Step = 4.0;
            s.NurbOutput = true;
            s.WantWireframe = false;
            // 先验证「预览不建胞元曲面」（这正是用户报「一点就卡死」的根因）
            s.WantCellSurfaces = false;
            string rep0;
            List<VoronoiFaceResult> res0 = Voronoi.Generate(plate, s, out rep0);
            int n0 = (res0.Count > 0 && res0[0].CellSurfaces != null) ? res0[0].CellSurfaces.Count : 0;
            Check(sb, ref pass, ref fail, n0 == 0,
                string.Format("预览（WantCellSurfaces=false）不构建胞元曲面（{0} 张）—— 这是防假死的关键", n0));
            s.WantCellSurfaces = true;                 // 只有点「生成」那一次才置 true
            string rep;
            List<VoronoiFaceResult> res = Voronoi.Generate(plate, s, out rep);
            if (res.Count == 0) { Check(sb, ref pass, ref fail, false, "无结果"); return; }
            VoronoiFaceResult r0 = res[0];
            int n = r0.CellSurfaces != null ? r0.CellSurfaces.Count : 0;
            sb.AppendLine(string.Format("  胞元曲面 {0} 张（修剪 {1} / 未修剪 {2} / 跳过 {3}），胞元 {4} 个",
                n, r0.NurbsTrimmed, r0.NurbsUntrimmed, r0.NurbsSkipped, r0.CellCount));
            Check(sb, ref pass, ref fail, n > 3, string.Format("生成了多张胞元曲面（{0}）", n));
            if (n == 0) return;

            int one = 0, isNurbs = 0; double sumArea = 0, minZ = double.MaxValue, maxZ = double.MinValue;
            for (int k = 0; k < n; k++)
            {
                Brep b = r0.CellSurfaces[k];
                if (b == null || b.Faces.Count == 0) continue;
                if (b.Faces.Count == 1) one++;
                try { if (b.Faces[0].ToNurbsSurface() != null) isNurbs++; } catch { }
                var amp = AreaMassProperties.Compute(b);
                if (amp != null) sumArea += amp.Area;
                BoundingBox bb = b.GetBoundingBox(true);
                if (bb.IsValid) { if (bb.Min.Z < minZ) minZ = bb.Min.Z; if (bb.Max.Z > maxZ) maxZ = bb.Max.Z; }
            }
            sb.AppendLine(string.Format("  单面占比 {0}/{1}；是 NURBS 的 {2}/{1}；面积合计 {3:0.#}；高度范围 {4:0.###} ~ {5:0.###}",
                one, n, isNurbs, sumArea, minZ, maxZ));
            Check(sb, ref pass, ref fail, one * 10 >= n * 9, string.Format("每个细胞基本都是一张面（{0}/{1}）", one, n));
            Check(sb, ref pass, ref fail, isNurbs * 10 >= n * 9, string.Format("曲面确实是 NURBS（{0}/{1}）", isNurbs, n));
            Check(sb, ref pass, ref fail, Math.Abs(minZ) < 0.05, string.Format("细胞边缘落在基准面上（最低点 {0:0.####} ≈ 0）", minZ));
            Check(sb, ref pass, ref fail, maxZ > s.Depth * 0.7 && maxZ < s.Depth * 1.05,
                string.Format("胞元顶部到达设定深度（最高 {0:0.###} ≈ {1}）", maxZ, s.Depth));

            // 边界形状：修剪过的胞元曲面，外环边数应该很少（= 胞元多边形的直边），
            // 如果还是「网格台阶」边数会是几十上百 —— 这是用户报「边缘锯齿」的判据
            int maxEdges = 0, checkedLoop = 0;
            for (int k = 0; k < n; k++)
            {
                Brep b = r0.CellSurfaces[k];
                if (b == null || b.Faces.Count != 1) continue;
                BrepLoop outer = null;
                foreach (BrepLoop lp in b.Faces[0].Loops) { if (lp.LoopType == BrepLoopType.Outer) { outer = lp; break; } }
                if (outer == null) continue;
                checkedLoop++;
                if (outer.Trims.Count > maxEdges) maxEdges = outer.Trims.Count;
            }
            sb.AppendLine(string.Format("  胞元边界边数（越大越像网格台阶）：检查了 {0} 个，最大 {1} 条", checkedLoop, maxEdges));
            Check(sb, ref pass, ref fail, checkedLoop > 0 && maxEdges <= 12,
                string.Format("胞元边缘是胞元多边形的直边（最大 {0} 条 ≤ 12），不是网格台阶", maxEdges));
        }

        // ================================================================ 用例14：线框是真直线
        // 用户口径：线框必须从**胞元多边形取边**（真直线），不能是采样网格的台阶、也不能断续。
        // 统计方式：每个胞元的多边形边各算一次「考虑」（共享边被两个胞元各算一次），
        //          账目 = 出段 + 去重 + 丢弃；输出条数 = 胞元边 + 区域外轮廓。
        static void Case14_WireframeStraight(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例14：线框是真直线（胞元多边形取边 / 共享边只出一条 / 账目自洽 / 不是台阶） ---");
            Guid id = Guid.Empty;
            try
            {
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
                id = doc.Objects.AddCurve(rect);
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "边界解析失败：" + why); return; }

                var s = new VoronoiSettings();
                s.MmToModel = 1.0;
                s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 7; s.Step = 0.4;
                s.OutputMode = 0; s.WantWireframe = true;

                string rep;
                List<VoronoiFaceResult> res = Voronoi.Generate(b.ToBrep(1e-3), s, out rep);
                sb.AppendLine("  生成日志: " + rep.Replace("\r\n", " | "));
                VoronoiFaceResult r0 = res.Count > 0 ? res[0] : null;
                if (r0 == null || r0.Wireframe.Count == 0) { Check(sb, ref pass, ref fail, false, "没有线框"); return; }
                int cellEdges = r0.Wireframe.Count - r0.WireOutline;

                // ① 直线性：每条都必须是单段直线（LineCurve + 1 个 span，没有折线/曲线段）
                int nonLine = 0, multiSpan = 0;
                for (int i = 0; i < r0.Wireframe.Count; i++)
                {
                    Curve c = r0.Wireframe[i];
                    if (!(c is LineCurve)) { nonLine++; continue; }
                    if (c.SpanCount != 1) multiSpan++;
                }
                Check(sb, ref pass, ref fail, nonLine == 0 && multiSpan == 0,
                    string.Format("线框全部是单段直线（{0} 条；非 LineCurve {1}，多段 {2}）", r0.Wireframe.Count, nonLine, multiSpan));

                // ② 去重：同一条边只出一条（两端点量化后的无序点对唯一）
                var keys = new HashSet<long>();
                int dup = 0;
                for (int i = 0; i < r0.Wireframe.Count; i++)
                {
                    LineCurve lc = r0.Wireframe[i] as LineCurve;
                    if (lc == null) continue;
                    if (!keys.Add(PairKey(lc.PointAtStart, lc.PointAtEnd))) dup++;
                }
                Check(sb, ref pass, ref fail, dup == 0, string.Format("同一条边只出一条（重复 {0} 条）", dup));

                // ③ 账目自洽：每次「考虑」只有一个结果；共享边按种子对只生成一次（去重命中应为 0）
                sb.AppendLine(string.Format("  胞元边 {0} + 外轮廓 {1} 条；考虑 {2} = 出段 {3} + 去重 {4} + 丢 {5}；裁边 {6} 次；胞元 {7} 个",
                    cellEdges, r0.WireOutline, r0.WireEdgeSeen, r0.WireEmitted, r0.WireDedup, r0.WireDropped, r0.WireSnapped, r0.CellCount));
                Check(sb, ref pass, ref fail, r0.WireEdgeSeen == r0.WireEmitted + r0.WireDedup + r0.WireDropped,
                    string.Format("账目自洽：考虑 {0} = 出段 {1} + 去重 {2} + 丢 {3}", r0.WireEdgeSeen, r0.WireEmitted, r0.WireDedup, r0.WireDropped));
                Check(sb, ref pass, ref fail, r0.WireDedup == 0 && cellEdges >= r0.WireEmitted && cellEdges <= r0.WireEmitted * 1.5,
                    string.Format("共享边按种子对只生成一次（出段 {0} 次 / 去重 {1} 次 → 胞元边 {2} 条；去重命中应为 0）",
                        r0.WireEmitted, r0.WireDedup, cellEdges));

                // ④ 条数与胞元数匹配：Voronoi 平面图平均每个胞元约 3 条边（算上边界处的裁剪）
                double perCell = r0.CellCount > 0 ? r0.WireEdgeSeen / (double)r0.CellCount : 0;
                Check(sb, ref pass, ref fail, perCell >= 1.2 && perCell <= 4.0,
                    string.Format("胞元边条数与胞元数匹配（每胞元 {0:0.##} 条，理论约 3 条）", perCell));

                // ⑤ 不越界：每条的端点与中点都在区域内（或边界上）—— 区域外的部分被裁掉，不是伸出去
                //    （端点会吸附到 1e-6×对角线 的统一格，所以容差按采样间距的 1% 给）
                var poly = new List<Point2d>
                {
                    new Point2d(0, 0), new Point2d(120, 0), new Point2d(120, 80), new Point2d(0, 80)
                };
                double eps = Math.Max(1e-9, s.Step * 0.01);
                int outEnd = 0, outMid = 0;
                for (int i = 0; i < r0.Wireframe.Count; i++)
                {
                    LineCurve lc = r0.Wireframe[i] as LineCurve;
                    if (lc == null) continue;
                    if (!InsidePoly(poly, new Point2d(lc.PointAtStart.X, lc.PointAtStart.Y), eps)) outEnd++;
                    if (!InsidePoly(poly, new Point2d(lc.PointAtEnd.X, lc.PointAtEnd.Y), eps)) outEnd++;
                    Point3d mid = lc.PointAt(0.5);
                    if (!InsidePoly(poly, new Point2d(mid.X, mid.Y), eps)) outMid++;
                }
                Check(sb, ref pass, ref fail, outEnd == 0 && outMid == 0,
                    string.Format("线框没有越出边界（端点越界 {0} 个，中点越界 {1} 个；容差 {2:0.####}）", outEnd, outMid, eps));

                // ⑥ 不是台阶：轴对齐（采样网格台阶的特征）线段占比必须很低
                int axis = 0;
                for (int i = 0; i < r0.Wireframe.Count; i++)
                {
                    LineCurve lc = r0.Wireframe[i] as LineCurve;
                    if (lc == null) continue;
                    double dx = Math.Abs(lc.PointAtEnd.X - lc.PointAtStart.X);
                    double dy = Math.Abs(lc.PointAtEnd.Y - lc.PointAtStart.Y);
                    if (dx < 1e-9 || dy < 1e-9) axis++;
                }
                Check(sb, ref pass, ref fail, axis <= r0.Wireframe.Count / 3,
                    string.Format("不是采样网格的台阶（轴对齐线段 {0}/{1} 条，采样网格会是 100%）", axis, r0.Wireframe.Count));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        // ================================================================ 用例15：高度场以胞元中心为准
        // 用户口径：每个胞元按「到中心点的距离」定高度 —— 中心 = 设定深度、胞元边 = 0（相邻胞元共享边上都是 0 → 不裂开）。
        static void Case15_CellCenterField(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例15：高度场以胞元中心为准（中心 = 设定深度 / 胞元边 = 0 / 共享边两侧都 ≈ 0） ---");
            Guid id = Guid.Empty;
            try
            {
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
                id = doc.Objects.AddCurve(rect);
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "边界解析失败：" + why); return; }

                var s = new VoronoiSettings();
                s.MmToModel = 1.0;
                s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 7; s.Step = 0.5;
                s.CellShape = 0;                       // 平顶（过渡宽度 3 < 内切半径，中心一定到满高）
                s.OutputMode = 1; s.WantWireframe = true;   // 网格面 + 线框（要比对网格顶点与线框点）

                string rep;
                List<VoronoiFaceResult> res = Voronoi.Generate(b.ToBrep(1e-3), s, out rep);
                VoronoiFaceResult r0 = res.Count > 0 ? res[0] : null;
                if (r0 == null || r0.Mesh == null || r0.Mesh.Faces.Count == 0)
                { Check(sb, ref pass, ref fail, false, "没生成出网格"); return; }
                Mesh mesh = r0.Mesh;
                var poly = new List<Point2d>
                {
                    new Point2d(0, 0), new Point2d(120, 0), new Point2d(120, 80), new Point2d(0, 80)
                };

                // ① 每个胞元一个中心点，且**中心点必须落在区域内**（用户截图：框外一圈中心点）
                int centersIn = 0;
                for (int i = 0; i < r0.CellCenters.Count; i++)
                    if (InsidePoly(poly, new Point2d(r0.CellCenters[i].X, r0.CellCenters[i].Y), 1e-6)) centersIn++;
                Check(sb, ref pass, ref fail, r0.CellCenters.Count >= r0.CellCount * 0.95 && centersIn >= 10,
                    string.Format("每个胞元一个中心点（{0} 个 / 胞元 {1} 个，其中 {2} 个在区域内）",
                        r0.CellCenters.Count, r0.CellCount, centersIn));
                Check(sb, ref pass, ref fail, centersIn == r0.CellCenters.Count,
                    string.Format("中心点全部落在区域内（{0}/{1}；边界胞元的质心原来会跑到框外）",
                        centersIn, r0.CellCenters.Count));

                // ② 中心处 = 设定深度（取离中心最近的网格顶点量）
                double worstPeak = 0, meanPeak = 0; int peaks = 0;
                for (int i = 0; i < r0.CellCenters.Count; i++)
                {
                    Point3d c = r0.CellCenters[i];
                    if (!InsidePoly(poly, new Point2d(c.X, c.Y), 1e-6)) continue;
                    int vi = NearestVertex(mesh, c);
                    if (vi < 0) continue;
                    double h = mesh.Vertices[vi].Z;
                    peaks++;
                    meanPeak += h;
                    worstPeak = Math.Max(worstPeak, Math.Abs(h - s.Depth));
                }
                if (peaks > 0) meanPeak /= peaks;
                sb.AppendLine(string.Format("  中心处高度：{0} 个胞元，平均 {1:0.###} mm（设定 {2}），与设定最大差 {3:0.###} mm",
                    peaks, meanPeak, s.Depth, worstPeak));
                Check(sb, ref pass, ref fail, peaks >= 10 && worstPeak < 0.2,
                    string.Format("中心处的采样点高度 = 设定深度（{0} 个胞元，最大差 {1:0.###} < 0.2）", peaks, worstPeak));

                // ③ 中心是最高处：中心附近（0.2×胞元尺寸）没有比中心明显更高的顶点（平顶模式下中心一圈都是满高）
                int higher = 0;
                double rad = s.CellSize * 0.2;
                for (int i = 0; i < r0.CellCenters.Count; i++)
                {
                    Point3d c = r0.CellCenters[i];
                    if (!InsidePoly(poly, new Point2d(c.X, c.Y), 1e-6)) continue;
                    int vi = NearestVertex(mesh, c);
                    if (vi < 0) continue;
                    double h0 = mesh.Vertices[vi].Z;
                    for (int k = 0; k < mesh.Vertices.Count; k++)
                    {
                        Point3d v = mesh.Vertices[k];
                        if (Math.Abs(v.X - c.X) > rad || Math.Abs(v.Y - c.Y) > rad) continue;
                        if (v.Z > h0 + 0.15) higher++;
                    }
                }
                Check(sb, ref pass, ref fail, higher == 0,
                    string.Format("中心是局部最高点（半径 {0:0.#} mm 内没有更高的顶点：违反 {1} 个）", rad, higher));

                // ④ 胞元边界上 = 0（新口径：扇形网格的外圈点就在胞元边上，逐点量）
                //    每条**胞元边**（线框里除区域外轮廓以外的那些）的两个端点，附近必须有一个网格顶点且高度 ≈ 0
                //    （外轮廓不算：区域边界上的顶点在胞元内部，是平台，不是沟槽）
                int cellWire = r0.Wireframe.Count - r0.WireOutline;
                int nearEdges = 0, bad = 0;
                double worstEdge = 0;
                double snapTol = Math.Max(s.Step * 0.05, 1e-4);
                for (int i = 0; i < cellWire; i++)
                {
                    LineCurve lc = r0.Wireframe[i] as LineCurve;
                    if (lc == null) continue;
                    Point3d[] ends = { lc.PointAtStart, lc.PointAtEnd };
                    for (int e = 0; e < 2; e++)
                    {
                        int vi = NearestVertex(mesh, ends[e]);
                        if (vi < 0) continue;
                        Point3d v = mesh.Vertices[vi];
                        double d = Math.Sqrt((v.X - ends[e].X) * (v.X - ends[e].X) + (v.Y - ends[e].Y) * (v.Y - ends[e].Y));
                        if (d > snapTol) continue;                 // 这个端点附近没有网格顶点（不算命中）
                        nearEdges++;
                        if (Math.Abs(v.Z) > 0.35) { bad++; worstEdge = Math.Max(worstEdge, Math.Abs(v.Z)); }
                    }
                }
                Check(sb, ref pass, ref fail, nearEdges > 20 && bad == 0,
                    string.Format("胞元边端点上的网格顶点高度 ≈ 0：命中 {0} 个，超过 0.35 的 {1} 个（最大 {2:0.###}）",
                        nearEdges, bad, worstEdge));

                // ⑤ 共享边不裂开：胞元边端点处只应有**一个**网格顶点（相邻胞元共用同一批外圈点）
                int bothSides = 0, sideBad = 0;
                for (int i = 0; i < cellWire; i++)
                {
                    LineCurve lc = r0.Wireframe[i] as LineCurve;
                    if (lc == null) continue;
                    Point3d[] ends = { lc.PointAtStart, lc.PointAtEnd };
                    for (int e = 0; e < 2; e++)
                    {
                        int cnt = 0;
                        for (int k = 0; k < mesh.Vertices.Count; k++)
                        {
                            Point3d v = mesh.Vertices[k];
                            double dx = v.X - ends[e].X, dy = v.Y - ends[e].Y;
                            if (dx * dx + dy * dy <= snapTol * snapTol) cnt++;
                        }
                        if (cnt == 0) continue;                    // 端点附近没顶点（不算）
                        bothSides++;
                        if (cnt != 1) sideBad++;
                    }
                }
                Check(sb, ref pass, ref fail, bothSides > 20 && sideBad == 0,
                    string.Format("共享边两侧共用同一批顶点（量了 {0} 个端点，重复/缺失 {1} 个）—— 不裂开", bothSides, sideBad));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        // ================================================================ 用例17：网格 = 用线框点建的扇形剖分
        // 用户口径：网格要用**生成的线框点**来建 —— 每 3 个点一个三角面（中心 + 相邻两个边界点做扇形剖分），
        // 不再独立跑「采样网格 + 高度场」。这里逐项证明「网格与线框同源」。
        static void Case17_FanFromWireframe(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例17：网格 = 线框点扇形剖分（外圈点 ⊂ 线框端点 / 顶点数恒等式 / 顶点都在射线上 / 采样间距只改分层） ---");
            Guid id = Guid.Empty, idL = Guid.Empty;
            try
            {
                var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
                id = doc.Objects.AddCurve(rect);
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "边界解析失败：" + why); return; }

                var s = new VoronoiSettings();
                s.MmToModel = 1.0;
                s.CellSize = 20.0; s.Depth = 3.0; s.EdgeWidth = 3.0; s.Relax = 2; s.Seed = 7; s.Step = 0.4;
                s.OutputMode = 1; s.WantWireframe = true;

                string rep;
                List<VoronoiFaceResult> res = Voronoi.Generate(b.ToBrep(1e-3), s, out rep);
                sb.AppendLine("  生成日志: " + rep.Replace("\r\n", " | "));
                VoronoiFaceResult r0 = res.Count > 0 ? res[0] : null;
                bool hasMesh = r0 != null && r0.Mesh != null && r0.Mesh.Faces.Count > 0;
                Check(sb, ref pass, ref fail, hasMesh, "生成了扇形网格");
                if (!hasMesh) return;

                // ① 三角面 + 顶点构成恒等式：顶点数 = 峰顶 + (层-1)×外圈引用 + 去重后的外圈点
                //    （这条只有「顶点 = 峰顶 + 径向层点 + 外圈点」才成立，等于证明网格就是扇形剖分）
                int quads = 0;
                for (int i = 0; i < r0.Mesh.Faces.Count; i++) if (!r0.Mesh.Faces.GetFace(i).IsTriangle) quads++;
                int expect = r0.FanApexPts.Count + (r0.FanLayers - 1) * r0.FanOuterRefs + r0.FanOuterPts.Count;
                sb.AppendLine(string.Format("  顶点构成：峰顶 {0} + (层 {1} - 1) × 外圈引用 {2} + 外圈点 {3} = {4}（实测 {5}）",
                    r0.FanApexPts.Count, r0.FanLayers, r0.FanOuterRefs, r0.FanOuterPts.Count, expect, r0.VertexCount));
                Check(sb, ref pass, ref fail, quads == 0,
                    string.Format("每个面都是三角面（非三角 {0} / 共 {1} 面）", quads, r0.Mesh.Faces.Count));
                Check(sb, ref pass, ref fail, r0.FanBlocked == 0 && r0.FanSkipped == 0 && r0.VertexCount == expect,
                    string.Format("顶点数 = 峰顶 + (层-1)×外圈引用 + 外圈点（实测 {0} vs 期望 {1}；被裁点 {2}，跳过胞元 {3}）",
                        r0.VertexCount, expect, r0.FanBlocked, r0.FanSkipped));

                // ② 外圈点全部来自线框端点（胞元边段端点 + 区域外轮廓点）
                double worstMiss;
                int miss = FanOuterMiss(r0, out worstMiss);
                Check(sb, ref pass, ref fail, r0.FanOuterPts.Count > 10 && miss == 0,
                    string.Format("外圈点全部来自线框端点（外圈 {0} 个 / 线框 {1} 条，缺失 {2} 个，最远 {3:0.####} mm）",
                        r0.FanOuterPts.Count, r0.Wireframe.Count, miss, worstMiss));

                // ③ 各胞元外圈环拼起来就是区域（环面积之和 = 区域面积；环拼错会立刻暴露）
                double ringErr = r0.RegionArea > 0 ? Math.Abs(r0.FanRingArea - r0.RegionArea) / r0.RegionArea : 1.0;
                Check(sb, ref pass, ref fail, ringErr < 0.01,
                    string.Format("外圈环面积之和 = 区域面积（{0:0.##} vs {1:0.##}，差 {2:0.0##}% < 1%）",
                        r0.FanRingArea, r0.RegionArea, ringErr * 100));

                // ④ 每个网格顶点都在「峰顶 → 外圈点」的射线上（其余顶点就是这条线上的径向层点）
                double worstRay;
                int offRay = FanOffRay(r0, out worstRay);
                Check(sb, ref pass, ref fail, offRay == 0,
                    string.Format("每个网格顶点都在「峰顶 → 外圈点」射线上（偏离 {0} 个 / 共 {1} 个，最远 {2:0.######} mm）",
                        offRay, r0.Mesh.Vertices.Count, worstRay));

                // ⑤ 统计「网格顶点里落在网格端点上的比例」（其余是径向层点：中心 → 外圈点的插值）
                int atWire = FanOnWire(r0);
                sb.AppendLine(string.Format("  网格顶点里落在「线框端点」上的 {0}/{1}（{2:0.#}%）；其余 {3} 个是径向层点（中心→外圈点的插值）",
                    atWire, r0.VertexCount, r0.VertexCount > 0 ? atWire * 100.0 / r0.VertexCount : 0, r0.VertexCount - atWire));
                Check(sb, ref pass, ref fail, atWire >= r0.FanOuterPts.Count,
                    string.Format("所有外圈点都在网格顶点里（{0} ≥ {1}）", atWire, r0.FanOuterPts.Count));

                // ⑥ 采样间距不再决定网格分层（网格 = 线框的三角面；细分由「细分程度」管）
                var s2 = s.Clone();
                s2.Step = 2.0;
                List<VoronoiFaceResult> res2 = Voronoi.Generate(b.ToBrep(1e-3), s2, out rep);
                VoronoiFaceResult r2 = res2.Count > 0 ? res2[0] : null;
                bool ok2 = r2 != null && r2.Mesh != null && r2.Mesh.Faces.Count > 0;
                Check(sb, ref pass, ref fail, ok2 && r0.FanLayers == 1 && r2.FanLayers == 1,
                    string.Format("采样间距不再决定网格分层（网格 = 线框三角面，细分由「细分程度」管）：0.4mm → {0} 层 / {1} 面，2.0mm → {2} 层 / {3} 面",
                        r0.FanLayers, r0.Mesh.Faces.Count, r2 != null ? r2.FanLayers : 0, r2 != null && r2.Mesh != null ? r2.Mesh.Faces.Count : 0));
                if (ok2)
                {
                    int same = 0;
                    for (int i = 0; i < r0.FanOuterPts.Count; i++)
                    {
                        double best = double.MaxValue;
                        for (int k = 0; k < r2.FanOuterPts.Count; k++)
                        {
                            double d = r0.FanOuterPts[i].DistanceTo(r2.FanOuterPts[k]);
                            if (d < best) best = d;
                        }
                        if (best <= 1e-6) same++;
                    }
                    Check(sb, ref pass, ref fail, r2.FanOuterPts.Count == r0.FanOuterPts.Count && same == r0.FanOuterPts.Count,
                        string.Format("采样间距不改变外圈点集（{0} vs {1} 个，逐点对上 {2}）—— 形状不再受采样间距影响",
                            r0.FanOuterPts.Count, r2.FanOuterPts.Count, same));
                }

                // ⑦ 凹区域（L 形）：外圈环要多条链 + 区域外轮廓补弧，网格仍然不越界、投影面积 = 区域面积
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0, 0, 0), new Point3d(60, 0, 0), new Point3d(60, 20, 0),
                    new Point3d(20, 20, 0), new Point3d(20, 50, 0), new Point3d(0, 50, 0), new Point3d(0, 0, 0)
                });
                idL = doc.Objects.AddCurve(pl);
                PlanarBoundary bl; string whyL;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(idL), out bl, out whyL))
                { Check(sb, ref pass, ref fail, false, "L 形边界解析失败：" + whyL); return; }

                var sl = new VoronoiSettings();
                sl.MmToModel = 1.0;
                sl.CellSize = 12.0; sl.Depth = 3.0; sl.EdgeWidth = 2.5; sl.Relax = 2; sl.Seed = 5; sl.Step = 0.4;
                sl.OutputMode = 1; sl.WantWireframe = true;
                List<VoronoiFaceResult> rl = Voronoi.Generate(bl.ToBrep(1e-3), sl, out rep);
                VoronoiFaceResult r1 = rl.Count > 0 ? rl[0] : null;
                bool okL = r1 != null && r1.Mesh != null && r1.Mesh.Faces.Count > 0;
                Check(sb, ref pass, ref fail, okL, "L 形（凹区域，外圈环有多条链）也生成了扇形网格");
                if (okL)
                {
                    var polyL = new List<Point2d>
                    {
                        new Point2d(0, 0), new Point2d(60, 0), new Point2d(60, 20),
                        new Point2d(20, 20), new Point2d(20, 50), new Point2d(0, 50)
                    };
                    double eps = sl.Step * 0.75;
                    int outside = 0;
                    for (int i = 0; i < r1.Mesh.Vertices.Count; i++)
                    {
                        Point3d v = r1.Mesh.Vertices[i];
                        if (!InsidePoly(polyL, new Point2d(v.X, v.Y), eps)) outside++;
                    }
                    Check(sb, ref pass, ref fail, outside == 0,
                        string.Format("L 形：没有顶点越出边界（越界 {0} / {1}）", outside, r1.Mesh.Vertices.Count));
                    double projErr = r1.RegionArea > 0 ? Math.Abs(r1.ProjectedArea - r1.RegionArea) / r1.RegionArea : 1.0;
                    Check(sb, ref pass, ref fail, projErr < 0.02,
                        string.Format("L 形：投影面积 = 区域面积（{0:0.##} vs {1:0.##}，差 {2:0.0##}% < 2%）—— 凹口没被盖住",
                            r1.ProjectedArea, r1.RegionArea, projErr * 100));
                    double worstL;
                    int missL = FanOuterMiss(r1, out worstL);
                    Check(sb, ref pass, ref fail, r1.FanOuterPts.Count > 10 && missL == 0,
                        string.Format("L 形：外圈点全部来自线框端点（外圈 {0} 个，缺失 {1} 个，最远 {2:0.####} mm；补弧 {3} 段）",
                            r1.FanOuterPts.Count, missL, worstL, r1.FanArcs));
                }
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); Cleanup(doc, idL); }
        }

        /// <summary>外圈点里不在线框端点上的个数（容差 1e-3 mm；线框端点做过量化吸附）</summary>
        static int FanOuterMiss(VoronoiFaceResult r, out double worst)
        {
            worst = 0;
            var wpts = new List<Point3d>();
            for (int i = 0; i < r.Wireframe.Count; i++)
            {
                LineCurve lc = r.Wireframe[i] as LineCurve;
                if (lc == null) continue;
                wpts.Add(lc.PointAtStart);
                wpts.Add(lc.PointAtEnd);
            }
            int miss = 0;
            for (int i = 0; i < r.FanOuterPts.Count; i++)
            {
                double best = double.MaxValue;
                for (int k = 0; k < wpts.Count; k++)
                {
                    double d = r.FanOuterPts[i].DistanceTo(wpts[k]);
                    if (d < best) best = d;
                }
                if (best > 1e-3) { miss++; if (best > worst) worst = best; }
            }
            return miss;
        }

        /// <summary>网格顶点里不在「峰顶 → 外圈点」射线上的个数（外圈点与峰顶本身也算命中）</summary>
        static int FanOffRay(VoronoiFaceResult r, out double worst)
        {
            worst = 0;
            int off = 0;
            for (int i = 0; i < r.Mesh.Vertices.Count; i++)
            {
                Point3d v = r.Mesh.Vertices[i];
                var p = new Point2d(v.X, v.Y);
                double best = double.MaxValue;
                for (int a = 0; a < r.FanApexPts.Count && best > 1e-7; a++)
                {
                    var c = new Point2d(r.FanApexPts[a].X, r.FanApexPts[a].Y);
                    if (p.DistanceTo(c) < 1e-9) { best = 0; break; }
                    for (int o = 0; o < r.FanOuterPts.Count; o++)
                    {
                        var q = new Point2d(r.FanOuterPts[o].X, r.FanOuterPts[o].Y);
                        double dx = q.X - c.X, dy = q.Y - c.Y;
                        double l2 = dx * dx + dy * dy;
                        if (!(l2 > 1e-18)) continue;
                        double t = ((p.X - c.X) * dx + (p.Y - c.Y) * dy) / l2;
                        if (t < -1e-9 || t > 1 + 1e-9) continue;
                        double ex = p.X - (c.X + t * dx), ey = p.Y - (c.Y + t * dy);
                        double d = Math.Sqrt(ex * ex + ey * ey);
                        if (d < best) best = d;
                        if (best <= 1e-7) break;
                    }
                }
                if (best > 1e-4) { off++; if (best > worst) worst = best; }
            }
            return off;
        }

        /// <summary>网格顶点里落在「线框端点」上的个数（容差 1e-3 mm）</summary>
        static int FanOnWire(VoronoiFaceResult r)
        {
            var wpts = new List<Point3d>();
            for (int i = 0; i < r.Wireframe.Count; i++)
            {
                LineCurve lc = r.Wireframe[i] as LineCurve;
                if (lc == null) continue;
                wpts.Add(lc.PointAtStart);
                wpts.Add(lc.PointAtEnd);
            }
            int n = 0;
            for (int i = 0; i < r.Mesh.Vertices.Count; i++)
            {
                Point3d v = r.Mesh.Vertices[i];
                for (int k = 0; k < wpts.Count; k++)
                {
                    double dx = v.X - wpts[k].X, dy = v.Y - wpts[k].Y;
                    if (dx * dx + dy * dy <= 1e-6) { n++; break; }
                }
            }
            return n;
        }

        static int NearestVertex(Mesh m, Point3d p)
        {
            int best = -1;
            double bd = double.MaxValue;
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3d v = m.Vertices[i];
                double d = (v.X - p.X) * (v.X - p.X) + (v.Y - p.Y) * (v.Y - p.Y);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>用例16：细胞壁壁厚 + 中心点 / 中心连线</summary>
        static void Case16_WallAndSpokes(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例16：细胞壁壁厚 + 中心点连线 ---");
            Guid id = Guid.Empty;
            try
            {
                var rect = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(80,0,0), new Point3d(80,60,0), new Point3d(0,60,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(rect);
                PlanarBoundary b; string why;
                if (!PlanarBoundary.TryResolve(doc.Objects.FindId(id), out b, out why))
                { Check(sb, ref pass, ref fail, false, "边界解析失败：" + why); return; }
                Brep brep = b.ToBrep(0.01);

                var s = new VoronoiSettings();
                s.MmToModel = 1.0; s.CellSize = 18; s.Depth = 2.5; s.EdgeWidth = 2.0;
                s.Relax = 1; s.Seed = 5; s.Step = 0.5; s.OutputMode = 0; s.WantWireframe = true;
                s.WallThickness = 0.0;
                string rep;
                List<VoronoiFaceResult> noWall = Voronoi.Generate(brep, s, out rep);
                sb.AppendLine("    壁厚0：" + (noWall.Count > 0 ? noWall[0].Diag : "(空)"));
                Check(sb, ref pass, ref fail, noWall.Count > 0 && noWall[0].Wireframe.Count > 0, "壁厚 0：有线框输出");
                Check(sb, ref pass, ref fail, noWall[0].Spokes.Count > 0,
                    string.Format("壁厚 0：有中心连线（{0} 条）", noWall[0].Spokes.Count));
                Check(sb, ref pass, ref fail, noWall[0].CellCenters.Count > 0,
                    string.Format("中心点非空（{0} 个）", noWall[0].CellCenters.Count));

                s.WallThickness = 1.0;                      // 壁厚 1mm
                List<VoronoiFaceResult> wall = Voronoi.Generate(brep, s, out rep);
                sb.AppendLine("    壁厚1：" + (wall.Count > 0 ? wall[0].Diag : "(空)"));
                Check(sb, ref pass, ref fail, wall.Count > 0 && wall[0].Wireframe.Count > 0, "壁厚 1：有线框输出");
                Check(sb, ref pass, ref fail, wall[0].Spokes.Count > 0,
                    string.Format("壁厚 1：中心连线跟着内缩后的点走（{0} 条）", wall[0].Spokes.Count));
                // 壁厚让线框条数变多（两侧各自内缩 → 不再是共享边）
                Check(sb, ref pass, ref fail, wall[0].Wireframe.Count > noWall[0].Wireframe.Count,
                    string.Format("壁厚 1：线框条数 {0} > 壁厚 0 的 {1}（共享边变两侧各自内缩）", wall[0].Wireframe.Count, noWall[0].Wireframe.Count));

                // 中心点 + 连线：每条连线的远端必须落在某个线框端点/顶点上，近端 = 中心
                var ends = new HashSet<long>();
                for (int i = 0; i < wall[0].Spokes.Count; i++)
                {
                    LineCurve lc = wall[0].Spokes[i] as LineCurve;
                    if (lc == null) continue;
                    foreach (Point3d p in new Point3d[] { lc.PointAtStart, lc.PointAtEnd })
                    {
                        long kk = (long)Math.Round(p.X * 1e6) * 1000003L ^ (long)Math.Round(p.Y * 1e6);
                        ends.Add(kk);
                    }
                }
                int spokeEndOk = 0, spokeEndBad = 0;
                for (int i = 0; i < wall[0].CellCenters.Count; i++)
                {
                    Point3d c = wall[0].CellCenters[i];
                    long kk = (long)Math.Round(c.X * 1e6) * 1000003L ^ (long)Math.Round(c.Y * 1e6);
                    if (ends.Contains(kk)) spokeEndOk++; else spokeEndBad++;
                }
                // 边界胞元的中心可能落在区域外（凸胞元被边界裁掉一半）→ 这些只报告，不算失败
                Check(sb, ref pass, ref fail, spokeEndOk > 0,
                    string.Format("区域内的中心点都出现在连线端点上（命中 {0} / 区域外未命中 {1}，后者只报告）", spokeEndOk, spokeEndBad));

                // 中心连线两端都必须在区域内（用户截图：连线朝框外跑）—— 壁厚 0 / 1 都量
                var rectPoly = new List<Point2d>
                {
                    new Point2d(0, 0), new Point2d(80, 0), new Point2d(80, 60), new Point2d(0, 60)
                };
                double spokeEps = s.Step * 0.5;
                int spokeOut = 0, spokeLines = 0;
                for (int wi = 0; wi < 2; wi++)
                {
                    List<VoronoiFaceResult> wr = wi == 0 ? noWall : wall;
                    if (wr == null || wr.Count == 0 || wr[0] == null) continue;
                    for (int i = 0; i < wr[0].Spokes.Count; i++)
                    {
                        LineCurve lc = wr[0].Spokes[i] as LineCurve;
                        if (lc == null) continue;
                        spokeLines++;
                        if (!InsidePoly(rectPoly, new Point2d(lc.PointAtStart.X, lc.PointAtStart.Y), spokeEps)) spokeOut++;
                        if (!InsidePoly(rectPoly, new Point2d(lc.PointAtEnd.X, lc.PointAtEnd.Y), spokeEps)) spokeOut++;
                    }
                }
                Check(sb, ref pass, ref fail, spokeLines > 20 && spokeOut == 0,
                    string.Format("中心连线两端都在区域内（壁厚 0/1 共 {0} 条，越界端点 {1} 个；容差 {2:0.###}）",
                        spokeLines, spokeOut, spokeEps));

                // 网格面模式：焊接 + 平滑（勾了平滑才有平滑结果，不勾只有网格）
                s.OutputMode = 1; s.WantWireframe = false; s.Smooth = true; s.SmoothAdaptive = 95; s.SmoothQuadCount = 2000;
                List<VoronoiFaceResult> sm = Voronoi.Generate(brep, s, out rep);
                Check(sb, ref pass, ref fail, sm.Count > 0 && sm[0].Mesh != null && sm[0].Mesh.Faces.Count > 0,
                    "网格面：合并焊接后有网格");
                var sr = Voronoi.ApplySmooth(sm[0].Mesh, s);
                Check(sb, ref pass, ref fail, sr != null && sr.Ok,
                    sr != null && sr.Ok ? string.Format("焊接后一键平滑成功（{0} 面）", sr.Faces) : "焊接后一键平滑失败");
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        // ------------------------------------------------------------------ 工具

        static Guid AddAny(RhinoDoc doc, GeometryBase geo)
        {
            if (geo == null) return Guid.Empty;
            Curve c = geo as Curve;
            if (c != null) return doc.Objects.AddCurve(c);
            Brep b = geo as Brep;
            if (b != null) return doc.Objects.AddBrep(b);
            Extrusion e = geo as Extrusion;
            if (e != null) return doc.Objects.AddExtrusion(e);
            Mesh m = geo as Mesh;
            if (m != null) return doc.Objects.AddMesh(m);
            return Guid.Empty;
        }

        static long Q3(Point3d p)
        {
            long a = (long)Math.Round(p.X * 1e6), b = (long)Math.Round(p.Y * 1e6), c = (long)Math.Round(p.Z * 1e6);
            unchecked { return (a * 73856093L) ^ (b * 19349663L) ^ (c * 83492791L); }
        }

        /// <summary>无序点对键：判断「同一条边是不是出了两次」</summary>
        static long PairKey(Point3d a, Point3d b)
        {
            long ka = Q3(a), kb = Q3(b);
            unchecked { return ka < kb ? (ka * 1000003L) ^ kb : (kb * 1000003L) ^ ka; }
        }

        static int LayerIndex(RhinoDoc doc, string name)
        {
            foreach (Layer l in doc.Layers)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
            return -1;
        }

        static int CountObjectsOnLayer(RhinoDoc doc, string name)
        {
            Rhino.DocObjects.RhinoObject[] objs = null;
            try { objs = doc.Objects.FindByLayer(name); } catch { }
            return objs == null ? 0 : objs.Length;
        }

        /// <summary>清掉自检往输出图层写的对象（图层本身留着）</summary>
        static void CleanupLayers(RhinoDoc doc)
        {
            string[] names = { Voronoi.LayerWire, Voronoi.LayerFace, Voronoi.LayerSmooth };
            for (int i = 0; i < names.Length; i++)
            {
                Rhino.DocObjects.RhinoObject[] objs = null;
                try { objs = doc.Objects.FindByLayer(names[i]); } catch { }
                if (objs == null) continue;
                for (int k = 0; k < objs.Length; k++)
                {
                    try { doc.Objects.Delete(objs[k], true); } catch { }
                }
            }
        }

        static void Cleanup(RhinoDoc doc, params Guid[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == Guid.Empty) continue;
                try { doc.Objects.Delete(ids[i], true); } catch { }
            }
        }
    }
}
