using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace HalftonePattern
{
    /// <summary>实时预览</summary>
    public class HalftonePreviewConduit : DisplayConduit
    {
        public List<Curve> Curves = new List<Curve>();
        static readonly Color Col = Color.FromArgb(255, 0, 145, 165);

        protected override void DrawForeground(DrawEventArgs e)
        {
            List<Curve> list = Curves;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Curve c = list[i];
                if (c == null || !c.IsValid) continue;
                try { e.Display.DrawCurve(c, Col, 2); } catch { }
            }
        }
    }

    /// <summary>置顶参数面板</summary>
    public class HalftonePanel : Form
    {
        public HalftoneSettings Settings { get; private set; }
        public bool Committed { get; private set; }
        public bool LivePreview { get { return _live.Checked; } }
        public event EventHandler ValueChanged;
        /// <summary>请求去视图里点一个阵列圆心（同心环/螺旋/抖动 需要）</summary>
        public event EventHandler PickCenterRequested;
        /// <summary>用户是否已手动指定过圆心</summary>
        public bool CenterPicked;

        readonly InfoBar _info = new InfoBar();
        readonly Label _centerInfo = new Label();
        readonly Label _faceHint = new Label();
        FlatButton _pickTarget;
        FlatButton _pickGrad;
        FlatButton _pickCenter;
        CardPanel _pickCard;
        readonly List<CardPanel> _scrollCards = new List<CardPanel>();
        readonly List<int> _scrollCardTops = new List<int>();
        VScrollBar _scrollBar;
        int _scrollBodyTop, _scrollFooterHeight, _scrollContentHeight;
        bool _scrollSync, _scrollFocusHooked;
        readonly IvanCheck _live = new IvanCheck();
        IvanSegmented _segFace;
        HeaderBand _header;
        bool _sync;
        bool _closing;

        // 图块上的短标签（完整名放 tooltip）
        static readonly string[] TileArrayNames = { "网格", "交错", "六边", "同心环", "螺旋", "抖动" };
        static readonly string[] TileShapeNames = { "圆形", "三角", "方形", "六边" };

        const int W = 434;

        public HalftonePanel(HalftoneSettings settings, string target)
        {
            Settings = settings.Clone();

            Text = "参数化阵列纹理 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("halftone", 16); } catch { }

            // 统一位置：最上面一条「选择」卡片（所有插件同一个位置，未选红 / 已选绿）
            {
            _pickCard = new CardPanel { Left = 12, Top = 56, Width = W - 24, Height = 44 };
            Controls.Add(_pickCard);
                _pickTarget = new FlatButton { Text = "选择物件", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 140, Height = 28 };
                _pickTarget.SetBounds(12, 8, 140, 28);
                _pickTarget.Click += (sx, ex) => { try { Rhino.RhinoApp.RunScript("_-HalftonePickTarget", false); } catch { } };
            _pickCard.Controls.Add(_pickTarget);

                _pickGrad = new FlatButton { Text = "选择渐变物件", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 150, Height = 28 };
                _pickGrad.SetBounds(160, 8, 150, 28);
                _pickGrad.Click += (sx, ex) => { try { Rhino.RhinoApp.RunScript("_-HalftonePickGradient", false); } catch { } };
            _pickCard.Controls.Add(_pickGrad);
            }

            int y = 0;
            var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "阵列参数" };
            AddScrollCard(card);

            int cy = 32;
            AddRow(card, "最大直径", "mm", 0.2, 10.0, 500.0, 0.01, Settings.MaxDia, ref cy, v => Settings.MaxDia = v);
            AddRow(card, "最小直径", "mm", 0.0, 10.0, 500.0, 0.01, Settings.MinDia, ref cy, v => Settings.MinDia = v);
            AddRow(card, "阵列间距", "mm", 0.2, 10.0, 500.0, 0.01, Settings.Pitch, ref cy, v => Settings.Pitch = v);
            AddRow(card, "边缘间距", "mm", 0.0, 10.0, 500.0, 0.01, Settings.Margin, ref cy, v => Settings.Margin = v);
            AddRow(card, "旋转角度", "°", -180.0, 180.0, 180.0, 1.0, Settings.Rotation, ref cy, v => Settings.Rotation = v);
            AddRow(card, "衰减幅度", "", 0.1, 5.0, 20.0, 0.01, Settings.Falloff, ref cy, v => Settings.Falloff = v);
            card.Height = cy + 6;
            y = card.Bottom + 8;

            // 参考面：只生成点选的那一个面 / 整个多重曲面的所有面
            {
                var scope = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 90, Title = "参考面" };
                // 单面对象：这个选项两种设置效果相同，整张卡片没必要占地方（用户口径：多余）→ 直接隐藏
                bool showScope = Settings.FaceCount > 1;
                scope.Visible = showScope;
                // 即使隐藏也保持为 Form 的直接子项；只有可见卡片进入滚动列表。
                if (showScope) AddScrollCard(scope);
                else Controls.Add(scope);

                _segFace = new IvanSegmented();
                _segFace.SetItems("选中面", "全部面");
                _segFace.SelectedIndex = Settings.OnlyFace >= 0 ? 0 : 1;
                _segFace.Enabled = Settings.FaceCount > 1;
                _segFace.SetBounds(12, 26, 186, 28);
                _segFace.SelectedChanged += (s2, e2) =>
                {
                    Settings.OnlyFace = _segFace.SelectedIndex == 0 ? Math.Max(0, Settings.PickedFace) : -1;
                    _faceHint.Text = FaceHint();
                    Raise();
                };
                scope.Controls.Add(_segFace);

                _faceHint.SetBounds(12, 58, W - 48, 18);
                _faceHint.ForeColor = Theme.InkFaint;
                _faceHint.Font = Theme.Small;
                _faceHint.Text = FaceHint();
                scope.Controls.Add(_faceHint);
                if (showScope) y = scope.Bottom + 8;
            }

            // 阵列方式（图标 + 文字图块）
            var cArray = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "阵列方式" };
            AddScrollCard(cArray);
            int ay = 26;
            AddTiles(cArray, HalftoneSettings.ArrayNames, TileArrayNames, Settings.ArrayMode,
                     3, 100, 36, i => TileIcons.ArrayIcon(i, 26), ref ay, v =>
                     {
                         int prev = Settings.ArrayMode;
                         Settings.ArrayMode = v;
                         // 同心环 / 螺旋 / 抖动：进入这类模式时让用户点一个圆心，图案从圆心向外扩散
                         bool centerBased = (v == 3 || v == 4 || v == 5);
                         bool prevCenterBased = (prev == 3 || prev == 4 || prev == 5);
                         if (centerBased && (!prevCenterBased || !CenterPicked)) RequestPickCenter();
                     });
            cArray.Height = ay + 6;
            y = cArray.Bottom + 8;

            // 图形形状（图标 + 文字图块）
            var cShape = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "图形形状" };
            AddScrollCard(cShape);
            int sy = 26;
            AddTiles(cShape, HalftoneSettings.ShapeNames, TileShapeNames, Settings.Shape,
                     4, 74, 36, i => TileIcons.ShapeIcon(i, 26), ref sy, v => Settings.Shape = v);
            cShape.Height = sy + 6;
            y = cShape.Bottom + 8;

            // 阵列圆心（同心环/螺旋/抖动 由它决定扩散中心；其它模式决定渐变起点）
            var cCenter = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "阵列圆心" };
            AddScrollCard(cCenter);

            _pickCenter = new FlatButton { Text = "选择圆心", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 96, Height = 26 };
            _pickCenter.SetBounds(12, 26, 96, 26);
            _pickCenter.Click += (s, e) => RequestPickCenter();
            cCenter.Controls.Add(_pickCenter);

            var useFace = new FlatButton { Text = "用曲面中心", Style = BtnStyle.Ghost, Width = 108, Height = 26 };
            useFace.SetBounds(116, 26, 108, 26);
            useFace.Click += (s, e) =>
            {
                Settings.CenterPoints = null;
                Settings.UsePickedCenter = false;
                CenterPicked = false;
                SetCenterState(false);
                UpdateCenterInfo();
                Raise();
            };
            cCenter.Controls.Add(useFace);

            _centerInfo.SetBounds(12, 54, W - 48, 18);
            _centerInfo.ForeColor = Theme.InkFaint;
            _centerInfo.Font = Theme.Small;
            cCenter.Controls.Add(_centerInfo);
            CenterPicked = Settings.CenterPoints != null && Settings.CenterPoints.Count > 0;
            SetCenterState(CenterPicked);
            UpdateCenterInfo();
            cCenter.Height = 78;
            y = cCenter.Bottom + 8;

            // 命令里选过起点物件时，保留「是否启用」开关
            if (Settings.CenterPoints != null && Settings.CenterPoints.Count > 0)
            {
                var cSrc = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 58, Title = "渐变起点" };
            AddScrollCard(cSrc);
                var cbSrc = new IvanCheck
                {
                    Text = "从指定物件开始渐变（不勾 = 从曲面中心）",
                    Checked = Settings.UsePickedCenter
                };
                cbSrc.SetBounds(12, 30, W - 48, 22);
                cbSrc.CheckedChanged += (s, e) => { Settings.UsePickedCenter = cbSrc.Checked; Raise(); };
                cSrc.Controls.Add(cbSrc);
                y = cSrc.Bottom + 8;
            }

            var opt = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 58, Title = "预览选项" };
            AddScrollCard(opt);
            _live.Text = "实时预览";
            _live.Checked = true;
            _live.SetBounds(12, 30, 140, 22);
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

            var header = new HeaderBand("参数化阵列纹理", target, "halftone");
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

        void RequestPickCenter()
        {
            EventHandler h = PickCenterRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>拾取成功后由会话回调，更新圆心显示</summary>
        public void MarkCenterPicked(Point3d p)
        {
            CenterPicked = true;
            Settings.UsePickedCenter = true;
            try { _centerInfo.Text = string.Format("圆心：({0:0.###}, {1:0.###}, {2:0.###})", p.X, p.Y, p.Z); }
            catch { }
        }

        void UpdateCenterInfo()
        {
            _centerInfo.Text = CenterPicked ? "圆心：已指定（点阵从圆心向外扩散）" : "圆心：曲面中心（同心环/螺旋/抖动 建议点选圆心）";
        }

        /// <summary>图标 + 文字的图块选择器（阵列方式 / 图形形状）；y 从卡片标题下方开始，返回卡片内容高度</summary>
        void AddTiles(CardPanel card, string[] fullNames, string[] tileNames, int selected, int perRow,
                      int tileW, int tileH, Func<int, Bitmap> iconMaker, ref int y, Action<int> onPick)
        {
            var tip = new ToolTip();
            var tiles = new List<IvanTile>();
            for (int i = 0; i < fullNames.Length; i++)
            {
                int idx = i;
                int col = i % perRow, row = i / perRow;
                var tb = new IvanTile(idx, tileNames[Math.Min(i, tileNames.Length - 1)], iconMaker(i));
                tip.SetToolTip(tb, fullNames[i]);
                tb.SetBounds(12 + col * (tileW + 4), y + row * (tileH + 4), tileW, tileH);
                tb.Selected = (i == selected);
                tb.Picked += (s, e) =>
                {
                    foreach (IvanTile t in tiles) { t.Selected = (t.Index == idx); t.Invalidate(); }
                    onPick(idx);
                    Raise();
                };
                tiles.Add(tb);
                card.Controls.Add(tb);
            }
            int rows = (fullNames.Length + perRow - 1) / perRow;
            y += rows * (tileH + 4) + 4;
        }

        /// <summary>一行参数：滑块覆盖常用范围，超出用手动输入（上限 hardMax）</summary>
        void AddRow(CardPanel card, string name, string unit, double min, double sliderMax, double hardMax,
                    double step, double value, ref int y, Action<double> setter)
        {
            int decimals = step >= 1.0 ? 0 : (step >= 0.1 ? 1 : 2);

            card.Controls.Add(new Label
            {
                Text = name + unit,
                Left = 10, Top = y + 5, Width = 84, Height = 18,
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

            y += 30;
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

        /// <summary>顶部「选择渐变物件」按钮的状态：未选 = 红、已选 = 绿</summary>
        public void SetGradientState(bool picked)
        {
            try { if (_pickGrad != null) _pickGrad.Picked = picked; } catch { }
        }

        /// <summary>「选择圆心」按钮的状态：没有明确圆心（退回曲面中心）= 红、有圆心 = 绿</summary>
        public void SetCenterState(bool picked)
        {
            try { if (_pickCenter != null) _pickCenter.Picked = picked; } catch { }
        }

        /// <summary>更新「阵列圆心」的说明（选渐变物件后显示是哪个物件）</summary>
        public void SetCenterInfo(string desc)
        {
            try
            {
                if (_centerInfo == null) return;
                _centerInfo.Text = string.IsNullOrEmpty(desc)
                    ? "圆心：曲面中心（同心环/螺旋/抖动 建议点选圆心）"
                    : ("圆心：渐变物件 " + desc);
            }
            catch { }
        }

        /// <summary>换参考物件后刷新「参考面」控件的可用状态与提示</summary>
        public void RefreshScope()
        {
            try
            {
                if (_segFace != null)
                {
                    _segFace.Enabled = Settings.FaceCount > 1;
                    _segFace.SelectedIndex = Settings.OnlyFace >= 0 ? 0 : 1;
                    // 卡片整体跟着面数显示/隐藏（单面对象不需要这个选项）
                    if (_segFace.Parent != null) _segFace.Parent.Visible = Settings.FaceCount > 1;
                }
                if (_faceHint != null) _faceHint.Text = FaceHint();
            }
            catch { }
        }

        string FaceHint()
        {
            if (Settings.FaceCount <= 1) return "当前对象只有 1 个面，两种设置效果相同";
            return Settings.OnlyFace >= 0
                ? string.Format("只生成点选的那一个面（第 {0} 面）", Settings.OnlyFace + 1)
                : string.Format("生成整个多重曲面的所有面（{0} 个面）", Settings.FaceCount);
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

    /// <summary>图块小图标：阵列方式 / 图形形状的缩略示意</summary>
    internal static class TileIcons
    {
        static readonly Color Ink = Color.FromArgb(0, 118, 190);

        public static Bitmap ArrayIcon(int mode, int size)
        {
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var br = new SolidBrush(Ink))
                {
                    float m = size * 0.14f, w = size - 2 * m, r = size * 0.062f;
                    switch (mode)
                    {
                        case 0:
                            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++)
                                Dot(g, br, m + i * w / 3f, m + j * w / 3f, r);
                            break;
                        case 1:
                            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++)
                                Dot(g, br, m + i * w / 3f + ((j & 1) == 1 ? w / 6f : 0f), m + j * w / 3f, r);
                            break;
                        case 2:
                            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++)
                                Dot(g, br, m + i * w / 3f + ((j & 1) == 1 ? w / 6f : 0f), m + j * w / 3f * 0.88f + size * 0.07f, r * 0.92f);
                            break;
                        case 3:
                            Dot(g, br, size / 2f, size / 2f, r);
                            for (int k = 1; k <= 2; k++)
                            {
                                float rr = k * w / 2.5f; int n = 6 * k;
                                for (int q = 0; q < n; q++)
                                {
                                    double a = 2 * Math.PI * q / n + (k - 1) * 0.35;
                                    Dot(g, br, size / 2f + rr * (float)Math.Cos(a), size / 2f + rr * (float)Math.Sin(a), r * 0.85f);
                                }
                            }
                            break;
                        case 4:
                            for (int t = 0; t < 24; t++)
                            {
                                double rr = Math.Sqrt(t + 0.5) * w / 10.5;
                                double a = t * 2.399963;
                                Dot(g, br, size / 2f + (float)(rr * Math.Cos(a)), size / 2f + (float)(rr * Math.Sin(a)), r * 0.8f);
                            }
                            break;
                        default:
                            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++)
                            {
                                float jx = (float)(Fract(Math.Sin(i * 12.9898 + j * 78.233) * 43758.5453) - 0.5) * w * 0.3f;
                                float jy = (float)(Fract(Math.Sin(i * 39.3468 + j * 11.135) * 24634.6345) - 0.5) * w * 0.3f;
                                Dot(g, br, m + i * w / 3f + jx, m + j * w / 3f + jy, r);
                            }
                            break;
                    }
                }
            }
            return bmp;
        }

        public static Bitmap ShapeIcon(int shape, int size)
        {
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var br = new SolidBrush(Ink))
                {
                    float cx = size / 2f, cy = size / 2f, rr = size * 0.33f;
                    switch (shape)
                    {
                        case 0: g.FillEllipse(br, cx - rr, cy - rr, rr * 2, rr * 2); break;
                        case 1: FillPoly(g, br, cx, cy, rr, 3, Math.PI / 2); break;
                        case 2: FillPoly(g, br, cx, cy, rr * 1.02f, 4, Math.PI / 4); break;
                        default: FillPoly(g, br, cx, cy, rr, 6, Math.PI / 6); break;
                    }
                }
            }
            return bmp;
        }

        static void Dot(Graphics g, Brush br, float x, float y, float r)
        {
            g.FillEllipse(br, x - r, y - r, r * 2, r * 2);
        }

        static void FillPoly(Graphics g, Brush br, float cx, float cy, float r, int n, double baseAng)
        {
            var pts = new PointF[n];
            for (int k = 0; k < n; k++)
            {
                double a = baseAng + k * 2 * Math.PI / n;
                pts[k] = new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a)));
            }
            g.FillPolygon(br, pts);
        }

        static double Fract(double v) { return v - Math.Floor(v); }
    }
}
