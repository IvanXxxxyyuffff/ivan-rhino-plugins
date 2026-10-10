using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace SurfaceUnifyPattern
{
    /// <summary>
    /// 实时预览（预览 = 输出）：半透明着色结果面 + 结果面线框；
    /// 另外用红线画原目标的裸露边界，方便肉眼对比「边界有没有贴合」。
    /// </summary>
    public class SurfaceUnifyPreviewConduit : DisplayConduit
    {
        public List<Brep> Shaded;                  // 结果面（可多个目标）
        public List<Polyline> SourceBoundary;      // 原边界（红线）
        public bool ShowSourceBoundary = true;
        public bool ShowWires = true;
        static readonly Color WireCol = Color.FromArgb(210, 74, 26, 44);
        static readonly Color FaceCol = Color.FromArgb(150, 226, 172, 194);
        static readonly Color SrcCol = Color.FromArgb(225, 205, 64, 64);

        protected override void DrawForeground(DrawEventArgs e)
        {
            try
            {
                List<Brep> bs = Shaded;
                if (bs != null)
                {
                    for (int i = 0; i < bs.Count; i++)
                    {
                        Brep b = bs[i];
                        if (b == null || b.Faces.Count == 0) continue;
                        e.Display.DrawBrepShaded(b, new DisplayMaterial(FaceCol, 0.35));
                        if (ShowWires) e.Display.DrawBrepWires(b, WireCol, 1);
                    }
                }
                if (ShowSourceBoundary && SourceBoundary != null)
                {
                    for (int i = 0; i < SourceBoundary.Count; i++)
                    {
                        Polyline pl = SourceBoundary[i];
                        if (pl == null || pl.Count < 2) continue;
                        e.Display.DrawPolyline(pl, SrcCol, 2);
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>多重曲面 → 单一曲面 参数面板：目标 + 拟合 + 边界与输出 + 预览选项</summary>
    public class SurfaceUnifyPanel : Form
    {
        public SurfaceUnifySettings Settings { get; private set; }
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

        FlatButton _pick;
        List<Control> _gridRows, _strengthRows, _smoothRows, _snapRows;
        IvanCheck _lockCheck, _holesCheck, _trimCheck, _srcCheck;
        Label _fitHint;

        public SurfaceUnifyPanel(SurfaceUnifySettings settings, string targetDesc)
        {
            Settings = settings.Clone();

            Text = "多重曲面 → 单一曲面 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("unify", 16); } catch { }

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
                _targetHint.Text = "曲面 / 多重曲面 / 挤出体 / 网格";
                card.Controls.Add(_targetHint);

                var hint2 = new Label();
                hint2.SetBounds(12, 82, W - 48, 26);
                hint2.ForeColor = Theme.InkFaint;
                hint2.Font = Theme.Small;
                hint2.Text = "要开放的片状体（有裸露边界）；闭合体会被拒绝。多重曲面的接缝会先焊起来";
                card.Controls.Add(hint2);

                y = card.Bottom + 8;
            }

            // ---- 拟合
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 200, Title = "拟合" };
                AddScrollCard(card);
                int cy = 26;

                _gridRows = AddRowEx(card, "控制点数", "", 4.0, 24.0, 40.0, 1.0, Settings.GridCount, ref cy, v => Settings.GridCount = (int)Math.Round(v));
                _strengthRows = AddRowEx(card, "贴合强度", "", 0.0, 1.0, 1.0, 0.01, Settings.FitStrength, ref cy, v => Settings.FitStrength = v);
                _smoothRows = AddRowEx(card, "平滑度", "", 0.0, 1.0, 1.0, 0.01, Settings.Smooth, ref cy, v => Settings.Smooth = v);
                _snapRows = AddRowEx(card, "最大贴合距离", "mm", 0.0, 20.0, 100000.0, 0.1, Settings.MaxSnapDistance, ref cy, v => Settings.MaxSnapDistance = v);

                _fitHint = new Label();
                _fitHint.SetBounds(12, cy, W - 48, 44);
                _fitHint.ForeColor = Theme.InkFaint;
                _fitHint.Font = Theme.Small;
                _fitHint.Text = FitHint();
                card.Controls.Add(_fitHint);
                cy += 48;

                card.Height = cy + 6;
                y = card.Bottom + 8;
            }

            // ---- 边界与输出
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 160, Title = "边界与输出" };
                AddScrollCard(card);
                int by = 26;

                _lockCheck = new IvanCheck { Text = "边界保形（边界点锁在原边界上）", Checked = Settings.LockBoundary };
                _lockCheck.SetBounds(12, by, W - 48, 22);
                _lockCheck.CheckedChanged += (s, e) =>
                {
                    Settings.LockBoundary = _lockCheck.Checked;
                    Raise();
                };
                card.Controls.Add(_lockCheck);
                by += 28;

                _holesCheck = new IvanCheck { Text = "保留内孔（内孔投影到结果面做修剪）", Checked = Settings.KeepHoles };
                _holesCheck.SetBounds(12, by, W - 48, 22);
                _holesCheck.CheckedChanged += (s, e) =>
                {
                    Settings.KeepHoles = _holesCheck.Checked;
                    Raise();
                };
                card.Controls.Add(_holesCheck);
                by += 28;

                _trimCheck = new IvanCheck { Text = "允许修剪（域外扩后按原边界剪掉多余部分）", Checked = Settings.AllowTrim };
                _trimCheck.SetBounds(12, by, W - 48, 22);
                _trimCheck.CheckedChanged += (s, e) =>
                {
                    Settings.AllowTrim = _trimCheck.Checked;
                    Raise();
                };
                card.Controls.Add(_trimCheck);
                by += 28;

                var bh = new Label();
                bh.SetBounds(12, by, W - 48, 34);
                bh.ForeColor = Theme.InkFaint;
                bh.Font = Theme.Small;
                bh.Text = "输出 = 1 张 NURBS 曲面（曲面数 1）；边界点就是原边界的采样点，所以边界完全逼近。关掉边界保形 = 边界也参与光顺";
                card.Controls.Add(bh);
                by += 38;

                card.Height = by + 6;
                y = card.Bottom + 8;
            }

            // ---- 预览选项
            {
                var opt = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 86, Title = "预览选项" };
                AddScrollCard(opt);
                _live.Text = "实时预览";
                _live.Checked = true;
                _live.SetBounds(12, 28, 150, 22);
                _live.CheckedChanged += (s, e) => Raise();
                opt.Controls.Add(_live);

                _srcCheck = new IvanCheck { Text = "显示原边界（红线）", Checked = Settings.ShowSourceBoundary };
                _srcCheck.SetBounds(12, 54, W - 48, 22);
                _srcCheck.CheckedChanged += (s, e) =>
                {
                    Settings.ShowSourceBoundary = _srcCheck.Checked;
                    Raise();
                };
                opt.Controls.Add(_srcCheck);
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

            _header = new HeaderBand("多重曲面 → 单一曲面", targetDesc, "unify");
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

        /// <summary>拟合卡片下的说明（把每个参数的作用写清楚，避免「点了没反应」）</summary>
        string FitHint()
        {
            return "控制点数 = 每向网格点数（越大越贴原曲面、面越重）；贴合强度 = 网格点向原曲面靠多少；平滑度 = 网格光顺强度；最大贴合距离 0 = 自动（对角线×0.25）";
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
                        if (number != null && Math.Abs((number.Top + number.Height / 2) - (lengthLabel.Top + lengthLabel.Height / 2)) <= 10)
                            number.DecimalPlaces = 2;
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

        /// <summary>供自检切换参数</summary>
        public void SetLockBoundary(bool on) { if (_lockCheck != null) _lockCheck.Checked = on; }
        public bool LockBoundaryChecked { get { return _lockCheck != null && _lockCheck.Checked; } }
        public void SetKeepHoles(bool on) { if (_holesCheck != null) _holesCheck.Checked = on; }
        public bool KeepHolesChecked { get { return _holesCheck != null && _holesCheck.Checked; } }
        public void SetAllowTrim(bool on) { if (_trimCheck != null) _trimCheck.Checked = on; }
        public bool AllowTrimChecked { get { return _trimCheck != null && _trimCheck.Checked; } }
        public void SetShowSourceBoundary(bool on) { if (_srcCheck != null) _srcCheck.Checked = on; }
        public bool ShowSourceBoundaryChecked { get { return _srcCheck != null && _srcCheck.Checked; } }

        /// <summary>拾取按钮当前状态（true = 绿/已选可用，false = 红；自检用）</summary>
        public bool PickState { get { return _pick != null && _pick.Picked; } }

        /// <summary>四个参数行当前是否可调（自检用）：0 控制点数 / 1 贴合强度 / 2 平滑度 / 3 最大贴合距离</summary>
        public bool RowEnabled(int which)
        {
            List<Control> rows = which == 0 ? _gridRows : (which == 1 ? _strengthRows : (which == 2 ? _smoothRows : _snapRows));
            if (rows == null || rows.Count == 0) return false;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && !rows[i].Enabled) return false;
            return true;
        }

        /// <summary>
        /// 供自检/会话用：按行名（如「控制点数」）设置数值 —— 走的是和用户拖动滑杆/敲数字框
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
                _targetHint.Text = ok ? "已选目标（片状体）" : "曲面 / 多重曲面 / 挤出体 / 网格";
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
