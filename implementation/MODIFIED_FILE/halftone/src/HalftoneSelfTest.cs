using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;

namespace HalftonePattern
{
    /// <summary>
    /// 自检：用真实几何验证阵列生成的核心不变量。
    /// 重点验证「曲面上的图形不变形」——每个图形按自身切平面构造，周长必须等于 2πr。
    /// 用法：命令 HalftoneSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放 run-halftone-selftest.flag 后重启 Rhino。
    /// </summary>
    public class HalftoneSelfTestCommand : Command
    {
        public override string EnglishName { get { return "HalftoneSelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\HalftoneSelfTest.txt");
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
            int pass = 0, fail = 0;
            sb.AppendLine("=== 参数化阵列纹理 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                Case1_CurvedCircles(doc, sb, ref pass, ref fail);
                Case2_RingsFromPickedCenter(doc, sb, ref pass, ref fail);
                Case3_PlanarGrid(doc, sb, ref pass, ref fail);
                Case4_FaceScope(doc, sb, ref pass, ref fail);
                Case5_GlobalLayout(doc, sb, ref pass, ref fail);
                Case6_UserFile(doc, sb, ref pass, ref fail);
                Case7_GradientPick(doc, sb, ref pass, ref fail);
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

        static Point3d[] Sample(Curve c, int n)
        {
            double[] ts = c.DivideByCount(n, true);
            if (ts == null) return null;
            var pts = new Point3d[ts.Length];
            for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
            return pts;
        }

        // ---------------------------------------------------------------- 用例1：曲面上的圆形不变形
        static void Case1_CurvedCircles(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例1：曲面（R30 柱面，120° 弧片）上的圆点必须还是圆（周长 = 2πr）---");
            // 圆弧拉伸成柱面片：就是「曲面面板」的典型形状
            var arc = new Arc(new Circle(new Plane(Point3d.Origin, Vector3d.ZAxis), 30), 2.0943951023931953); // 120°
            Surface panel = Surface.CreateExtrusion(new ArcCurve(arc), new Vector3d(0, 0, 100));
            Brep side = panel == null ? null : Brep.CreateFromSurface(panel);
            if (side == null) { Check(sb, ref pass, ref fail, false, "创建柱面片失败"); return; }
            doc.Objects.AddBrep(side);

            var s = new HalftoneSettings();
            s.MmToModel = 1.0;
            s.MaxDia = 8.0; s.MinDia = 8.0;                // 等直径，方便逐个校验
            s.Pitch = 14.0; s.Margin = 1.0; s.Shape = 0; s.ArrayMode = 0;

            string report;
            List<HalftoneFaceResult> res = Halftone.Generate(side, s, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));

            var curves = new List<Curve>();
            foreach (HalftoneFaceResult r in res) curves.AddRange(r.Shapes);
            Check(sb, ref pass, ref fail, curves.Count > 0, string.Format("曲面上生成了圆点（{0} 个）", curves.Count));
            if (curves.Count == 0) return;

            double rad = s.MaxDia * 0.5;
            double expect = 2 * Math.PI * rad;
            double worstLen = 0, worstSurf = 0;
            bool allClosed = true;
            BrepFace face = side.Faces[0];
            foreach (Curve c in curves)
            {
                if (!c.IsClosed) allClosed = false;
                double len = c.GetLength();
                double err = Math.Abs(len - expect) / expect;
                if (err > worstLen) worstLen = err;

                Point3d[] pts = Sample(c, 24);
                foreach (Point3d p in pts)
                {
                    double u, v;
                    if (!face.ClosestPoint(p, out u, out v)) { worstSurf = 1e9; continue; }
                    double d = face.PointAt(u, v).DistanceTo(p);
                    if (d > worstSurf) worstSurf = d;
                }
            }
            Check(sb, ref pass, ref fail, allClosed, "所有圆点闭合");
            Check(sb, ref pass, ref fail, worstSurf < 0.05,
                string.Format("圆点全部贴在曲面上（最大偏差 {0:0.#####}）", worstSurf));

            // 铺满：圆点边缘到真实面边界的距离应当就是设定的边缘间距（不能多留一条缝）
            double minEdge = double.MaxValue;
            foreach (Curve c in curves)
            {
                Point3d[] pts = Sample(c, 32);
                if (pts == null) continue;
                foreach (Point3d p in pts)
                {
                    double d = MinDistToLoops(side, p);
                    if (d < minEdge) minEdge = d;
                }
            }
            sb.AppendLine(string.Format("  圆点边缘到面边界的最小距离 {0:0.###} mm（设定边缘间距 {1:0.###} mm）",
                minEdge, s.Margin));
            Check(sb, ref pass, ref fail, minEdge <= s.Margin + 0.5,
                string.Format("圆点铺到了边界（实测 {0:0.###}，要求 <= 设定 {1:0.###} + 0.5）", minEdge, s.Margin));
            Check(sb, ref pass, ref fail, worstLen < 0.02,
                string.Format("周长偏差最大 {0:0.##}%（要求 <2%，即不变形）", worstLen * 100));

            // 对照：旧做法（在「面中心的大切平面」上画圆再回投）在同一批位置上的偏差
            double legacy = 0;
            Plane frame = res[0].Plane;
            foreach (Curve c in curves)
            {
                Point3d ctr = CurveCenter(c);
                legacy = Math.Max(legacy, LegacyCircleError(face, frame, ctr, rad));
            }
            sb.AppendLine(string.Format("  【对照】旧做法（面中心大切平面投影）同样位置的周长偏差最大 {0:0.##}%",
                legacy * 100));
        }

        // ---------------------------------------------------------------- 用例2：同心环以指定圆心扩散
        static void Case2_RingsFromPickedCenter(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例2：同心环 / 螺旋 以指定圆心为中心扩散 ---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 80)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer }, 0.001);
            if (bps == null || bps.Length == 0) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var center = new Point3d(30, 20, 0);   // 故意不在面中心(60,40)
            var s = new HalftoneSettings();
            s.MaxDia = 6.0; s.MinDia = 1.0; s.Pitch = 6.0; s.Margin = 0.5;
            s.ArrayMode = 3;                        // 同心环
            s.CenterPoints = new List<Point3d> { center };
            s.UsePickedCenter = true;

            string report;
            List<HalftoneFaceResult> res = Halftone.Generate(brep, s, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));
            var pts = new List<Point3d>();
            foreach (HalftoneFaceResult rr in res) pts.AddRange(ShapeCenters(rr));
            Check(sb, ref pass, ref fail, pts.Count > 0, string.Format("同心环生成 {0} 个图形", pts.Count));
            if (pts.Count == 0) return;

            // 最近的点应该就在指定圆心附近（环的第 0 环是圆心本身）
            double near = double.MaxValue;
            foreach (Point3d p in pts) near = Math.Min(near, p.DistanceTo(center));
            Check(sb, ref pass, ref fail, near < 1e-6,
                string.Format("存在落在指定圆心上的图形（最近距离 {0:0.######}）", near));

            // 所有图形到圆心的距离都应该是间距的整数倍（同心环特征）
            double worstRing = 0;
            foreach (Point3d p in pts)
            {
                double d = p.DistanceTo(center);
                double k = Math.Round(d / s.Pitch);
                worstRing = Math.Max(worstRing, Math.Abs(d - k * s.Pitch));
            }
            Check(sb, ref pass, ref fail, worstRing < 0.02,
                string.Format("各图形落在圆心同心环上（最大偏差 {0:0.####}）", worstRing));

            // 螺旋（黄金角）
            s.ArrayMode = 4;
            res = Halftone.Generate(brep, s, out report);
            var sp2 = new List<Point3d>();
            foreach (HalftoneFaceResult rr in res) sp2.AddRange(ShapeCenters(rr));
            Check(sb, ref pass, ref fail, sp2.Count > 0, string.Format("螺旋生成 {0} 个图形", sp2.Count));
            double near2 = double.MaxValue;
            foreach (Point3d p in sp2) near2 = Math.Min(near2, p.DistanceTo(center));
            Check(sb, ref pass, ref fail, near2 < 0.6,
                string.Format("螺旋从指定圆心开始（最近距离 {0:0.###}）", near2));

            // 抖动网格
            s.ArrayMode = 5;
            res = Halftone.Generate(brep, s, out report);
            var sp3 = new List<Point3d>();
            foreach (HalftoneFaceResult rr in res) sp3.AddRange(ShapeCenters(rr));
            Check(sb, ref pass, ref fail, sp3.Count > 0, string.Format("抖动生成 {0} 个图形", sp3.Count));
        }

        // ---------------------------------------------------------------- 用例3：平面网格基本不变量
        static void Case3_PlanarGrid(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例3：平面网格（带孔）图形不越界、不落孔内 ---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(200, 340), new Interval(0, 140)).ToNurbsCurve();
            var hole = new Circle(new Plane(new Point3d(270, 70, 0), Vector3d.ZAxis), 15).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer, hole }, 0.001);
            if (bps == null || bps.Length == 0) { Check(sb, ref pass, ref fail, false, "创建带孔平面失败"); return; }
            Brep brep = bps[0];
            doc.Objects.AddBrep(brep);

            var s = new HalftoneSettings();
            s.MaxDia = 8.0; s.MinDia = 1.0; s.Pitch = 6.0; s.Margin = 1.0;
            s.ArrayMode = 1; s.Shape = 0;

            string report;
            List<HalftoneFaceResult> res = Halftone.Generate(brep, s, out report);
            sb.AppendLine("生成日志: " + report.Replace("\r\n", " | "));
            var curves = new List<Curve>();
            foreach (HalftoneFaceResult r in res) curves.AddRange(r.Shapes);
            Check(sb, ref pass, ref fail, curves.Count > 0, string.Format("平面生成 {0} 个图形", curves.Count));
            if (curves.Count == 0) return;

            int inHole = 0, outside = 0;
            BrepFace face = brep.Faces[0];
            foreach (Curve c in curves)
            {
                Point3d[] pts = Sample(c, 16);
                foreach (Point3d p in pts)
                {
                    double rr = Math.Sqrt((p.X - 270) * (p.X - 270) + (p.Y - 70) * (p.Y - 70));
                    if (rr < 15) inHole++;
                    double u, v;
                    if (face.ClosestPoint(p, out u, out v) &&
                        face.IsPointOnFace(u, v) == PointFaceRelation.Exterior) outside++;
                }
            }
            Check(sb, ref pass, ref fail, inHole == 0, "没有图形落入孔内");
            Check(sb, ref pass, ref fail, outside == 0, "没有图形越出面边界");
        }

        // ================================================================ 用例4：参考面（选中面 / 全部面）
        // 用户口径：「参数化阵列 打开面板没有选择参考面的选项」。
        // 多重曲面输入：默认全部面；切「选中面」后只生成点选的那一个面。
        static void Case4_FaceScope(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例4：参考面（选中面 / 全部面）---");
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

            var s = new HalftoneSettings();
            s.MmToModel = 1.0;
            s.MaxDia = 6.0; s.MinDia = 1.0; s.Pitch = 5.0; s.Margin = 1.0;
            s.FaceCount = 2; s.PickedFace = 0;

            string report;
            List<HalftoneFaceResult> all = Halftone.Generate(patch, s, out report);
            int withShapes = 0;
            foreach (HalftoneFaceResult r in all) if (r.Shapes.Count > 0) withShapes++;
            sb.AppendLine(string.Format("  全部面：返回 {0} 个结果，其中 {1} 个有图形", all.Count, withShapes));
            Check(sb, ref pass, ref fail, all.Count == 2 && withShapes == 2,
                "全部面：两个面都生成了图形");

            s.OnlyFace = 0;
            List<HalftoneFaceResult> one = Halftone.Generate(patch, s, out report);
            int n0 = one.Count > 0 ? one[0].Shapes.Count : 0;
            bool onLeft = true;
            if (one.Count == 1)
                foreach (Point3d c in ShapeCenters(one[0]))
                    if (c.X > 60.0 + 0.2) onLeft = false;
            sb.AppendLine(string.Format("  选中面（第 1 面）：返回 {0} 个结果，图形 {1} 个", one.Count, n0));
            Check(sb, ref pass, ref fail, one.Count == 1 && one[0].FaceIndex == 0 && n0 > 0,
                string.Format("选中面：只生成第 1 面（返回 {0} 个结果，图形 {1} 个）", one.Count, n0));
            Check(sb, ref pass, ref fail, onLeft, "选中面：图形全部落在选中的那个面上（没有跑到隔壁）");

            s.OnlyFace = 1;
            List<HalftoneFaceResult> two = Halftone.Generate(patch, s, out report);
            int n1 = two.Count > 0 ? two[0].Shapes.Count : 0;
            bool onRight = true;
            if (two.Count == 1)
                foreach (Point3d c in ShapeCenters(two[0]))
                    if (c.X < 60.0 - 0.2) onRight = false;
            sb.AppendLine(string.Format("  选中面（第 2 面）：返回 {0} 个结果，图形 {1} 个", two.Count, n1));
            Check(sb, ref pass, ref fail, two.Count == 1 && two[0].FaceIndex == 1 && n1 > 0,
                string.Format("选中面：只生成第 2 面（返回 {0} 个结果，图形 {1} 个）", two.Count, n1));
            Check(sb, ref pass, ref fail, onRight, "选中面：图形全部落在选中的那个面上（没有跑到隔壁）");
        }

        // ================================================================ 用例5：多重曲面「整体」排版
        // 用户口径：「输入多重曲面之后 生成的纹理不是按一个整体去生成的」。
        // 判据：整个对象共用**一套全局阵列** —— 所有图形中心落在同一个间距格上（相位一致），
        // 而不是每个面各铺一套（各面各铺时两面的相位会差出小半格）。
        static void Case5_GlobalLayout(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例5：多重曲面整体排版（两块 60x100 共面，接缝在 x=60）---");
            Brep pa = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(60, 0, 0),
                                                  new Point3d(60, 100, 0), new Point3d(0, 100, 0), 1e-6);
            Brep pb = Brep.CreateFromCornerPoints(new Point3d(60, 0, 0), new Point3d(120, 0, 0),
                                                  new Point3d(120, 100, 0), new Point3d(60, 100, 0), 1e-6);
            if (pa == null || pb == null) { Check(sb, ref pass, ref fail, false, "创建共面两块失败"); return; }
            Brep[] jn = Brep.JoinBreps(new Brep[] { pa, pb }, 1e-4);
            if (jn == null || jn.Length != 1 || jn[0].Faces.Count != 2)
            {
                Check(sb, ref pass, ref fail, false, "两块共面片没能合成一个多重曲面");
                return;
            }
            Brep patch = jn[0];
            doc.Objects.AddBrep(patch);

            var s = new HalftoneSettings();
            s.MmToModel = 1.0;
            s.MaxDia = 3.0; s.MinDia = 3.0; s.Pitch = 8.0; s.Margin = 1.0;
            s.ArrayMode = 0; s.Shape = 0; s.Falloff = 1.0;
            s.OnlyFace = -1; s.FaceCount = 2; s.UsePickedCenter = false;

            string report;
            List<HalftoneFaceResult> res = Halftone.Generate(patch, s, out report);
            sb.AppendLine("  生成日志: " + report.Replace("\r\n", " | "));
            int nA = res.Count > 0 ? res[0].Shapes.Count : 0;
            int nB = res.Count > 1 ? res[1].Shapes.Count : 0;
            sb.AppendLine(string.Format("  两面图形数：A {0} / B {1}（合计 {2}）", nA, nB, nA + nB));
            Check(sb, ref pass, ref fail, nA > 20 && nB > 20,
                string.Format("两块面都排上了图形（A {0} / B {1}）", nA, nB));
            if (nA == 0 || nB == 0) return;

            // 相位一致性：所有图形中心的 x 减去某个基准后，应当都是 pitch 的整数倍（同一套全局阵列）
            var xs = new List<double>();
            foreach (HalftoneFaceResult r in res)
                foreach (Point3d c in ShapeCenters(r)) xs.Add(c.X);
            xs.Sort();
            double baseX = xs[0];
            double pitch = s.Pitch;
            double worstPhase = 0;
            foreach (double x in xs)
            {
                double m = (x - baseX) % pitch;
                if (m < 0) m += pitch;
                double d = Math.Min(m, pitch - m);       // 离整数格的距离
                if (d > worstPhase) worstPhase = d;
            }
            sb.AppendLine(string.Format("  相位一致性：{0} 个图形中心离「同一套间距格」的最大偏差 {1:0.###} mm（间距 {2}）",
                xs.Count, worstPhase, pitch));
            Check(sb, ref pass, ref fail, worstPhase < 0.15,
                string.Format("整个多重曲面共用一套阵列（相位最大偏差 {0:0.###} < 0.15）", worstPhase));
        }

        // ================================================================ 用例6：用户给的 anli.3dm 实跑
        static void Case6_UserFile(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例6：用户文件 anli.3dm 实跑（多重曲面整体排版）---");
            string path = @"D:\UserData\Desktop\anli.3dm";
            if (!System.IO.File.Exists(path))
            {
                sb.AppendLine("  （文件不存在，跳过）");
                return;
            }
            Rhino.FileIO.File3dm f = null;
            try { f = Rhino.FileIO.File3dm.Read(path); } catch (Exception ex) { sb.AppendLine("  读失败：" + ex.Message); }
            if (f == null) { Check(sb, ref pass, ref fail, false, "anli.3dm 读不出来"); return; }

            int oi = 0, tested = 0;
            foreach (Rhino.FileIO.File3dmObject o in f.Objects)
            {
                Brep b = Brep.TryConvertBrep(o.Geometry);
                oi++;
                if (b == null) continue;
                sb.AppendLine(string.Format("  [{0}] '{1}' 面数 {2} solid={3}", oi, o.Name, b.Faces.Count, b.IsSolid));
                for (int fi = 0; fi < b.Faces.Count; fi++)
                {
                    BrepFace face = b.Faces[fi];
                    Plane pl; bool planar = face.TryGetPlane(out pl, 1e-3) && pl.IsValid;
                    sb.AppendLine(string.Format("      face{0}: planar={1}", fi, planar ? "Y" : "N"));
                }
                for (int ei = 0; ei < b.Edges.Count && ei < 40; ei++)
                {
                    BrepEdge e = b.Edges[ei];
                    int[] af = e.AdjacentFaces();
                    string kind = (af == null || af.Length < 2) ? "自由边" : (af[0] == af[1] ? "接缝" : "共享");
                    sb.AppendLine(string.Format("      edge{0}: {1} faces=[{2}]", ei, kind, af == null ? "-" : string.Join(",", af)));
                }

                var s = new HalftoneSettings();
                s.MmToModel = 1.0;
                s.MaxDia = 3.0; s.MinDia = 1.0; s.Pitch = 5.0; s.Margin = 0.5;
                s.ArrayMode = 0; s.Shape = 0; s.OnlyFace = -1; s.FaceCount = b.Faces.Count;
                s.UsePickedCenter = false;
                string rep2;
                List<HalftoneFaceResult> rr = Halftone.Generate(b, s, out rep2);
                int tot = 0;
                foreach (HalftoneFaceResult r in rr) tot += r.Shapes.Count;
                sb.AppendLine("      整体排版: " + rep2.Replace("\r\n", " | "));
                Check(sb, ref pass, ref fail, tot > 0, string.Format("anli.3dm 上生成了 {0} 个图形", tot));
                tested++;
            }
            Check(sb, ref pass, ref fail, tested > 0, string.Format("anli.3dm 里测了 {0} 个曲面对象", tested));
        }

        // ================================================================ 用例7：选择渐变物件（拾取 + 跟随 + 生效）
        // 用户口径：「参数化阵列插件没有选取渐变的物件按钮，增加上去，并测试选取物件渐变是否生效」。
        static void Case7_GradientPick(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例7：选择渐变物件（取点 / 跟随 / 生效）---");
            Brep box = new Box(new BoundingBox(0, 0, 0, 10, 10, 10)).ToBrep();
            Guid gid = box == null ? Guid.Empty : doc.Objects.AddBrep(box);
            if (gid == Guid.Empty) { Check(sb, ref pass, ref fail, false, "放渐变参考小方块失败"); return; }

            Point3d c1 = ArrayTextureCore.ObjectCenter(doc, gid);
            bool moved = false;
            Rhino.DocObjects.RhinoObject go = doc.Objects.FindId(gid);
            if (go != null)
            {
                moved = go.Geometry.Transform(Transform.Translation(new Vector3d(60, 0, 0)));
                go.CommitChanges();
            }
            Point3d c2 = ArrayTextureCore.ObjectCenter(doc, gid);
            sb.AppendLine(string.Format("  渐变物件中心：({0:0.##},{1:0.##},{2:0.##}) -> ({3:0.##},{4:0.##},{5:0.##})",
                c1.X, c1.Y, c1.Z, c2.X, c2.Y, c2.Z));
            Check(sb, ref pass, ref fail, moved && c1.IsValid && c2.IsValid && Math.Abs((c2.X - c1.X) - 60) < 0.01,
                "渐变物件中心跟着物件走（+60mm）—— 每次现取，不缓存旧坐标");

            // 渐变生效：同一块面，渐变起点在一角 / 另一角，靠它那半边的图形更大
            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(140, 0, 0),
                                                     new Point3d(140, 60, 0), new Point3d(0, 60, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建渐变测试平面失败"); return; }
            var s = new HalftoneSettings();
            s.MmToModel = 1.0;
            s.MaxDia = 6.0; s.MinDia = 0.6; s.Pitch = 6.0; s.Margin = 1.0;
            s.ArrayMode = 0; s.Shape = 0; s.Falloff = 1.0;
            s.OnlyFace = -1; s.FaceCount = 1; s.UsePickedCenter = true;
            s.CenterPoints = new List<Point3d> { new Point3d(20, 30, 0) };
            double nearL, farL; int nL;
            MeanDotSize(plate, s, new Point3d(20, 30, 0), out nL, out nearL, out farL);
            s.CenterPoints = new List<Point3d> { new Point3d(120, 30, 0) };
            double nearR, farR; int nR;
            MeanDotSize(plate, s, new Point3d(120, 30, 0), out nR, out nearR, out farR);
            sb.AppendLine(string.Format("  起点在左(20,30)：近侧平均直径 {0:0.###}，远侧 {1:0.###}（{2} 个图形）", nearL, farL, nL));
            sb.AppendLine(string.Format("  起点在右(120,30)：近侧平均直径 {0:0.###}，远侧 {1:0.###}（{2} 个图形）", nearR, farR, nR));
            Check(sb, ref pass, ref fail, nL > 50 && nR > 50 && nearL > farL * 2.0 && nearR > farR * 2.0,
                "渐变生效：靠近渐变物件那侧图形明显更大（左右两次都成立）");
        }

        /// <summary>算「靠近渐变起点的一半」与「外侧一半」的平均图形直径</summary>
        static void MeanDotSize(Brep brep, HalftoneSettings s, Point3d center, out int n, out double meanNear, out double meanFar)
        {
            n = 0; meanNear = 0; meanFar = 0;
            string rep;
            List<HalftoneFaceResult> res = Halftone.Generate(brep, s, out rep);
            var items = new List<double[]>();
            foreach (HalftoneFaceResult r in res)
                foreach (Curve c in r.Shapes)
                {
                    BoundingBox bb = c.GetBoundingBox(false);
                    if (!bb.IsValid) continue;
                    double dia = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
                    items.Add(new double[] { dia, bb.Center.DistanceTo(center) });
                }
            n = items.Count;
            if (items.Count < 4) return;
            items.Sort((a, b) => a[1].CompareTo(b[1]));
            int half = items.Count / 2;
            double sn = 0, sf = 0;
            for (int i = 0; i < half; i++) sn += items[i][0];
            for (int i = half; i < items.Count; i++) sf += items[i][0];
            meanNear = half > 0 ? sn / half : 0;
            meanFar = items.Count - half > 0 ? sf / (items.Count - half) : 0;
        }

        /// <summary>点到面所有边界的最近距离</summary>
        static double MinDistToLoops(Brep brep, Point3d p)
        {
            double best = double.MaxValue;
            for (int fi = 0; fi < brep.Faces.Count; fi++)
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
            return best;
        }

        static Point3d CurveCenter(Curve c)
        {
            Point3d[] pts = Sample(c, 12);
            if (pts == null || pts.Length == 0) return Point3d.Unset;
            var acc = new Point3d(0, 0, 0);
            foreach (Point3d p in pts) acc += p;
            return new Point3d(acc.X / pts.Length, acc.Y / pts.Length, acc.Z / pts.Length);
        }

        /// <summary>旧做法：在面中心的大切平面里画 40 边形圆，再逐点 ClosestPoint 回投到曲面，测其周长偏差</summary>
        static double LegacyCircleError(BrepFace face, Plane frame, Point3d centerOnSurface, double r)
        {
            if (!centerOnSurface.IsValid) return 0;
            Vector3d v = centerOnSurface - frame.Origin;
            var c2 = new Point2d(v.X * frame.XAxis.X + v.Y * frame.XAxis.Y + v.Z * frame.XAxis.Z,
                                 v.X * frame.YAxis.X + v.Y * frame.YAxis.Y + v.Z * frame.YAxis.Z);
            var pts = new List<Point3d>(41);
            for (int k = 0; k < 40; k++)
            {
                double a = k * 2 * Math.PI / 40;
                Point3d q = frame.PointAt(c2.X + r * Math.Cos(a), c2.Y + r * Math.Sin(a));
                double u, vv;
                if (!face.ClosestPoint(q, out u, out vv)) continue;
                pts.Add(face.PointAt(u, vv));
            }
            if (pts.Count < 3) return 0;
            pts.Add(pts[0]);
            var plc = new PolylineCurve(pts);
            double len = plc.GetLength();
            double expect = 2 * Math.PI * r;
            return Math.Abs(len - expect) / expect;
        }

        /// <summary>取图形中心（平面图形取平面中心，曲面图形取点平均）</summary>
        static IEnumerable<Point3d> ShapeCenters(HalftoneFaceResult r)
        {
            var list = new List<Point3d>();
            foreach (Curve c in r.Shapes)
            {
                Point3d[] pts = Sample(c, 12);
                if (pts == null || pts.Length == 0) continue;
                var acc = new Point3d(0, 0, 0);
                foreach (Point3d p in pts) acc += p;
                list.Add(new Point3d(acc.X / pts.Length, acc.Y / pts.Length, acc.Z / pts.Length));
            }
            return list;
        }
    }
}
