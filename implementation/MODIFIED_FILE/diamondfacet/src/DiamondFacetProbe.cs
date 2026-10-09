using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;

namespace DiamondFacetPattern
{
    /// <summary>临时诊断（不进最终交付）：摸清 Mesh.CreateFromTessellation 的行为 + 自实现 Delaunay 的有效性</summary>
    public class DiamondFacetProbeCommand : Command
    {
        public override string EnglishName { get { return "DiamondFacetProbe"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var sb = new StringBuilder();
            Probe(sb);
            RhinoApp.WriteLine(sb.ToString());
            return Result.Success;
        }

        internal static void Probe(StringBuilder sb)
        {
            sb.AppendLine("=== 剖分引擎诊断 ===");
            var plane = Plane.WorldXY;

            // 100x100 方形：40 个轮廓点 + 5x5 内部点
            var ring = new List<Point3d>();
            for (int i = 0; i < 10; i++) ring.Add(new Point3d(i * 10, 0, 0));
            for (int i = 0; i < 10; i++) ring.Add(new Point3d(100, i * 10, 0));
            for (int i = 0; i < 10; i++) ring.Add(new Point3d(100 - i * 10, 100, 0));
            for (int i = 0; i < 10; i++) ring.Add(new Point3d(0, 100 - i * 10, 0));
            var inner = new List<Point3d>();
            for (int y = 1; y <= 5; y++)
                for (int x = 1; x <= 5; x++)
                    inner.Add(new Point3d(x * 16.6, y * 16.6, 0));
            var all = new List<Point3d>(ring);
            all.AddRange(inner);

            var outlines = new List<IEnumerable<Point3d>>();
            outlines.Add(ring);

            TryMesh(sb, "A 内部点 + 轮廓环, allowNewVertices=false", () => Mesh.CreateFromTessellation(inner, outlines, plane, false));
            TryMesh(sb, "B 内部点 + 轮廓环, allowNewVertices=true ", () => Mesh.CreateFromTessellation(inner, outlines, plane, true));
            TryMesh(sb, "C 全部点 + 轮廓环, false", () => Mesh.CreateFromTessellation(all, outlines, plane, false));
            TryMesh(sb, "D 全部点 + 轮廓环, true ", () => Mesh.CreateFromTessellation(all, outlines, plane, true));
            TryMesh(sb, "E 全部点 + 空轮廓, false", () => Mesh.CreateFromTessellation(all, new List<IEnumerable<Point3d>>(), plane, false));
            TryMesh(sb, "F 全部点 + 空轮廓, true ", () => Mesh.CreateFromTessellation(all, new List<IEnumerable<Point3d>>(), plane, true));
            TryMesh(sb, "G 全部点 + null 轮廓, true ", () => Mesh.CreateFromTessellation(all, null, plane, true));
            var outlines2 = new List<IEnumerable<Point3d>>();
            outlines2.Add(ring);
            outlines2.Add(ring);
            TryMesh(sb, "H 全部点 + 轮廓环×2, true", () => Mesh.CreateFromTessellation(all, outlines2, plane, true));

            // 自实现 Delaunay 有效性：圆点集（39 环点 + 96 内部点）
            var cpts = new List<Point2d>();
            for (int i = 0; i < 39; i++)
            {
                double a = i * 2 * Math.PI / 39.0;
                cpts.Add(new Point2d(50 * Math.Cos(a), 50 * Math.Sin(a)));
            }
            var rng = new Rng(2);
            for (int y = -4; y <= 4; y++)
                for (int x = -4; x <= 4; x++)
                    cpts.Add(new Point2d(x * 10 + rng.NextRange(-5, 5), y * 10 + rng.NextRange(-5, 5)));
            List<int[]> tris = DiamondFacetCore.Delaunay(cpts);
            double area = 0;
            int degen = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                Point2d a = cpts[tris[i][0]], b = cpts[tris[i][1]], c = cpts[tris[i][2]];
                double t = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) * 0.5;
                area += t;
                if (t < 1e-9) degen++;
            }
            double want = Math.PI * 2500.0;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "自实现 Delaunay：点 {0} → 三角面 {1}（理论上限 2n-5 = {2}）· 面积和 {3:0.#}（凸包 ≈ {4:0.#}，比值 {5:0.###}）· 退化面 {6}",
                cpts.Count, tris.Count, 2 * cpts.Count - 5, area, want, area / want, degen));

            sb.AppendLine("--- QuadRemesh 探针（一键平滑用）---");
            ProbeQuadRemesh(sb);
            sb.AppendLine("--- 圆角引擎探针（Brep.CreateFilletEdges）---");
            ProbeFillet(sb, "2 面屋顶（1 条内部边）", Roof());
            ProbeFillet(sb, "4 面扇（中心点，4 条内部边）", Fan(4));
            ProbeFillet(sb, "12 面扇（12 条内部边）", Fan(12));
            ProbeFillet(sb, "6 面枕·外圈共面（z 全 0）", Pillow(6, false));
            ProbeFillet(sb, "6 面枕·外圈起伏（z 随机）", Pillow(6, true));
        }

        static Brep Tri(Point3d p0, Point3d p1, Point3d p2)
        {
            try
            {
                var tri = new PolylineCurve(new Point3d[] { p0, p1, p2, p0 });
                return Brep.CreateTrimmedPlane(new Plane(p0, p1, p2), tri);
            }
            catch { return null; }
        }

        static List<Brep> Roof()
        {
            var a = new Point3d(0, 0, 0); var b = new Point3d(10, 0, 0);
            var c = new Point3d(5, 8, 1.5); var d = new Point3d(5, -8, 1.5);
            var l = new List<Brep>();
            Brep f1 = Tri(a, b, c), f2 = Tri(a, d, b);
            if (f1 != null) l.Add(f1);
            if (f2 != null) l.Add(f2);
            return l;
        }

        static List<Brep> Fan(int n)
        {
            var l = new List<Brep>();
            var c = new Point3d(0, 0, 2.0);
            for (int i = 0; i < n; i++)
            {
                double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
                var p0 = new Point3d(10 * Math.Cos(a0), 10 * Math.Sin(a0), 0);
                var p1 = new Point3d(10 * Math.Cos(a1), 10 * Math.Sin(a1), 0);
                Brep f = Tri(c, p0, p1);
                if (f != null) l.Add(f);
            }
            return l;
        }

        static List<Brep> Pillow(int n, bool jitter)
        {
            var l = new List<Brep>();
            var c = new Point3d(0, 0, 3.0);
            var rnd = new Rng(7);
            for (int i = 0; i < n; i++)
            {
                double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
                double z0 = jitter ? rnd.NextRange(-1.5, 1.5) : 0.0;
                double z1 = jitter ? rnd.NextRange(-1.5, 1.5) : 0.0;
                var p0 = new Point3d(10 * Math.Cos(a0), 10 * Math.Sin(a0), z0);
                var p1 = new Point3d(10 * Math.Cos(a1), 10 * Math.Sin(a1), z1);
                Brep f = Tri(c, p0, p1);
                if (f != null) l.Add(f);
                Brep g = Tri(p0, new Point3d(0, 0, 0), p1);
                if (g != null) l.Add(g);
            }
            return l;
        }

        static void ProbeFillet(StringBuilder sb, string label, List<Brep> faces)
        {
            if (faces.Count == 0) { sb.AppendLine("  " + label + "：构造失败"); return; }
            Brep joined = null;
            try
            {
                Brep[] arr = Brep.JoinBreps(faces, 1e-6);
                if (arr != null && arr.Length > 0) joined = arr[0];
            }
            catch (Exception ex) { sb.AppendLine("  " + label + "：Join 异常 " + ex.Message); return; }
            if (joined == null) { sb.AppendLine("  " + label + "：Join 返回空（面没接上）"); return; }

            var interior = new List<int>();
            double minDihedral = 999;
            try
            {
                for (int i = 0; i < joined.Edges.Count; i++)
                {
                    int[] af = joined.Edges[i].AdjacentFaces();
                    if (af == null || af.Length != 2) continue;
                    interior.Add(i);
                    double ang = DiamondFacetCore.DihedralDeg(joined, joined.Edges[i], af[0], af[1]);
                    if (ang < minDihedral) minDihedral = ang;
                }
            }
            catch { }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}：输入面 {1} → 合成面 {2} / 边 {3} / 内部边 {4} / 最小二面角 {5:0.#}°",
                label, faces.Count, joined.Faces.Count, joined.Edges.Count, interior.Count, minDihedral));
            if (interior.Count == 0) { sb.AppendLine("    （没有内部边，无法倒圆角）"); return; }

            foreach (double r in new double[] { 1.0, 0.5, 0.2 })
            {
                try
                {
                    var radii = new double[interior.Count];
                    for (int i = 0; i < radii.Length; i++) radii[i] = r;
                    Brep[] outp = Brep.CreateFilletEdges(joined, interior, radii, radii, BlendType.Fillet, RailType.RollingBall, 1e-6);
                    if (outp == null) { sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    r={0:0.##} → null", r)); continue; }
                    int fc = (outp.Length > 0 && outp[0] != null) ? outp[0].Faces.Count : 0;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    r={0:0.##} → {1} 个结果，第一个面数 {2}{3}", r, outp.Length, fc,
                        outp.Length > 0 && fc > joined.Faces.Count ? "  ✓ 倒圆成功" : ""));
                    if (outp.Length > 0 && fc > joined.Faces.Count) return;
                }
                catch (Exception ex) { sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    r={0:0.##} → 异常 {1}", r, ex.Message)); }
            }
        }

        /// <summary>QuadRemesh 探针：默认值 + 参数组合 + 为什么可能返回 null（输入网格要求）</summary>
        static void ProbeQuadRemesh(StringBuilder sb)
        {
            try
            {
                var def = new QuadRemeshParameters();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  默认参数：TargetQuadCount={0} AdaptiveSize={1} AdaptiveQuadCount={2} DetectHardEdges={3}",
                    def.TargetQuadCount, def.AdaptiveSize, def.AdaptiveQuadCount, def.DetectHardEdges));
            }
            catch (Exception ex) { sb.AppendLine("  默认参数读取失败：" + ex.Message); }

            Mesh sheet = TestSheet(24, 4.0);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  测试网格：顶点 {0} / 三角面 {1}（开放片）", sheet.Vertices.Count, sheet.Faces.Count));

            var sphere = Mesh.CreateFromSphere(new Sphere(Plane.WorldXY, 20.0), 32, 24);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  测试球：顶点 {0} / 面 {1}（闭合）", sphere.Vertices.Count, sphere.Faces.Count));

            // A) 默认参数原样
            TryQuad(sb, "默认参数（全默认）", sheet, null);
            // B) 只要目标面数
            TryQuad(sb, "只设 TargetQuadCount=5000", sheet, prm => { prm.TargetQuadCount = 5000; });
            // C) 目标面数 + 平滑度 95（0~100 标度）
            TryQuad(sb, "TargetQuadCount=5000, AdaptiveSize=95", sheet, prm => { prm.TargetQuadCount = 5000; prm.AdaptiveSize = 95; });
            // D) 同上但关掉自适应四边面数量
            TryQuad(sb, "…+AdaptiveQuadCount=false", sheet, prm => { prm.TargetQuadCount = 5000; prm.AdaptiveSize = 95; prm.AdaptiveQuadCount = false; });
            // E) 关掉检测硬边
            TryQuad(sb, "…+DetectHardEdges=false", sheet, prm => { prm.TargetQuadCount = 5000; prm.AdaptiveSize = 95; prm.DetectHardEdges = false; });
            // F) 闭合球（看是不是要求闭合/流形）
            TryQuad(sb, "闭合球 + 默认", sphere, null);
            TryQuad(sb, "闭合球 + 5000/95", sphere, prm => { prm.TargetQuadCount = 5000; prm.AdaptiveSize = 95; });
            // G) 焊接 + 统一法向后的开放片
            var sheet2 = sheet.DuplicateMesh();
            try { sheet2.Vertices.CombineIdentical(true, true); sheet2.Vertices.CullUnused(); sheet2.FaceNormals.ComputeFaceNormals(); sheet2.UnifyNormals(); } catch { }
            TryQuad(sb, "焊接+统一法向后的开放片", sheet2, prm => { prm.TargetQuadCount = 5000; prm.AdaptiveSize = 95; });
            // H) Brep 版本
            try
            {
                Brep bp = Brep.CreateFromMesh(sheet, false);
                var prm = new QuadRemeshParameters();
                prm.TargetQuadCount = 5000; prm.AdaptiveSize = 95;
                Mesh rm = Mesh.QuadRemeshBrep(bp, prm);
                sb.AppendLine("  QuadRemeshBrep(开放片) → " + (rm == null ? "null" : ("面 " + rm.Faces.Count)));
            }
            catch (Exception ex) { sb.AppendLine("  QuadRemeshBrep 异常：" + ex.Message); }
            // I2) Brep 路径下平滑度对比（80 / 95 / 100，看参数是否真的接上）
            try
            {
                Brep bp2 = Brep.CreateFromMesh(sheet, false);
                foreach (double ad in new double[] { 80, 95, 100 })
                {
                    var prm = new QuadRemeshParameters();
                    prm.TargetQuadCount = 5000; prm.AdaptiveSize = ad; prm.AdaptiveQuadCount = true;
                    Mesh rm = Mesh.QuadRemeshBrep(bp2, prm);
                    double minE = double.MaxValue, maxE = 0;
                    if (rm != null)
                        for (int i = 0; i < rm.TopologyEdges.Count; i++)
                        {
                            double L = rm.TopologyEdges.EdgeLine(i).Length;
                            if (L < minE) minE = L; if (L > maxE) maxE = L;
                        }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  Brep 路径 平滑度 {0} → {1}", ad,
                        rm == null ? "null" : string.Format(CultureInfo.InvariantCulture, "面 {0}，边长 {1:0.##}~{2:0.##}（比 {3:0.#}）", rm.Faces.Count, minE, maxE, maxE / Math.Max(1e-9, minE))));
                }
            }
            catch (Exception ex) { sb.AppendLine("  Brep 平滑度对比异常：" + ex.Message); }
            // I) 异步版本（带进度回调）
            try
            {
                var prm = new QuadRemeshParameters();
                prm.TargetQuadCount = 5000; prm.AdaptiveSize = 95;
                var task = sheet.QuadRemeshAsync(prm, null, System.Threading.CancellationToken.None);
                task.Wait(60000);
                Mesh rm = task.IsCompleted ? task.Result : null;
                sb.AppendLine("  QuadRemeshAsync → " + (rm == null ? "null" : ("面 " + rm.Faces.Count)));
            }
            catch (Exception ex) { sb.AppendLine("  QuadRemeshAsync 异常：" + ex.Message); }
        }

        static Mesh TestSheet(int n, double step)
        {
            var m = new Mesh();
            for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                    m.Vertices.Add(x * step, y * step, 3.0 * Math.Sin(x * 0.6) * Math.Cos(y * 0.5));
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int a = y * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                    m.Faces.AddFace(a, b, d);
                    m.Faces.AddFace(a, d, c);
                }
            m.FaceNormals.ComputeFaceNormals();
            return m;
        }

        static void TryQuad(StringBuilder sb, string label, Mesh m, Action<QuadRemeshParameters> set)
        {
            try
            {
                var prm = new QuadRemeshParameters();
                if (set != null) set(prm);
                Mesh rm = m.QuadRemesh(prm);
                if (rm == null) { sb.AppendLine("  " + label + " → null"); return; }
                int quads = 0, tris = 0;
                for (int i = 0; i < rm.Faces.Count; i++)
                    if (rm.Faces.GetFace(i).IsTriangle) tris++; else quads++;
                string subd = "-";
                try { SubD sd = SubD.CreateFromMesh(rm); subd = sd == null ? "SubD null" : (sd.IsValid ? "SubD 有效" : "SubD 无效"); }
                catch (Exception ex) { subd = "SubD 异常 " + ex.Message; }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} → 面 {1}（四边 {2} / 三角 {3}）· {4}", label, rm.Faces.Count, quads, tris, subd));
            }
            catch (Exception ex) { sb.AppendLine("  " + label + " → 异常：" + ex.Message); }
        }

        static void TryMesh(StringBuilder sb, string label, Func<Mesh> f)
        {
            try
            {
                Mesh m = f();
                if (m == null) { sb.AppendLine("  " + label + " → null"); return; }
                double area = 0;
                try { AreaMassProperties amp = AreaMassProperties.Compute(m); if (amp != null) area = amp.Area; } catch { }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} → 顶点 {1} / 面 {2} / ngon {3} / 面积 {4:0.#}", label, m.Vertices.Count, m.Faces.Count, m.Ngons.Count, area));
            }
            catch (Exception ex) { sb.AppendLine("  " + label + " → 异常：" + ex.Message); }
        }

    }
}
