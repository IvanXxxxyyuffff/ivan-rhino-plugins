using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using Rhino.PlugIns;

// Rhino 8 的插件加载器按「程序集级」[assembly: Guid] 识别插件 ID（库存插件如 Grasshopper/IronPython 都是这样）；
// 只有类级 [Guid] 时 Rhino 读不到 ID，会把插件登记成全零 GUID，随后其它插件加载就报「ID 已被使用」。
#if RH7
[assembly: Guid("9C4E7A21-3D58-4B06-8E97-2F1B6A3C5D08")]
#else
[assembly: Guid("4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27")]
#endif

namespace HalftonePattern
{
    /// <summary>插件入口：圆点渐变阵列（Rhino 7 / Rhino 8 分别用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("9C4E7A21-3D58-4B06-8E97-2F1B6A3C5D08")]
#else
    [Guid("4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27")]
#endif
    public class HalftoneDotsPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            RhinoApp.WriteLine("参数化阵列纹理插件已加载。命令：ParametricTexture（旧名 HalftoneDots 仍可用）");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放一个 run-halftone-selftest.flag，
        /// 下次启动 Rhino 会自动跑几何自检并把报告写到同目录 HalftoneSelfTest.txt（跑完删除标志）。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
                string flag = Path.Combine(dir, "run-halftone-selftest.flag");
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
                HalftoneSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, HalftoneSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("阵列自检失败：" + ex.Message); } catch { }
            }
        }
    }

    /// <summary>参数化阵列纹理（主命令）</summary>
    public class ParametricTextureCommand : Command
    {
        public override string EnglishName { get { return "ParametricTexture"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode) { return ArrayTextureCore.Run(doc); }
    }

    /// <summary>旧命令名 HalftoneDots：保留为别名，等价于 ParametricTexture</summary>
    public class HalftoneDotsCommand : Command
    {
        public override string EnglishName { get { return "HalftoneDots"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode) { return ArrayTextureCore.Run(doc); }
    }

    /// <summary>面板上「选择物件」按钮调用的隐藏命令（面板是非模态的，GetObject 只能在命令上下文里跑）</summary>
    public class HalftonePickTargetCommand : Command
    {
        public override string EnglishName { get { return "HalftonePickTarget"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            ArrayTextureSession session = ArrayTextureSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 ParametricTexture 打开参数面板。");
                return Result.Nothing;
            }

            var go = new GetObject();
            go.SetCommandPrompt("选择要生成圆点渐变阵列的曲面或多重曲面（多重曲面可点选其中单个面）");
            go.GeometryFilter = ObjectType.Surface | ObjectType.PolysrfFilter;
            go.SubObjectSelect = true;
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            ObjRef objRef = go.Object(0);
            if (objRef == null || objRef.ObjectId == Guid.Empty)
            {
                session.MarkTargetInvalid("没有选到物件，请点「选择物件」重选。");
                return Result.Failure;
            }
            Brep brep = objRef.Brep();
            if (brep == null || brep.Faces.Count == 0)
            {
                RhinoApp.WriteLine("所选对象不含可用曲面。");
                session.MarkTargetInvalid("所选对象不含可用曲面，请换一个。");
                return Result.Failure;
            }

            session.SetTarget(doc, objRef.ObjectId, ArrayTextureCore.DescribeObject(doc, objRef),
                              ArrayTextureCore.PickFaceIndex(brep, objRef), brep.Faces.Count);
            RhinoApp.WriteLine("目标物件已更新：{0}", ArrayTextureCore.DescribeObject(doc, objRef));
            return Result.Success;
        }
    }

    /// <summary>面板上「选择渐变物件」按钮调用的隐藏命令：用这个物件的位置当渐变起点（越靠它图形越大）</summary>
    public class HalftonePickGradientCommand : Command
    {
        public override string EnglishName { get { return "HalftonePickGradient"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            ArrayTextureSession session = ArrayTextureSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 ParametricTexture 打开参数面板。");
                return Result.Nothing;
            }
            var go = new GetObject();
            go.SetCommandPrompt("选择渐变起点物件（图形尺寸从它向外渐变；点 / 线 / 曲面 / 网格都行）");
            go.GeometryFilter = ObjectType.AnyObject;
            go.SubObjectSelect = false;
            go.EnablePreSelect(false, true);
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            ObjRef objRef = go.Object(0);
            RhinoObject obj = objRef != null ? objRef.Object() : null;
            if (obj == null || obj.Geometry == null)
            {
                session.MarkGradientInvalid("没有选到物件，请点「选择渐变物件」重选。");
                return Result.Failure;
            }
            Point3d c = ArrayTextureCore.ObjectCenter(doc, objRef.ObjectId);
            if (!c.IsValid)
            {
                RhinoApp.WriteLine("拿不到这个物件的位置，换一个试试。");
                session.MarkGradientInvalid("拿不到这个物件的位置，换一个试试。");
                return Result.Failure;
            }

            session.SetGradient(doc, objRef.ObjectId, ArrayTextureCore.DescribeObject(doc, objRef));
            RhinoApp.WriteLine("渐变起点已设置：图形尺寸从它向外渐变（移动它会实时跟随）。");
            return Result.Success;
        }
    }

    internal static class ArrayTextureCore
    {
        /// <summary>没有目标物件时，面板头部与状态条显示的提示</summary>
        internal const string EmptyTargetHint = "未选择物件 —— 点「选择物件」按钮选目标";

        public static Result Run(RhinoDoc doc)
        {
            var settings = new HalftoneSettings();
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            // 统一口径：不再看文档预选，永远先开面板、由面板上的「选择物件」按钮来选
            // （用户要求：所有插件都强制在面板里选物件，去掉「先选物件再运行命令」的逻辑）
            {
                var empty = new ArrayTextureSession(doc, Guid.Empty, Guid.Empty, settings, EmptyTargetHint);
                empty.Start();
                RhinoApp.WriteLine("参数面板已打开。点面板上的「选择物件」按钮选目标曲面（也可以直接在视图里点选后再点按钮）。");
                return Result.Success;
            }
        }

        /// <summary>把渐变起点物件转成采样点：点→本身；曲线→沿线 64 等分；其它→包围盒中心</summary>
        internal static List<Point3d> ExtractCenterPoints(RhinoObject obj)
        {
            var pts = new List<Point3d>();
            if (obj == null || obj.Geometry == null) return pts;
            try
            {
                var pt = obj.Geometry as Rhino.Geometry.Point;
                if (pt != null) { pts.Add(pt.Location); return pts; }

                var crv = obj.Geometry as Curve;
                if (crv != null)
                {
                    double[] ts = crv.DivideByCount(64, true);
                    if (ts != null)
                        foreach (double t in ts) pts.Add(crv.PointAt(t));
                    if (pts.Count >= 2) return pts;
                    pts.Clear();
                }

                BoundingBox bb = obj.Geometry.GetBoundingBox(true);
                if (bb.IsValid) pts.Add(bb.Center);
            }
            catch { }
            return pts;
        }

        /// <summary>文档里预选中的第一个可用物件（转不成 Brep 的忽略）</summary>
        internal static ObjRef FirstSelected(RhinoDoc doc)
        {
            try
            {
                var objs = doc.Objects.GetSelectedObjects(false, false);
                if (objs == null) return null;
                foreach (RhinoObject o in objs)
                {
                    if (o == null || o.Id == Guid.Empty) continue;
                    if (Brep.TryConvertBrep(o.Geometry) == null) continue;
                    // 用 ObjRef(Guid)：Rhino 7 没有 ObjRef(RhinoDoc, Guid) 这个重载
                    return new ObjRef(o.Id);
                }
            }
            catch { }
            return null;
        }

        /// <summary>物件当前的位置（包围盒中心；每次现取 —— 物件移动后中心就变了）</summary>
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

        /// <summary>取用户点选到的面序号：优先用子对象选中的面，否则用点选位置最近的面</summary>
        internal static int PickFaceIndex(Brep brep, ObjRef objRef)
        {
            int fi = -1;
            try
            {
                BrepFace f = objRef.Face();
                if (f != null) fi = f.FaceIndex;
            }
            catch { }
            if (fi < 0)
            {
                try
                {
                    Point3d pt = objRef.SelectionPoint();
                    if (pt.IsValid)
                    {
                        Point3d cp; ComponentIndex ci; double s, t; Vector3d nrm;
                        if (brep.ClosestPoint(pt, out cp, out ci, out s, out t, 0.0, out nrm) &&
                            ci.ComponentIndexType == ComponentIndexType.BrepFace)
                            fi = ci.Index;
                    }
                }
                catch { }
            }
            if (fi < 0 || fi >= brep.Faces.Count) fi = 0;
            return fi;
        }

        internal static string DescribeObject(RhinoDoc doc, ObjRef objRef)
        {
            string name = null;
            RhinoObject obj = objRef.Object();
            if (obj != null) name = obj.Name;
            string id = objRef.ObjectId.ToString();
            if (id.Length > 8) id = id.Substring(0, 8);
            return string.IsNullOrEmpty(name) ? ("目标对象 " + id) : ("目标：" + name);
        }

        internal static int EnsureLayer(RhinoDoc doc, string name)
        {
            foreach (Layer l in doc.Layers)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
            var nl = new Layer();
            nl.Name = name;
            nl.Color = Color.FromArgb(0, 145, 165);
            return doc.Layers.Add(nl);
        }
    }

    /// <summary>一次「参数化阵列纹理」会话：浮动面板 + 预览 + 文档事件实时刷新（不阻塞 Rhino）</summary>
    internal class ArrayTextureSession
    {
        static readonly List<ArrayTextureSession> _live = new List<ArrayTextureSession>();

        readonly RhinoDoc _doc;
        Guid _targetId;                    // 可变：面板上「选择物件」可以换目标
        Guid _centerId;                    // 可变：圆心来源物件被删后要清掉，否则一直按旧物件采样
        Guid _gradId = Guid.Empty;                       // 渐变物件：记 ID 不记坐标，物件一动就能跟上
        readonly Timer _gradWatch = new Timer();         // 拖动跟随轮询（Gumball 拖动不一定发文档事件）
        readonly Timer _recheck = new Timer();           // 删除事件延迟复查（Move/Transform 是「先删旧、再加新」）
        Point3d _gradSeen = Point3d.Unset;
        Point3d _gradApplied = Point3d.Unset;
        bool _gradPending;
        Guid _recheckId = Guid.Empty;
        int _recheckKind;                  // 1 = 目标曲面，2 = 渐变 / 圆心物件
        readonly HalftonePanel _panel;
        readonly HalftonePreviewConduit _conduit = new HalftonePreviewConduit();
        readonly Timer _timer = new Timer();
        List<HalftoneFaceResult> _results = new List<HalftoneFaceResult>();
        bool _closed;

        public ArrayTextureSession(RhinoDoc doc, Guid targetId, Guid centerId, HalftoneSettings settings, string desc)
        {
            _doc = doc;
            _targetId = targetId;
            _centerId = centerId;
            _panel = new HalftonePanel(settings, desc);
        }

        /// <summary>当前打开着的面板（拾取命令用它把新物件交给会话）</summary>
        internal static ArrayTextureSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        /// <summary>设置/更新渐变起点物件（记 ID；移动物件后预览会自动跟着变）</summary>
        public void SetGradient(RhinoDoc doc, Guid id, string desc)
        {
            if (_closed) return;
            Point3d c = ArrayTextureCore.ObjectCenter(doc, id);
            if (!c.IsValid)
            {
                MarkGradientInvalid("拿不到这个物件的位置，换一个试试。");
                return;
            }
            _gradId = id;
            try
            {
                _panel.Settings.CenterPoints = new List<Point3d> { c };
                _panel.Settings.UsePickedCenter = true;
            }
            catch { }
            _gradSeen = c; _gradApplied = c; _gradPending = false;
            _panel.SetGradientState(true);
            _panel.SetCenterState(true);
            _panel.SetCenterInfo(desc);
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        /// <summary>换目标曲面：更新头部与「参考面」控件，清掉预览再重算</summary>
        public void SetTarget(RhinoDoc doc, Guid id, string desc, int pickedFace, int faceCount)
        {
            if (_closed) return;
            _targetId = id;
            try
            {
                _panel.Settings.FaceCount = faceCount;
                _panel.Settings.PickedFace = pickedFace;
                if (_panel.Settings.OnlyFace >= faceCount) _panel.Settings.OnlyFace = -1;
            }
            catch { }
            _panel.SetTarget(desc);
            _panel.SetTargetState(id != Guid.Empty);
            _panel.RefreshScope();
            _results = new List<HalftoneFaceResult>();
            _conduit.Curves = new List<Curve>();
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        /// <summary>拾取没拿到可用目标：按钮置红 + 状态条说明（已有目标不自动清空，等用户重选）</summary>
        public void MarkTargetInvalid(string message)
        {
            if (_closed) return;
            _panel.SetTargetState(false);
            if (!string.IsNullOrEmpty(message)) _panel.SetInfo(message);
        }

        /// <summary>渐变拾取没拿到可用物件：按钮置红 + 状态条说明</summary>
        public void MarkGradientInvalid(string message)
        {
            if (_closed) return;
            _panel.SetGradientState(false);
            if (!string.IsNullOrEmpty(message)) _panel.SetInfo(message);
        }

        public void Start()
        {
            // 同一时间只保留一个面板，避免预览叠加
            foreach (ArrayTextureSession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 120;
            _timer.Tick += delegate { _timer.Stop(); Rebuild(); };
            _panel.ValueChanged += delegate
            {
                if (!_panel.LivePreview) return;
                _timer.Stop(); _timer.Start();
            };
            _panel.FormClosed += delegate { OnClosed(); };
            _panel.PickCenterRequested += delegate { PickCenter(); };

            RhinoDoc.ReplaceRhinoObject += OnReplaceObject;
            RhinoDoc.DeleteRhinoObject += OnDeleteObject;

            _recheck.Interval = 250;
            _recheck.Tick += delegate { RecheckTick(); };
            _recheck.Stop();

            _gradWatch.Interval = 400;
            _gradWatch.Tick += delegate { GradWatchTick(); };
            _gradWatch.Start();

            _conduit.Enabled = true;
            _panel.Show();
            Rebuild();
            _panel.Activate();
            _live.Add(this);
        }

        /// <summary>到视图里点一个圆心：同心环/螺旋/抖动 的点阵与渐变都从它向外扩散</summary>
        void PickCenter()
        {
            if (_closed) return;
            _panel.Hide();
            try
            {
                var gp = new GetPoint();
                gp.SetCommandPrompt("选择阵列圆心：在曲面上点一下（回车 / Esc = 取消，仍用曲面中心）");
                gp.AcceptNothing(true);
                Rhino.Input.GetResult r = gp.Get();
                if (r == Rhino.Input.GetResult.Point)
                {
                    Point3d pt = gp.Point();
                    _panel.Settings.CenterPoints = new List<Point3d> { pt };
                    _panel.Settings.UsePickedCenter = true;
                    _panel.MarkCenterPicked(pt);
                    _panel.SetCenterState(true);
                    RhinoApp.WriteLine("阵列圆心已设定：{0}", pt.ToString());
                }
                else
                {
                    RhinoApp.WriteLine("已取消选择圆心。");
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("选择圆心失败：" + ex.Message);
            }
            finally
            {
                if (!_closed) { _panel.Show(); _panel.Activate(); }
            }
            Rebuild();
        }

        /// <summary>目标曲面或起点物件被移动/编辑时实时刷新</summary>
        void OnReplaceObject(object sender, RhinoReplaceObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (e.Document != null && e.Document.RuntimeSerialNumber != _doc.RuntimeSerialNumber) return;
                if (e.ObjectId == _targetId || (_centerId != Guid.Empty && e.ObjectId == _centerId)) Rebuild();
                if (_gradId != Guid.Empty && e.ObjectId == _gradId) ApplyGradientPoint();
            }
            catch { }
        }

        /// <summary>把渐变物件的当前位置取回来并重算（物件被移动/替换后调用）</summary>
        void ApplyGradientPoint()
        {
            Point3d c = ArrayTextureCore.ObjectCenter(_doc, _gradId);
            if (!c.IsValid) { _panel.SetGradientState(false); return; }
            try
            {
                _panel.Settings.CenterPoints = new List<Point3d> { c };
                _panel.Settings.UsePickedCenter = true;
            }
            catch { }
            _gradSeen = c; _gradApplied = c; _gradPending = false;
            _panel.SetGradientState(true);
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        /// <summary>渐变物件被拖动时跟随：等它停下来（连续两次位置一致）再重算，拖动过程不重算</summary>
        void GradWatchTick()
        {
            if (_closed || _gradId == Guid.Empty || !_panel.LivePreview) return;
            try
            {
                Point3d c = ArrayTextureCore.ObjectCenter(_doc, _gradId);
                if (!c.IsValid) { _panel.SetGradientState(false); return; }
                double u2m = _panel.Settings.MmToModel > 0 ? _panel.Settings.MmToModel : 1.0;
                double tol = Math.Max(1e-6, Math.Max(_panel.Settings.Pitch, 0.05) * u2m * 0.1);
                if (_gradSeen.IsValid && c.DistanceTo(_gradSeen) > tol) { _gradSeen = c; _gradPending = true; return; }
                _gradSeen = c;
                if (!_gradPending && _gradApplied.IsValid && c.DistanceTo(_gradApplied) <= tol) return;
                _gradPending = false;
                ApplyGradientPoint();
            }
            catch { }
        }

        void OnDeleteObject(object sender, RhinoObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (e.ObjectId == _targetId) { _recheckId = e.ObjectId; _recheckKind = 1; _recheck.Stop(); _recheck.Start(); return; }
                if ((_gradId != Guid.Empty && e.ObjectId == _gradId) ||
                    (_centerId != Guid.Empty && e.ObjectId == _centerId))
                {
                    _recheckId = e.ObjectId; _recheckKind = 2; _recheck.Stop(); _recheck.Start();
                }
            }
            catch { }
        }

        /// <summary>删除事件 250ms 后复查：物件还在 = 只是移动/替换（照常跟随）；真没了才按删除处理</summary>
        void RecheckTick()
        {
            _recheck.Stop();
            if (_closed) return;
            try
            {
                RhinoObject o = _recheckId == Guid.Empty ? null : _doc.Objects.FindId(_recheckId);
                if (o != null)
                {
                    Rebuild();
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }
                if (_recheckKind == 2)
                {
                    _gradId = Guid.Empty;
                    _centerId = Guid.Empty;
                    try
                    {
                        _panel.CenterPicked = false;
                        _panel.Settings.CenterPoints = null;
                        _panel.Settings.UsePickedCenter = false;
                    }
                    catch { }
                    _panel.SetGradientState(false);
                    _panel.SetCenterState(false);
                    _panel.SetCenterInfo("");
                    Rebuild();
                    _panel.SetInfo("渐变物件已被删除，已回到曲面中心。");
                    RhinoApp.WriteLine("渐变物件已被删除，已回到曲面中心。");
                    try { _doc.Views.Redraw(); } catch { }
                }
                else
                {
                    _conduit.Curves = new List<Curve>();
                    _panel.SetTargetState(false);
                    _panel.SetInfo("目标曲面已被删除，请点「选择物件」重选。");
                    try { _doc.Views.Redraw(); } catch { }
                }
            }
            catch { }
            finally { _recheckId = Guid.Empty; _recheckKind = 0; }
        }

        void Rebuild()
        {
            if (_closed) return;
            try
            {
                RhinoObject obj = _targetId == Guid.Empty ? null : _doc.Objects.FindId(_targetId);
                if (obj == null)
                {
                    _results = new List<HalftoneFaceResult>();
                    _conduit.Curves = new List<Curve>();
                    _panel.SetTargetState(false);
                    _panel.SetInfo(_targetId == Guid.Empty
                        ? ArrayTextureCore.EmptyTargetHint
                        : "目标曲面已被删除，请点「选择物件」重选。");
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }
                Brep brep = obj.DuplicateGeometry() as Brep;
                if (brep == null) brep = obj.Geometry as Brep;
                if (brep == null || brep.Faces.Count == 0)
                {
                    _panel.SetTargetState(false);
                    _panel.SetInfo("目标物件已不是曲面。");
                    return;
                }

                // 起点物件可能被移动/编辑，重新采样
                if (_centerId != Guid.Empty)
                {
                    RhinoObject co = _doc.Objects.FindId(_centerId);
                    if (co != null)
                    {
                        List<Point3d> pts = ArrayTextureCore.ExtractCenterPoints(co);
                        if (pts != null && pts.Count > 0) _panel.Settings.CenterPoints = pts;
                    }
                }

                HalftoneSettings cur = _panel.Settings;
                string report;
                _results = Halftone.Generate(brep, cur, out report);
                int n = 0;
                foreach (HalftoneFaceResult r in _results) n += r.Shapes.Count;

                if (_panel.LivePreview)
                {
                    var curves = new List<Curve>();
                    foreach (HalftoneFaceResult r in _results) curves.AddRange(r.Shapes);
                    _conduit.Curves = curves;
                    _doc.Views.Redraw();
                }

                string modeName = HalftoneSettings.ArrayNames[Math.Max(0, Math.Min(HalftoneSettings.ArrayNames.Length - 1, cur.ArrayMode))];
                string shapeName = HalftoneSettings.ShapeNames[Math.Max(0, Math.Min(HalftoneSettings.ShapeNames.Length - 1, cur.Shape))];
                string srcName = _panel.CenterPicked
                    ? "指定圆心"
                    : ((cur.CenterPoints != null && cur.CenterPoints.Count > 0 && cur.UsePickedCenter) ? "指定物件" : "曲面中心");
                _panel.SetInfo(string.Format(
                    "{0} 个图形 / {1} 个面 · {2} · {3} · 旋转 {4:0.##}° · 衰减 {5:0.##} · 起点 {6}\r\n间距 {7:0.##} mm，直径 {8:0.##} → {9:0.##} mm，边距 {10:0.##} mm",
                    n, _results.Count, modeName, shapeName, cur.Rotation, cur.Falloff, srcName,
                    cur.Pitch, cur.MaxDia, cur.MinDia, cur.Margin));
            }
            catch (Exception ex) { _panel.SetInfo("计算失败：" + ex.Message); }
        }

        public void ClosePanel()
        {
            try { _panel.Close(); } catch { }
        }

        void OnClosed()
        {
            if (_closed) return;

            // 先按当前参数重算并落盘（面板可能刚改过参数），再摘事件
            if (_panel.Committed)
            {
                try
                {
                    Rebuild();
                    int count = 0;
                    uint undo = _doc.BeginUndoRecord("生成参数化阵列纹理");
                    try
                    {
                        int layer = ArrayTextureCore.EnsureLayer(_doc, "阵列纹理");
                        var attrs = new ObjectAttributes();
                        attrs.LayerIndex = layer;
                        foreach (HalftoneFaceResult r in _results)
                            foreach (Curve c in r.Shapes) { _doc.Objects.AddCurve(c, attrs); count++; }
                    }
                    finally { _doc.EndUndoRecord(undo); }
                    RhinoApp.WriteLine("已生成 {0} 个图形（图层：阵列纹理）。", count);
                }
                catch (Exception ex) { RhinoApp.WriteLine("生成失败：" + ex.Message); }
            }
            else
            {
                RhinoApp.WriteLine("已取消。");
            }

            _closed = true;
            try { RhinoDoc.ReplaceRhinoObject -= OnReplaceObject; } catch { }
            try { RhinoDoc.DeleteRhinoObject -= OnDeleteObject; } catch { }
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            try { _gradWatch.Stop(); _gradWatch.Dispose(); } catch { }
            try { _recheck.Stop(); _recheck.Dispose(); } catch { }
            try { _conduit.Enabled = false; } catch { }
            _live.Remove(this);
            try { _doc.Views.Redraw(); } catch { }
        }
    }
}
