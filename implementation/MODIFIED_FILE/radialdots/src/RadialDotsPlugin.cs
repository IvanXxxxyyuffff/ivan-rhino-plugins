using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;   // [assembly: Guid] 需要（否则 Guid 解析成 System.Guid）
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.PlugIns;

// Rhino 按「程序集级」[assembly: Guid] 识别插件 ID；只写类级 [Guid] 会被登记成全零 GUID。
#if RH7
[assembly: Guid("45403763-4C11-42E3-989C-430F82FEEB77")]
#else
[assembly: Guid("F54E41C9-847C-4ED5-AE5C-FD905C55A1B6")]
#endif

namespace RadialDotsPattern
{
    /// <summary>插件入口：径向渐变圆点（Rhino 7 / 8 用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("45403763-4C11-42E3-989C-430F82FEEB77")]
#else
    [Guid("F54E41C9-847C-4ED5-AE5C-FD905C55A1B6")]
#endif
    public class RadialDotsPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            RhinoApp.WriteLine("径向渐变圆点插件已加载。命令：RadialDots");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-radialdots-selftest.flag，
        /// 下次启动 Rhino 自动跑几何自检并写报告到同目录 RadialDotsSelfTest.txt，跑完退出。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-radialdots-selftest.flag");
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
                RadialDotsSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, RadialDotsSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("径向渐变圆点自检失败：" + ex.Message); } catch { }
            }
        }
    }

    public class RadialDotsCommand : Command
    {
        public override string EnglishName { get { return "RadialDots"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode) { return RadialDotsCore.Run(doc); }
    }

    internal static class RadialDotsCore
    {
        public static Result Run(RhinoDoc doc)
        {
            var settings = new RadialDotSettings();
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            var session = new RadialDotsSession(doc, settings);
            session.Start();
            RhinoApp.WriteLine("参数面板已打开：拉杆调形态、预览实时更新；点「生成」写入图层。");
            return Result.Success;
        }

        /// <summary>生成平面：取当前视图的工作平面（CPlane），取不到就用世界 XY</summary>
        internal static Plane GenerationPlane(RhinoDoc doc)
        {
            try
            {
                var av = doc.Views.ActiveView;
                if (av != null)
                {
                    Plane pl = av.ActiveViewport.ConstructionPlane();
                    if (pl.IsValid) return pl;
                }
            }
            catch { }
            return Plane.WorldXY;
        }

        internal static string PlaneDesc(Plane pl)
        {
            try
            {
                return string.Format("生成在工作平面 ({0:0.#}, {1:0.#}, {2:0.#})",
                    pl.Origin.X, pl.Origin.Y, pl.Origin.Z);
            }
            catch { return "生成在工作平面"; }
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

    /// <summary>一次「径向渐变圆点」会话：浮动面板 + 实时预览 + 关闭时写图层（不阻塞 Rhino）</summary>
    internal class RadialDotsSession
    {
        static readonly List<RadialDotsSession> _live = new List<RadialDotsSession>();

        readonly RhinoDoc _doc;
        readonly Plane _plane;
        readonly RadialDotsPanel _panel;
        readonly RadialDotsPreviewConduit _conduit = new RadialDotsPreviewConduit();
        readonly Timer _timer = new Timer();
        RadialDotResult _result;
        bool _closed;

        public RadialDotsSession(RhinoDoc doc, RadialDotSettings settings)
        {
            _doc = doc;
            _plane = RadialDotsCore.GenerationPlane(doc);
            _panel = new RadialDotsPanel(settings, RadialDotsCore.PlaneDesc(_plane));
        }

        internal static RadialDotsSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        public void Start()
        {
            foreach (RadialDotsSession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 120;
            _timer.Tick += delegate { _timer.Stop(); Rebuild(); };
            _panel.ValueChanged += delegate
            {
                if (!_panel.LivePreview) return;
                _timer.Stop(); _timer.Start();
            };
            _panel.FormClosed += delegate { OnClosed(); };

            _conduit.Enabled = true;
            _panel.Show();
            Rebuild();
            _panel.Activate();
            _live.Add(this);
        }

        void Rebuild()
        {
            if (_closed) return;
            try
            {
                RadialDotSettings cur = _panel.Settings;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _result = RadialDots.Generate(_plane, cur);
                sw.Stop();

                if (_panel.LivePreview)
                {
                    _conduit.Curves = _result != null ? _result.Curves : new List<Curve>();
                    try { _doc.Views.Redraw(); } catch { }
                }

                if (_result == null) { _panel.SetInfo("计算失败"); return; }
                string merged = _result.MergedGroups > 0
                    ? string.Format("，{0} 组重叠已合并（{1} 个并成一体）", _result.MergedGroups, _result.MergedDots)
                    : "，没有需要合并的重叠";
                string note = string.IsNullOrEmpty(_result.Note) ? "" : "\r\n⚠ " + _result.Note;
                _panel.SetInfo(string.Format("{0} 个图形{1} · 输出 {2} 条曲线 · {3} ms\r\n{4}｜间距 {5:0.##} mm，直径 {6:0.##}~{7:0.##} mm，峰值 {8:0.##}{9}",
                    _result.Dots, cur.Merge ? merged : "（未合并）", _result.Count, sw.ElapsedMilliseconds,
                    RadialDotSettings.LayoutNames[Math.Max(0, Math.Min(RadialDotSettings.LayoutNames.Length - 1, cur.Layout))],
                    cur.Pitch, cur.MinDia, cur.MaxDia, cur.Peak, note));
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

            if (_panel.Committed)
            {
                try
                {
                    Rebuild();
                    if (_result != null && _result.Curves.Count > 0)
                    {
                        int n = 0;
                        uint undo = _doc.BeginUndoRecord("生成径向渐变圆点");
                        try
                        {
                            int layer = RadialDotsCore.EnsureLayer(_doc, "径向渐变圆点", Color.FromArgb(122, 92, 214));
                            var attrs = new ObjectAttributes(); attrs.LayerIndex = layer;
                            foreach (Curve c in _result.Curves)
                            {
                                if (c == null) continue;
                                if (_doc.Objects.AddCurve(c, attrs) != Guid.Empty) n++;
                            }
                        }
                        finally { _doc.EndUndoRecord(undo); }
                        RhinoApp.WriteLine("已生成 {0} 条曲线（{1} 个图形，{2} 组重叠已合并；图层：径向渐变圆点）。",
                            n, _result.Dots, _result.MergedGroups);
                        _doc.Views.Redraw();
                    }
                    else RhinoApp.WriteLine("没有可生成的图形。");
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
}
