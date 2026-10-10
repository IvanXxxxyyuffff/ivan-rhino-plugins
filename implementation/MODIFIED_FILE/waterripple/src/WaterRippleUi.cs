using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace WaterRipplePattern
{
    /// <summary>
    /// 实时预览（预览 = 输出）：
    ///   不勾一键平滑 → 半透明着色网格 + 网格线框（看得清剖分）；
    ///   勾了一键平滑 → 直接画细分曲面（DrawSubDShaded / DrawSubDWires），看得见平滑后的形状。
    /// </summary>
    public class WaterRipplePreviewConduit : DisplayConduit
    {
        public Mesh Shaded;                 // 要着色 + 画线框的网格
        public List<SubD> SmoothSubDs;      // 勾了一键平滑时直接画细分曲面（可多个目标）
        public bool ShowMeshWires = true;
        static readonly Color WireCol = Color.FromArgb(210, 20, 54, 86);
        static readonly Color FaceCol = Color.FromArgb(150, 128, 190, 226);

        protected override void DrawForeground(DrawEventArgs e)
        {
            try
            {
                List<SubD> sds = SmoothSubDs;
                if (sds != null && sds.Count > 0)
                {
                    for (int i = 0; i < sds.Count; i++)
                    {
                        SubD sd = sds[i];
                        if (sd == null) continue;
                        e.Display.DrawSubDShaded(sd, new DisplayMaterial(FaceCol, 0.35));
                        if (ShowMeshWires) e.Display.DrawSubDWires(sd, WireCol, 1);
                    }
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
        }
    }

    /// <summary>水波纹参数面板：目标 + 波纹 + 剖分与输出 + 实时预览</summary>
    public class WaterRipplePanel : Form
    {
        public WaterRippleSettings Settings { get; private set; }
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
        IvanSegmented _modeSeg;
        FlatButton _pick;
        List<Control> _smoothRows;
        CardPanel _outCard;
        List<Control> _countRows, _dirRows, _spreadRows;
        Label _modeHint;
        IvanCheck _lockCheck;
        List<Control> _fadeRows, _blendRows;

        public WaterRipplePanel(WaterRippleSettings settings, string targetDesc)
        {
            Settings = settings.Clone();

            Text = "水波纹 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("ripple", 16); } catch { }

            int y = 0;

            // ---- 目标
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 116, Title = "目标" };
                AddScrollCard(card);

                _target.SetBounds(12, 26, W - 48, 18);
                _target.ForeColor = Theme.Ink;
                _target.Font = Theme.Body;
                _target.Text = targetDesc;
                card.Controls.Add(_target);

                _pick = new FlatButton { Text = "选择目标", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 104, Height = 28, Left = 12, Top = 50 };
                _pick.Click += (s, e) => { EventHandler h = PickRequested; if (h != null) h(this, EventArgs.Empty); };
                card.Controls.Add(_pick);

                _targetHint.SetBounds(124, 54, W - 160, 20);
                _targetHint.ForeColor = Theme.InkFaint;
                _targetHint.Font = Theme.Small;
                _targetHint.Text = "曲面 / 多重曲面 / 闭合平面曲线";
                card.Controls.Add(_targetHint);

                var hint2 = new Label();
                hint2.SetBounds(12, 82, W - 48, 26);
                hint2.ForeColor = Theme.InkFaint;
                hint2.Font = Theme.Small;
                hint2.Text = "多重曲面当成一整个面（波纹跨面连续）；曲线要闭合的平面曲线";
                card.Controls.Add(hint2);

                y = card.Bottom + 8;
            }

            // ---- 波纹
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 240, Title = "波纹" };
                AddScrollCard(card);
                int cy = 26;

                var mode = new IvanSegmented();
                mode.SetItems(new string[] { "有机水波", "定向条带", "同心涟漪" });
                mode.SelectedIndex = Settings.WaveMode;
                mode.SetBounds(12, cy, W - 48, 28);
                mode.SelectedChanged += (s, e) =>
                {
                    Settings.WaveMode = mode.SelectedIndex;
                    ApplyModeState();
                    _outHint.Text = OutHint();
                    Raise();
                };
                card.Controls.Add(mode);
                _modeSeg = mode;
                cy += 36;

                AddRow(card, "波长", "mm", 0.1, 50.0, 5000.0, 0.1, Settings.Wavelength, ref cy, v => Settings.Wavelength = v);
                AddRow(card, "波高", "mm", 0.0, 10.0, 1000.0, 0.05, Settings.WaveHeight, ref cy, v => Settings.WaveHeight = v);
                _countRows = AddRowEx(card, "波数", "", 1.0, 10.0, 10.0, 1.0, Settings.WaveCount, ref cy, v => Settings.WaveCount = (int)Math.Round(v));
                _dirRows = AddRowEx(card, "主方向", "°", -180.0, 180.0, 180.0, 1.0, Settings.Direction, ref cy, v => Settings.Direction = v);
                _spreadRows = AddRowEx(card, "方向散布", "°", 0.0, 180.0, 180.0, 1.0, Settings.Spread, ref cy, v => Settings.Spread = v);
                AddRow(card, "波峰形状", "", 0.0, 1.0, 1.0, 0.01, Settings.Crest, ref cy, v => Settings.Crest = v);

                _modeHint = new Label();
                _modeHint.SetBounds(12, cy, W - 48, 30);
                _modeHint.ForeColor = Theme.InkFaint;
                _modeHint.Font = Theme.Small;
                _modeHint.Text = ModeHint();
                card.Controls.Add(_modeHint);
                cy += 34;

                card.Height = cy + 6;
                y = card.Bottom + 8;
            }

            // ---- 边界（固定边界 + 边界到纹理的过渡）
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 168, Title = "边界" };
                AddScrollCard(card);
                int by = 26;

                _lockCheck = new IvanCheck { Text = "固定边界（边界锁在原位置，纹理向内过渡）", Checked = Settings.LockBoundary };
                _lockCheck.SetBounds(12, by, W - 48, 22);
                _lockCheck.CheckedChanged += (s, e) =>
                {
                    Settings.LockBoundary = _lockCheck.Checked;
                    ApplyBoundaryState();
                    _outHint.Text = OutHint();
                    Raise();
                };
                card.Controls.Add(_lockCheck);
                by += 28;

                // 拖这两行 = 自动开固定边界（否则它们没有作用，用户会以为「点了没反应」）
                _fadeRows = AddRowEx(card, "过渡宽度", "mm", 0.0, 20.0, 1000.0, 0.1, Settings.Fade, ref by, v =>
                {
                    Settings.Fade = v;
                    if (!Settings.LockBoundary) SetLockBoundary(true);
                });
                _blendRows = AddRowEx(card, "过渡平滑度", "", 0.0, 1.0, 1.0, 0.01, Settings.BlendSmooth, ref by, v =>
                {
                    Settings.BlendSmooth = v;
                    if (!Settings.LockBoundary) SetLockBoundary(true);
                });

                var bh = new Label();
                bh.SetBounds(12, by, W - 48, 32);
                bh.ForeColor = Theme.InkFaint;
                bh.Font = Theme.Small;
                bh.Text = "固定边界 = 边界顶点一点不动；过渡宽度 = 从边界到全幅的距离（0 用 波长×0.5 兜底）；过渡平滑度 = 这条过渡的柔和程度（0 线性 → 1 最柔）";
                card.Controls.Add(bh);
                by += 36;

                card.Height = by + 6;
                y = card.Bottom + 8;
            }

            // ---- 剖分与输出
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 200, Title = "剖分与输出" };
                AddScrollCard(card);

                int fy = 26;
                AddRow(card, "每波长分段", "", 4.0, 32.0, 64.0, 1.0, Settings.SamplesPerWave, ref fy, v => Settings.SamplesPerWave = v);

                IvanSlider seedBar;
                NumericUpDown seedNum;
                AddRow(card, "随机种子", "", 0.0, 999.0, 999999.0, 1.0, Settings.Seed, ref fy,
                    v => Settings.Seed = (int)Math.Round(v), out seedBar, out seedNum);

                var dice = new FlatButton { Text = "换一种", Style = BtnStyle.Ghost, Width = 88, Height = 26, Left = 12, Top = fy };
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
                seedHint.SetBounds(108, fy + 4, W - 144, 18);
                seedHint.ForeColor = Theme.InkFaint;
                seedHint.Font = Theme.Small;
                seedHint.Text = "换一种 = 换一套随机波形（有机模式）";
                card.Controls.Add(seedHint);
                fy += 34;

                _smoothCheck = new IvanCheck { Text = "一键平滑（转细分曲面 SubD）", Checked = Settings.Smooth };
                _smoothCheck.SetBounds(12, fy, W - 48, 22);
                _smoothCheck.CheckedChanged += (s, e) =>
                {
                    Settings.Smooth = _smoothCheck.Checked;
                    _outHint.Text = OutHint();
                    Raise();
                };
                card.Controls.Add(_smoothCheck);
                _smoothRows = new List<Control>();
                fy += 28;

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

            _header = new HeaderBand("水波纹", targetDesc, "ripple");
            _header.SetBounds(0, 0, W, 48);
            Controls.Add(_header);
            _scrollBar.BringToFront();
            _info.BringToFront();
            ok.BringToFront();
            cancel.BringToFront();
            _header.BringToFront();

            ApplyModeState();
            ApplyBoundaryState();
            AcceptButton = ok;
            CancelButton = cancel;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) CloseAnimated(); };
            ApplyLengthDisplayFormat();
            PlaceNearRhino();
        }

        string OutHint()
        {
            if (Settings.WaveMode == 0)
                return Settings.Smooth
                    ? "有机水波（多方向随机叠加）→ 细分曲面（SubD，图层：水波纹-平滑）"
                    : "有机水波（多方向随机叠加）→ 网格面（图层：水波纹-网格）";
            if (Settings.WaveMode == 1)
                return Settings.Smooth
                    ? "定向条带 → 细分曲面（SubD，图层：水波纹-平滑）"
                    : "定向条带 → 网格面（图层：水波纹-网格）";
            return Settings.Smooth
                ? "同心涟漪 → 细分曲面（SubD，图层：水波纹-平滑）"
                : "同心涟漪 → 网格面（图层：水波纹-网格）";
        }

        /// <summary>当前波形下这三个参数各自的作用（写在卡片里，避免用户以为「点了没反应」）</summary>
        string ModeHint()
        {
            if (Settings.WaveMode == 1)
                return "条带：波数 = 叠几层谐波；主方向 = 条带方向；方向散布 = 条带摆动幅度";
            if (Settings.WaveMode == 2)
                return "同心：波数 = 叠几列涟漪（干涉）；主方向 = 涟漪源偏移方向；方向散布 = 环的起伏";
            return "有机：波数 = 叠加几个方向；主方向 = 主方向；方向散布 = 方向随机范围";
        }

        /// <summary>
        /// 切换波形时只更新说明：**所有行始终可用**（用户口径：灰掉的行 = 点了没反应，不允许留）。
        /// 每个参数在三种波形下都有明确作用（见 WaterRippleCore.WaveField 的注释）。
        /// </summary>
        void ApplyModeState()
        {
            try
            {
                SetRowsEnabled(_countRows, true);
                SetRowsEnabled(_dirRows, true);
                SetRowsEnabled(_spreadRows, true);
                if (_modeHint != null) _modeHint.Text = ModeHint();
            }
            catch { }
        }

        static void SetRowsEnabled(List<Control> rows, bool on)
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null) rows[i].Enabled = on;
        }

        /// <summary>固定边界块：勾了才能调过渡参数（未勾 = 全幅到边，没有过渡可调）</summary>
        void ApplyBoundaryState()
        {
            try
            {
                bool on = _lockCheck != null && _lockCheck.Checked;
                SetRowsEnabled(_fadeRows, on);
                SetRowsEnabled(_blendRows, on);
            }
            catch { }
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

        /// <summary>加一行并把这行的三个控件（标签/滑杆/数字框）返回，便于整行灰掉/启用</summary>
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

        /// <summary>供自检切换波形（0 有机 / 1 条带 / 2 同心）</summary>
        public void SetWaveMode(int mode)
        {
            if (_modeSeg != null) _modeSeg.SelectedIndex = Math.Max(0, Math.Min(2, mode));
        }

        /// <summary>供自检开关「一键平滑」</summary>
        public void SetSmooth(bool on)
        {
            if (_smoothCheck != null) _smoothCheck.Checked = on;
        }

        /// <summary>供自检/会话开关「固定边界」</summary>
        public void SetLockBoundary(bool on)
        {
            if (_lockCheck != null) _lockCheck.Checked = on;
        }

        /// <summary>固定边界当前是否勾选（自检用）</summary>
        public bool LockBoundaryChecked { get { return _lockCheck != null && _lockCheck.Checked; } }

        /// <summary>边界过渡参数当前是否可调（自检用）</summary>
        public bool BoundaryRowsEnabled
        {
            get
            {
                if (_fadeRows == null || _fadeRows.Count == 0) return false;
                for (int i = 0; i < _fadeRows.Count; i++)
                    if (_fadeRows[i] != null && !_fadeRows[i].Enabled) return false;
                return true;
            }
        }

        /// <summary>
        /// 供自检/会话用：按行名（如「波长」「波高」）设置数值 —— 走的是和用户拖动滑杆/敲数字框
        /// 完全一样的链路（改数字框 → 联动滑杆 → 写回 Settings → Raise）。
        /// </summary>
        public bool SetParam(string name, double value)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (Control card in Controls)
            {
                foreach (Control c in card.Controls)
                {
                    Label lab = c as Label;
                    if (lab == null || string.IsNullOrEmpty(lab.Text)) continue;
                    if (!lab.Text.StartsWith(name, StringComparison.Ordinal)) continue;
                    foreach (Control input in card.Controls)
                    {
                        NumericUpDown num = input as NumericUpDown;
                        if (num == null) continue;
                        // 同一行 = 两者竖直中线对齐（标签 Top=y+6、数字框 Top=y+2，别用 Top 相等判断）
                        int labMid = lab.Top + lab.Height / 2, numMid = num.Top + num.Height / 2;
                        if (Math.Abs(labMid - numMid) > 10) continue;
                        try
                        {
                            decimal v = (decimal)value;
                            if (v < num.Minimum) v = num.Minimum;
                            if (v > num.Maximum) v = num.Maximum;
                            num.Value = v;
                            return true;
                        }
                        catch { return false; }
                    }
                }
            }
            return false;
        }

        /// <summary>某个参数行当前是否可调（自检用）</summary>
        public bool ParamEnabled(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (Control card in Controls)
            {
                foreach (Control c in card.Controls)
                {
                    Label lab = c as Label;
                    if (lab == null || string.IsNullOrEmpty(lab.Text)) continue;
                    if (!lab.Text.StartsWith(name, StringComparison.Ordinal)) continue;
                    return lab.Enabled;
                }
            }
            return false;
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

        /// <summary>平滑参数当前是否可调（水波纹没有从属参数 → 恒 true；自检用）</summary>
        public bool SmoothParamsEnabled
        {
            get { return _smoothCheck != null && _smoothCheck.Enabled; }
        }

        /// <summary>波形行的可用状态（自检用）：波数 / 主方向 / 方向散布</summary>
        public bool RowEnabled(int which)
        {
            List<Control> rows = which == 0 ? _countRows : (which == 1 ? _dirRows : _spreadRows);
            if (rows == null || rows.Count == 0) return false;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && !rows[i].Enabled) return false;
            return true;
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

        /// <summary>拾取按钮状态：**没选 / 选错（类型不支持、解析失败） / 被删 = 红**，选到可用目标 = 绿</summary>
        public void SetTargetState(bool ok)
        {
            try
            {
                if (_pick != null) _pick.Picked = ok;
                _target.ForeColor = ok ? Theme.Ink : Theme.InkFaint;
                _targetHint.Text = ok ? "已选目标（曲面 / 多重曲面 / 闭合曲线）" : "曲面 / 多重曲面 / 闭合平面曲线";
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
