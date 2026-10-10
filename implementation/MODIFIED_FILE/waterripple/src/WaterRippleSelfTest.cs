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

namespace WaterRipplePattern
{
    /// <summary>
    /// 无人值守自检。用法：命令 WaterRippleSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放
    /// run-waterripple-selftest.flag 后启动 Rhino（跑完自动写报告并退出）。
    /// </summary>
    public class WaterRippleSelfTestCommand : Command
    {
        public override string EnglishName { get { return "WaterRippleSelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\WaterRippleSelfTest.txt");
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
            sb.AppendLine("=== 水波纹 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                Case1_Pure(sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case2_PlanarSurface(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case3_PolySurfaceSeam(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case4_CurveBoundary(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case5_WaveModes(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case6_Fade(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case7_ZeroAndSeed(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case8_SubDAndDocument(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case9_Reject(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case10_Panel(sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case11_PanelWiring(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case12_LockBoundary(doc, sb, ref pass, ref fail);
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

        /// <summary>用例1：波形场纯函数（确定性 / 幅度上限 / 三种波形的口径 / 波长周期性 / 波峰塑形）</summary>
        static void Case1_Pure(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例1：波形场（纯函数） ---");
            var s = new WaterRippleSettings { Wavelength = 10.0, WaveHeight = 2.0, WaveCount = 5, Seed = 3, Crest = 0.0 };
            double k = 2.0 * Math.PI / s.Wavelength;

            // 确定性
            double a1 = WaterRippleCore.WaveField(3.0, 7.0, s, k, 0, 0);
            double a2 = WaterRippleCore.WaveField(3.0, 7.0, s, k, 0, 0);
            Check(sb, ref pass, ref fail, Math.Abs(a1 - a2) < 1e-15, "同一位置同一参数结果完全一致（确定性）");

            // 幅度上限（三种模式 × 波峰 0 / 1）
            double worst = 0;
            for (int mode = 0; mode < 3; mode++)
                for (int crest = 0; crest <= 1; crest++)
                {
                    var t = s.Clone(); t.WaveMode = mode; t.Crest = crest; t.WaveCount = 7;
                    for (int i = 0; i < 400; i++)
                    {
                        double u = (i % 20) * 1.37, v = (i / 20) * 2.11;
                        double h = WaterRippleCore.WaveField(u, v, t, k, 5, 5);
                        worst = Math.Max(worst, Math.Abs(h));
                    }
                }
            Check(sb, ref pass, ref fail, worst <= 1.0 + 1e-9,
                string.Format(CultureInfo.InvariantCulture, "波形场恒在 ±1 之内（实测最大 {0:0.####}）", worst));

            // 条带：等相位线垂直于主方向 → 同一条等相位线上的点高度相同（方向散布 = 0 时是纯直纹）
            {
                var t = s.Clone(); t.WaveMode = 1; t.WaveCount = 1; t.Direction = 0.0; t.Spread = 0.0;
                bool same = true;
                for (int i = 0; i < 20; i++)
                {
                    double u = i * 0.7;
                    double h1 = WaterRippleCore.WaveField(u, -5.0, t, k, 0, 0);
                    double h2 = WaterRippleCore.WaveField(u, 9.0, t, k, 0, 0);
                    if (Math.Abs(h1 - h2) > 1e-12) same = false;
                }
                Check(sb, ref pass, ref fail, same, "定向条带（散布 0）：方向 0° 时高度只随 u 变（同 u 不同 v 完全相同）");
                double h0 = WaterRippleCore.WaveField(0.0, 0.0, t, k, 0, 0);
                double hw = WaterRippleCore.WaveField(s.Wavelength, 0.0, t, k, 0, 0);
                Check(sb, ref pass, ref fail, Math.Abs(h0 - hw) < 1e-9, "定向条带：相隔一个波长的两点高度相同（周期 = 波长）");
                var t90 = t.Clone(); t90.Direction = 90.0;
                double hu = WaterRippleCore.WaveField(4.0, 0.0, t90, k, 0, 0);
                double hv = WaterRippleCore.WaveField(4.0, 6.0, t90, k, 0, 0);
                Check(sb, ref pass, ref fail, Math.Abs(hu - hv) > 1e-6, "方向改 90°：高度改随 v 变（方向参数真的生效）");
                var tsp = t.Clone(); tsp.Spread = 40.0;      // 方向散布在条带下 = 条带摆动
                double sp1 = WaterRippleCore.WaveField(4.0, -5.0, tsp, k, 0, 0);
                double sp2 = WaterRippleCore.WaveField(4.0, 5.0, tsp, k, 0, 0);
                Check(sb, ref pass, ref fail, Math.Abs(sp1 - sp2) > 1e-6, "定向条带：方向散布 > 0 时条带摆动（同 u 不同 v 不再相同）");
            }

            // 同心：**正圆环**（用户实测反馈：以前半径按 1.45 拉成椭圆了）+ 主方向 = 涟漪源偏移方向
            {
                var t = s.Clone(); t.WaveMode = 2; t.Spread = 0.0; t.WaveCount = 1; t.Direction = 0.0;
                double worstCirc = 0;
                double h0 = WaterRippleCore.WaveField(7.3, 0.0, t, k, 0, 0);
                for (int i = 0; i < 24; i++)
                {
                    double a = i * Math.PI / 12.0;
                    double hh = WaterRippleCore.WaveField(7.3 * Math.Cos(a), 7.3 * Math.Sin(a), t, k, 0, 0);
                    worstCirc = Math.Max(worstCirc, Math.Abs(hh - h0));
                }
                Check(sb, ref pass, ref fail, worstCirc < 1e-12,
                    string.Format(CultureInfo.InvariantCulture, "同心涟漪（主方向 0、散布 0）：与源等距的点高度完全相同 → 环是正圆（最差差 {0:0.######}）", worstCirc));

                var t0 = t.Clone(); t0.Direction = 0.0;
                var t90 = t.Clone(); t90.Direction = 90.0;
                double c0a = WaterRippleCore.WaveField(0.0, 6.0, t0, k, 0, 0);
                double c0b = WaterRippleCore.WaveField(0.0, -6.0, t0, k, 0, 0);
                Check(sb, ref pass, ref fail, Math.Abs(c0a - c0b) < 1e-12, "同心涟漪：主方向 0° → 源就在中心（±方向完全对称）");
                double c9a = WaterRippleCore.WaveField(0.0, 6.0, t90, k, 0, 0);
                double c9b = WaterRippleCore.WaveField(0.0, -6.0, t90, k, 0, 0);
                Check(sb, ref pass, ref fail, Math.Abs(c9a - c9b) > 1e-6, "同心涟漪：主方向改 90° → 源沿该方向偏移（±方向不再对称）");
                var tsp = t.Clone(); tsp.Spread = 60.0;
                double c1 = WaterRippleCore.WaveField(9.0, 2.0, tsp, k, 0, 0);
                double c2 = WaterRippleCore.WaveField(9.0, 2.0, t, k, 0, 0);
                Check(sb, ref pass, ref fail, Math.Abs(c1 - c2) > 1e-6, "同心涟漪：方向散布 > 0 → 环起波浪（不再规则）");
            }

            // 有机：种子 / 波数生效
            {
                var t = s.Clone(); t.WaveMode = 0; t.WaveCount = 6;
                var t2 = t.Clone(); t2.Seed = t.Seed + 1;
                double d = 0;
                for (int i = 0; i < 50; i++)
                {
                    double u = i * 0.53, v = i * 0.29;
                    d += Math.Abs(WaterRippleCore.WaveField(u, v, t, k, 0, 0) - WaterRippleCore.WaveField(u, v, t2, k, 0, 0));
                }
                Check(sb, ref pass, ref fail, d > 1e-3, string.Format(CultureInfo.InvariantCulture, "有机水波：换种子结果不同（累计差 {0:0.###}）", d));
                var t3 = t.Clone(); t3.WaveCount = 1;
                double d2 = 0;
                for (int i = 0; i < 50; i++)
                {
                    double u = i * 0.53, v = i * 0.29;
                    d2 += Math.Abs(WaterRippleCore.WaveField(u, v, t, k, 0, 0) - WaterRippleCore.WaveField(u, v, t3, k, 0, 0));
                }
                Check(sb, ref pass, ref fail, d2 > 1e-3, string.Format(CultureInfo.InvariantCulture, "有机水波：波数生效（1 个方向 vs 6 个方向累计差 {0:0.###}）", d2));
            }

            // 波峰塑形
            {
                bool ok = true;
                for (int i = 0; i <= 20; i++)
                {
                    double x = -1.0 + i * 0.1;
                    double y = WaterRippleCore.Shape(x, 0.7);
                    if (Math.Abs(y) > 1.0 + 1e-9) ok = false;
                    if (x * y < 0) ok = false;                       // 不翻符号
                }
                Check(sb, ref pass, ref fail, ok, "波峰塑形：不翻符号、幅度不超 ±1");
                Check(sb, ref pass, ref fail, Math.Abs(WaterRippleCore.Shape(0, 1)) < 1e-12, "波峰塑形：0 点仍是 0");
                double sharp = WaterRippleCore.Shape(0.3, 1.0), soft = WaterRippleCore.Shape(0.3, 0.0);
                Check(sb, ref pass, ref fail, Math.Abs(sharp) > Math.Abs(soft),
                    string.Format(CultureInfo.InvariantCulture, "波峰形状：越大越「平顶陡壁」（0.3 处 {0:0.###} > 圆滑 {1:0.###}，值被推向饱和 = 平顶 + 陡壁）", sharp, soft));
            }
        }

        /// <summary>用例2：平面曲面输入 —— 位移沿法向、幅度对、XY 不跑偏</summary>
        static void Case2_PlanarSurface(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例2：平面曲面输入（100×60） ---");
            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(100, 0, 0),
                                                     new Point3d(100, 60, 0), new Point3d(0, 60, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }

            var s = new WaterRippleSettings { Wavelength = 20.0, WaveHeight = 3.0, WaveMode = 0, WaveCount = 5, Seed = 11, SamplesPerWave = 16.0 };
            string rep;
            WaterRippleResult r = WaterRippleCore.Generate(plate, s, out rep);
            sb.AppendLine("    " + rep);
            Check(sb, ref pass, ref fail, r.Ripple != null && r.Ripple.Faces.Count > 0,
                string.Format(CultureInfo.InvariantCulture, "生成了网格（{0} 顶点 / {1} 面）", r.Vertices, r.Faces));

            double half = s.WaveHeight * 0.5;
            Check(sb, ref pass, ref fail, r.MinH >= -half - 1e-9 && r.MaxH <= half + 1e-9,
                string.Format(CultureInfo.InvariantCulture, "位移在 ±波高/2 之内（实测 {0:0.###} ~ {1:0.###}，波高 {2:0.###}）", r.MinH, r.MaxH, s.WaveHeight));
            Check(sb, ref pass, ref fail, (r.MaxH - r.MinH) > half * 0.8,
                string.Format(CultureInfo.InvariantCulture, "起伏是真的（峰谷差 {0:0.###}）", r.MaxH - r.MinH));

            // 位移只沿 Z（平面法向）：XY 足迹不变
            var bb = r.Ripple.GetBoundingBox(true);
            Check(sb, ref pass, ref fail, Math.Abs(bb.Min.X) < 1e-6 && Math.Abs(bb.Max.X - 100) < 1e-6 &&
                                          Math.Abs(bb.Min.Y) < 1e-6 && Math.Abs(bb.Max.Y - 60) < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "XY 足迹不变（{0:0.##}×{1:0.##}，应 100×60）→ 位移只沿法向", bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y));

            // 每个顶点的 Z 都等于波形场值（平面输入 → 位移 = f(u,v)）
            bool exact = true;
            double k = 2.0 * Math.PI / s.Wavelength;
            for (int i = 0; i < r.Ripple.Vertices.Count; i++)
            {
                Point3d p = r.Ripple.Vertices.Point3dAt(i);
                double want = WaterRippleCore.WaveField(p.X, p.Y, s, k, 50.0, 30.0) * half;
                if (Math.Abs(p.Z - want) > 1e-6) { exact = false; break; }
            }
            Check(sb, ref pass, ref fail, exact, "每个顶点的位移都精确等于波形场值（平面输入：位移 = f(u,v)）");

            // 剖分精度跟着波长走：顶点间距 ≈ 波长/每波长分段
            double spacing = 0;
            int cnt = 0;
            for (int i = 0; i < r.Ripple.TopologyEdges.Count && cnt < 400; i++)
            {
                Line l = r.Ripple.TopologyEdges.EdgeLine(i);
                if (l == null || !l.IsValid) continue;
                spacing += l.Length; cnt++;
            }
            spacing = cnt > 0 ? spacing / cnt : 0;
            double want2 = s.Wavelength / s.SamplesPerWave;
            Check(sb, ref pass, ref fail, spacing > want2 * 0.4 && spacing < want2 * 2.5,
                string.Format(CultureInfo.InvariantCulture, "剖分密度跟着波长走（平均边长 {0:0.###}，目标 {1:0.###}）", spacing, want2));
        }

        /// <summary>用例3：多重曲面当成一整个面 —— 缝上不裂、位移跨面连续</summary>
        static void Case3_PolySurfaceSeam(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例3：多重曲面（两张共面面片拼成 200×60） ---");
            Brep left = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(100, 0, 0),
                                                    new Point3d(100, 60, 0), new Point3d(0, 60, 0), 1e-6);
            Brep right = Brep.CreateFromCornerPoints(new Point3d(100, 0, 0), new Point3d(200, 0, 0),
                                                     new Point3d(200, 60, 0), new Point3d(100, 60, 0), 1e-6);
            Brep[] joined = Brep.JoinBreps(new Brep[] { left, right }, 1e-6);
            Brep poly = (joined != null && joined.Length > 0) ? joined[0] : null;
            if (poly == null || poly.Faces.Count < 2) { Check(sb, ref pass, ref fail, false, "拼多重曲面失败"); return; }
            sb.AppendLine("    多重曲面面数 = " + poly.Faces.Count);

            var s = new WaterRippleSettings { Wavelength = 25.0, WaveHeight = 4.0, WaveMode = 0, WaveCount = 5, Seed = 5, SamplesPerWave = 16.0 };
            string rep;
            WaterRippleResult r = WaterRippleCore.Generate(poly, s, out rep);
            sb.AppendLine("    " + rep);
            Check(sb, ref pass, ref fail, r.Ripple != null && r.Ripple.Faces.Count > 0, "多重曲面生成了网格");
            if (r.Ripple == null) return;

            // 缝上焊接：没有重复顶点（同一位置只留一个）
            int dup = 0;
            var seen = new Dictionary<long, int>();
            for (int i = 0; i < r.Ripple.Vertices.Count; i++)
            {
                long key = Q3(r.Ripple.Vertices.Point3dAt(i));
                int c;
                if (seen.TryGetValue(key, out c)) { seen[key] = c + 1; dup++; }
                else seen[key] = 1;
            }
            Check(sb, ref pass, ref fail, dup == 0, string.Format(CultureInfo.InvariantCulture, "缝上没有重复顶点（重复 {0} 个）→ 跨面焊成一张", dup));

            // 位移跨面连续：每个顶点的 Z 都等于波形场值（与面归属无关）
            double k = 2.0 * Math.PI / s.Wavelength, half = s.WaveHeight * 0.5;
            int bad = 0;
            for (int i = 0; i < r.Ripple.Vertices.Count; i++)
            {
                Point3d p = r.Ripple.Vertices.Point3dAt(i);
                double want = WaterRippleCore.WaveField(p.X, p.Y, s, k, 100.0, 30.0) * half;
                if (Math.Abs(p.Z - want) > 1e-6) bad++;
            }
            Check(sb, ref pass, ref fail, bad == 0,
                string.Format(CultureInfo.InvariantCulture, "位移跨面连续（{0} 个顶点与波形场不符，应 0）→ 缝上不裂", bad));

            // 裸边只剩外轮廓：周长 ≈ 2×(200+60)
            double nakedLen = 0;
            try
            {
                Polyline[] naked = r.Ripple.GetNakedEdges();
                if (naked != null)
                    for (int i = 0; i < naked.Length; i++) nakedLen += naked[i].Length;
            }
            catch { }
            double per = 2.0 * (200.0 + 60.0);
            Check(sb, ref pass, ref fail, nakedLen > per * 0.9 && nakedLen < per * 1.15,
                string.Format(CultureInfo.InvariantCulture, "裸边只剩外轮廓（{0:0.#}，外周长 {1:0.#}）→ 中间那道缝焊住了", nakedLen, per));
        }

        /// <summary>用例4：闭合平面曲线边界 —— 铺满边界、不越界</summary>
        static void Case4_CurveBoundary(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例4：闭合平面曲线边界（120×60 矩形） ---");
            var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 60)).ToNurbsCurve();
            var s = new WaterRippleSettings { Wavelength = 18.0, WaveHeight = 2.5, WaveMode = 0, WaveCount = 4, Seed = 9, SamplesPerWave = 16.0 };
            string rep;
            WaterRippleResult r = WaterRippleCore.Generate(rect, s, out rep);
            sb.AppendLine("    " + rep);
            Check(sb, ref pass, ref fail, r.Ripple != null && r.Ripple.Faces.Count > 0, "曲线边界生成了网格面片");
            if (r.Ripple == null) return;

            var bb = r.Ripple.GetBoundingBox(true);
            bool filled = Math.Abs(bb.Min.X) < 1e-3 && Math.Abs(bb.Max.X - 120) < 1e-3 &&
                          Math.Abs(bb.Min.Y) < 1e-3 && Math.Abs(bb.Max.Y - 60) < 1e-3;
            Check(sb, ref pass, ref fail, filled,
                string.Format(CultureInfo.InvariantCulture, "面片铺满边界（{0:0.##}×{1:0.##}，应 120×60）", bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y));

            // 不越界：所有顶点在曲线内（XY）
            int outside = 0;
            var pl = new Plane(Point3d.Origin, Vector3d.ZAxis);
            for (int i = 0; i < r.Ripple.Vertices.Count; i++)
            {
                Point3d p = r.Ripple.Vertices.Point3dAt(i);
                var p2 = new Point3d(p.X, p.Y, 0);
                bool inCurve;
                try { inCurve = rect.Contains(p2, pl, 1e-3) == PointContainment.Inside || rect.Contains(p2, pl, 1e-3) == PointContainment.Coincident; }
                catch { inCurve = true; }
                if (!inCurve) outside++;
            }
            Check(sb, ref pass, ref fail, outside == 0, string.Format(CultureInfo.InvariantCulture, "没有顶点越出曲线边界（越界 {0} 个）", outside));
        }

        /// <summary>用例5：三种波形在网格上确实不同（有机 / 条带 / 同心）</summary>
        static void Case5_WaveModes(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例5：三种波形对比 ---");
            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(120, 0, 0),
                                                     new Point3d(120, 80, 0), new Point3d(0, 80, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }

            var baseSet = new WaterRippleSettings { Wavelength = 20.0, WaveHeight = 3.0, WaveCount = 1, Seed = 4, SamplesPerWave = 14.0 };
            var sigs = new double[3];
            var names = new string[] { "有机水波", "定向条带", "同心涟漪" };
            for (int mode = 0; mode < 3; mode++)
            {
                var s = baseSet.Clone(); s.WaveMode = mode;
                if (mode == 1) s.Direction = 0.0;
                string rep;
                WaterRippleResult r = WaterRippleCore.Generate(plate, s, out rep);
                sigs[mode] = r != null && r.Ripple != null ? RippleSignature(r.Ripple) : double.NaN;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    {0}：位移 {1:0.###}~{2:0.###}，特征值 {3:0.####}",
                    names[mode], r != null ? r.MinH : 0, r != null ? r.MaxH : 0, sigs[mode]));
            }
            Check(sb, ref pass, ref fail, !double.IsNaN(sigs[0]) && !double.IsNaN(sigs[1]) && !double.IsNaN(sigs[2]),
                "三种波形都能生成网格");
            Check(sb, ref pass, ref fail, Math.Abs(sigs[0] - sigs[1]) > 1e-4 && Math.Abs(sigs[1] - sigs[2]) > 1e-4 && Math.Abs(sigs[0] - sigs[2]) > 1e-4,
                "三种波形的结果互不相同（波形参数真的切了算法）");

            // 条带方向 0° 且方向散布 0：同一 u 的顶点位移相同（网格上也成立）
            {
                var s = baseSet.Clone(); s.WaveMode = 1; s.Direction = 0.0; s.Spread = 0.0;
                string rep;
                WaterRippleResult r = WaterRippleCore.Generate(plate, s, out rep);
                var groups = new Dictionary<long, double>();
                int mismatch = 0;
                for (int i = 0; i < r.Ripple.Vertices.Count; i++)
                {
                    Point3d p = r.Ripple.Vertices.Point3dAt(i);
                    long key = (long)Math.Round(p.X * 1000.0);
                    double z = p.Z;
                    double prev;
                    if (groups.TryGetValue(key, out prev)) { if (Math.Abs(prev - z) > 1e-6) mismatch++; }
                    else groups[key] = z;
                }
                Check(sb, ref pass, ref fail, mismatch == 0,
                    string.Format(CultureInfo.InvariantCulture, "条带（方向 0°）网格上同一 u 的顶点位移相同（不符 {0} 个）", mismatch));
            }
        }

        /// <summary>用例6：边缘收平 —— 边界处位移回落到 0，中间仍是全幅</summary>
        static void Case6_Fade(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例6：边缘收平（12mm） ---");
            var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 60)).ToNurbsCurve();
            var s = new WaterRippleSettings
            {
                Wavelength = 20.0, WaveHeight = 4.0, WaveMode = 0, WaveCount = 5, Seed = 6,
                SamplesPerWave = 16.0, LockBoundary = true, Fade = 12.0, BlendSmooth = 0.5
            };
            string rep;
            WaterRippleResult r = WaterRippleCore.Generate(rect, s, out rep);
            sb.AppendLine("    " + rep);
            if (r.Ripple == null) { Check(sb, ref pass, ref fail, false, "收平用例没生成网格"); return; }

            // 边界顶点（裸边上的点）位移 ≈ 0
            double maxEdge = 0;
            int edgePts = 0;
            try
            {
                Polyline[] naked = r.Ripple.GetNakedEdges();
                if (naked != null)
                    for (int i = 0; i < naked.Length; i++)
                        for (int j = 0; j < naked[i].Count; j++)
                        {
                            Point3d p = naked[i][j];
                            maxEdge = Math.Max(maxEdge, Math.Abs(p.Z));
                            edgePts++;
                        }
            }
            catch { }
            Check(sb, ref pass, ref fail, edgePts > 0 && maxEdge < s.WaveHeight * 0.02,
                string.Format(CultureInfo.InvariantCulture, "边界顶点位移回落到 0（{0} 个边界点，最大 |h| = {1:0.####}）", edgePts, maxEdge));

            // 中间仍是全幅
            double mid = 0;
            for (int i = 0; i < r.Ripple.Vertices.Count; i++)
            {
                Point3d p = r.Ripple.Vertices.Point3dAt(i);
                if (Math.Abs(p.X - 60) < 6 && Math.Abs(p.Y - 30) < 6) mid = Math.Max(mid, Math.Abs(p.Z));
            }
            Check(sb, ref pass, ref fail, mid > s.WaveHeight * 0.15,
                string.Format(CultureInfo.InvariantCulture, "中间区域仍是全幅起伏（中心附近最大 |h| = {0:0.###}）", mid));

            // 对照：不固定边界时边界顶点跟着起伏
            var s0 = s.Clone(); s0.LockBoundary = false; s0.Fade = 12.0;
            WaterRippleResult r0 = WaterRippleCore.Generate(rect, s0, out rep);
            double maxEdge0 = 0;
            try
            {
                Polyline[] naked = r0.Ripple.GetNakedEdges();
                if (naked != null)
                    for (int i = 0; i < naked.Length; i++)
                        for (int j = 0; j < naked[i].Count; j++) maxEdge0 = Math.Max(maxEdge0, Math.Abs(naked[i][j].Z));
            }
            catch { }
            Check(sb, ref pass, ref fail, maxEdge0 > s.WaveHeight * 0.1,
                string.Format(CultureInfo.InvariantCulture, "对照：不收平时边界顶点有明显位移（最大 |h| = {0:0.###}）", maxEdge0));
        }

        /// <summary>用例7：波高 0 = 纯平；种子可复现</summary>
        static void Case7_ZeroAndSeed(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例7：波高 0 / 种子复现 ---");
            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(80, 0, 0),
                                                     new Point3d(80, 50, 0), new Point3d(0, 50, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }

            var flat = new WaterRippleSettings { Wavelength = 20.0, WaveHeight = 0.0, SamplesPerWave = 12.0 };
            string rep;
            WaterRippleResult rf = WaterRippleCore.Generate(plate, flat, out rep);
            double maxZ = 0;
            if (rf.Ripple != null)
                for (int i = 0; i < rf.Ripple.Vertices.Count; i++) maxZ = Math.Max(maxZ, Math.Abs(rf.Ripple.Vertices.Point3dAt(i).Z));
            Check(sb, ref pass, ref fail, rf.Ripple != null && maxZ < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "波高 0 → 网格完全平（最大 |Z| = {0:0.#######}）", maxZ));

            var s1 = new WaterRippleSettings { Wavelength = 20.0, WaveHeight = 3.0, WaveMode = 0, WaveCount = 5, Seed = 21, SamplesPerWave = 12.0 };
            var s2 = s1.Clone();
            WaterRippleResult ra = WaterRippleCore.Generate(plate, s1, out rep);
            WaterRippleResult rb = WaterRippleCore.Generate(plate, s2, out rep);
            double diff = 0;
            if (ra.Ripple != null && rb.Ripple != null && ra.Ripple.Vertices.Count == rb.Ripple.Vertices.Count)
                for (int i = 0; i < ra.Ripple.Vertices.Count; i++)
                    diff = Math.Max(diff, ra.Ripple.Vertices.Point3dAt(i).DistanceTo(rb.Ripple.Vertices.Point3dAt(i)));
            Check(sb, ref pass, ref fail, ra.Ripple != null && rb.Ripple != null && diff < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "同种子两次生成完全一致（最大点差 {0:0.#######}）", diff));

            var s3 = s1.Clone(); s3.Seed = 22;
            WaterRippleResult rc = WaterRippleCore.Generate(plate, s3, out rep);
            double diff2 = 0;
            if (ra.Ripple != null && rc.Ripple != null && ra.Ripple.Vertices.Count == rc.Ripple.Vertices.Count)
                for (int i = 0; i < ra.Ripple.Vertices.Count; i++)
                    diff2 = Math.Max(diff2, ra.Ripple.Vertices.Point3dAt(i).DistanceTo(rc.Ripple.Vertices.Point3dAt(i)));
            Check(sb, ref pass, ref fail, diff2 > 1e-4,
                string.Format(CultureInfo.InvariantCulture, "换种子结果不同（最大点差 {0:0.####}）", diff2));
        }

        /// <summary>用例8：一键平滑 → 细分曲面；写文档只落一份</summary>
        static void Case8_SubDAndDocument(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例8：一键平滑（SubD）+ 写文档 ---");
            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(100, 0, 0),
                                                     new Point3d(100, 60, 0), new Point3d(0, 60, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }

            var off = new WaterRippleSettings { Wavelength = 22.0, WaveHeight = 3.0, WaveCount = 4, Seed = 8, Smooth = false };
            string rep;
            WaterRippleResult ro = WaterRippleCore.Generate(plate, off, out rep);
            Check(sb, ref pass, ref fail, ro.SmoothSubD == null, "不勾一键平滑：没有细分曲面（只出网格）");

            var on = off.Clone(); on.Smooth = true;
            WaterRippleResult rn = WaterRippleCore.Generate(plate, on, out rep);
            sb.AppendLine("    " + rep);
            Check(sb, ref pass, ref fail, rn.SmoothSubD != null,
                rn.SmoothSubD != null ? "勾选一键平滑：网格 → 细分曲面（SubD）成功" : "细分曲面没建出来（按网格输出）");
            if (rn.SmoothSubD != null)
                Check(sb, ref pass, ref fail, rn.SmoothSubD.Vertices.Count > 0,
                    string.Format(CultureInfo.InvariantCulture, "细分曲面有控制点：{0} 个", rn.SmoothSubD.Vertices.Count));

            // 写文档：勾平滑 → 只写 SubD；不勾 → 只写网格
            var before = new HashSet<Guid>();
            foreach (RhinoObject o in doc.Objects) before.Add(o.Id);
            int mc, sc;
            WaterRippleCore.AddToDocument(doc, new List<WaterRippleResult> { rn }, out mc, out sc);
            Check(sb, ref pass, ref fail, mc == 0 && sc == 1,
                string.Format(CultureInfo.InvariantCulture, "勾平滑写文档：只写 1 份细分曲面（网格 {0} / 平滑 {1}）", mc, sc));
            var added = new List<Guid>();
            foreach (RhinoObject o in doc.Objects) if (!before.Contains(o.Id)) added.Add(o.Id);
            int ls = LayerIndex(doc, WaterRippleCore.LayerSmooth);
            bool onLayer = added.Count == 1 && ls >= 0;
            if (onLayer)
            {
                RhinoObject o = doc.Objects.FindId(added[0]);
                onLayer = o != null && o.Geometry is SubD && o.Attributes.LayerIndex == ls;
            }
            Check(sb, ref pass, ref fail, onLayer, "写进去的是细分物件且在图层 " + WaterRippleCore.LayerSmooth);
            Cleanup(doc, added.ToArray());

            var before2 = new HashSet<Guid>();
            foreach (RhinoObject o in doc.Objects) before2.Add(o.Id);
            WaterRippleCore.AddToDocument(doc, new List<WaterRippleResult> { ro }, out mc, out sc);
            Check(sb, ref pass, ref fail, mc == 1 && sc == 0,
                string.Format(CultureInfo.InvariantCulture, "不勾平滑写文档：只写 1 个网格（网格 {0} / 平滑 {1}）", mc, sc));
            var added2 = new List<Guid>();
            foreach (RhinoObject o in doc.Objects) if (!before2.Contains(o.Id)) added2.Add(o.Id);
            int lm = LayerIndex(doc, WaterRippleCore.LayerMesh);
            bool onLayer2 = added2.Count == 1 && lm >= 0;
            if (onLayer2)
            {
                RhinoObject o = doc.Objects.FindId(added2[0]);
                onLayer2 = o != null && o.Geometry is Mesh && o.Attributes.LayerIndex == lm;
            }
            Check(sb, ref pass, ref fail, onLayer2, "写进去的是网格物件且在图层 " + WaterRippleCore.LayerMesh);
            Cleanup(doc, added2.ToArray());
        }

        /// <summary>用例9：不支持的目标要给明确理由（网格 / 细分物件 / 开口曲线 / 非平面曲线）</summary>
        static void Case9_Reject(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例9：不支持的目标 ---");
            var ids = new List<Guid>();
            try
            {
                var mesh = new Mesh();
                mesh.Vertices.Add(0, 0, 0); mesh.Vertices.Add(10, 0, 0); mesh.Vertices.Add(10, 10, 0); mesh.Vertices.Add(0, 10, 0);
                mesh.Faces.AddFace(0, 1, 2, 3);
                Guid mId = doc.Objects.AddMesh(mesh);
                ids.Add(mId);
                GeometryBase g; string why;
                bool ok = WaterRippleCore.TryResolve(doc.Objects.FindId(mId), out g, out why);
                Check(sb, ref pass, ref fail, !ok && !string.IsNullOrEmpty(why), "网格被拒且给了理由：" + why);

                Guid openId = doc.Objects.AddCurve(new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0)));
                ids.Add(openId);
                ok = WaterRippleCore.TryResolve(doc.Objects.FindId(openId), out g, out why);
                Check(sb, ref pass, ref fail, !ok && why.Contains("闭合"), "开口曲线被拒且给了理由：" + why);

                var pts = new Point3d[] { new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 10, 5), new Point3d(0, 10, 0), new Point3d(0, 0, 0) };
                Guid curveId = doc.Objects.AddCurve(new PolylineCurve(pts));
                ids.Add(curveId);
                ok = WaterRippleCore.TryResolve(doc.Objects.FindId(curveId), out g, out why);
                Check(sb, ref pass, ref fail, !ok && why.Contains("平面"), "非平面闭合曲线被拒且给了理由：" + why);

                Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(50, 0, 0),
                                                         new Point3d(50, 40, 0), new Point3d(0, 40, 0), 1e-6);
                Guid bId = doc.Objects.AddBrep(plate);
                ids.Add(bId);
                ok = WaterRippleCore.TryResolve(doc.Objects.FindId(bId), out g, out why);
                Check(sb, ref pass, ref fail, ok, "平面曲面能解析（正例）");
            }
            finally { Cleanup(doc, ids.ToArray()); }
        }

        /// <summary>用例10：面板交互（拾取按钮红绿 / 波形切换灰掉用不到的行 / 一键平滑）</summary>
        static void Case10_Panel(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例10：面板交互 ---");
            WaterRipplePanel panel = null;
            try
            {
                panel = new WaterRipplePanel(new WaterRippleSettings(), WaterRippleCore.EmptyTargetHint);
                panel.Show();
                Application.DoEvents();
                Check(sb, ref pass, ref fail, panel != null, "面板构造并显示成功");
                Check(sb, ref pass, ref fail, !panel.PickState, "刚打开（没选目标）：拾取按钮是红的");
                panel.SetTargetState(true);
                Check(sb, ref pass, ref fail, panel.PickState, "选到可用目标：拾取按钮变绿");
                panel.SetTargetState(false);
                Check(sb, ref pass, ref fail, !panel.PickState, "选错/被删（置 false）：拾取按钮变红");

                panel.SetWaveMode(0);
                Check(sb, ref pass, ref fail, panel.RowEnabled(0) && panel.RowEnabled(1) && panel.RowEnabled(2),
                    "有机水波：波数 / 主方向 / 方向散布 都可调");
                panel.SetWaveMode(1);
                Check(sb, ref pass, ref fail, panel.RowEnabled(0) && panel.RowEnabled(1) && panel.RowEnabled(2),
                    "定向条带：三行仍然都可调（不留灰掉的行）");
                panel.SetWaveMode(2);
                Check(sb, ref pass, ref fail, panel.RowEnabled(0) && panel.RowEnabled(1) && panel.RowEnabled(2),
                    "同心涟漪：三行仍然都可调（不留灰掉的行）");

                panel.SetSmooth(false);
                Check(sb, ref pass, ref fail, panel.SmoothBlockVisible, "一键平滑勾选框可见");
                panel.SetSmooth(true);
                Check(sb, ref pass, ref fail, panel.Settings.Smooth, "勾选一键平滑 → 参数写进 Settings");

                // 图标（工具条/面板头部/插件中心共用同一套渲染器）：能渲染，并导出 PNG 供人工核对
                try
                {
                    System.Drawing.Bitmap ic = IvanUi.Theme.CreateGlassIcon("ripple", 64);
                    Check(sb, ref pass, ref fail, ic != null && ic.Width == 64, "水波纹图标能渲染（64×64）");
                    if (ic != null)
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(DefaultReportPath);
                            if (!string.IsNullOrEmpty(dir)) ic.Save(Path.Combine(dir, "WaterRippleIcon.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        catch { }
                        ic.Dispose();
                    }
                }
                catch (Exception ex) { Check(sb, ref pass, ref fail, false, "图标渲染异常：" + ex.Message); }
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "面板用例异常：" + ex.Message); }
            finally { try { if (panel != null) panel.Close(); } catch { } }
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>面板参数接线：改这个参数 → 生成的网格必须跟着变（「点了没反应」回归）</summary>
        /// <summary>面板参数接线：三种波形下，每个参数改值都必须改变结果（「点了没反应」回归）</summary>
        static void Case11_PanelWiring(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例11：面板参数接线（三种波形下每个参数都必须改变结果） ---");
            Brep plate = Brep.CreateFromCornerPoints(new Point3d(0, 0, 0), new Point3d(140, 0, 0),
                                                     new Point3d(140, 90, 0), new Point3d(0, 90, 0), 1e-6);
            if (plate == null) { Check(sb, ref pass, ref fail, false, "创建平面失败"); return; }

            WaterRipplePanel panel = null;
            try
            {
                var seedSet = new WaterRippleSettings
                {
                    Wavelength = 22.0, WaveHeight = 3.0, WaveMode = 0, WaveCount = 5,
                    Direction = 0.0, Spread = 55.0, Crest = 0.0, Fade = 0.0, Seed = 3,
                    SamplesPerWave = 14.0, MmToModel = 1.0
                };
                panel = new WaterRipplePanel(seedSet, WaterRippleCore.EmptyTargetHint);
                panel.Show();
                Application.DoEvents();
                panel.SetLockBoundary(true);        // 过渡参数要先开固定边界才可调
                Application.DoEvents();
                Check(sb, ref pass, ref fail, panel.LockBoundaryChecked && panel.BoundaryRowsEnabled,
                    "固定边界：勾上后过渡宽度 / 过渡平滑度可调");

                string[] names = { "波长", "波高", "波数", "主方向", "方向散布", "波峰形状", "每波长分段", "过渡宽度", "过渡平滑度", "随机种子" };
                double[] values = { 34.0, 7.0, 2.0, 45.0, 8.0, 0.9, 8.0, 18.0, 0.9, 77.0 };
                string[] modeNames = { "有机水波", "定向条带", "同心涟漪" };

                for (int m = 0; m < 3; m++)
                {
                    sb.AppendLine("    [" + modeNames[m] + "]");
                    panel.SetWaveMode(m);
                    Application.DoEvents();
                    double baseSig = SigOf(plate, panel.Settings);
                    for (int i = 0; i < names.Length; i++)
                    {
                        string name = names[i];
                        double before = ParamValue(panel, name);
                        bool set = panel.SetParam(name, values[i]);
                        double after = ParamValue(panel, name);
                        bool moved = set && Math.Abs(after - before) > 1e-9;
                        double sig = SigOf(plate, panel.Settings);
                        if (!moved)
                            Check(sb, ref pass, ref fail, false,
                                string.Format(CultureInfo.InvariantCulture, "{0} · {1}：面板上改不动（{2:0.###} → {3:0.###}）",
                                    modeNames[m], name, before, after));
                        else
                            Check(sb, ref pass, ref fail, !double.IsNaN(sig) && Math.Abs(sig - baseSig) > 1e-4,
                                string.Format(CultureInfo.InvariantCulture, "{0} · {1}：{2:0.###} → {3:0.###} 结果跟着变（{4:0.####} ≠ {5:0.####}）",
                                    modeNames[m], name, before, values[i], sig, baseSig));
                        panel.SetParam(name, before);      // 复位
                    }
                }
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "接线用例异常：" + ex.Message); }
            finally { try { if (panel != null) panel.Close(); } catch { } }
        }

        /// <summary>用例12：固定边界 + 边界到纹理的过渡（宽度 / 平滑度）</summary>
        static void Case12_LockBoundary(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例12：固定边界 + 边界过渡 ---");

            // ① 过渡曲线（纯函数）
            Check(sb, ref pass, ref fail, WaterRippleCore.BlendCurve(0.0, 0.5) == 0.0 && WaterRippleCore.BlendCurve(1.0, 0.5) == 1.0,
                "过渡曲线：两端精确落在 0 / 1（边界完全锁住、远端全幅）");
            bool mono = true; double prev = -1;
            for (int i = 0; i <= 20; i++)
            {
                double v = WaterRippleCore.BlendCurve(i / 20.0, 1.0);
                if (v < prev - 1e-12) mono = false;
                prev = v;
            }
            Check(sb, ref pass, ref fail, mono, "过渡曲线：单调不回头（不会出现反向凹坑）");
            double lin = WaterRippleCore.BlendCurve(0.25, 0.0), mid = WaterRippleCore.BlendCurve(0.25, 0.5), soft = WaterRippleCore.BlendCurve(0.25, 1.0);
            Check(sb, ref pass, ref fail, lin > mid && mid > soft,
                string.Format(CultureInfo.InvariantCulture, "过渡平滑度：越大过渡越柔（0.25 处 线性 {0:0.###} > 0.5 {1:0.###} > 1.0 {2:0.###}）", lin, mid, soft));

            // ② 网格：固定边界 = 边界顶点一点不动；不固定 = 边界跟着起伏
            var rect = new Rectangle3d(Plane.WorldXY, new Interval(0, 120), new Interval(0, 60)).ToNurbsCurve();
            var on = new WaterRippleSettings
            {
                Wavelength = 20.0, WaveHeight = 4.0, WaveCount = 5, Seed = 6,
                SamplesPerWave = 16.0, LockBoundary = true, Fade = 10.0, BlendSmooth = 0.5
            };
            string rep;
            WaterRippleResult rOn = WaterRippleCore.Generate(rect, on, out rep);
            sb.AppendLine("    " + rep);
            if (rOn.Ripple == null) { Check(sb, ref pass, ref fail, false, "固定边界用例没生成网格"); return; }
            double maxEdge = 0; int edgePts = 0;
            try
            {
                Polyline[] naked = rOn.Ripple.GetNakedEdges();
                if (naked != null)
                    for (int i = 0; i < naked.Length; i++)
                        for (int j = 0; j < naked[i].Count; j++) { maxEdge = Math.Max(maxEdge, Math.Abs(naked[i][j].Z)); edgePts++; }
            }
            catch { }
            Check(sb, ref pass, ref fail, edgePts > 0 && maxEdge < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "固定边界：边界顶点一点不动（{0} 个边界点，最大 |Z| = {1:0.#########}）", edgePts, maxEdge));

            var off = on.Clone(); off.LockBoundary = false;
            WaterRippleResult rOff = WaterRippleCore.Generate(rect, off, out rep);
            double maxEdgeOff = 0;
            try
            {
                Polyline[] naked = rOff.Ripple.GetNakedEdges();
                if (naked != null)
                    for (int i = 0; i < naked.Length; i++)
                        for (int j = 0; j < naked[i].Count; j++) maxEdgeOff = Math.Max(maxEdgeOff, Math.Abs(naked[i][j].Z));
            }
            catch { }
            Check(sb, ref pass, ref fail, maxEdgeOff > on.WaveHeight * 0.1,
                string.Format(CultureInfo.InvariantCulture, "对照（不固定边界）：边界跟着起伏（最大 |Z| = {0:0.###}）", maxEdgeOff));

            // ③ 过渡宽度：越宽，整体起伏越小（同一随机种子下比最大 |Z|）
            var narrow = on.Clone(); narrow.Fade = 4.0;
            var wide = on.Clone(); wide.Fade = 30.0;
            double aNarrow = MaxAbsZ(WaterRippleCore.Generate(rect, narrow, out rep).Ripple);
            double aWide = MaxAbsZ(WaterRippleCore.Generate(rect, wide, out rep).Ripple);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    过渡宽度 4mm → 最大 |Z| {0:0.###}；30mm → {1:0.###}", aNarrow, aWide));
            Check(sb, ref pass, ref fail, aNarrow > 0 && aWide > 0 && aWide < aNarrow,
                string.Format(CultureInfo.InvariantCulture, "过渡宽度生效：越宽整体起伏越小（{0:0.###} < {1:0.###}）", aWide, aNarrow));

            // ④ 过渡平滑度：越柔，**过渡带内**的起伏越小（同宽度、同种子；整体最大 |Z| 由内部全幅主导，看不出差别）
            var hard = on.Clone(); hard.BlendSmooth = 0.0;
            var soft2 = on.Clone(); soft2.BlendSmooth = 1.0;
            Mesh mHard = WaterRippleCore.Generate(rect, hard, out rep).Ripple;
            Mesh mSoft = WaterRippleCore.Generate(rect, soft2, out rep).Ripple;
            double bHard = BandAbsZ(mHard, 1.0, 5.0);      // 只量过渡带靠边界的一半（t<0.5）：越柔这里越平
            double bSoft = BandAbsZ(mSoft, 1.0, 5.0);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    过渡平滑度 0 → 过渡带靠边界半段最大 |Z| {0:0.###}；1 → {1:0.###}", bHard, bSoft));
            Check(sb, ref pass, ref fail, bHard > 0 && bSoft > 0 && bSoft < bHard,
                string.Format(CultureInfo.InvariantCulture, "过渡平滑度生效：越柔、靠边界一侧越平（{0:0.###} < {1:0.###}）", bSoft, bHard));

            // ⑤ 闭合体（没有开放边界）：固定边界开着也不崩，并在报告里说清楚
            Brep box = Brep.CreateFromBox(new BoundingBox(new Point3d(0, 0, 0), new Point3d(40, 30, 20)));
            if (box != null)
            {
                var sBox = on.Clone();
                WaterRippleResult rBox = WaterRippleCore.Generate(box, sBox, out rep);
                Check(sb, ref pass, ref fail, rBox.Ripple != null && rBox.Ripple.Faces.Count > 0 && rep.Contains("没有开放边界"),
                    "闭合体 + 固定边界：不崩，并在报告里写明「没有开放边界，按全幅处理」");
            }

            // ⑥ SubD 边界保形：固定边界时极限面要贴住原边界（不打 crease 会被磨圆收进去）
            //    ⚠ ToBrep() 的签名在 Rhino 7 / 8 不同，这段只在 Rhino 8 自检里跑
#if !RH7
            {
                var sSm = new WaterRippleSettings
                {
                    Wavelength = 30.0, WaveHeight = 3.0, WaveCount = 3, Seed = 5,
                    SamplesPerWave = 4.0, LockBoundary = true, Fade = 10.0, Smooth = true
                };
                WaterRippleResult rSm = WaterRippleCore.Generate(rect, sSm, out rep);
                if (rSm.SmoothSubD == null)
                    Check(sb, ref pass, ref fail, false, "SubD 边界保形：细分曲面没建出来");
                else
                {
                    Check(sb, ref pass, ref fail, rep.Contains("边界已打 crease"),
                        "SubD：固定边界时自动给边界打 crease（报告里写明）");
                    double wx = 0, wy = 0;
                    try
                    {
                        Brep lb = rSm.SmoothSubD.ToBrep();
                        if (lb != null)
                        {
                            BoundingBox bb = lb.GetBoundingBox(true);
                            if (bb.IsValid) { wx = bb.Max.X - bb.Min.X; wy = bb.Max.Y - bb.Min.Y; }
                        }
                    }
                    catch { }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    SubD 极限面包围盒（打了 crease）{0:0.###} × {1:0.###}（原边界 120 × 60）", wx, wy));
                    Check(sb, ref pass, ref fail, wx > 119.5 && wy > 59.5,
                        string.Format(CultureInfo.InvariantCulture,
                            "固定边界 + 一键平滑：极限面贴住原边界（{0:0.###} × {1:0.###}）", wx, wy));

                    // 角点保不保：直接量「角 (120,60,0) 到极限面的最近距离」（bbox 测不出来 —— 直边中点仍然到 120）
                    var corner = new Point3d(120, 60, 0);
                    double dCreased = LimitDistTo(rSm.SmoothSubD, corner);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    角 (120,60) 到极限面距离：打了 crease {0:0.###}", dCreased));
                    Check(sb, ref pass, ref fail, dCreased < 0.5,
                        string.Format(CultureInfo.InvariantCulture, "打了 crease：四个角保留（离角 {0:0.###} < 0.5）", dCreased));

                    // 对照：同一张网格、不做 crease → 角被磨圆
                    double dPlain = double.MaxValue;
                    try
                    {
                        SubD plain = SubD.CreateFromMesh(rSm.Ripple);
                        dPlain = LimitDistTo(plain, corner);
                    }
                    catch { }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    角 (120,60) 到极限面距离：不打 crease {0:0.###}", dPlain));
                    Check(sb, ref pass, ref fail, dPlain > 0.5 && dPlain < double.MaxValue,
                        string.Format(CultureInfo.InvariantCulture, "对照：不打 crease 的 SubD 角被磨圆（离角 {0:0.###} > 0.5）", dPlain));
                }
            }
#endif
        }

        /// <summary>极限面（SubD.ToBrep）里离给定点最近的距离：用来验证角点有没有被磨掉</summary>
        static double LimitDistTo(SubD sd, Point3d p)
        {
#if RH7
            return double.MaxValue;     // Rhino 7 的 ToBrep 需要参数、且本机只编译不运行
#else
            if (sd == null) return double.MaxValue;
            try
            {
                Brep lb = sd.ToBrep();
                if (lb == null) return double.MaxValue;
                double best = double.MaxValue;
                for (int i = 0; i < lb.Vertices.Count; i++)
                {
                    Point3d q = lb.Vertices[i].Location;
                    double d = q.DistanceTo(p);
                    if (d < best) best = d;
                }
                return best;
            }
            catch { return double.MaxValue; }
#endif
        }

        /// <summary>网格顶点里最大的 |Z|（过渡宽度/平滑度这类「整体起伏被压了多少」的量）</summary>
        static double MaxAbsZ(Mesh m)
        {
            if (m == null) return 0;
            double mx = 0;
            for (int i = 0; i < m.Vertices.Count; i++) mx = Math.Max(mx, Math.Abs(m.Vertices.Point3dAt(i).Z));
            return mx;
        }

        /// <summary>
        /// 过渡带内（离边界 lo~hi 之间）的最大 |Z|：过渡宽度 / 过渡平滑度这类参数要看这里
        /// （整体最大 |Z| 由内部「全幅」顶点主导，看不出过渡的变化）。
        /// </summary>
        static double BandAbsZ(Mesh m, double lo, double hi)
        {
            if (m == null) return 0;
            Polyline[] naked = null;
            try { naked = m.GetNakedEdges(); } catch { }
            if (naked == null || naked.Length == 0) return MaxAbsZ(m);
            double mx = 0;
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3d p = m.Vertices.Point3dAt(i);
                double d = WaterRippleCore.DistToPolylines(naked, p);
                if (d < lo || d > hi) continue;
                mx = Math.Max(mx, Math.Abs(p.Z));
            }
            return mx;
        }

        /// <summary>网格特征值：顶点数 + 平均 |Z| + 过渡带幅度（密度 / 起伏 / 过渡任一变化都会变）</summary>
        static double SigOf(Brep target, WaterRippleSettings s)
        {
            try
            {
                string rep;
                WaterRippleResult r = WaterRippleCore.Generate(target, s, out rep);
                if (r == null || r.Ripple == null || r.Ripple.Vertices.Count == 0) return double.NaN;
                double sum = 0;
                for (int i = 0; i < r.Ripple.Vertices.Count; i++) sum += Math.Abs(r.Ripple.Vertices.Point3dAt(i).Z);
                double band = BandAbsZ(r.Ripple, 0.0, 8.0);
                return r.Ripple.Vertices.Count * 1000.0 + sum / r.Ripple.Vertices.Count + band * 10.0;
            }
            catch { return double.NaN; }
        }

        /// <summary>从面板 Settings 里按行名读当前值（接线自检用）</summary>
        static double ParamValue(WaterRipplePanel panel, string name)
        {
            if (panel == null) return double.NaN;
            WaterRippleSettings s = panel.Settings;
            switch (name)
            {
                case "波长": return s.Wavelength;
                case "波高": return s.WaveHeight;
                case "波数": return s.WaveCount;
                case "主方向": return s.Direction;
                case "方向散布": return s.Spread;
                case "波峰形状": return s.Crest;
                case "每波长分段": return s.SamplesPerWave;
                case "过渡宽度": return s.Fade;
                case "过渡平滑度": return s.BlendSmooth;
                case "随机种子": return s.Seed;
            }
            return double.NaN;
        }

        /// <summary>网格位移的特征值（用来比较三种波形确实不同）：按位置加权的 Z 采样和</summary>
        static double RippleSignature(Mesh m)
        {
            double sum = 0;
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3d p = m.Vertices.Point3dAt(i);
                sum += p.Z * (1.0 + 0.001 * p.X) * (1.0 + 0.002 * p.Y);
            }
            return m.Vertices.Count > 0 ? sum / m.Vertices.Count : 0;
        }

        static long Q3(Point3d p)
        {
            long a = (long)Math.Round(p.X * 1e6), b = (long)Math.Round(p.Y * 1e6), c = (long)Math.Round(p.Z * 1e6);
            unchecked { return (a * 73856093L) ^ (b * 19349663L) ^ (c * 83492791L); }
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
