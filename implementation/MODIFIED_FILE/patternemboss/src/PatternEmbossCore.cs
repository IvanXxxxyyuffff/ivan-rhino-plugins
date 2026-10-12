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

            merged.Faces.ConvertQuadsToTriangles();
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
        public static SubD ToSubD(Mesh m)
        {
            if (m == null || m.Faces.Count == 0) return null;
            try
            {
                SubD s = SubD.CreateFromMesh(m);
                if (s == null || !s.IsValid) return null;
                return s;
            }
            catch { return null; }
        }

        // ============================================================ 单面生成

        static Mesh GenFace(BrepFace face, PatternEmbossSettings s, ref double minH, ref double maxH)
        {
            if (face == null) return null;
            Interval du = face.Domain(0), dv = face.Domain(1);

            // ---- 物理尺度（uv 每单位对应模型长度）；平面走 Frame 直投保证无扭曲
            double su = 1, sv = 1;
            Plane frame = Plane.Unset;
            Point3d center = Point3d.Unset;
            bool planar = false;
            try { planar = face.TryGetPlane(out frame, 1e-3) && frame.IsValid; } catch { planar = false; }
            if (planar)
            {
                try { center = face.PointAt(du.Mid, dv.Mid); } catch { center = Point3d.Unset; }
            }
            else
            {
                ComputeScales(face, du, dv, out su, out sv);
            }

            double cellMm = s.CellSize * s.MmToModel;
            double grooveMm = Math.Max(0, s.Groove * s.MmToModel);
            double heightModel = s.Height * s.MmToModel;
            double marginModel = Math.Max(0, s.Margin * s.MmToModel);
            int dens = Math.Max(6, Math.Min(24, s.Density));
            double stepMm = cellMm / dens;                     // 采样步长（物理）

            // ---- 区域范围（平面用包围盒；曲面用 uv 域 + 尺度换算）
            double umin = du.Min, umax = du.Max, vmin = dv.Min, vmax = dv.Max;
            double spanMu = (umax - umin) * su, spanMv = (vmax - vmin) * sv;

            int nu = (int)Math.Floor(spanMu / stepMm) + 1;
            int nv = (int)Math.Floor(spanMv / stepMm) + 1;
            if ((long)nu * nv > GridCap)
            {
                // 降密度保护：保持至少 4 采样/单元
                double k = Math.Sqrt((long)nu * nv / (double)GridCap);
                dens = Math.Max(4, (int)(dens / k));
                stepMm = cellMm / dens;
                nu = (int)Math.Floor(spanMu / stepMm) + 1;
                nv = (int)Math.Floor(spanMv / stepMm) + 1;
                if ((long)nu * nv > GridCap) return null;   // 仍超 → 放弃该面
            }
            if (nu < 2 || nv < 2) return null;

            // ---- 格点采样
            var pts = new Point3d[nu * nv];
            var nrm = new Vector3d[nu * nv];
            var inside = new bool[nu * nv];
            var hgt = new double[nu * nv];
            for (int i = 0; i < nu; i++)
            {
                double u = umin + (umax - umin) * i / (double)(nu - 1);
                for (int j = 0; j < nv; j++)
                {
                    double v = vmin + (vmax - vmin) * j / (double)(nv - 1);
                    int k = i * nv + j;
                    Point3d p; Vector3d n;
                    bool ok = false;
                    try
                    {
                        p = face.PointAt(u, v);
                        n = face.NormalAt(u, v);
                        ok = p.IsValid && n.IsValid && n.Unitize() && face.IsPointOnFace(u, v) != PointFaceRelation.Exterior;
                    }
                    catch { p = Point3d.Unset; n = Vector3d.Unset; }
                    if (!ok) { inside[k] = false; continue; }
                    inside[k] = true;
                    pts[k] = p; nrm[k] = n;

                    // 物理图案坐标
                    double x, y;
                    if (planar && center.IsValid)
                    {
                        Vector3d d = p - frame.Origin;
                        x = d * frame.XAxis; y = d * frame.YAxis;
                    }
                    else
                    {
                        x = (u - umin) * su; y = (v - vmin) * sv;
                    }
                    hgt[k] = HeightField(x, y, cellMm, grooveMm, heightModel, s.Roundness, s.Pattern);
                }
            }

            // ---- 留边渐隐（多源 BFS：到最近界外格点的步数 × 步长）
            if (marginModel > 1e-12)
            {
                var dist = new int[nu * nv];
                var q = new Queue<int>();
                for (int k = 0; k < dist.Length; k++)
                {
                    if (inside[k]) dist[k] = int.MaxValue;
                    else { dist[k] = 0; q.Enqueue(k); }
                }
                // 域边缘也算界外（贴边渐隐）
                for (int i = 0; i < nu; i++) { TrySeed(q, dist, i * nv + 0); TrySeed(q, dist, i * nv + (nv - 1)); }
                for (int j = 0; j < nv; j++) { TrySeed(q, dist, 0 * nv + j); TrySeed(q, dist, (nu - 1) * nv + j); }
                while (q.Count > 0)
                {
                    int k = q.Dequeue();
                    int i = k / nv, j = k % nv;
                    int d = dist[k] + 1;
                    if (i > 0) Push(q, dist, k - nv, d);
                    if (i < nu - 1) Push(q, dist, k + nv, d);
                    if (j > 0) Push(q, dist, k - 1, d);
                    if (j < nv - 1) Push(q, dist, k + 1, d);
                }
                for (int k = 0; k < dist.Length; k++)
                {
                    if (!inside[k]) continue;
                    double dm = dist[k] * stepMm;
                    if (dm < marginModel) hgt[k] *= Math.Max(0.0, dm / marginModel);
                }
            }

            // ---- 建网格（四角都在界内的格子）
            var mesh = new Mesh();
            var idx = new int[nu * nv];
            for (int k = 0; k < idx.Length; k++) idx[k] = -1;
            double usedMin = double.MaxValue, usedMax = double.MinValue;
            for (int i = 0; i < nu; i++)
            {
                for (int j = 0; j < nv; j++)
                {
                    int k = i * nv + j;
                    if (!inside[k]) continue;
                    double h = hgt[k];
                    if (h < usedMin) usedMin = h;
                    if (h > usedMax) usedMax = h;
                    Point3d q0 = pts[k] + nrm[k] * h;
                    idx[k] = mesh.Vertices.Add(q0);
                }
            }
            for (int i = 0; i < nu - 1; i++)
            {
                for (int j = 0; j < nv - 1; j++)
                {
                    int a = idx[i * nv + j], b = idx[(i + 1) * nv + j], c = idx[(i + 1) * nv + j + 1], d = idx[i * nv + j + 1];
                    if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                    mesh.Faces.AddFace(a, b, c, d);
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
