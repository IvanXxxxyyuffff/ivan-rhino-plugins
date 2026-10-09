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
[assembly: Guid("6A1D4E27-9C83-4B15-A7F2-3D58E1B96C40")]
#else
[assembly: Guid("D2F75B18-4E69-4A3C-8B51-7C0E29D6F3A8")]
#endif

namespace DiamondFacetPattern
{
    /// <summary>插件入口：钻石切面（Rhino 7 / 8 用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("6A1D4E27-9C83-4B15-A7F2-3D58E1B96C40")]
#else
    [Guid("D2F75B18-4E69-4A3C-8B51-7C0E29D6F3A8")]
#endif
    public class DiamondFacetPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            RhinoApp.WriteLine("钻石切面插件已加载。命令：DiamondFacet");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-diamondfacet-selftest.flag，
        /// 下次启动 Rhino 自动跑几何自检并写报告到同目录 DiamondFacetSelfTest.txt，跑完退出。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-diamondfacet-selftest.flag");
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
                DiamondFacetSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, DiamondFacetSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("钻石切面自检失败：" + ex.Message); } catch { }
            }
        }
    }

    public class DiamondFacetCommand : Command
    {
        public override string EnglishName { get { return "DiamondFacet"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var settings = new DiamondFacetSettings();
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            // 预选的物件直接当边界；没预选就用空目标打开面板，由面板上的「选择边界」来选
            var ids = new List<Guid>();
            try
            {
                foreach (RhinoObject o in doc.Objects.GetSelectedObjects(false, false))
                    if (o != null) ids.Add(o.Id);
            }
            catch { }

            string desc = DiamondFacetSession.DescribeTargets(doc, ids);
            var session = new DiamondFacetSession(doc, ids, settings, desc);
            session.Start();
            RhinoApp.WriteLine(ids.Count > 0
                ? "参数面板已打开（已用选中的 " + ids.Count + " 个物件作为边界）。"
                : "参数面板已打开。点面板上的「选择边界」选平面或闭合曲线（可多选）。");
            return Result.Success;
        }
    }

    /// <summary>面板上「选择边界」按钮调用的隐藏命令（面板是非模态的，GetObject 只能在命令上下文里跑）</summary>
    public class DiamondFacetPickTargetCommand : Command
    {
        public override string EnglishName { get { return "DiamondFacetPickTarget"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            DiamondFacetSession session = DiamondFacetSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 DiamondFacet 打开参数面板。");
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

            session.SetTargets(doc, ids, DiamondFacetSession.DescribeTargets(doc, ids));
            RhinoApp.WriteLine("边界已更新：" + DiamondFacetSession.DescribeTargets(doc, ids));
            return Result.Success;
        }
    }

    /// <summary>一次「钻石切面」会话：浮动面板 + 实时预览 + 关闭时写图层（不阻塞 Rhino）</summary>
    internal class DiamondFacetSession
    {
        static readonly List<DiamondFacetSession> _live = new List<DiamondFacetSession>();
        const int MaxPreviewCurves = 8000;      // 预览最多画这么多条线，多了只画一部分（面板里如实说明）

        readonly RhinoDoc _doc;
        readonly List<Guid> _targets;
        readonly DiamondFacetPanel _panel;
        readonly DiamondFacetPreviewConduit _conduit = new DiamondFacetPreviewConduit();
        readonly Timer _timer = new Timer();
        List<DiamondFacetResult> _results = new List<DiamondFacetResult>();
        bool _closed;

        public DiamondFacetSession(RhinoDoc doc, List<Guid> targets, DiamondFacetSettings settings, string desc)
        {
            _doc = doc;
            _targets = targets != null ? new List<Guid>(targets) : new List<Guid>();
            _panel = new DiamondFacetPanel(settings, desc);
            _panel.PickRequested += delegate { RunPick(); };
        }

        internal static DiamondFacetSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        /// <summary>边界摘要（1 个就报名字，多个报数量 + 第一个）</summary>
        internal static string DescribeTargets(RhinoDoc doc, List<Guid> ids)
        {
            if (ids == null || ids.Count == 0) return DiamondFacetCore.EmptyTargetHint;
            if (ids.Count == 1)
            {
                RhinoObject o = doc.Objects.FindId(ids[0]);
                return "边界：" + DiamondFacetCore.Describe(o);
            }
            RhinoObject first = doc.Objects.FindId(ids[0]);
            return string.Format("{0} 个边界（{1} 等）", ids.Count, DiamondFacetCore.Describe(first));
        }

        public void SetTargets(RhinoDoc doc, List<Guid> ids, string desc)
        {
            if (_closed) return;
            _targets.Clear();
            if (ids != null) _targets.AddRange(ids);
            _panel.SetTarget(desc);
            // 选到的东西要能真的解析成平面边界才算「选对」；选错/选到不支持的类型一律红
            int valid; string why;
            ValidateTargets(doc, out valid, out why);
            _panel.SetTargetState(valid > 0);
            if (valid == 0 && _targets.Count > 0)
                _panel.SetInfo("所选对象不可用：" + (why ?? "无法解析成平面边界") + "\r\n请选平面 / 平面曲面 / 闭合平面曲线。");
            _results = new List<DiamondFacetResult>();
            _conduit.Curves = new List<Curve>();
            _conduit.Shaded = null;
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        /// <summary>目标里有多少个能解析成平面边界（选错类型时用来把按钮置红）</summary>
        void ValidateTargets(RhinoDoc doc, out int valid, out string firstWhy)
        {
            valid = 0; firstWhy = null;
            for (int i = 0; i < _targets.Count; i++)
            {
                RhinoObject o = null;
                try { o = doc.Objects.FindId(_targets[i]); } catch { }
                FacetBoundary b; string why;
                if (DiamondFacetCore.TryResolve(o, out b, out why)) valid++;
                else if (firstWhy == null) firstWhy = why;
            }
        }

        void RunPick()
        {
            try { RhinoApp.RunScript("_-DiamondFacetPickTarget", false); }
            catch (Exception ex) { _panel.SetInfo("选择失败：" + ex.Message); }
        }

        public void Start()
        {
            foreach (DiamondFacetSession s in _live.ToArray()) s.ClosePanel();

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
                _conduit.Curves = new List<Curve>();
                _conduit.Shaded = null;
                _panel.SetTarget(DescribeTargets(_doc, _targets));
                _panel.SetTargetState(_targets.Count > 0);
                _panel.SetInfo(_targets.Count > 0 ? "一个边界被删除，已用剩下的边界重算。" : "边界已被删除，请点「选择边界」重选。");
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
                    _results = new List<DiamondFacetResult>();
                    _conduit.Curves = new List<Curve>();
                    _conduit.Shaded = null;
                    _panel.SetInfo(DiamondFacetCore.EmptyTargetHint);
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }

                DiamondFacetSettings cur = _panel.Settings;
                string report;
                _results = DiamondFacetCore.Generate(_doc, _targets, cur, out report);

                int tris = 0, curves = 0, faces = 0, smooth = 0, smoothFaces = 0, failed = 0;
                double secs = 0;
                var all = new List<Curve>();
                var mesh = new Mesh();
                var smoothMesh = new Mesh();
                foreach (DiamondFacetResult r in _results)
                {
                    tris += r.Triangles; curves += r.Wireframe.Count; faces += r.Faces.Count; secs += r.Seconds;
                    if (!string.IsNullOrEmpty(r.Error)) { failed++; continue; }
                    all.AddRange(r.Wireframe);
                    if (r.Shaded != null) { try { mesh.Append(r.Shaded); } catch { } }
                    if (r.Smoothed && r.SmoothMesh != null)
                    {
                        smooth++; smoothFaces += r.SmoothMesh.Faces.Count;
                        try { smoothMesh.Append(r.SmoothMesh); } catch { }
                    }
                }

                _conduit.OutputMode = cur.OutputMode;
                if (_panel.LivePreview)
                {
                    // 预览与输出一致：仅线框 → 只画线；面 → 只画面（勾了平滑就画平滑后的形状，不叠线框）
                    _conduit.Curves = cur.OutputMode == 0
                        ? (all.Count > MaxPreviewCurves ? all.GetRange(0, MaxPreviewCurves) : all)
                        : new List<Curve>();
                    _conduit.Shaded = cur.OutputMode == 1
                        ? ((cur.Smooth && smoothMesh.Faces.Count > 0) ? smoothMesh : mesh)
                        : null;   // 面模式：着色 + conduit 里画网格线框
                    _doc.Views.Redraw();
                }

                int validTargets; string validWhy;
                ValidateTargets(_doc, out validTargets, out validWhy);
                _panel.SetTargetState(validTargets > 0);

                string line = string.Format("{0} 个边界：{1} 个三角面 · {2} 条线{3} · {4:0.00}s",
                    _results.Count, tris, curves,
                    cur.OutputMode == 0
                        ? "（仅线框，不带面）"
                        : (cur.Smooth
                            ? string.Format(" · {0} 张网格片 / {1} 个三角面", smooth, smoothFaces)
                            : string.Format(" · {0} 张平面面（不带线框）", faces)),
                    secs);
                if (failed > 0) line += string.Format(" · {0} 个边界失败", failed);
                if (all.Count > MaxPreviewCurves) line += string.Format(" · 预览只画前 {0} 条", MaxPreviewCurves);
                _panel.SetInfo(line + "\r\n" + FirstNote(report));
            }
            catch (Exception ex)
            {
                _panel.SetInfo("计算失败：" + ex.Message);
            }
        }

        static string FirstNote(string report)
        {
            if (string.IsNullOrEmpty(report)) return "";
            string[] lines = report.Split('\n');
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
                    int curveCount, faceCount, smoothCount;
                    uint undo = _doc.BeginUndoRecord("生成钻石切面");
                    try
                    {
                        DiamondFacetCore.AddToDocument(_doc, _results, _panel.Settings.OutputMode,
                            out curveCount, out faceCount, out smoothCount);
                    }
                    finally { _doc.EndUndoRecord(undo); }
                    RhinoApp.WriteLine(_panel.Settings.OutputMode == 0
                        ? string.Format("已生成 {0} 条线框曲线（图层：{1}）。", curveCount, DiamondFacetCore.LayerWire)
                        : (smoothCount > 0
                            ? string.Format("已生成 {0} 份细分网格（每个三角切面一张网格片，图层：{1}）。",
                                smoothCount, DiamondFacetCore.LayerSmooth)
                            : string.Format("已生成 {0} 张平面面（图层：{1}，不带线框）。", faceCount, DiamondFacetCore.LayerFace)));
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
            try { _conduit.Enabled = false; } catch { }
            try { RhinoDoc.ReplaceRhinoObject -= OnReplaceObject; } catch { }
            try { RhinoDoc.DeleteRhinoObject -= OnDeleteObject; } catch { }
            _live.Remove(this);
            try { _doc.Views.Redraw(); } catch { }
        }
    }
}
