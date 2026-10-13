using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace PatternEmboss
{
    /// <summary>图案压花参数（长度单位 mm）</summary>
    public class PatternEmbossSettings
    {
        public int Pattern = 0;          // 0=六边形（蜂窝） 1=四边形（方阵） 2=菱形（菱格）
        public double CellSize = 4.0;    // 单元尺寸 mm（六边形对边距 / 方格边长 / 菱形对角线基准）
        public double Height = 0.4;      // 凹凸高度 mm（正 = 凸起，负 = 凹陷）
        public double Groove = 0.8;      // 槽宽 mm（单元之间凹槽的总宽度）
        public double Roundness = 0.7;   // 圆润度 0..1（1 = 圆肩过渡，0 = 锋利直壁）
        public double Margin = 1.0;      // 边缘留边 mm（边界平滑带宽度；0 = 纹理贴到边）
        public int Density = 10;         // 每单元采样数（6..24）
        public double Thickness = 0.0;   // 加厚 mm（0 = 仅面片网格；>0 = 壳/实体）
        public bool SubDOutput = false;  // 输出 = SubD 细分平滑曲面（一键平滑；代替纯网格）
        public double MmToModel = 1.0;   // 面板 mm → 模型单位

        // ---- 一键平滑的 QuadRemesh 重构参数（勾选 SubDOutput 后生效）
        public int QrTargetQuadCount = 0;    // 目标四边形数（0 = 自动：按单元数 ×200 换算，保住图案细节）
        public bool QrAdaptiveCount = false; // 自适应四边形数（勾选后忽略目标四边形数）
        public double QrTargetEdgeLength = 0; // 目标边长 mm（0 = 自动；仅较新 RhinoCommon 支持，老版本自动跳过）
        public double QrAdaptiveSize = 0;    // 自适应尺寸 0..1（越大越随曲率自适应）
        public int QrSymmetryAxis = 0;       // 对称轴 0=无 1=X 2=Y 3=Z
        public bool QrDetectHardEdges = true;// 检测硬边（沿硬边布四边形）

        public PatternEmbossSettings Clone() { return (PatternEmbossSettings)MemberwiseClone(); }

        public static readonly string[] PatternNames = { "六边形", "四边形", "菱形" };
    }

    /// <summary>生成结果</summary>
    public class PatternEmbossResult
    {
        public Mesh Mesh;                // 带凹凸的网格（所有面合并）
        public bool Solid;               // 是否加厚成功
        public int Vertices;
        public int Faces;
        public double MinH;              // 实际高度最小值（模型单位）
        public double MaxH;              // 实际高度最大值
        public double Area;              // 网格面积
        public string Note = "";
    }

    /// <summary>图案压花引擎：曲面 → 细网格 → 按图案高度场沿法向偏移 → 可选加厚成壳</summary>
    public static class PatternEmbossCore
    {
        const double Tol = 1e-6;
        const long GridCap = 4_000_000;      // 总格点上限（防大面炸内存）

        public static PatternEmbossResult Generate(Brep brep, PatternEmbossSettings s, out string report)
        {
            var res = new PatternEmbossResult();
            report = "";
            if (brep == null || brep.Faces.Count == 0) { res.Note = "输入为空"; report = res.Note; return res; }
            if (s == null) s = new PatternEmbossSettings();
            if (!(s.CellSize * s.MmToModel > 1e-9)) { res.Note = "单元尺寸无效"; report = res.Note; return res; }

            var merged = new Mesh();
            int facesDone = 0, facesSkipped = 0;
            double minH = double.MaxValue, maxH = double.MinValue;
            var why = new System.Text.StringBuilder();

            for (int fi = 0; fi < brep.Faces.Count; fi++)
            {
                Mesh one = null;
                try { one = GenFace(brep.Faces[fi], s, ref minH, ref maxH); }
                catch (Exception ex) { one = null; why.Append("面" + (fi + 1) + " 异常：" + ex.Message + "；"); }
                if (one == null || one.Faces.Count == 0) { facesSkipped++; why.Append("面" + (fi + 1) + " 无有效网格；"); continue; }
                merged.Append(one);
                facesDone++;
            }

            if (merged.Faces.Count == 0)
            {
                res.Note = "没有生成任何网格（" + why.ToString() + "）";
                report = res.Note;
                return res;
            }

            try { merged.Vertices.CombineIdentical(true, true); } catch { }
            try { merged.Vertices.CullUnused(); } catch { }
            try { merged.FaceNormals.ComputeFaceNormals(); } catch { }

            double thick = s.Thickness * s.MmToModel;
            if (thick > 1e-9)
            {
                try
                {
                    Mesh solid = null;
                    try { solid = merged.Offset(-thick, true); } catch { solid = null; }
                    if (solid == null) { try { solid = merged.Offset(thick, true); } catch { solid = null; } }
                    if (solid != null && solid.Faces.Count > 0)
                    {
                        merged = solid;
                        res.Solid = true;
                    }
                    else why.Append("加厚失败（Offset 返回空），已回退面片网格；");
                }
                catch (Exception ex) { why.Append("加厚异常：" + ex.Message + "，已回退面片网格；"); }
            }

            res.Mesh = merged;
            res.Vertices = merged.Vertices.Count;
            res.Faces = merged.Faces.Count;
            res.MinH = minH == double.MaxValue ? 0 : minH;
            res.MaxH = maxH == double.MinValue ? 0 : maxH;
            try { var amp = AreaMassProperties.Compute(merged); res.Area = amp != null ? amp.Area : 0; } catch { res.Area = 0; }
            res.Note = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "面 {0}/{1} 成功{2}；高 {3:0.####}~{4:0.####}{5}",
                facesDone, brep.Faces.Count,
                res.Solid ? "（已加厚成壳）" : "",
                res.MinH, res.MaxH,
                why.Length > 0 ? "；" + why : "");
            report = res.Note;
            return res;
        }

        /// <summary>一键平滑：网格 → SubD 细分曲面（Catmull-Clark）；失败返回 null</summary>
        public static string LastSubDError = "";
        public static SubD ToSubD(Mesh m)
        {
            LastSubDError = "";
            if (m == null || m.Faces.Count == 0) return null;
            try
            {
                SubD s = SubD.CreateFromMesh(m);
                if (s == null) { LastSubDError = "CreateFromMesh 返回 null"; return null; }
                if (!s.IsValid) { LastSubDError = "结果 IsValid=false"; return null; }
                return s;
            }
            catch (Exception ex) { LastSubDError = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        /// <summary>单元数估算（面积 ÷ 单元面积；六边形约 0.866·cell²，统一按 0.8 近似）</summary>
        public static double EstCellCount(Mesh m, PatternEmbossSettings s)
        {
            try
            {
                double area = 0;
                var amp = AreaMassProperties.Compute(m);
                if (amp != null) area = amp.Area;
                double cell = Math.Max(1e-9, s.CellSize * s.MmToModel);
                return area / (cell * cell * 0.8);
            }
            catch { return 0; }
        }

        /// <summary>自动目标四边形数：单元数 × 200（每单元约 200 个四边形才够还原细槽），夹在 [4000, 200000]</summary>
        public static int AutoQuadTarget(Mesh m, PatternEmbossSettings s)
        {
            double cells = EstCellCount(m, s);
            if (cells <= 0) return 30000;
            return Math.Max(4000, Math.Min(200000, (int)Math.Round(cells * 200.0)));
        }

        /// <summary>TargetEdgeLength 是较新 RhinoCommon 才有的属性：反射按需设置，老 Rhino 7 自动跳过</summary>
        public static void SetTargetEdgeLength(object prm, double el)
        {
            try
            {
                var pi = prm.GetType().GetProperty("TargetEdgeLength");
                if (pi != null && pi.CanWrite) pi.SetValue(prm, el, null);
            }
            catch { }
        }

        /// <summary>一键平滑第一步：QuadRemesh 重拓扑（四边形重构，参数来自设置；失败返回 null）</summary>
        public static string LastQuadRemeshError = "";
        public static Mesh QuadRemesh(Mesh m, PatternEmbossSettings s)
        {
            LastQuadRemeshError = "";
            if (m == null || m.Faces.Count == 0 || s == null) return null;
            try
            {
                // QuadRemesh 要求输入网格已计算顶点法向（否则静默返回 null —— 实测 Rhino 8.30）
                try { m.Normals.ComputeNormals(); } catch { }
                var prm = new QuadRemeshParameters();
                prm.AdaptiveQuadCount = s.QrAdaptiveCount;
                prm.AdaptiveSize = Math.Max(0.0, Math.Min(1.0, s.QrAdaptiveSize));
                prm.DetectHardEdges = s.QrDetectHardEdges;
                prm.SymmetryAxis = (QuadRemeshSymmetryAxis)Math.Max(0, Math.Min(3, s.QrSymmetryAxis));
                prm.TargetQuadCount = s.QrTargetQuadCount > 0 ? s.QrTargetQuadCount : AutoQuadTarget(m, s);
                double el = s.QrTargetEdgeLength * s.MmToModel;
                if (el > 1e-9) SetTargetEdgeLength(prm, el);
                Mesh q = m.QuadRemesh(prm);
                if (q == null) { LastQuadRemeshError = "QuadRemesh 返回 null"; return null; }
                try { q.Vertices.CombineIdentical(true, true); } catch { }
                try { q.Vertices.CullUnused(); } catch { }
                try { q.UnifyNormals(); } catch { }
                if (!q.IsValid || q.Faces.Count == 0) { LastQuadRemeshError = "结果无效（IsValid=" + q.IsValid + " faces=" + q.Faces.Count + "）"; return null; }
                return q;
            }
            catch (Exception ex) { LastQuadRemeshError = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        // ============================================================ 单面生成（图案化拓扑：每单元同心环四边面片，槽缝 = 共享边环）

        static Mesh GenFace(BrepFace face, PatternEmbossSettings s, ref double minH, ref double maxH)
        {
            if (face == null) return null;
            Interval du = face.Domain(0), dv = face.Domain(1);

            // ---- 物理尺度（uv 每单位对应模型长度）；平面走 Frame 直投保证无扭曲
            double su = 1, sv = 1;
            Plane frame = Plane.Unset;
            bool planar = false;
            try { planar = face.TryGetPlane(out frame, 1e-3) && frame.IsValid; } catch { planar = false; }
            ComputeScales(face, du, dv, out su, out sv);   // 平面也取（边界渐隐网格用）

            double cellMm = s.CellSize * s.MmToModel;
            double grooveMm = Math.Max(0, s.Groove * s.MmToModel);
            double heightModel = s.Height * s.MmToModel;
            double marginModel = Math.Max(0, s.Margin * s.MmToModel);
            int dens = Math.Max(6, Math.Min(24, s.Density));
            double stepMm = cellMm / dens;

            // ---- 单元几何（与 HeightField 的格子约定严格一致）
            int NC; double[] cdx, cdy; double dyRow, QC;
            switch (s.Pattern)
            {
                case 1: // 方阵：角在 45°+90°k
                {
                    NC = 4; cdx = new double[4]; cdy = new double[4];
                    double q = cellMm * 0.5 * Math.Sqrt(2.0);
                    for (int k2 = 0; k2 < 4; k2++) { double a = Math.PI / 4.0 + Math.PI / 2.0 * k2; cdx[k2] = q * Math.Cos(a); cdy[k2] = q * Math.Sin(a); }
                    dyRow = cellMm; QC = cellMm * 0.7072;
                    break;
                }
                case 2: // 菱形：四角 (±rx,0)(0,±ry)
                {
                    NC = 4; cdx = new double[4]; cdy = new double[4];
                    cdx[0] = cellMm * 0.5; cdy[0] = 0;
                    cdx[1] = 0; cdy[1] = cellMm * 0.5;
                    cdx[2] = -cellMm * 0.5; cdy[2] = 0;
                    cdx[3] = 0; cdy[3] = -cellMm * 0.5;
                    dyRow = cellMm * 0.5; QC = cellMm * 0.5;
                    break;
                }
                default: // 蜂窝（pointy-top）：角在 30°+60°k
                {
                    NC = 6; cdx = new double[6]; cdy = new double[6];
                    double Rc = cellMm / Math.Sqrt(3.0);
                    for (int k2 = 0; k2 < 6; k2++) { double a = Math.PI / 6.0 + Math.PI / 3.0 * k2; cdx[k2] = Rc * Math.Cos(a); cdy[k2] = Rc * Math.Sin(a); }
                    dyRow = 1.5 * (cellMm / Math.Sqrt(3.0)); QC = cellMm / Math.Sqrt(3.0);
                    break;
                }
            }
            bool rowOffset = (s.Pattern != 1);

            // ---- 每边采样数 K、同心环数 M（面数保护）
            double perim = 0;
            for (int k2 = 0; k2 < NC; k2++) { int n2 = (k2 + 1) % NC; perim += Math.Sqrt((cdx[n2] - cdx[k2]) * (cdx[n2] - cdx[k2]) + (cdy[n2] - cdy[k2]) * (cdy[n2] - cdy[k2])); }
            int K = Math.Max(3, (int)Math.Round((perim / NC) / stepMm));
            int M = Math.Max(3, dens / 2);
            int per = NC * K;

            // ---- 区域范围（图案坐标 x,y）
            double xmin, xmax, ymin, ymax;
            if (planar)
            {
                bool any = false; xmin = xmax = ymin = ymax = 0;
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        Point3d P;
                        try { P = face.PointAt(du.ParameterAt(i / 4.0), dv.ParameterAt(j / 4.0)); } catch { continue; }
                        if (!P.IsValid) continue;
                        Vector3d d = P - frame.Origin;
                        double x = d * frame.XAxis, y = d * frame.YAxis;
                        if (!any) { xmin = xmax = x; ymin = ymax = y; any = true; }
                        else { if (x < xmin) xmin = x; if (x > xmax) xmax = x; if (y < ymin) ymin = y; if (y > ymax) ymax = y; }
                    }
                if (!any) { xmin = 0; xmax = du.Max - du.Min; ymin = 0; ymax = dv.Max - dv.Min; }
            }
            else
            {
                xmin = 0; xmax = (du.Max - du.Min) * su;
                ymin = 0; ymax = (dv.Max - dv.Min) * sv;
            }

            // ---- 面数保护（预估超限时降一档；仍超则放弃该面）
            {
                double cellsEst = Math.Max(1.0, (xmax - xmin + cellMm) * (ymax - ymin + cellMm) / (0.75 * cellMm * cellMm));
                if (cellsEst * per * M > 900000.0)
                {
                    K = Math.Max(3, K / 2);
                    M = Math.Max(3, M / 2);
                    per = NC * K;
                    if (cellsEst * per * M > 900000.0) return null;
                }
            }

            // ---- 边缘渐隐：粗网格 BFS 距离场（uv 空间，按物理步长）+ 双线性查表
            int gnu = 0, gnv = 0; double gu0 = 0, gv0 = 0, guStep = 1, gvStep = 1;
            int[] gdist = null;
            if (marginModel > 1e-12)
            {
                double buStep = 0.5 * stepMm / Math.Max(su, 1e-9);
                double bvStep = 0.5 * stepMm / Math.Max(sv, 1e-9);
                gnu = (int)Math.Floor((du.Max - du.Min) / buStep) + 3;
                gnv = (int)Math.Floor((dv.Max - dv.Min) / bvStep) + 3;
                guStep = (du.Max - du.Min) / Math.Max(1, gnu - 3);
                gvStep = (dv.Max - dv.Min) / Math.Max(1, gnv - 3);
                while ((long)gnu * gnv > GridCap && gnu > 3 && gnv > 3)
                {
                    guStep *= 2; gvStep *= 2;
                    gnu = (int)Math.Floor((du.Max - du.Min) / guStep) + 3;
                    gnv = (int)Math.Floor((dv.Max - dv.Min) / gvStep) + 3;
                }
                gu0 = du.Min - guStep; gv0 = dv.Min - gvStep;
                gdist = new int[gnu * gnv];
                var q = new Queue<int>();
                for (int i = 0; i < gnu; i++)
                    for (int j = 0; j < gnv; j++)
                    {
                        double u = gu0 + i * guStep, v = gv0 + j * gvStep;
                        bool ins = false;
                        try { ins = face.IsPointOnFace(u, v) != PointFaceRelation.Exterior; } catch { ins = false; }
                        int kk = i * gnv + j;
                        if (ins) gdist[kk] = int.MaxValue; else { gdist[kk] = 0; q.Enqueue(kk); }
                    }
                for (int i = 0; i < gnu; i++) { TrySeed(q, gdist, i * gnv); TrySeed(q, gdist, i * gnv + gnv - 1); }
                for (int j = 0; j < gnv; j++) { TrySeed(q, gdist, j); TrySeed(q, gdist, (gnu - 1) * gnv + j); }
                while (q.Count > 0)
                {
                    int kk = q.Dequeue();
                    int i = kk / gnv, j = kk % gnv;
                    int dd = gdist[kk] + 1;
                    if (i > 0) Push(q, gdist, kk - gnv, dd);
                    if (i < gnu - 1) Push(q, gdist, kk + gnv, dd);
                    if (j > 0) Push(q, gdist, kk - 1, dd);
                    if (j < gnv - 1) Push(q, gdist, kk + 1, dd);
                }
            }

            Func<double, double, double> MarginAt = (u, v) =>
            {
                if (gdist == null) return 1.0;
                double fi = (u - gu0) / guStep, fj = (v - gv0) / gvStep;
                if (fi < 0 || fj < 0 || fi > gnu - 1 || fj > gnv - 1) return 0.0;
                int i0 = Math.Min(gnu - 2, (int)fi), j0 = Math.Min(gnv - 2, (int)fj);
                double tx = fi - i0, ty = fj - j0;
                double d00 = gdist[i0 * gnv + j0] == int.MaxValue ? 1e9 : gdist[i0 * gnv + j0];
                double d10 = gdist[(i0 + 1) * gnv + j0] == int.MaxValue ? 1e9 : gdist[(i0 + 1) * gnv + j0];
                double d01 = gdist[i0 * gnv + j0 + 1] == int.MaxValue ? 1e9 : gdist[i0 * gnv + j0 + 1];
                double d11 = gdist[(i0 + 1) * gnv + j0 + 1] == int.MaxValue ? 1e9 : gdist[(i0 + 1) * gnv + j0 + 1];
                double dm = ((d00 * (1 - tx) + d10 * tx) * (1 - ty) + (d01 * (1 - tx) + d11 * tx) * ty) * ((guStep + gvStep) * 0.5);
                double f = dm / marginModel;
                return f >= 1.0 ? 1.0 : (f <= 0.0 ? 0.0 : f);
            };

            // ---- 顶点焊接（图案坐标 0.1µm 量化）
            var vmap = new Dictionary<long, int>(1 << 16);
            var mesh = new Mesh();
            double usedMin = double.MaxValue, usedMax = double.MinValue;
            Func<double, double, int> GetV = (x, y) =>
            {
                long rx = (long)Math.Round(x * 1e4);
                long ry = (long)Math.Round(y * 1e4);
                long key = rx * 2000000011L + ry;
                int idx;
                if (vmap.TryGetValue(key, out idx)) return idx;

                double u, v; Point3d p; Vector3d n; bool ok;
                if (planar)
                {
                    p = frame.Origin + x * frame.XAxis + y * frame.YAxis;
                    n = frame.ZAxis;
                    double uu, vv;
                    ok = false; u = v = 0;
                    try
                    {
                        // 界外点会被 ClosestPoint 吸到边界上（IsPointOnFace 返回 Boundary 而非 Exterior）→ 用距离复核
                        ok = face.ClosestPoint(p, out uu, out vv)
                            && face.PointAt(uu, vv).DistanceTo(p) < 1e-4
                            && face.IsPointOnFace(uu, vv) != PointFaceRelation.Exterior;
                        u = uu; v = vv;
                    }
                    catch { ok = false; }
                }
                else
                {
                    u = du.Min + x / su; v = dv.Min + y / sv;
                    try
                    {
                        p = face.PointAt(u, v);
                        n = face.NormalAt(u, v);
                        ok = u >= du.Min && u <= du.Max && v >= dv.Min && v <= dv.Max
                            && p.IsValid && n.IsValid && n.Unitize() && face.IsPointOnFace(u, v) != PointFaceRelation.Exterior;
                    }
                    catch { p = Point3d.Unset; n = Vector3d.Unset; ok = false; }
                }
                if (!ok) { vmap[key] = -1; return -1; }

                double h = HeightField(x, y, cellMm, grooveMm, heightModel, s.Roundness, s.Pattern);
                if (gdist != null) h *= MarginAt(u, v);
                if (h < usedMin) usedMin = h;
                if (h > usedMax) usedMax = h;
                idx = mesh.Vertices.Add(p + n * h);
                vmap[key] = idx;
                return idx;
            };

            // ---- 单元遍历（格子约定与 HeightField 一致）
            long rowMinL = (long)Math.Floor((ymin - QC - dyRow) / dyRow) - 1;
            long rowMaxL = (long)Math.Ceiling((ymax + QC + dyRow) / dyRow) + 1;
            var bxr = new double[per];
            var byr = new double[per];
            for (long row = rowMinL; row <= rowMaxL; row++)
            {
                double off = (rowOffset && (((row % 2) + 2) % 2 == 1)) ? cellMm * 0.5 : 0;
                long colMinL = (long)Math.Floor((xmin - QC - cellMm - off) / cellMm) - 1;
                long colMaxL = (long)Math.Ceiling((xmax + QC + cellMm - off) / cellMm) + 1;
                for (long col = colMinL; col <= colMaxL; col++)
                {
                    double cx = col * cellMm + off, cy = row * dyRow;
                    if (cx + QC < xmin || cx - QC > xmax || cy + QC < ymin || cy - QC > ymax) continue;

                    // 边界环（逐边均匀采样，环绕 CCW）
                    for (int e = 0; e < NC; e++)
                    {
                        int e2 = (e + 1) % NC;
                        double x0 = cx + cdx[e], y0 = cy + cdy[e];
                        double x1 = cx + cdx[e2], y1 = cy + cdy[e2];
                        for (int i = 0; i < K; i++)
                        {
                            double t = (double)i / K;
                            bxr[e * K + i] = x0 + (x1 - x0) * t;
                            byr[e * K + i] = y0 + (y1 - y0) * t;
                        }
                    }
                    // 同心环
                    var rings = new int[M][];
                    bool anyValid = false;
                    for (int r = 0; r < M; r++)
                    {
                        double t = 1.0 - (double)r / M;
                        var rr = new int[per];
                        for (int i = 0; i < per; i++)
                        {
                            rr[i] = GetV(cx + (bxr[i] - cx) * t, cy + (byr[i] - cy) * t);
                            if (rr[i] >= 0) anyValid = true;
                        }
                        rings[r] = rr;
                    }
                    if (!anyValid) continue;
                    // 外环带（同心环之间的四边面）
                    for (int r = 0; r < M - 1; r++)
                    {
                        int[] A = rings[r], B2 = rings[r + 1];
                        for (int i = 0; i < per; i++)
                        {
                            int j2 = (i + 1) % per;
                            int a = A[i], b = A[j2], c = B2[j2], d = B2[i];
                            if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                            if (a == b || b == c || c == d || d == a || a == c || b == d) continue;
                            mesh.Faces.AddFace(a, b, c, d);
                        }
                    }
                    // 中心封盖：每两段环边一个四边面（per 为偶数恒成立），中心为极点；环边被恰好两个面共用（无洞）
                    int cIdx = GetV(cx, cy);
                    {
                        int[] A = rings[M - 1];
                        for (int i = 0; i < per; i += 2)
                        {
                            int a = cIdx, b = A[i], c = A[(i + 1) % per], d = A[(i + 2) % per];
                            if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                            if (a == b || b == c || c == d || d == a || a == c || b == d) continue;
                            mesh.Faces.AddFace(a, b, c, d);
                        }
                    }
                }
            }

            if (mesh.Faces.Count == 0) return null;
            if (usedMin != double.MaxValue)
            {
                if (usedMin < minH) minH = usedMin;
                if (usedMax > maxH) maxH = usedMax;
            }
            try { mesh.FaceNormals.ComputeFaceNormals(); } catch { }
            return mesh;
        }

        static void TrySeed(Queue<int> q, int[] dist, int k)
        {
            if (k < 0 || k >= dist.Length) return;
            if (dist[k] == 0) return;
            dist[k] = 0; q.Enqueue(k);
        }
        static void Push(Queue<int> q, int[] dist, int k, int d)
        {
            if (dist[k] <= d) return;
            dist[k] = d; q.Enqueue(k);
        }

        static void ComputeScales(BrepFace face, Interval du, Interval dv, out double su, out double sv)
        {
            su = 1; sv = 1;
            try
            {
                double hu = Math.Max((du.Max - du.Min) * 1e-3, 1e-9);
                double hv = Math.Max((dv.Max - dv.Min) * 1e-3, 1e-9);
                double accU = 0, accV = 0; int n = 0;
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        double u = du.ParameterAt(i / 4.0), v = dv.ParameterAt(j / 4.0);
                        Point3d P = face.PointAt(u, v);
                        Point3d Pu = face.PointAt(u + hu, v);
                        Point3d Pv = face.PointAt(u, v + hv);
                        if (!P.IsValid || !Pu.IsValid || !Pv.IsValid) continue;
                        accU += Pu.DistanceTo(P) / hu;
                        accV += Pv.DistanceTo(P) / hv;
                        n++;
                    }
                if (n > 0) { su = accU / n; sv = accV / n; }
            }
            catch { }
            if (!(su > 1e-9)) su = 1;
            if (!(sv > 1e-9)) sv = 1;
        }

        // ============================================================ 图案高度场

        /// <summary>图案高度：单元内滿高、单元间凹槽归零；roundness 控制肩部圆滑度（0 锋利 1 圆润）</summary>
        internal static double HeightField(double x, double y, double cell, double grooveMm, double height,
            double roundness, int pattern)
        {
            if (!(cell > 1e-12)) return 0;
            double r = Math.Max(0, Math.Min(1, roundness));
            double gw = Math.Max(grooveMm, 1e-6) * 0.5;   // 半槽宽 = 到边界距离阈值
            double d;   // 到所属单元边界的距离（>=0 在单元内）

            switch (pattern)
            {
                case 0:  // 六边形（pointy-top 蜂窝）
                {
                    double R = cell / Math.Sqrt(3.0);      // 外接圆半径
                    double dy = 1.5 * R;                   // 行距
                    int row = (int)Math.Round(y / dy);
                    double best = double.MaxValue; double bx = 0, by = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        int rw = row + dr;
                        double off = ((rw % 2) + 2) % 2 == 1 ? cell * 0.5 : 0;
                        int col = (int)Math.Round((x - off) / cell);
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            double cx = (col + dc) * cell + off;
                            double cy = rw * dy;
                            double dd = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                            if (dd < best) { best = dd; bx = x - cx; by = y - cy; }
                        }
                    }
                    // 到 6 条边的距离：apothem = cell/2（pointy-top）；边法向 0°,60°,...,300°
                    double ap = cell * 0.5;
                    double maxDot = double.MinValue;
                    for (int e = 0; e < 6; e++)
                    {
                        double th = Math.PI / 3.0 * e;
                        double dot = bx * Math.Cos(th) + by * Math.Sin(th);
                        if (dot > maxDot) maxDot = dot;
                    }
                    d = ap - maxDot;
                    break;
                }
                case 1:  // 四边形（正方格）
                {
                    double cx = Math.Round(x / cell) * cell;
                    double cy = Math.Round(y / cell) * cell;
                    double bx = Math.Abs(x - cx), by = Math.Abs(y - cy);
                    d = cell * 0.5 - Math.Max(bx, by);
                    break;
                }
                default: // 菱形（菱格；隔行错半格）
                {
                    double ry = cell * 0.5;                    // 菱形竖直半对角
                    double rx = cell * 0.5;                    // 水平半对角
                    int row = (int)Math.Round(y / ry);
                    double best = double.MaxValue; double bx = 0, by = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        int rw = row + dr;
                        double off = ((rw % 2) + 2) % 2 == 1 ? rx : 0;
                        int col = (int)Math.Round((x - off) / (2 * rx));
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            double cx = (col + dc) * 2 * rx + off;
                            double cy = rw * ry;
                            double dd = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                            if (dd < best) { best = dd; bx = x - cx; by = y - cy; }
                        }
                    }
                    // 菱形边界 |bx|/rx + |by|/ry = 1；到边界距离（近似欧氏）：
                    d = (1.0 - (Math.Abs(bx) / rx + Math.Abs(by) / ry)) * Math.Min(rx, ry) / Math.Sqrt(2.0);
                    break;
                }
            }

            if (d <= 0) return 0;
            double t = Math.Min(1.0, d / gw);
            double smooth = t * t * (3 - 2 * t);
            double prof = (1 - r) * t + r * smooth;
            return height * prof;
        }
    }
}
