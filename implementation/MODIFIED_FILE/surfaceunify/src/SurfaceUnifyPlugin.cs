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
[assembly: Guid("4E2B7A19-3C58-4D6E-8F01-9A2B3C4D5E60")]
#else
[assembly: Guid("7F3C8B2A-4D69-4E7F-9021-AB3C4D5E6F71")]
#endif

namespace SurfaceUnifyPattern
{
    /// <summary>插件入口：多重曲面 → 单一曲面（Rhino 7 / 8 用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("4E2B7A19-3C58-4D6E-8F01-9A2B3C4D5E60")]
#else
    [Guid("7F3C8B2A-4D69-4E7F-9021-AB3C4D5E6F71")]
#endif
    public class SurfaceUnifyPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            MaybeRunProbe();
            RhinoApp.WriteLine("多重曲面转单一曲面插件已加载。命令：SurfaceUnify");
            return LoadReturnCode.Success;
        }

        /// <summary>无人值守诊断：run-surfaceunify-probe.flag（内容见 SurfaceUnifyProbeCommand 注释）</summary>
        static void MaybeRunProbe()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                if (!File.Exists(Path.Combine(dir, "run-surfaceunify-probe.flag"))) return;
                RhinoApp.Idle += ProbeOnIdle;
            }
            catch { }
        }

        static void ProbeOnIdle(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= ProbeOnIdle;
                SurfaceUnifyProbeCommand.RunFromFlag();
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("单一曲面诊断失败：" + ex.Message); } catch { }
            }
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-surfaceunify-selftest.flag，
        /// 下次启动 Rhino 自动跑几何自检并写报告到同目录 SurfaceUnifySelfTest.txt，跑完退出。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-surfaceunify-selftest.flag");
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
                SurfaceUnifySelfTestCommand.RunTo(RhinoDoc.ActiveDoc, SurfaceUnifySelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("单一曲面自检失败：" + ex.Message); } catch { }
            }
        }
    }

    public class SurfaceUnifyCommand : Command
    {
        public override string EnglishName { get { return "SurfaceUnify"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var settings = new SurfaceUnifySettings();
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

            string desc = SurfaceUnifySession.DescribeTargets(doc, ids);
            var session = new SurfaceUnifySession(doc, ids, settings, desc);
            session.Start();
            RhinoApp.WriteLine(ids.Count > 0
                ? "参数面板已打开（已用选中的 " + ids.Count + " 个物件作为目标）。"
                : "参数面板已打开。点面板上的「选择目标」选多重曲面 / 曲面 / 挤出体 / 网格。");
            return Result.Success;
        }
    }

    /// <summary>面板上「选择目标」按钮调用的隐藏命令（面板是非模态的，GetObject 只能在命令上下文里跑）</summary>
    public class SurfaceUnifyPickTargetCommand : Command
    {
        public override string EnglishName { get { return "SurfaceUnifyPickTarget"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            SurfaceUnifySession session = SurfaceUnifySession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 SurfaceUnify 打开参数面板。");
                return Result.Nothing;
            }

            var go = new GetObject();
            go.SetCommandPrompt("选择目标：多重曲面 / 曲面 / 挤出体 / 网格（可多选，回车结束）");
            go.GeometryFilter = ObjectType.Brep | ObjectType.Surface | ObjectType.Mesh | ObjectType.Extrusion;
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

            session.SetTargets(doc, ids, SurfaceUnifySession.DescribeTargets(doc, ids));
            RhinoApp.WriteLine("目标已更新：" + SurfaceUnifySession.DescribeTargets(doc, ids));
            return Result.Success;
        }
    }

    /// <summary>
    /// 诊断命令（实测/排查用）：把目标的边界环结构（几条环、各自面积/周长、4 角、4 条边长）
    /// 和拟合偏差打成报告，结果写进当前文档（图层「单一曲面」）。
    /// 支持无人值守：%LOCALAPPDATA%\IVAN\logs\run-surfaceunify-probe.flag，内容为 key=value 多行：
    ///   open=D:\...\x.3dm   save=D:\...\out.3dm   capture=%TEMP%\x.png   report=...
    ///   grid=12  fit=1  smooth=0.15  lock=1  holes=1  trim=1  snap=0
    /// </summary>
    public class SurfaceUnifyProbeCommand : Command
    {
        public override string EnglishName { get { return "SurfaceUnifyProbe"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\SurfaceUnifyProbe.txt");
            }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RunProbe(doc, new SurfaceUnifySettings(), null, null, DefaultReportPath);
            RhinoApp.WriteLine("诊断报告：" + DefaultReportPath);
            return Result.Success;
        }

        internal static void RunFromFlag()
        {
            string dir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
            string flag = Path.Combine(dir, "run-surfaceunify-probe.flag");
            var kv = new Dictionary<string, string>();
            try
            {
                if (File.Exists(flag))
                    foreach (string line in File.ReadAllLines(flag))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) kv[line.Substring(0, i).Trim().ToLowerInvariant()] = line.Substring(i + 1).Trim();
                    }
            }
            catch { }
            try { File.Delete(flag); } catch { }

            var s = new SurfaceUnifySettings();
            int n; double d;
            string v;
            if (kv.TryGetValue("grid", out v) && int.TryParse(v, out n)) s.GridCount = n;
            if (kv.TryGetValue("fit", out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.FitStrength = d;
            if (kv.TryGetValue("smooth", out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.Smooth = d;
            if (kv.TryGetValue("snap", out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.MaxSnapDistance = d;
            if (kv.TryGetValue("lock", out v)) s.LockBoundary = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
            if (kv.TryGetValue("holes", out v)) s.KeepHoles = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
            if (kv.TryGetValue("trim", out v)) s.AllowTrim = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            string open = Get(kv, "open");
            if (!string.IsNullOrEmpty(open) && File.Exists(open))
            {
                RhinoDoc opened = null;
                try
                {
                    bool already;
                    opened = RhinoDoc.Open(open, out already);
                }
                catch { }
                if (opened != null) doc = opened;
            }

            string report = Get(kv, "report");
            RunProbe(doc, s, Get(kv, "save"), Get(kv, "capture"),
                string.IsNullOrEmpty(report) ? DefaultReportPath : report);
            try { RhinoApp.Exit(); } catch { }
        }

        static string Get(Dictionary<string, string> kv, string key)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : null;
        }

        internal static void RunProbe(RhinoDoc doc, SurfaceUnifySettings s, string savePath, string capturePath, string reportPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 多重曲面 → 单一曲面 诊断报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            if (doc == null) { WriteReport(reportPath, sb); return; }
            sb.AppendLine("文档：" + (string.IsNullOrEmpty(doc.Path) ? "(未保存)" : doc.Path));

            var targets = new List<RhinoObject>();
            try
            {
                foreach (RhinoObject o in doc.Objects)
                {
                    if (o == null) continue;
                    GeometryBase g = o.Geometry;
                    if (g is Brep bp && bp.Faces.Count > 0) targets.Add(o);
                }
            }
            catch { }
            sb.AppendLine("目标数：" + targets.Count);
            if (targets.Count == 0) { WriteReport(reportPath, sb); return; }

            int layer = SurfaceUnifyCore.EnsureLayer(doc, SurfaceUnifyCore.LayerSurface, System.Drawing.Color.FromArgb(168, 69, 111));
            var attrs = new ObjectAttributes();
            attrs.LayerIndex = layer;
            int written = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine("---- 目标 " + (i + 1) + "：" + SurfaceUnifyCore.Describe(targets[i]) + " ----");
                try { sb.Append(SurfaceUnifyCore.Diagnose(targets[i].Geometry, s)); }
                catch (Exception ex) { sb.AppendLine("诊断异常：" + ex.Message); }

                string rep;
                SurfaceUnifyResult r = SurfaceUnifyCore.Generate(targets[i].Geometry, s, out rep);
                if (r.Result != null && r.Result.Faces.Count > 0)
                {
                    attrs.Name = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "单一曲面 {0}（偏差 {1:0.####}）", i + 1, r.MaxDeviation);
                    if (doc.Objects.AddBrep(r.Result, attrs) != Guid.Empty) written++;
                }
            }
            sb.AppendLine();
            sb.AppendLine("写入文档：" + written + " 张单一曲面（图层 " + SurfaceUnifyCore.LayerSurface + "）");
            WriteReport(reportPath, sb);

            if (!string.IsNullOrEmpty(capturePath))
            {
                try
                {
                    RhinoApp.RunScript("_-SelAll _-Zoom _Selected _-SelNone", false);
                    RhinoApp.RunScript(string.Format("_-ViewCaptureToFile \"{0}\" _Enter", capturePath), false);
                    sb.AppendLine("截图：" + capturePath);
                }
                catch (Exception ex) { sb.AppendLine("截图失败：" + ex.Message); }
                WriteReport(reportPath, sb);
            }

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    bool ok = doc.WriteFile(savePath, new Rhino.FileIO.FileWriteOptions());
                    sb.AppendLine("另存：" + savePath + " → " + (ok ? "成功" : "失败"));
                }
                catch (Exception ex) { sb.AppendLine("另存异常：" + ex.Message); }
                WriteReport(reportPath, sb);
            }
        }

        static void WriteReport(string reportPath, System.Text.StringBuilder sb)
        {
            if (string.IsNullOrEmpty(reportPath)) return;
            try
            {
                string d = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                File.WriteAllText(reportPath, sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch { }
            try { RhinoApp.WriteLine(sb.ToString()); } catch { }
        }
    }

    /// <summary>一次「多重曲面 → 单一曲面」会话：浮动面板 + 实时预览 + 关闭时写图层（不阻塞 Rhino）</summary>
    internal class SurfaceUnifySession
    {
        static readonly List<SurfaceUnifySession> _live = new List<SurfaceUnifySession>();

        readonly RhinoDoc _doc;
        readonly List<Guid> _targets;
        readonly SurfaceUnifyPanel _panel;
        readonly SurfaceUnifyPreviewConduit _conduit = new SurfaceUnifyPreviewConduit();
        readonly Timer _timer = new Timer();
        List<SurfaceUnifyResult> _results = new List<SurfaceUnifyResult>();
        bool _closed;

        public SurfaceUnifySession(RhinoDoc doc, List<Guid> targets, SurfaceUnifySettings settings, string desc)
        {
            _doc = doc;
            _targets = targets != null ? new List<Guid>(targets) : new List<Guid>();
            _panel = new SurfaceUnifyPanel(settings, desc);
            _panel.PickRequested += delegate { RunPick(); };
        }

        internal static SurfaceUnifySession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        /// <summary>目标摘要（1 个就报名字，多个报数量 + 第一个）</summary>
        internal static string DescribeTargets(RhinoDoc doc, List<Guid> ids)
        {
            if (ids == null || ids.Count == 0) return SurfaceUnifyCore.EmptyTargetHint;
            if (ids.Count == 1)
            {
                RhinoObject o = doc.Objects.FindId(ids[0]);
                return "目标：" + SurfaceUnifyCore.Describe(o);
            }
            RhinoObject first = doc.Objects.FindId(ids[0]);
            return string.Format("{0} 个目标（{1} 等）", ids.Count, SurfaceUnifyCore.Describe(first));
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
                _panel.SetInfo("所选对象不可用：" + (why ?? "无法解析") + "\r\n请选多重曲面 / 曲面 / 挤出体 / 网格（片状、有裸露边界）。");
            _results = new List<SurfaceUnifyResult>();
            _conduit.Shaded = null;
            _conduit.SourceBoundary = null;
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
                if (SurfaceUnifyCore.TryResolve(o, out g, out why)) valid++;
                else if (firstWhy == null) firstWhy = why;
            }
        }

        void RunPick()
        {
            try { RhinoApp.RunScript("_-SurfaceUnifyPickTarget", false); }
            catch (Exception ex) { _panel.SetInfo("选择失败：" + ex.Message); }
        }

        public void Start()
        {
            foreach (SurfaceUnifySession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 120;
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
                _conduit.SourceBoundary = null;
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
                    _results = new List<SurfaceUnifyResult>();
                    _conduit.Shaded = null;
                    _conduit.SourceBoundary = null;
                    _panel.SetInfo(SurfaceUnifyCore.EmptyTargetHint);
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }

                SurfaceUnifySettings cur = _panel.Settings;
                _results = new List<SurfaceUnifyResult>();
                var breps = new List<Brep>();
                var loops = new List<Polyline>();
                int failed = 0, faces = 0, holes = 0;
                double secs = 0, worstDev = 0, worstBoundary = 0;
                string firstNote = "";

                for (int i = 0; i < _targets.Count; i++)
                {
                    RhinoObject o = null;
                    try { o = _doc.Objects.FindId(_targets[i]); } catch { }
                    GeometryBase g; string why;
                    if (!SurfaceUnifyCore.TryResolve(o, out g, out why))
                    {
                        failed++;
                        if (firstNote.Length == 0) firstNote = "目标不可用：" + why;
                        continue;
                    }
                    string rep;
                    SurfaceUnifyResult r = SurfaceUnifyCore.Generate(g, cur, out rep);
                    _results.Add(r);
                    secs += r.Seconds;
                    if (!string.IsNullOrEmpty(r.Error))
                    {
                        failed++;
                        if (firstNote.Length == 0) firstNote = r.Error;
                        continue;
                    }
                    if (firstNote.Length == 0) firstNote = r.Note;
                    if (r.Result != null && r.Result.Faces.Count > 0) { breps.Add(r.Result); faces += r.Result.Faces.Count; }
                    holes += r.Holes;
                    if (r.MaxDeviation > worstDev) worstDev = r.MaxDeviation;
                    if (r.BoundaryDeviation > worstBoundary) worstBoundary = r.BoundaryDeviation;
                    if (r.SourceBoundary != null) loops.AddRange(r.SourceBoundary);
                }

                if (_panel.LivePreview)
                {
                    // 预览 = 输出：结果面着色 + 线框，原边界红线
                    _conduit.Shaded = breps.Count > 0 ? breps : null;
                    _conduit.SourceBoundary = loops.Count > 0 ? loops : null;
                    _conduit.ShowSourceBoundary = cur.ShowSourceBoundary;
                    _doc.Views.Redraw();
                }

                int validTargets; string validWhy;
                ValidateTargets(_doc, out validTargets, out validWhy);
                _panel.SetTargetState(validTargets > 0);

                string line = string.Format("{0} 个目标 → {1} 张单一曲面（{2} 面 · {3} 个内孔）· 最大偏差 {4:0.####} · 边界偏差 {5:0.####} · {6:0.00}s",
                    _results.Count, breps.Count, faces, holes, worstDev, worstBoundary, secs);
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
                    int count;
                    uint undo = _doc.BeginUndoRecord("生成单一曲面");
                    try
                    {
                        SurfaceUnifyCore.AddToDocument(_doc, _results, out count);
                    }
                    finally { _doc.EndUndoRecord(undo); }
                    RhinoApp.WriteLine(string.Format("已生成 {0} 张单一曲面（图层：{1}）。", count, SurfaceUnifyCore.LayerSurface));
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
