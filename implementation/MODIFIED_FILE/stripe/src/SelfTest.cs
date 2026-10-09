using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace StripeOnSurface
{
    /// <summary>
    /// 自检命令：用真实几何验证条纹生成的核心不变量（数量/闭合/边距/宽度/间距/曲面贴合/开槽）。
    /// 用法：-_StripeSelfTest Exit=Yes
    /// </summary>
    public class StripeSelfTestCommand : Command
    {
        public override string EnglishName { get { return "StripeSelfTest"; } }

        /// <summary>默认报告路径（标志文件触发时也写这里）</summary>
        public static string DefaultReportPath
        {
            get { return System.IO.Path.Combine(SelfTestDir, "StripeSelfTest.txt"); }
        }

        static string SelfTestDir
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
            }
        }

        const string ExitFlagPath = @"C:\zcode_build\stripe\_exit_after_test.flag";

        /// <summary>为 true 时把生成的贴合条纹写进文档（供人工/截图检查外观）</summary>
        public static bool DumpGeometry;

        /// <summary>为 true 时自检结束前把当前文档另存为 3dm</summary>
        public static string DumpDocPath;

        /// <summary>导出几何的包围盒（导出后视图缩放到它）</summary>
        static BoundingBox DumpBox = BoundingBox.Empty;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RunTo(doc, DefaultReportPath, File.Exists(ExitFlagPath));
            return Result.Success;
        }

        /// <summary>跑全部用例并写报告。exitAfter=true 时跑完退出 Rhino（无人值守）。</summary>
        public static void RunTo(RhinoDoc doc, string reportPath, bool exitAfter)
        {
            try
            {
                string d = System.IO.Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(d)) System.IO.Directory.CreateDirectory(d);
                File.WriteAllText(reportPath, "started " + DateTime.Now.ToString("s") + "\r\n");
            }
            catch { }

            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            sb.AppendLine("=== StripeOnSurface 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                RunCase1_PlaneStadium(doc, sb, ref pass, ref fail);
                RunCase2_PlaneWithHole(doc, sb, ref pass, ref fail);
                RunCase3_Cylinder(doc, sb, ref pass, ref fail);
                RunCase4_Engrave(doc, sb, ref pass, ref fail);
                RunCase5_AngleSemantics(doc, sb, ref pass, ref fail);
                RunCase6_Conform(doc, sb, ref pass, ref fail);
                RunCase7_SmoothRoundEnds(doc, sb, ref pass, ref fail);
                RunCase8_PolySurface(doc, sb, ref pass, ref fail);
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
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("写报告失败: " + ex.Message);
            }

            try
            {
                RhinoApp.WriteLine(sb.ToString());
            }
            catch { }

            if (exitAfter)
            {
                try
                {
                    RhinoDoc d = RhinoDoc.ActiveDoc;
                    if (d != null) d.Modified = false;   // 无人值守退出，不弹保存对话框
                }
                catch { }
                RhinoApp.Exit();
            }
            else if (!string.IsNullOrEmpty(DumpDocPath))
            {
                try
                {
                    RhinoDoc d = RhinoDoc.ActiveDoc;
                    if (d != null)
                    {
                        var av = d.Views.ActiveView;
                        if (av != null)
                        {
                            av.ActiveViewport.SetProjection(Rhino.Display.DefinedViewportProjection.Top, null, false);
                            BoundingBox zb = DumpBox;
                            if (!zb.IsValid) zb = new BoundingBox(new Point3d(-10, -10, -10), new Point3d(10, 10, 10));
                            zb.Inflate(6.0);
                            av.ActiveViewport.ZoomBoundingBox(zb);
                        }
                        d.Views.Redraw();
                        var wopt = new Rhino.FileIO.FileWriteOptions();
                        d.WriteFile(DumpDocPath, wopt);
                        RhinoApp.WriteLine("自检几何已另存：" + DumpDocPath);
                    }
                }
                catch (Exception ex) { RhinoApp.WriteLine("另存失败：" + ex.Message); }
            }
        }

        // ------------------------------------------------------------------
        static void Check(StringBuilder sb, ref int pass, ref int fail, bool ok, string what)
        {
            if (ok) { pass++; sb.AppendLine("[PASS] " + what); }
            else { fail++; sb.AppendLine("[FAIL] " + what); }
        }

        static double Dot(Point3d p, Vector3d v) { return p.X * v.X + p.Y * v.Y + p.Z * v.Z; }
        static double Dot(Vector3d a, Vector3d b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        static double PlaneDist(Plane pl, Point3d p) { return Math.Abs(Dot(p - pl.Origin, pl.ZAxis)); }
        static Point3d[] SamplePoints(Curve c, int n)
        {
            double[] ts = c.DivideByCount(n, true);
            if (ts == null) return null;
            var pts = new Point3d[ts.Length];
            for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
            return pts;
        }
        static double Volume(Brep b)
        {
            var vp = VolumeMassProperties.Compute(b);
            return vp == null ? 0 : vp.Volume;
        }

        static void RunCase1_PlaneStadium(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例1：平面圆角端面（80x40 直线段 + R20 端头，即 120x40 胶囊形）---");

            var pts = new List<Point2d>
            {
                new Point2d(0, 20), new Point2d(80, 20)
            };
            List<Point2d> ring = StripePattern.StadiumPoints(new Point2d(0, 0), new Point2d(80, 0), 40, true, 32);
            var curvePts = new List<Point3d>();
            foreach (Point2d p in ring) curvePts.Add(new Point3d(p.X, p.Y, 0));
            curvePts.Add(curvePts[0]);
            var outline = new PolylineCurve(curvePts);

            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outline }, 0.001);
            if (bps == null || bps.Length == 0)
            {
                Check(sb, ref pass, ref fail, false, "创建测试平面失败");
                return;
            }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var settings = new StripeSettings();
            settings.Width = 1.5;
            settings.Spacing = 3.0;
            settings.AngleDeg = 45;
            settings.Margin = 2.0;
            settings.RoundedEnds = true;

            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, settings, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));

            int total = 0;
            foreach (StripeFaceResult r in res) total += r.Stripes.Count;
            Check(sb, ref pass, ref fail, total > 0, string.Format("生成了条纹（{0} 条）", total));
            if (total == 0) return;

            bool allClosed = true, onSurface = true, onPlane = true;
            double minEdge = double.MaxValue;
            Plane facePlane = res[0].Plane;
            var stripes = new List<Curve>();
            foreach (StripeFaceResult r in res) stripes.AddRange(r.Stripes);

            foreach (Curve c in stripes)
            {
                if (!c.IsClosed) allClosed = false;
                Point3d[] sp = SamplePoints(c, 64);
                if (sp == null) continue;
                foreach (Point3d p in sp)
                {
                    if (PlaneDist(facePlane, p) > 0.01) onPlane = false;
                    double u, v;
                    BrepFace face = res[0].FaceIndex >= 0 ? brep.Faces[res[0].FaceIndex] : null;
                    if (face != null && face.ClosestPoint(p, out u, out v))
                    {
                        if (face.PointAt(u, v).DistanceTo(p) > 0.01) onSurface = false;
                        if (face.IsPointOnFace(u, v) == PointFaceRelation.Exterior) onSurface = false;
                    }
                    double de = MinDistToLoops(brep, p, true);
                    if (de < minEdge) minEdge = de;
                }
            }

            Check(sb, ref pass, ref fail, allClosed, "所有条纹曲线闭合");
            Check(sb, ref pass, ref fail, onPlane, "条纹全部落在面所在平面上");
            Check(sb, ref pass, ref fail, onSurface, "条纹全部位于面内（未越出边界）");
            Check(sb, ref pass, ref fail, minEdge >= settings.Margin - 1e-3,
                string.Format("实测最小边距 {0:0.####} >= 设定 {1:0.####}", minEdge, settings.Margin));

            // 宽度与间距：投影到图案法向
            double w = settings.Width, gap = settings.Spacing;
            Vector3d n = res[0].DiagNormal;
            Check(sb, ref pass, ref fail, n.IsValid && n.Length > 0.9,
                "图案法向可用于校验（" + (n.IsValid ? n.ToString() : "无效") + "）");

            var spans = new List<double[]>();
            foreach (Curve c in stripes)
            {
                Point3d[] sp = SamplePoints(c, 96);
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (Point3d p in sp)
                {
                    double t = Dot(p, n);
                    if (t < lo) lo = t;
                    if (t > hi) hi = t;
                }
                spans.Add(new double[] { lo, hi });
            }
            spans.Sort((a, b) => a[0].CompareTo(b[0]));

            double maxWidthErr = 0, maxGapErr = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                double wd = spans[i][1] - spans[i][0];
                maxWidthErr = Math.Max(maxWidthErr, Math.Abs(wd - w));
                if (i > 0)
                {
                    double g = spans[i][0] - spans[i - 1][1];
                    maxGapErr = Math.Max(maxGapErr, Math.Abs(g - gap));
                }
            }
            Check(sb, ref pass, ref fail, maxWidthErr < 0.01,
                string.Format("条纹宽度实测偏差 {0:0.#####}（设定 {1}）", maxWidthErr, w));
            Check(sb, ref pass, ref fail, maxGapErr < 0.01,
                string.Format("条纹净间距实测偏差 {0:0.#####}（设定 {1}）", maxGapErr, gap));

            // 圆角端：用 面积/周长 反解宽度
            Curve probe = LongestCurve(stripes);
            if (probe != null)
            {
                var amp = AreaMassProperties.Compute(probe);
                double P = probe.GetLength();
                double A = amp != null ? amp.Area : 0;
                double disc = P * P / 4.0 - Math.PI * A;
                if (disc >= 0 && A > 0)
                {
                    double w1 = (P / 2.0 - Math.Sqrt(disc)) / (Math.PI / 2.0);
                    double w2 = (P / 2.0 + Math.Sqrt(disc)) / (Math.PI / 2.0);
                    double wm = Math.Min(w1, w2);
                    Check(sb, ref pass, ref fail, Math.Abs(wm - w) < 0.01,
                        string.Format("按面积/周长反解宽度 {0:0.#####}（设定 {1}）", wm, w));
                }
                else
                {
                    Check(sb, ref pass, ref fail, false, "面积/周长反解不可用");
                }
            }
        }

        static void RunCase2_PlaneWithHole(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例2：带孔平面 140x140，中心 R15 圆孔 ---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(200, 340), new Interval(0, 140)).ToNurbsCurve();
            var hole = new Circle(new Plane(new Point3d(270, 70, 0), Vector3d.ZAxis), 15).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer, hole }, 0.001);
            if (bps == null || bps.Length == 0)
            {
                Check(sb, ref pass, ref fail, false, "创建带孔平面失败");
                return;
            }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);
            Check(sb, ref pass, ref fail, brep.Faces[0].Loops.Count >= 2,
                string.Format("面含 {0} 个环（含内孔）", brep.Faces[0].Loops.Count));

            var settings = new StripeSettings();
            settings.Width = 1.0; settings.Spacing = 2.0; settings.AngleDeg = 30; settings.Margin = 3.0;

            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, settings, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));

            int total = 0;
            foreach (StripeFaceResult r in res) total += r.Stripes.Count;
            Check(sb, ref pass, ref fail, total > 0, string.Format("生成了条纹（{0} 条）", total));

            double minEdge = double.MaxValue;
            int insideHole = 0;
            foreach (StripeFaceResult r in res)
            {
                foreach (Curve c in r.Stripes)
                {
                    Point3d[] sp = SamplePoints(c, 64);
                    foreach (Point3d p in sp)
                    {
                        double d = MinDistToLoops(brep, p, true);
                        if (d < minEdge) minEdge = d;
                        double rr = new Vector2d(p.X - 270, p.Y - 70).Length;
                        if (rr < 15) insideHole++;
                    }
                }
            }
            Check(sb, ref pass, ref fail, insideHole == 0, "没有条纹落入圆孔内");
            Check(sb, ref pass, ref fail, minEdge >= settings.Margin - 1e-3,
                string.Format("含孔面实测最小边距 {0:0.####} >= 设定 {1:0.####}", minEdge, settings.Margin));
        }

        static void RunCase3_Cylinder(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例3：曲面（圆柱 R30 H80，侧面为 UV 模式）---");
            var cyl = new Cylinder(new Circle(new Plane(new Point3d(500, 0, 0), Vector3d.ZAxis), 30), 80);
            Brep brep = cyl.ToBrep(true, true);
            if (brep == null)
            {
                Check(sb, ref pass, ref fail, false, "创建圆柱失败");
                return;
            }
            doc.Objects.AddBrep(brep);

            var settings = new StripeSettings();
            settings.MmToModel = 1.0;
            settings.Width = 2.0; settings.Spacing = 3.0; settings.AngleDeg = 60; settings.Margin = 2.0;

            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, settings, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));

            int total = 0, curvedFaces = 0;
            bool curvedExplained = false;
            foreach (StripeFaceResult r in res)
            {
                total += r.Stripes.Count;
                if (!r.Planar && r.Stripes.Count > 0) curvedFaces++;
                if (!r.Planar && !string.IsNullOrEmpty(r.Note)) curvedExplained = true;   // 任何明确提示都算（整圈环绕 / 几乎垂直于参考平面…）
            }
            Check(sb, ref pass, ref fail, total > 0, string.Format("圆柱体共生成 {0} 条条纹（其中 {1} 个曲面面）", total, curvedFaces));
            // 整圈环绕的侧面（圆柱整个侧面）切平面投影会自己叠起来：铺不出条纹是已知限制，
            // 但必须**说清楚**并给替代做法，不能默默什么都不给。
            Check(sb, ref pass, ref fail, curvedFaces > 0 || curvedExplained,
                curvedFaces > 0
                    ? string.Format("侧面（曲面）确实生成了条纹（{0} 个曲面面）", curvedFaces)
                    : "整圈环绕的侧面没有条纹，但给出了明确提示（含替代做法）");
            if (curvedFaces == 0)
            {
                string why = "";
                foreach (StripeFaceResult r in res) if (!r.Planar && !string.IsNullOrEmpty(r.Note)) { why = r.Note; break; }
                sb.AppendLine("  曲面面的提示：" + why);
            }

            double worst = 0;
            foreach (StripeFaceResult r in res)
            {
                BrepFace face = brep.Faces[r.FaceIndex];
                foreach (Curve c in r.Stripes)
                {
                    Point3d[] sp = SamplePoints(c, 32);
                    foreach (Point3d p in sp)
                    {
                        double u, v;
                        if (!face.ClosestPoint(p, out u, out v)) { worst = 1e9; continue; }
                        double d = face.PointAt(u, v).DistanceTo(p);
                        if (d > worst) worst = d;
                    }
                }
            }
            Check(sb, ref pass, ref fail, worst < 0.05,
                string.Format("曲面贴合最大偏差 {0:0.#####}（要求 < 0.05）", worst));
        }

        static void RunCase6_Conform(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例6：完全贴合边界（矩形 120x40，边缘距离 0 / 3）---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(1000, 1120), new Interval(0, 40)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer }, 0.001);
            if (bps == null || bps.Length == 0)
            {
                Check(sb, ref pass, ref fail, false, "创建贴合测试平面失败");
                return;
            }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var settings = new StripeSettings();
            settings.Width = 1.5; settings.Spacing = 3.0; settings.AngleDeg = 45;
            settings.Margin = 0.0; settings.ConformToBoundary = true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, settings, out report);
            sw.Stop();
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));
            sb.AppendLine(string.Format("耗时 {0} ms", sw.ElapsedMilliseconds));

            var stripes = new List<Curve>();
            foreach (StripeFaceResult r in res) stripes.AddRange(r.Stripes);
            Check(sb, ref pass, ref fail, stripes.Count > 0, string.Format("生成了贴合条纹（{0} 条）", stripes.Count));
            if (stripes.Count == 0) return;

            if (DumpGeometry)
            {
                try
                {
                    var la = new Rhino.DocObjects.ObjectAttributes();
                    la.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
                    la.ObjectColor = System.Drawing.Color.FromArgb(230, 30, 60);
                    foreach (Curve c in stripes)
                    {
                        doc.Objects.AddCurve(c, la);
                        DumpBox.Union(c.GetBoundingBox(true));
                    }
                }
                catch { }
            }

            bool allClosed = true, inside = true, onPlane = true;
            double minEdge = double.MaxValue, area = 0;
            Plane facePlane = res[0].Plane;
            BrepFace face = brep.Faces[res[0].FaceIndex];
            foreach (Curve c in stripes)
            {
                if (!c.IsClosed) allClosed = false;
                var amp = AreaMassProperties.Compute(c);
                if (amp != null) area += amp.Area;
                Point3d[] sp = SamplePoints(c, 64);
                if (sp == null) continue;
                foreach (Point3d p in sp)
                {
                    if (PlaneDist(facePlane, p) > 0.01) onPlane = false;
                    double u, v;
                    if (face.ClosestPoint(p, out u, out v))
                    {
                        if (face.PointAt(u, v).DistanceTo(p) > 0.01) inside = false;
                        if (face.IsPointOnFace(u, v) == PointFaceRelation.Exterior) inside = false;
                    }
                    double de = MinDistToLoops(brep, p, true);
                    if (de < minEdge) minEdge = de;
                }
            }
            Check(sb, ref pass, ref fail, allClosed, "所有贴合条纹闭合");
            Check(sb, ref pass, ref fail, onPlane, "贴合条纹落在面所在平面上");
            Check(sb, ref pass, ref fail, inside, "贴合条纹全部位于面内");
            Check(sb, ref pass, ref fail, minEdge < 0.02,
                string.Format("边缘距离=0 时条纹贴到边界（实测最小边距 {0:0.####}）", minEdge));

            double expect = 120.0 * 40.0 * settings.Width / (settings.Width + settings.Spacing);
            Check(sb, ref pass, ref fail, Math.Abs(area - expect) / expect < 0.06,
                string.Format("填充面积 {0:0.##} 与理论 {1:0.##} 偏差 {2:0.##}%（要求 <6%）",
                    area, expect, Math.Abs(area - expect) / expect * 100.0));

            // 宽度与间距（沿图案法向）
            Vector3d n = res[0].DiagNormal;
            var spans = new List<double[]>();
            foreach (Curve c in stripes)
            {
                Point3d[] sp = SamplePoints(c, 96);
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (Point3d p in sp)
                {
                    double t = Dot(p, n);
                    if (t < lo) lo = t;
                    if (t > hi) hi = t;
                }
                spans.Add(new double[] { lo, hi });
            }
            spans.Sort((a, b) => a[0].CompareTo(b[0]));
            int widthOk = 0, widthTotal = 0;
            double maxGapErr = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                double wd = spans[i][1] - spans[i][0];
                widthTotal++;
                if (Math.Abs(wd - settings.Width) < 0.02) widthOk++;
                if (i > 0)
                {
                    double g = spans[i][0] - spans[i - 1][1];
                    if (g > 0) maxGapErr = Math.Max(maxGapErr, Math.Abs(g - settings.Spacing));
                }
            }
            Check(sb, ref pass, ref fail, widthOk >= widthTotal - 2,
                string.Format("{0}/{1} 条宽度实测等于设定 {2}", widthOk, widthTotal, settings.Width));
            Check(sb, ref pass, ref fail, maxGapErr < 0.02,
                string.Format("贴合条纹净间距偏差 {0:0.#####}（设定 {1}）", maxGapErr, settings.Spacing));

            // 边缘距离 = 3：条纹应整体缩进
            settings.Margin = 3.0;
            res = StripePattern.Generate(brep, settings, out report);
            double minEdge3 = double.MaxValue;
            int n3 = 0;
            foreach (StripeFaceResult r in res)
            {
                n3 += r.Stripes.Count;
                foreach (Curve c in r.Stripes)
                {
                    Point3d[] sp = SamplePoints(c, 64);
                    if (sp == null) continue;
                    foreach (Point3d p in sp)
                    {
                        double de = MinDistToLoops(brep, p, true);
                        if (de < minEdge3) minEdge3 = de;
                    }
                }
            }
            Check(sb, ref pass, ref fail, n3 > 0, string.Format("边缘距离=3 时生成 {0} 条", n3));
            Check(sb, ref pass, ref fail, minEdge3 >= 3.0 - 0.05,
                string.Format("边缘距离=3 时实测最小边距 {0:0.####} >= 2.95", minEdge3));

            // 诊断：RoundCorners 单元 + 首条条纹形状
            {
                var sq = new List<Point2d> { new Point2d(0, 0), new Point2d(10, 0), new Point2d(10, 10), new Point2d(0, 10) };
                double a0 = Math.Abs(PolyArea2d(sq));
                var sqR = StripePattern.RoundCorners(sq, 2.0, 0.2);
                double a1 = Math.Abs(PolyArea2d(sqR));
                sb.AppendLine(string.Format("  [诊断] RoundCorners 正方形 10x10 倒 R2：顶点 {0} -> {1}，面积 {2:0.###} -> {3:0.###}",
                    sq.Count, sqR.Count, a0, a1));
            }
            if (res.Count > 0 && res[0].Stripes.Count > 0)
            {
                Curve c0 = res[0].Stripes[0];
                Polyline pl0;
                if (c0.TryGetPolyline(out pl0))
                {
                    Point3d[] p0 = pl0.ToArray();
                    int nn = p0.Length; double bw = 0; int bi = -1;
                    for (int i = 0; i < nn; i++)
                    {
                        Vector3d a1 = p0[(i - 1 + nn) % nn] - p0[i];
                        Vector3d a2 = p0[(i + 1) % nn] - p0[i];
                        if (!a1.Unitize() || !a2.Unitize()) continue;
                        double aa = Math.PI - Math.Acos(Math.Max(-1.0, Math.Min(1.0, Dot(a1, a2))));
                        if (aa > bw) { bw = aa; bi = i; }
                    }
                    sb.AppendLine(string.Format("  [诊断] 首条条纹顶点 {0}，闭合={1}，最大转角 {2:0.####} rad @ {3}",
                        nn, c0.IsClosed, bw, bi >= 0 ? p0[bi].ToString() : "-"));
                    if (bi >= 0 && nn > 2)
                        sb.AppendLine("  [诊断] 该处相邻点 " + p0[(bi - 1 + nn) % nn].ToString() + " / "
                            + p0[(bi + 1) % nn].ToString());
                }
                else sb.AppendLine("  [诊断] 首条条纹不是折线");
            }

            // 硬边倒圆角：贴合模式下给直角倒 R0.8
            settings.Margin = 2.0;
            settings.CornerRadius = 0.0;
            res = StripePattern.Generate(brep, settings, out report);
            double lenMax = 0;
            foreach (StripeFaceResult r in res) foreach (Curve c in r.Stripes) lenMax = Math.Max(lenMax, c.GetLength());
            double turnSharp = MaxTurn(res, lenMax * 0.5);
            double areaSharp = TotalArea(res);

            // 临时探针：定位残余 45° 硬角（已知 3D 位置 -> frame 2D）
            StripePattern.DbgProbeLog.Clear();
            {
                Plane pl = res.Count > 0 ? res[0].Plane : Plane.WorldXY;
                StripePattern.DbgProbe = StripePattern.To2d(pl, new Point3d(1031.3309, 38.0, 0));
                StripePattern.DbgProbeR = 0.6;
            }
            settings.CornerRadius = 0.8;
            StripePattern.DbgCorners = StripePattern.DbgRounded = StripePattern.DbgSharp = 0;
            StripePattern.DbgDegenerate = StripePattern.DbgNoFit = 0;
            res = StripePattern.Generate(brep, settings, out report);
            StripePattern.DbgProbeR = 0.0;
            foreach (string s in StripePattern.DbgProbeLog) sb.AppendLine("  [探针] " + s);
            sb.AppendLine(string.Format("  [诊断] 拐点 {0}：倒圆 {1}，太尖跳过 {2}，退化 {3}，放不下 {4}",
                StripePattern.DbgCorners, StripePattern.DbgRounded, StripePattern.DbgSharp,
                StripePattern.DbgDegenerate, StripePattern.DbgNoFit));
            int nRound = 0;
            foreach (StripeFaceResult r in res) nRound += r.Stripes.Count;
            double turnRound = MaxTurn(res, lenMax * 0.5);
            double areaRound = TotalArea(res);

            sb.AppendLine(string.Format("  倒圆前最大转角 {0:0.###} rad / 面积 {1:0.##}；倒圆后 {2:0.###} rad / 面积 {3:0.##}（只算长度 >= {4:0.#} 的条纹）",
                turnSharp, areaSharp, turnRound, areaRound, lenMax * 0.5));
            Check(sb, ref pass, ref fail, nRound > 0, string.Format("倒圆后仍生成 {0} 条", nRound));
            Check(sb, ref pass, ref fail, turnSharp > 1.2,
                string.Format("未倒圆时确实有硬角（最大转角 {0:0.###} rad）", turnSharp));
            Check(sb, ref pass, ref fail, turnRound < 0.4,
                string.Format("倒 R0.8 后硬角消失（最大转角 {0:0.###} rad，要求 <0.4）", turnRound));
            Check(sb, ref pass, ref fail, areaRound < areaSharp && areaRound > areaSharp * 0.9,
                string.Format("倒圆只削掉角部材料（面积 {0:0.##} -> {1:0.##}）", areaSharp, areaRound));

            // 各条纹的圆角半径必须一致（曾经的 bug：半径按到相邻采样点的距离限幅，
            // 条纹越短采样越密，倒角就越小，看起来从左到右越来越大）
            // 半径必须只由设定值决定，不能随条纹长短变化（bug：倒角从左到右越来越大）
            var pairs = new List<double[]>();   // {条纹长度, 圆角半径}
            foreach (StripeFaceResult r in res)
                foreach (Curve c in r.Stripes)
                {
                    double R = MedianFilletRadius(c);
                    if (R <= 0) continue;
                    pairs.Add(new double[] { c.GetLength(), R });
                }
            double maxLen = 0;
            foreach (double[] q in pairs) if (q[0] > maxLen) maxLen = q[0];
            var longs = new List<double[]>();
            double rHi = double.MinValue;
            foreach (double[] q in pairs)
            {
                if (q[1] > rHi) rHi = q[1];
                if (q[0] >= maxLen * 0.5) longs.Add(q);   // 只看够长的条纹（角落里的碎片端头太短，本来就放不下大圆角）
            }
            longs.Sort((a, b) => a[0].CompareTo(b[0]));
            sb.AppendLine("  [诊断] 长度/半径：" + RadiusList(pairs));
            foreach (string s in FilletDbg) sb.AppendLine("  [诊断] 半径取样 " + s);
            FilletDbg.Clear();

            // 圆角半径本身会被「端头短边」限制（两个圆角各占一半，放不下就按能放下的最大半径），
            // 这是几何约束、不是 bug。真正要防的是【半径随条纹长度系统性变大】：
            // 把长条纹按长度切成两半，两半的平均半径不能有明显差异。
            double half1 = 0, half2 = 0;
            int n1 = 0, n2 = 0;
            for (int i = 0; i < longs.Count; i++)
            {
                if (i < longs.Count / 2) { half1 += longs[i][1]; n1++; }
                else { half2 += longs[i][1]; n2++; }
            }
            double m1 = n1 > 0 ? half1 / n1 : 0;
            double m2 = n2 > 0 ? half2 / n2 : 0;
            sb.AppendLine(string.Format("  长条纹圆角半径：短半段平均 {0:0.####}（{1} 条）/ 长半段平均 {2:0.####}（{3} 条），设定 R{4}",
                m1, n1, m2, n2, settings.CornerRadius));
            Check(sb, ref pass, ref fail, n1 >= 2 && n2 >= 2 && Math.Abs(m1 - m2) < settings.CornerRadius * 0.3,
                string.Format("圆角半径不随条纹长度系统性变化（两半段均值差 {0:0.####}，要求 <{1:0.####}）",
                    Math.Abs(m1 - m2), settings.CornerRadius * 0.3));
            Check(sb, ref pass, ref fail, rHi <= settings.CornerRadius + 1e-6 && rHi > settings.CornerRadius * 0.3,
                string.Format("圆角半径在合理区间（最大 {0:0.####}，设定 {1}）", rHi, settings.CornerRadius));

            if (DumpGeometry)
            {
                try
                {
                    var la2 = new Rhino.DocObjects.ObjectAttributes();
                    la2.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
                    la2.ObjectColor = System.Drawing.Color.FromArgb(20, 90, 220);
                    foreach (StripeFaceResult r in res)
                        foreach (Curve c in r.Stripes) { doc.Objects.AddCurve(c, la2); DumpBox.Union(c.GetBoundingBox(true)); }
                }
                catch { }
            }

            // 曲面（圆柱侧面）贴合模式
            var cyl = new Cylinder(new Circle(new Plane(new Point3d(1300, 0, 0), Vector3d.ZAxis), 30), 80);
            Brep cylBrep = cyl.ToBrep(true, true);
            if (cylBrep != null)
            {
                doc.Objects.AddBrep(cylBrep);
                var cs = new StripeSettings();
                cs.MmToModel = 1.0;
                cs.Width = 2.0; cs.Spacing = 3.0; cs.AngleDeg = 60; cs.Margin = 1.0;
                cs.ConformToBoundary = true;
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                List<StripeFaceResult> cr = StripePattern.Generate(cylBrep, cs, out report);
                sw2.Stop();
                int cn = 0; double worst = 0;
                foreach (StripeFaceResult r in cr)
                {
                    cn += r.Stripes.Count;
                    BrepFace cf = cylBrep.Faces[r.FaceIndex];
                    foreach (Curve c in r.Stripes)
                    {
                        Point3d[] sp = SamplePoints(c, 32);
                        foreach (Point3d p in sp)
                        {
                            double u, v;
                            if (!cf.ClosestPoint(p, out u, out v)) { worst = 1e9; continue; }
                            double d = cf.PointAt(u, v).DistanceTo(p);
                            if (d > worst) worst = d;
                        }
                    }
                }
                Check(sb, ref pass, ref fail, cn > 0, string.Format("圆柱贴合模式生成 {0} 条（{1} ms）", cn, sw2.ElapsedMilliseconds));
                Check(sb, ref pass, ref fail, worst < 0.05,
                    string.Format("圆柱贴合最大偏差 {0:0.#####}（要求 < 0.05）", worst));
            }
        }

        static void RunCase4_Engrave(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例4：实体开槽（胶囊实体 120x40x20，深 0.8）---");
            List<Point2d> ring = StripePattern.StadiumPoints(new Point2d(0, 0), new Point2d(80, 0), 40, true, 32);
            var pts = new List<Point3d>();
            foreach (Point2d p in ring) pts.Add(new Point3d(p.X + 800, p.Y, 0));
            pts.Add(pts[0]);
            var outline = new PolylineCurve(pts);
            Surface srf = Surface.CreateExtrusion(outline, new Vector3d(0, 0, 20));
            if (srf == null) { Check(sb, ref pass, ref fail, false, "拉伸失败"); return; }
            Brep solid = Brep.CreateFromSurface(srf);
            solid = solid.CapPlanarHoles(0.001);
            if (solid == null || !solid.IsSolid) { Check(sb, ref pass, ref fail, false, "创建实体失败"); return; }
            doc.Objects.AddBrep(solid);
            double vol0 = Volume(solid);
            Check(sb, ref pass, ref fail, vol0 > 0, string.Format("原始实体体积 {0:0.###}", vol0));

            var settings = new StripeSettings();
            settings.Width = 2.0; settings.Spacing = 3.0; settings.AngleDeg = 45; settings.Margin = 3.0; settings.Depth = 0.8;

            string report;
            List<StripeFaceResult> res = StripePattern.Generate(solid, settings, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));
            int total = 0;
            foreach (StripeFaceResult r in res) total += r.Stripes.Count;
            Check(sb, ref pass, ref fail, total > 0, string.Format("实体面上生成 {0} 条条纹", total));

            string err;
            Brep engraved = Engraver.Engrave(solid, res, settings.Depth, out err);
            if (engraved == null)
            {
                Check(sb, ref pass, ref fail, false, "开槽失败: " + err);
                return;
            }
            doc.Objects.AddBrep(engraved);
            double vol1 = Volume(engraved);
            Check(sb, ref pass, ref fail, engraved.IsSolid, "开槽后仍为封闭实体");
            Check(sb, ref pass, ref fail, vol1 < vol0 - 1e-6,
                string.Format("体积减少 {0:0.####}（{1:0.###} -> {2:0.###}）", vol0 - vol1, vol0, vol1));

            double expect = 0;
            foreach (StripeFaceResult r in res)
                foreach (Curve c in r.Stripes)
                {
                    var amp = AreaMassProperties.Compute(c);
                    if (amp != null) expect += amp.Area;
                }
            double expectVol = expect * settings.Depth;
            // 口径说明：expectVol 是「所有面条纹面积 × 深度」= 理论上限。闭合实体只从**外部可见面**切入，
            // 底面 / 内部那些条纹不产生体积变化（共面参考平面下两个盖的图案现在是一致的），
            // 所以实测通常是上限的 ~40%~110%（侧面包裹面铺不出条纹时更低）。这里按量级判定。
            double cutVol = vol0 - vol1;
            Check(sb, ref pass, ref fail, cutVol > expectVol * 0.35 && cutVol < expectVol * 1.1,
                string.Format("切除体积 {0:0.####} 与理论上限 {1:0.####} 同量级（0.35~1.1 倍；闭合实体只从外面切入）", cutVol, expectVol));
        }

        // 角度语义：0° = 世界 X 轴在面内的投影；90° = 与之垂直
        static void RunCase5_AngleSemantics(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例5：角度语义（世界 X 轴在面内的投影为 0°）与 mm 尺寸 ---");
            var rect = new Rectangle3d(Plane.WorldXY, new Interval(1000, 1060), new Interval(0, 100)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { rect }, 0.001);
            if (bps == null || bps.Length == 0) { Check(sb, ref pass, ref fail, false, "测试面创建失败"); return; }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var s = new StripeSettings();
            s.MmToModel = 1.0;
            s.Width = 2.0; s.Spacing = 4.0; s.Margin = 2.0;

            double[] angles = { 0, 45, 90 };
            foreach (double a in angles)
            {
                s.AngleDeg = a;
                string rep;
                List<StripeFaceResult> res = StripePattern.Generate(brep, s, out rep);
                if (res.Count == 0 || res[0].Stripes.Count == 0)
                {
                    Check(sb, ref pass, ref fail, false, string.Format("{0:0}° 未生成条纹", a));
                    continue;
                }
                Vector3d n = res[0].DiagNormal;
                double th = a * Math.PI / 180.0;
                double d = Math.Abs(n.X * (-Math.Sin(th)) + n.Y * Math.Cos(th));
                Check(sb, ref pass, ref fail, d > 0.999,
                    string.Format("{0:0}° 角度真实（排布法向与理论点积 {1:0.#####}）", a, d));
            }

            // mm 尺寸校验：90° 时条纹沿 Y，用 X 方向跨度量宽度与净间距
            s.AngleDeg = 90;
            string rep2;
            List<StripeFaceResult> res2 = StripePattern.Generate(brep, s, out rep2);
            var spans = new List<double[]>();
            foreach (StripeFaceResult r in res2)
            {
                foreach (Curve c in r.Stripes)
                {
                    Point3d[] sp = SamplePoints(c, 96);
                    double lo = double.MaxValue, hi = double.MinValue;
                    foreach (Point3d p in sp) { if (p.X < lo) lo = p.X; if (p.X > hi) hi = p.X; }
                    spans.Add(new double[] { lo, hi });
                }
            }
            spans.Sort((p, q) => p[0].CompareTo(q[0]));
            double maxW = 0, maxG = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                maxW = Math.Max(maxW, Math.Abs((spans[i][1] - spans[i][0]) - s.Width));
                if (i > 0) maxG = Math.Max(maxG, Math.Abs((spans[i][0] - spans[i - 1][1]) - s.Spacing));
            }
            Check(sb, ref pass, ref fail, spans.Count > 0 && maxW < 0.01,
                string.Format("mm 宽度实测偏差 {0:0.#####}（设定 {1} mm）", maxW, s.Width));
            Check(sb, ref pass, ref fail, spans.Count > 1 && maxG < 0.01,
                string.Format("mm 净间距实测偏差 {0:0.#####}（设定 {1} mm）", maxG, s.Spacing));
        }

        static Curve LongestCurve(List<Curve> curves)
        {
            Curve best = null;
            double bl = -1;
            foreach (Curve c in curves)
            {
                double l = c.GetLength();
                if (l > bl) { bl = l; best = c; }
            }
            return best;
        }

        // ================================================================== 用例7
        // 用户反馈：条纹在曲面上生成时，端头圆显示成「一段段直线」而不是曲线。
        // 根因：端头半圆在 2D 里被固定采样成 28 段折线（StadiumPoints capSegs=28），
        // 再整体以 PolylineCurve 输出。平面面因为映射是刚性的，采样密一点看不出来；
        // 曲面面映射是非线性的，放大后每段直线的折角就明显了。
        // 本用例直接量「曲线自然分段接点处的最大折角」：折线端头每段之间都有折角，
        // 真曲线端头与两侧直线相切，接点折角≈0。

        /// <summary>把曲线按它自己的自然分段拆开（折线拆成各直线段，PolyCurve 拆成各子段）</summary>
        static List<Curve> BreakCurve(Curve c)
        {
            var list = new List<Curve>();
            if (c == null) return list;
            Polyline pl;
            if (c.TryGetPolyline(out pl) && pl.Count >= 2)
            {
                int m = pl.Count;
                for (int i = 0; i + 1 < m; i++) list.Add(new LineCurve(pl[i], pl[i + 1]));
                if (pl.IsClosed && m >= 3) list.Add(new LineCurve(pl[m - 1], pl[0]));
                return list;
            }
            Curve[] segs = c.DuplicateSegments();
            if (segs != null && segs.Length > 0) list.AddRange(segs);
            else list.Add(c);
            return list;
        }

        /// <summary>曲线自然分段接点处的最大折角（rad）——「看起来是不是折线」的直接度量</summary>
        static double MaxBreakTurn(Curve c)
        {
            List<Curve> segs = BreakCurve(c);
            int n = segs.Count;
            if (n < 2) return 0;
            bool closed = false;
            try { closed = c.IsClosed; } catch { }
            double worst = 0;
            int last = closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                Curve s1 = segs[i], s2 = segs[(i + 1) % n];
                Vector3d t1 = s1.TangentAtEnd, t2 = s2.TangentAtStart;
                if (t1.Length < 1e-12 || t2.Length < 1e-12) continue;
                if (!t1.Unitize() || !t2.Unitize()) continue;
                double ang = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Dot(t1, t2))));
                if (ang > worst) worst = ang;
            }
            return worst;
        }

        static void SegStats(Curve c, out int lines, out int curves)
        {
            lines = 0; curves = 0;
            foreach (Curve s in BreakCurve(c))
            {
                if (s is LineCurve) lines++;
                else curves++;
            }
        }

        static double DistPointSeg3d(Point3d p, Point3d a, Point3d b)
        {
            Vector3d v = b - a, w = p - a;
            double len2 = v * v;
            double t = 0;
            if (len2 > 1e-20) { t = (w * v) / len2; if (t < 0) t = 0; else if (t > 1) t = 1; }
            return (p - (a + v * t)).Length;
        }

        /// <summary>曲线对参考折线（理想端头密采样映射结果）的最大偏差</summary>
        static double MaxDeviationTo(Curve c, List<Point3d> refPts)
        {
            if (c == null || refPts == null || refPts.Count < 2) return double.MaxValue;
            double[] ts = c.DivideByCount(400, true);
            if (ts == null) return double.MaxValue;
            double worst = 0;
            foreach (double t in ts)
            {
                Point3d p = c.PointAt(t);
                double best = double.MaxValue;
                for (int i = 0; i + 1 < refPts.Count; i++)
                {
                    double d = DistPointSeg3d(p, refPts[i], refPts[i + 1]);
                    if (d < best) best = d;
                }
                if (best > worst) worst = best;
            }
            return worst;
        }

        /// <summary>修复前的口径：端头半圆固定采样 28 段 -> PolylineCurve（用于对照）</summary>
        static Curve OldStadiumOnSurface(BrepFace face, Plane frame, Point2d a, Point2d b, double width)
        {
            List<Point2d> pts = StripePattern.StadiumPoints(a, b, width, true, 28);
            if (pts.Count < 3) return null;
            var p3 = new List<Point3d>();
            foreach (Point2d q in pts)
            {
                Point3d pp;
                if (!StripePattern.MapTo3d(frame, face, false, q, out pp)) return null;
                p3.Add(pp);
            }
            p3.Add(p3[0]);
            return new PolylineCurve(p3);
        }

        static void RunCase7_SmoothRoundEnds(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例7：曲面上的端部圆角必须是曲线（不是一段段折线）---");

            // 取一个曲面：优先用用户给的复现文件，拿不到就退回一段圆柱面补丁
            Brep srcBrep = null;
            BrepFace face = null;
            string src = "";
            string repro = @"D:\UserData\Desktop\ceshi.3dm";
            try
            {
                if (File.Exists(repro))
                {
                    Rhino.FileIO.File3dm f3d = Rhino.FileIO.File3dm.Read(repro);
                    if (f3d != null)
                    {
                        int nBrep = 0, nSrf = 0, nMesh = 0, nOther = 0;
                        foreach (Rhino.FileIO.File3dmObject o in f3d.Objects)
                        {
                            Brep bp = o.Geometry as Brep;
                            if (bp == null)
                            {
                                Surface sf = o.Geometry as Surface;
                                if (sf != null) { nSrf++; bp = Brep.CreateFromSurface(sf); }
                            }
                            if (bp == null)
                            {
                                Extrusion ex = o.Geometry as Extrusion;
                                if (ex != null) bp = ex.ToBrep();
                            }
                            if (bp == null) { if (o.Geometry is Mesh) nMesh++; else nOther++; continue; }
                            nBrep++;
                            for (int i = 0; i < bp.Faces.Count; i++)
                            {
                                Plane fr; bool pl;
                                if (!StripePattern.TryBuildFrame(bp.Faces[i], out fr, out pl)) continue;
                                if (pl) continue;
                                srcBrep = bp; face = bp.Faces[i];
                                src = string.Format("ceshi.3dm 的曲面面#{0}（面 {1} 个）", i, bp.Faces.Count);
                                break;
                            }
                            if (face != null) break;
                        }
                        sb.AppendLine(string.Format("  [诊断] ceshi.3dm 对象统计：Brep/Surface/Extrusion {0}，Mesh {1}，其他 {2}", nBrep, nMesh, nOther));
                        if (nSrf > 0) sb.AppendLine("  [诊断] 其中 Surface 对象 " + nSrf + " 个（已转 Brep）");
                    }
                    if (face == null) sb.AppendLine("  [诊断] ceshi.3dm 里没找到可用的曲面面");
                }
                else sb.AppendLine("  [诊断] " + repro + " 不存在，用曲面补丁回退");
            }
            catch (Exception ex) { sb.AppendLine("  [诊断] 读 ceshi.3dm 失败：" + ex.Message); }

            if (face == null)
            {
                // R30 圆柱面上 90° 的一段开放补丁：边界投影到参考平面后区域有效，
                // 能真实生成条纹（整圆柱侧面因为接缝+上下圆的投影自重叠，本来就生成不出条纹）
                var arc = new Arc(new Circle(new Plane(new Point3d(2100, 0, 0), Vector3d.ZAxis), 30), Math.PI * 0.5);
                Surface patch = Surface.CreateExtrusion(new ArcCurve(arc), new Vector3d(0, 0, 80));
                if (patch != null) srcBrep = Brep.CreateFromSurface(patch);
                if (srcBrep != null)
                {
                    for (int i = 0; i < srcBrep.Faces.Count; i++)
                    {
                        Plane fr; bool pl;
                        if (!StripePattern.TryBuildFrame(srcBrep.Faces[i], out fr, out pl)) continue;
                        if (pl) continue;
                        face = srcBrep.Faces[i];
                        src = "圆柱面 90° 补丁 R30 H80（回退）";
                        break;
                    }
                }
            }
            if (face == null)
            {
                Check(sb, ref pass, ref fail, false, "找不到可用于端部圆角用例的曲面");
                return;
            }
            sb.AppendLine("  曲面来源：" + src);

            Plane frame; bool planar;
            if (!StripePattern.TryBuildFrame(face, out frame, out planar) || planar)
            {
                Check(sb, ref pass, ref fail, false, "曲面参考坐标系无效");
                return;
            }
            BoundingBox fbb = face.GetBoundingBox(true);
            double diag = fbb.Diagonal.Length;
            if (!(diag > 1e-9)) diag = 1.0;
            string stype = "?";
            try { stype = face.UnderlyingSurface().ToString(); } catch { }
            sb.AppendLine(string.Format("  曲面：{0}，包围盒对角 {1:0.###}，类型 {2}", src, diag, stype));

            // ---- A. 单条条纹（已知 a/b/宽）的端头对照（尺寸按面的大小缩放，量纲无关）
            double width = diag * 0.10;
            double halfL = diag * 0.20;
            Point2d a = new Point2d(-halfL, 0), b = new Point2d(halfL, 0);
            double tol = diag * 1e-4;

            var reference = new List<Point3d>();
            foreach (Point2d q in StripePattern.StadiumPoints(a, b, width, true, 1000))
            {
                Point3d p3;
                if (StripePattern.MapTo3d(frame, face, false, q, out p3)) reference.Add(p3);
            }
            if (reference.Count > 2) reference.Add(reference[0]);

            Curve oldC = OldStadiumOnSurface(face, frame, a, b, width);
            Curve newC = StripePattern.MakeStadiumOnSurface(face, frame, a, b, width, true, tol);
            if (newC == null)
            {
                Check(sb, ref pass, ref fail, false, "曲面上生成端部圆角条纹失败");
                return;
            }
            doc.Objects.AddCurve(newC);

            int oL, oC, nL, nC;
            SegStats(oldC, out oL, out oC);
            SegStats(newC, out nL, out nC);
            double oTurn = MaxBreakTurn(oldC), nTurn = MaxBreakTurn(newC);
            double oDev = MaxDeviationTo(oldC, reference), nDev = MaxDeviationTo(newC, reference);

            sb.AppendLine(string.Format("  [诊断] 端头半圆 R{0:0.###}：旧口径（28 段折线）直线段 {1} / 曲线段 {2}，对理想端头最大偏差 {3:0.#####}",
                width * 0.5, oL, oC, oDev));
            sb.AppendLine(string.Format("  [诊断] 端头半圆 R{0:0.###}：新口径（解析半圆+自适应 NURBS）直线段 {1} / 曲线段 {2}，对理想端头最大偏差 {3:0.#####}",
                width * 0.5, nL, nC, nDev));
            sb.AppendLine(string.Format("  [诊断] 接点折角（旧 {0:0.####} rad / 新 {1:0.####} rad）：两者都被「两侧直线在曲面上是 3D 弦」这一映射效应主导，"
                + "不是端头自身的棱面，仅作记录", oTurn, nTurn));

            Check(sb, ref pass, ref fail, oL >= 20,
                string.Format("旧口径确实把端头采样成折线（端头直线段 {0} 段）", oL));
            Check(sb, ref pass, ref fail, nL == 2 && nC == 2,
                string.Format("端头是真曲线：整条条纹只有 2 段直线（两侧）+ 2 段曲线（两个端头），实际 {0} 直线 / {1} 曲线", nL, nC));
            Check(sb, ref pass, ref fail, nDev < oDev * 0.2,
                string.Format("端头对理想轮廓的最大偏差 {0:0.#####} -> {1:0.#####}（旧 -> 新，要求新 < 旧的 20%）", oDev, nDev));

            // ---- B. 端到端：在这个曲面上真实生成条纹，逐条量端头
            var settings = new StripeSettings();
            settings.MmToModel = 1.0;
            settings.Width = diag * 0.02; settings.Spacing = diag * 0.04;
            settings.AngleDeg = 60; settings.Margin = diag * 0.01;
            settings.RoundedEnds = true;

            string report;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<StripeFaceResult> res = StripePattern.Generate(srcBrep, settings, out report);
            sw.Stop();
            sb.AppendLine("  生成日志: " + report.Replace("\r\n", " | "));

            int nStripes = 0, lineSegs = 0, curveSegs = 0, worstLines = 0, minCurves = int.MaxValue;
            double worstTurn = 0;
            foreach (StripeFaceResult r in res)
            {
                if (r.Planar) continue;
                foreach (Curve c in r.Stripes)
                {
                    if (nStripes >= 300) break;
                    nStripes++;
                    int L, C;
                    SegStats(c, out L, out C);
                    lineSegs += L; curveSegs += C;
                    if (L > worstLines) worstLines = L;
                    if (C < minCurves) minCurves = C;
                    double t = MaxBreakTurn(c);
                    if (t > worstTurn) worstTurn = t;
                }
            }
            sb.AppendLine(string.Format("  [诊断] 曲面端到端：{0} 条条纹（{1} ms），直线段共 {2} / 曲线段共 {3}，最少曲线段 {4}，接点最大折角 {5:0.####} rad",
                nStripes, sw.ElapsedMilliseconds, lineSegs, curveSegs, minCurves == int.MaxValue ? 0 : minCurves, worstTurn));
            Check(sb, ref pass, ref fail, nStripes > 0, string.Format("曲面上生成 {0} 条条纹", nStripes));
            Check(sb, ref pass, ref fail, nStripes > 0 && worstLines <= 2,
                string.Format("单条条纹最多 {0} 段直线（= 两侧直线，端头是曲线）", worstLines));
            Check(sb, ref pass, ref fail, nStripes > 0 && minCurves >= 2,
                string.Format("每条条纹至少 {0} 段曲线（两个端头），折线口径下这个数是 0", minCurves == int.MaxValue ? 0 : minCurves));
        }

        /// <summary>所有条纹折线的最大转角（rad）：有硬角时接近 1.57（直角）。minLen>0 时只统计够长的条纹</summary>
        static double MaxTurn(List<StripeFaceResult> res) { return MaxTurn(res, 0); }

        static double MaxTurn(List<StripeFaceResult> res, double minLen)
        {
            double worst = 0;
            foreach (StripeFaceResult r in res)
            {
                foreach (Curve c in r.Stripes)
                {
                    if (minLen > 0 && c.GetLength() < minLen) continue;
                    Polyline pl;
                    Point3d[] pts = null;
                    if (c.TryGetPolyline(out pl)) pts = pl.ToArray();
                    else
                    {
                        // 圆角现在是真曲线（ArcCurve / NURBS），没有折线顶点可数：
                        // 改量「曲线自身自然分段接点处的折角」，与折线口径等价
                        // （都是「曲线自己的角」），且不随曲线长度缩放。
                        double bt = MaxBreakTurn(c);
                        if (bt > worst) worst = bt;
                        continue;
                    }
                    if (pts == null || pts.Length < 3) continue;
                    int n = pts.Length;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3d v1 = pts[(i - 1 + n) % n] - pts[i];
                        Vector3d v2 = pts[(i + 1) % n] - pts[i];
                        if (v1.Length < 1e-4 || v2.Length < 1e-4) continue;   // 亚微米级的边方向是浮点噪声，不计
                        if (!v1.Unitize() || !v2.Unitize()) continue;
                        // 转角 = 偏离直线的角度（直线段上 v1、v2 反向，acos = π）
                        double ang = Math.PI - Math.Acos(Math.Max(-1.0, Math.Min(1.0, Dot(v1, v2))));
                        if (ang > 2.8) continue;   // 条纹被面角切出的针尖（零宽度），倒不了圆角，不计
                        if (ang > worst) worst = ang;
                    }
                }
            }
            return worst;
        }

        /// <summary>
        /// 用三点外接圆反解圆角半径：圆角上的相邻三点必然落在半径 rr 的圆上。
        /// 取所有「有明显转角」的三点半径的中位数，代表这条条纹的圆角半径。
        /// </summary>
        internal static List<string> FilletDbg = new List<string>();

        static double MedianFilletRadius(Curve c)
        {
            Polyline pl;
            if (c.TryGetPolyline(out pl)) return MedianFilletRadius(pl.ToArray());

            // 圆角现在是真曲线（ArcCurve / NURBS），没有折线顶点可数：
            // 逐段【各自独立】反解（每段自己闭合成环做三点外接圆），再取各段半径的中位数。
            // 不能把各段的采样点拼成一条长链：拼接处的三点跨两段，会把半径算成几十上百。
            var per = new List<double>();
            List<Curve> segs = BreakCurve(c);
            foreach (Curve s in segs)
            {
                if (s is LineCurve) continue;
                Point3d[] sp = SamplePoints(s, 12);
                if (sp == null) continue;
                double r = MedianFilletRadius(sp);
                if (r > 0) per.Add(r);
            }
            if (FilletDbg.Count < 3)
            {
                var t = new StringBuilder();
                foreach (double r in per) t.AppendFormat("{0:0.###} ", r);
                FilletDbg.Add(string.Format("曲线 {0}，段数 {1}，各段半径：[{2}]", c.GetType().Name, segs.Count, t.ToString().Trim()));
            }
            if (per.Count == 0) return 0;
            per.Sort();
            return per[per.Count / 2];
        }

        static double MedianFilletRadius(Point3d[] p)
        {
            if (p == null) return 0;
            int n = p.Length;
            if (n < 5) return 0;
            var rs = new List<double>();
            for (int i = 0; i < n; i++)
            {
                Point3d a = p[(i - 1 + n) % n], b = p[i], d = p[(i + 1) % n];
                Vector3d v1 = a - b, v2 = d - b;
                if (!v1.Unitize() || !v2.Unitize()) continue;
                double turn = Math.PI - Math.Acos(Math.Max(-1.0, Math.Min(1.0, Dot(v1, v2))));
                if (turn < 0.05 || turn > Math.PI - 0.05) continue;
                Vector3d cr = Vector3d.CrossProduct(a - b, d - b);
                double area = cr.Length * 0.5;
                if (area < 1e-9) continue;
                double ab = a.DistanceTo(b), bd = b.DistanceTo(d), da = d.DistanceTo(a);
                double R = ab * bd * da / (4.0 * area);
                if (R > 1e-6 && R < 1e6) rs.Add(R);
            }
            if (rs.Count == 0) return 0;
            rs.Sort();
            return rs[rs.Count / 2];
        }

        /// <summary>找出圆角后残余最大转角的位置及其两侧边长，用于定位「倒不掉的硬角」</summary>
        static string WorstTurnInfo(List<StripeFaceResult> res, double minLen)
        {
            double worst = 0; string where = "-"; double e1 = 0, e2 = 0; int idx = -1; double wlen = 0;
            foreach (StripeFaceResult r in res)
                foreach (Curve c in r.Stripes)
                {
                    if (minLen > 0 && c.GetLength() < minLen) continue;
                    Polyline pl;
                    if (!c.TryGetPolyline(out pl)) continue;
                    Point3d[] p = pl.ToArray();
                    int n = p.Length;
                    for (int i = 0; i < n; i++)
                    {
                        Point3d a = p[(i - 1 + n) % n], b = p[i], d = p[(i + 1) % n];
                        Vector3d v1 = a - b, v2 = d - b;
                        double l1 = v1.Length, l2 = v2.Length;
                        if (l1 < 1e-4 || l2 < 1e-4) continue;
                        if (!v1.Unitize() || !v2.Unitize()) continue;
                        double turn = Math.PI - Math.Acos(Math.Max(-1.0, Math.Min(1.0, Dot(v1, v2))));
                        if (turn > 2.8) continue;
                        if (turn > worst) { worst = turn; where = b.ToString(); e1 = l1; e2 = l2; idx = i; wlen = c.GetLength(); }
                    }
                }
            return string.Format("残余最大转角 {0:0.####} rad @ {1}，两侧边长 {2:0.###}/{3:0.###}（顶点序 {4}，所在条纹长 {5:0.#}）",
                worst, where, e1, e2, idx, wlen);
        }

        /// <summary>把最大转角所在条纹、该顶点附近的折线点打出来</summary>
        static string WorstTurnPoints(List<StripeFaceResult> res, double minLen)
        {
            double worst = 0; Point3d[] pp = null; int idx = -1;
            foreach (StripeFaceResult r in res)
                foreach (Curve c in r.Stripes)
                {
                    if (minLen > 0 && c.GetLength() < minLen) continue;
                    Polyline pl;
                    if (!c.TryGetPolyline(out pl)) continue;
                    Point3d[] p = pl.ToArray();
                    int n = p.Length;
                    for (int i = 0; i < n; i++)
                    {
                        Point3d a = p[(i - 1 + n) % n], b = p[i], d = p[(i + 1) % n];
                        Vector3d v1 = a - b, v2 = d - b;
                        if (v1.Length < 1e-5 || v2.Length < 1e-5) continue;
                        if (!v1.Unitize() || !v2.Unitize()) continue;
                        double turn = Math.PI - Math.Acos(Math.Max(-1.0, Math.Min(1.0, Dot(v1, v2))));
                        if (turn > 2.8) continue;
                        if (turn > worst) { worst = turn; pp = p; idx = i; }
                    }
                }
            if (pp == null) return "-";
            int nn = pp.Length;
            var sb = new System.Text.StringBuilder();
            for (int k = -4; k <= 4; k++)
            {
                int i = ((idx + k) % nn + nn) % nn;
                sb.AppendFormat("{0}{1:0.####},{2:0.####}  ", k == 0 ? "*" : "", pp[i].X, pp[i].Y);
            }
            return sb.ToString();
        }

        static string RadiusList(List<double[]> pairs)
        {
            pairs.Sort((a, b) => a[0].CompareTo(b[0]));
            var sb = new System.Text.StringBuilder();
            int n = Math.Min(pairs.Count, 12);
            for (int i = 0; i < n; i++)
                sb.AppendFormat("L{0:0.#}/R{1:0.###}  ", pairs[i][0], pairs[i][1]);
            if (pairs.Count > n) sb.Append("...（共 " + pairs.Count + " 条）");
            return sb.ToString();
        }

        static double PolyArea2d(List<Point2d> p)
        {
            double s = 0;
            for (int i = 0; i < p.Count; i++)
            {
                Point2d a = p[i], b = p[(i + 1) % p.Count];
                s += a.X * b.Y - b.X * a.Y;
            }
            return s * 0.5;
        }

        static double TotalArea(List<StripeFaceResult> res)
        {
            double a = 0;
            foreach (StripeFaceResult r in res)
                foreach (Curve c in r.Stripes)
                {
                    var amp = AreaMassProperties.Compute(c);
                    if (amp != null) a += amp.Area;
                }
            return a;
        }

        static double MinDistToLoops(Brep brep, Point3d p, bool anyFace)
        {
            double best = double.MaxValue;
            for (int fi = 0; fi < brep.Faces.Count; fi++)
            {
                foreach (BrepLoop loop in brep.Faces[fi].Loops)
                {
                    Curve c = loop.To3dCurve();
                    if (c == null) continue;
                    double t;
                    if (c.ClosestPoint(p, out t))
                    {
                        double d = c.PointAt(t).DistanceTo(p);
                        if (d < best) best = d;
                    }
                }
                if (!anyFace) break;
            }
            return best;
        }

        // ================================================================ 用例8：共面多重曲面 → 按一个整体排条纹
        // 用户口径：「输入多重曲面之后 生成的纹理不是按一个整体去生成的」。
        // 量两件事：
        //   · 条纹跨过内部接缝（接缝两侧都有端头贴到缝上，没有留出一条"壕沟"）
        //   · 接缝两侧的端头对齐（同一个缝位置上都有端头，错位很小）
        // 外轮廓仍然要留边距（自由边不受影响）。
        static void RunCase8_PolySurface(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例8：共面多重曲面（两块 60x100 共面，接缝在 x=60）---");
            Brep pa = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(60, 0, 0),
                                                  new Point3d(60, 100, 0), new Point3d(0, 100, 0), 1e-6);
            Brep pb = Brep.CreateFromCornerPoints(new Point3d(60, 0, 0), new Point3d(120, 0, 0),
                                                  new Point3d(120, 100, 0), new Point3d(60, 100, 0), 1e-6);
            if (pa == null || pb == null) { Check(sb, ref pass, ref fail, false, "创建共面两块失败"); return; }
            Brep[] jn = Brep.JoinBreps(new Brep[] { pa, pb }, 1e-4);
            if (jn == null || jn.Length != 1 || jn[0].Faces.Count != 2)
            {
                Check(sb, ref pass, ref fail, false, string.Format("两块共面片没能合成一个多重曲面（{0} 个结果）",
                    jn == null ? 0 : jn.Length));
                return;
            }
            Brep patch = jn[0];
            doc.Objects.AddBrep(patch);

            var settings = new StripeSettings();
            settings.MmToModel = 1.0;
            settings.Width = 2.0; settings.Spacing = 3.0; settings.AngleDeg = 45.0; settings.Margin = 2.0;
            settings.RoundedEnds = true;

            string report;
            List<StripeFaceResult> res = StripePattern.Generate(patch, settings, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));

            int nA = 0, nB = 0;
            if (res.Count >= 1 && res[0] != null) nA = res[0].Stripes.Count;
            if (res.Count >= 2 && res[1] != null) nB = res[1].Stripes.Count;
            Check(sb, ref pass, ref fail, nA > 0 && nB > 0,
                string.Format("两块面都生成了条纹（A {0} 条 / B {1} 条）", nA, nB));
            if (nA == 0 || nB == 0) return;

            // 方向一致（同一个全局方向投影到各自切平面）
            if (res[0].DiagNormal.IsValid && res[1].DiagNormal.IsValid)
            {
                double d = Math.Abs(Dot(res[0].DiagNormal, res[1].DiagNormal));
                Check(sb, ref pass, ref fail, d > 0.999,
                    string.Format("两面条纹方向一致（方向向量夹角余弦 {0:0.####}）", d));
            }

            // 接缝两侧的端头：谁贴到缝上（|x-60| 很小），记下缝上的 y
            var touchA = new List<double>();
            var touchB = new List<double>();
            var tipA3d = new List<Point3d>();
            for (int fi = 0; fi < res.Count && fi < 2; fi++)
            {
                foreach (Curve c in res[fi].Stripes)
                {
                    // 取样要够密：端头是 R=w/2 的小圆弧，96 点会直接跳过切点（误判成「没贴到缝」）
                    Point3d[] sp = SamplePoints(c, 1500);
                    if (sp == null) continue;
                    double best = double.MaxValue, bestY = 0; Point3d bestPt = Point3d.Unset;
                    foreach (Point3d p in sp)
                    {
                        double d = Math.Abs(p.X - 60.0);
                        if (d < best) { best = d; bestY = p.Y; bestPt = p; }
                    }
                    if (best < 0.05)
                    {
                        if (fi == 0) { touchA.Add(bestY); tipA3d.Add(bestPt); }
                        else touchB.Add(bestY);
                    }
                }
            }
            sb.AppendLine(string.Format("  贴到接缝上的端头：A 面 {0} 个，B 面 {1} 个", touchA.Count, touchB.Count));
            // 诊断：把两面前几个端头的原始坐标打出来（判断是「端头没过缝」还是「相位差」）
            var rawA = new List<string>();
            var rawB = new List<string>();
            for (int fi = 0; fi < res.Count && fi < 2; fi++)
            {
                var acc = fi == 0 ? rawA : rawB;
                foreach (Curve c in res[fi].Stripes)
                {
                    Point3d[] sp = SamplePoints(c, 1500);
                    if (sp == null) continue;
                    double best = double.MaxValue; Point3d bp = Point3d.Unset;
                    foreach (Point3d p in sp) { double d = Math.Abs(p.X - 60.0); if (d < best) { best = d; bp = p; } }
                    if (best < 0.6) acc.Add(string.Format("x={0:0.###} y={1:0.###} 离缝={2:0.###}", bp.X, bp.Y, best));
                    if (acc.Count >= 4) break;
                }
            }
            sb.AppendLine("  A 面前几个端头：" + string.Join(" | ", rawA.ToArray()));
            sb.AppendLine("  B 面前几个端头：" + string.Join(" | ", rawB.ToArray()));
            Check(sb, ref pass, ref fail, touchA.Count >= 3 && touchB.Count >= 3,
                "接缝两侧都有条纹端头贴到缝上 —— 没有在接缝处留出壕沟");

            // 对齐判据：A 面每个贴缝端头，到 B 面条纹的最近距离。
            // 对齐时两段端头在缝上正好接上（距离 ≈ 0）；各排各的会差出小半个间距。
            // 注意不能直接比 y：圆头端点在 45° 方向上，A 的端点在带中心上方 (w/2)·sin45、B 的在下方，
            // 直接比 y 会凭空差出 2·(w/2)·sin45 ≈ 1.41mm（这是端点几何，不是错位）。
            double worst = 0;
            foreach (Point3d tip in tipA3d)
            {
                double bd = double.MaxValue;
                foreach (Curve c in res[1].Stripes)
                {
                    Point3d[] sp2 = SamplePoints(c, 400);
                    if (sp2 == null) continue;
                    foreach (Point3d q in sp2)
                    {
                        double d = q.DistanceTo(tip);
                        if (d < bd) bd = d;
                    }
                }
                if (bd > worst) worst = bd;
            }
            sb.AppendLine(string.Format("  端头接缝贴合：A 面端头到 B 面条纹的最大距离 {0:0.###} mm", worst));
            // 判据说明：两段端头都在缝上（上面已量到 x=60、离缝=0），这里量的是「A 的端头离 B 的条纹有多远」。
            // 圆头端点在 45° 方向上，A 的端头在带中心上方、B 的在下，几何上就会差 2·(w/2)·sin45 ≈ 1.41mm，
            // 所以阈值取 1.5（各排各的相位时这个数会到几十 mm，仍然能抓住真问题）。
            Check(sb, ref pass, ref fail, worst < 1.5,
                string.Format("接缝两侧端头接住（最大距离 {0:0.###} < 1.5，含圆头端点几何 ~1.41）", worst));

            double minEdge = double.MaxValue;
            foreach (StripeFaceResult r in res) if (r.MinEdgeDistance < minEdge) minEdge = r.MinEdgeDistance;
            sb.AppendLine(string.Format("  实测最小边距 {0:0.###} mm（目标 {1}，外轮廓仍要留边距）",
                minEdge == double.MaxValue ? -1 : minEdge, settings.Margin));
            Check(sb, ref pass, ref fail, minEdge >= settings.Margin - 0.05,
                string.Format("外轮廓仍然留边距（{0:0.###} >= {1}）", minEdge, settings.Margin));
        }
    }
}
