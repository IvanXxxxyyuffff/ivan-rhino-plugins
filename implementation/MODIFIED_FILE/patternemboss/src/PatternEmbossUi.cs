using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace PatternEmboss
{
    /// <summary>实时预览：画生成的带凹凸网格（浅色面 + 深色线）</summary>
    public class PatternEmbossPreviewConduit : DisplayConduit
    {
        public Mesh Mesh;
        static readonly Color FaceCol = Color.FromArgb(150, 120, 196, 214);
        static readonly Color WireCol = Color.FromArgb(210, 22, 96, 116);

        protected override void DrawForeground(DrawEventArgs e)
        {
            Mesh m = Mesh;
            if (m == null || m.Faces.Count == 0) return;
            try
            {
                e.Display.DrawMeshShaded(m, new DisplayMaterial(FaceCol, 0.35));
                e.Display.DrawMeshWires(m, WireCol, 1);
            }
            catch { }
        }
    }

    /// <summary>参数面板（这个插件没有目标物件：只有参数 + 拉杆，直接在工作平面上生成）</summary>
    public class PatternEmbossPanel : Form
    {
        public PatternEmbossSettings Settings { get; private set; }
        public bool Committed { get; private set; }
        public bool LivePreview { get { return _live.Checked; } }
        public event EventHandler ValueChanged;

        readonly InfoBar _info = new InfoBar();
        readonly IvanCheck _live = new IvanCheck();
        readonly Label _layoutHint = new Label();
        readonly Label _shapeHint = new Label();
        readonly Label _targetInfo = new Label();
        FlatButton _pickBtn;
        IvanSegmented _segPattern;
        public event EventHandler PickRequested;
        readonly List<CardPanel> _scrollCards = new List<CardPanel>();
        readonly List<int> _scrollCardTops = new List<int>();
        VScrollBar _scrollBar;
        int _scrollBodyTop, _scrollFooterHeight, _scrollContentHeight;
        bool _scrollSync, _scrollFocusHooked;
        HeaderBand _header;
        bool _sync;
        bool _closing;

        const int W = 434;

        public PatternEmbossPanel(PatternEmbossSettings settings, string placeDesc)
        {
            Settings = settings.Clone();

            Text = "图案压花 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("emboss", 16); } catch { }

            int y = 0;

            // ---- 目标曲面
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 88, Title = "目标" };
                AddScrollCard(card);

                _targetInfo.SetBounds(12, 26, W - 48, 18);
                _targetInfo.ForeColor = Theme.Ink;
                _targetInfo.Font = Theme.Body;
                _targetInfo.Text = "未选曲面";
                card.Controls.Add(_targetInfo);

                _pickBtn = new FlatButton { Text = "选择曲面", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 100, Height = 28, Left = 12, Top = 50 };
                _pickBtn.Click += (s2, e2) => { EventHandler h = PickRequested; if (h != null) h(this, EventArgs.Empty); };
                card.Controls.Add(_pickBtn);

                var hint = new Label();
                hint.SetBounds(122, 54, W - 140, 20);
                hint.ForeColor = Theme.InkFaint;
                hint.Font = Theme.Small;
                hint.Text = "在曲面上生成凹凸纹理";
                card.Controls.Add(hint);

                y = card.Bottom + 8;
            }

            // ---- 图案类型
            {
                var cPat = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 74, Title = "图案类型" };
                AddScrollCard(cPat);
                _segPattern = new IvanSegmented();
                _segPattern.SetItems(PatternEmbossSettings.PatternNames);
                _segPattern.SelectedIndex = ClampIdx(Settings.Pattern, PatternEmbossSettings.PatternNames.Length);
                _segPattern.SetBounds(12, 26, W - 48, 28);
                _segPattern.SelectedChanged += (s2, e2) => { Settings.Pattern = _segPattern.SelectedIndex; Raise(); };
                cPat.Controls.Add(_segPattern);
                y = cPat.Bottom + 8;
            }

            // ---- 纹理参数
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "纹理参数" };
                AddScrollCard(card);
                int cy = 26;
                AddRow(card, "单元尺寸", "mm", 0.5, 20.0, 5000.0, 0.1, Settings.CellSize, ref cy, v => Settings.CellSize = v);
                AddRow(card, "高度", "mm", -5.0, 5.0, 500.0, 0.05, Settings.Height, ref cy, v => Settings.Height = v);
                AddRow(card, "槽宽", "mm", 0.0, 5.0, 500.0, 0.05, Settings.Groove, ref cy, v => Settings.Groove = v);
                AddRow(card, "圆润度", "", 0.0, 1.0, 1.0, 0.01, Settings.Roundness, ref cy, v => Settings.Roundness = v);
                AddRow(card, "边缘留边", "mm", 0.0, 10.0, 500.0, 0.1, Settings.Margin, ref cy, v => Settings.Margin = v);
                AddRow(card, "采样密度", "", 6.0, 24.0, 24.0, 1.0, Settings.Density, ref cy, v => Settings.Density = (int)Math.Round(v));
                AddRow(card, "加厚", "mm", 0.0, 5.0, 500.0, 0.1, Settings.Thickness, ref cy, v => Settings.Thickness = v);
                card.Height = cy + 6;
                y = card.Bottom + 8;
            }

            // ---- 预览选项
            {
                var opt = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 58, Title = "预览选项" };
                AddScrollCard(opt);
                _live.Text = "实时预览";
                _live.Checked = true;
                _live.SetBounds(12, 26, 150, 22);
                _live.CheckedChanged += (s2, e2) => Raise();
                opt.Controls.Add(_live);
                opt.Height = 58;
                y = opt.Bottom + 8;
            }

            // ---- 一键平滑输出（QuadRemesh 重构 → SubD 细分曲面）
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "一键平滑输出（细分曲面）" };
                AddScrollCard(card);
                int cy = 26;

                var smoothOn = new IvanCheck { Text = "启用一键平滑（图案化四边网直接转 SubD）", Checked = Settings.SubDOutput };
                smoothOn.SetBounds(12, cy, W - 48, 22);
                smoothOn.CheckedChanged += (s2, e2) => { Settings.SubDOutput = smoothOn.Checked; Raise(); };
                card.Controls.Add(smoothOn);
                cy += 30;

                var subdHint = new Label();
                subdHint.SetBounds(12, cy - 4, W - 48, 30);
                subdHint.ForeColor = Theme.InkFaint;
                subdHint.Font = Theme.Small;
                subdHint.Text = "拓扑沿图案（每单元同心环四边面），直接转细分曲面输出";
                card.Controls.Add(subdHint);
                cy += 26;

                card.Height = cy + 6;
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

            _header = new HeaderBand("图案压花", placeDesc, "emboss");
            _header.SetBounds(0, 0, W, 48);
            Controls.Add(_header);
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

        public void SetTarget(string desc) { SetTarget(desc, true); }

        /// <summary>目标状态：picked=false → 拾取按钮红（未选）、true → 绿（已选）——四插件统一口径</summary>
        public void SetTarget(string desc, bool picked)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke((Action)(() => SetTarget(desc, picked))); return; }
                _targetInfo.Text = desc;
                if (_pickBtn != null) _pickBtn.Picked = picked;
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
