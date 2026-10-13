using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;   // [assembly: Guid] 需要（否则 Guid 解析成 System.Guid）
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.Input.Custom;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.PlugIns;

// Rhino 按「程序集级」[assembly: Guid] 识别插件 ID；只写类级 [Guid] 会被登记成全零 GUID。
#if RH7
[assembly: Guid("FD17B96E-7D68-4E36-AF3D-D24837F3F8BF")]
#else
[assembly: Guid("719FCC1D-E8AE-4B5F-AE71-AE3DBF6699EA")]
#endif

namespace PatternEmboss
{
    /// <summary>插件入口：图案压花（Rhino 7 / 8 用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("FD17B96E-7D68-4E36-AF3D-D24837F3F8BF")]
#else
    [Guid("719FCC1D-E8AE-4B5F-AE71-AE3DBF6699EA")]
#endif
    public class PatternEmbossPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            MaybeRunProbe();
            RhinoApp.WriteLine("图案压花插件已加载。命令：PatternEmboss");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：%LOCALAPPDATA%\IVAN\logs\ 放 run-emboss-selftest.flag →
        /// 以 /nosplash 启动的无人值守实例自动跑几何自检并写报告（普通实例不消费旗标，不会被劫持退出）。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                if (!IsUnattended()) return;
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-emboss-selftest.flag");
                if (!File.Exists(flag)) return;
                string claimed = flag + ".claimed";
                try { File.Delete(claimed); } catch { }
                try { File.Move(flag, claimed); } catch { return; }   // 原子领取；抢不到说明别的实例在处理
                try { File.Delete(claimed); } catch { }               // 领取后即删：即使没跑完也不会劫持下一次启动
                RhinoApp.Idle += SelfTestOnIdle;
            }
            catch { }
        }

        static void SelfTestOnIdle(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= SelfTestOnIdle;
                PatternEmbossSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, PatternEmbossSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("图案压花自检失败：" + ex.Message); } catch { }
            }
        }

        /// <summary>无人值守探针：%LOCALAPPDATA%\IVAN\logs\run-emboss-probe.flag（key=value 多行）
        ///   bench=&lt;保存路径&gt;：造测试曲面（平板 + 球）另存 3dm；save= 另存当前文档；report= 报告路径；qr= 诊断矩阵
        ///   仅 /nosplash 启动的无人值守实例消费旗标；消费即删（普通实例不受影响、也不会被劫持退出）</summary>
        static void MaybeRunProbe()
        {
            RhinoApp.Idle += PatternEmbossProbeOnIdle;
        }

        static void PatternEmbossProbeOnIdle(object sender, EventArgs e)
        {
            try
            {
                if (!IsUnattended()) return;
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flagPath = Path.Combine(dir, "run-emboss-probe.flag");
                if (!File.Exists(flagPath)) return;
                string claimed = flagPath + ".claimed";
                try { File.Delete(claimed); } catch { }
                try { File.Move(flagPath, claimed); } catch { return; }   // 原子领取
                string content = File.ReadAllText(claimed);
                if (string.IsNullOrEmpty(content)) { try { File.Delete(claimed); } catch { } return; }
                RhinoDoc d = RhinoDoc.ActiveDoc;
                if ((d == null || d.Objects.Count == 0) && content.Contains("bench="))
                {
                    try { File.Move(claimed, flagPath); } catch { }      // 条件不满足 → 退回旗标，留给之后的实例
                    return;
                }
                try { File.Delete(claimed); } catch { }                   // 消费即删：不会劫持下一次启动
                PatternEmbossProbeCommand.RunFromFlag(content, d);
            }
            catch { }
        }

        /// <summary>无人值守实例判定：命令行含 /nosplash（工具链启动的隐藏实例）。普通启动的实例不消费任何旗标。</summary>
        static int _unattended = -1;   // -1 未判定 / 0 否 / 1 是（进程启动参数不变，判一次即可）
        static bool IsUnattended()
        {
            if (_unattended >= 0) return _unattended == 1;
            bool ok = false;
            try
            {
                foreach (string a in System.Environment.GetCommandLineArgs())
                    if (a != null && a.IndexOf("nosplash", StringComparison.OrdinalIgnoreCase) >= 0) { ok = true; break; }
            }
            catch { }
            _unattended = ok ? 1 : 0;
            return ok;
        }
    }

    public class PatternEmbossCommand : Command
    {
        public override string EnglishName { get { return "PatternEmboss"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode) { return PatternEmbossLauncher.Run(doc); }
    }

    /// <summary>选目标曲面的隐藏命令（面板「选择曲面」按钮调用；面板非模态，GetObject 只能在命令上下文跑）</summary>
    public class PatternEmbossPickCommand : Command
    {
        public override string EnglishName { get { return "PatternEmbossPick"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var session = PatternEmbossSession.Active;
            if (session == null) { RhinoApp.WriteLine("请先运行 PatternEmboss 打开参数面板。"); return Result.Nothing; }

            var go = new GetObject();
            go.SetCommandPrompt("选择要压花的曲面 / 多重曲面（可多选）");
            go.GeometryFilter = ObjectType.Surface | ObjectType.Brep;
            go.SubObjectSelect = false;
            go.GetMultiple(1, 0);
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            var breps = new List<Brep>();
            for (int i = 0; i < go.ObjectCount; i++)
            {
                ObjRef r = go.Object(i);
                if (r == null) continue;
                Brep b = null;
                try { b = r.Brep(); } catch { b = null; }
                if (b == null) { try { b = Brep.TryConvertBrep(r.Geometry()); } catch { b = null; } }
                if (b != null && b.Faces.Count > 0) breps.Add(b);
            }
            if (breps.Count == 0) { RhinoApp.WriteLine("没有选到曲面。"); return Result.Failure; }
            session.SetTargets(breps);
            RhinoApp.WriteLine("目标已更新：" + breps.Count + " 个曲面。");
            return Result.Success;
        }
    }

    internal static class PatternEmbossLauncher
    {
        public static Result Run(RhinoDoc doc)
        {
            var settings = new PatternEmbossSettings();
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            // 预选曲面直接当目标（选中即用）；没预选就空面板等拾取
            var breps = new List<Brep>();
            try
            {
                foreach (RhinoObject o in doc.Objects.GetSelectedObjects(false, false))
                {
                    if (o == null) continue;
                    Brep b = null;
                    try { b = o.Geometry as Brep; } catch { b = null; }
                    if (b == null) { try { b = Brep.TryConvertBrep(o.Geometry); } catch { b = null; } }
                    if (b != null && b.Faces.Count > 0) breps.Add(b);
                }
            }
            catch { }

            var session = new PatternEmbossSession(doc, settings, breps);
            session.Start();
            RhinoApp.WriteLine(breps.Count > 0
                ? ("参数面板已打开（已用预选的 " + breps.Count + " 个曲面作为目标）。")
                : "参数面板已打开：点「选择曲面」挑目标，拉杆调纹理；点「生成」写入图层。");
            return Result.Success;
        }

        internal static int EnsureLayer(RhinoDoc doc, string name, Color col)
        {
            foreach (Layer l in doc.Layers)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
            var nl = new Layer();
            nl.Name = name;
            nl.Color = col;
            return doc.Layers.Add(nl);
        }
    }

    /// <summary>一次「图案压花」会话：浮动面板 + 实时预览 + 关闭时写图层（不阻塞 Rhino）</summary>
    internal class PatternEmbossSession
    {
        static readonly List<PatternEmbossSession> _live = new List<PatternEmbossSession>();

        readonly RhinoDoc _doc;
        readonly PatternEmbossPanel _panel;
        readonly PatternEmbossPreviewConduit _conduit = new PatternEmbossPreviewConduit();
        readonly Timer _timer = new Timer();
        readonly List<Brep> _targets = new List<Brep>();
        PatternEmbossResult _result;
        bool _closed;

        public PatternEmbossSession(RhinoDoc doc, PatternEmbossSettings settings, List<Brep> targets)
        {
            _doc = doc;
            if (targets != null) _targets.AddRange(targets);
            _panel = new PatternEmbossPanel(settings, "曲面目标：" + (_targets.Count > 0 ? _targets.Count + " 个" : "未选"));
        }

        internal static PatternEmbossSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        public void SetTargets(List<Brep> breps)
        {
            if (_closed) return;
            _targets.Clear();
            if (breps != null) _targets.AddRange(breps);
            _panel.SetTarget("曲面目标：" + (_targets.Count > 0 ? _targets.Count + " 个" : "未选"), _targets.Count > 0);
            _conduit.Mesh = null;
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        public void Start()
        {
            foreach (PatternEmbossSession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 160;
            _timer.Tick += delegate { _timer.Stop(); Rebuild(); };
            _panel.ValueChanged += delegate
            {
                if (!_panel.LivePreview) return;
                _timer.Stop(); _timer.Start();
            };
            _panel.PickRequested += delegate { RunPick(); };
            _panel.FormClosed += delegate { OnClosed(); };

            _conduit.Enabled = true;
            _panel.Show();
            Rebuild();
            _panel.Activate();
            _live.Add(this);
        }

        void RunPick()
        {
            try { RhinoApp.RunScript("_-PatternEmbossPick", false); }
            catch (Exception ex) { _panel.SetInfo("选择失败：" + ex.Message); }
        }

        void Rebuild()
        {
            if (_closed) return;
            try
            {
                PatternEmbossSettings cur = _panel.Settings;
                if (_targets.Count == 0)
                {
                    _result = null;
                    _conduit.Mesh = null;
                    _panel.SetInfo("先点「选择曲面」挑目标（或在运行命令前预选好曲面）。");
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var merged = new Mesh();
                var notes = new List<string>();
                double minH = double.MaxValue, maxH = double.MinValue;
                bool solid = false;
                foreach (Brep b in _targets)
                {
                    string rep;
                    PatternEmbossResult r = PatternEmbossCore.Generate(b, cur, out rep);
                    if (r.Mesh != null && r.Mesh.Faces.Count > 0)
                    {
                        merged.Append(r.Mesh);
                        if (r.MinH < minH) minH = r.MinH;
                        if (r.MaxH > maxH) maxH = r.MaxH;
                        solid = solid || r.Solid;
                    }
                    if (!string.IsNullOrEmpty(r.Note)) notes.Add(r.Note);
                }
                sw.Stop();

                _result = new PatternEmbossResult();
                _result.Mesh = merged.Faces.Count > 0 ? merged : null;
                _result.Solid = solid;
                _result.Vertices = merged.Vertices.Count;
                _result.Faces = merged.Faces.Count;
                _result.MinH = minH == double.MaxValue ? 0 : minH;
                _result.MaxH = maxH == double.MinValue ? 0 : maxH;
                try { var amp = AreaMassProperties.Compute(merged); _result.Area = amp != null ? amp.Area : 0; } catch { }
                _result.Note = notes.Count > 0 ? string.Join(" ", notes.ToArray()) : "";
                if (_result.Faces > 0)
                {
                    try { merged.Vertices.CombineIdentical(true, true); } catch { }
                    try { merged.FaceNormals.ComputeFaceNormals(); } catch { }
                }

                if (_panel.LivePreview)
                {
                    _conduit.Mesh = _result.Mesh;
                    try { _doc.Views.Redraw(); } catch { }
                }

                if (_result.Faces == 0) { _panel.SetInfo("没有生成网格（" + (string.IsNullOrEmpty(_result.Note) ? "检查目标与参数" : _result.Note) + "）"); return; }
                string qrNote = cur.SubDOutput ? "\r\n一键平滑：图案化四边网直接转 SubD（拓扑沿图案）" : "";
                _panel.SetInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} 顶点 / {1} 面{2} · 高 {3:0.####}~{4:0.####} · 曲面积 {5:0.##} · {6} ms\r\n模式：{7} · 单元 {8:0.##} mm · 槽宽 {9:0.##} mm{10}{11}",
                    _result.Vertices, _result.Faces, _result.Solid ? "（已加厚）" : "",
                    _result.MinH, _result.MaxH, _result.Area, sw.ElapsedMilliseconds,
                    PatternEmbossSettings.PatternNames[Math.Max(0, Math.Min(2, cur.Pattern))],
                    cur.CellSize, cur.Groove,
                    string.IsNullOrEmpty(_result.Note) ? "" : ("\r\n⚠ " + _result.Note),
                    qrNote));
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

            if (_panel.Committed && _result != null && _result.Mesh != null && _result.Mesh.Faces.Count > 0)
            {
                try
                {
                    int layer = PatternEmbossLauncher.EnsureLayer(_doc, "图案压花", Color.FromArgb(26, 132, 152));
                    var attrs = new ObjectAttributes(); attrs.LayerIndex = layer;
                    attrs.Name = "图案压花（" + PatternEmbossSettings.PatternNames[Math.Max(0, Math.Min(2, _panel.Settings.Pattern))] + "）";
                    uint undo = _doc.BeginUndoRecord("生成图案压花");
                    try
                    {
                        bool wrote = false;
                        if (_panel.Settings.SubDOutput)
                        {
                            SubD subd = PatternEmbossCore.ToSubD(_result.Mesh);
                            if (subd != null && _doc.Objects.AddSubD(subd, attrs) != Guid.Empty)
                            {
                                RhinoApp.WriteLine("已生成图案压花 SubD 细分曲面（图案化四边网直接转换；图层：图案压花）。");
                                wrote = true;
                            }
                            else RhinoApp.WriteLine("SubD 转换失败，回退网格输出。");
                        }
                        if (!wrote)
                        {
                            if (_doc.Objects.AddMesh(_result.Mesh, attrs) != Guid.Empty)
                                RhinoApp.WriteLine("已生成图案压花网格（{0} 顶点 / {1} 面；图层：图案压花）。", _result.Vertices, _result.Faces);
                            else RhinoApp.WriteLine("写入失败（AddMesh 返回空）。");
                        }
                    }
                    finally { _doc.EndUndoRecord(undo); }
                    _doc.Views.Redraw();
                }
                catch (Exception ex) { RhinoApp.WriteLine("写入失败：" + ex.Message); }
            }
            else
            {
                RhinoApp.WriteLine("已取消，未生成任何几何。");
            }

            _closed = true;
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            try { _conduit.Enabled = false; } catch { }
            _live.Remove(this);
            try { _doc.Views.Redraw(); } catch { }
        }
    }

    /// <summary>探针命令（无人值守基准/回归用）：造测试曲面 + 另存</summary>
    public class PatternEmbossProbeCommand : Command
    {
        public override string EnglishName { get { return "PatternEmbossProbe"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string rep = ProbeFlags.DefaultReportPath;
            try { File.WriteAllText(rep, "=== PatternEmbossProbe（直接运行无 flag 内容）===\n", new System.Text.UTF8Encoding(false)); } catch { }
            return Result.Success;
        }

        internal static void RunFromFlag(string content, RhinoDoc doc)
        {
            var kv = new Dictionary<string, string>();
            try
            {
                foreach (string line in content.Split('\n'))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) kv[line.Substring(0, i).Trim().ToLowerInvariant()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
            var sb = new System.Text.StringBuilder("=== patternemboss probe ===\n");
            try
            {
                string bench = null; kv.TryGetValue("bench", out bench);
                if (!string.IsNullOrEmpty(bench))
                {
                    // 造测试曲面：平板（40×60）+ 球（R=30）
                    RhinoDoc doc2 = doc;
                    int layer = PatternEmbossLauncher.EnsureLayer(doc2, "embossbench", Color.Gray);
                    var at = new ObjectAttributes(); at.LayerIndex = layer;
                    Brep[] plate = Brep.CreatePlanarBreps(new Curve[] {
                        new PolylineCurve(new Point3d[] { new Point3d(0,0,0), new Point3d(60,0,0), new Point3d(60,40,0), new Point3d(0,40,0), new Point3d(0,0,0) })
                    }, 1e-6);
                    if (plate != null && plate.Length > 0) doc2.Objects.AddBrep(plate[0], at);
                    Brep sphere = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 30.0)));
                    if (sphere != null) doc2.Objects.AddBrep(sphere, at);
                    bool ok = doc2.WriteFile(bench, new Rhino.FileIO.FileWriteOptions());
                    sb.AppendLine("bench=" + bench + " wrote=" + ok + " objects=" + doc2.Objects.Count);
                }
                string save = null; kv.TryGetValue("save", out save);
                if (!string.IsNullOrEmpty(save))
                {
                    bool ok = doc.WriteFile(save, new Rhino.FileIO.FileWriteOptions());
                    sb.AppendLine("save=" + save + " ok=" + ok);
                }
                string qr = null; kv.TryGetValue("qr", out qr);
                if (!string.IsNullOrEmpty(qr)) { sb.AppendLine("== QuadRemesh 诊断 =="); QrDiag(sb); }
                string stages = null; kv.TryGetValue("stages", out stages);
                if (!string.IsNullOrEmpty(stages)) StageDump(doc, stages, sb);
            }
            catch (Exception ex) { sb.AppendLine("EXC: " + ex.Message); }
            try
            {
                string report = null; kv.TryGetValue("report", out report);
                if (string.IsNullOrEmpty(report)) report = ProbeFlags.DefaultReportPath;
                File.WriteAllText(report, sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch { }
            try { RhinoApp.Exit(); } catch { }
        }

        /// <summary>三阶段对比导出：原始网格 / 直接转 SubD / 加厚壳，并排 + 截图</summary>
        static void StageDump(RhinoDoc doc, string path3dm, System.Text.StringBuilder sb)
        {
            try
            {
                string dir = Path.GetDirectoryName(path3dm);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var pl = new PolylineCurve(new Point3d[] { new Point3d(0, 0, 0), new Point3d(60, 0, 0), new Point3d(60, 40, 0), new Point3d(0, 40, 0), new Point3d(0, 0, 0) });
                Brep[] bs = Brep.CreatePlanarBreps(new Curve[] { pl }, 1e-6);
                Brep plate = bs != null && bs.Length > 0 ? bs[0] : null;
                if (plate == null) { sb.AppendLine("stages: 平板构造失败"); return; }
                var s = new PatternEmbossSettings();
                string rep;
                PatternEmbossResult r = PatternEmbossCore.Generate(plate, s, out rep);
                Mesh raw = r.Mesh;
                sb.AppendLine("stages: raw faces=" + (raw != null ? raw.Faces.Count : 0));
                SubD sd = raw != null ? PatternEmbossCore.ToSubD(raw) : null;
                sb.AppendLine("stages: direct-subd ok=" + (sd != null && sd.IsValid) + " err=" + PatternEmbossCore.LastSubDError);
                Mesh subdMesh = null;
                if (sd != null)
                {
                    try
                    {
                        // ToBrep 的签名随 SDK 版本变化（新版无参 / 老版 7.0.20314 需要 options）→ 反射调用
                        Brep sbr = null;
                        foreach (var mi in typeof(SubD).GetMethods())
                        {
                            if (mi.Name != "ToBrep") continue;
                            var ps = mi.GetParameters();
                            try
                            {
                                if (ps.Length == 0) { sbr = (Brep)mi.Invoke(sd, null); break; }
                                if (ps.Length == 1) { sbr = (Brep)mi.Invoke(sd, new object[] { Activator.CreateInstance(ps[0].ParameterType) }); break; }
                            }
                            catch { }
                        }
                        Mesh[] mm = sbr != null ? Mesh.CreateFromBrep(sbr, MeshingParameters.Default) : null;
                        var acc = new Mesh();
                        if (mm != null) foreach (Mesh m in mm) acc.Append(m);
                        subdMesh = acc.Faces.Count > 0 ? acc : null;
                    }
                    catch { }
                }
                var s2 = s.Clone(); s2.Thickness = 1.0;
                PatternEmbossResult r2 = PatternEmbossCore.Generate(plate, s2, out rep);
                Mesh shell = r2.Mesh;
                sb.AppendLine("stages: shell faces=" + (shell != null ? shell.Faces.Count : 0) + " solid=" + r2.Solid);

                int layer = PatternEmbossLauncher.EnsureLayer(doc, "emboss-stages", Color.Gray);
                var at = new ObjectAttributes(); at.LayerIndex = layer;
                var bb = BoundingBox.Empty;
                double x = 0;
                if (raw != null) { Mesh mm = raw.DuplicateMesh(); mm.Translate(x, 0, 0); at.Name = "A-raw"; if (doc.Objects.AddMesh(mm, at) != Guid.Empty) bb.Union(mm.GetBoundingBox(true)); x += 70; }
                if (sd != null) { SubD sc = (SubD)sd.Duplicate(); sc.Translate(x, 0, 0); at.Name = "C-subd"; if (doc.Objects.AddSubD(sc, at) != Guid.Empty) { try { bb.Union(sc.GetBoundingBox(true)); } catch { } } x += 70; }
                if (shell != null) { Mesh mm = shell.DuplicateMesh(); mm.Translate(x, 0, 0); at.Name = "D-shell"; if (doc.Objects.AddMesh(mm, at) != Guid.Empty) bb.Union(mm.GetBoundingBox(true)); x += 70; }
                bool ok = doc.WriteFile(path3dm, new Rhino.FileIO.FileWriteOptions());
                sb.AppendLine("stages: saved=" + ok + " -> " + path3dm);

                string pngBase = path3dm.EndsWith(".3dm", StringComparison.OrdinalIgnoreCase) ? path3dm.Substring(0, path3dm.Length - 4) : path3dm;
                try
                {
                    if (raw != null) WriteObj(pngBase + "-A-raw.obj", raw);
                    if (subdMesh != null) WriteObj(pngBase + "-C-subd.obj", subdMesh);
                    if (shell != null) WriteObj(pngBase + "-D-shell.obj", shell);
                    sb.AppendLine("stages: obj exported");
                }
                catch (Exception ex) { sb.AppendLine("obj EXC: " + ex.Message); }
                try
                {
                    var view = doc.Views.ActiveView;
                    if (view == null) { sb.AppendLine("capture: 无活动视图"); return; }
                    view.ActiveViewport.SetProjection(Rhino.Display.DefinedViewportProjection.Top, "Top", false);
                    view.ActiveViewport.ZoomBoundingBox(bb);
                    CaptureTo(view, pngBase + "-top-shaded.png", "Shaded", sb);
                    CaptureTo(view, pngBase + "-top-wire.png", "Wireframe", sb);
                    var c = bb.Center; double d = Math.Max(10.0, bb.Diagonal.Length * 0.9);
                    view.ActiveViewport.SetProjection(Rhino.Display.DefinedViewportProjection.Perspective, "Persp", false);
                    view.ActiveViewport.SetCameraTarget(c, false);
                    view.ActiveViewport.SetCameraLocation(new Point3d(c.X + d * 0.55, c.Y - d * 0.75, c.Z + d * 0.55), false);
                    view.ActiveViewport.ZoomBoundingBox(bb);
                    CaptureTo(view, pngBase + "-iso-shaded.png", "Shaded", sb);
                }
                catch (Exception ex) { sb.AppendLine("capture EXC: " + ex.Message); }
            }
            catch (Exception ex) { sb.AppendLine("stages EXC: " + ex.Message); }
        }

        /// <summary>极简 OBJ 导出（顶点 + 三角/四边形面；只为本机诊断用）</summary>
        static void WriteObj(string path, Mesh m)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb2 = new System.Text.StringBuilder(m.Vertices.Count * 24 + m.Faces.Count * 16);
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3d p = m.Vertices.Point3dAt(i);
                sb2.Append("v ").Append(p.X.ToString("R", ci)).Append(' ').Append(p.Y.ToString("R", ci)).Append(' ').Append(p.Z.ToString("R", ci)).Append('\n');
            }
            for (int i = 0; i < m.Faces.Count; i++)
            {
                MeshFace f = m.Faces[i];
                if (f.IsQuad) sb2.Append("f ").Append(f.A + 1).Append(' ').Append(f.B + 1).Append(' ').Append(f.C + 1).Append(' ').Append(f.D + 1).Append('\n');
                else sb2.Append("f ").Append(f.A + 1).Append(' ').Append(f.B + 1).Append(' ').Append(f.C + 1).Append('\n');
            }
            File.WriteAllText(path, sb2.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>按英文名找显示模式（FindByName 在中文本地化环境查不到，须遍历 EnglishName）</summary>
        static Rhino.Display.DisplayModeDescription FindDisplayMode(string english)
        {
            try
            {
                var all = Rhino.Display.DisplayModeDescription.GetDisplayModes();
                if (all != null)
                {
                    foreach (var m in all)
                        if (string.Equals(m.EnglishName, english, StringComparison.OrdinalIgnoreCase)) return m;
                    foreach (var m in all)
                        if (!string.IsNullOrEmpty(m.EnglishName) && m.EnglishName.IndexOf(english, StringComparison.OrdinalIgnoreCase) >= 0) return m;
                }
            }
            catch { }
            return null;
        }

        static void CaptureTo(Rhino.Display.RhinoView view, string png, string mode, System.Text.StringBuilder sb)
        {
            try
            {
                var dm = FindDisplayMode(mode);
                if (dm != null) { try { view.ActiveViewport.DisplayMode = dm; } catch { } }
                view.Redraw();
                System.Drawing.Bitmap bmp = view.CaptureToBitmap(new System.Drawing.Size(1280, 900));
                if (bmp != null)
                {
                    bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    sb.AppendLine("capture: " + png + " (mode=" + mode + (dm == null ? ", 未找到模式!" : "") + ")");
                }
            }
            catch (Exception ex) { sb.AppendLine("capture " + mode + " EXC: " + ex.Message); }
        }

        /// <summary>QuadRemesh 诊断矩阵：不同网格 × 不同参数组合，记录 结果/异常</summary>
        static void QrDiag(System.Text.StringBuilder sb)
        {
            Mesh relief = null, sphere = null;
            try
            {
                // 规则波浪网格（无极点）：图案化网格上 QR 会长时间不返回（已知），诊断只对安全网格有意义
                var grid = new Mesh();
                int nu = 81, nv = 61; double st = 0.5;
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                        grid.Vertices.Add(i * st, j * st, 0.5 * Math.Sin(i * st * 0.25) * Math.Cos(j * st * 0.3));
                for (int i = 0; i < nu - 1; i++)
                    for (int j = 0; j < nv - 1; j++)
                    {
                        int a = i * nv + j, b = (i + 1) * nv + j, c = (i + 1) * nv + j + 1, d = i * nv + j + 1;
                        grid.Faces.AddFace(a, b, c, d);
                    }
                grid.Faces.ConvertQuadsToTriangles();
                try { grid.FaceNormals.ComputeFaceNormals(); } catch { }
                relief = grid;
                Mesh sph = new Mesh();
                try
                {
                    Brep sb2 = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 30.0)));
                    Mesh[] ms = Mesh.CreateFromBrep(sb2);
                    if (ms != null) foreach (Mesh mm in ms) sph.Append(mm);
                }
                catch { }
                sphere = sph.Faces.Count > 0 ? sph : null;
            }
            catch (Exception ex) { sb.AppendLine("准备网格 EXC: " + ex.Message); }
            sb.AppendLine("relief faces=" + (relief != null ? relief.Faces.Count : 0) + " ; sphere faces=" + (sphere != null ? sphere.Faces.Count : 0));
            TryQr(sb, "A relief + count500/adaptive=false", relief, p => { p.TargetQuadCount = 500; p.AdaptiveQuadCount = false; });
            TryQr(sb, "B relief + count500/adaptive=true ", relief, p => { p.TargetQuadCount = 500; p.AdaptiveQuadCount = true; });
            TryQr(sb, "C relief + 默认参数          ", relief, p => { });
            TryQr(sb, "D relief + edgeLen0.5        ", relief, p => { PatternEmbossCore.SetTargetEdgeLength(p, 0.5); });
            TryQr(sb, "E sphere + count500          ", sphere, p => { p.TargetQuadCount = 500; });
            TryQr(sb, "F sphere + 默认参数          ", sphere, p => { });

            // G：同尺寸平板（无纹理）
            Mesh flat = null;
            try
            {
                var pl = new PolylineCurve(new Point3d[] { new Point3d(0, 0, 0), new Point3d(60, 0, 0), new Point3d(60, 40, 0), new Point3d(0, 40, 0), new Point3d(0, 0, 0) });
                Brep[] bs = Brep.CreatePlanarBreps(new Curve[] { pl }, 1e-6);
                if (bs != null && bs.Length > 0)
                {
                    flat = new Mesh();
                    Mesh[] fm = Mesh.CreateFromBrep(bs[0]);
                    if (fm != null) foreach (Mesh f in fm) flat.Append(f);
                    if (flat.Faces.Count == 0) flat = null;
                }
            }
            catch { }
            TryQr(sb, "G 平板(无纹理) + count500    ", flat, p => { p.TargetQuadCount = 500; });

            // I：网格修复（合并顶点 + 统一法向）
            Mesh repaired = null;
            try
            {
                repaired = relief.DuplicateMesh();
                repaired.Vertices.CombineIdentical(true, true);
                repaired.Vertices.CullUnused();
                repaired.UnifyNormals();
                repaired.Normals.ComputeNormals();
            }
            catch { }
            TryQr(sb, "I 浮雕修复后 + count500      ", repaired, p => { p.TargetQuadCount = 500; });

            // J：网格诊断（裸边 / 封闭 / 有效性）
            if (relief != null)
            {
                int naked = -1;
                try { Polyline[] ne = relief.GetNakedEdges(); naked = ne != null ? ne.Length : -1; } catch { }
                sb.AppendLine("J grid nakedEdges=" + naked + " closed=" + relief.IsClosed + " valid=" + relief.IsValid);
            }

            // L/M：手造 200×150 平面网格（L 全四边形 / M 全三角），验证 QR 对输入面型的偏好
            Mesh gridQ = null, gridT = null;
            try
            {
                int nu = 201, nv = 151; double st2 = 0.4;
                gridQ = new Mesh(); gridT = new Mesh();
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        gridQ.Vertices.Add(i * st2, j * st2, 0);
                        gridT.Vertices.Add(i * st2, j * st2, 0);
                    }
                for (int i = 0; i < nu - 1; i++)
                    for (int j = 0; j < nv - 1; j++)
                    {
                        int a = i * nv + j, b = (i + 1) * nv + j, c = (i + 1) * nv + j + 1, d = i * nv + j + 1;
                        gridQ.Faces.AddFace(a, b, c, d);
                        gridT.Faces.AddFace(a, b, c);
                        gridT.Faces.AddFace(a, c, d);
                    }
            }
            catch { }
            TryQr(sb, "L 手造平面网格(四边面) count500", gridQ, p => { p.TargetQuadCount = 500; });
            TryQr(sb, "M 手造平面网格(三角面) count500", gridT, p => { p.TargetQuadCount = 500; });

            // N/O：仅补顶点法向（验证「法向未计算 → QR 静默失败」假设）
            Mesh nrmOnly = null;
            try { nrmOnly = relief.DuplicateMesh(); nrmOnly.Normals.ComputeNormals(); } catch { }
            TryQr(sb, "N 浮雕+仅顶点法向 count500   ", nrmOnly, p => { p.TargetQuadCount = 500; });
            Mesh gridN = null;
            try { gridN = gridQ.DuplicateMesh(); gridN.Normals.ComputeNormals(); } catch { }
            TryQr(sb, "O 手造网格+仅法向 count500   ", gridN, p => { p.TargetQuadCount = 500; });
        }

        static void TryQr(System.Text.StringBuilder sb, string what, Mesh m, Action<QuadRemeshParameters> cfg)
        {
            if (m == null || m.Faces.Count == 0) { sb.AppendLine(what + " → 无输入网格"); return; }
            try
            {
                var p = new QuadRemeshParameters();
                cfg(p);
                Mesh q = m.QuadRemesh(p);
                sb.AppendLine(what + " → " + (q == null ? "null 结果" : (q.IsValid ? "OK faces=" + q.Faces.Count : "无效 faces=" + q.Faces.Count)));
            }
            catch (Exception ex) { sb.AppendLine(what + " → EXC " + ex.GetType().Name + ": " + ex.Message); }
        }
    }

    internal static class ProbeFlags
    {
        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\PatternEmbossProbe.txt");
            }
        }
    }
}
