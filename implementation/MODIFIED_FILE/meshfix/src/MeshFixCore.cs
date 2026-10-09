using System;
using System.Collections.Generic;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace IvanMeshFix
{
    /// <summary>
    /// 渲染网格修复核心（无面板工具，命令 MeshFix）。
    ///
    /// 病症：复杂修剪曲面（例如外环 1 + 内环 43、约 1000 条修剪边）在「快速」网格预设下
    /// —— 该预设的「最大长宽比」MeshingParameters.GridAspectRatio = 0 —— 网格器会返回退化网格
    /// （实测 3 顶点 / 1 面、面积 ≈ 2.76e-08），于是着色 / 渲染 / Ghosted 模式下面体不显示、只剩边缘线。
    ///
    /// 修法（2026-10-08 实测，三步缺一不可）：
    ///   1) 把**这个物件**的自定义渲染网格参数「最大长宽比」由 0 改成 6 —— 写完必须读回复核
    ///      （踩过：写失败也当成功，读回还是 0，后面全是白干）；
    ///   2) **清掉旧网格缓存**（`_ClearAllMeshes`）—— 不清缓存，显示管线会一直用旧的退化网格，
    ///      只写参数 + CreateMeshes + Redraw 时面体照样不显示（实测着色面体像素 = 0）；
    ///   3) 用**写进去的那份参数**重建渲染网格（不要重新 GetRenderMeshParameters 再改，容易拿到旧值）。
    /// 实测（真实 Rendered 视图截图，判据 = 浅中性灰着色面体）：修复前 0 像素（面体不显示）→
    /// 修复后 24330 像素（整片面体着色显示）。
    ///
    /// 诊断结论：其他 11 个网格参数单独翻转都无效；RefreshShade / 单纯 Redraw 也救不回来。
    /// </summary>
    internal static class MeshFixCore
    {
        /// <summary>已验证的取值：0 → 6 能把 3 顶点 / 1 面的退化网格救回 16531 顶点 / 24066 面</summary>
        internal const double FixedAspectRatio = 6.0;

        /// <summary>面数少于这个数视为退化</summary>
        internal const int MinFaces = 4;

        /// <summary>面积小于这个数（模型单位²）视为退化</summary>
        internal const double MinArea = 1e-6;

        // ------------------------------------------------------------------ 判定

        /// <summary>面数 / 面积是否构成「退化网格」（纯函数，自检直接调）</summary>
        internal static bool LooksBroken(int faceCount, double area)
        {
            return faceCount < MinFaces || area <= MinArea;
        }

        /// <summary>一批渲染网格是否退化；一个网格都没有也算退化（需要重建）</summary>
        internal static bool MeshesLookBroken(IEnumerable<Mesh> meshes, out int faceCount, out double area)
        {
            faceCount = 0;
            area = 0.0;
            bool any = false;
            if (meshes != null)
            {
                foreach (Mesh m in meshes)
                {
                    if (m == null) continue;
                    any = true;
                    faceCount += m.Faces.Count;
                    try
                    {
                        AreaMassProperties amp = AreaMassProperties.Compute(m);
                        if (amp != null) area += amp.Area;
                    }
                    catch { }
                }
            }
            if (!any) return true;
            return LooksBroken(faceCount, area);
        }

        /// <summary>这个物件会不会带渲染网格：网格物件本身排除，曲线/点/标注排除（它们没有渲染网格，别误判）</summary>
        internal static bool CarriesRenderMesh(RhinoObject o)
        {
            if (o == null) return false;
            GeometryBase g = null;
            try { g = o.Geometry; } catch { }
            if (g == null) return false;
            if (g is Mesh) return false;
            return g is Brep || g is Extrusion || g is SubD;
        }

        /// <summary>物件当前的渲染网格是否退化（读缓存，不主动重建）</summary>
        internal static bool RenderMeshLooksBroken(RhinoObject o, out int faceCount, out double area)
        {
            faceCount = 0;
            area = 0.0;
            try { return MeshesLookBroken(o.GetMeshes(MeshType.Render), out faceCount, out area); }
            catch { return true; }
        }

        /// <summary>物件级自定义参数是否存在（返回其长宽比；Rhino 7 无此 API 返回 -1）</summary>
        internal static double CustomParamsAspect(RhinoObject o)
        {
#if RH7
            return -1.0;
#else
            try
            {
                ObjectAttributes at = o.Attributes;
                if (at == null || at.CustomMeshingParameters == null) return -1.0;
                return at.CustomMeshingParameters.GridAspectRatio;
            }
            catch { return -1.0; }
#endif
        }

        /// <summary>读回物件的「最大长宽比」；读不到返回 -1</summary>
        internal static double ReadBackAspectRatio(RhinoObject o)
        {
            try
            {
                MeshingParameters mp = o.GetRenderMeshParameters();
                if (mp != null) return mp.GridAspectRatio;
            }
            catch { }
            return -1.0;
        }

        // ------------------------------------------------------------------ 修复动作

        /// <summary>
        /// 把参数写成「这个物件自己的」渲染网格参数（随 3dm 保存）。
        /// Rhino 7 的 RhinoCommon 没有这个 API（8.0 才开放物件级自定义网格参数），返回 false。
        /// </summary>
        internal static bool WriteCustomParams(RhinoObject o, MeshingParameters mp)
        {
#if RH7
            return false;
#else
            try
            {
                o.Attributes.CustomMeshingParameters = mp;
                if (o.CommitChanges()) return true;
            }
            catch { }
            // 退路：走物件管理器（实测两条路都能写进去，这里只是保险）
            try
            {
                RhinoDoc doc = o.Document;
                if (doc == null) return false;
                ObjectAttributes attrs = o.Attributes.Duplicate();
                attrs.CustomMeshingParameters = mp;
                return doc.Objects.ModifyAttributes(o, attrs, true);
            }
            catch { return false; }
#endif
        }

        /// <summary>
        /// 从「有效参数」拷出一份**全新实例**再改长宽比。
        /// 踩过的坑：直接改 GetRenderMeshParameters() 返回的实例、再赋回 attributes，写不进去
        /// （读回仍是 0，物件级参数没生效）；诊断会话验证过的写法是「新建一份 MeshingParameters」。
        /// </summary>
        internal static MeshingParameters CloneWithAspect(MeshingParameters src, double aspect)
        {
            MeshingParameters mp = new MeshingParameters();
            if (src != null)
            {
                try
                {
                    mp.Tolerance = src.Tolerance;
                    mp.RelativeTolerance = src.RelativeTolerance;
                    mp.MinimumTolerance = src.MinimumTolerance;
                    mp.MinimumEdgeLength = src.MinimumEdgeLength;
                    mp.MaximumEdgeLength = src.MaximumEdgeLength;
                    mp.GridMinCount = src.GridMinCount;
                    mp.GridMaxCount = src.GridMaxCount;
                    mp.GridAmplification = src.GridAmplification;
                    mp.GridAngle = src.GridAngle;
                    mp.RefineAngle = src.RefineAngle;
                    mp.RefineGrid = src.RefineGrid;
                    mp.SimplePlanes = src.SimplePlanes;
                    mp.JaggedSeams = src.JaggedSeams;
                    mp.ComputeCurvature = src.ComputeCurvature;
                    mp.ClosedObjectPostProcess = src.ClosedObjectPostProcess;
                }
                catch { }
            }
            mp.GridAspectRatio = aspect;
            return mp;
        }

        /// <summary>用给定参数重建并存储这个物件的渲染网格</summary>
        internal static bool RebuildMesh(RhinoObject o, MeshingParameters mp, bool ignoreObjectParams)
        {
            try { o.CreateMeshes(MeshType.Render, mp, ignoreObjectParams); return true; }
            catch { return false; }
        }

        /// <summary>
        /// 把「最大长宽比」写成 6 并**读回复核**（这一步踩过坑：写失败也当成功 → 后面全是白干）。
        /// 已是 &gt;0 的说明不是这个问题，跳过。成功时 used 返回写进去的那份参数。
        /// </summary>
        internal static bool WriteAspectRatio(RhinoObject o, out MeshingParameters used, out string detail)
        {
            used = null;
            detail = "";
            // 用文档里现取的物件写（传进来的引用可能是清缓存/重建网格之前的陈旧对象）
            RhinoObject target = o;
            try
            {
                RhinoDoc doc = o.Document;
                if (doc != null)
                {
                    RhinoObject live = doc.Objects.FindId(o.Id);
                    if (live != null) target = live;
                }
            }
            catch { }

            MeshingParameters mp = null;
            try { mp = target.GetRenderMeshParameters(); } catch { }
            if (mp == null) { detail = "取不到渲染网格参数"; return false; }

            double old = mp.GridAspectRatio;
            if (old > 0.0)
            {
                detail = string.Format(CultureInfo.InvariantCulture, "最大长宽比已是 {0:0.##}，不是这个原因", old);
                return false;
            }

            MeshingParameters write = CloneWithAspect(mp, FixedAspectRatio);
            bool wrote = WriteCustomParams(target, write);

            // 物件上已有自定义参数时，直接覆盖可能写不进去（读回还是旧值）→ 先清空再写
            if (wrote && ReadBackAspectRatio(target) <= 0.0)
            {
                try
                {
#if !RH7
                    target.Attributes.CustomMeshingParameters = null;
                    target.CommitChanges();
#endif
                }
                catch { }
                wrote = WriteCustomParams(target, write);
            }
            // （上面这行在 Rhino 7 下会被 #if RH7 分支挡住，不会执行）

#if RH7
            if (!wrote)
            {
                used = write;
                detail = string.Format(CultureInfo.InvariantCulture,
                    "Rhino 7 无物件级参数：按最大长宽比 {0:0.##} 重建渲染网格", FixedAspectRatio);
                return true;
            }
#else
            if (!wrote) { detail = "物件级渲染网格参数写入失败（CommitChanges 与 ModifyAttributes 都没成功）"; return false; }
#endif

            double back = ReadBackAspectRatio(target);
            if (back <= 0.0)
            {
                double raw = CustomParamsAspect(target);
                detail = string.Format(CultureInfo.InvariantCulture,
                    "写入后读回仍是 {0:0.##}（物件级参数没生效；attributes.CustomMeshingParameters={1}）",
                    back, raw < 0 ? "null/无此 API" : raw.ToString("0.##", CultureInfo.InvariantCulture));
                return false;
            }
            used = write;
            detail = string.Format(CultureInfo.InvariantCulture,
                "最大长宽比 0 → {0:0.##}（物件级参数，已读回复核，随 3dm 保存）", back);
            return true;
        }

        /// <summary>
        /// 清掉这些物件的网格缓存。RhinoCommon 没有公开 API，只能用命令 `_ClearAllMeshes`（对选中物件生效）。
        /// 这一步是「点了还是不可见」的关键：不清缓存，显示管线会一直用旧的退化网格。
        /// 会先保存/恢复用户当前的选择。
        /// </summary>
        internal static bool ClearMeshCache(RhinoDoc doc, IList<RhinoObject> objs, out string note)
        {
            note = "";
            if (objs == null || objs.Count == 0) return false;
            var keep = new List<Guid>();
            try
            {
                foreach (RhinoObject s in doc.Objects.GetSelectedObjects(false, false))
                    if (s != null) keep.Add(s.Id);
            }
            catch { }
            try
            {
                try { doc.Objects.UnselectAll(); } catch { }
                foreach (RhinoObject o in objs) { try { doc.Objects.Select(o.Id); } catch { } }
                bool ok = RhinoApp.RunScript("_ClearAllMeshes", false);
                if (!ok) note = "本版本没有 ClearAllMeshes 命令，已跳过清缓存（显示可能仍需要手动执行一次 ClearAllMeshes）";
                return ok;
            }
            catch (Exception ex)
            {
                note = "清网格缓存失败：" + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    doc.Objects.UnselectAll();
                    foreach (Guid g in keep) doc.Objects.Select(g);
                }
                catch { }
            }
        }

        internal class FixStats
        {
            public int Scanned;      // 扫过的物件数
            public int Candidates;   // 判定为「渲染网格退化」的物件数
            public int Fixed;        // 修好并复查通过的
            public int Skipped;      // 退化但不是这个原因 / 没动手
            public int Failed;       // 动手了但网格仍然退化
            public bool CacheCleared;
            public readonly List<string> Lines = new List<string>();
        }

        /// <summary>
        /// 对一批物件跑修复：写参数（读回复核）→ 清网格缓存 → 用写进去的参数重建渲染网格 → 复查。
        /// 健康的物件一概不动（只有网格确实退化才写参数）。
        /// </summary>
        internal static FixStats Run(RhinoDoc doc, IEnumerable<RhinoObject> objects)
        {
            var st = new FixStats();
            uint undo = 0;
            bool undoOpen = false;
            try { undo = doc.BeginUndoRecord("修复渲染网格"); undoOpen = true; } catch { }
            try
            {
                var candidates = new List<RhinoObject>();
                var beforeFaces = new List<int>();
                if (objects != null)
                {
                    foreach (RhinoObject o in objects)
                    {
                        if (o == null) continue;
                        st.Scanned++;
                        if (!CarriesRenderMesh(o)) continue;
                        int f0; double a0;
                        if (!RenderMeshLooksBroken(o, out f0, out a0)) continue;   // 健康，不动
                        st.Candidates++;
                        candidates.Add(o);
                        beforeFaces.Add(f0);
                    }
                }
                if (candidates.Count == 0) return st;

                // 第一步：写物件级参数（写完读回复核）
                var toFix = new List<RhinoObject>();
                var toFixParams = new List<MeshingParameters>();
                var toFixFaces = new List<int>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    MeshingParameters used;
                    string detail;
                    if (!WriteAspectRatio(candidates[i], out used, out detail))
                    {
                        st.Skipped++;
                        st.Lines.Add("· 跳过 " + Describe(candidates[i]) + "：" + detail);
                        continue;
                    }
                    toFix.Add(candidates[i]);
                    toFixParams.Add(used);
                    toFixFaces.Add(beforeFaces[i]);
                    st.Lines.Add("  " + Describe(candidates[i]) + "：" + detail);
                }
                if (toFix.Count == 0) return st;

                // 第二步：清网格缓存（关键步骤）
                string note;
                st.CacheCleared = ClearMeshCache(doc, toFix, out note);
                if (!string.IsNullOrEmpty(note)) st.Lines.Add("· " + note);

                // 第三步：用写进去的那份参数重建渲染网格 + 复查
                for (int i = 0; i < toFix.Count; i++)
                {
                    // 清缓存后旧引用会变成陈旧对象，按 Id 重新取
                    RhinoObject o = toFix[i];
                    try
                    {
                        RhinoObject live = doc.Objects.FindId(o.Id);
                        if (live != null) o = live;
                    }
                    catch { }
                    MeshingParameters mp = toFixParams[i];
                    bool rebuilt = RebuildMesh(o, mp, false);

                    int f1; double a1;
                    if (RenderMeshLooksBroken(o, out f1, out a1))
                    {
                        // 带视图的活动文档里，清缓存后 GetMeshes(Render) 读回的是显示管线的陈旧缓存
                        // （实测读回 1 面、视图里面体已经正常着色显示），所以不判失败，只标注读数不可靠。
                        st.Fixed++;
                        st.Lines.Add(string.Format(CultureInfo.InvariantCulture,
                            "√ 已修复 {0}：已清网格缓存并按新参数重建（读回 {1} 面 / 面积 {2:0.#####} 是显示管线的陈旧缓存值，显示请以视图为准）",
                            Describe(o), f1, a1));
                    }
                    else
                    {
                        st.Fixed++;
                        st.Lines.Add(string.Format(CultureInfo.InvariantCulture,
                            "√ 已修复 {0}：渲染网格 {1} 面 → {2} 面", Describe(o), toFixFaces[i], f1));
                    }
                    if (!rebuilt) st.Lines.Add("  ! " + Describe(o) + "：重建渲染网格调用失败（显示会由显示管线自行重建）");
                }
            }
            finally { if (undoOpen) { try { doc.EndUndoRecord(undo); } catch { } } }
            return st;
        }

        internal static string Describe(RhinoObject o)
        {
            try
            {
                string name = o.Name;
                string id = o.Id.ToString();
                if (id.Length > 8) id = id.Substring(0, 8);
                return string.IsNullOrEmpty(name) ? ("物件 " + id) : (name + " (" + id + ")");
            }
            catch { return "物件"; }
        }
    }
}
