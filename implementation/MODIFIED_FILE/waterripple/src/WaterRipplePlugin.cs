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
[assembly: Guid("901D517B-DBFD-4AD8-9A7B-82F465031C1F")]
#else
[assembly: Guid("6C615EE9-EADB-4346-A6F5-633CA3FD7D16")]
#endif

namespace WaterRipplePattern
{
    /// <summary>插件入口：水波纹（Rhino 7 / 8 用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("901D517B-DBFD-4AD8-9A7B-82F465031C1F")]
#else
    [Guid("6C615EE9-EADB-4346-A6F5-633CA3FD7D16")]
#endif
    public class WaterRipplePlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            RhinoApp.WriteLine("水波纹插件已加载。命令：WaterRipple");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-waterripple-selftest.flag，
        /// 下次启动 Rhino 自动跑几何自检并写报告到同目录 WaterRippleSelfTest.txt，跑完退出。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-waterripple-selftest.flag");
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
                WaterRippleSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, WaterRippleSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("水波纹自检失败：" + ex.Message); } catch { }
            }
        }
    }

    public class WaterRippleCommand : Command
    {
        public override string EnglishName { get { return "WaterRipple"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var settings = new WaterRippleSettings();
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            // 预选的物件直接当目标；没预选就用空目标打开面板，由面板上的「选择目标」来选
            var ids = new List<Guid>();
            try
            {
                foreach (RhinoObject o in doc.Objects.GetSelectedObjects(false, false))
                    if (o != null) ids.Add(o.Id);
            }
            catch { }

            string desc = WaterRippleSession.DescribeTargets(doc, ids);
            var session = new WaterRippleSession(doc, ids, settings, desc);
            session.Start();
            RhinoApp.WriteLine(ids.Count > 0
                ? "参数面板已打开（已用选中的 " + ids.Count + " 个物件作为目标）。"
                : "参数面板已打开。点面板上的「选择目标」选曲面 / 多重曲面 / 闭合平面曲线。");
            return Result.Success;
        }
    }

    /// <summary>面板上「选择目标」按钮调用的隐藏命令（面板是非模态的，GetObject 只能在命令上下文里跑）</summary>
    public class WaterRipplePickTargetCommand : Command
    {
        public override string EnglishName { get { return "WaterRipplePickTarget"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            WaterRippleSession session = WaterRippleSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 WaterRipple 打开参数面板。");
                return Result.Nothing;
            }

            var go = new GetObject();
            go.SetCommandPrompt("选择目标：曲面 / 多重曲面 / 闭合平面曲线（可多选，回车结束）");
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

            session.SetTargets(doc, ids, WaterRippleSession.DescribeTargets(doc, ids));
            RhinoApp.WriteLine("目标已更新：" + WaterRippleSession.DescribeTargets(doc, ids));
            return Result.Success;
        }
    }

    /// <summary>一次「水波纹」会话：浮动面板 + 实时预览 + 关闭时写图层（不阻塞 Rhino）</summary>
    internal class WaterRippleSession
    {
        static readonly List<WaterRippleSession> _live = new List<WaterRippleSession>();

        readonly RhinoDoc _doc;
        readonly List<Guid> _targets;
        readonly WaterRipplePanel _panel;
        readonly WaterRipplePreviewConduit _conduit = new WaterRipplePreviewConduit();
        readonly Timer _timer = new Timer();
        List<WaterRippleResult> _results = new List<WaterRippleResult>();
        bool _closed;

        public WaterRippleSession(RhinoDoc doc, List<Guid> targets, WaterRippleSettings settings, string desc)
        {
            _doc = doc;
            _targets = targets != null ? new List<Guid>(targets) : new List<Guid>();
            _panel = new WaterRipplePanel(settings, desc);
            _panel.PickRequested += delegate { RunPick(); };
        }

        internal static WaterRippleSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        /// <summary>目标摘要（1 个就报名字，多个报数量 + 第一个）</summary>
        internal static string DescribeTargets(RhinoDoc doc, List<Guid> ids)
        {
            if (ids == null || ids.Count == 0) return WaterRippleCore.EmptyTargetHint;
            if (ids.Count == 1)
            {
                RhinoObject o = doc.Objects.FindId(ids[0]);
                return "目标：" + WaterRippleCore.Describe(o);
            }
            RhinoObject first = doc.Objects.FindId(ids[0]);
            return string.Format("{0} 个目标（{1} 等）", ids.Count, WaterRippleCore.Describe(first));
        }

        public void SetTargets(RhinoDoc doc, List<Guid> ids, string desc)
        {
            if (_closed) return;
            _targets.Clear();
            if (ids != null) _targets.AddRange(ids);
            _panel.SetTarget(desc);
            // 选到的东西要能真的解析成可用目标才算「选对」；选错/选到不支持的类型一律红
            int valid; string why;
            ValidateTargets(doc, out valid, out why);
            _panel.SetTargetState(valid > 0);
            if (valid == 0 && _targets.Count > 0)
                _panel.SetInfo("所选对象不可用：" + (why ?? "无法解析") + "\r\n请选曲面 / 多重曲面 / 闭合平面曲线。");
            _results = new List<WaterRippleResult>();
            _conduit.Shaded = null;
            _conduit.SmoothSubDs = null;
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        /// <summary>目标里有多少个能解析（选错类型时用来把按钮置红）</summary>
        void ValidateTargets(RhinoDoc doc, out int valid, out string firstWhy)
        {
            valid = 0; firstWhy = null;
            for (int i = 0; i < _targets.Count; i++)
            {
                RhinoObject o = null;
                try { o = doc.Objects.FindId(_targets[i]); } catch { }
                GeometryBase g; string why;
                if (WaterRippleCore.TryResolve(o, out g, out why)) valid++;
                else if (firstWhy == null) firstWhy = why;
            }
        }

        void RunPick()
        {
            try { RhinoApp.RunScript("_-WaterRipplePickTarget", false); }
            catch (Exception ex) { _panel.SetInfo("选择失败：" + ex.Message); }
        }

        public void Start()
        {
            foreach (WaterRippleSession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 90;
            _timer.Tick += delegate { _timer.Stop(); Rebuild(); };
            _panel.ValueChanged += delegate
            {
                if (!_panel.LivePreview) return;
                _timer.Stop(); _timer.Start();
            };
            _panel.FormClosed += delegate { OnClosed(); };

            RhinoDoc.ReplaceRhinoObject += OnReplaceObject;
            RhinoDoc.DeleteRhinoObject += OnDeleteObject;

            _conduit.Enabled = true;
            _panel.Show();
            Rebuild();
            _panel.Activate();
            _live.Add(this);
        }

        void OnReplaceObject(object sender, RhinoReplaceObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (e.Document != null && e.Document.RuntimeSerialNumber != _doc.RuntimeSerialNumber) return;
                if (_targets.Contains(e.ObjectId)) Rebuild();
            }
            catch { }
        }

        void OnDeleteObject(object sender, RhinoObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (!_targets.Contains(e.ObjectId)) return;
                _targets.Remove(e.ObjectId);
                _conduit.Shaded = null;
                _conduit.SmoothSubDs = null;
                _panel.SetTarget(DescribeTargets(_doc, _targets));
                _panel.SetTargetState(_targets.Count > 0);
                _panel.SetInfo(_targets.Count > 0 ? "一个目标被删除，已用剩下的目标重算。" : "目标已被删除，请点「选择目标」重选。");
                Rebuild();
                _doc.Views.Redraw();
            }
            catch { }
        }

        void Rebuild()
        {
            if (_closed) return;
            try
            {
                if (_targets.Count == 0)
                {
                    _results = new List<WaterRippleResult>();
                    _conduit.Shaded = null;
                    _conduit.SmoothSubDs = null;
                    _panel.SetInfo(WaterRippleCore.EmptyTargetHint);
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }

                WaterRippleSettings cur = _panel.Settings;
                _results = new List<WaterRippleResult>();
                var meshAll = new Mesh();
                var subs = new List<SubD>();
                int failed = 0, verts = 0, faces = 0;
                double secs = 0, loH = double.MaxValue, hiH = double.MinValue;
                string firstNote = "";

                for (int i = 0; i < _targets.Count; i++)
                {
                    RhinoObject o = null;
                    try { o = _doc.Objects.FindId(_targets[i]); } catch { }
                    GeometryBase g; string why;
                    if (!WaterRippleCore.TryResolve(o, out g, out why))
                    {
                        failed++;
                        if (firstNote.Length == 0) firstNote = "目标不可用：" + why;
                        continue;
                    }
                    string rep;
                    WaterRippleResult r = WaterRippleCore.Generate(g, cur, out rep);
                    _results.Add(r);
                    secs += r.Seconds;
                    if (!string.IsNullOrEmpty(r.Error))
                    {
                        failed++;
                        if (firstNote.Length == 0) firstNote = r.Error;
                        continue;
                    }
                    if (firstNote.Length == 0) firstNote = r.Note;
                    verts += r.Vertices; faces += r.Faces;
                    if (r.MinH < loH) loH = r.MinH;
                    if (r.MaxH > hiH) hiH = r.MaxH;
                    if (r.Ripple != null) { try { meshAll.Append(r.Ripple); } catch { } }
                    if (r.SmoothSubD != null) subs.Add(r.SmoothSubD);
                }

                if (_panel.LivePreview)
                {
                    // 预览 = 输出：勾了一键平滑就画细分曲面，否则画网格
                    _conduit.Shaded = meshAll.Faces.Count > 0 ? meshAll : null;
                    _conduit.SmoothSubDs = subs.Count > 0 ? subs : null;
                    _doc.Views.Redraw();
                }

                int validTargets; string validWhy;
                ValidateTargets(_doc, out validTargets, out validWhy);
                _panel.SetTargetState(validTargets > 0);

                string line = string.Format("{0} 个目标：{1} 顶点 / {2} 面 · 位移 {3:0.###}~{4:0.###} · {5:0.00}s",
                    _results.Count, verts, faces,
                    loH == double.MaxValue ? 0 : loH, hiH == double.MinValue ? 0 : hiH, secs);
                if (failed > 0) line += string.Format(" · {0} 个目标失败", failed);
                _panel.SetInfo(line + "\r\n" + FirstNote(firstNote));
            }
            catch (Exception ex)
            {
                _panel.SetInfo("计算失败：" + ex.Message);
            }
        }

        static string FirstNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            string[] lines = note.Split('\n');
            if (lines.Length == 0) return "";
            string s = lines[0].Trim();
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
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
                    int meshCount, smoothCount;
                    uint undo = _doc.BeginUndoRecord("生成水波纹");
                    try
                    {
                        WaterRippleCore.AddToDocument(_doc, _results, out meshCount, out smoothCount);
                    }
                    finally { _doc.EndUndoRecord(undo); }
                    RhinoApp.WriteLine(smoothCount > 0
                        ? string.Format("已生成 {0} 份细分曲面（图层：{1}）。", smoothCount, WaterRippleCore.LayerSmooth)
                        : string.Format("已生成 {0} 个网格面（图层：{1}）。", meshCount, WaterRippleCore.LayerMesh));
                    try { _doc.Views.Redraw(); } catch { }
                }
                catch (Exception ex) { RhinoApp.WriteLine("写入失败：" + ex.Message); }
            }
            else if (_panel.Committed)
            {
                RhinoApp.WriteLine("没有选择目标，未生成任何几何。");
            }
            else
            {
                RhinoApp.WriteLine("已取消，未生成任何几何。");
            }

            _closed = true;
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            try { _conduit.Enabled = false; } catch { }
            try { RhinoDoc.ReplaceRhinoObject -= OnReplaceObject; } catch { }
            try { RhinoDoc.DeleteRhinoObject -= OnDeleteObject; } catch { }
            _live.Remove(this);
            try { _doc.Views.Redraw(); } catch { }
        }
    }
}
