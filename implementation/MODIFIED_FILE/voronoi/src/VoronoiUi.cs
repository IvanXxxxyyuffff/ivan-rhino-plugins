using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace VoronoiTexture
{
    /// <summary>
    /// 实时预览：
    ///   线框 → 画胞元边界线；
    ///   网格面 → 半透明着色网格 **+ 网格线框**（DrawMeshWires，快）；勾了「一键平滑」就画平滑后的形状。
    /// </summary>
    public class VoronoiPreviewConduit : DisplayConduit
    {
        public List<Curve> Curves = new List<Curve>();
        /// <summary>胞元中心点（预览里画成点标记 + 小十字）</summary>
        public List<Point3d> Centers = new List<Point3d>();
        /// <summary>中心点十字标记的半长（模型单位，会话按胞元尺寸给）</summary>
        public double MarkerSize = 0.5;
        public Mesh Shaded;                    // 要着色 + 画线框的网格（勾了平滑就是平滑后的网格）
        /// <summary>勾了「一键平滑」时直接画细分曲面（预览 = 输出，用户口径：勾了要看得见细分曲面）</summary>
        public SubD SmoothSubD;
        public int OutputMode = 1;
        public bool ShowMeshWires = true;
        static readonly Color Col = Color.FromArgb(255, 176, 132, 40);
        static readonly Color CenterCol = Color.FromArgb(255, 214, 60, 40);
        static readonly Color WireCol = Color.FromArgb(210, 84, 58, 16);
        static readonly Color FaceCol = Color.FromArgb(150, 232, 204, 140);

        protected override void DrawForeground(DrawEventArgs e)
        {
            if (OutputMode == 1)
            {
                try
                {
                    SubD sd = SmoothSubD;
                    if (sd != null)
                    {
                        // 勾了「一键平滑」：直接画细分曲面（跟输出一致）
                        e.Display.DrawSubDShaded(sd, new DisplayMaterial(FaceCol, 0.35));
                        if (ShowMeshWires) e.Display.DrawSubDWires(sd, WireCol, 1);
                        return;
                    }
                    Mesh m = Shaded;
                    if (m != null && m.Faces.Count > 0)
                    {
                        e.Display.DrawMeshShaded(m, new DisplayMaterial(FaceCol, 0.35));
                        if (ShowMeshWires) e.Display.DrawMeshWires(m, WireCol, 1);
                    }
                }
                catch { }
                return;
            }

            List<Curve> list = Curves;
            if (list == null || list.Count == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                Curve c = list[i];
                if (c == null) continue;
                try { e.Display.DrawCurve(c, Col, 1); } catch { }
            }

            // 中心点：画成醒目的点标记（用户口径：中心点要看得见）
            List<Point3d> cts = Centers;
            if (cts == null || cts.Count == 0) return;
            double mk = MarkerSize > 1e-9 ? MarkerSize : 0.5;
            for (int i = 0; i < cts.Count; i++)
            {
                Point3d p = cts[i];
                try { e.Display.DrawPoint(p, PointStyle.Simple, 7, CenterCol); } catch { }
                // 小十字（屏幕上看不见点标记的显示模式下也一定能看到）
                try
                {
                    e.Display.DrawLine(new Point3d(p.X - mk, p.Y, p.Z), new Point3d(p.X + mk, p.Y, p.Z), CenterCol, 2);
                    e.Display.DrawLine(new Point3d(p.X, p.Y - mk, p.Z), new Point3d(p.X, p.Y + mk, p.Z), CenterCol, 2);
                }
                catch { }
            }
        }
    }

    /// <summary>泰森多边形纹参数面板：边界 + 胞元参数 + 胞元造型 + 输出与边界 + 胞元渐变 + 实时预览</summary>
    public class VoronoiPanel : Form
    {
        public VoronoiSettings Settings { get; private set; }
        public bool Committed { get; private set; }
        public bool LivePreview { get { return _live.Checked; } }
        public event EventHandler ValueChanged;
        public event EventHandler PickRequested;            // 「选择边界」
        public event EventHandler PickGradientRequested;    // 「选择渐变物件」

        readonly InfoBar _info = new InfoBar();
        readonly IvanCheck _live = new IvanCheck();
        readonly Label _target = new Label();
        readonly Label _targetHint = new Label();
        readonly Label _gradHint = new Label();
        readonly Label _outHint = new Label();
        readonly List<CardPanel> _scrollCards = new List<CardPanel>();
        readonly List<int> _scrollCardTops = new List<int>();
        VScrollBar _scrollBar;
        int _scrollBodyTop, _scrollFooterHeight, _scrollContentHeight;
        bool _scrollSync, _scrollFocusHooked;
        HeaderBand _header;
        bool _sync;
        bool _closing;

        const int W = 434;

        FlatButton _pick, _pickGrad;
        IvanCheck _smoothCheck;
        IvanSegmented _outSeg;
        IvanSegmented _gradSeg;
        List<Control> _smoothRows;
        CardPanel _outCard;
        int _smoothBaseY, _smoothBlockHeight;
        bool _smoothShown = true;

        public VoronoiPanel(VoronoiSettings settings, string targetDesc)
        {
            Settings = settings.Clone();

            Text = "泰森多边形纹 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("voronoi", 16); } catch { }

            int y = 0;

            // ---- 边界（跟钻石切面同一个位置：最上面一张卡片，未选红 / 已选绿）
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 116, Title = "边界" };
                AddScrollCard(card);

                _target.SetBounds(12, 26, W - 48, 18);
                _target.ForeColor = Theme.Ink;
                _target.Font = Theme.Body;
                _target.Text = targetDesc;
                card.Controls.Add(_target);

                _pick = new FlatButton { Text = "选择边界", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 104, Height = 28, Left = 12, Top = 50 };
                _pick.Click += (s, e) => { EventHandler h = PickRequested; if (h != null) h(this, EventArgs.Empty); };
                card.Controls.Add(_pick);

                _targetHint.SetBounds(124, 54, W - 160, 20);
                _targetHint.ForeColor = Theme.InkFaint;
                _targetHint.Font = Theme.Small;
                _targetHint.Text = "可多选：平面 / 闭合平面曲线";
                card.Controls.Add(_targetHint);

                var hint2 = new Label();
                hint2.SetBounds(12, 82, W - 48, 26);
                hint2.ForeColor = Theme.InkFaint;
                hint2.Font = Theme.Small;
                hint2.Text = "在平面边界内生成胞元；胞元尺寸 = 种子平均间距（真实 mm）";
                card.Controls.Add(hint2);

                y = card.Bottom + 8;
            }

            // ---- 胞元参数
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "胞元参数" };
                AddScrollCard(card);

                int cy = 26;
                AddRow(card, "胞元尺寸", "mm", 2.0, 60.0, 500.0, 0.5, Settings.CellSize, ref cy, v => Settings.CellSize = v);
                AddRow(card, "凹凸深度", "mm", -20.0, 20.0, 200.0, 0.05, Settings.Depth, ref cy, v => Settings.Depth = v);
                AddRow(card, "过渡宽度", "mm", 0.05, 20.0, 200.0, 0.05, Settings.EdgeWidth, ref cy, v => Settings.EdgeWidth = v);
                AddRow(card, "凸起形状", "", 0.2, 3.0, 10.0, 0.05, Settings.Shape, ref cy, v => Settings.Shape = v);
                AddRow(card, "规整度", "", 0, 20.0, 30.0, 1.0, Settings.Relax, ref cy, v => Settings.Relax = (int)Math.Round(v));
                AddRow(card, "随机种子", "", 0, 200.0, 9999.0, 1.0, Settings.Seed, ref cy, v => Settings.Seed = (int)Math.Round(v));
                AddRow(card, "壁厚", "mm", 0.0, 10.0, 100.0, 0.05, Settings.WallThickness, ref cy, v => Settings.WallThickness = v);
                AddRow(card, "边界收平", "mm", 0.0, 20.0, 200.0, 0.05, Settings.EdgeFade, ref cy, v => Settings.EdgeFade = v);
                AddRow(card, "采样间距", "mm", 0.05, 3.0, 20.0, 0.05, Settings.Step, ref cy, v => Settings.Step = v);

                card.Height = cy + 6;
                y = card.Bottom + 8;
            }

            // ---- 胞元造型：平顶（老行为）/ 穹顶（鹅卵石）—— 只换高度曲线，其余参数照常生效
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "胞元造型" };
                AddScrollCard(card);

                int sy = 26;
                card.Controls.Add(new Label
                {
                    Text = "胞元形状", Left = 12, Top = sy + 6, Width = 62, Height = 18,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.InkSoft, Font = Theme.Body
                });
                var segShape = new IvanSegmented();
                segShape.SetItems("平顶", "穹顶");
                segShape.SelectedIndex = Settings.CellShape == 1 ? 1 : 0;
                segShape.SetBounds(78, sy, 186, 28);
                segShape.SelectedChanged += (s, e) =>
                {
                    Settings.CellShape = segShape.SelectedIndex == 1 ? 1 : 0;
                    SyncShape();
                    Raise();
                };
                card.Controls.Add(segShape);
                sy += 32;

                _shapeHint.SetBounds(12, sy, W - 48, 18);
                _shapeHint.ForeColor = Theme.InkFaint;
                _shapeHint.Font = Theme.Small;
                card.Controls.Add(_shapeHint);
                sy += 22;

                // 穹顶圆度：越小越饱满（1 = 线性到边）；平顶模式下这一行禁用（SyncShape 同步）
                AddRow(card, "穹顶圆度", "", 0.3, 3.0, 3.0, 0.05, Settings.DomePower, ref sy,
                       v => Settings.DomePower = v, out _domeLabel, out _domeBar, out _domeNum);

                card.Height = sy + 6;
                y = card.Bottom + 8;
                SyncShape();
            }

            // ---- 输出与边界（输出二选一：线框 / 网格面；一键平滑只在网格面模式出现）
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 176, Title = "输出与边界" };
                AddScrollCard(card);

                _outSeg = new IvanSegmented();
                _outSeg.SetItems("线框", "网格面");
                _outSeg.SelectedIndex = Settings.OutputMode == 0 ? 0 : 1;
                _outSeg.SetBounds(12, 26, W - 48, 28);
                _outSeg.SelectedChanged += (s, e) =>
                {
                    Settings.OutputMode = _outSeg.SelectedIndex == 0 ? 0 : 1;
                    ApplySmoothState();
                    Raise();
                };
                card.Controls.Add(_outSeg);

                _smoothCheck = new IvanCheck { Text = "一键平滑（细分网格 → 细分曲面 SubD）", Checked = Settings.Smooth };
                _smoothCheck.SetBounds(12, 62, W - 48, 22);
                _smoothCheck.CheckedChanged += (s, e) =>
                {
                    Settings.Smooth = _smoothCheck.Checked;
                    ApplySmoothState();
                    Raise();
                };
                card.Controls.Add(_smoothCheck);

                int fy = 88;
                _smoothRows = new List<Control>();
                _smoothRows.AddRange(AddRowEx(card, "平滑度", "%", 80.0, 100.0, 100.0, 1.0, Settings.SmoothAdaptive, ref fy, v => Settings.SmoothAdaptive = v));
                _smoothRows.AddRange(AddRowEx(card, "细分程度", "", 100.0, 20000.0, 500000.0, 100.0, Settings.SmoothQuadCount, ref fy, v => Settings.SmoothQuadCount = (int)Math.Round(v)));
                _smoothBaseY = 62;                       // 平滑块起始 y
                _smoothBlockHeight = fy - _smoothBaseY;  // 平滑块总高

                _outHint.SetBounds(12, fy, W - 48, 18);
                _outHint.ForeColor = Theme.InkFaint;
                _outHint.Font = Theme.Small;
                _outHint.Text = OutHint();
                card.Controls.Add(_outHint);

                card.Height = fy + 24;
                _outCard = card;
                y = card.Bottom + 8;
            }

            // ---- 胞元渐变：从参考物件向外，胞元疏密变化（只控疏密，不控深度）
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 148, Title = "胞元渐变" };
                AddScrollCard(card);

                _pickGrad = new FlatButton { Text = "选择渐变物件", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 140, Height = 28, Left = 12, Top = 26 };
                _pickGrad.Click += (s, e) => { EventHandler h = PickGradientRequested; if (h != null) h(this, EventArgs.Empty); };
                card.Controls.Add(_pickGrad);

                var clrG = new FlatButton { Text = "取消渐变", Style = BtnStyle.Ghost, Width = 92, Height = 28, Left = 160, Top = 26 };
                clrG.Click += (s, e) =>
                {
                    Settings.GradientOn = false;
                    Settings.GradientPoint = Point3d.Unset;
                    _gradHint.Text = GradHint();
                    Raise();
                };
                card.Controls.Add(clrG);

                int gy = 60;
                card.Controls.Add(new Label
                {
                    Text = "靠近物件", Left = 12, Top = gy + 6, Width = 62, Height = 18,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.InkSoft, Font = Theme.Body
                });
                _gradSeg = new IvanSegmented();
                _gradSeg.SetItems("密集", "稀疏");
                _gradSeg.SelectedIndex = Settings.GradientNear == 1 ? 1 : 0;
                _gradSeg.SetBounds(78, gy, 186, 28);
                _gradSeg.SelectedChanged += (s, e) =>
                {
                    Settings.GradientNear = _gradSeg.SelectedIndex == 1 ? 1 : 0;
                    _gradHint.Text = GradHint();
                    Raise();
                };
                card.Controls.Add(_gradSeg);
                gy += 32;

                AddRow(card, "渐变幅度", "", 0.0, 2.0, 2.0, 0.05, Settings.GradientAmount, ref gy, v => Settings.GradientAmount = v);

                _gradHint.SetBounds(12, gy, W - 48, 18);
                _gradHint.ForeColor = Theme.InkFaint;
                _gradHint.Font = Theme.Small;
                _gradHint.Text = GradHint();
                card.Controls.Add(_gradHint);

                card.Height = gy + 24;
                y = card.Bottom + 8;
            }

            // ---- 预览选项
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 58, Title = "预览选项" };
                AddScrollCard(card);
                _live.Text = "实时预览";
                _live.Checked = true;
                _live.SetBounds(12, 30, 150, 22);
                _live.CheckedChanged += (s, e) => Raise();
                card.Controls.Add(_live);
                y = card.Bottom + 8;
            }

            _info.Text = "就绪";
            Controls.Add(_info);

            var ok = new FlatButton { Text = "生成", Style = BtnStyle.Primary, Width = 96, Height = 32, Left = W - 12 - 96 - 104 };
            ok.Click += (s, e) => { Committed = true; CloseAnimated(); };
            Controls.Add(ok);

            var cancel = new FlatButton { Text = "取消", Style = BtnStyle.Ghost, Width = 96, Height = 32, Left = W - 12 - 96 };
            cancel.Click += (s, e) => CloseAnimated();
            Controls.Add(cancel);

            const int bodyTop = 56, footerHeight = 112;
            int clientHeight = FitClientHeight(bodyTop + y + footerHeight);
            _scrollBodyTop = bodyTop;
            _scrollFooterHeight = footerHeight;
            _scrollContentHeight = y;
            _scrollBar = new VScrollBar { SmallChange = 28, LargeChange = 1, Minimum = 0 };
            _scrollBar.ValueChanged += (s, e) => { if (!_scrollSync) LayoutScrollCards(); };
            Controls.Add(_scrollBar);
            _info.SetBounds(12, clientHeight - footerHeight, W - 24, 60);
            ok.Top = clientHeight - 12 - 32;
            cancel.Top = clientHeight - 12 - 32;
            ClientSize = new Size(W, clientHeight);
            LayoutScrollCards();

            _header = new HeaderBand("泰森多边形纹", targetDesc, "voronoi");
            _header.SetBounds(0, 0, W, 48);
            Controls.Add(_header);
            _scrollBar.BringToFront();
            _info.BringToFront();
            ok.BringToFront();
            cancel.BringToFront();
            _header.BringToFront();

            ApplySmoothState();
            AcceptButton = ok;
            CancelButton = cancel;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) CloseAnimated(); };
            ApplyLengthDisplayFormat();
            PlaceNearRhino();
        }

        Label _domeLabel;
        IvanSlider _domeBar;
        NumericUpDown _domeNum;
        readonly Label _shapeHint = new Label();

        string OutHint()
        {
            if (Settings.OutputMode == 0) return "只输出胞元线框（曲线，图层：泰森多边形纹-线框）；不带网格面";
            return Settings.Smooth
                ? "网格 = 线框三角面各一张网格片 + 片内细分 → 转成细分曲面（SubD，图层：泰森多边形纹-平滑）；不带线框"
                : "网格 = 线框的每个三角面各一张网格片（未细分）→ 图层：泰森多边形纹-面；不带线框";
        }

        // Pure display formatting: stored values, limits, increments and callbacks are untouched.
        void ApplyLengthDisplayFormat()
        {
            foreach (Control card in Controls)
                foreach (Control labelControl in card.Controls)
                {
                    Label lengthLabel = labelControl as Label;
                    if (lengthLabel == null || !lengthLabel.Text.EndsWith("mm", StringComparison.Ordinal)) continue;
                    foreach (Control input in card.Controls)
                    {
                        NumericUpDown number = input as NumericUpDown;
                        if (number != null && number.Top + 6 == lengthLabel.Top) number.DecimalPlaces = 2;
                    }
                }
        }

        static int FitClientHeight(int desiredHeight)
        {
            try
            {
                IntPtr h = Rhino.RhinoApp.MainWindowHandle();
                Screen scr = h != IntPtr.Zero ? Screen.FromHandle(h) : Screen.PrimaryScreen;
                return Math.Min(desiredHeight, Math.Max(320, scr.WorkingArea.Height - 32));
            }
            catch { return desiredHeight; }
        }

        void AddScrollCard(CardPanel card)
        {
            Controls.Add(card);
            _scrollCards.Add(card);
            _scrollCardTops.Add(card.Top);
        }

        void LayoutScrollCards()
        {
            if (_scrollBar == null || IsDisposed) return;
            int viewportHeight = Math.Max(1, ClientSize.Height - _scrollBodyTop - _scrollFooterHeight);
            int maxOffset = Math.Max(0, _scrollContentHeight - viewportHeight);
            _scrollSync = true;
            try
            {
                _scrollBar.SetBounds(W - 14, _scrollBodyTop, 12, viewportHeight);
                _scrollBar.Minimum = 0;
                _scrollBar.SmallChange = Math.Min(28, Math.Max(1, viewportHeight));
                _scrollBar.LargeChange = viewportHeight;
                _scrollBar.Maximum = Math.Max(0, _scrollContentHeight - 1);
                if (_scrollBar.Value > maxOffset) _scrollBar.Value = maxOffset;
                _scrollBar.Visible = maxOffset > 0;
                _scrollBar.Enabled = maxOffset > 0;
            }
            finally { _scrollSync = false; }

            if (!_scrollFocusHooked)
            {
                foreach (CardPanel card in _scrollCards) HookScrollFocus(card, card);
                _scrollFocusHooked = true;
            }

            int offset = _scrollBar.Value;
            int viewportBottom = _scrollBodyTop + viewportHeight;
            for (int i = 0; i < _scrollCards.Count; i++)
            {
                CardPanel card = _scrollCards[i];
                card.Top = _scrollBodyTop + _scrollCardTops[i] - offset;
                int clipTop = Math.Max(0, _scrollBodyTop - card.Top);
                int clipBottom = Math.Min(card.Height, viewportBottom - card.Top);
                int clipHeight = clipBottom - clipTop;
                if (clipHeight <= 0)
                {
                    Region previous = card.Region;
                    var emptyRegion = new Region();
                    emptyRegion.MakeEmpty();
                    card.Region = emptyRegion;
                    if (previous != null) previous.Dispose();
                    // Keep the direct child focusable for Tab navigation; the empty
                    // region hides it until the focus handler scrolls it into view.
                    card.Visible = true;
                    continue;
                }
                Region oldRegion = card.Region;
                card.Region = new Region(new Rectangle(0, clipTop, card.Width, clipHeight));
                if (oldRegion != null) oldRegion.Dispose();
                card.Visible = true;
            }
        }

        void HookScrollFocus(CardPanel card, Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                child.Enter += (s, e) => EnsureScrollControlVisible(card, s as Control);
                HookScrollFocus(card, child);
            }
        }

        void EnsureScrollControlVisible(CardPanel card, Control target)
        {
            if (_scrollBar == null || _scrollSync || target == null || card == null || !card.Visible) return;
            try
            {
                int viewportHeight = Math.Max(1, ClientSize.Height - _scrollBodyTop - _scrollFooterHeight);
                int viewportBottom = _scrollBodyTop + viewportHeight;
                System.Drawing.Point screenTop = target.PointToScreen(System.Drawing.Point.Empty);
                int targetTop = PointToClient(screenTop).Y;
                int targetBottom = targetTop + Math.Max(1, target.Height);
                int delta = targetTop < _scrollBodyTop ? targetTop - _scrollBodyTop :
                            (targetBottom > viewportBottom ? targetBottom - viewportBottom : 0);
                if (delta == 0) return;
                int maxOffset = Math.Max(0, _scrollContentHeight - viewportHeight);
                int value = Math.Max(0, Math.Min(maxOffset, _scrollBar.Value + delta));
                if (_scrollBar.Value != value) _scrollBar.Value = value;
            }
            catch { }
        }

        /// <summary>头部渐变带（自绘，不吃点击）</summary>
        class HeaderBand : Control
        {
            readonly string _title, _kind;
            string _sub;
            public HeaderBand(string title, string sub, string kind)
            {
                _title = title; _sub = sub; _kind = kind;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Canvas;
            }
            public void SetSub(string sub) { _sub = sub; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                Theme.DrawHeader(e.Graphics, new Rectangle(0, 0, Width, Height), _title, _sub, _kind);
            }
        }

        void PlaceNearRhino()
        {
            try
            {
                IntPtr h = Rhino.RhinoApp.MainWindowHandle();
                Screen scr = h != IntPtr.Zero ? Screen.FromHandle(h) : Screen.PrimaryScreen;
                Rectangle wa = scr.WorkingArea;
                Location = new System.Drawing.Point(Math.Max(wa.Left, wa.Right - Width - 420),
                                     wa.Top + Math.Max(60, (wa.Height - Height) / 6));
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }
        }

        // 面板进入：淡入 + 上移 8px（finesse：进入 300ms / ease-out，位移 8–12px，不从 scale(0) 开始）
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                if (!Motion.Enabled) return;
                int y0 = Top;
                Opacity = 0;
                Motion.To(this, "enter", 0, 1, Motion.Slow, Motion.EaseOut, v =>
                {
                    Opacity = v;
                    Top = (int)Math.Round(y0 + 8 * (1 - v));
                }, () => { Opacity = 1; Top = y0; });
            }
            catch { try { Opacity = 1; } catch { } }
        }

        /// <summary>退出永远比进入快一档（150ms / ease-in），关掉动画时直接关</summary>
        void CloseAnimated()
        {
            if (_closing) return;
            _closing = true;
            if (!Motion.Enabled) { Close(); return; }
            try { Motion.To(this, "exit", 1, 0, Motion.Exit, Motion.EaseIn, v => Opacity = v, () => Close()); }
            catch { Close(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                CloseAnimated();
                return;
            }
            base.OnFormClosing(e);
        }

        void Raise()
        {
            EventHandler h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        void AddRow(CardPanel card, string name, string unit, double min, double sliderMax, double hardMax,
                    double step, double value, ref int y, Action<double> setter)
        {
            Label label;
            IvanSlider bar;
            NumericUpDown num;
            AddRow(card, name, unit, min, sliderMax, hardMax, step, value, ref y, setter, out label, out bar, out num);
        }

        /// <summary>加一行并把这行的三个控件（标签/滑杆/数字框）返回，便于整行显示/隐藏</summary>
        List<Control> AddRowEx(CardPanel card, string name, string unit, double min, double sliderMax, double hardMax,
                    double step, double value, ref int y, Action<double> setter)
        {
            int before = card.Controls.Count;
            Label label;
            IvanSlider bar;
            NumericUpDown num;
            AddRow(card, name, unit, min, sliderMax, hardMax, step, value, ref y, setter, out label, out bar, out num);
            var row = new List<Control>();
            for (int i = before; i < card.Controls.Count; i++) row.Add(card.Controls[i]);
            return row;
        }

        /// <summary>同上，另外把这一行里的控件交回去（新参数需要和别的控件联动 Enabled 时用）</summary>
        void AddRow(CardPanel card, string name, string unit, double min, double sliderMax, double hardMax,
                    double step, double value, ref int y, Action<double> setter,
                    out Label label, out IvanSlider bar, out NumericUpDown num)
        {
            int decimals = step >= 1.0 ? 0 : (step >= 0.1 ? 1 : 2);

            // out 参数不能在下面的 lambda 里用（CS1628），所以先建局部变量，最后再交出去
            var lab = new Label
            {
                Text = name + unit,
                Left = 10, Top = y + 6, Width = 84, Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.InkSoft,
                Font = Theme.Body
            };
            card.Controls.Add(lab);

            var barL = new IvanSlider
            {
                Minimum = min,
                Maximum = sliderMax,
                Step = step,
                Left = 96, Top = y, Width = 186, Height = 26,
                Value = ClampD(value, min, sliderMax)
            };
            card.Controls.Add(barL);

            var numL = new NumericUpDown
            {
                DecimalPlaces = decimals,
                Increment = (decimal)step,
                Minimum = (decimal)min,
                Maximum = (decimal)hardMax,
                Left = 288, Top = y + 2, Width = 86, Height = 24,
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Body,
                BackColor = Theme.Surface,
                Value = (decimal)Math.Round(ClampD(value, min, hardMax), decimals)
            };
            card.Controls.Add(numL);

            barL.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                _sync = true;
                try
                {
                    double v = barL.Value;
                    numL.Value = (decimal)Math.Round(ClampD(v, min, hardMax), decimals);
                    setter(v);
                }
                finally { _sync = false; }
                Raise();
            };

            numL.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                _sync = true;
                try
                {
                    double v = (double)numL.Value;
                    barL.Value = ClampD(v, barL.Minimum, barL.Maximum);
                    setter(v);
                }
                finally { _sync = false; }
                Raise();
            };

            label = lab;
            bar = barL;
            num = numL;
            y += 32;
        }

        static double ClampD(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>
        /// 平滑块的可见/可用状态（用户口径）：
        ///   线框 → 一键平滑整块**隐藏**（看不到）；
        ///   网格面 + 未勾选 → 平滑块可见但参数**灰掉**；
        ///   网格面 + 已勾选 → 参数可调。卡片高度跟着变，后面的卡片自动上移/下移。
        /// </summary>
        void ApplySmoothState()
        {
            try
            {
                bool faceMode = Settings.OutputMode == 1;
                if (_smoothCheck != null) _smoothCheck.Visible = faceMode;
                bool paramOn = faceMode && _smoothCheck != null && _smoothCheck.Checked;
                if (_smoothRows != null)
                    for (int i = 0; i < _smoothRows.Count; i++)
                    {
                        Control c = _smoothRows[i];
                        if (c == null) continue;
                        c.Visible = faceMode;
                        c.Enabled = paramOn;
                    }
                if (_outCard != null)
                {
                    int baseHeight = _outCard.Height - (_smoothShown ? _smoothBlockHeight : 0);
                    _smoothShown = faceMode;
                    _outCard.Height = baseHeight + (faceMode ? _smoothBlockHeight : 0);
                    if (_outHint != null) _outHint.Top = _smoothBaseY + (faceMode ? _smoothBlockHeight : 0);
                    RelayoutCards();
                }
                if (_outHint != null) _outHint.Text = OutHint();
            }
            catch { }
        }

        /// <summary>卡片高度变了以后，按顺序重排后面卡片的位置（滚动区跟着更新）</summary>
        void RelayoutCards()
        {
            int y = 0;
            for (int i = 0; i < _scrollCards.Count; i++)
            {
                CardPanel card = _scrollCards[i];
                card.Top = y;
                _scrollCardTops[i] = y;
                y = card.Bottom + 8;
            }
            _scrollContentHeight = y;
            LayoutScrollCards();
        }

        /// <summary>穹顶圆度只在穹顶模式下有意义：平顶模式下把这一行禁用（置灰）</summary>
        void SyncShape()
        {
            try
            {
                bool dome = Settings.CellShape == 1;
                if (_domeBar != null) _domeBar.Enabled = dome;
                if (_domeNum != null) _domeNum.Enabled = dome;
                if (_domeLabel != null) _domeLabel.ForeColor = dome ? Theme.InkSoft : Theme.InkFaint;
                if (_shapeHint != null) _shapeHint.Text = ShapeHint();
            }
            catch { }
        }

        string ShapeHint()
        {
            if (Settings.CellShape == 1)
                return "穹顶：胞元中间鼓起来、边界自然收到 0（不用过渡宽度，没有平顶）";
            return "平顶：内部是平顶，只到边界过渡宽度内平滑收边（老行为）";
        }

        string GradHint(string desc = null)
        {
            if (!Settings.GradientOn || !Settings.GradientPoint.IsValid)
                return "未设置渐变参考物件（不设置时胞元均匀分布）";
            if (Math.Abs(Settings.GradientAmount) < 1e-9)
                return "渐变幅度 0：胞元均匀分布（远处本来就回基础胞元尺寸）";
            string head = string.IsNullOrEmpty(desc) ? "已设置渐变参考物件" : ("参考：" + desc);
            string dir = Settings.GradientNear == 1
                ? "靠近物件处胞元更大（更稀）"
                : "靠近物件处胞元更小（更密）";
            return head + "；" + dir + "，远处回到基础胞元尺寸";
        }

        /// <summary>换参考边界后更新头部副标题与「边界」卡片上的目标描述</summary>
        public void SetTarget(string desc)
        {
            try
            {
                if (_header != null) _header.SetSub(desc);
                if (_target != null) _target.Text = desc;
            }
            catch { }
        }

        /// <summary>拾取按钮状态：**没选 / 选错（类型不支持、解析失败） / 被删 = 红**，选到可用边界 = 绿</summary>
        public void SetTargetState(bool ok)
        {
            try
            {
                if (_pick != null) _pick.Picked = ok;
                _target.ForeColor = ok ? Theme.Ink : Theme.InkFaint;
                _targetHint.Text = ok ? "已选边界（可多选：平面 / 闭合平面曲线）" : "可多选：平面 / 闭合平面曲线";
            }
            catch { }
        }

        /// <summary>渐变物件按钮状态：未选 / 选错 / 被删 = 红，选到可用 = 绿</summary>
        public void SetGradientState(bool ok)
        {
            try { if (_pickGrad != null) _pickGrad.Picked = ok; } catch { }
        }

        /// <summary>渐变参考物件变化后更新提示</summary>
        public void SetGradient(string desc)
        {
            try { if (_gradHint != null) _gradHint.Text = GradHint(desc); } catch { }
        }

        public void SetInfo(string text)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke((Action)(() => { if (!IsDisposed) _info.Text = text; })); return; }
                _info.Text = text;
            }
            catch { }
        }

        /// <summary>供会话/自检切换输出模式（0 线框 / 1 网格面）</summary>
        public void SelectOutputMode(int mode)
        {
            if (_outSeg != null) _outSeg.SelectedIndex = mode == 0 ? 0 : 1;
        }

        /// <summary>供自检切换渐变方向（0 靠近更密 / 1 靠近更稀）</summary>
        public void SelectGradientMode(int mode)
        {
            if (_gradSeg != null) _gradSeg.SelectedIndex = mode == 1 ? 1 : 0;
        }

        /// <summary>供自检开关「一键平滑」</summary>
        public void SetSmooth(bool on)
        {
            if (_smoothCheck != null) _smoothCheck.Checked = on;
        }

        /// <summary>拾取按钮当前是否「已选到可用边界」（自检用）</summary>
        public bool PickState
        {
            get { return _pick != null && _pick.Picked; }
        }

        /// <summary>渐变按钮当前是否「已选到可用物件」（自检用）</summary>
        public bool GradientPickState
        {
            get { return _pickGrad != null && _pickGrad.Picked; }
        }

        /// <summary>平滑块当前是否可见（自检用）</summary>
        public bool SmoothBlockVisible
        {
            get { return _smoothCheck != null && _smoothCheck.Visible; }
        }

        /// <summary>平滑参数当前是否可调（自检用）</summary>
        public bool SmoothParamsEnabled
        {
            get
            {
                if (_smoothRows == null || _smoothRows.Count == 0) return false;
                for (int i = 0; i < _smoothRows.Count; i++)
                    if (_smoothRows[i] != null && !_smoothRows[i].Enabled) return false;
                return true;
            }
        }
    }
}
