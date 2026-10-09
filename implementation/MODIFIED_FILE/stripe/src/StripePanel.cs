using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;

namespace StripeOnSurface
{
    /// <summary>置顶参数面板（WinForms，非模态 + 实时预览）</summary>
    public class StripePanel : Form
    {
        public StripeSettings Settings { get; private set; }
        FlatButton _pickTarget;
        CardPanel _pickCard;
        public bool Committed { get; private set; }
        public bool LivePreview { get { return _live.Checked; } }
        public event EventHandler ValueChanged;

        readonly InfoBar _info = new InfoBar();
        readonly IvanCheck _live = new IvanCheck();
        readonly IvanCheck _round = new IvanCheck();
        readonly IvanCheck _conform = new IvanCheck();
        readonly List<CardPanel> _scrollCards = new List<CardPanel>();
        readonly List<int> _scrollCardTops = new List<int>();
        VScrollBar _scrollBar;
        int _scrollBodyTop, _scrollFooterHeight, _scrollContentHeight;
        bool _scrollSync, _scrollFocusHooked;
        HeaderBand _header;
        bool _sync;
        bool _closing;

        const int W = 434;

        public StripePanel(StripeSettings settings, string target)
        {
            Settings = settings.Clone();

            Text = "表面条纹 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("stripe", 16); } catch { }

            // 统一位置：最上面一条「选择」卡片（所有插件同一个位置，未选红 / 已选绿）
            {
                _pickCard = new CardPanel { Left = 12, Top = 56, Width = W - 24, Height = 44 };
                Controls.Add(_pickCard);
                _pickTarget = new FlatButton { Text = "选择物件", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 140, Height = 28 };
                _pickTarget.SetBounds(12, 8, 140, 28);
                _pickTarget.Click += (sx, ex) => { try { Rhino.RhinoApp.RunScript("_-StripePickTarget", false); } catch { } };
                _pickCard.Controls.Add(_pickTarget);
            }

            int y = 0;
            var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "条纹参数" };
            AddScrollCard(card);

            int cy = 32;
            AddRow(card, "条纹宽度", "mm", 0.05, 20.0, 0.05, Settings.Width, ref cy, v => Settings.Width = v);
            AddRow(card, "条纹间距", "mm", 0.00, 50.0, 0.05, Settings.Spacing, ref cy, v => Settings.Spacing = v);
            AddRow(card, "倾斜角度", "°", -90.0, 90.0, 1.0, Settings.AngleDeg, ref cy, v => Settings.AngleDeg = v);
            AddRow(card, "边缘距离", "mm", 0.00, 50.0, 0.05, Settings.Margin, ref cy, v => Settings.Margin = v);
            AddRow(card, "圆角半径", "mm", 0.00, 20.0, 0.05, Settings.CornerRadius, ref cy, v => Settings.CornerRadius = v);
            card.Height = cy + 8;
            y = card.Bottom + 8;

            var opt = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 72, Title = "边界与预览" };
            AddScrollCard(opt);

            _live.Text = "实时预览";
            _live.Checked = true;
            _live.SetBounds(12, 38, 104, 22);
            _live.CheckedChanged += (s, e) => Raise();
            opt.Controls.Add(_live);

            _conform.Text = "贴合边界";
            _conform.Checked = Settings.ConformToBoundary;
            _conform.SetBounds(126, 38, 104, 22);
            _conform.CheckedChanged += (s, e) =>
            {
                Settings.ConformToBoundary = _conform.Checked;
                _round.Enabled = !_conform.Checked;
                Raise();
            };
            opt.Controls.Add(_conform);

            _round.Text = "端部圆角";
            _round.Checked = Settings.RoundedEnds;
            _round.Enabled = !Settings.ConformToBoundary;
            _round.SetBounds(240, 38, 116, 22);
            _round.CheckedChanged += (s, e) => { Settings.RoundedEnds = _round.Checked; Raise(); };
            opt.Controls.Add(_round);
            y = opt.Bottom + 8;

            _info.Text = "就绪";
            Controls.Add(_info);

            var ok = new FlatButton { Text = "生成", Style = BtnStyle.Primary, Width = 96, Height = 32, Left = W - 12 - 96 - 104 };
            ok.Click += (s, e) => { Committed = true; CloseAnimated(); };
            Controls.Add(ok);

            var cancel = new FlatButton { Text = "取消", Style = BtnStyle.Ghost, Width = 96, Height = 32, Left = W - 12 - 96 };
            cancel.Click += (s, e) => CloseAnimated();
            Controls.Add(cancel);

            const int bodyTop = 108, footerHeight = 96;
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

            var header = new HeaderBand("表面条纹", target, "stripe");
            header.SetBounds(0, 0, W, 48);
            Controls.Add(header);
            _header = header;
            _scrollBar.BringToFront();
            _pickCard.BringToFront();
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
                    card.Region = null;
                    if (previous != null) previous.Dispose();
                    card.Visible = false;
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
            if (_scrollBar == null || _scrollSync || target == null) return;
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
                int x = wa.Right - Width - 24;
                int yy = wa.Top + Math.Max(80, (wa.Height - Height) / 5);
                Location = new System.Drawing.Point(Math.Max(wa.Left, x), yy);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }
        }

        // 进入：淡入 + 上移 8px（300ms / ease-out）
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

        // 退出更快一档（150ms / ease-in）
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

        void AddRow(CardPanel card, string name, string unit, double min, double max, double step,
                    double value, ref int y, Action<double> setter)
        {
            int decimals = step < 1.0 ? 2 : 0;

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
                Maximum = max,
                Step = step,
                Left = 96, Top = y, Width = 186, Height = 26,
                Value = ClampD(value, min, max)
            };
            card.Controls.Add(bar);

            var num = new NumericUpDown
            {
                DecimalPlaces = decimals,
                Increment = (decimal)step,
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                Left = 288, Top = y + 2, Width = 86, Height = 24,
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Body,
                BackColor = Theme.Surface,
                Value = (decimal)Math.Round(ClampD(value, min, max), decimals)
            };
            card.Controls.Add(num);

            bar.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                _sync = true;
                try
                {
                    double v = bar.Value;
                    num.Value = (decimal)Math.Round(ClampD(v, min, max), decimals);
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

        /// <summary>换目标物件后更新头部副标题</summary>
        public void SetTarget(string desc)
        {
            try { if (_header != null) _header.SetSub(desc); } catch { }
        }

        /// <summary>顶部「选择物件」按钮的状态：未选 = 红、已选 = 绿</summary>
        public void SetTargetState(bool picked)
        {
            try { if (_pickTarget != null) _pickTarget.Picked = picked; } catch { }
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
