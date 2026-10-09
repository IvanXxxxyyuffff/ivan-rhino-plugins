using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;

namespace RadialDotsPattern
{
    /// <summary>
    /// 无人值守自检。用法：命令 RadialDotsSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放
    /// run-radialdots-selftest.flag 后重启 Rhino（跑完自动写报告并退出）。
    /// </summary>
    public class RadialDotsSelfTestCommand : Command
    {
        public override string EnglishName { get { return "RadialDotsSelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\RadialDotsSelfTest.txt");
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
            sb.AppendLine("=== 径向渐变圆点 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                Case1_Basic(sb, ref pass, ref fail);
                Case2_MergeOverlap(sb, ref pass, ref fail);
                Case3_NoOverlap(sb, ref pass, ref fail);
                Case4_Shapes(sb, ref pass, ref fail);
                Case5_Layouts(sb, ref pass, ref fail);
                Case6_PanelSmoke(sb, ref pass, ref fail);
                Case7_UserSpiral(sb, ref pass, ref fail);
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

        static RadialDotSettings Base()
        {
            var s = new RadialDotSettings();
            s.MmToModel = 1.0;
            s.OuterR = 60.0; s.InnerR = 26.0; s.Pitch = 6.0;
            s.MaxDia = 5.0; s.MinDia = 0.0; s.Peak = 0.3; s.Falloff = 1.0;
            s.Layout = 0; s.Shape = 0; s.Stagger = true; s.Jitter = 0.0; s.Seed = 1;
            return s;
        }

        // ---------------------------------------------------------------- 用例1：基本生成 + 渐变方向
        static void Case1_Basic(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例1：基本生成（外 R60 / 内 R26 / 间距 6 / 直径 0~5 / 峰值 0.3）---");
            RadialDotSettings s = Base();
            s.Merge = false;                     // 先看单个图形
            RadialDotResult r = RadialDots.Generate(Plane.WorldXY, s);
            sb.AppendLine(string.Format("  图形 {0} 个，输出 {1} 条曲线{2}",
                r.Dots, r.Count, string.IsNullOrEmpty(r.Note) ? "" : "；note=" + r.Note));
            Check(sb, ref pass, ref fail, r.Dots > 100 && r.Count == r.Dots,
                string.Format("生成了足够多的图形（{0} 个）", r.Dots));
            if (r.Dots == 0) return;

            int closed = 0, outOfBand = 0, tooBig = 0, offPlane = 0;
            double peakR = s.InnerR + s.Peak * (s.OuterR - s.InnerR);
            double sumPeak = 0; int nPeak = 0;
            double maxDia = 0;
            var byR = new List<double[]>();          // {半径, 直径}
            foreach (Curve c in r.Curves)
            {
                if (c.IsClosed) closed++;
                BoundingBox bb = c.GetBoundingBox(false);
                if (!bb.IsValid) continue;
                Point3d cen = bb.Center;
                double rad = Math.Sqrt(cen.X * cen.X + cen.Y * cen.Y);
                if (rad < s.InnerR - 0.5 || rad > s.OuterR + 0.5) outOfBand++;
                if (Math.Abs(cen.Z) > 1e-6) offPlane++;
                double dia = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
                if (dia > maxDia) maxDia = dia;
                if (dia > s.MaxDia + 0.05) tooBig++;
                byR.Add(new double[] { rad, dia });
                if (Math.Abs(rad - peakR) < 3.0) { sumPeak += dia; nPeak++; }
            }
            // 外缘用「半径最大的那 10%」量：环是离散的（26+6k），固定半径窗口可能一个都取不到
            double sumOuter = 0; int nOuter = 0;
            if (byR.Count >= 10)
            {
                byR.Sort((a, b) => a[0].CompareTo(b[0]));
                nOuter = Math.Max(3, byR.Count / 10);
                for (int k = byR.Count - nOuter; k < byR.Count; k++) sumOuter += byR[k][1];
            }
            double meanPeak = nPeak > 0 ? sumPeak / nPeak : 0;
            double meanOuter = nOuter > 0 ? sumOuter / nOuter : 0;
            sb.AppendLine(string.Format("  峰值环(r≈{0:0.#}) 平均直径 {1:0.###}（{2} 个）；最外 {3} 个平均直径 {4:0.###}；最大直径 {5:0.###}",
                peakR, meanPeak, nPeak, nOuter, meanOuter, maxDia));
            Check(sb, ref pass, ref fail, closed == r.Count, string.Format("所有图形闭合（{0}/{1}）", closed, r.Count));
            Check(sb, ref pass, ref fail, outOfBand == 0, string.Format("图形中心都落在半径带内（越界 {0} 个）", outOfBand));
            Check(sb, ref pass, ref fail, offPlane == 0, string.Format("图形都落在生成平面上（离面 {0} 个）", offPlane));
            Check(sb, ref pass, ref fail, tooBig == 0, string.Format("没有超过最大直径的图形（超 {0} 个）", tooBig));
            Check(sb, ref pass, ref fail, meanPeak > meanOuter * 3.0 && nPeak > 3 && nOuter > 3,
                string.Format("尺寸按半径渐变：峰值处明显大于外缘（{0:0.###} vs {1:0.###}）", meanPeak, meanOuter));
            Check(sb, ref pass, ref fail, maxDia > s.MaxDia * 0.85,
                string.Format("峰值处真的达到了最大直径（{0:0.###} ≈ {1}）", maxDia, s.MaxDia));

            // 同一 seed 结果可复现
            RadialDotResult r2 = RadialDots.Generate(Plane.WorldXY, s);
            Check(sb, ref pass, ref fail, r2.Dots == r.Dots,
                string.Format("同参数结果可复现（{0} = {1}）", r2.Dots, r.Dots));
        }

        // ---------------------------------------------------------------- 用例2：重叠自动布尔合并
        static void Case2_MergeOverlap(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例2：重叠自动布尔合并（直径 3~9 > 间距 6，必然相交）---");
            RadialDotSettings s = Base();
            s.MaxDia = 9.0; s.MinDia = 3.0;
            s.Merge = false;
            RadialDotResult off = RadialDots.Generate(Plane.WorldXY, s);
            s.Merge = true;
            RadialDotResult on = RadialDots.Generate(Plane.WorldXY, s);
            sb.AppendLine(string.Format("  不合并：{0} 个图形 -> {1} 条曲线；合并：{0} 个图形 -> {2} 条曲线（{3} 组、{4} 个并成一体）",
                on.Dots, off.Count, on.Count, on.MergedGroups, on.MergedDots));
            Check(sb, ref pass, ref fail, off.Dots > 100 && off.Count == off.Dots,
                string.Format("关掉合并时每个图形独立输出（{0} 个 = {1} 条）", off.Dots, off.Count));
            Check(sb, ref pass, ref fail, on.Dots == off.Dots,
                string.Format("开关合并不改变图形数量（{0}）", on.Dots));
            Check(sb, ref pass, ref fail, on.MergedGroups > 0,
                string.Format("检测到重叠并合并（{0} 组，{1} 个图形）", on.MergedGroups, on.MergedDots));
            Check(sb, ref pass, ref fail, on.Count < on.Dots,
                string.Format("输出曲线数少于图形数（{0} < {1}）", on.Count, on.Dots));

            double lenOff = 0, lenOn = 0;
            foreach (Curve c in off.Curves) lenOff += c.GetLength();
            foreach (Curve c in on.Curves) lenOn += c.GetLength();
            sb.AppendLine(string.Format("  总周长：不合并 {0:0.#} mm -> 合并 {1:0.#} mm（合并掉重叠部分）", lenOff, lenOn));
            Check(sb, ref pass, ref fail, lenOn < lenOff * 0.95,
                string.Format("合并后的总周长明显变小（{0:0.#} < {1:0.#}）", lenOn, lenOff));

            // 合并结果仍然闭合、且都在平面上
            int closed = 0, offPlane = 0;
            foreach (Curve c in on.Curves)
            {
                if (c.IsClosed) closed++;
                BoundingBox bb = c.GetBoundingBox(false);
                if (bb.IsValid && Math.Abs(bb.Center.Z) > 1e-6) offPlane++;
            }
            Check(sb, ref pass, ref fail, closed == on.Count,
                string.Format("合并结果全部闭合（{0}/{1}）", closed, on.Count));
            Check(sb, ref pass, ref fail, offPlane == 0, "合并结果都落在生成平面上");

            // 回归：带边缘的图形缩到 0 会被跳过，此时分组不能用「未跳过的点集」（曾经越界 IndexOutOfRange）
            RadialDotSettings s2 = Base();
            s2.MaxDia = 9.0; s2.MinDia = 0.0; s2.Merge = true;
            RadialDotResult r2 = RadialDots.Generate(Plane.WorldXY, s2);
            sb.AppendLine(string.Format("  回归（最小直径 0，边缘图形被跳过）：图形 {0} -> 输出 {1}，合并组 {2}",
                r2.Dots, r2.Count, r2.MergedGroups));
            Check(sb, ref pass, ref fail, r2.Dots > 100 && r2.Count > 0,
                "有图形被跳过时也能正常合并（不越界）");
        }

        // ---------------------------------------------------------------- 用例3：没有交集就保持独立
        static void Case3_NoOverlap(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例3：没有交集时保持独立（直径固定 2，间距 6）---");
            RadialDotSettings s = Base();
            s.MaxDia = 2.0; s.MinDia = 2.0; s.Merge = true;
            RadialDotResult r = RadialDots.Generate(Plane.WorldXY, s);
            sb.AppendLine(string.Format("  图形 {0} 个 -> 输出 {1} 条曲线，合并组 {2}", r.Dots, r.Count, r.MergedGroups));
            Check(sb, ref pass, ref fail, r.Dots > 100, string.Format("生成了足够多的图形（{0}）", r.Dots));
            Check(sb, ref pass, ref fail, r.MergedGroups == 0, "没有交集时不合并");
            Check(sb, ref pass, ref fail, r.Count == r.Dots,
                string.Format("输出条数 = 图形数（{0} = {1}，各自独立）", r.Count, r.Dots));
        }

        // ---------------------------------------------------------------- 用例4：四种形状
        static void Case4_Shapes(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例4：图形形状（圆形 / 方形 / 三角形 / 六边形 / 圆方交替）---");
            for (int sh = 0; sh < RadialDotSettings.ShapeNames.Length; sh++)
            {
                RadialDotSettings s = Base();
                s.Shape = sh; s.Merge = false;
                RadialDotResult r = RadialDots.Generate(Plane.WorldXY, s);
                int closed = 0;
                foreach (Curve c in r.Curves) if (c.IsClosed) closed++;
                Check(sb, ref pass, ref fail, r.Dots > 100 && closed == r.Count,
                    string.Format("{0}：{1} 个图形全部闭合（{2}）", RadialDotSettings.ShapeNames[sh], r.Dots, closed));
            }
        }

        // ---------------------------------------------------------------- 用例5：四种阵列方式
        static void Case5_Layouts(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例5：阵列方式（同心环 / 螺旋 / 方形网格 / 交错网格）---");
            for (int ly = 0; ly < RadialDotSettings.LayoutNames.Length; ly++)
            {
                RadialDotSettings s = Base();
                s.Layout = ly; s.Merge = false;
                RadialDotResult r = RadialDots.Generate(Plane.WorldXY, s);
                int bad = 0;
                foreach (Curve c in r.Curves)
                {
                    BoundingBox bb = c.GetBoundingBox(false);
                    if (!bb.IsValid) { bad++; continue; }
                    Point3d cen = bb.Center;
                    double rad = Math.Sqrt(cen.X * cen.X + cen.Y * cen.Y);
                    if (rad < s.InnerR - 0.5 || rad > s.OuterR + 0.5) bad++;
                }
                Check(sb, ref pass, ref fail, r.Dots > 100 && bad == 0,
                    string.Format("{0}：{1} 个图形，越界 {2} 个", RadialDotSettings.LayoutNames[ly], r.Dots, bad));
            }

            // 同心环的错位开关：关掉之后环与环的角向分布一致（点数会变，只要求都生成得出来）
            RadialDotSettings s2 = Base();
            s2.Stagger = false; s2.Merge = false;
            RadialDotResult r2 = RadialDots.Generate(Plane.WorldXY, s2);
            Check(sb, ref pass, ref fail, r2.Dots > 100,
                string.Format("关掉「相邻环错开半格」也能生成（{0} 个）", r2.Dots));
        }

        /// <summary>曲线等分采样（这个自检文件里自己带一份，不依赖别处的助手）</summary>
        static Point3d[] Sample48(Curve c, int n)
        {
            if (c == null) return null;
            double[] ts = c.DivideByCount(n, true);
            if (ts == null || ts.Length == 0) return null;
            var pts = new Point3d[ts.Length];
            for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
            return pts;
        }

        /// <summary>
        /// 输出里还有多少对曲线**真的相交**（= 该合并的没合并掉）。
        /// 不能只看外接圆/包围盒：布尔合并出来的大区域包围盒很大，会把附近一堆独立图形全算成「相交」（实测误报 554 对）。
        /// 这里用「一条曲线的采样点严格落在另一条曲线内部」（Curve.Contains，边界点算 Coincident 不算 Inside）判定。
        /// </summary>
        static int CountIntersectingPairs(List<Curve> curves, out int total, out double maxDepth)
        {
            maxDepth = 0;
            var pts = new List<Point3d[]>();
            var bbs = new List<BoundingBox>();
            var plane = Plane.WorldXY;
            foreach (Curve c in curves)
            {
                if (c == null) continue;
                Point3d[] sp = Sample48(c, 48);
                if (sp == null || sp.Length < 3) continue;
                pts.Add(sp);
                bbs.Add(c.GetBoundingBox(false));
                try { Plane pl; if (c.TryGetPlane(out pl, 1e-3)) plane = pl; } catch { }
            }
            total = pts.Count;
            int n = pts.Count, cnt = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (!bbs[i].IsValid || !bbs[j].IsValid) continue;
                    if (bbs[i].Max.X < bbs[j].Min.X || bbs[j].Max.X < bbs[i].Min.X ||
                        bbs[i].Max.Y < bbs[j].Min.Y || bbs[j].Max.Y < bbs[i].Min.Y) continue;   // 包围盒不相交 -> 不可能有交
                    double depth = 0;
                    for (int k = 0; k < pts[i].Length; k++)
                    {
                        if (curves[j] == null) continue;
                        if (curves[j].Contains(pts[i][k], plane, 0.05) != PointContainment.Inside) continue;
                        double t;
                        if (curves[j].ClosestPoint(pts[i][k], out t)) depth = Math.Max(depth, pts[i][k].DistanceTo(curves[j].PointAt(t)));
                    }
                    for (int k = 0; k < pts[j].Length; k++)
                    {
                        if (curves[i] == null) continue;
                        if (curves[i].Contains(pts[j][k], plane, 0.05) != PointContainment.Inside) continue;
                        double t;
                        if (curves[i].ClosestPoint(pts[j][k], out t)) depth = Math.Max(depth, pts[j][k].DistanceTo(curves[i].PointAt(t)));
                    }
                    if (depth > maxDepth) maxDepth = depth;
                    if (depth > 0.05) cnt++;      // 侵入超过 0.05mm 才算「明显叠在一起」（相切/亚微米级接触不算）
                }
            return cnt;
        }

        // ---------------------------------------------------------------- 用例7：用户实测场景（螺旋 + 小间距）
        // 用户报：「有些场景下 布尔运算失效了 输出的圆圈依然有交集」
        // 参数就按他截图里的那组来（外 R15 / 内 R5 / 间距 0.81 / 直径 0.65~1.16 / 峰值 0.48 / 衰减 1.2 / 螺旋）
        static void Case7_UserSpiral(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例7：用户实测场景（螺旋，958 个图形那种密度）---");
            RadialDotSettings s = Base();
            s.OuterR = 15.0; s.InnerR = 5.0; s.Pitch = 0.81;
            s.MaxDia = 1.16; s.MinDia = 0.65; s.Peak = 0.48; s.Falloff = 1.2;
            s.Layout = 1; s.Shape = 0; s.Merge = true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            RadialDotResult r = RadialDots.Generate(Plane.WorldXY, s);
            sw.Stop();
            int total; double maxDepth;
            int bad = CountIntersectingPairs(r.Curves, out total, out maxDepth);
            sb.AppendLine(string.Format("  图形 {0} -> 输出 {1} 条；合并组 {2}（{3} 个并成一体）；耗时 {4} ms",
                r.Dots, r.Count, r.MergedGroups, r.MergedDots, sw.ElapsedMilliseconds));
            sb.AppendLine(string.Format("  输出里仍然互相叠进去（>0.05mm）的对数：{0} / 共 {1} 条曲线；最大侵入深度 {2:0.####} mm",
                bad, total, maxDepth));
            // 收尾清理（FinalCleanup）自己的修复前后对比 + 诊断：回归时看这一行就能判断「还剩几对、卡在哪一步」
            sb.AppendLine(string.Format("  收尾清理前后对比：交叠对数 修复前 {0} -> 修复后 {1}；最大侵入 修复前 {2:0.####} -> 修复后 {3:0.####} mm",
                r.PairsBefore, r.PairsAfter, r.MaxDepthBefore, r.MaxDepthAfter));
            sb.AppendLine(string.Format("  自检独立复核：{0} 对（Core 自报修复后 {1} 对{2}）",
                bad, r.PairsAfter, bad == r.PairsAfter ? "，一致" : "，不一致"));
            if (!string.IsNullOrEmpty(r.Diag)) sb.AppendLine("  " + r.Diag);
            if (!string.IsNullOrEmpty(r.Note)) sb.AppendLine("  note=" + r.Note);
            Check(sb, ref pass, ref fail, r.Dots > 800, string.Format("图形数符合预期（{0}）", r.Dots));
            Check(sb, ref pass, ref fail, r.MergedGroups > 0,
                string.Format("这么密的图形确实检测到重叠并合并（{0} 组）", r.MergedGroups));
            Check(sb, ref pass, ref fail, r.PairsAfter <= r.PairsBefore,
                string.Format("收尾清理没有让交叠变多（修复前 {0} 对 -> 修复后 {1} 对）", r.PairsBefore, r.PairsAfter));
            Check(sb, ref pass, ref fail, bad == 0,
                string.Format("输出里没有互相交叠的图形（还有 {0} 对，最大侵入 {1:0.####}mm）", bad, maxDepth));
        }

        // ---------------------------------------------------------------- 用例6：面板冒烟
        static void Case6_PanelSmoke(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine();
            sb.AppendLine("--- 用例6：参数面板冒烟（构造 + 切换 + 整窗重绘两遍 + 关闭路径）---");
            try
            {
                RadialDotSettings s = Base();
                using (var p = new RadialDotsPanel(s, "冒烟测试"))
                {
                    bool ok = p.Controls.Count >= 5 && p.ClientSize.Width > 300 && p.ClientSize.Height > 400;
                    Check(sb, ref pass, ref fail, ok,
                        string.Format("面板构造成功（{0} 个控件，{1}x{2}）", p.Controls.Count, p.ClientSize.Width, p.ClientSize.Height));

                    // 找到两个分段控件：第一个 = 阵列方式，第二个 = 图形形状
                    var segs = new List<IvanUi.IvanSegmented>();
                    foreach (Control c in p.Controls)
                        foreach (Control k in c.Controls)
                        {
                            var sg = k as IvanUi.IvanSegmented;
                            if (sg != null) segs.Add(sg);
                        }
                    Check(sb, ref pass, ref fail, segs.Count >= 2,
                        string.Format("面板里有阵列方式 / 图形形状两个分段控件（{0} 个）", segs.Count));
                    if (segs.Count >= 2)
                    {
                        segs[0].SelectedIndex = 3;
                        segs[1].SelectedIndex = 4;
                        Check(sb, ref pass, ref fail, p.Settings.Layout == 3 && p.Settings.Shape == 4,
                            string.Format("切换写回参数（Layout = {0}，Shape = {1}）", p.Settings.Layout, p.Settings.Shape));
                        segs[0].SelectedIndex = 0;
                        segs[1].SelectedIndex = 0;
                    }

                    try
                    {
                        p.Show();
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(420);   // 等进入动画跑完
                        Application.DoEvents();
                        var rect = new Rectangle(0, 0, p.ClientSize.Width, p.ClientSize.Height);
                        string png = Path.Combine(Path.GetTempPath(), "RadialDotsPanel-smoke.png");
                        using (var bmp = new Bitmap(p.ClientSize.Width, p.ClientSize.Height))
                        {
                            p.DrawToBitmap(bmp, rect);
                            p.DrawToBitmap(bmp, rect);
                            bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        Check(sb, ref pass, ref fail, true, "面板整窗重绘两遍无异常（截图 " + png + "）");
                    }
                    catch (Exception ex)
                    {
                        Check(sb, ref pass, ref fail, false, "面板重绘异常：" + ex.Message);
                    }

                    try
                    {
                        p.Close();
                        var sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        while (!p.IsDisposed && sw.ElapsedMilliseconds < 3000)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        }
                        Check(sb, ref pass, ref fail, p.IsDisposed,
                            string.Format("关闭动画跑完后窗体真的关掉了（{0} ms）", sw.ElapsedMilliseconds));
                    }
                    catch (Exception ex)
                    {
                        Check(sb, ref pass, ref fail, false, "关闭路径异常：" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Check(sb, ref pass, ref fail, false, "面板冒烟异常：" + ex.Message);
            }
        }
    }
}
