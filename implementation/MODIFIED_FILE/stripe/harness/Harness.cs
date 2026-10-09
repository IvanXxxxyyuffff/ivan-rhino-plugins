using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Geometry;
using StripeOnSurface;

namespace StripeHarness
{
    /// <summary>
    /// 独立几何校验：直接调用插件的几何核心（src\StripePattern.cs / src\Engraver.cs），
    /// 用 RhinoCommon 真实几何验证 数量/闭合/边距/宽度/间距/曲面贴合/开槽 等不变量。
    /// 不依赖 Rhino 界面。
    /// </summary>
    class Harness
    {
        static int _pass = 0, _fail = 0;
        static StringBuilder _sb = new StringBuilder();

        static void Check(bool ok, string what)
        {
            if (ok) { _pass++; _sb.AppendLine("[PASS] " + what); }
            else { _fail++; _sb.AppendLine("[FAIL] " + what); }
        }

        static double Dot(Point3d p, Vector3d v) { return p.X * v.X + p.Y * v.Y + p.Z * v.Z; }
        static double PlaneDist(Plane pl, Point3d p)
        {
            Vector3d v = p - pl.Origin;
            return Math.Abs(v.X * pl.ZAxis.X + v.Y * pl.ZAxis.Y + v.Z * pl.ZAxis.Z);
        }

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

        static int Main(string[] args)
        {
            // 让 RhinoCommon 找到 Rhino 的原生库
            try
            {
                string rhinoSys = @"D:\Rhino 8\System";
                if (System.IO.Directory.Exists(rhinoSys))
                {
                    Environment.SetEnvironmentVariable("PATH",
                        rhinoSys + ";" + Environment.GetEnvironmentVariable("PATH"));
                }
            }
            catch { }

            _sb.AppendLine("=== StripeOnSurface 独立几何校验 ===");
            _sb.AppendLine("RhinoCommon: " + typeof(Brep).Assembly.GetName().Version);

            try
            {
                using (new Rhino.Runtime.InProcess.RhinoCore(new string[] { "/nosplash" },
                           Rhino.Runtime.InProcess.WindowStyle.NoWindow))
                {
                    _sb.AppendLine("RhinoCore 已启动（无界面）: " + Rhino.RhinoApp.Version);

                    try { Case1_PlaneStadium(); } catch (Exception ex) { Check(false, "用例1 异常: " + ex.Message); }
                    try { Case2_PlaneWithHole(); } catch (Exception ex) { Check(false, "用例2 异常: " + ex.Message); }
                    try { Case3_Cylinder(); } catch (Exception ex) { Check(false, "用例3 异常: " + ex.Message); }
                    try { Case4_Engrave(); } catch (Exception ex) { Check(false, "用例4 异常: " + ex.Message); }
                    try { Case5_Settings(); } catch (Exception ex) { Check(false, "用例5 异常: " + ex.Message); }
                    try { Case6_AngleAndMm(); } catch (Exception ex) { Check(false, "用例6 异常: " + ex.Message); }
                    try { Case7_Conform(); } catch (Exception ex) { Check(false, "用例7 异常: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                _sb.AppendLine("[FAIL] RhinoCore 启动失败: " + ex);
                _fail++;
            }

            _sb.AppendLine();
            _sb.AppendLine("RESULT: " + _pass + " pass / " + _fail + " fail -> " + (_fail == 0 ? "PASS" : "FAIL"));
            string txt = _sb.ToString();
            Console.WriteLine(txt);
            try { System.IO.File.WriteAllText(@"C:\zcode_build\stripe\_harness.txt", txt, Encoding.UTF8); } catch { }
            return _fail == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- 用例1
        static void Case1_PlaneStadium()
        {
            _sb.AppendLine();
            _sb.AppendLine("--- 用例1：平面胶囊形（120x40，直线段80 + R20 端头），45° 条纹 ---");
            List<Point2d> ring = StripePattern.StadiumPoints(new Point2d(0, 0), new Point2d(80, 0), 40, true, 32);
            var cps = new List<Point3d>();
            foreach (Point2d p in ring) cps.Add(new Point3d(p.X, p.Y, 0));
            cps.Add(cps[0]);
            var outline = new PolylineCurve(cps);

            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outline }, 0.001);
            Check(bps != null && bps.Length > 0, "平面创建成功");
            if (bps == null || bps.Length == 0) return;
            Brep brep = bps[0];

            var s = new StripeSettings { Width = 1.5, Spacing = 3.0, AngleDeg = 45, Margin = 2.0, RoundedEnds = true };
            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, s, out report);
            _sb.AppendLine("  生成日志: " + report.Replace("\r\n", " | "));

            int total = 0;
            foreach (StripeFaceResult r in res) total += r.Stripes.Count;
            Check(total > 0, "生成了条纹（" + total + " 条）");
            if (total == 0) return;

            var stripes = new List<Curve>();
            foreach (StripeFaceResult r in res) stripes.AddRange(r.Stripes);

            bool allClosed = true, onPlane = true, onFace = true;
            double minEdge = double.MaxValue;
            Plane pl = res[0].Plane;
            BrepFace face = brep.Faces[res[0].FaceIndex];
            foreach (Curve c in stripes)
            {
                if (!c.IsClosed) allClosed = false;
                Point3d[] sp = SamplePoints(c, 64);
                if (sp == null) continue;
                foreach (Point3d p in sp)
                {
                    if (PlaneDist(pl, p) > 0.01) onPlane = false;
                    double u, v;
                    if (!face.ClosestPoint(p, out u, out v)) { onFace = false; continue; }
                    if (face.PointAt(u, v).DistanceTo(p) > 0.01) onFace = false;
                    if (face.IsPointOnFace(u, v) == PointFaceRelation.Exterior) onFace = false;
                    double d = MinDistToLoops(brep, p);
                    if (d < minEdge) minEdge = d;
                }
            }
            Check(allClosed, "所有条纹曲线闭合");
            Check(onPlane, "条纹落在面所在平面上");
            Check(onFace, "条纹全部位于面内（未越界）");
            Check(minEdge >= s.Margin - 1e-3,
                string.Format("实测最小边距 {0:0.####} >= 设定 {1:0.####}", minEdge, s.Margin));

            // 宽度/间距：投影到条纹排布法向
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

            double maxW = 0, maxGap = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                maxW = Math.Max(maxW, Math.Abs((spans[i][1] - spans[i][0]) - s.Width));
                if (i > 0) maxGap = Math.Max(maxGap, Math.Abs((spans[i][0] - spans[i - 1][1]) - s.Spacing));
            }
            Check(maxW < 0.01, string.Format("宽度偏差 {0:0.#####}（设定 {1}）", maxW, s.Width));
            Check(maxGap < 0.01, string.Format("净间距偏差 {0:0.#####}（设定 {1}）", maxGap, s.Spacing));

            // 面积/周长反解宽度（验证圆角端）
            Curve probe = null;
            double bl = -1;
            foreach (Curve c in stripes) { double l = c.GetLength(); if (l > bl) { bl = l; probe = c; } }
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
                    Check(Math.Abs(wm - s.Width) < 0.01,
                        string.Format("面积/周长反解宽度 {0:0.#####}（设定 {1}）", wm, s.Width));
                }
                else Check(false, "面积/周长反解不可用");
            }
        }

        // ---------------------------------------------------------------- 用例2
        static void Case2_PlaneWithHole()
        {
            _sb.AppendLine();
            _sb.AppendLine("--- 用例2：带孔平面 140x140 + 中心 R15 孔，30° 条纹 ---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(200, 340), new Interval(0, 140)).ToNurbsCurve();
            var hole = new Circle(new Plane(new Point3d(270, 70, 0), Vector3d.ZAxis), 15).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer, hole }, 0.001);
            Check(bps != null && bps.Length > 0, "带孔平面创建成功");
            if (bps == null || bps.Length == 0) return;
            Brep brep = bps[0];
            Check(brep.Faces[0].Loops.Count >= 2, "面含 " + brep.Faces[0].Loops.Count + " 个环（含内孔）");

            var s = new StripeSettings { Width = 1.0, Spacing = 2.0, AngleDeg = 30, Margin = 3.0 };
            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, s, out report);
            _sb.AppendLine("  生成日志: " + report.Replace("\r\n", " | "));

            int total = 0;
            double minEdge = double.MaxValue;
            int inHole = 0;
            foreach (StripeFaceResult r in res)
            {
                total += r.Stripes.Count;
                foreach (Curve c in r.Stripes)
                {
                    Point3d[] sp = SamplePoints(c, 64);
                    foreach (Point3d p in sp)
                    {
                        double d = MinDistToLoops(brep, p);
                        if (d < minEdge) minEdge = d;
                        if (new Vector2d(p.X - 270, p.Y - 70).Length < 15) inHole++;
                    }
                }
            }
            Check(total > 0, "生成了条纹（" + total + " 条）");
            Check(inHole == 0, "没有条纹落入圆孔内");
            Check(minEdge >= s.Margin - 1e-3,
                string.Format("含孔面最小边距 {0:0.####} >= 设定 {1:0.####}", minEdge, s.Margin));
        }

        // ---------------------------------------------------------------- 用例3
        static void Case3_Cylinder()
        {
            _sb.AppendLine();
            _sb.AppendLine("--- 用例3：圆柱 R30 H80（侧面走 UV 模式），60° 条纹 ---");
            var cyl = new Cylinder(new Circle(new Plane(new Point3d(500, 0, 0), Vector3d.ZAxis), 30), 80);
            Brep brep = cyl.ToBrep(true, true);
            Check(brep != null, "圆柱创建成功");
            if (brep == null) return;

            var s = new StripeSettings { Width = 2.0, Spacing = 3.0, AngleDeg = 60, Margin = 2.0 };
            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, s, out report);
            _sb.AppendLine("  生成日志: " + report.Replace("\r\n", " | "));

            int total = 0, uvFaces = 0;
            foreach (StripeFaceResult r in res)
            {
                total += r.Stripes.Count;
                if (!r.Planar && r.Stripes.Count > 0) uvFaces++;
            }
            Check(total > 0, "圆柱共生成 " + total + " 条条纹（" + uvFaces + " 个面走 UV 模式）");
            Check(uvFaces > 0, "侧面确实进入 UV 模式");

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
            Check(worst < 0.05, string.Format("曲面贴合最大偏差 {0:0.#####}（要求 < 0.05）", worst));
        }

        // ---------------------------------------------------------------- 用例4
        static void Case4_Engrave()
        {
            _sb.AppendLine();
            _sb.AppendLine("--- 用例4：实体开槽（胶囊实体 120x40x20，深 0.8）---");
            List<Point2d> ring = StripePattern.StadiumPoints(new Point2d(0, 0), new Point2d(80, 0), 40, true, 32);
            var cps = new List<Point3d>();
            foreach (Point2d p in ring) cps.Add(new Point3d(p.X + 800, p.Y, 0));
            cps.Add(cps[0]);
            var outline = new PolylineCurve(cps);
            Surface srf = Surface.CreateExtrusion(outline, new Vector3d(0, 0, 20));
            Check(srf != null, "拉伸面创建成功");
            if (srf == null) return;
            Brep solid = Brep.CreateFromSurface(srf);
            solid = solid.CapPlanarHoles(0.001);
            Check(solid != null && solid.IsSolid, "封闭实体创建成功");
            if (solid == null || !solid.IsSolid) return;
            double vol0 = Volume(solid);
            Check(vol0 > 0, string.Format("原始体积 {0:0.###}", vol0));

            var s = new StripeSettings { Width = 2.0, Spacing = 3.0, AngleDeg = 45, Margin = 3.0, Depth = 0.8 };
            string report;
            List<StripeFaceResult> res = StripePattern.Generate(solid, s, out report);
            _sb.AppendLine("  生成日志: " + report.Replace("\r\n", " | "));
            int total = 0;
            foreach (StripeFaceResult r in res) total += r.Stripes.Count;
            Check(total > 0, "实体面上生成 " + total + " 条条纹");

            string err;
            Brep engraved = Engraver.Engrave(solid, res, s.Depth, out err);
            Check(engraved != null, "开槽完成" + (engraved == null ? "（" + err + "）" : ""));
            if (engraved == null) return;
            double vol1 = Volume(engraved);
            Check(engraved.IsSolid, "开槽后仍为封闭实体");
            Check(vol1 < vol0 - 1e-6, string.Format("体积减少 {0:0.####}（{1:0.###} -> {2:0.###}）", vol0 - vol1, vol0, vol1));

            double area = 0;
            foreach (StripeFaceResult r in res)
                foreach (Curve c in r.Stripes)
                {
                    var amp = AreaMassProperties.Compute(c);
                    if (amp != null) area += amp.Area;
                }
            double expect = area * s.Depth;
            double errPct = Math.Abs((vol0 - vol1) - expect) / Math.Max(expect, 1e-9) * 100;
            Check(errPct < 5, string.Format("切除体积 {0:0.####} vs 理论 {1:0.####}（偏差 {2:0.##}%）", vol0 - vol1, expect, errPct));
        }

        // ---------------------------------------------------------------- 用例5
        static void Case5_Settings()
        {
            _sb.AppendLine();
            _sb.AppendLine("--- 用例5：参数敏感性（宽度/间距/角度/边距 变化是否生效）---");
            List<Point2d> ring = StripePattern.StadiumPoints(new Point2d(0, 0), new Point2d(120, 0), 60, true, 32);
            var cps = new List<Point3d>();
            foreach (Point2d p in ring) cps.Add(new Point3d(p.X, p.Y, 0));
            cps.Add(cps[0]);
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { new PolylineCurve(cps) }, 0.001);
            if (bps == null || bps.Length == 0) { Check(false, "测试面创建失败"); return; }
            Brep brep = bps[0];

            // 大间距 -> 条纹数应减少
            var s1 = new StripeSettings { Width = 2, Spacing = 4, AngleDeg = 45, Margin = 2 };
            var s2 = new StripeSettings { Width = 2, Spacing = 12, AngleDeg = 45, Margin = 2 };
            string rep;
            int n1 = Count(StripePattern.Generate(brep, s1, out rep));
            int n2 = Count(StripePattern.Generate(brep, s2, out rep));
            Check(n2 < n1, string.Format("间距 4->12 使条纹数 {0} -> {1}", n1, n2));

            // 宽度变大 -> 条数持平或减少
            var s3 = new StripeSettings { Width = 8, Spacing = 4, AngleDeg = 45, Margin = 2 };
            int n3 = Count(StripePattern.Generate(brep, s3, out rep));
            Check(n3 <= n1, string.Format("宽度 2->8 使条纹数 {0} -> {1}", n1, n3));

            // 大边距 -> 条纹变短（总面积下降）
            var s4 = new StripeSettings { Width = 2, Spacing = 4, AngleDeg = 45, Margin = 2 };
            var s5 = new StripeSettings { Width = 2, Spacing = 4, AngleDeg = 45, Margin = 12 };
            double a4 = Area(StripePattern.Generate(brep, s4, out rep));
            double a5 = Area(StripePattern.Generate(brep, s5, out rep));
            Check(a5 < a4 * 0.9, string.Format("边距 2->12 使条纹总面积 {0:0.#} -> {1:0.#}", a4, a5));

            // 角度变化 -> 条纹长度分布变化（总长不同）
            var s6 = new StripeSettings { Width = 2, Spacing = 4, AngleDeg = 0, Margin = 2 };
            var s7 = new StripeSettings { Width = 2, Spacing = 4, AngleDeg = 90, Margin = 2 };
            double l6 = TotalLength(StripePattern.Generate(brep, s6, out rep));
            double l7 = TotalLength(StripePattern.Generate(brep, s7, out rep));
            Check(Math.Abs(l6 - l7) > 1.0, string.Format("0° 与 90° 条纹总长不同（{0:0.#} vs {1:0.#}）", l6, l7));

            // 圆角端 vs 方端：面积不同
            var s8 = new StripeSettings { Width = 2, Spacing = 4, AngleDeg = 45, Margin = 2, RoundedEnds = false };
            double a8 = Area(StripePattern.Generate(brep, s8, out rep));
            Check(a8 < a4, string.Format("方端面积 {0:0.#} < 圆角端面积 {1:0.#}", a8, a4));
        }

        // 用例6：真实角度（0/45/90°）与真实 mm 尺寸
        static void Case6_AngleAndMm()
        {
            _sb.AppendLine();
            _sb.AppendLine("--- 用例6：真实角度（0/45/90 度）与真实 mm 尺寸 ---");
            var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 200), new Interval(0, 100)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { rect }, 0.001);
            if (bps == null || bps.Length == 0) { Check(false, "测试面创建失败"); return; }
            Brep brep = bps[0];

            var s = new StripeSettings();
            s.MmToModel = 1.0;
            s.Width = 2.0; s.Spacing = 4.0; s.Margin = 3.0;

            foreach (double a in new double[] { 0, 45, 90 })
            {
                s.AngleDeg = a;
                string rep;
                List<StripeFaceResult> res = StripePattern.Generate(brep, s, out rep);
                if (res.Count == 0 || res[0].Stripes.Count == 0) { Check(false, a + " 度未生成条纹"); continue; }
                Vector3d n = res[0].DiagNormal;
                double th = a * Math.PI / 180.0;
                double d = Math.Abs(n.X * (-Math.Sin(th)) + n.Y * Math.Cos(th));
                Check(d > 0.999, string.Format("{0:0} 度为真实角度（排布法向点积 {1:0.#####}）", a, d));
            }

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
            Check(spans.Count > 1, "生成多条条纹用于尺寸校验（" + spans.Count + " 条）");
            Check(maxW < 0.01, string.Format("宽度真实 mm 偏差 {0:0.#####}（设定 {1} mm）", maxW, s.Width));
            Check(maxG < 0.01, string.Format("净间距真实 mm 偏差 {0:0.#####}（设定 {1} mm）", maxG, s.Spacing));
        }

        static int Count(List<StripeFaceResult> res)
        {
            int n = 0;
            foreach (StripeFaceResult r in res) n += r.Stripes.Count;
            return n;
        }

        static double Area(List<StripeFaceResult> res)
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

        static double TotalLength(List<StripeFaceResult> res)
        {
            double a = 0;
            foreach (StripeFaceResult r in res)
                foreach (Curve c in r.Stripes)
                {
                    var cl = CurveLength(c);
                    a += cl;
                }
            return a;
        }

        static double CurveLength(Curve c)
        {
            // 长度 = 周长 - 两端帽（用面积反解宽度代替）：直接采样折线长度
            Polyline pl;
            if (c.TryGetPolyline(out pl)) return pl.Length;
            return c.GetLength();
        }

        // ---------------------------------------------------------------- 用例7：贴合边界
        static void Case7_Conform()
        {
            _sb.AppendLine();
            _sb.AppendLine("--- 用例7：完全贴合边界（矩形 120x40，边缘距离 0 / 3）---");
            var outer = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 40)).ToNurbsCurve();
            Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { outer }, 0.001);
            Check(bps != null && bps.Length > 0, "矩形平面创建成功");
            if (bps == null || bps.Length == 0) return;
            Brep brep = bps[0];

            var s = new StripeSettings
            {
                Width = 1.5, Spacing = 3.0, AngleDeg = 45, Margin = 0.0, ConformToBoundary = true
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string report;
            List<StripeFaceResult> res = StripePattern.Generate(brep, s, out report);
            sw.Stop();
            _sb.AppendLine("  生成日志: " + report.Replace("\r\n", " | "));
            _sb.AppendLine("  耗时 " + sw.ElapsedMilliseconds + " ms");

            var stripes = new List<Curve>();
            foreach (StripeFaceResult r in res) stripes.AddRange(r.Stripes);
            Check(stripes.Count > 0, "生成了贴合条纹（" + stripes.Count + " 条）");
            if (stripes.Count == 0) return;

            bool allClosed = true, onFace = true;
            double minEdge = double.MaxValue, area = 0;
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
                    double u, v;
                    if (!face.ClosestPoint(p, out u, out v)) { onFace = false; continue; }
                    if (face.PointAt(u, v).DistanceTo(p) > 0.01) onFace = false;
                    if (face.IsPointOnFace(u, v) == PointFaceRelation.Exterior) onFace = false;
                    double d = MinDistToLoops(brep, p);
                    if (d < minEdge) minEdge = d;
                }
            }
            Check(allClosed, "所有贴合条纹闭合");
            Check(onFace, "贴合条纹全部位于面内（未越界）");
            Check(minEdge < 0.02, string.Format("边缘距离=0 时贴到边界（实测最小边距 {0:0.####}）", minEdge));

            double expect = 120.0 * 40.0 * s.Width / (s.Width + s.Spacing);
            double dev = Math.Abs(area - expect) / expect;
            Check(dev < 0.06, string.Format("填充面积 {0:0.##} vs 理论 {1:0.##}（偏差 {2:0.##}%）",
                area, expect, dev * 100));

            // 宽度/间距
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
            int wOk = 0, wAll = 0;
            double maxGap = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                wAll++;
                if (Math.Abs((spans[i][1] - spans[i][0]) - s.Width) < 0.02) wOk++;
                if (i > 0)
                {
                    double g = spans[i][0] - spans[i - 1][1];
                    if (g > 0) maxGap = Math.Max(maxGap, Math.Abs(g - s.Spacing));
                }
            }
            Check(wOk >= wAll - 2, string.Format("{0}/{1} 条宽度等于设定 {2}", wOk, wAll, s.Width));
            Check(maxGap < 0.02, string.Format("净间距偏差 {0:0.#####}（设定 {1}）", maxGap, s.Spacing));

            // 边缘距离 = 3
            s.Margin = 3.0;
            res = StripePattern.Generate(brep, s, out report);
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
                        double d = MinDistToLoops(brep, p);
                        if (d < minEdge3) minEdge3 = d;
                    }
                }
            }
            Check(n3 > 0, "边缘距离=3 时生成 " + n3 + " 条");
            Check(minEdge3 >= 3.0 - 0.05, string.Format("边缘距离=3 实测最小边距 {0:0.####} >= 2.95", minEdge3));

            // 带孔面：条纹不应盖住孔
            var outer2 = new Rectangle3d(Plane.WorldXY, new Interval(200, 340), new Interval(0, 140)).ToNurbsCurve();
            var hole = new Circle(new Plane(new Point3d(270, 70, 0), Vector3d.ZAxis), 15).ToNurbsCurve();
            Brep[] bps2 = Brep.CreatePlanarBreps(new Curve[] { outer2, hole }, 0.001);
            if (bps2 != null && bps2.Length > 0)
            {
                Brep holeBrep = bps2[0];
                var s2 = new StripeSettings
                {
                    Width = 1.0, Spacing = 2.0, AngleDeg = 30, Margin = 1.0, ConformToBoundary = true
                };
                List<StripeFaceResult> res2 = StripePattern.Generate(holeBrep, s2, out report);
                int inHole = 0, cnt2 = 0;
                double minEdgeH = double.MaxValue;
                foreach (StripeFaceResult r in res2)
                {
                    cnt2 += r.Stripes.Count;
                    foreach (Curve c in r.Stripes)
                    {
                        Point3d[] sp = SamplePoints(c, 48);
                        if (sp == null) continue;
                        foreach (Point3d p in sp)
                        {
                            double rr = Math.Sqrt((p.X - 270) * (p.X - 270) + (p.Y - 70) * (p.Y - 70));
                            if (rr < 15 - 1e-6) inHole++;
                            double d = MinDistToLoops(holeBrep, p);
                            if (d < minEdgeH) minEdgeH = d;
                        }
                    }
                }
                Check(cnt2 > 0, "带孔面贴合模式生成 " + cnt2 + " 条");
                Check(inHole == 0, "贴合条纹未落入圆孔内（越界点 " + inHole + "）");
                Check(minEdgeH >= s2.Margin - 0.05,
                    string.Format("带孔面实测最小边距 {0:0.####} >= {1}", minEdgeH, s2.Margin));
            }

            // 圆柱侧面（曲面贴合）
            var cyl = new Cylinder(new Circle(new Plane(new Point3d(500, 0, 0), Vector3d.ZAxis), 30), 80);
            Brep cylBrep = cyl.ToBrep(true, true);
            if (cylBrep != null)
            {
                var cs = new StripeSettings
                {
                    MmToModel = 1.0, Width = 2.0, Spacing = 3.0, AngleDeg = 60, Margin = 1.0,
                    ConformToBoundary = true
                };
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
                Check(cn > 0, string.Format("圆柱贴合生成 {0} 条（{1} ms）", cn, sw2.ElapsedMilliseconds));
                Check(worst < 0.05, string.Format("圆柱贴合最大偏差 {0:0.#####}", worst));
            }
        }

        static double MinDistToLoops(Brep brep, Point3d p)
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
            }
            return best;
        }
    }
}
