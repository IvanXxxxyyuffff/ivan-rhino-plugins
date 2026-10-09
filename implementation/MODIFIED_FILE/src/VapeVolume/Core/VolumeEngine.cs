using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace VapeVolume.Core
{
    public enum VolumeMode
    {
        Liquid = 0,
        Solid = 1,
        Both = 2
    }

    /// <summary>单个物件的容量计算结果，全部数值单位为毫升。</summary>
    public sealed class VolumeReport
    {
        public bool Ok;
        public string Error = "";
        public Guid Id = Guid.Empty;
        public string Name = "";

        /// <summary>所有壳体是否都闭合。</summary>
        public bool Closed;

        /// <summary>结果是否包含近似成分（自动封口 / 网格近似）。</summary>
        public bool Approximate;

        /// <summary>是否对开放曲面做过自动封口。</summary>
        public bool WasCapped;

        /// <summary>是否检测到封闭内腔。</summary>
        public bool HasCavity;

        public int ShellCount;

        /// <summary>内腔容积 = 可灌注的液体体积。</summary>
        public double LiquidMl;

        /// <summary>材料体积 = 实体本身占用的体积（已扣除内腔）。</summary>
        public double MaterialMl;

        /// <summary>最外层包裹体积（外形轮廓围成的体积）。</summary>
        public double EnvelopeMl;

        public string Note = "";
        public Point3d LabelPoint = Point3d.Origin;
        public string SourceKind = "";

        public double PrimaryMl(VolumeMode mode)
        {
            if (mode == VolumeMode.Solid) return MaterialMl;
            return HasCavity ? LiquidMl : MaterialMl;
        }

        public string PrimaryLabel(VolumeMode mode)
        {
            if (mode == VolumeMode.Solid) return "实体体积";
            if (HasCavity) return "液体容量（内腔）";
            if (WasCapped) return "密封容积（曲面所围）";
            return "实体体积（无内腔）";
        }
    }

    /// <summary>「油杯」模式的结果：按壁厚扣减后的内部容积。</summary>
    public sealed class CupResult
    {
        public bool Ok;
        public string Error = "";

        /// <summary>基准容积（毫升）：已按模式处理完毕，尚未乘转换率、尚未扣雾化芯。</summary>
        public double BaseMl;

        /// <summary>是否直接用了模型已有的封闭内腔。</summary>
        public bool UsedExistingCavity;

        /// <summary>是否对实体做了壁厚偏置。</summary>
        public bool WallRemoved;

        public bool Approximate;
        public string Note = "";

        /// <summary>「液体所在区域」的实体（内腔或扣完壁厚的核心），供扣雾化芯做布尔运算。可能为 null。</summary>
        public Brep LiquidSolid;
    }

    /// <summary>雾化芯圆柱的轴向。</summary>
    public enum CoilAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    /// <summary>雾化芯扣除结果。</summary>
    public sealed class CoilResult
    {
        public bool Ok;
        public string Error = "";

        /// <summary>与液体区域相交的那部分雾化芯体积（毫升）。</summary>
        public double VolumeMl;

        /// <summary>该部分沿轴的长度（毫米），用于核对。</summary>
        public double LengthMm;

        public bool Approximate;
        public string Note = "";
    }

    internal sealed class ShellInfo
    {
        public double CubicUnits;
        public bool Closed;
        public bool WasCapped;
        public int Depth;
        public Brep BrepRef;
        public List<Point3d> Samples = new List<Point3d>();
    }

    /// <summary>
    /// 几何核心：物件 → 毫升。
    ///
    /// 判定逻辑：
    ///   1) 把物件拆成互相独立的「壳体」(shell)：Brep 按共享边分组，Mesh 按连通分量分组。
    ///   2) 用点在体内测试 / 半射线奇偶测试求每个壳体的嵌套层数 depth。
    ///   3) depth 偶数 = 实体，奇数 = 空腔；据此得到
    ///        材料体积 = Σ(偶数层) − Σ(奇数层)
    ///        内腔容积 = Σ(奇数层) − Σ(偶数层且≥2)
    ///        外形包裹体积 = Σ(第 0 层)
    /// </summary>
    public static class VolumeEngine
    {
        const int SampleCount = 6;

        // ─────────────────────────── 对外入口 ───────────────────────────

        public static VolumeReport Compute(RhinoObject obj, UnitContext units, MeshingParameters mp)
        {
            var r = new VolumeReport();
            if (obj == null)
            {
                r.Error = "没有物件";
                return r;
            }

            GeometryBase geom;
            try
            {
                r.Id = obj.Id;
                r.Name = obj.Attributes != null && !string.IsNullOrWhiteSpace(obj.Attributes.Name)
                    ? obj.Attributes.Name
                    : "（未命名）";
                geom = obj.Geometry;
            }
            catch (Exception ex)
            {
                r.Error = "读取物件失败：" + ex.Message;
                return r;
            }

            AnalyzeGeometry(geom, r, units, mp);

            try
            {
                var bb = geom.GetBoundingBox(true);
                if (bb.IsValid) r.LabelPoint = bb.Center;
            }
            catch
            {
                // 标注点取不到不影响结果
            }

            return r;
        }

        /// <summary>对任意几何做壳体分析（供对话框/壁厚扣减复用）。</summary>
        public static void AnalyzeGeometry(GeometryBase geom, VolumeReport r, UnitContext units, MeshingParameters mp)
        {
            if (geom == null)
            {
                r.Error = "物件几何为空";
                r.Ok = false;
                return;
            }

            if (units == null) units = new UnitContext(UnitSystem.Millimeters, null);

            try
            {
                var mesh = geom as Mesh;
                if (mesh != null)
                {
                    AnalyzeMesh(mesh, r, units, mp);
                }
                else
                {
                    Brep brep = ToBrep(geom);
                    if (brep == null)
                    {
                        r.Error = "不支持的物件类型：" + geom.GetType().Name;
                        r.Ok = false;
                        return;
                    }
                    AnalyzeBrep(brep, r, units, mp);
                }
            }
            catch (Exception ex)
            {
                r.Error = "计算失败：" + ex.Message;
                r.Ok = false;
                return;
            }

            r.Ok = string.IsNullOrEmpty(r.Error)
                   && r.ShellCount > 0
                   && (r.MaterialMl > 0.0 || r.LiquidMl > 0.0);
        }

        /// <summary>
        /// 「油杯」模式：算出杯子内部可容纳的容积。
        ///   · 模型本身已有封闭内腔 → 直接用内腔容积（不再额外扣壁厚）
        ///   · 模型是实心体     → 按壁厚向内偏置，取内部核心体积
        /// </summary>
        public static CupResult ComputeCup(RhinoObject obj, UnitContext units, double wallMm, MeshingParameters mp)
        {
            var cup = new CupResult();
            if (obj == null)
            {
                cup.Error = "没有物件";
                return cup;
            }

            VolumeReport rep = Compute(obj, units, mp);
            if (!rep.Ok)
            {
                cup.Error = rep.Error;
                return cup;
            }

            double tol = SafeTolerance();
            GeometryBase geom = obj.Geometry;
            Brep brep = ToBrepAny(geom);

            if (rep.HasCavity)
            {
                cup.Ok = true;
                cup.BaseMl = rep.LiquidMl;
                cup.UsedExistingCavity = true;
                cup.Approximate = rep.Approximate;
                cup.Note = "模型里已有封闭内腔，直接取内腔容积（未再额外扣壁厚）。";
                if (brep != null) cup.LiquidSolid = FindCavitySolid(brep, tol, mp);
                return cup;
            }

            double wall = Math.Max(0.0, wallMm);
            if (wall <= 1.0e-9)
            {
                cup.Ok = true;
                cup.BaseMl = rep.MaterialMl;
                cup.LiquidSolid = brep;
                cup.Note = "壁厚为 0，取实体体积。";
                return cup;
            }

            if (brep == null)
            {
                cup.Error = "网格物件暂不支持壁厚扣减，请把它转成 NURBS（ToNURBS）后重试，或改用「油的体积」模式。";
                return cup;
            }

            if (!brep.IsSolid)
            {
                try
                {
                    Brep capped = brep.CapPlanarHoles(SafeTolerance());
                    if (capped != null && capped.IsSolid) brep = capped;
                }
                catch
                {
                    // 封口失败就按原样尝试偏置
                }
            }

            double unitsPerMm = Math.Max(units.MillimetersPerUnit, 1.0e-12);
            double t = wall / unitsPerMm;               // 毫米 → 模型单位

            if (!(t > tol * 0.5))
                tol = t * 0.01;                         // 壁厚极薄时收紧容差

            Brep joinedFirst;
            double coreVolume = OffsetCoreVolume(brep, -t, true, tol, units, mp,
                                                 out bool approx, out string err, out joinedFirst);
            Brep joined = joinedFirst;

            if (!(coreVolume > 0.0))
            {
                // 退一步：只偏置曲面本体（solid=false），再按实体体积取核心
                Brep joinedAlt;
                double alt = OffsetCoreVolume(brep, -t, false, tol, units, mp,
                                              out bool approx2, out string err2, out joinedAlt);
                if (alt > 0.0)
                {
                    coreVolume = alt;
                    approx = approx2;
                    joined = joinedAlt;
                }
                else if (string.IsNullOrEmpty(err))
                {
                    err = err2;
                }
            }

            // 合理性校验：核心体积必须比物件本身小。
            // 不满足说明偏置方向或结果不可信（例如形状自交、偏置失败）。
            bool sane = coreVolume > 0.0 && (rep.MaterialMl <= 0.0 || coreVolume < rep.MaterialMl * 0.9999);

            if (!sane)
            {
                // 兜底：面积法估算 V_core ≈ V − A×t，结果标注为近似
                double areaMm2;
                double fallback = AreaEstimateCore(rep.MaterialMl, t, units, brep, out areaMm2);
                if (fallback > 0.0)
                {
                    cup.Ok = true;
                    cup.BaseMl = fallback;
                    cup.WallRemoved = true;
                    cup.Approximate = true;
                    cup.Note = string.Format(
                        "偏置法在这个形状上不可用，已改用面积法估算：容积 ≈ V − A×t（表面积 {0:0.##} mm²、壁厚 {1:0.###} mm）。结果为近似值。",
                        areaMm2, wall);
                    return cup;
                }

                cup.Error = "按壁厚向内扣减失败（" + (string.IsNullOrEmpty(err) ? "偏置结果不合理" : err)
                            + "）。可改用「油的体积」模式直接测量。";
                return cup;
            }

            cup.Ok = true;
            cup.BaseMl = coreVolume;
            cup.WallRemoved = true;
            cup.Approximate = approx;
            cup.Note = string.Format("已按壁厚 {0:0.###} mm 向内偏置后取内部容积。", wall);

            // 取出核心实体，供扣雾化芯做布尔运算
            if (joined != null)
            {
                Brep core = FindCavitySolid(joined, tol, mp);
                if (core == null && joined.IsSolid) core = joined;
                cup.LiquidSolid = core;
            }

            return cup;
        }

        // ─────────────────────────── Brep 路径 ───────────────────────────

        static void AnalyzeBrep(Brep brep, VolumeReport r, UnitContext units, MeshingParameters mp)
        {
            r.SourceKind = "Brep";

            double tol = SafeTolerance();
            var shells = SplitShells(brep);
            if (shells.Count == 0)
            {
                r.Error = "无法从该物件提取有效曲面";
                return;
            }

            var infos = new List<ShellInfo>();
            r.Closed = true;

            foreach (var raw in shells)
            {
                var info = new ShellInfo();
                Brep cur = raw;
                bool closed;
                try { closed = cur.IsSolid; } catch { closed = false; }

                if (!closed)
                {
                    try
                    {
                        Brep capped = cur.CapPlanarHoles(tol);
                        if (capped != null && capped.IsSolid)
                        {
                            cur = capped;
                            closed = true;
                            info.WasCapped = true;
                            r.WasCapped = true;
                        }
                    }
                    catch
                    {
                        // 封口失败则按近似处理
                    }
                }

                double cubic = 0.0;
                if (closed)
                {
                    try
                    {
                        var vmp = VolumeMassProperties.Compute(cur);
                        if (vmp != null) cubic = Math.Abs(vmp.Volume);
                    }
                    catch
                    {
                        cubic = 0.0;
                    }
                }

                if (cubic <= 0.0)
                {
                    cubic = MeshVolume(cur, mp);
                    if (cubic > 0.0) r.Approximate = true;
                }

                r.Closed = r.Closed && closed;
                info.Closed = closed;
                info.CubicUnits = cubic;
                info.BrepRef = cur;
                info.Samples = SamplePointsFromBrep(cur, mp);
                infos.Add(info);
            }

            for (int i = 0; i < infos.Count; i++)
                infos[i].Depth = infos.Count > 1 ? BrepDepth(infos, i, tol) : 0;

            Classify(infos, units, r);
        }

        static List<Brep> SplitShells(Brep brep)
        {
            var result = new List<Brep>();
            int n = brep.Faces.Count;
            if (n == 0) return result;

            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            foreach (var edge in brep.Edges)
            {
                int[] af;
                try { af = edge.AdjacentFaces(); } catch { af = null; }
                if (af == null || af.Length < 2) continue;
                for (int k = 1; k < af.Length; k++)
                    Union(parent, af[0], af[k]);
            }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                List<int> list;
                if (!groups.TryGetValue(root, out list))
                {
                    list = new List<int>();
                    groups[root] = list;
                }
                list.Add(i);
            }

            foreach (var g in groups.Values)
            {
                Brep sub;
                try
                {
                    sub = g.Count == n ? brep.DuplicateBrep() : brep.DuplicateSubBrep(g);
                }
                catch
                {
                    sub = null;
                }
                if (sub != null) result.Add(sub);
            }

            return result;
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }

        static int BrepDepth(List<ShellInfo> infos, int index, double tol)
        {
            var self = infos[index];
            if (self.Samples.Count == 0) return 0;

            int depth = 0;
            for (int j = 0; j < infos.Count; j++)
            {
                if (j == index) continue;
                Brep other = infos[j].BrepRef;
                if (other == null) continue;

                int inside = 0, total = 0;
                foreach (var p in self.Samples)
                {
                    if (!p.IsValid) continue;
                    total++;
                    try
                    {
                        if (other.IsPointInside(p, tol, true)) inside++;
                    }
                    catch
                    {
                        // 单点失败不影响多数表决
                    }
                }

                if (total > 0 && inside * 2 > total) depth++;
            }
            return depth;
        }

        // ─────────────────────────── Mesh 路径 ───────────────────────────

        static void AnalyzeMesh(Mesh mesh, VolumeReport r, UnitContext units, MeshingParameters mp)
        {
            r.SourceKind = "Mesh";

            Mesh[] pieces;
            try { pieces = mesh.SplitDisjointPieces(); } catch { pieces = null; }
            if (pieces == null || pieces.Length == 0)
            {
                r.Error = "网格无法拆分出独立壳体";
                return;
            }

            var infos = new List<ShellInfo>();
            r.Closed = true;
            double rayLen = 1.0;
            try
            {
                var bb = mesh.GetBoundingBox(true);
                if (bb.IsValid) rayLen = Math.Max(bb.Diagonal.Length * 3.0, 1e-3);
            }
            catch
            {
                rayLen = 1.0;
            }

            foreach (var p in pieces)
            {
                var info = new ShellInfo();
                bool closed;
                try { closed = p.IsClosed; } catch { closed = false; }

                double cubic = 0.0;
                if (closed)
                {
                    try { cubic = Math.Abs(p.Volume()); } catch { cubic = 0.0; }
                }
                if (cubic <= 0.0) r.Approximate = true;

                r.Closed = r.Closed && closed;
                info.Closed = closed;
                info.CubicUnits = cubic;
                info.Samples = SampleMeshPoints(p, SampleCount);
                infos.Add(info);
            }

            for (int i = 0; i < infos.Count; i++)
                infos[i].Depth = MeshDepth(pieces, infos, i, rayLen);

            Classify(infos, units, r);
        }

        static int MeshDepth(Mesh[] pieces, List<ShellInfo> infos, int index, double rayLen)
        {
            var self = infos[index];
            if (!self.Closed || self.Samples.Count == 0) return 0;

            var dirs = new[]
            {
                new Vector3d(0.2371, 0.5917, 0.7712),
                new Vector3d(-0.6131, 0.3111, 0.7261),
                new Vector3d(0.4471, -0.7748, 0.4471)
            };

            int depth = 0;
            for (int j = 0; j < pieces.Length; j++)
            {
                if (j == index) continue;
                if (!infos[j].Closed) continue;

                int insidePoints = 0, validPoints = 0;
                foreach (var pt in self.Samples)
                {
                    int insideDirs = 0, validDirs = 0;
                    foreach (var d0 in dirs)
                    {
                        var d = new Vector3d(d0);
                        if (!d.Unitize()) continue;

                        // 必须是「半射线」：从采样点朝一个方向射出。
                        // 整条直线穿凸体时交点恒为偶数（内部 2、外部 0 或 2），无法区分内外；
                        // 半射线才是「内部 = 奇数」。
                        var ray = new Line(pt + d * (rayLen * 1.0e-6), pt + d * rayLen);
                        int count;
                        try
                        {
                            var hits = Intersection.MeshLine(pieces[j], ray, out int[] faceIds);
                            count = hits == null ? 0 : hits.Length;
                        }
                        catch
                        {
                            count = -1;
                        }
                        if (count >= 0)
                        {
                            validDirs++;
                            if (count % 2 == 1) insideDirs++;
                        }
                    }
                    if (validDirs > 0)
                    {
                        validPoints++;
                        if (insideDirs * 2 > validDirs) insidePoints++;
                    }
                }

                if (validPoints > 0 && insidePoints * 2 > validPoints) depth++;
            }
            return depth;
        }

        // ─────────────────────────── 分类与汇总 ───────────────────────────

        static void Classify(List<ShellInfo> infos, UnitContext units, VolumeReport r)
        {
            double material = 0.0, cavity = 0.0, envelope = 0.0;
            bool anyVoid = false;

            foreach (var s in infos)
            {
                if (s.Depth % 2 == 0)
                {
                    material += s.CubicUnits;
                    if (s.Depth == 0) envelope += s.CubicUnits;
                    else cavity -= s.CubicUnits;   // 空腔中的实体岛
                }
                else
                {
                    material -= s.CubicUnits;
                    cavity += s.CubicUnits;
                    anyVoid = true;
                }
            }

            if (material < 0.0) material = 0.0;
            if (cavity < 0.0) cavity = 0.0;

            r.ShellCount = infos.Count;
            r.MaterialMl = units.ToMilliliters(material);
            r.LiquidMl = units.ToMilliliters(cavity);
            r.EnvelopeMl = units.ToMilliliters(envelope);
            r.HasCavity = anyVoid && cavity > 0.0;

            if (!r.Closed)
                r.Note = "存在未闭合曲面：已尝试自动封口，结果为近似值。";
            else if (r.HasCavity)
                r.Note = "检测到封闭内腔，液体容量即内腔容积。";
            else if (r.WasCapped)
                r.Note = "开放曲面已自动封口，所得体积为曲面所围区域的容积。";
            else if (r.Approximate)
                r.Note = "部分壳体以网格近似计算体积。";
            else
                r.Note = "物件为实心体，未检测到独立内腔。";
        }

        // ─────────────────────── 壁厚扣减（向内偏置） ───────────────────────

        /// <summary>
        /// 把 Brep 向内偏置 distance，返回内部核心体积。
        /// solid=true 时用 Rhino 的实体偏置（会得到「外壳+内壳」的空心体，取内腔）；
        /// solid=false 时只偏置曲面本体，取所围体积。
        /// </summary>
        static double OffsetCoreVolume(Brep brep, double distance, bool solid, double tol,
                                       UnitContext units, MeshingParameters mp,
                                       out bool approximate, out string error, out Brep joinedResult)
        {
            approximate = false;
            error = "";
            joinedResult = null;
            Brep merged = null;

            Brep[] results = null;
            try
            {
                Brep[] blends;
                Brep[] walls;
                results = Brep.CreateOffsetBrep(brep, distance, solid, false, tol, out blends, out walls);

                if (results != null && results.Length > 0)
                {
                    // 把偏置结果合并成一个 Brep，交给同一套壳体分析
                    var all = new List<Brep>();
                    foreach (var b in results) if (b != null) all.Add(b);
                    if (!solid && blends != null)
                    {
                        foreach (var b in blends) if (b != null) all.Add(b);
                    }

                    if (all.Count == 1)
                    {
                        results = all.ToArray();
                        merged = all[0];
                    }
                    else if (all.Count > 1)
                    {
                        Brep[] joined = null;
                        try { joined = Brep.JoinBreps(all, tol); } catch { joined = null; }
                        if (joined != null && joined.Length > 0)
                        {
                            results = joined;
                            if (joined.Length == 1) merged = joined[0];
                        }
                        else
                        {
                            results = all.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 0.0;
            }

            if (results == null || results.Length == 0)
            {
                if (string.IsNullOrEmpty(error)) error = "偏置没有产生任何结果";
                return 0.0;
            }

            double material = 0.0, cavity = 0.0;
            bool hasCavity = false;

            foreach (var b in results)
            {
                if (b == null) continue;

                var sub = new VolumeReport();
                AnalyzeGeometry(b, sub, units, mp);
                if (!sub.Ok) continue;

                material += sub.MaterialMl;
                cavity += sub.LiquidMl;
                hasCavity = hasCavity || sub.HasCavity;
                approximate = approximate || sub.Approximate;
            }

            // 偏向保守：有内腔就用内腔（实体偏置得到空心的壳，其内腔才是核心体积）
            double core = hasCavity ? cavity : material;
            if (!(core > 0.0) && string.IsNullOrEmpty(error)) error = "偏置结果无法计算体积";
            joinedResult = merged;
            return core;
        }

        // ─────────────────────── 内腔实体 / 雾化芯扣除 ───────────────────────

        /// <summary>
        /// 从 Brep 里取出「内腔实体」：嵌套层数为奇数、体积最大的那个闭合壳体。
        /// 它自己围成的体积就是内腔容积，可直接拿去做布尔运算。
        /// </summary>
        public static Brep FindCavitySolid(Brep brep, double tol, MeshingParameters mp)
        {
            try
            {
                var shells = SplitShells(brep);
                if (shells.Count < 2) return null;

                var infos = new List<ShellInfo>();
                foreach (var s in shells)
                {
                    var info = new ShellInfo();
                    Brep cur = s;
                    bool closed;
                    try { closed = cur.IsSolid; } catch { closed = false; }

                    if (!closed)
                    {
                        try
                        {
                            Brep capped = cur.CapPlanarHoles(tol);
                            if (capped != null && capped.IsSolid) { cur = capped; closed = true; }
                        }
                        catch
                        {
                            // 封口失败就按未闭合处理
                        }
                    }

                    double v = 0.0;
                    if (closed)
                    {
                        try
                        {
                            var vmp = VolumeMassProperties.Compute(cur);
                            if (vmp != null) v = Math.Abs(vmp.Volume);
                        }
                        catch
                        {
                            v = 0.0;
                        }
                    }

                    info.Closed = closed;
                    info.CubicUnits = v;
                    info.BrepRef = cur;
                    info.Samples = SamplePointsFromBrep(cur, mp);
                    infos.Add(info);
                }

                for (int i = 0; i < infos.Count; i++)
                    infos[i].Depth = BrepDepth(infos, i, tol);

                ShellInfo best = null;
                foreach (var s in infos)
                {
                    if (s.Depth % 2 != 1 || !s.Closed) continue;
                    if (best == null || s.CubicUnits > best.CubicUnits) best = s;
                }
                if (best == null || best.BrepRef == null) return null;

                Brep solid = best.BrepRef.DuplicateBrep();
                try
                {
                    var vmp = VolumeMassProperties.Compute(solid);
                    if (vmp != null && vmp.Volume < 0.0) solid.Flip();   // 统一朝外，便于布尔运算
                }
                catch
                {
                    // 翻转失败不影响后续体积计算
                }
                return solid;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 雾化芯：在物件中心沿指定轴向拉一个圆柱，算出它落在「液体区域」内的体积。
        /// 先做布尔交集精确求体积；布尔不可用或拿不到内腔实体时，退回
        /// 「中心线贯穿长度 × 截面积」估算，并把结果标为近似。
        /// </summary>
        public static CoilResult ComputeCoil(Brep liquidSolid, BoundingBox box, double diameterMm,
                                             CoilAxis axis, UnitContext units, MeshingParameters mp)
        {
            var r = new CoilResult();
            if (!(diameterMm > 0.0))
            {
                r.Ok = true;
                r.Note = "雾化芯直径为 0，未扣除。";
                return r;
            }
            if (!box.IsValid)
            {
                r.Error = "物件包围盒无效，无法放置雾化芯。";
                return r;
            }

            double unitsPerMm = Math.Max(units.MillimetersPerUnit, 1.0e-12);
            double radius = (diameterMm * 0.5) / unitsPerMm;
            double radiusMm = diameterMm * 0.5;
            Vector3d dir = axis == CoilAxis.X ? Vector3d.XAxis
                         : axis == CoilAxis.Y ? Vector3d.YAxis
                         : Vector3d.ZAxis;

            Point3d center = box.Center;
            double length = Math.Max(box.Diagonal.Length * 1.6, radius * 8.0);

            Brep cylinder;
            try
            {
                var cyl = new Cylinder(new Circle(new Plane(center - dir * (length * 0.5), dir), radius), length);
                cylinder = cyl.ToBrep(true, true);
            }
            catch (Exception ex)
            {
                r.Error = "生成雾化芯圆柱失败：" + ex.Message;
                return r;
            }
            if (cylinder == null)
            {
                r.Error = "生成雾化芯圆柱失败。";
                return r;
            }

            double tol = SafeTolerance();
            double sectionMm2 = Math.PI * radiusMm * radiusMm;

            // 1) 精确：圆柱 ∩ 液体区域
            if (liquidSolid != null)
            {
                try
                {
                    var inter = Brep.CreateBooleanIntersection(new[] { cylinder }, new[] { liquidSolid }, tol);
                    if (inter != null && inter.Length > 0)
                    {
                        double v = 0.0;
                        foreach (var b in inter)
                        {
                            if (b == null) continue;
                            var vmp = VolumeMassProperties.Compute(b);
                            if (vmp != null) v += Math.Abs(vmp.Volume);
                        }
                        if (v > 0.0)
                        {
                            r.Ok = true;
                            r.VolumeMl = units.ToMilliliters(v);
                            r.LengthMm = r.VolumeMl * 1000.0 / Math.Max(sectionMm2, 1.0e-12);
                            r.Note = string.Format(
                                "雾化芯 ⌀{0:0.##} mm，在内腔中长约 {1:0.##} mm，按布尔交集精确扣除。",
                                diameterMm, r.LengthMm);
                            return r;
                        }
                    }
                }
                catch
                {
                    // 落到下面的估算
                }
            }

            // 2) 兜底：中心线贯穿长度 × 截面积
            double span;
            if (liquidSolid != null)
            {
                span = CenterlineSpan(liquidSolid, center, dir);
                r.Approximate = true;
                r.Note = "布尔运算不可用，按中心线贯穿长度 × 截面积估算雾化芯体积（近似）。";
            }
            else
            {
                span = LengthAlongAxis(box, axis);
                r.Approximate = true;
                r.Note = "拿不到内腔实体，按物件轴向包围盒长度估算雾化芯体积（近似）。";
            }

            if (!(span > 0.0))
            {
                r.Error = "无法确定雾化芯在物件内的长度。";
                return r;
            }

            double area = Math.PI * radius * radius;         // 模型单位²
            r.Ok = true;
            r.VolumeMl = units.ToMilliliters(area * span);
            r.LengthMm = span * unitsPerMm;
            r.Note += string.Format("（⌀{0:0.##} mm × {1:0.##} mm = {2}）",
                diameterMm, r.LengthMm, Fmt.Ml(r.VolumeMl, 3));
            return r;
        }

        /// <summary>沿轴线取中心线在实体内部的贯穿长度（模型单位）。</summary>
        static double CenterlineSpan(Brep solid, Point3d center, Vector3d dir)
        {
            try
            {
                var box = solid.GetBoundingBox(true);
                if (!box.IsValid) return 0.0;

                double half = box.Diagonal.Length * 1.4;
                if (!(half > 0.0)) return 0.0;

                var curve = new LineCurve(center - dir * half, center + dir * half);
                Curve[] overlaps;
                Point3d[] points;
                if (!Intersection.CurveBrep(curve, solid, SafeTolerance(), out overlaps, out points))
                    return 0.0;
                if (points == null || points.Length == 0) return 0.0;

                double total = curve.GetLength();
                if (!(total > 0.0)) return 0.0;

                var ts = new List<double>();
                foreach (var p in points)
                {
                    double t;
                    if (curve.ClosestPoint(p, out t)) ts.Add(t);
                }
                if (ts.Count == 0) return 0.0;
                ts.Sort();

                double prev = 0.0;
                for (int i = 0; i <= ts.Count; i++)
                {
                    double next = i < ts.Count ? ts[i] : 1.0;
                    if (0.5 >= prev && 0.5 <= next) return (next - prev) * total;
                    prev = next;
                }
                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        static double LengthAlongAxis(BoundingBox box, CoilAxis axis)
        {
            switch (axis)
            {
                case CoilAxis.X: return box.Max.X - box.Min.X;
                case CoilAxis.Y: return box.Max.Y - box.Min.Y;
                default: return box.Max.Z - box.Min.Z;
            }
        }

        /// <summary>Brep 优先；网格则转成 Brep（面数过多时不转，避免卡顿）。</summary>
        public static Brep ToBrepAny(GeometryBase geom)
        {
            Brep b = ToBrep(geom);
            if (b != null) return b;

            var mesh = geom as Mesh;
            if (mesh != null)
            {
                try
                {
                    if (mesh.Faces.Count <= 200000)
                        return Brep.CreateFromMesh(mesh.DuplicateMesh(), true);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// 面积法估算「扣壁厚后的核心体积」：V_core ≈ V − A×t。
        /// 对薄壁容器是很好的近似（凸体上是精确到 O(t²) 的展开），不依赖偏置运算，
        /// 作为偏置失败时的兜底。
        /// </summary>
        static double AreaEstimateCore(double materialMl, double t, UnitContext units, Brep brep, out double areaMm2)
        {
            areaMm2 = 0.0;
            try
            {
                var amp = AreaMassProperties.Compute(brep);
                if (amp == null) return 0.0;

                double area = Math.Abs(amp.Area);                                              // 模型单位²
                double volume = materialMl / Math.Max(units.MillilitersPerCubicUnit, 1.0e-30);  // 立方模型单位

                double mmPerUnit = Math.Max(units.MillimetersPerUnit, 1.0e-12);
                areaMm2 = area * mmPerUnit * mmPerUnit;

                double core = volume - area * t;
                if (!(core > 0.0)) return 0.0;
                return units.ToMilliliters(core);
            }
            catch
            {
                return 0.0;
            }
        }

        // ─────────────────────────── 工具方法 ───────────────────────────

        public static Brep ToBrep(GeometryBase geom)
        {
            if (geom == null) return null;
            var subd = geom as SubD;
            // 注意：Rhino 7.0 里 SubD.ToBrep() 无参重载还不存在，必须显式传选项，
            // 所以这里统一用带参写法（7.0 与 8.x 都支持）。
            if (subd != null) return subd.ToBrep(SubDToBrepOptions.Default);
            var surface = geom as Surface;
            if (surface != null) return surface.ToBrep();
            return Brep.TryConvertBrep(geom);
        }

        public static double SafeTolerance()
        {
            double tol = 0.001;
            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc != null)
                {
                    double t = doc.ModelAbsoluteTolerance;
                    if (t > 0.0 && !double.IsNaN(t) && !double.IsInfinity(t)) tol = t;
                }
            }
            catch
            {
                tol = 0.001;
            }
            return tol;
        }

        static Mesh JoinBrepMesh(Brep b, MeshingParameters mp)
        {
            try
            {
                Mesh[] parts = Mesh.CreateFromBrep(b, mp ?? MeshingParameters.Default);
                if (parts == null || parts.Length == 0) return null;

                var m = new Mesh();
                foreach (var p in parts)
                {
                    if (p != null) m.Append(p);
                }
                if (m.Vertices.Count == 0)
                {
                    m.Dispose();
                    return null;
                }
                return m;
            }
            catch
            {
                return null;
            }
        }

        static double MeshVolume(Brep b, MeshingParameters mp)
        {
            var m = JoinBrepMesh(b, mp);
            if (m == null) return 0.0;
            double v = 0.0;
            try
            {
                if (m.IsClosed) v = Math.Abs(m.Volume());
            }
            catch
            {
                v = 0.0;
            }
            m.Dispose();
            return v;
        }

        static List<Point3d> SamplePointsFromBrep(Brep b, MeshingParameters mp)
        {
            var pts = new List<Point3d>();
            var m = JoinBrepMesh(b, mp);
            if (m != null)
            {
                pts.AddRange(SampleMeshPoints(m, SampleCount));
                m.Dispose();
            }
            if (pts.Count == 0)
            {
                try
                {
                    var bb = b.GetBoundingBox(true);
                    if (bb.IsValid) pts.Add(bb.Center);
                }
                catch
                {
                    // 无采样点时嵌套判定退化为第 0 层
                }
            }
            return pts;
        }

        static List<Point3d> SampleMeshPoints(Mesh m, int count)
        {
            var pts = new List<Point3d>();
            try
            {
                int c = m.Vertices.Count;
                if (c > 0)
                {
                    int n = Math.Min(count, c);
                    for (int k = 0; k < n; k++)
                    {
                        int idx = n <= 1 ? 0 : (int)Math.Round((double)k * (c - 1) / (n - 1));
                        if (idx < 0) idx = 0;
                        if (idx >= c) idx = c - 1;
                        Point3f v = m.Vertices[idx];
                        pts.Add(new Point3d(v.X, v.Y, v.Z));
                    }
                }

                var bb = m.GetBoundingBox(true);
                if (bb.IsValid) pts.Add(bb.Center);
            }
            catch
            {
                // 采样失败时返回空列表
            }
            return pts;
        }
    }
}
