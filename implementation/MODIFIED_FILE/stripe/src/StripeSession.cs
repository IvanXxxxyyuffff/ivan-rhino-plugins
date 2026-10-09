using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace StripeOnSurface
{
    /// <summary>
    /// 一次「表面条纹」会话：浮动面板 + 实时预览 + 文档事件刷新（不阻塞 Rhino）。
    ///
    /// 以前命令里用 while(...){ DoEvents(); Sleep(4); } 把命令上下文占住，
    /// 导致面板上的「选择物件」拾取命令根本跑不起来。现在命令打开面板后立刻返回，
    /// 写图层改到面板关闭（FormClosed）时做 —— 与泰森多边形纹插件的会话结构一致。
    /// </summary>
    internal class StripeSession
    {
        static readonly List<StripeSession> _live = new List<StripeSession>();

        readonly RhinoDoc _doc;
        Guid _targetId;                    // 可变：面板上「选择物件」可以换目标
        readonly StripePanel _panel;
        readonly StripePreviewConduit _conduit = new StripePreviewConduit();
        readonly Timer _timer = new Timer();
        readonly Timer _recheck = new Timer();     // 删除事件延迟复查（移动/替换常是「先删旧、再加新」）
        Guid _recheckId = Guid.Empty;
        List<StripeFaceResult> _results = new List<StripeFaceResult>();
        bool _closed;

        public StripeSession(RhinoDoc doc, Guid targetId, StripeSettings settings, string desc)
        {
            _doc = doc;
            _targetId = targetId;
            _panel = new StripePanel(settings, desc);
        }

        /// <summary>当前打开着的面板（拾取命令用它把新物件交给会话）</summary>
        internal static StripeSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        /// <summary>换目标曲面：更新头部、清掉预览再重算</summary>
        public void SetTarget(RhinoDoc doc, Guid id, string desc)
        {
            if (_closed) return;
            _targetId = id;
            _panel.SetTarget(desc);
            _panel.SetTargetState(id != Guid.Empty);
            _results = new List<StripeFaceResult>();
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

        public void Start()
        {
            // 同一时间只保留一个面板，避免预览叠加
            foreach (StripeSession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 90;                       // 原来的防抖节奏，原样保留
            _timer.Tick += delegate { _timer.Stop(); Rebuild(); };
            _panel.ValueChanged += delegate
            {
                if (!_panel.LivePreview) return;
                _timer.Stop(); _timer.Start();
            };
            _panel.FormClosed += delegate { OnClosed(); };

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

        /// <summary>目标曲面被移动/编辑时实时刷新（几何从文档现取，不缓存旧 Brep）</summary>
        void OnReplaceObject(object sender, RhinoReplaceObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (e.Document != null && e.Document.RuntimeSerialNumber != _doc.RuntimeSerialNumber) return;
                if (e.ObjectId == _targetId) Rebuild();
            }
            catch { }
        }

        void OnDeleteObject(object sender, RhinoObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (e.ObjectId == _targetId) { _recheckId = e.ObjectId; _recheck.Stop(); _recheck.Start(); }
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
                _conduit.Curves = new List<Curve>();
                _panel.SetTargetState(false);
                _panel.SetInfo("目标曲面已被删除，请点「选择物件」重选。");
                try { _doc.Views.Redraw(); } catch { }
            }
            catch { }
            finally { _recheckId = Guid.Empty; }
        }

        void Rebuild()
        {
            if (_closed) return;
            try
            {
                RhinoObject obj = _targetId == Guid.Empty ? null : _doc.Objects.FindId(_targetId);
                if (obj == null)
                {
                    _results = new List<StripeFaceResult>();
                    _conduit.Curves = new List<Curve>();
                    _panel.SetTargetState(false);
                    _panel.SetInfo(_targetId == Guid.Empty
                        ? StripeCore.EmptyTargetHint
                        : "目标曲面已被删除，请点「选择物件」重选。");
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }

                Brep brep = obj.DuplicateGeometry() as Brep;
                if (brep == null) brep = obj.Geometry as Brep;
                if (brep == null)
                {
                    _panel.SetTargetState(false);
                    _panel.SetInfo("目标物件已不是曲面。");
                    return;
                }

                StripeSettings cur = _panel.Settings;
                string report;
                _results = StripePattern.Generate(brep, cur, out report);

                int n = 0;
                double minEdge = double.MaxValue;
                foreach (StripeFaceResult r in _results)
                {
                    n += r.Stripes.Count;
                    if (r.MinEdgeDistance < minEdge) minEdge = r.MinEdgeDistance;
                }

                if (_panel.LivePreview)
                {
                    var curves = new List<Curve>();
                    foreach (StripeFaceResult r in _results) curves.AddRange(r.Stripes);
                    _conduit.Curves = curves;
                    _doc.Views.Redraw();
                }

                string edgeTxt = minEdge == double.MaxValue ? "-" : minEdge.ToString("0.###");
                string modeTxt = cur.ConformToBoundary
                    ? "贴合边界"
                    : (cur.RoundedEnds ? "端部圆角" : "端部平齐");
                if (cur.CornerRadius > 1e-9 && !(cur.RoundedEnds && !cur.ConformToBoundary))
                    modeTxt += string.Format("，硬边倒圆 R{0:0.##}", cur.CornerRadius);
                _panel.SetInfo(string.Format("{0} 条条纹 / {1} 个面 · {2}；目标边距 {3:0.###}，实测最小边距 {4}",
                    n, _results.Count, modeTxt, cur.Margin, edgeTxt));
            }
            catch (Exception ex)
            {
                _panel.SetInfo("计算失败：" + ex.Message);
            }
        }

        public void ClosePanel()
        {
            try { _panel.Close(); } catch { }
        }

        /// <summary>面板关闭：提交则按当前参数重算并写图层（原来 while 循环之后那一段）</summary>
        void OnClosed()
        {
            if (_closed) return;

            if (_panel.Committed)
            {
                try
                {
                    Rebuild();

                    int curveCount = 0;
                    uint undo = _doc.BeginUndoRecord("生成表面条纹");
                    try
                    {
                        int layerIndex = StripeCore.EnsureLayer(_doc, "表面条纹");
                        var attrs = new ObjectAttributes();
                        attrs.LayerIndex = layerIndex;

                        foreach (StripeFaceResult r in _results)
                        {
                            foreach (Curve c in r.Stripes)
                            {
                                _doc.Objects.AddCurve(c, attrs);
                                curveCount++;
                            }
                        }
                        RhinoApp.WriteLine("已生成 {0} 条条纹曲线（图层：表面条纹）。", curveCount);
                    }
                    finally
                    {
                        _doc.EndUndoRecord(undo);
                    }
                    try { _doc.Views.Redraw(); } catch { }
                }
                catch (Exception ex) { RhinoApp.WriteLine("写入失败：" + ex.Message); }
            }
            else
            {
                RhinoApp.WriteLine("已取消，未生成任何几何。");
            }

            _closed = true;
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            try { _recheck.Stop(); _recheck.Dispose(); } catch { }
            try { _conduit.Enabled = false; } catch { }
            try { RhinoDoc.ReplaceRhinoObject -= OnReplaceObject; } catch { }
            try { RhinoDoc.DeleteRhinoObject -= OnDeleteObject; } catch { }
            _live.Remove(this);
            try { _doc.Views.Redraw(); } catch { }
        }
    }
}
