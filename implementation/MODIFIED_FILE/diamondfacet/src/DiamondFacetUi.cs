using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace DiamondFacetPattern
{
    /// <summary>
    /// 实时预览：
    ///   仅线框 → 画切面线框曲线；
    ///   面 → 半透明着色面体 **+ 网格线框**（DrawMeshWires，快），这样能看清面片/四边面的边界。
    /// </summary>
    public class DiamondFacetPreviewConduit : DisplayConduit
    {
        public List<Curve> Curves = new List<Curve>();
        public Mesh Shaded;                    // 要着色 + 画线框的网格（勾了平滑就是平滑后的网格）
        public int OutputMode = 1;
        public bool ShowMeshWires = true;
        static readonly Color Col = Color.FromArgb(255, 122, 92, 214);
        static readonly Color WireCol = Color.FromArgb(210, 46, 30, 86);
        static readonly Color FaceCol = Color.FromArgb(150, 132, 104, 220);

        protected override void DrawForeground(DrawEventArgs e)
        {
            if (OutputMode == 1)
            {
                try
                {
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
        }
    }

    /// <summary>钻石切面参数面板：边界 + 参数 + 输出模式 + 实时预览</summary>
    public class DiamondFacetPanel : Form
    {
        public DiamondFacetSettings Settings { get; private set; }
        public bool Committed { get; private set; }
        public bool LivePreview { get { return _live.Checked; } }
        public event EventHandler ValueChanged;
        public event EventHandler PickRequested;

        readonly InfoBar _info = new InfoBar();
        readonly IvanCheck _live = new IvanCheck();
        readonly Label _target = new Label();
        readonly Label _targetHint = new Label();
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

        IvanCheck _smoothCheck;
        IvanSegmented _outSeg;
        FlatButton _pick;
        List<Control> _smoothRows;
        CardPanel _outCard;
        int _smoothBaseY, _smoothBlockHeight;

        public DiamondFacetPanel(DiamondFacetSettings settings, string targetDesc)
        {
            Settings = settings.Clone();

            Text = "钻石切面 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("diamond", 16); } catch { }

            int y = 0;

            // ---- 边界
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
                _targetHint.Text = "可多选：平面 / 闭合曲线";
                card.Controls.Add(_targetHint);

                var hint2 = new Label();
                hint2.SetBounds(12, 82, W - 48, 26);
                hint2.ForeColor = Theme.InkFaint;
                hint2.Font = Theme.Small;
                hint2.Text = "在边界内生成三角切面；面片大小 = 目标边长（真实 mm）";
                card.Controls.Add(hint2);

                y = card.Bottom + 8;
            }

            // ---- 切面参数
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 200, Title = "切面参数" };
                AddScrollCard(card);
                int cy = 26;
                AddRow(card, "面片大小", "mm", 0.5, 50.0, 2000.0, 0.1, Settings.FacetSize, ref cy, v => Settings.FacetSize = v);
                AddRow(card, "起伏高度", "mm", 0.0, 20.0, 1000.0, 0.01, Settings.ReliefHeight, ref cy, v => Settings.ReliefHeight = v);
                AddRow(card, "松弛次数", "", 0.0, 20.0, 100.0, 1.0, Settings.Relax, ref cy, v => Settings.Relax = (int)Math.Round(v));
                IvanSlider seedBar;
                NumericUpDown seedNum;
                AddRow(card, "随机种子", "", 0.0, 999.0, 999999.0, 1.0, Settings.Seed, ref cy,
                    v => Settings.Seed = (int)Math.Round(v), out seedBar, out seedNum);

                var dice = new FlatButton { Text = "换一种", Style = BtnStyle.Ghost, Width = 88, Height = 26, Left = 12, Top = cy };
                dice.Click += (s, e) =>
                {
                    int next = new Random().Next(0, 1000);
                    _sync = true;
                    try
                    {
                        Settings.Seed = next;
                        if (seedBar != null) seedBar.Value = next;
                        if (seedNum != null) seedNum.Value = next;
                    }
                    finally { _sync = false; }
                    Raise();
                };
                card.Controls.Add(dice);
                var seedHint = new Label();
                seedHint.SetBounds(108, cy + 4, W - 144, 18);
                seedHint.ForeColor = Theme.InkFaint;
                seedHint.Font = Theme.Small;
                seedHint.Text = "换一种 = 换一套随机切法";
                card.Controls.Add(seedHint);
                cy += 34;

                card.Height = cy + 6;
                y = card.Bottom + 8;
            }

            // ---- 输出与边界
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 116, Title = "输出与边界" };
                AddScrollCard(card);

                var seg = new IvanSegmented();
                seg.SetItems(new string[] { "仅线框", "面" });
                seg.SelectedIndex = Settings.OutputMode == 0 ? 0 : 1;
                seg.SetBounds(12, 26, W - 48, 28);
                seg.SelectedChanged += (s, e) =>
                {
                    Settings.OutputMode = seg.SelectedIndex == 0 ? 0 : 1;
                    ApplySmoothState();
                    _outHint.Text = OutHint();
                    Raise();
                };
                card.Controls.Add(seg);
                _outSeg = seg;

                var lockB = new IvanCheck { Text = "固定边界（边界上的点锁在边界上）", Checked = Settings.LockBoundary };
                lockB.SetBounds(12, 60, W - 48, 22);
                lockB.CheckedChanged += (s, e) => { Settings.LockBoundary = lockB.Checked; Raise(); };
                card.Controls.Add(lockB);

                _smoothCheck = new IvanCheck { Text = "一键平滑（每个切面细分网格片）", Checked = Settings.Smooth };
                _smoothCheck.SetBounds(12, 84, W - 48, 22);
                _smoothCheck.CheckedChanged += (s, e) =>
                {
                    Settings.Smooth = _smoothCheck.Checked;
                    ApplySmoothState();
                    _outHint.Text = OutHint();
                    Raise();
                };
                card.Controls.Add(_smoothCheck);

                int fy = 110;
                _smoothRows = new List<Control>();
                _smoothRows.AddRange(AddRowEx(card, "平滑度", "%", 80.0, 100.0, 100.0, 1.0, Settings.SmoothAdaptive, ref fy, v => Settings.SmoothAdaptive = v));
                _smoothRows.AddRange(AddRowEx(card, "细分程度", "", 100.0, 20000.0, 500000.0, 100.0, Settings.SmoothQuadCount, ref fy, v => Settings.SmoothQuadCount = (int)Math.Round(v)));
                _smoothBaseY = 84;                       // 平滑块起始 y
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

            // ---- 预览选项
            {
                var opt = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 58, Title = "预览选项" };
                AddScrollCard(opt);
                _live.Text = "实时预览";
                _live.Checked = true;
                _live.SetBounds(12, 30, 150, 22);
                _live.CheckedChanged += (s, e) => Raise();
                opt.Controls.Add(_live);
                y = opt.Bottom + 8;
            }

            _info.Text = "就绪";
            Controls.Add(_info);

            var ok = new FlatButton { Text = "生成", Style = BtnStyle.Primary, Width = 96, Height = 32, Left = W - 12 - 96 - 104 };
            ok.Click += (s, e) => { Committed = true; CloseAnimated(); };
            Controls.Add(ok);

            var cancel = new FlatButton { Text = "取消", Style = BtnStyle.Ghost, Width = 96, Height = 32, Left = W - 12 - 96 };
            cancel.Click += (s, e) => CloseAnimated();
            Controls.Add(cancel);

            const int bodyTop = 56, footerHeight = 96;
            int clientHeight = FitClientHeight(bodyTop + y + footerHeight);
            _scrollBodyTop = bodyTop;
            _scrollFooterHeight = footerHeight;
            _scrollContentHeight = y;
            _scrollBar = new VScrollBar { SmallChange = 28, LargeChange = 1, Minimum = 0 };
            _scrollBar.ValueChanged += (s, e) => { if (!_scrollSync) LayoutScrollCards(); };
            Controls.Add(_scrollBar);
            _info.SetBounds(12, clientHeight - footerHeight, W - 24, 44);
            ok.Top = clientHeight - 12 - 32;
            cancel.Top = clientHeight - 12 - 32;
            ClientSize = new Size(W, clientHeight);
            LayoutScrollCards();

            _header = new HeaderBand("钻石切面", targetDesc, "diamond");
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

        string OutHint()
        {
            if (Settings.OutputMode == 0) return "只输出三角线框（曲线，图层：钻石切面-线框）；不带面";
            return Settings.Smooth
                ? "每个三角切面细分出一张网格片 → 输出网格（图层：钻石切面-平滑）；平滑度 = 网格边长的均匀度（100% 最均匀）"
                : "只输出原本的平面（每个三角面一张独立 NURBS 平面，图层：钻石切面-面；不带线框）";
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
            IvanSlider bar;
            NumericUpDown num;
            AddRow(card, name, unit, min, sliderMax, hardMax, step, value, ref y, setter, out bar, out num);
        }

        /// <summary>加一行并把这行的三个控件（标签/滑杆/数字框）返回，便于整行显示/隐藏</summary>
        List<Control> AddRowEx(CardPanel card, string name, string unit, double min, double sliderMax, double hardMax,
                    double step, double value, ref int y, Action<double> setter)
        {
            int before = card.Controls.Count;
            IvanSlider bar;
            NumericUpDown num;
            AddRow(card, name, unit, min, sliderMax, hardMax, step, value, ref y, setter, out bar, out num);
            var row = new List<Control>();
            for (int i = before; i < card.Controls.Count; i++) row.Add(card.Controls[i]);
            return row;
        }

        /// <summary>
        /// 平滑块的可见/可用状态（用户口径）：
        ///   仅线框 → 一键平滑整块**隐藏**（看不到）；
        ///   面 + 未勾选 → 平滑块可见但参数**灰掉**；
        ///   面 + 已勾选 → 参数可调。卡片高度跟着变，后面的卡片自动上移/下移。
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
                    RelayoutCards();
                }
            }
            catch { }
        }

        bool _smoothShown = true;

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

        /// <summary>供会话/自检切换输出模式（0 仅线框 / 1 面）</summary>
        public void SelectOutputMode(int mode)
        {
            if (_outSeg != null) _outSeg.SelectedIndex = mode == 0 ? 0 : 1;
        }

        /// <summary>供自检开关「一键平滑」</summary>
        public void SetSmooth(bool on)
        {
            if (_smoothCheck != null) _smoothCheck.Checked = on;
        }

        /// <summary>拾取按钮当前状态（true = 绿/已选可用，false = 红；自检用）</summary>
        public bool PickState
        {
            get { return _pick != null && _pick.Picked; }
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

        void AddRow(CardPanel card, string name, string unit, double min, double sliderMax, double hardMax,
                    double step, double value, ref int y, Action<double> setter,
                    out IvanSlider outBar, out NumericUpDown outNum)
        {
            int decimals = step >= 1.0 ? 0 : (step >= 0.1 ? 1 : 2);

            card.Controls.Add(new Label
            {
                Text = name + unit,
                Left = 10, Top = y + 6, Width = 84, Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.InkSoft,
                Font = Theme.Body
            });

            var bar = new IvanSlider
            {
                Minimum = min,
                Maximum = sliderMax,
                Step = step,
                Left = 96, Top = y, Width = 186, Height = 26,
                Value = ClampD(value, min, sliderMax)
            };
            card.Controls.Add(bar);

            var num = new NumericUpDown
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
            card.Controls.Add(num);

            bar.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                _sync = true;
                try
                {
                    double v = bar.Value;
                    num.Value = (decimal)Math.Round(ClampD(v, min, hardMax), decimals);
                    setter(v);
                }
                finally { _sync = false; }
                Raise();
            };

            num.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                _sync = true;
                try
                {
                    double v = (double)num.Value;
                    bar.Value = ClampD(v, bar.Minimum, bar.Maximum);
                    setter(v);
                }
                finally { _sync = false; }
                Raise();
            };

            outBar = bar;
            outNum = num;
            y += 32;
        }

        static double ClampD(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

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
                _targetHint.Text = ok ? "已选边界（可多选：平面 / 闭合曲线）" : "可多选：平面 / 闭合曲线";
            }
            catch { }
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
    }
}
