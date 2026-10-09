using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace IvanMeshFix
{
    /// <summary>
    /// 无人值守自检。用法：命令 MeshFixSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放
    /// run-meshfix-selftest.flag 后启动 Rhino（跑完自动写报告并退出）。
    /// </summary>
    public class MeshFixSelfTestCommand : Command
    {
        public override string EnglishName { get { return "MeshFixSelfTest"; } }

        /// <summary>用户实际出问题的文件（诊断确认：渲染网格 3 顶点 / 1 面）</summary>
        internal const string RealFile = @"D:\UserData\Desktop\11.3dm";

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\MeshFixSelfTest.txt");
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
            sb.AppendLine("=== 网格修复 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                Case1_Detection(sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case2_HealthyUntouched(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case3_SyntheticTrimmed(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case4_RealFile(sb, ref pass, ref fail, ref skip);
                Flush(reportPath, sb, pass, fail, skip);
                Case5_WholeDocScan(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case6_DisplayVisibility(doc, sb, ref pass, ref fail, ref skip);
                Flush(reportPath, sb, pass, fail, skip);
            }
            catch (Exception ex)
            {
                sb.AppendLine("[FAIL] 自检异常: " + ex);
                fail++;
            }

            sb.AppendLine();
            if (skip > 0) sb.AppendLine("跳过: " + skip + " 项（文件/环境不具备，不算失败）");
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

        /// <summary>用例1：退化 / 健康 / 空网格的判定（纯函数）</summary>
        static void Case1_Detection(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例1：退化网格判定 ---");

            var tiny = new Mesh();
            tiny.Vertices.Add(0.0, 0.0, 0.0);
            tiny.Vertices.Add(1e-4, 0.0, 0.0);
            tiny.Vertices.Add(0.0, 1e-4, 0.0);
            tiny.Faces.AddFace(0, 1, 2);
            int f; double a;
            bool broken = MeshFixCore.MeshesLookBroken(new Mesh[] { tiny }, out f, out a);
            Check(sb, ref pass, ref fail, broken,
                string.Format(CultureInfo.InvariantCulture, "退化网格（1 面 / 面积 {0:0.########}）判定为需要修复", a));

            Mesh sphere = Mesh.CreateFromSphere(new Sphere(Plane.WorldXY, 10.0), 24, 24);
            bool healthyBroken = MeshFixCore.MeshesLookBroken(new Mesh[] { sphere }, out f, out a);
            Check(sb, ref pass, ref fail, !healthyBroken,
                string.Format(CultureInfo.InvariantCulture, "健康球面网格（{0} 面 / 面积 {1:0.##}）判定为正常", f, a));

            bool emptyBroken = MeshFixCore.MeshesLookBroken(new Mesh[0], out f, out a);
            Check(sb, ref pass, ref fail, emptyBroken, "一个渲染网格都没有 → 判定为需要重建");
        }

        /// <summary>用例2：健康的物件一概不动（不写自定义参数、不重建）</summary>
        static void Case2_HealthyUntouched(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例2：健康物件不被误改 ---");
            Guid id = Guid.Empty;
            try
            {
                id = doc.Objects.AddBrep(new Sphere(Plane.WorldXY, 12.0).ToBrep());
                RhinoObject o = doc.Objects.FindId(id);
                if (o == null) { Check(sb, ref pass, ref fail, false, "球面加入文档失败"); return; }
                try { o.CreateMeshes(MeshType.Render, o.GetRenderMeshParameters(), false); } catch { }

                int f0; double a0;
                bool broken = MeshFixCore.RenderMeshLooksBroken(o, out f0, out a0);
                Check(sb, ref pass, ref fail, !broken,
                    string.Format(CultureInfo.InvariantCulture, "球面渲染网格健康（{0} 面 / 面积 {1:0.##}）", f0, a0));

                MeshFixCore.FixStats st = MeshFixCore.Run(doc, new RhinoObject[] { o });
                Check(sb, ref pass, ref fail, st.Candidates == 0 && st.Fixed == 0 && st.Failed == 0,
                    string.Format("一键修复对它没动手（退化 0 / 修复 0 / 失败 0，扫描 {0}）", st.Scanned));
#if RH7
                sb.AppendLine("[INFO] Rhino 7 的 RhinoCommon 没有物件级自定义网格参数，跳过「未写入自定义参数」检查");
#else
                Check(sb, ref pass, ref fail, o.Attributes.CustomMeshingParameters == null,
                    "健康物件没有被写入自定义渲染网格参数");
#endif
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { if (id != Guid.Empty) { try { doc.Objects.Delete(id, true); } catch { } } }
        }

        /// <summary>
        /// 用例3：合成「外环 1 + 内环 43」的修剪曲面，用「快速」预设参数（最大长宽比 0）网格化。
        /// 断言的是版本无关的不变量：跑完一键修复后网格必须健康 —— 退化了就必须被修好，
        /// 本来就正常（实测本机 542 面 / 面积 8478）就必须一点都不动它。
        /// </summary>
        static void Case3_SyntheticTrimmed(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例3：长宽比 0 的合成修剪曲面（退化则修复 / 健康则不误改）---");
            Guid id = Guid.Empty;
            try
            {
                int innerLoops;
                Brep plate = BuildHoleyPlate(doc.ModelAbsoluteTolerance, out innerLoops);
                if (plate == null) { Check(sb, ref pass, ref fail, false, "合成修剪曲面失败"); return; }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  构造：1 个外环 + {0} 个内环，面积 {1:0.##}", innerLoops, plate.GetArea()));

                id = doc.Objects.AddBrep(plate);
                RhinoObject o = doc.Objects.FindId(id);
                if (o == null) { Check(sb, ref pass, ref fail, false, "修剪曲面加入文档失败"); return; }

                // 出问题的条件：物件用「快速」预设的渲染网格参数（最大长宽比 = 0）
                MeshingParameters mp = FastParams(doc);
                mp.GridAspectRatio = 0.0;
                bool wrote = MeshFixCore.WriteCustomParams(o, mp);
                MeshFixCore.RebuildMesh(o, mp, !wrote);

                int f0; double a0;
                bool broken = MeshFixCore.RenderMeshLooksBroken(o, out f0, out a0);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  实测（最大长宽比 0）：{0} 面 / 面积 {1:0.########}", f0, a0));

                MeshFixCore.FixStats st = MeshFixCore.Run(doc, new RhinoObject[] { o });
                int f1; double a1;
                bool still = MeshFixCore.RenderMeshLooksBroken(o, out f1, out a1);
                Check(sb, ref pass, ref fail, !still,
                    string.Format(CultureInfo.InvariantCulture,
                        "跑完一键修复后网格是健康的：{0} 面 → {1} 面（面积 {2:0.####}）", f0, f1, a1));

                if (broken)
                {
                    Check(sb, ref pass, ref fail, st.Fixed == 1, string.Format("退化件被修复（修复计数 = {0}，期望 1）", st.Fixed));
#if RH7
                    sb.AppendLine("[INFO] Rhino 7：走「按长宽比 6 重建渲染网格」的路径，无物件级参数可断言");
#else
                    MeshingParameters after = o.Attributes.CustomMeshingParameters;
                    Check(sb, ref pass, ref fail,
                        after != null && Math.Abs(after.GridAspectRatio - MeshFixCore.FixedAspectRatio) < 1e-9,
                        "物件级参数已写成最大长宽比 6（随 3dm 保存）");
#endif
                }
                else
                {
                    Check(sb, ref pass, ref fail, st.Candidates == 0 && st.Fixed == 0,
                        string.Format("这个合成件本来就网格正常 → 一键修复没动它（退化 {0} / 修复 {1}）", st.Candidates, st.Fixed));
                    sb.AppendLine("[INFO] 长宽比 0 只在真实复杂修剪曲面上触发退化（真实复现见用例4）；本用例退化为「健康件不被误改」的回归位");
                }
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { if (id != Guid.Empty) { try { doc.Objects.Delete(id, true); } catch { } } }
        }

        /// <summary>用例4：用户真实 3dm 实跑（复制到临时目录再打开 → 找退化件 → 修复 → 复查 → 幂等）</summary>
        static void Case4_RealFile(StringBuilder sb, ref int pass, ref int fail, ref int skip)
        {
            sb.AppendLine("--- 用例4：用户 3dm 实跑 ---");
            if (!File.Exists(RealFile)) { sb.AppendLine("[SKIP] 找不到 " + RealFile); skip++; return; }
            long origLen = new FileInfo(RealFile).Length;

            // 复制一份再打开：既不会碰到用户原文件（只读打开，绝不写回），
            // 也避开「文件正被另一个 Rhino 打开」的 .rhl 锁。
            string copy = Path.Combine(Path.GetTempPath(), "MeshFix-11-copy.3dm");
            try { File.Copy(RealFile, copy, true); }
            catch (Exception ex) { sb.AppendLine("[SKIP] 复制文件失败：" + ex.Message); skip++; return; }

            RhinoDoc d = null;
            try { d = RhinoDoc.OpenHeadless(copy); }
            catch (Exception ex) { sb.AppendLine("[SKIP] 打不开副本：" + ex.Message); skip++; return; }
            if (d == null) { sb.AppendLine("[SKIP] 打不开副本（返回 null）"); skip++; return; }

            try
            {
                var objs = new List<RhinoObject>(d.Objects.GetObjectList(ObjectType.AnyObject));
                int brokenCount = 0, f0 = 0;
                double a0 = 0.0;
                RhinoObject first = null;
                foreach (RhinoObject o in objs)
                {
                    if (!MeshFixCore.CarriesRenderMesh(o)) continue;
                    int f; double a;
                    if (!MeshFixCore.RenderMeshLooksBroken(o, out f, out a)) continue;
                    brokenCount++;
                    if (first == null) { first = o; f0 = f; a0 = a; }
                }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  扫描 {0} 个物件，渲染网格退化 {1} 个", objs.Count, brokenCount));
                Check(sb, ref pass, ref fail, brokenCount >= 1, "文件里确实存在渲染网格退化的物件");
                if (first == null) return;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  第一个退化件：{0}，{1} 面 / 面积 {2:0.########}", MeshFixCore.Describe(first), f0, a0));

                MeshFixCore.FixStats st = MeshFixCore.Run(d, new RhinoObject[] { first });
                int f1; double a1;
                bool still = MeshFixCore.RenderMeshLooksBroken(first, out f1, out a1);
                Check(sb, ref pass, ref fail, st.Fixed == 1 && !still,
                    string.Format(CultureInfo.InvariantCulture,
                        "修复后渲染网格恢复：{0} 面 → {1} 面（面积 {2:0.####}）", f0, f1, a1));

                MeshFixCore.FixStats again = MeshFixCore.Run(d, new RhinoObject[] { first });
                Check(sb, ref pass, ref fail, again.Candidates == 0 && again.Fixed == 0,
                    "对同一个物件再跑一次：退化 0 / 修复 0（幂等，不会反复写参数）");

                Check(sb, ref pass, ref fail,
                    File.Exists(RealFile) && new FileInfo(RealFile).Length == origLen,
                    string.Format("原文件仍是 {0} 字节（只读副本上做修复，没写回原文件）", origLen));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally
            {
                try { d.Modified = false; } catch { }
            }
        }

        /// <summary>用例5：没有退化件时，一键修复（全文档扫描路径）不报错也不乱改</summary>
        static void Case5_WholeDocScan(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例5：全文档扫描（未选中物件时的一键路径）---");
            Guid id = Guid.Empty;
            try
            {
                id = doc.Objects.AddBrep(new Sphere(Plane.WorldXY, 8.0).ToBrep());
                RhinoObject o = doc.Objects.FindId(id);
                try { o.CreateMeshes(MeshType.Render, o.GetRenderMeshParameters(), false); } catch { }

                var all = new List<RhinoObject>();
                var objs = doc.Objects.GetObjectList(ObjectType.AnyObject);
                if (objs != null) all.AddRange(objs);

                MeshFixCore.FixStats st = MeshFixCore.Run(doc, all);
                Check(sb, ref pass, ref fail, st.Candidates == 0 && st.Failed == 0,
                    string.Format("全量扫描 {0} 个物件：退化 0、失败 0（曲线/网格物件不误判）", st.Scanned));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { if (id != Guid.Empty) { try { doc.Objects.Delete(id, true); } catch { } } }
        }

        /// <summary>
        /// 用例6（用户实测口径 · 验收）：真实文件的几何 → 当前文档（文档级用「快速」预设 = 最大长宽比 0，
        /// 物件不带自定义参数，与出问题的文件同状态）→ 真实 Rendered 视图截图。
        /// 判据是**浅中性灰像素 = 着色面体**（诊断成功图 214/217/219，背景 157,163,170）——
        /// 只数「非背景像素」会把线框也算进去、证明不了面体显示（踩过）。
        /// 用户报障「点了之后还是不可见」的根因：只写参数 + CreateMeshes + Redraw 时显示管线一直用旧的
        /// 退化网格 → 必须先清网格缓存（ClearAllMeshes），显示才会按新参数重新网格化（实测 0 → 24331 像素）。
        /// </summary>
        static void Case6_DisplayVisibility(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail, ref int skip)
        {
            sb.AppendLine("--- 用例6：显示可见性（真渲染截图，判据 = 浅中性灰着色面体）---");
            if (!File.Exists(RealFile)) { sb.AppendLine("[SKIP] 找不到 " + RealFile); skip++; return; }
            string copy = Path.Combine(Path.GetTempPath(), "MeshFix-11-display.3dm");
            try { File.Copy(RealFile, copy, true); }
            catch (Exception ex) { sb.AppendLine("[SKIP] 复制失败：" + ex.Message); skip++; return; }

            string tmp = Path.GetTempPath();
            Guid id = Guid.Empty;
            try
            {
                Brep geom = null;
                RhinoDoc src = null;
                try { src = RhinoDoc.OpenHeadless(copy); } catch { }
                if (src == null) { sb.AppendLine("[SKIP] 打不开副本"); skip++; return; }
                foreach (RhinoObject o in src.Objects.GetObjectList(ObjectType.AnyObject))
                {
                    if (o == null || !MeshFixCore.CarriesRenderMesh(o)) continue;
                    try { geom = o.DuplicateGeometry() as Brep; } catch { }
                    if (geom != null) break;
                }
                if (geom == null) { sb.AppendLine("[SKIP] 副本里没有可用的 Brep"); skip++; return; }

                // 文档级「快速」预设 + 物件不带自定义参数 = 出问题文件的状态
                try { doc.MeshingParameterStyle = MeshingParameterStyle.Fast; } catch { }
                id = doc.Objects.AddBrep(geom);
                RhinoObject obj = doc.Objects.FindId(id);
                if (obj == null) { sb.AppendLine("[SKIP] 加入当前文档失败"); skip++; return; }
                MeshingParameters eff = obj.GetRenderMeshParameters();
                try { obj.CreateMeshes(MeshType.Render, eff, false); } catch { }
                int f0; double a0;
                MeshFixCore.RenderMeshLooksBroken(obj, out f0, out a0);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  初始状态：文档级 aspect={0:0.##}，渲染网格 {1} 面 / 面积 {2:0.########}", eff.GridAspectRatio, f0, a0));

                RhinoView view = doc.Views.ActiveView;
                if (view == null) { sb.AppendLine("[SKIP] 没有活动视图"); skip++; return; }
                try
                {
                    DisplayModeDescription dm = DisplayModeDescription.FindByName("Rendered");
                    if (dm == null) dm = DisplayModeDescription.FindByName("Shaded");
                    if (dm != null) view.ActiveViewport.DisplayMode = dm;
                    sb.AppendLine("  显示模式：" + view.ActiveViewport.DisplayMode.EnglishName);
                }
                catch { }
                try { view.ActiveViewport.ZoomBoundingBox(obj.Geometry.GetBoundingBox(true)); } catch { }

                int s0 = Shot(view, Path.Combine(tmp, "MeshFix-11-before.png"));
                sb.AppendLine("  修复前：着色面体像素 " + s0 + "（0 = 只剩边缘线、面体不显示）");

                MeshFixCore.FixStats st = MeshFixCore.Run(doc, new RhinoObject[] { obj });
                foreach (string line in st.Lines) sb.AppendLine("  " + line);
                sb.AppendLine("  清网格缓存：" + (st.CacheCleared ? "成功" : "未执行/失败"));

                int s1 = Shot(view, Path.Combine(tmp, "MeshFix-11-after.png"));
                sb.AppendLine("  修复后：着色面体像素 " + s1);

                if (s0 < 0 || s1 < 0) { sb.AppendLine("[SKIP] 截图失败（拿不到位图）"); skip++; return; }
                Check(sb, ref pass, ref fail, st.Fixed == 1 && st.Failed == 0,
                    string.Format("修复计数：修复 {0} / 失败 {1}（期望 1 / 0）", st.Fixed, st.Failed));

                if (s1 <= 20000)
                {
                    // 截图环境自检：放一个必然可见的球，缩放到它。连球都拍不到着色像素，
                    // 说明这个窗口拍不了真渲染（隐藏窗口 / 无 OpenGL 上下文）→ 按 SKIP 处理，不算失败。
                    int ctrl = ControlCapture(view, doc, sb);
                    if (ctrl <= 5000)
                    {
                        sb.AppendLine("[SKIP] 本窗口拍不到真渲染（对照球着色像素 " + ctrl + "）：显示验收需要在**可见**的 Rhino 窗口里跑，不是修复失败");
                        skip++;
                        return;
                    }
                    sb.AppendLine("  对照球着色像素 " + ctrl + "（截图环境正常）");
                }

                Check(sb, ref pass, ref fail, s1 > 20000 && s1 > s0 * 4,
                    string.Format(CultureInfo.InvariantCulture,
                        "修复后面体在 Rendered 视图里真的着色显示：着色像素 {0} → {1}", s0, s1));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { if (id != Guid.Empty) { try { doc.Objects.Delete(id, true); } catch { } } }
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>截图环境自检：临时放一个球并缩放到它，返回它的着色像素（拍不到说明窗口不能真渲染）</summary>
        static int ControlCapture(RhinoView view, RhinoDoc doc, StringBuilder sb)
        {
            Guid gid = Guid.Empty;
            try
            {
                gid = doc.Objects.AddBrep(new Sphere(Plane.WorldXY, 20.0).ToBrep());
                RhinoObject o = doc.Objects.FindId(gid);
                if (o == null) return -1;
                try { o.CreateMeshes(MeshType.Render, o.GetRenderMeshParameters(), false); } catch { }
                try { view.ActiveViewport.ZoomBoundingBox(o.Geometry.GetBoundingBox(true)); } catch { }
                return Shot(view, Path.Combine(Path.GetTempPath(), "MeshFix-11-control.png"));
            }
            catch (Exception ex) { sb.AppendLine("  对照球异常：" + ex.Message); return -1; }
            finally { if (gid != Guid.Empty) { try { doc.Objects.Delete(gid, true); } catch { } } }
        }

        /// <summary>命令级真渲染截图（连拍两张取后一张）→ 返回「浅中性灰着色面体」像素数</summary>
        static int Shot(RhinoView view, string path)
        {
            try
            {
                try { view.Redraw(); } catch { }
                try { RhinoApp.Wait(); } catch { }
                string first = path.Replace(".png", "-a.png");
                RhinoApp.RunScript(string.Format("_-ViewCaptureToFile \"{0}\" _Enter", first), false);
                try { RhinoApp.Wait(); } catch { }
                RhinoApp.RunScript(string.Format("_-ViewCaptureToFile \"{0}\" _Enter", path), false);
                try { RhinoApp.Wait(); } catch { }
                if (!File.Exists(path)) return -1;
                using (Bitmap bmp = new Bitmap(path))
                {
                    // 只数「浅中性灰」= 着色面体（默认材质）。线框是黑线、背景是蓝灰 (157,163,170)，
                    // 都不满足「三通道接近且够亮」，所以这个判据能区分「面体着色」和「只剩线框」。
                    int w = bmp.Width, h = bmp.Height, count = 0;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            Color c = bmp.GetPixel(x, y);
                            int mx = Math.Max(c.R, Math.Max(c.G, c.B));
                            int mn = Math.Min(c.R, Math.Min(c.G, c.B));
                            if (mx - mn <= 8 && mn >= 190) count++;
                        }
                    return count;
                }
            }
            catch (Exception ex) { RhinoApp.WriteLine("截图失败：" + ex.Message); return -1; }
        }

        /// <summary>「快速」预设的渲染网格参数（最大长宽比就是 0 —— 出问题的条件）</summary>
        static MeshingParameters FastParams(RhinoDoc doc)
        {
            MeshingParameters mp = null;
            try { mp = doc.GetMeshingParameters(MeshingParameterStyle.Fast); } catch { }
            if (mp == null) { try { mp = MeshingParameters.FastRenderMesh; } catch { } }
            if (mp == null) { try { mp = doc.GetCurrentMeshingParameters(); } catch { } }
            if (mp == null) mp = new MeshingParameters(1.0);
            return mp;
        }

        /// <summary>一块 100x100 的平面，挖 43 个圆孔（外环 1 + 内环 43，与出问题的物件同型）</summary>
        static Brep BuildHoleyPlate(double tol, out int innerLoops)
        {
            innerLoops = 0;
            Plane pl = Plane.WorldXY;
            Brep baseBrep = Brep.CreateFromSurface(new PlaneSurface(pl, new Interval(-50.0, 50.0), new Interval(-50.0, 50.0)));
            if (baseBrep == null) return null;

            var cutters = new List<Curve>();
            for (int i = 0; i < 43; i++)
            {
                double x = -40.0 + (i % 8) * 11.0;
                double y = -40.0 + (i / 8) * 11.0;
                cutters.Add(new Circle(pl, new Point3d(x, y, 0.0), 3.4).ToNurbsCurve());
            }

            // Split 返回的是「一个 Brep、里面多张面」：挖孔后面积最大的那张面就是带内环的板
            Brep split = baseBrep.Faces[0].Split(cutters, tol);
            Brep best = null;
            double bestArea = -1.0;
            if (split != null)
            {
                for (int k = 0; k < split.Faces.Count; k++)
                {
                    Brep piece = null;
                    double a = 0.0;
                    try
                    {
                        piece = split.Faces[k].DuplicateFace(true);
                        if (piece == null || piece.Faces.Count == 0) continue;
                        AreaMassProperties amp = AreaMassProperties.Compute(piece);
                        if (amp != null) a = amp.Area;
                    }
                    catch { continue; }
                    if (a > bestArea) { bestArea = a; best = piece; }
                }
            }
            if (best == null) return null;
            try
            {
                foreach (BrepLoop lp in best.Loops)
                    if (lp.LoopType == BrepLoopType.Inner) innerLoops++;
            }
            catch { }
            return best;
        }

        /// <summary>每跑完一个用例就把报告落盘（万一后面某步卡死，报告里能看到卡在哪）</summary>
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
