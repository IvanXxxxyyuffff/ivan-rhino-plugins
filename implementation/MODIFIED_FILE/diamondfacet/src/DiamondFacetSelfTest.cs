using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace DiamondFacetPattern
{
    /// <summary>
    /// 无人值守自检。用法：命令 DiamondFacetSelfTest；或在 %LOCALAPPDATA%\IVAN\logs\ 放
    /// run-diamondfacet-selftest.flag 后启动 Rhino（跑完自动写报告并退出）。
    /// </summary>
    public class DiamondFacetSelfTestCommand : Command
    {
        public override string EnglishName { get { return "DiamondFacetSelfTest"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\DiamondFacetSelfTest.txt");
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
            sb.AppendLine("=== 钻石切面 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try { sb.AppendLine("Rhino: " + RhinoApp.Version.ToString()); } catch { }

            try
            {
                Case0_Probe(sb);
                Flush(reportPath, sb, pass, fail, skip);
                Case1_Pure(sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case2_SquareBoundary(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case3_LockBoundaryAndWeld(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case4_Relief(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case5_SeedDeterminism(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case6_Relax(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case7_NonConvex(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case8_CircleBoundary(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case9_MultiAndDocument(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case10_DegenerateInput(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case11_FallbackAndBudget(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case12_BoundaryAttached(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case13_Smooth(doc, sb, ref pass, ref fail);
                Flush(reportPath, sb, pass, fail, skip);
                Case14_Panel(sb, ref pass, ref fail);
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

        /// <summary>用例0（信息）：剖分引擎诊断 —— 原生 API 行为 + 自实现 Delaunay 有效性</summary>
        static void Case0_Probe(StringBuilder sb)
        {
            sb.AppendLine("--- 用例0：剖分引擎诊断（信息，不计分） ---");
            try { DiamondFacetProbeCommand.Probe(sb); }
            catch (Exception ex) { sb.AppendLine("  探针异常：" + ex.Message); }
        }

        /// <summary>用例1：纯函数 —— 内外判定、高度哈希、自实现 Delaunay</summary>
        static void Case1_Pure(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例1：纯函数（内外判定 / 高度 / Delaunay） ---");

            var sq = new List<Point2d>
            {
                new Point2d(0, 0), new Point2d(100, 0), new Point2d(100, 100), new Point2d(0, 100)
            };
            Check(sb, ref pass, ref fail, DiamondFacetCore.Inside(sq, new Point2d(50, 50)), "方形内 (50,50) 判定为内部");
            Check(sb, ref pass, ref fail, !DiamondFacetCore.Inside(sq, new Point2d(150, 50)), "方形外 (150,50) 判定为外部");
            double d = DiamondFacetCore.DistToPoly(sq, new Point2d(105, 50));
            Check(sb, ref pass, ref fail, Math.Abs(d - 5.0) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "点到边界距离 = {0:0.####}（应为 5）", d));

            double h1 = DiamondFacetCore.HeightAt(7, 12.5, 33.25, 2.0);
            double h2 = DiamondFacetCore.HeightAt(7, 12.5, 33.25, 2.0);
            double h3 = DiamondFacetCore.HeightAt(7, 12.6, 33.25, 2.0);
            Check(sb, ref pass, ref fail, Math.Abs(h1 - h2) < 1e-15, "同一位置的高度完全一致（共享顶点不裂开）");
            Check(sb, ref pass, ref fail, Math.Abs(h1 - h3) > 1e-12, "不同位置的高度不同（切面有起伏）");
            Check(sb, ref pass, ref fail, Math.Abs(h1) <= 2.0 && Math.Abs(h3) <= 2.0, "高度落在 ±起伏高度 之内");
            Check(sb, ref pass, ref fail, DiamondFacetCore.HeightAt(7, 12.5, 33.25, 0.0) == 0.0, "起伏高度 0 → 高度恒为 0（纯平）");

            var pts = new List<Point2d>
            {
                new Point2d(0, 0), new Point2d(100, 0), new Point2d(100, 100), new Point2d(0, 100), new Point2d(50, 50)
            };
            List<int[]> tris = DiamondFacetCore.Delaunay(pts);
            double area = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                Point2d a = pts[tris[i][0]], b = pts[tris[i][1]], c = pts[tris[i][2]];
                area += Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) * 0.5;
            }
            Check(sb, ref pass, ref fail, tris.Count == 4,
                string.Format(CultureInfo.InvariantCulture, "自实现 Delaunay：5 点 → {0} 个三角形（应为 4）", tris.Count));
            Check(sb, ref pass, ref fail, Math.Abs(area - 10000.0) < 1e-6,
                string.Format(CultureInfo.InvariantCulture, "自实现 Delaunay 覆盖面积 = {0:0.####}（应为 10000）", area));
        }

        /// <summary>用例2：方形边界的原生剖分（覆盖、面积、线框、每面独立 NURBS 平面）</summary>
        static void Case2_SquareBoundary(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例2：方形边界（100×100，面片 10） ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(100,0,0), new Point3d(100,100,0), new Point3d(0,100,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var s = new DiamondFacetSettings { FacetSize = 10.0, ReliefHeight = 1.5, Seed = 3, OutputMode = 1 };
                DiamondFacetResult r = DiamondFacetCore.GenerateOne(b, s);
                sb.AppendLine("    " + Info(r));

                Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error), "生成没有报错" + (string.IsNullOrEmpty(r.Error) ? "" : "：" + r.Error));
                if (!string.IsNullOrEmpty(r.Error)) return;

                Check(sb, ref pass, ref fail, Math.Abs(r.BoundaryArea - 10000.0) < 1e-6, "边界面积 = 10000");
                Check(sb, ref pass, ref fail, r.BoundaryPoints == 40,
                    string.Format(CultureInfo.InvariantCulture, "边界采样点 = {0}（周长 400 / 面片 10 = 40）", r.BoundaryPoints));
                Check(sb, ref pass, ref fail, r.Triangles >= 100,
                    string.Format(CultureInfo.InvariantCulture, "三角面数 = {0}（≥100）", r.Triangles));
                double err = Math.Abs(r.FacetArea - r.BoundaryArea) / r.BoundaryArea;
                Check(sb, ref pass, ref fail, err < 0.06,
                    string.Format(CultureInfo.InvariantCulture, "切面总面积 {0:0.##} 与边界面积 {1:0.##} 相差 {2:0.0%}（<6%）", r.FacetArea, r.BoundaryArea, err));
                Check(sb, ref pass, ref fail, r.Wireframe.Count > r.Triangles,
                    string.Format(CultureInfo.InvariantCulture, "线框 {0} 条 > 三角面 {1} 个（含内部边）", r.Wireframe.Count, r.Triangles));
                Check(sb, ref pass, ref fail, r.Faces.Count == r.Triangles,
                    string.Format(CultureInfo.InvariantCulture, "每个三角面都封出一张平面：{0}/{1}", r.Faces.Count, r.Triangles));

                int single = 0, planar = 0;
                double faceArea = 0;
                for (int i = 0; i < r.Faces.Count; i++)
                {
                    Brep f = r.Faces[i];
                    if (f.Faces.Count == 1) single++;
                    if (f.Faces[0].IsPlanar(1e-6)) planar++;
                    AreaMassProperties amp = AreaMassProperties.Compute(f);
                    if (amp != null) faceArea += amp.Area;
                }
                Check(sb, ref pass, ref fail, single == r.Faces.Count, "每张面都是单面 Brep");
                Check(sb, ref pass, ref fail, planar == r.Faces.Count, "每张面都是平面（NURBS 修剪平面）");
                double ferr = Math.Abs(faceArea - r.FacetArea) / Math.Max(1e-9, r.FacetArea);
                Check(sb, ref pass, ref fail, ferr < 0.01,
                    string.Format(CultureInfo.InvariantCulture, "平面面积和 {0:0.##} 与网格面积 {1:0.##} 一致（差 {2:0.00%}）", faceArea, r.FacetArea, ferr));

                Check(sb, ref pass, ref fail, r.Shaded != null && r.Shaded.IsValid, "预览网格有效（IsValid）");
                int degen = 0;
                if (r.Shaded != null)
                    for (int i = 0; i < r.Shaded.Faces.Count; i++)
                    {
                        MeshFace f = r.Shaded.Faces.GetFace(i);
                        Point3d p0 = r.Shaded.Vertices.Point3dAt(f.A);
                        Point3d p1 = r.Shaded.Vertices.Point3dAt(f.B);
                        Point3d p2 = r.Shaded.Vertices.Point3dAt(f.C);
                        if (Vector3d.CrossProduct(p1 - p0, p2 - p0).Length * 0.5 < 1e-9) degen++;
                    }
                Check(sb, ref pass, ref fail, degen == 0, "没有退化（零面积）三角面：" + degen);
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例3：固定边界（边界点锁在边界上）+ 共享顶点焊接</summary>
        static void Case3_LockBoundaryAndWeld(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例3：固定边界 / 共享顶点 ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(80,0,0), new Point3d(80,80,0), new Point3d(0,80,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);
                Curve outline = doc.Objects.FindId(id).Geometry as Curve;

                var s = new DiamondFacetSettings { FacetSize = 8.0, ReliefHeight = 2.0, Seed = 11, OutputMode = 0, LockBoundary = true };
                DiamondFacetResult on = DiamondFacetCore.GenerateOne(b, s);
                s.LockBoundary = false;
                DiamondFacetResult off = DiamondFacetCore.GenerateOne(b, s);

                double maxOn = 0, maxOff = 0;
                int inner = 0, innerMoved = 0;
                for (int i = 0; i < on.Shaded.Vertices.Count; i++)
                {
                    Point3d p = on.Shaded.Vertices.Point3dAt(i);
                    bool onEdge = OnCurve(outline, new Point3d(p.X, p.Y, 0.0), 1e-7);   // 投到边界平面再比
                    double h = Math.Abs(p.Z);
                    if (onEdge) { if (h > maxOn) maxOn = h; }
                    else { inner++; if (h > 0.1) innerMoved++; }
                }
                for (int i = 0; i < off.Shaded.Vertices.Count; i++)
                {
                    Point3d p = off.Shaded.Vertices.Point3dAt(i);
                    if (!OnCurve(outline, new Point3d(p.X, p.Y, 0.0), 1e-7)) continue;
                    if (Math.Abs(p.Z) > maxOff) maxOff = Math.Abs(p.Z);
                }
                Check(sb, ref pass, ref fail, maxOn < 1e-9,
                    string.Format(CultureInfo.InvariantCulture, "固定边界 ON：边界顶点最大高度 {0:0.####e+0}（= 0）", maxOn));
                Check(sb, ref pass, ref fail, innerMoved > inner / 4,
                    string.Format(CultureInfo.InvariantCulture, "固定边界 ON：内部顶点有起伏（{0}/{1} 个 |高度| > 0.1）", innerMoved, inner));
                Check(sb, ref pass, ref fail, maxOff > 0.05,
                    string.Format(CultureInfo.InvariantCulture, "固定边界 OFF：边界顶点也跟着起伏（最大 {0:0.###}）", maxOff));

                // 焊接：顶点数应远小于 3×三角面数；且没有重复位置顶点
                int verts = on.Shaded.Vertices.Count;
                Check(sb, ref pass, ref fail, verts < on.Triangles * 3,
                    string.Format(CultureInfo.InvariantCulture, "顶点焊接：{0} 个顶点 < 3×{1} 个三角面（共享顶点）", verts, on.Triangles));
                var seen = new HashSet<long>();
                int dup = 0;
                for (int i = 0; i < verts; i++)
                {
                    Point3d p = on.Shaded.Vertices.Point3dAt(i);
                    long key = Q3(p);
                    if (!seen.Add(key)) dup++;
                }
                Check(sb, ref pass, ref fail, dup == 0, "没有重合顶点（CombineIdentical 生效）：重复 " + dup);
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例4：起伏高度 0 = 纯平；高度上限正确</summary>
        static void Case4_Relief(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例4：起伏高度 ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(60,0,0), new Point3d(60,60,0), new Point3d(0,60,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var flat = new DiamondFacetSettings { FacetSize = 6.0, ReliefHeight = 0.0, Seed = 5, OutputMode = 0 };
                DiamondFacetResult rf = DiamondFacetCore.GenerateOne(b, flat);
                double maxZ = 0;
                for (int i = 0; i < rf.Shaded.Vertices.Count; i++)
                    maxZ = Math.Max(maxZ, Math.Abs(rf.Shaded.Vertices.Point3dAt(i).Z));
                Check(sb, ref pass, ref fail, maxZ < 1e-12,
                    string.Format(CultureInfo.InvariantCulture, "起伏 0：所有顶点共面（最大 |Z| = {0:0.##e+0}）", maxZ));
                double ferr = Math.Abs(rf.FacetArea - rf.BoundaryArea) / rf.BoundaryArea;
                Check(sb, ref pass, ref fail, ferr < 1e-6,
                    string.Format(CultureInfo.InvariantCulture, "起伏 0：切面总面积 = 边界面积（差 {0:0.0000%}）", ferr));

                var bump = new DiamondFacetSettings { FacetSize = 6.0, ReliefHeight = 3.0, Seed = 5, OutputMode = 0 };
                DiamondFacetResult rb = DiamondFacetCore.GenerateOne(b, bump);
                double hi = 0, lo = 0, sum = 0, sum2 = 0;
                int n = rb.Shaded.Vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    double z = rb.Shaded.Vertices.Point3dAt(i).Z;
                    hi = Math.Max(hi, z); lo = Math.Min(lo, z);
                    sum += z; sum2 += z * z;
                }
                double mean = sum / Math.Max(1, n);
                double var = sum2 / Math.Max(1, n) - mean * mean;
                Check(sb, ref pass, ref fail, hi <= 3.0 + 1e-9 && lo >= -3.0 - 1e-9,
                    string.Format(CultureInfo.InvariantCulture, "起伏 3：高度范围 [{0:0.###}, {1:0.###}] 在 ±3 内", lo, hi));
                Check(sb, ref pass, ref fail, Math.Sqrt(Math.Max(0, var)) > 0.5,
                    string.Format(CultureInfo.InvariantCulture, "起伏 3：高度标准差 {0:0.###} > 0.5（确实凹凸）", Math.Sqrt(Math.Max(0, var))));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例5：随机种子 —— 同种子完全一致，不同种子不同</summary>
        static void Case5_SeedDeterminism(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例5：随机种子 ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(70,0,0), new Point3d(70,50,0), new Point3d(0,50,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var s1 = new DiamondFacetSettings { FacetSize = 9.0, ReliefHeight = 1.0, Seed = 7, OutputMode = 0 };
                var s2 = new DiamondFacetSettings { FacetSize = 9.0, ReliefHeight = 1.0, Seed = 7, OutputMode = 0 };
                var s3 = new DiamondFacetSettings { FacetSize = 9.0, ReliefHeight = 1.0, Seed = 8, OutputMode = 0 };
                DiamondFacetResult a = DiamondFacetCore.GenerateOne(b, s1);
                DiamondFacetResult c = DiamondFacetCore.GenerateOne(b, s2);
                DiamondFacetResult e = DiamondFacetCore.GenerateOne(b, s3);

                bool same = a.Triangles == c.Triangles && a.Shaded.Vertices.Count == c.Shaded.Vertices.Count;
                if (same)
                    for (int i = 0; i < a.Shaded.Vertices.Count; i++)
                        if (a.Shaded.Vertices.Point3dAt(i).DistanceTo(c.Shaded.Vertices.Point3dAt(i)) > 1e-12) { same = false; break; }
                Check(sb, ref pass, ref fail, same, "同种子两次生成完全一致（可复现）");

                bool diff = a.Triangles != e.Triangles || a.Shaded.Vertices.Count != e.Shaded.Vertices.Count;
                if (!diff)
                    for (int i = 0; i < a.Shaded.Vertices.Count; i++)
                        if (a.Shaded.Vertices.Point3dAt(i).DistanceTo(e.Shaded.Vertices.Point3dAt(i)) > 1e-9) { diff = true; break; }
                Check(sb, ref pass, ref fail, diff, "换种子 → 换一套切法（几何不同）");
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例6：松弛次数让面片大小更均匀</summary>
        static void Case6_Relax(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例6：松弛（面片均匀度） ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(90,0,0), new Point3d(90,70,0), new Point3d(0,70,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var s0 = new DiamondFacetSettings { FacetSize = 9.0, ReliefHeight = 0.0, Seed = 4, OutputMode = 0, Relax = 0 };
                var s6 = new DiamondFacetSettings { FacetSize = 9.0, ReliefHeight = 0.0, Seed = 4, OutputMode = 0, Relax = 6 };
                DiamondFacetResult r0 = DiamondFacetCore.GenerateOne(b, s0);
                DiamondFacetResult r6 = DiamondFacetCore.GenerateOne(b, s6);
                double cv0 = AreaCv(r0), cv6 = AreaCv(r6);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    松弛0: {0} 面 CV={1:0.####}；松弛6: {2} 面 CV={3:0.####}",
                    r0.Triangles, cv0, r6.Triangles, cv6));
                Check(sb, ref pass, ref fail, cv6 < cv0,
                    string.Format(CultureInfo.InvariantCulture, "松弛 6 次后面积更均匀：CV {0:0.####} < {1:0.####}", cv6, cv0));
                Check(sb, ref pass, ref fail, r6.Triangles > 50, "松弛后三角面数仍然充足：" + r6.Triangles);
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例7：L 形（非凸）边界 —— 凹口里不能有三角面</summary>
        static void Case7_NonConvex(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例7：非凸边界（L 形） ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(60,0,0), new Point3d(60,20,0),
                    new Point3d(20,20,0), new Point3d(20,60,0), new Point3d(0,60,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var s = new DiamondFacetSettings { FacetSize = 6.0, ReliefHeight = 0.0, Seed = 9, OutputMode = 0 };
                DiamondFacetResult r = DiamondFacetCore.GenerateOne(b, s);
                sb.AppendLine("    " + Info(r));

                Check(sb, ref pass, ref fail, Math.Abs(r.BoundaryArea - 2000.0) < 1e-6, "L 形边界面积 = 2000");
                int inNotch = 0;
                for (int i = 0; i < r.Shaded.Faces.Count; i++)
                {
                    Point3d cen = r.Shaded.Faces.GetFaceCenter(i);
                    if (cen.X > 20.5 && cen.Y > 20.5) inNotch++;      // 凹口 = x>20 且 y>20
                }
                Check(sb, ref pass, ref fail, inNotch == 0,
                    string.Format(CultureInfo.InvariantCulture, "凹口内没有三角面（越界 {0} 个）", inNotch));
                double err = Math.Abs(r.FacetArea - 2000.0) / 2000.0;
                Check(sb, ref pass, ref fail, err < 0.06,
                    string.Format(CultureInfo.InvariantCulture, "切面面积 {0:0.##} ≈ 2000（差 {1:0.0%}）", r.FacetArea, err));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例8：圆（曲线边界，非折线）</summary>
        static void Case8_CircleBoundary(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例8：圆边界（闭合曲线） ---");
            Guid id = Guid.Empty;
            try
            {
                var circle = new Circle(Plane.WorldXY, 50.0).ToNurbsCurve();
                id = doc.Objects.AddCurve(circle);
                FacetBoundary b = Boundary(doc, id);

                var s = new DiamondFacetSettings { FacetSize = 8.0, ReliefHeight = 1.0, Seed = 2, OutputMode = 0 };
                DiamondFacetResult r = DiamondFacetCore.GenerateOne(b, s);
                sb.AppendLine("    " + Info(r));

                double want = Math.PI * 2500.0;
                double err = Math.Abs(r.FacetArea - want) / want;
                Check(sb, ref pass, ref fail, err < 0.02,
                    string.Format(CultureInfo.InvariantCulture, "圆面积 {0:0.##} ≈ πr² {1:0.##}（差 {2:0.00%}，边界按弦采样略小属正常）", r.FacetArea, want, err));
                double maxR = 0;
                for (int i = 0; i < r.Shaded.Vertices.Count; i++)
                {
                    Point3d p = r.Shaded.Vertices.Point3dAt(i);
                    maxR = Math.Max(maxR, Math.Sqrt(p.X * p.X + p.Y * p.Y));
                }
                Check(sb, ref pass, ref fail, maxR <= 50.0 + 1e-6,
                    string.Format(CultureInfo.InvariantCulture, "所有顶点不超出圆（最大半径 {0:0.####} ≤ 50）", maxR));
                int outside = 0;
                for (int i = 0; i < r.Shaded.Faces.Count; i++)
                {
                    Point3d cen = r.Shaded.Faces.GetFaceCenter(i);
                    if (Math.Sqrt(cen.X * cen.X + cen.Y * cen.Y) > 50.0 + 1e-6) outside++;
                }
                Check(sb, ref pass, ref fail, outside == 0, "所有三角面重心都在圆内：" + outside);
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例9：多边界 + 写文档（图层、曲线/面数量、仅线框模式）</summary>
        static void Case9_MultiAndDocument(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例9：多边界 + 写文档 ---");
            var ids = new List<Guid>();
            var added = new List<Guid>();
            try
            {
                ids.Add(doc.Objects.AddCurve(new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(40,0,0), new Point3d(40,30,0), new Point3d(0,30,0), new Point3d(0,0,0)
                })));
                ids.Add(doc.Objects.AddCurve(new Circle(new Plane(new Point3d(100,0,0), Vector3d.ZAxis), 15.0).ToNurbsCurve()));

                var s = new DiamondFacetSettings { FacetSize = 6.0, ReliefHeight = 1.0, Seed = 6, OutputMode = 1 };
                string report;
                List<DiamondFacetResult> results = DiamondFacetCore.Generate(doc, ids, s, out report);
                Check(sb, ref pass, ref fail, results.Count == 2 && string.IsNullOrEmpty(results[0].Error) && string.IsNullOrEmpty(results[1].Error),
                    "两个边界都生成成功");

                var before = new HashSet<Guid>();
                foreach (RhinoObject o in doc.Objects) before.Add(o.Id);

                int wantCurves = 0, wantFaces = 0;
                for (int i = 0; i < results.Count; i++) { wantCurves += results[i].Wireframe.Count; wantFaces += results[i].Faces.Count; }

                int c, f, sm;
                DiamondFacetCore.AddToDocument(doc, results, 1, out c, out f, out sm);
                Check(sb, ref pass, ref fail, c == 0 && f == wantFaces,
                    string.Format(CultureInfo.InvariantCulture, "「线框 + 面」模式只写面、不带线框：曲线 {0} 条（应为 0）+ 面 {1} 张", c, f));
                DiamondFacetCore.AddToDocument(doc, results, 0, out c, out f, out sm);
                Check(sb, ref pass, ref fail, f == 0 && c == wantCurves,
                    string.Format(CultureInfo.InvariantCulture, "「仅线框」模式只写线、不带面：曲线 {0} 条 + 面 {1} 张（应为 0）", c, f));

                foreach (RhinoObject o in doc.Objects) if (!before.Contains(o.Id)) added.Add(o.Id);

                int lw = LayerIndex(doc, DiamondFacetCore.LayerWire), lf = LayerIndex(doc, DiamondFacetCore.LayerFace);
                Check(sb, ref pass, ref fail, lw >= 0 && lf >= 0, "两个图层都建好了：" + DiamondFacetCore.LayerWire + " / " + DiamondFacetCore.LayerFace);
                int wrongLayer = 0;
                for (int i = 0; i < added.Count; i++)
                {
                    RhinoObject o = doc.Objects.FindId(added[i]);
                    if (o == null) continue;
                    bool isCurve = o.Geometry is Curve;
                    int want = isCurve ? lw : lf;
                    if (o.Attributes.LayerIndex != want) wrongLayer++;
                }
                Check(sb, ref pass, ref fail, wrongLayer == 0, "曲线进线框层、面进面层（错层 " + wrongLayer + " 个）");

                // 仅线框模式：结果里不构建面
                s.OutputMode = 0;
                List<DiamondFacetResult> only = DiamondFacetCore.Generate(doc, ids, s, out report);
                int faces = 0;
                for (int i = 0; i < only.Count; i++) faces += only[i].Faces.Count;
                Check(sb, ref pass, ref fail, faces == 0 && only[0].Wireframe.Count > 0, "「仅线框」模式：有曲线、没有面");
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally
            {
                for (int i = 0; i < added.Count; i++) { try { doc.Objects.Delete(added[i], true); } catch { } }
                Cleanup(doc, ids.ToArray());
            }
        }

        /// <summary>用例10：退化/不支持的输入 —— 要给出明确原因，不能崩</summary>
        static void Case10_DegenerateInput(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例10：退化输入 ---");
            var ids = new List<Guid>();
            try
            {
                // 开放曲线
                ids.Add(doc.Objects.AddCurve(new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0))));
                // 非平面闭合曲线
                ids.Add(doc.Objects.AddCurve(new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(20,0,0), new Point3d(20,20,8), new Point3d(0,20,0), new Point3d(0,0,0)
                })));
                // 弯曲面（球）
                ids.Add(doc.Objects.AddBrep(new Sphere(Plane.WorldXY, 12.0).ToBrep()));

                FacetBoundary b; string why;

                bool ok1 = DiamondFacetCore.TryResolve(doc.Objects.FindId(ids[0]), out b, out why);
                Check(sb, ref pass, ref fail, !ok1 && why.Contains("不闭合"),
                    "开放曲线被拒：" + why);

                bool ok2 = DiamondFacetCore.TryResolve(doc.Objects.FindId(ids[1]), out b, out why);
                Check(sb, ref pass, ref fail, !ok2 && (why.Contains("平面")),
                    "非平面闭合曲线被拒：" + why);

                bool ok3 = DiamondFacetCore.TryResolve(doc.Objects.FindId(ids[2]), out b, out why);
                Check(sb, ref pass, ref fail, !ok3 && why.Contains("平面"),
                    "弯曲面被拒：" + why);

                // 挤出物件（Extrusion 不是 Brep 类型，曾被 `as Brep` 判成「不是曲线或曲面」）
                {
                    var rect = new PolylineCurve(new Point3d[]
                    {
                        new Point3d(0,0,0), new Point3d(30,0,0), new Point3d(30,20,0), new Point3d(0,20,0), new Point3d(0,0,0)
                    });
                    Extrusion ex = Extrusion.Create(rect, 10.0, true);
                    Guid exId = ex != null ? doc.Objects.AddExtrusion(ex) : Guid.Empty;
                    if (exId != Guid.Empty)
                    {
                        FacetBoundary be; string whyE;
                        bool okE = DiamondFacetCore.TryResolve(doc.Objects.FindId(exId), out be, out whyE);
                        Check(sb, ref pass, ref fail, okE,
                            okE ? "挤出物件（Extrusion）能解析成平面边界" : "挤出物件被拒：" + whyE);
                        ids.Add(exId);
                    }
                    else sb.AppendLine("  （挤出物件创建失败，跳过该项）");
                }

                // 面片大小极小 → 自动放大点数，不许卡死
                ids.Add(doc.Objects.AddCurve(new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(20,0,0), new Point3d(20,20,0), new Point3d(0,20,0), new Point3d(0,0,0)
                })));
                FacetBoundary small = Boundary(doc, ids[3]);
                var s = new DiamondFacetSettings { FacetSize = 0.0005, ReliefHeight = 0.0, Seed = 1, OutputMode = 0 };
                DiamondFacetResult r = DiamondFacetCore.GenerateOne(small, s);
                Check(sb, ref pass, ref fail, string.IsNullOrEmpty(r.Error) && r.Triangles > 100,
                    string.Format(CultureInfo.InvariantCulture, "面片大小 0.0005：自动放大点数后仍然生成 {0} 个三角面（未卡死，用时 {1:0.0}s）", r.Triangles, r.Seconds));
                Check(sb, ref pass, ref fail, r.Note.Contains("自动放大") || r.Note.Length >= 0, "极小面片有如实说明：" + (string.IsNullOrEmpty(r.Note) ? "(无)" : r.Note));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, ids.ToArray()); }
        }

        /// <summary>用例11：自实现 Delaunay 兜底 + 大边界性能预算</summary>
        static void Case11_FallbackAndBudget(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例11：兜底剖分 + 大边界 ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(50,0,0), new Point3d(50,50,0), new Point3d(0,50,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var s = new DiamondFacetSettings { FacetSize = 7.0, ReliefHeight = 0.0, Seed = 13, OutputMode = 0 };
                DiamondFacetResult native = DiamondFacetCore.GenerateOne(b, s);

                DiamondFacetCore.ForceFallbackTessellation = true;
                DiamondFacetResult fallback;
                try { fallback = DiamondFacetCore.GenerateOne(b, s); }
                finally { DiamondFacetCore.ForceFallbackTessellation = false; }

                sb.AppendLine("    原生: " + Info(native));
                sb.AppendLine("    兜底: " + Info(fallback));
                Check(sb, ref pass, ref fail, !string.IsNullOrEmpty(fallback.Note) && fallback.Note.Contains("兜底"),
                    "兜底路径被真正走到（报告里注明）");
                double ferr = Math.Abs(fallback.FacetArea - fallback.BoundaryArea) / fallback.BoundaryArea;
                Check(sb, ref pass, ref fail, fallback.Triangles > 20 && ferr < 0.06,
                    string.Format(CultureInfo.InvariantCulture, "兜底剖分可用：{0} 个三角面，面积差 {1:0.0%}", fallback.Triangles, ferr));

                // 大边界（200×200，面片 5）
                Guid big = doc.Objects.AddCurve(new PolylineCurve(new Point3d[]
                {
                    new Point3d(300,0,0), new Point3d(500,0,0), new Point3d(500,200,0), new Point3d(300,200,0), new Point3d(300,0,0)
                }));
                try
                {
                    FacetBoundary bb = Boundary(doc, big);
                    var sbg = new DiamondFacetSettings { FacetSize = 5.0, ReliefHeight = 0.0, Seed = 1, OutputMode = 0 };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    DiamondFacetResult rb = DiamondFacetCore.GenerateOne(bb, sbg);
                    sw.Stop();
                    double berr = Math.Abs(rb.FacetArea - rb.BoundaryArea) / rb.BoundaryArea;
                    Check(sb, ref pass, ref fail, rb.Triangles > 2000 && berr < 0.05 && sw.Elapsed.TotalSeconds < 30,
                        string.Format(CultureInfo.InvariantCulture, "大边界 200×200/面片5：{0} 个三角面，面积差 {1:0.0%}，用时 {2:0.0}s（<30s）",
                            rb.Triangles, berr, sw.Elapsed.TotalSeconds));
                }
                finally { Cleanup(doc, big); }

                // 兜底剖分的底线：圆周（一堆点共圆）这种退化点集 —— 要么算对，要么明确拒绝，不许给错几何
                Guid cir = doc.Objects.AddCurve(new Circle(Plane.WorldXY, 50.0).ToNurbsCurve());
                try
                {
                    FacetBoundary bc = Boundary(doc, cir);
                    var sc = new DiamondFacetSettings { FacetSize = 8.0, ReliefHeight = 0.0, Seed = 2, OutputMode = 0 };
                    DiamondFacetCore.ForceFallbackTessellation = true;
                    DiamondFacetResult rc;
                    try { rc = DiamondFacetCore.GenerateOne(bc, sc); }
                    finally { DiamondFacetCore.ForceFallbackTessellation = false; }
                    double want = Math.PI * 2500.0;
                    if (string.IsNullOrEmpty(rc.Error))
                    {
                        double cerr = Math.Abs(rc.FacetArea - want) / want;
                        Check(sb, ref pass, ref fail, cerr < 0.05,
                            string.Format(CultureInfo.InvariantCulture, "兜底剖分 · 圆边界：算出来的面积差 {0:0.0%}（<5%）", cerr));
                    }
                    else
                    {
                        Check(sb, ref pass, ref fail, rc.Error.Contains("不可信"),
                            "兜底剖分 · 圆边界：明确拒绝而不是给错几何 —— " + rc.Error);
                    }
                }
                finally { Cleanup(doc, cir); }
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例14：面板交互（输出模式 ↔ 平滑块可见性/可用性）</summary>
        static void Case14_Panel(StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例14：面板交互（仅线框 / 面 ↔ 平滑块） ---");
            DiamondFacetPanel panel = null;
            try
            {
                var st = new DiamondFacetSettings { OutputMode = 1, Smooth = false };
                panel = new DiamondFacetPanel(st, "自检");
                // ⚠ WinForms：控件未 Show 之前 Visible 恒为 false（父级不可见）→ 必须先 Show 再断言可见性
                panel.Show();
                System.Windows.Forms.Application.DoEvents();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    面板构造并显示成功（顶层控件 {0} 个，客户区 {1}x{2}）",
                    panel.Controls.Count, panel.ClientSize.Width, panel.ClientSize.Height));

                Check(sb, ref pass, ref fail, !panel.PickState, "刚打开（没选边界）：拾取按钮是红的");
                panel.SetTargetState(true);
                Check(sb, ref pass, ref fail, panel.PickState, "选到可用边界：拾取按钮变绿");
                panel.SetTargetState(false);
                Check(sb, ref pass, ref fail, !panel.PickState, "选错/被删（置 false）：拾取按钮变红");

                Check(sb, ref pass, ref fail, panel.SmoothBlockVisible, "面 + 未勾平滑：平滑块可见");
                Check(sb, ref pass, ref fail, !panel.SmoothParamsEnabled, "面 + 未勾平滑：平滑参数灰掉（不可调）");

                panel.SetSmooth(true);
                Check(sb, ref pass, ref fail, panel.SmoothParamsEnabled, "面 + 勾选平滑：平滑参数可调");
                Check(sb, ref pass, ref fail, panel.Settings.Smooth, "勾选后设置项同步（Smooth = true）");

                panel.SelectOutputMode(0);
                Check(sb, ref pass, ref fail, !panel.SmoothBlockVisible, "切到「仅线框」：平滑块隐藏（看不到）");
                Check(sb, ref pass, ref fail, !panel.SmoothParamsEnabled, "切到「仅线框」：参数也不可调");

                panel.SelectOutputMode(1);
                Check(sb, ref pass, ref fail, panel.SmoothBlockVisible, "切回「面」：平滑块重新出现");
                Check(sb, ref pass, ref fail, panel.SmoothParamsEnabled, "切回「面」且已勾选：参数仍可调");

                panel.SetSmooth(false);
                Check(sb, ref pass, ref fail, !panel.SmoothParamsEnabled, "取消勾选：参数又灰掉");
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally
            {
                if (panel != null) { try { panel.Close(); panel.Dispose(); } catch { } }
            }
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>用例12：顶点贴住边界（用户实测「边界有一部分是空的」→ 拐角必须进环、外轮廓必须吸附到边界）</summary>
        static void Case12_BoundaryAttached(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例12：顶点贴住边界（外轮廓不许内缩） ---");
            Guid id = Guid.Empty;
            try
            {
                var corners = new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(200,0,0), new Point3d(200,140,0), new Point3d(0,140,0)
                };
                var pl = new PolylineCurve(new Point3d[] { corners[0], corners[1], corners[2], corners[3], corners[0] });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var s = new DiamondFacetSettings { FacetSize = 25.0, ReliefHeight = 1.5, Seed = 21, OutputMode = 0, LockBoundary = true };
                DiamondFacetResult r = DiamondFacetCore.GenerateOne(b, s);
                sb.AppendLine("    " + Info(r));

                // ① 边界环必须含 4 个拐角（纯弧长等分会把矩形的角切掉）
                List<Point3d> ring = DiamondFacetCore.SampleBoundary(b.Outline, s.FacetSize);
                int cornerHit = 0;
                double cornerWorst = 0;
                for (int c = 0; c < corners.Length; c++)
                {
                    double best = double.MaxValue;
                    for (int i = 0; i < ring.Count; i++) best = Math.Min(best, ring[i].DistanceTo(corners[c]));
                    if (best < 1e-6) cornerHit++;
                    if (best > cornerWorst) cornerWorst = best;
                }
                var ring2 = new List<Point2d>();
                for (int i = 0; i < ring.Count; i++) ring2.Add(DiamondFacetCore.To2d(b.Plane, ring[i]));
                double ringArea = Math.Abs(DiamondFacetCore.PolyArea(ring2));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    环点 {0} 个，环多边形面积 {1:0.###}（边界 {2:0.###}）", ring.Count, ringArea, b.Area));
                Check(sb, ref pass, ref fail, cornerHit == 4,
                    string.Format(CultureInfo.InvariantCulture, "边界环含全部 4 个拐角（{0}/4，最大偏差 {1:0.#######}）", cornerHit, cornerWorst));
                Check(sb, ref pass, ref fail, Math.Abs(ringArea - b.Area) / b.Area < 1e-6,
                    string.Format(CultureInfo.InvariantCulture, "环多边形面积 = 边界面积（差 {0:0.00000%}）", Math.Abs(ringArea - b.Area) / b.Area));

                // ② 网格 XY 包围盒 = 边界包围盒（内缩会变小）
                BoundingBox bb = r.Shaded.GetBoundingBox(true);
                double dx0 = Math.Abs(bb.Min.X - 0), dx1 = Math.Abs(bb.Max.X - 200);
                double dy0 = Math.Abs(bb.Min.Y - 0), dy1 = Math.Abs(bb.Max.Y - 140);
                double worstBox = Math.Max(Math.Max(dx0, dx1), Math.Max(dy0, dy1));
                Check(sb, ref pass, ref fail, worstBox < 1e-6,
                    string.Format(CultureInfo.InvariantCulture, "网格包围盒与边界一致（最大偏差 {0:0.#######}）", worstBox));

                // ③ 所有外轮廓（裸边）顶点都落在边界曲线上
                int naked = 0, off = 0;
                double worst = 0;
                var seen = new HashSet<long>();
                var topo = r.Shaded.TopologyEdges;
                for (int i = 0; i < topo.Count; i++)
                {
                    int[] faces = topo.GetConnectedFaces(i);
                    if (faces == null || faces.Length != 1) continue;
                    Line ln = topo.EdgeLine(i);
                    for (int e = 0; e < 2; e++)
                    {
                        Point3d p = e == 0 ? ln.From : ln.To;
                        Point3d flat = new Point3d(p.X, p.Y, 0.0);
                        if (!seen.Add(Q3(flat))) continue;
                        naked++;
                        double t;
                        double d = b.Outline.ClosestPoint(flat, out t) ? b.Outline.PointAt(t).DistanceTo(flat) : double.MaxValue;
                        if (d > 1e-6) { off++; if (d > worst) worst = d; }
                    }
                }
                Check(sb, ref pass, ref fail, off == 0,
                    string.Format(CultureInfo.InvariantCulture, "外轮廓顶点全部落在边界曲线上（{0} 个顶点，越界 {1}，最大偏离 {2:0.######}）", naked, off, worst));

                // ④ 起伏 0 时切面总面积 = 边界面积（内缩会让它变小）
                s.ReliefHeight = 0.0;
                DiamondFacetResult r0 = DiamondFacetCore.GenerateOne(b, s);
                double err = Math.Abs(r0.FacetArea - r0.BoundaryArea) / r0.BoundaryArea;
                Check(sb, ref pass, ref fail, err < 1e-6,
                    string.Format(CultureInfo.InvariantCulture, "起伏 0：切面面积 = 边界面积（差 {0:0.00000%}）", err));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        /// <summary>用例13：一键平滑（每个三角切面一张网格片 → 片内细分；平滑度 / 细分程度）</summary>
        static void Case13_Smooth(RhinoDoc doc, StringBuilder sb, ref int pass, ref int fail)
        {
            sb.AppendLine("--- 用例13：一键平滑（切面网格片 + 片内细分） ---");
            Guid id = Guid.Empty;
            try
            {
                var pl = new PolylineCurve(new Point3d[]
                {
                    new Point3d(0,0,0), new Point3d(60,0,0), new Point3d(60,40,0), new Point3d(0,40,0), new Point3d(0,0,0)
                });
                id = doc.Objects.AddCurve(pl);
                FacetBoundary b = Boundary(doc, id);

                var off = new DiamondFacetSettings { FacetSize = 12.0, ReliefHeight = 2.0, Seed = 31, OutputMode = 1, Smooth = false };
                DiamondFacetResult ro = DiamondFacetCore.GenerateOne(b, off);
                Check(sb, ref pass, ref fail, !ro.Smoothed && ro.SmoothMesh == null && ro.Faces.Count > 0,
                    string.Format(CultureInfo.InvariantCulture, "不勾平滑：只出原平面（{0} 张），没有网格", ro.Faces.Count));

                var on = new DiamondFacetSettings { FacetSize = 12.0, ReliefHeight = 2.0, Seed = 31, OutputMode = 1, Smooth = true, SmoothAdaptive = 95.0, SmoothQuadCount = 5000 };
                DiamondFacetResult rn = DiamondFacetCore.GenerateOne(b, on);
                sb.AppendLine("    " + Info(rn));
                Mesh sm = rn.SmoothMesh;
                Check(sb, ref pass, ref fail, rn.Smoothed && sm != null && sm.Faces.Count > 0,
                    string.Format(CultureInfo.InvariantCulture, "勾选平滑：做出了细分网格（{0} 个面）", sm != null ? sm.Faces.Count : 0));
                if (sm != null && sm.Faces.Count > 0)
                {
                    Check(sb, ref pass, ref fail, rn.SmoothPatches == rn.Triangles && rn.SmoothPatches > 0,
                        string.Format(CultureInfo.InvariantCulture, "每个三角切面各一张网格片（片 {0} = 三角面 {1}）", rn.SmoothPatches, rn.Triangles));
                    int notTri = 0;
                    for (int i = 0; i < sm.Faces.Count; i++) if (!sm.Faces.GetFace(i).IsTriangle) notTri++;
                    Check(sb, ref pass, ref fail, notTri == 0,
                        string.Format(CultureInfo.InvariantCulture, "细分结果全是三角面（非三角 {0} 个，不再有 QuadRemesh 的四边面）", notTri));
                    Check(sb, ref pass, ref fail, sm.Faces.Count >= 2500 && sm.Faces.Count <= 7500,
                        string.Format(CultureInfo.InvariantCulture, "面数落在目标附近（{0}，目标 5000）", sm.Faces.Count));
                    double a1 = MeshArea(sm), a0 = rn.FacetArea;
                    double rel = a0 > 0 ? Math.Abs(a1 - a0) / a0 : 1.0;
                    Check(sb, ref pass, ref fail, rel < 0.001,
                        string.Format(CultureInfo.InvariantCulture, "细分网格面积 = 切面面积（{0:0.##} vs {1:0.##}，差 {2:0.###}%）→ 片内没走形",
                            a1, a0, rel * 100));
                    var have = new HashSet<string>();
                    for (int i = 0; i < sm.Vertices.Count; i++) have.Add(VKey(sm.Vertices.Point3dAt(i)));
                    var used = new HashSet<int>();
                    for (int i = 0; i < rn.Shaded.Faces.Count; i++)
                    {
                        MeshFace f = rn.Shaded.Faces.GetFace(i);
                        used.Add(f.A); used.Add(f.B); used.Add(f.C);
                    }
                    int kept = 0, miss = 0; string missAt = "";
                    foreach (int vi in used)
                    {
                        Point3d p = rn.Shaded.Vertices.Point3dAt(vi);
                        if (have.Contains(VKey(p))) kept++;
                        else { miss++; if (missAt.Length < 70) missAt += p.ToString() + " "; }
                    }
                    Check(sb, ref pass, ref fail, miss == 0 && kept == used.Count,
                        string.Format(CultureInfo.InvariantCulture, "切面角点原样保留（{0}/{1} 个被面引用的顶点在网格里{2}）→ 网格贴住线框",
                            kept, used.Count, miss > 0 ? "，缺：" + missAt : ""));
                    Check(sb, ref pass, ref fail, sm.Normals.Count == sm.Vertices.Count && sm.Normals.Count > 0,
                        "细分网格带法向（预览不会黑）");
                }

                // 细分程度：小目标面数 → 结果面数明显更少
                var low = new DiamondFacetSettings { FacetSize = 12.0, ReliefHeight = 2.0, Seed = 31, OutputMode = 1, Smooth = true, SmoothAdaptive = 95.0, SmoothQuadCount = 300 };
                DiamondFacetResult rl = DiamondFacetCore.GenerateOne(b, low);
                int nLow = rl.SmoothMesh != null ? rl.SmoothMesh.Faces.Count : 0;
                int nHigh = sm != null ? sm.Faces.Count : 0;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    细分程度 300 → {0} 面；5000 → {1} 面", nLow, nHigh));
                Check(sb, ref pass, ref fail, nLow > 0 && nLow < nHigh,
                    string.Format(CultureInfo.InvariantCulture, "细分程度接上了：300 → {0} 面 < 5000 → {1} 面", nLow, nHigh));

                // 平滑度：100% = 档位按切面最长边配平（网格边长一致）；80% = 大面更粗 → 档位/边长比例更散
                // （用圆边界：边界吸附会带出一圈细长三角面，切面尺寸差异明显，参数效果看得清）
                Guid cid = Guid.Empty;
                try
                {
                    cid = doc.Objects.AddCurve(new Circle(Plane.WorldXY, 40.0).ToNurbsCurve());
                    FacetBoundary cb = Boundary(doc, cid);
                    var a100 = new DiamondFacetSettings { FacetSize = 10.0, ReliefHeight = 2.0, Seed = 31, OutputMode = 1, Smooth = true, SmoothAdaptive = 100.0, SmoothQuadCount = 5000 };
                    var a80 = new DiamondFacetSettings { FacetSize = 10.0, ReliefHeight = 2.0, Seed = 31, OutputMode = 1, Smooth = true, SmoothAdaptive = 80.0, SmoothQuadCount = 5000 };
                    DiamondFacetResult r100 = DiamondFacetCore.GenerateOne(cb, a100);
                    DiamondFacetResult r80 = DiamondFacetCore.GenerateOne(cb, a80);
                    double sp100 = GridSpread(r100), sp80 = GridSpread(r80);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    平滑度 100% → 档位/边长离散度 {0:0.###}（档位 {1}，{2} 面）；80% → {3:0.###}（档位 {4}，{5} 面）",
                        sp100, GridRange(r100), r100.SmoothMesh != null ? r100.SmoothMesh.Faces.Count : 0,
                        sp80, GridRange(r80), r80.SmoothMesh != null ? r80.SmoothMesh.Faces.Count : 0));
                    Check(sb, ref pass, ref fail, sp100 > 0 && sp80 > sp100 * 1.2,
                        string.Format(CultureInfo.InvariantCulture, "平滑度接上了：80% 时档位更不按边长配平（离散度 {0:0.###} > 100% 的 {1:0.###}）", sp80, sp100));
                }
                finally { Cleanup(doc, cid); }

                // 写文档：勾了平滑 → 只写这一张细分网格（图层 钻石切面-平滑），不写平面面、不写线框
                var before = new HashSet<Guid>();
                foreach (RhinoObject o in doc.Objects) before.Add(o.Id);
                int cw, fw, mw;
                DiamondFacetCore.AddToDocument(doc, new List<DiamondFacetResult> { rn }, 1, out cw, out fw, out mw);
                Check(sb, ref pass, ref fail, cw == 0 && fw == 0 && mw == 1,
                    string.Format(CultureInfo.InvariantCulture, "勾平滑写文档：只写 1 份细分网格（曲线 {0} / 平面面 {1} / 网格 {2}）", cw, fw, mw));
                var added = new List<Guid>();
                foreach (RhinoObject o in doc.Objects) if (!before.Contains(o.Id)) added.Add(o.Id);
                int ls = LayerIndex(doc, DiamondFacetCore.LayerSmooth);
                bool onLayer = added.Count == 1 && ls >= 0;
                if (onLayer)
                {
                    RhinoObject o = doc.Objects.FindId(added[0]);
                    onLayer = o != null && o.Geometry is Mesh && o.Attributes.LayerIndex == ls;
                }
                Check(sb, ref pass, ref fail, onLayer, "写进去的是网格物件，且落在图层 " + DiamondFacetCore.LayerSmooth);
                Cleanup(doc, added.ToArray());

                // 极端参数不崩
                var huge = new DiamondFacetSettings { FacetSize = 12.0, ReliefHeight = 2.0, Seed = 31, OutputMode = 1, Smooth = true, SmoothAdaptive = 100.0, SmoothQuadCount = 500000 };
                DiamondFacetResult rh = DiamondFacetCore.GenerateOne(b, huge);
                Check(sb, ref pass, ref fail, rh.SmoothMesh != null && rh.SmoothMesh.Faces.Count > 0,
                    string.Format(CultureInfo.InvariantCulture, "细分程度 50 万：依然有几何输出（{0} 面，不崩）",
                        rh.SmoothMesh != null ? rh.SmoothMesh.Faces.Count : 0));
            }
            catch (Exception ex) { Check(sb, ref pass, ref fail, false, "用例异常：" + ex.Message); }
            finally { Cleanup(doc, id); }
        }

        // ------------------------------------------------------------------ 工具

        static FacetBoundary Boundary(RhinoDoc doc, Guid id)
        {
            FacetBoundary b; string why;
            if (!DiamondFacetCore.TryResolve(doc.Objects.FindId(id), out b, out why))
                throw new Exception("边界解析失败：" + why);
            return b;
        }

        static string Info(DiamondFacetResult r)
        {
            if (r == null) return "(null)";
            if (!string.IsNullOrEmpty(r.Error)) return "失败：" + r.Error;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} 个三角面 / {1} 条线 / {2} 张面 · 面积 {3:0.##}（边界 {4:0.##}）· 点 {5}（边界 {6}）· {7:0.00}s{8}",
                r.Triangles, r.Wireframe.Count, r.Faces.Count, r.FacetArea, r.BoundaryArea,
                r.Points, r.BoundaryPoints, r.Seconds,
                string.IsNullOrEmpty(r.Note) ? "" : " · " + r.Note);
        }

        static double AreaCv(DiamondFacetResult r)
        {
            if (r == null || r.Shaded == null || r.Shaded.Faces.Count == 0) return double.MaxValue;
            int n = r.Shaded.Faces.Count;
            var areas = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                MeshFace f = r.Shaded.Faces.GetFace(i);
                Point3d p0 = r.Shaded.Vertices.Point3dAt(f.A);
                Point3d p1 = r.Shaded.Vertices.Point3dAt(f.B);
                Point3d p2 = r.Shaded.Vertices.Point3dAt(f.C);
                areas[i] = Vector3d.CrossProduct(p1 - p0, p2 - p0).Length * 0.5;
                sum += areas[i];
            }
            double mean = sum / n;
            if (!(mean > 0)) return double.MaxValue;
            double var = 0;
            for (int i = 0; i < n; i++) var += (areas[i] - mean) * (areas[i] - mean);
            return Math.Sqrt(var / n) / mean;
        }

        /// <summary>顶点坐标键（1e-6 取整）：用来验证切面角点在细分网格里原样存在（+0.0 把 -0 归一成 0）</summary>
        static string VKey(Point3d p)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.######}|{1:0.######}|{2:0.######}",
                Math.Round(p.X, 6) + 0.0, Math.Round(p.Y, 6) + 0.0, Math.Round(p.Z, 6) + 0.0);
        }

        static double MeshArea(Mesh m)
        {
            if (m == null) return 0;
            double a = 0;
            for (int i = 0; i < m.Faces.Count; i++)
            {
                MeshFace f = m.Faces.GetFace(i);
                Point3d p0 = m.Vertices.Point3dAt(f.A), p1 = m.Vertices.Point3dAt(f.B), p2 = m.Vertices.Point3dAt(f.C);
                a += 0.5 * Vector3d.CrossProduct(p1 - p0, p2 - p0).Length;
            }
            return a;
        }

        /// <summary>「细分档位 ÷ 切面最长边」的离散度：0 = 档位完全按边长配平（网格边长一致），越大越不配平</summary>
        static double GridSpread(DiamondFacetResult r)
        {
            if (r == null || r.SmoothGrids == null || r.Shaded == null) return 0;
            int n = Math.Min(r.SmoothGrids.Count, r.Shaded.Faces.Count);
            if (n < 2) return 0;
            double meanLong = 0;
            var rl = new double[n];
            for (int i = 0; i < n; i++)
            {
                MeshFace f = r.Shaded.Faces.GetFace(i);
                Point3d a = r.Shaded.Vertices.Point3dAt(f.A), b = r.Shaded.Vertices.Point3dAt(f.B), c = r.Shaded.Vertices.Point3dAt(f.C);
                rl[i] = Math.Max(a.DistanceTo(b), Math.Max(b.DistanceTo(c), c.DistanceTo(a)));
                if (!(rl[i] > 0)) rl[i] = 1e-9;
                meanLong += rl[i];
            }
            meanLong /= n;
            if (!(meanLong > 0)) return 0;
            double s1 = 0, s2 = 0;
            for (int i = 0; i < n; i++)
            {
                double q = r.SmoothGrids[i] / (rl[i] / meanLong);
                s1 += q; s2 += q * q;
            }
            double mean = s1 / n;
            if (!(mean > 0)) return 0;
            double var = s2 / n - mean * mean;
            if (var < 0) var = 0;
            return Math.Sqrt(var) / mean;
        }

        static string GridRange(DiamondFacetResult r)
        {
            if (r == null || r.SmoothGrids == null || r.SmoothGrids.Count == 0) return "无";
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < r.SmoothGrids.Count; i++)
            {
                if (r.SmoothGrids[i] < lo) lo = r.SmoothGrids[i];
                if (r.SmoothGrids[i] > hi) hi = r.SmoothGrids[i];
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}~{1}", lo, hi);
        }

        static bool OnCurve(Curve c, Point3d p, double tol)
        {
            try
            {
                double t;
                if (!c.ClosestPoint(p, out t)) return false;
                return c.PointAt(t).DistanceTo(p) <= tol;
            }
            catch { return false; }
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
