using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;   // [assembly: Guid] 需要（否则 Guid 解析成 System.Guid）
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using Rhino.PlugIns;

// Rhino 按「程序集级」[assembly: Guid] 识别插件 ID；只写类级 [Guid] 会被登记成全零 GUID。
#if RH7
[assembly: Guid("7E4B1C85-9A37-4D62-8F10-2C6B5E8D4A93")]
#else
[assembly: Guid("5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72")]
#endif

namespace VoronoiTexture
{
    /// <summary>插件入口：泰森多边形纹（Rhino 7 / 8 用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("7E4B1C85-9A37-4D62-8F10-2C6B5E8D4A93")]
#else
    [Guid("5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72")]
#endif
    public class VoronoiTexturePlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            RhinoApp.WriteLine("泰森多边形纹插件已加载。命令：VoronoiTexture");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-voronoi-selftest.flag，
        /// 下次启动 Rhino 自动跑几何自检并写报告到同目录 VoronoiSelfTest.txt，跑完退出。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-voronoi-selftest.flag");
                if (!File.Exists(flag)) return;
                File.Delete(flag);
                RhinoApp.Idle += SelfTestOnIdle;
            }
            catch { }
        }

        static void SelfTestOnIdle(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= SelfTestOnIdle;
                VoronoiSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, VoronoiSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("泰森多边形自检失败：" + ex.Message); } catch { }
            }
        }
    }

    public class VoronoiTextureCommand : Command
    {
        public override string EnglishName { get { return "VoronoiTexture"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode) { return VoronoiCore.Run(doc); }
    }

    /// <summary>面板上「选择边界」按钮调用的隐藏命令（面板是非模态的，GetObject 只能在命令里跑）</summary>
    public class VoronoiPickTargetCommand : Command
    {
        public override string EnglishName { get { return "VoronoiPickTarget"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            VoronoiSession session = VoronoiSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 VoronoiTexture 打开参数面板。");
                return Result.Nothing;
            }
            var go = new GetObject();
            go.SetCommandPrompt("选择边界：平面 / 平面曲面 / 闭合平面曲线（可多选，回车结束）");
            go.GeometryFilter = ObjectType.Curve | ObjectType.Surface | ObjectType.Brep;
            go.SubObjectSelect = false;
            go.GetMultiple(1, 0);
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            var ids = new List<Guid>();
            for (int i = 0; i < go.ObjectCount; i++)
            {
                ObjRef objRef = go.Object(i);
                if (objRef != null && objRef.ObjectId != Guid.Empty) ids.Add(objRef.ObjectId);
            }
            if (ids.Count == 0) { RhinoApp.WriteLine("没有选到物件。"); return Result.Failure; }

            session.SetTargets(doc, ids, VoronoiCore.DescribeTargets(doc, ids));
            RhinoApp.WriteLine("边界已更新：" + VoronoiCore.DescribeTargets(doc, ids));
            return Result.Success;
        }
    }

    /// <summary>面板上「选择渐变物件」按钮调用的隐藏命令（胞元从该物件向外逐渐变化）</summary>
    public class VoronoiPickGradientCommand : Command
    {
        public override string EnglishName { get { return "VoronoiPickGradient"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            VoronoiSession session = VoronoiSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 VoronoiTexture 打开参数面板。");
                return Result.Nothing;
            }
            var go = new GetObject();
            go.SetCommandPrompt("选择渐变参考物件（胞元从它向外逐渐变化；点 / 曲线 / 曲面 / 网格都行）");
            go.GeometryFilter = ObjectType.AnyObject;
            go.SubObjectSelect = false;
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            ObjRef objRef = go.Object(0);
            RhinoObject obj = objRef != null ? objRef.Object() : null;
            if (obj == null || obj.Geometry == null)
            {
                session.SetGradientInvalid("没选到可用物件。");
                return Result.Failure;
            }
            Point3d c = VoronoiSession.ObjectCenter(doc, objRef.ObjectId);
            if (!c.IsValid)
            {
                session.SetGradientInvalid("这个物件拿不到中心点，换一个试试。");
                RhinoApp.WriteLine("拿不到这个物件的中心点，换一个试试。");
                return Result.Failure;
            }

            // 记住物件 ID（不是坐标）：之后移动这个物件，胞元分布会跟着实时更新
            session.SetGradient(doc, objRef.ObjectId, VoronoiCore.DescribeObject(doc, objRef));
            RhinoApp.WriteLine("渐变参考物件已设置：胞元从它向外逐渐变化（移动它会实时跟随）。");
            return Result.Success;
        }
    }

    internal static class VoronoiCore
    {
        public static Result Run(RhinoDoc doc)
        {
            // 预选的物件直接当边界；没预选就用空目标打开面板，由面板上的「选择边界」来选
            var ids = new List<Guid>();
            try
            {
                foreach (RhinoObject o in doc.Objects.GetSelectedObjects(false, false))
                    if (o != null && o.Id != Guid.Empty) ids.Add(o.Id);
            }
            catch { }

            var settings = new VoronoiSettings();
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            string desc = DescribeTargets(doc, ids);
            var session = new VoronoiSession(doc, ids, settings, desc);
            session.Start();
            RhinoApp.WriteLine(ids.Count > 0
                ? "参数面板已打开（已用选中的 " + ids.Count + " 个物件作为边界）。"
                : "参数面板已打开。点面板上的「选择边界」选平面或闭合平面曲线（可多选）。");
            return Result.Success;
        }

        internal const string EmptyTargetHint = "未选择边界 —— 点「选择边界」按钮选平面或闭合平面曲线";

        /// <summary>边界摘要（1 个就报名字，多个报数量 + 第一个）</summary>
        internal static string DescribeTargets(RhinoDoc doc, List<Guid> ids)
        {
            if (ids == null || ids.Count == 0) return EmptyTargetHint;
            RhinoObject first = null;
            try { first = doc.Objects.FindId(ids[0]); } catch { }
            if (ids.Count == 1) return "边界：" + PlanarBoundary.Describe(first);
            return string.Format("{0} 个边界（{1} 等）", ids.Count, PlanarBoundary.Describe(first));
        }

        internal static string DescribeObject(RhinoDoc doc, ObjRef objRef)
        {
            RhinoObject obj = objRef != null ? objRef.Object() : null;
            string name = obj != null ? obj.Name : null;
            string id = objRef != null ? objRef.ObjectId.ToString() : Guid.Empty.ToString();
            if (id.Length > 8) id = id.Substring(0, 8);
            string kind = (obj == null || obj.Geometry == null) ? "物件" : obj.Geometry.GetType().Name;
            return string.IsNullOrEmpty(name) ? (kind + " " + id) : (kind + "：" + name);
        }
    }

    /// <summary>一次「泰森多边形纹」会话：浮动面板 + 实时预览 + 文档事件刷新（不阻塞 Rhino）</summary>
    internal class VoronoiSession
    {
        static readonly List<VoronoiSession> _live = new List<VoronoiSession>();
        const int MaxPreviewCurves = 8000;      // 预览最多画这么多条线，多了只画一部分（面板里如实说明）

        readonly RhinoDoc _doc;
        readonly List<Guid> _targets;              // 平面边界（可多选）
        readonly VoronoiPanel _panel;
        readonly VoronoiPreviewConduit _conduit = new VoronoiPreviewConduit();
        readonly Timer _timer = new Timer();      // 参数改动的防抖
        readonly Timer _watch = new Timer();      // 渐变物件被拖动时的跟随轮询
        readonly Timer _recheck = new Timer();    // 删除事件延迟复查（移动/替换会先删后加）
        Guid _recheckId = Guid.Empty;
        int _recheckKind;                         // 1 = 边界，2 = 渐变物件
        Guid _gradientId = Guid.Empty;            // 渐变参考物件：记 ID 不记坐标，物件一动就能跟上
        Point3d _watchSeen = Point3d.Unset;       // 上一轮轮询看到的中心
        Point3d _watchApplied = Point3d.Unset;    // 已经按它重算过的中心
        bool _watchPending;
        List<VoronoiFaceResult> _results = new List<VoronoiFaceResult>();
        Mesh _merged = new Mesh();                // 合并焊接后的整张网格（输出/预览用同一份）
        VoronoiSmoothResult _smooth;
        bool _closed;

        public VoronoiSession(RhinoDoc doc, List<Guid> targets, VoronoiSettings settings, string desc)
        {
            _doc = doc;
            _targets = targets != null ? new List<Guid>(targets) : new List<Guid>();
            _panel = new VoronoiPanel(settings, desc);
            _panel.PickRequested += delegate { RunPick("_-VoronoiPickTarget"); };
            _panel.PickGradientRequested += delegate { RunPick("_-VoronoiPickGradient"); };
        }

        /// <summary>当前打开着的面板（拾取命令用它把新物件交给会话）</summary>
        internal static VoronoiSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        /// <summary>物件当前的中心点（每次现取：物件移动后中心就变了）</summary>
        internal static Point3d ObjectCenter(RhinoDoc doc, Guid id)
        {
            try
            {
                if (id == Guid.Empty) return Point3d.Unset;
                RhinoObject o = doc.Objects.FindId(id);
                if (o == null || o.Geometry == null) return Point3d.Unset;
                BoundingBox bb = o.Geometry.GetBoundingBox(true);
                if (!bb.IsValid) return Point3d.Unset;
                return bb.Center;
            }
            catch { return Point3d.Unset; }
        }

        /// <summary>目标里有多少个能解析成平面边界（选错类型时用来把按钮置红）</summary>
        internal static void ValidateTargets(RhinoDoc doc, List<Guid> ids, out int valid, out string firstWhy)
        {
            valid = 0; firstWhy = null;
            if (ids == null) return;
            for (int i = 0; i < ids.Count; i++)
            {
                RhinoObject o = null;
                try { o = doc.Objects.FindId(ids[i]); } catch { }
                PlanarBoundary b; string why;
                if (PlanarBoundary.TryResolve(o, out b, out why)) valid++;
                else if (firstWhy == null) firstWhy = why;
            }
        }

        void RunPick(string command)
        {
            try { RhinoApp.RunScript(command, false); }
            catch (Exception ex) { _panel.SetInfo("选择失败：" + ex.Message); }
        }

        /// <summary>设置/更新边界（拾取命令与命令预选都走这里）；选不到可用边界一律置红</summary>
        public void SetTargets(RhinoDoc doc, List<Guid> ids, string desc)
        {
            if (_closed) return;
            _targets.Clear();
            if (ids != null) _targets.AddRange(ids);
            _panel.SetTarget(desc);
            int valid; string why;
            ValidateTargets(doc, _targets, out valid, out why);
            _panel.SetTargetState(valid > 0);
            if (valid == 0 && _targets.Count > 0)
                _panel.SetInfo("所选对象不可用：" + (why ?? "无法解析成平面边界") + "\r\n请选平面 / 平面曲面 / 闭合平面曲线。");
            ClearPreview();
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        /// <summary>设置/更新胞元渐变的参考物件（记 ID；移动物件后预览会自动跟着变）</summary>
        public void SetGradient(RhinoDoc doc, Guid id, string desc)
        {
            if (_closed) return;
            Point3d c = ObjectCenter(doc, id);
            if (!c.IsValid) { SetGradientInvalid("这个物件拿不到中心点，换一个试试。"); return; }
            _gradientId = id;
            try
            {
                _panel.Settings.GradientOn = true;
                _panel.Settings.GradientPoint = c;
            }
            catch { }
            _watchSeen = c; _watchApplied = c; _watchPending = false;
            _panel.SetGradient(desc);
            _panel.SetGradientState(true);
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        /// <summary>渐变物件不可用（选错 / 拿不到中心点）：按钮置红 + 说明原因</summary>
        public void SetGradientInvalid(string why)
        {
            if (_closed) return;
            _panel.SetGradientState(false);
            _panel.SetInfo(why + "\r\n请选一个能取到中心点的物件（点 / 曲线 / 曲面 / 网格都行）。");
        }

        public void Start()
        {
            foreach (VoronoiSession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 140;
            _timer.Tick += delegate { _timer.Stop(); Rebuild(); };
            _panel.ValueChanged += delegate
            {
                if (!_panel.LivePreview) return;
                _timer.Stop(); _timer.Start();
            };
            _panel.FormClosed += delegate { OnClosed(); };

            // 渐变物件被 Gumball / 拖动编辑时不一定发文档事件 —— 400ms 轮询中心点兜底，
            // 等物件停下来（连续两次位置一致）再重算，避免拖动过程中每帧重算卡手。
            _watch.Interval = 400;
            _watch.Tick += delegate { WatchTick(); };
            _watch.Start();

            // 删除事件不能立刻当「真删了」：Move/Transform 这类操作在 Rhino 里常常是「先删旧对象、再加新对象」，
            // 延迟 250ms 复查一次，对象还在就说明只是移动/替换（继续用，别把渐变关掉）。
            _recheck.Interval = 250;
            _recheck.Tick += delegate { RecheckTick(); };
            _recheck.Stop();

            RhinoDoc.ReplaceRhinoObject += OnReplaceObject;
            RhinoDoc.DeleteRhinoObject += OnDeleteObject;

            _conduit.Enabled = true;
            _panel.Show();
            Rebuild();
            _panel.Activate();
            _live.Add(this);
        }

        void WatchTick()
        {
            if (_closed) return;
            try
            {
                if (_gradientId == Guid.Empty) return;
                if (!_panel.LivePreview || !_panel.Settings.GradientOn) return;
                Point3d c = ObjectCenter(_doc, _gradientId);
                if (!c.IsValid) return;
                double u2m = _panel.Settings.MmToModel > 0 ? _panel.Settings.MmToModel : 1.0;
                double tol = Math.Max(1e-6, Math.Max(_panel.Settings.Step, 0.05) * u2m * 0.1);
                if (_watchSeen.IsValid && c.DistanceTo(_watchSeen) > tol)
                {
                    _watchSeen = c;
                    _watchPending = true;      // 还在动：先记下，等停下来
                    return;
                }
                _watchSeen = c;
                bool changed = _watchPending || !_watchApplied.IsValid || c.DistanceTo(_watchApplied) > tol;
                if (!changed) return;
                _watchPending = false;
                _watchApplied = c;
                _panel.Settings.GradientPoint = c;
                Rebuild();
                try { _doc.Views.Redraw(); } catch { }
            }
            catch { }
        }

        void OnReplaceObject(object sender, RhinoReplaceObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (e.Document != null && e.Document.RuntimeSerialNumber != _doc.RuntimeSerialNumber) return;
                if (_targets.Contains(e.ObjectId) ||
                    (_gradientId != Guid.Empty && e.ObjectId == _gradientId)) Rebuild();
            }
            catch { }
        }

        void OnDeleteObject(object sender, RhinoObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (_targets.Contains(e.ObjectId)) { _recheckId = e.ObjectId; _recheckKind = 1; _recheck.Stop(); _recheck.Start(); return; }
                if (_gradientId != Guid.Empty && e.ObjectId == _gradientId)
                {
                    _recheckId = e.ObjectId; _recheckKind = 2; _recheck.Stop(); _recheck.Start();
                }
            }
            catch { }
        }

        /// <summary>删除事件 250ms 后复查：对象还在 = 只是移动/替换（照常继续）；真没了才按删除处理</summary>
        void RecheckTick()
        {
            _recheck.Stop();
            if (_closed) return;
            try
            {
                RhinoObject o = _recheckId == Guid.Empty ? null : _doc.Objects.FindId(_recheckId);
                if (o != null)
                {
                    // 还在：Move / Transform / 替换几何 —— 直接重算，不要关渐变、不要报「已删除」
                    _watchPending = false;
                    Rebuild();
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }
                if (_recheckKind == 2)
                {
                    _gradientId = Guid.Empty;
                    try
                    {
                        _panel.Settings.GradientOn = false;
                        _panel.Settings.GradientPoint = Point3d.Unset;
                    }
                    catch { }
                    _panel.SetGradient(null);
                    _panel.SetGradientState(false);
                    Rebuild();
                    _panel.SetInfo("渐变参考物件已被删除，渐变已关闭。");
                    RhinoApp.WriteLine("渐变参考物件已被删除，渐变已关闭。");
                    try { _doc.Views.Redraw(); } catch { }
                }
                else
                {
                    _targets.Remove(_recheckId);
                    _panel.SetTarget(VoronoiCore.DescribeTargets(_doc, _targets));
                    int valid; string why;
                    ValidateTargets(_doc, _targets, out valid, out why);   // 剩下的边界也要真能解析才算绿
                    _panel.SetTargetState(valid > 0);
                    ClearPreview();
                    Rebuild();
                    _panel.SetInfo(_targets.Count > 0 ? "一个边界被删除，已用剩下的边界重算。" : "边界已被删除，请点「选择边界」重选。");
                    try { _doc.Views.Redraw(); } catch { }
                }
            }
            catch { }
            finally { _recheckId = Guid.Empty; _recheckKind = 0; }
        }

        void ClearPreview()
        {
            _results = new List<VoronoiFaceResult>();
            _merged = new Mesh();
            _smooth = null;
            _conduit.Curves = BuildPreviewCurves(_results);
                    { var cl = new List<Point3d>(); for (int ci = 0; ci < _results.Count; ci++) if (_results[ci] != null && _results[ci].CellCenters != null) cl.AddRange(_results[ci].CellCenters); _conduit.Centers = cl; }
            _conduit.Shaded = null;
            _conduit.SmoothSubD = null;
        }

        /// <summary>预览曲线 = 胞元边 + 中心连线（连线单独一条列表，预览时合起来画）</summary>
        static List<Curve> BuildPreviewCurves(List<VoronoiFaceResult> results)
        {
            var outp = new List<Curve>();
            if (results == null) return outp;
            for (int i = 0; i < results.Count; i++)
            {
                VoronoiFaceResult r = results[i];
                if (r == null) continue;
                if (r.Wireframe != null) outp.AddRange(r.Wireframe);
                if (r.Spokes != null) outp.AddRange(r.Spokes);
            }
            return outp;
        }

        void Rebuild()
        {
            if (_closed) return;
            try
            {
                if (_targets.Count == 0)
                {
                    ClearPreview();
                    _panel.SetTargetState(false);
                    _panel.SetInfo(VoronoiCore.EmptyTargetHint);
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }

                VoronoiSettings cur = _panel.Settings;
                // 渐变参考点每次现取：物件被移动/缩放后，分布跟着走
                if (cur.GradientOn && _gradientId != Guid.Empty)
                {
                    Point3d gc = ObjectCenter(_doc, _gradientId);
                    if (gc.IsValid)
                    {
                        cur.GradientPoint = gc;
                        _watchSeen = gc; _watchApplied = gc; _watchPending = false;
                    }
                }
                cur.WantWireframe = cur.OutputMode == 0;   // 线框模式才提取胞元边界线

                var results = new List<VoronoiFaceResult>();
                var lines = new List<string>();
                int failed = 0, cells = 0, verts = 0, faces = 0, wires = 0, wiresSnapped = 0, wiresDropped = 0;
                int wireDedup = 0, wireSeen = 0, wireEmitted = 0, wireOutline = 0, cellFallback = 0;
                double secs = 0, projArea = 0, regionArea = 0;
                for (int i = 0; i < _targets.Count; i++)
                {
                    RhinoObject obj = null;
                    try { obj = _doc.Objects.FindId(_targets[i]); } catch { }
                    PlanarBoundary b; string why;
                    if (!PlanarBoundary.TryResolve(obj, out b, out why))
                    {
                        failed++;
                        lines.Add(string.Format("跳过 {0}：{1}", obj != null ? PlanarBoundary.Describe(obj) : _targets[i].ToString(), why));
                        continue;
                    }
                    Brep plane = b.ToBrep(Math.Max(1e-6, cur.Tol * (cur.MmToModel > 0 ? cur.MmToModel : 1.0)));
                    if (plane == null)
                    {
                        failed++;
                        lines.Add(b.Desc + "：边界曲线无法变成平面面");
                        continue;
                    }

                    string rep;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    List<VoronoiFaceResult> rs = Voronoi.Generate(plane, cur, out rep);
                    sw.Stop();
                    secs += sw.Elapsed.TotalSeconds;

                    int c0 = 0, f0 = 0, w0 = 0, sn0 = 0; string note = "";
                    for (int k = 0; k < rs.Count; k++)
                    {
                        results.Add(rs[k]);
                        cells += rs[k].CellCount;
                        verts += rs[k].VertexCount;
                        faces += rs[k].FaceCount;
                        wires += rs[k].Wireframe.Count;
                        wiresSnapped += rs[k].WireSnapped;
                        wiresDropped += rs[k].WireDropped;
                        wireDedup += rs[k].WireDedup;
                        wireSeen += rs[k].WireEdgeSeen;
                        wireEmitted += rs[k].WireEmitted;
                        wireOutline += rs[k].WireOutline;
                        cellFallback += rs[k].CellFallback;
                        projArea += rs[k].ProjectedArea;
                        regionArea += rs[k].RegionArea;
                        c0 += rs[k].CellCount; f0 += rs[k].FaceCount; w0 += rs[k].Wireframe.Count; sn0 += rs[k].WireSnapped;
                        if (note.Length == 0 && !string.IsNullOrEmpty(rs[k].Note)) note = rs[k].Note;
                    }
                    lines.Add(string.Format("{0}：胞元 {1} 个 / 网格 {2} 面 / 线框 {3} 条（吸附 {4} 点）· 投影 {5:0.#}/{6:0.#} · {7:0.00}s{8}",
                        b.Desc, c0, f0, w0, sn0, rs.Count > 0 ? rs[0].ProjectedArea : 0, rs.Count > 0 ? rs[0].RegionArea : 0,
                        sw.Elapsed.TotalSeconds,
                        note.Length > 0 ? " · " + note : ""));
                }
                _results = results;

                Mesh merged = Voronoi.MergeWeld(_results, WeldTol(cur));
                _merged = merged != null ? merged : new Mesh();
                // 网格面 = 线框的每个三角面一张网格片（片内细分由「细分程度 / 平滑度」控制）；
                // 勾了「一键平滑」再把它转成**细分曲面（SubD）**输出（不再用 QuadRemesh 重构拓扑）
                _smooth = cur.OutputMode == 1 ? Voronoi.ApplySmooth(_merged, cur) : null;

                if (_panel.LivePreview)
                {
                    // 预览与输出一致：线框 → 只画线；网格面 → 只画面（勾了平滑就画平滑后的形状，不叠线框）
                    _conduit.OutputMode = cur.OutputMode;
                    if (cur.OutputMode == 0)
                    {
                        // 线框模式：胞元边 + 中心连线 + 中心点（都要看得见 —— 用户口径）
                        var all = new List<Curve>();
                        var cl = new List<Point3d>();
                        for (int i = 0; i < _results.Count; i++)
                        {
                            all.AddRange(_results[i].Wireframe);
                            if (_results[i].Spokes != null) all.AddRange(_results[i].Spokes);
                            if (_results[i].CellCenters != null) cl.AddRange(_results[i].CellCenters);
                        }
                        _conduit.Curves = all.Count > MaxPreviewCurves ? all.GetRange(0, MaxPreviewCurves) : all;
                        _conduit.Centers = cl;
                        _conduit.MarkerSize = Math.Max(cur.CellSize * cur.MmToModel * 0.10, 1e-6);
                        _conduit.Shaded = null;
                        _conduit.SmoothSubD = null;
                    }
                    else
                    {
                        _conduit.Curves = new List<Curve>();
                        _conduit.Centers = new List<Point3d>();
                        // 勾了「一键平滑」→ 直接画细分曲面（预览 = 输出）；否则画网格
                        _conduit.SmoothSubD = (_smooth != null && _smooth.Ok) ? _smooth.SubD : null;
                        _conduit.Shaded = _conduit.SmoothSubD != null ? null : _merged;
                    }
                    _doc.Views.Redraw();
                }

                int validTargets; string validWhy;
                ValidateTargets(_doc, _targets, out validTargets, out validWhy);
                _panel.SetTargetState(validTargets > 0);

                // 出问题时光看数字不知道原因 —— 把第一个非空 note 直接摆到状态条上
                string firstNote = "";
                foreach (VoronoiFaceResult r in _results)
                    if (!string.IsNullOrEmpty(r.Note)) { firstNote = r.Note; break; }
                if (firstNote.Length == 0 && failed > 0 && lines.Count > 0) firstNote = lines[0];   // 选错类型 / 解析失败：把原因摆出来
                if (cells == 0 && verts == 0 && firstNote.Length == 0)
                    firstNote = "这个边界上没生成出任何东西：可以试试把胞元尺寸调小，或换更细的采样间距";

                string mode;
                string meshTxt;                          // 线框模式不建网格（输出二选一），别显示成「网格 0 面」像是没算出来
                if (cur.OutputMode == 0)
                {
                    meshTxt = "网格 —（线框模式不建）";
                    mode = string.Format("（只出线框，图层 {0}：胞元边 {1} + 外轮廓 {2} 条；考虑 {3} = 出段 {4} + 去重 {5} + 丢 {6}，裁边 {7}）",
                        Voronoi.LayerWire, wires - wireOutline, wireOutline, wireSeen, wireEmitted, wireDedup, wiresDropped, wiresSnapped);
                }
                else if (_smooth != null && _smooth.Ok)
                {
                    meshTxt = string.Format("网格 {0} 顶点 / {1} 面（线框点扇形剖分）", verts, faces);
                    mode = string.Format("（网格面 → {0} + 平滑结果 → {1}）", Voronoi.LayerFace, Voronoi.LayerSmooth);
                }
                else
                {
                    meshTxt = string.Format("网格 {0} 顶点 / {1} 面（线框点扇形剖分）", verts, faces);
                    mode = string.Format("（只出网格面，图层 {0}，不带线框）", Voronoi.LayerFace);
                }

                string info = string.Format(
                    "{0} 个边界 / {1} 个胞元 · {2} · 线框 {3} 条（胞元边 {4} + 外轮廓 {5}，考虑 {6} / 去重 {7} / 裁边 {8} / 丢 {9}）· {10:0} ms\r\n{11}｜投影 {12:0.#} / 边界 {13:0.#}（差 {14:0.0#}%）｜高度场：胞元中心距离（中心 = 深度，边 = 0{15}）｜胞元 {16:0.##} mm，{17} {18:0.##} mm，过渡 {19:0.##} mm，形状 {20:0.##}，规整度 {21}，种子 {22}",
                    _targets.Count, cells, meshTxt, wires, wires - wireOutline, wireOutline, wireSeen, wireDedup, wiresSnapped, wiresDropped, secs * 1000,
                    mode, projArea, regionArea, regionArea > 1e-9 ? (projArea - regionArea) / regionArea * 100.0 : 0.0,
                    cellFallback > 0 ? string.Format("；{0} 点退回距离场", cellFallback) : "",
                    cur.CellSize, cur.Depth >= 0 ? "凸起" : "凹陷", Math.Abs(cur.Depth),
                    cur.EdgeWidth, cur.Shape, cur.Relax, cur.Seed);
                if (failed > 0) info += string.Format("｜{0} 个边界跳过", failed);
                if (cur.OutputMode == 0 && wires > MaxPreviewCurves)
                    info += string.Format("｜预览只画前 {0} 条", MaxPreviewCurves);
                if (firstNote.Length > 0) info += "\r\n⚠ " + firstNote;
                if (_smooth != null && !string.IsNullOrEmpty(_smooth.Note) && _smooth.Ok) info += "\r\n" + _smooth.Note;
                _panel.SetInfo(info);
            }
            catch (Exception ex) { _panel.SetInfo("计算失败：" + ex.Message); }
        }

        /// <summary>焊接位置容差（模型单位）：只用来识别「同一个点」，远小于采样间距</summary>
        static double WeldTol(VoronoiSettings s)
        {
            double u2m = s != null && s.MmToModel > 0 ? s.MmToModel : 1.0;
            double step = s != null ? s.Step : 0.4;
            return Math.Max(1e-6, step * u2m * 1e-3);
        }

        public void ClosePanel()
        {
            try { _panel.Close(); } catch { }
        }

        void OnClosed()
        {
            if (_closed) return;
            if (_panel.Committed && _targets.Count > 0)
            {
                try
                {
                    Rebuild();
                    int curveCount, meshCount, smoothCount;
                    uint undo = _doc.BeginUndoRecord("生成泰森多边形纹");
                    try
                    {
                        Voronoi.AddToDocument(_doc, _results, _merged, _smooth, _panel.Settings.OutputMode,
                            out curveCount, out meshCount, out smoothCount);
                    }
                    finally { _doc.EndUndoRecord(undo); }
                    RhinoApp.WriteLine(_panel.Settings.OutputMode == 0
                        ? string.Format("已生成 {0} 条胞元线框曲线（图层：{1}）。", curveCount, Voronoi.LayerWire)
                        : (smoothCount > 0
                            ? string.Format("已生成 {0} 个网格面 + {1} 份平滑结果（图层：{2} / {3}）。",
                                meshCount, smoothCount, Voronoi.LayerFace, Voronoi.LayerSmooth)
                            : string.Format("已生成 {0} 个网格面（图层：{1}，不带线框）。", meshCount, Voronoi.LayerFace)));
                    try { _doc.Views.Redraw(); } catch { }
                }
                catch (Exception ex) { RhinoApp.WriteLine("写入失败：" + ex.Message); }
            }
            else if (_panel.Committed)
            {
                RhinoApp.WriteLine("没有选择边界，未生成任何几何。");
            }
            else
            {
                RhinoApp.WriteLine("已取消，未生成任何几何。");
            }

            _closed = true;
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            try { _watch.Stop(); _watch.Dispose(); } catch { }
            try { _recheck.Stop(); _recheck.Dispose(); } catch { }
            try { _conduit.Enabled = false; } catch { }
            try { RhinoDoc.ReplaceRhinoObject -= OnReplaceObject; } catch { }
            try { RhinoDoc.DeleteRhinoObject -= OnDeleteObject; } catch { }
            _live.Remove(this);
            try { _doc.Views.Redraw(); } catch { }
        }
    }
}
