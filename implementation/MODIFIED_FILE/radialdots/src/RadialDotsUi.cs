using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace RadialDotsPattern
{
    /// <summary>实时预览：只画生成出来的图形（合并后是什么样就画什么样）</summary>
    public class RadialDotsPreviewConduit : DisplayConduit
    {
        public List<Curve> Curves = new List<Curve>();
        static readonly Color Col = Color.FromArgb(255, 122, 92, 214);

        protected override void DrawForeground(DrawEventArgs e)
        {
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

    /// <summary>参数面板（这个插件没有目标物件：只有参数 + 拉杆，直接在工作平面上生成）</summary>
    public class RadialDotsPanel : Form
    {
        public RadialDotSettings Settings { get; private set; }
        public bool Committed { get; private set; }
        public bool LivePreview { get { return _live.Checked; } }
        public event EventHandler ValueChanged;

        readonly InfoBar _info = new InfoBar();
        readonly IvanCheck _live = new IvanCheck();
        readonly Label _layoutHint = new Label();
        readonly Label _shapeHint = new Label();
        CardPanel _layoutCard, _shapeCard;
        IvanSegmented _segLayout;
        IvanSegmented _segShape;
        readonly List<CardPanel> _scrollCards = new List<CardPanel>();
        readonly List<int> _scrollCardTops = new List<int>();
        VScrollBar _scrollBar;
        int _scrollBodyTop, _scrollFooterHeight, _scrollContentHeight;
        bool _scrollSync, _scrollFocusHooked;
        HeaderBand _header;
        bool _sync;
        bool _closing;

        const int W = 434;

        public RadialDotsPanel(RadialDotSettings settings, string placeDesc)
        {
            Settings = settings.Clone();

            Text = "径向渐变圆点 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("radialdots", 16); } catch { }

            int y = 0;
            var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "尺寸与渐变" };
            AddScrollCard(card);

            int cy = 26;
            // 滑杆范围按「小尺寸图案」给：半径 0–10 mm、间距/直径 0–5 mm，步长 0.01（两位小数）；
            // 要更大直接在右侧数字框里输入（上限 2000 / 200 mm）
            AddRow(card, "外半径", "mm", 0.5, 10.0, 2000.0, 0.01, Settings.OuterR, ref cy, v => Settings.OuterR = v);
            AddRow(card, "内半径", "mm", 0.0, 10.0, 2000.0, 0.01, Settings.InnerR, ref cy, v => Settings.InnerR = v);
            AddRow(card, "间距", "mm", 0.05, 5.0, 200.0, 0.01, Settings.Pitch, ref cy, v => Settings.Pitch = v);
            AddRow(card, "最大直径", "mm", 0.05, 5.0, 200.0, 0.01, Settings.MaxDia, ref cy, v => Settings.MaxDia = v);
            AddRow(card, "最小直径", "mm", 0.0, 5.0, 200.0, 0.01, Settings.MinDia, ref cy, v => Settings.MinDia = v);
            AddRow(card, "峰值位置", "", 0.0, 1.0, 1.0, 0.01, Settings.Peak, ref cy, v => Settings.Peak = v);
            AddRow(card, "衰减", "", 0.1, 5.0, 20.0, 0.01, Settings.Falloff, ref cy, v => Settings.Falloff = v);
            card.Height = cy + 6;
            y = card.Bottom + 8;

            // 阵列方式
            {
                var cLayout = _layoutCard = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 96, Title = "阵列方式" };
                AddScrollCard(cLayout);

                _segLayout = new IvanSegmented();
                _segLayout.SetItems(RadialDotSettings.LayoutNames);
                _segLayout.SelectedIndex = ClampIdx(Settings.Layout, RadialDotSettings.LayoutNames.Length);
                _segLayout.SetBounds(12, 26, W - 48, 28);
                _segLayout.SelectedChanged += (s2, e2) =>
                {
                    Settings.Layout = _segLayout.SelectedIndex;
                    _layoutHint.Text = LayoutHint();
                    Raise();
                };
                cLayout.Controls.Add(_segLayout);

                var stagger = new IvanCheck { Text = "相邻环错开半格（同心环）", Checked = Settings.Stagger };
                stagger.SetBounds(12, 60, W - 48, 22);
                stagger.CheckedChanged += (s2, e2) => { Settings.Stagger = stagger.Checked; Raise(); };
                cLayout.Controls.Add(stagger);

                _layoutHint.SetBounds(12, 80, W - 48, 16);
                _layoutHint.ForeColor = Theme.InkFaint;
                _layoutHint.Font = Theme.Small;
                _layoutHint.Text = LayoutHint();
                cLayout.Height = 100;
                cLayout.Controls.Add(_layoutHint);
                y = cLayout.Bottom + 8;
            }

            // 图形与细节
            {
                var cShape = _shapeCard = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 150, Title = "图形与细节" };
                AddScrollCard(cShape);

                _segShape = new IvanSegmented();
                _segShape.SetItems(RadialDotSettings.ShapeNames);
                _segShape.SelectedIndex = ClampIdx(Settings.Shape, RadialDotSettings.ShapeNames.Length);
                _segShape.SetBounds(12, 26, W - 48, 28);
                _segShape.SelectedChanged += (s2, e2) =>
                {
                    Settings.Shape = _segShape.SelectedIndex;
                    _shapeHint.Text = ShapeHint();
                    Raise();
                };
                cShape.Controls.Add(_segShape);

                int sy = 62;
                AddRow(cShape, "整体旋转", "°", -180.0, 180.0, 180.0, 0.01, Settings.Rotation, ref sy, v => Settings.Rotation = v);
                AddRow(cShape, "位置抖动", "", 0.0, 1.0, 1.0, 0.01, Settings.Jitter, ref sy, v => Settings.Jitter = v);

                var merge = new IvanCheck { Text = "重叠的图形自动布尔合并成一个整体", Checked = Settings.Merge };
                merge.SetBounds(12, sy, W - 48, 22);
                merge.CheckedChanged += (s2, e2) => { Settings.Merge = merge.Checked; Raise(); };
                cShape.Controls.Add(merge);

                _shapeHint.SetBounds(12, sy + 22, W - 48, 16);
                _shapeHint.ForeColor = Theme.InkFaint;
                _shapeHint.Font = Theme.Small;
                _shapeHint.Text = ShapeHint();
                cShape.Controls.Add(_shapeHint);
                cShape.Height = sy + 42;
                y = cShape.Bottom + 8;
            }

            var opt = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 58, Title = "预览选项" };
            AddScrollCard(opt);
            _live.Text = "实时预览";
            _live.Checked = true;
            _live.SetBounds(12, 30, 150, 22);
            _live.CheckedChanged += (s, e) => Raise();
            opt.Controls.Add(_live);
            y = opt.Bottom + 8;

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

            _header = new HeaderBand("径向渐变圆点", placeDesc, "radialdots");
            _header.SetBounds(0, 0, W, 48);
            Controls.Add(_header);
            Controls.SetChildIndex(_layoutCard, 0);
            Controls.SetChildIndex(_shapeCard, 1);
            _layoutCard.Controls.SetChildIndex(_segLayout, 0);
            _shapeCard.Controls.SetChildIndex(_segShape, 0);
            _scrollBar.BringToFront();
            _info.BringToFront();
            ok.BringToFront();
            cancel.BringToFront();
            _header.BringToFront();

            AcceptButton = ok;
            CancelButton = cancel;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) CloseAnimated(); };
            ApplyLengthDisplayFormat();
            PlaceNearRhino();
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

        static int ClampIdx(int v, int n) { return v < 0 ? 0 : (v >= n ? n - 1 : v); }

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

            y += 32;
        }

        static double ClampD(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        string LayoutHint()
        {
            switch (Settings.Layout)
            {
                case 1: return "螺旋：黄金角排布，越往外点数越多（面积均匀）";
                case 2: return "方形网格：按间距铺格，只保留半径带内的格子";
                case 3: return "交错网格：隔行错开半格，密度更均匀";
                default: return "同心环：每环按弧长取个数，环间距 = 间距";
            }
        }

        string ShapeHint()
        {
            if (Settings.Shape == 4) return "圆方交替：相邻图形一圆一方";
            if (Settings.Shape == 0) return "圆形：真圆弧输出";
            return "多边形：外接圆直径 = 直径参数";
        }

        public void SetPlace(string desc)
        {
            try { if (_header != null) _header.SetSub(desc); } catch { }
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
