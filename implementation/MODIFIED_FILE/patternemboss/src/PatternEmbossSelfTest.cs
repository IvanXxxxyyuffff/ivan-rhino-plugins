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

namespace PatternEmboss
{
    /// <summary>
    /// 无人值守自检。用法：命令 PatternEmbossSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放
    /// run-emboss-selftest.flag 后重启 Rhino（跑完自动写报告并退出）。
    /// </summary>
    public class PatternEmbossSelfTestCommand : Command
    {
        public override string EnglishName { get { return "PatternEmbossSelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\PatternEmbossSelfTest.txt");
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
            sb.AppendLine("=== 图案压花 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                Case1_PlateHex(sb, ref pass, ref fail);
                Case2_SphereQuad(sb, ref pass, ref fail);
                Case3_RhombusNeg(sb, ref pass, ref fail);
                Case4_MarginFade(sb, ref pass, ref fail);
                Case5_Deterministic(sb, ref pass, ref fail);
                Case6_PanelSmoke(sb, ref pass, ref fail);
                Case7_Thickness(sb, ref pass, ref fail);
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

        static PatternEmbossSettings Base()
        {
            var s = new PatternEmbossSettings();
            s.MmToModel = 1.0;
            s.Pattern = 0; s.CellSize = 4.0; s.Height = 0.4; s.Groove = 0.8;
            s.Roundness = 0.7; s.Margin = 1.0; s.Density = 10; s.Thickness = 0.0;
            return s;
        }

        static Brep MakePlate()
        {
            var pl = new PolylineCurve(new Point3d[] { new Point3d(0, 0, 0), new Point3d(60, 0, 0), new Point3d(60, 40, 0), new Point3d(0, 40, 0), new Point3d(0, 0, 0) });
            Brep[] b = Brep.CreatePlanarBreps(new Curve[] { pl }, 1e-6);
            return (b != null && b.Length > 0) ? b[0] : null;
        }

        static Brep MakeSphere(double r)
        {
            try { return Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, r))); }
            catch { return null; }
        }

        static double MeshHash(Mesh m)
        {
            double h = 0;
            if (m == null) return double.NaN;
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3d p = m.Vertices.Point3dAt(i);
                h += p.X * 0.13 + p.Y * 0.27 + p.Z * 0.41 + i * 1e-9;
            }
            return h;
        }

        // ---------------------------------------------------------------- 用例1：平板 + 六边形（基本生成 + 高度范围）
        static void Case1_PlateHex(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例1：平板 + 六边形（生成 + 高度范围） ---");
            Brep plate = MakePlate();
            Check(sb, ref pass, ref fail, plate != null, "构造：60×40 平板");
            if (plate == null) return;

            var s = Base();
            s.Pattern = 0;
            string rep;
            PatternEmbossResult r = PatternEmbossCore.Generate(plate, s, out rep);
            Check(sb, ref pass, ref fail, r.Mesh != null && r.Faces > 0,
                string.Format(CultureInfo.InvariantCulture, "生成成功（{0} 顶点 / {1} 面）", r.Vertices, r.Faces));
            if (r.Mesh == null || r.Faces == 0) return;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      [info] 高度 {0:0.####}~{1:0.####} · 面积 {2:0.#} · {3}", r.MinH, r.MaxH, r.Area, string.IsNullOrEmpty(r.Note) ? "" : r.Note));
            Check(sb, ref pass, ref fail, Math.Abs(r.MaxH - 0.4) < 0.04,
                string.Format(CultureInfo.InvariantCulture, "凸起峰值 ≈ 高度参数 0.4（实测 {0:0.####}）", r.MaxH));
            Check(sb, ref pass, ref fail, Math.Abs(r.MinH) < 0.04,
                string.Format(CultureInfo.InvariantCulture, "槽底贴面 ≈ 0（实测 {0:0.####}）", r.MinH));
            Check(sb, ref pass, ref fail, r.Mesh.IsValid, "网格有效性 IsValid");
            // 3D 表面积口径：纹理化后表面积只增不减（凸起/凹槽的侧面贡献），增幅应在合理范围
            Check(sb, ref pass, ref fail, r.Area >= 2400 * 0.99 && r.Area <= 2400 * 1.3,
                string.Format(CultureInfo.InvariantCulture, "纹理化表面积 ≥ 平板且增幅合理（{0:0.#} vs 2400，+{1:0.0%}）", r.Area, (r.Area - 2400) / 2400.0));
        }

        // ---------------------------------------------------------------- 用例2：球面 + 四边形（法向偏移正确性）
        static void Case2_SphereQuad(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例2：球面 R30 + 四边形（法向偏移） ---");
            Brep sphere = MakeSphere(30.0);
            Check(sb, ref pass, ref fail, sphere != null, "构造：球面 R=30");
            if (sphere == null) return;

            var s = Base();
            s.Pattern = 1; s.CellSize = 6.0; s.Height = 0.5;
            string rep;
            PatternEmbossResult r = PatternEmbossCore.Generate(sphere, s, out rep);
            Check(sb, ref pass, ref fail, r.Mesh != null && r.Faces > 0,
                string.Format(CultureInfo.InvariantCulture, "生成成功（{0} 顶点 / {1} 面）", r.Vertices, r.Faces));
            if (r.Mesh == null || r.Faces == 0) return;

            // 所有顶点应落在 [R-groove 下界容差, R+height+容差] 的壳层内（凹槽贴面 → 半径 ≈R）
            double minR = double.MaxValue, maxR = 0;
            for (int i = 0; i < r.Mesh.Vertices.Count; i++)
            {
                double d = r.Mesh.Vertices.Point3dAt(i).DistanceTo(Point3d.Origin);
                if (d < minR) minR = d;
                if (d > maxR) maxR = d;
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      [info] 顶点半径范围 {0:0.###}~{1:0.###}（R=30）", minR, maxR));
            Check(sb, ref pass, ref fail, maxR <= 30.0 + 0.5 + 0.05 && maxR > 30.2,
                string.Format(CultureInfo.InvariantCulture, "外凸顶点半径 ≈ R+0.5（实测最大 {0:0.###}）", maxR));
            Check(sb, ref pass, ref fail, minR >= 30.0 - 0.05 && minR < 30.0 + 0.2,
                string.Format(CultureInfo.InvariantCulture, "槽底顶点半径 ≈ R（实测最小 {0:0.###}）", minR));
        }

        // ---------------------------------------------------------------- 用例3：菱形 + 负高（全凹陷）
        static void Case3_RhombusNeg(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例3：平板 + 菱形（负高 = 全凹陷） ---");
            Brep plate = MakePlate();
            Check(sb, ref pass, ref fail, plate != null, "构造：60×40 平板");
            if (plate == null) return;

            var s = Base();
            s.Pattern = 2; s.Height = -0.3; s.CellSize = 5.0;
            string rep;
            PatternEmbossResult r = PatternEmbossCore.Generate(plate, s, out rep);
            Check(sb, ref pass, ref fail, r.Mesh != null && r.Faces > 0, "生成成功");
            if (r.Mesh == null || r.Faces == 0) return;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      [info] 高度 {0:0.####}~{1:0.####}", r.MinH, r.MaxH));
            Check(sb, ref pass, ref fail, r.MaxH <= 0.001,
                string.Format(CultureInfo.InvariantCulture, "负高模式全部 ≤ 0（实测最大 {0:0.####}）", r.MaxH));
            Check(sb, ref pass, ref fail, r.MinH < -0.25,
                string.Format(CultureInfo.InvariantCulture, "凹槽最深处 ≈ -0.3（实测 {0:0.####}）", r.MinH));
        }

        // ---------------------------------------------------------------- 用例4：边缘留边渐隐
        static void Case4_MarginFade(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例4：边缘留边（Margin=3mm 边界渐隐） ---");
            Brep plate = MakePlate();
            Check(sb, ref pass, ref fail, plate != null, "构造：60×40 平板");
            if (plate == null) return;

            var s = Base();
            s.Margin = 3.0;
            string rep;
            PatternEmbossResult r = PatternEmbossCore.Generate(plate, s, out rep);
            Check(sb, ref pass, ref fail, r.Mesh != null && r.Faces > 0, "生成成功");
            if (r.Mesh == null || r.Faces == 0) return;

            // 到边界 0.5mm 以内的顶点：高度应 < 0.1（3mm 留边的 1/6 处，线性渐隐 ≈ 0.4*0.17=0.067）
            double worstEdge = 0;
            for (int i = 0; i < r.Mesh.Vertices.Count; i++)
            {
                Point3d p = r.Mesh.Vertices.Point3dAt(i);
                double edgeDist = Math.Min(Math.Min(p.X, 60 - p.X), Math.Min(p.Y, 40 - p.Y));
                if (edgeDist < 0.5 && Math.Abs(p.Z) > worstEdge) worstEdge = Math.Abs(p.Z);
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "      [info] 贴边带（0.5mm）最大高度 {0:0.####}", worstEdge));
            Check(sb, ref pass, ref fail, worstEdge < 0.1,
                string.Format(CultureInfo.InvariantCulture, "边界带高度渐隐到 <0.1（实测 {0:0.####}）", worstEdge));

            // 中央区域仍有满高（不被留边误伤）
            double centerMax = 0;
            for (int i = 0; i < r.Mesh.Vertices.Count; i++)
            {
                Point3d p = r.Mesh.Vertices.Point3dAt(i);
                if (Math.Abs(p.X - 30) < 10 && Math.Abs(p.Y - 20) < 8 && p.Z > centerMax) centerMax = p.Z;
            }
            Check(sb, ref pass, ref fail, centerMax > 0.3,
                string.Format(CultureInfo.InvariantCulture, "中央区域满高保留（实测 {0:0.####}）", centerMax));
        }

        // ---------------------------------------------------------------- 用例5：确定性
        static void Case5_Deterministic(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例5：确定性（同参数两次结果一致） ---");
            Brep plate = MakePlate();
            Check(sb, ref pass, ref fail, plate != null, "构造：60×40 平板");
            if (plate == null) return;

            var s = Base();
            string rep;
            PatternEmbossResult r1 = PatternEmbossCore.Generate(plate, s, out rep);
            PatternEmbossResult r2 = PatternEmbossCore.Generate(plate, s, out rep);
            bool same = r1.Mesh != null && r2.Mesh != null && r1.Vertices == r2.Vertices && r1.Faces == r2.Faces
                && Math.Abs(MeshHash(r1.Mesh) - MeshHash(r2.Mesh)) < 1e-6;
            Check(sb, ref pass, ref fail, same,
                string.Format(CultureInfo.InvariantCulture, "两次生成一致（{0}v/{1}f vs {2}v/{3}f，哈希差 {4:0.#####}）",
                    r1.Vertices, r1.Faces, r2.Vertices, r2.Faces, Math.Abs(MeshHash(r1.Mesh) - MeshHash(r2.Mesh))));
        }

        // ---------------------------------------------------------------- 用例6：面板烟测
        static void Case6_PanelSmoke(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例6：面板烟测 ---");
            PatternEmbossPanel panel = null;
            try
            {
                panel = new PatternEmbossPanel(new PatternEmbossSettings(), "自检目标");
                Check(sb, ref pass, ref fail, true, "面板：构造成功（无异常）");
                panel.SetTarget("曲面目标：2 个");
                Check(sb, ref pass, ref fail, true, "面板：SetTarget 可调用");
                panel.SetInfo("测试");
                Check(sb, ref pass, ref fail, panel.Settings != null && panel.Settings.CellSize > 0, "面板：Settings 可读");
            }
            catch (Exception ex)
            {
                Check(sb, ref pass, ref fail, false, "面板：构造异常 " + ex.Message);
            }
            finally
            {
                try { if (panel != null) panel.Close(); } catch { }
                try { if (panel != null) panel.Dispose(); } catch { }
            }
        }

        // ---------------------------------------------------------------- 用例7：加厚
        static void Case7_Thickness(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例7：加厚（Thickness=1mm） ---");
            Brep plate = MakePlate();
            Check(sb, ref pass, ref fail, plate != null, "构造：60×40 平板");
            if (plate == null) return;

            var s = Base();
            s.Thickness = 1.0;
            string rep;
            PatternEmbossResult r = PatternEmbossCore.Generate(plate, s, out rep);
            Check(sb, ref pass, ref fail, r.Mesh != null && r.Faces > 0,
                string.Format(CultureInfo.InvariantCulture, "生成成功（{0} 顶点 / {1} 面，Solid={2}）", r.Vertices, r.Faces, r.Solid));
            if (r.Mesh == null || r.Faces == 0) return;
            sb.AppendLine("      [info] " + (string.IsNullOrEmpty(r.Note) ? "（无附注）" : r.Note));
            // 加厚成功 → 顶点数应显著多于面片网格（背面 + 侧壁）；失败回退也允许，但必须有说明
            Check(sb, ref pass, ref fail, r.Solid || r.Note.Contains("回退"),
                "加厚成功 OR 明确回退说明（不静默丢壳）");
        }
    }
}
