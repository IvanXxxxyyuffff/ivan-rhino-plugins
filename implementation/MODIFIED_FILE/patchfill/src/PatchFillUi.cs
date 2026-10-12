using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IvanUi;
using Rhino.Display;
using Rhino.Geometry;

namespace PatchFillPattern
{
    /// <summary>
    /// 实时预览（预览 = 输出）：半透明着色结果面 + 线框；原边界画红线；
    /// 内部约束点画成小十字，方便肉眼确认「内部约束有没有被贴合」。
    /// </summary>
    public class PatchFillPreviewConduit : DisplayConduit
    {
        public List<Brep> Shaded;                  // 结果面
        public List<Polyline> SourceBoundary;      // 原边界（红线）
        public List<Point3d> InnerPoints;          // 内部约束采样点（青点）
        public bool ShowSourceBoundary = true;
        public bool ShowWires = true;
        public bool Zebra = false;                 // 斑马纹预览（XNURBS 对齐，2026-10-12）
        static readonly Color WireCol = Color.FromArgb(210, 96, 30, 12);
        static readonly Color FaceCol = Color.FromArgb(150, 240, 176, 140);
        static readonly Color SrcCol = Color.FromArgb(225, 205, 64, 64);
        static readonly Color InnerCol = Color.FromArgb(225, 22, 132, 152);

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
                        if (Zebra)
                        {
                            // 斑马纹：结果面网格化 → 顶点按「法向 vs 视向」的反射角映射黑白条纹 →
                            // 顶点色网格绘制。条纹在曲率突变处会断/扭，用来目检 G1/G2 光顺度。
                            Mesh zm = null;
                            try
                            {
                                var mp = MeshingParameters.Default;
                                mp.MinimumTolerance = 0.05;
                                Mesh[] ms = Mesh.CreateFromBrep(b, mp);
                                if (ms != null && ms.Length > 0)
                                {
                                    zm = new Mesh();
                                    foreach (Mesh m1 in ms) if (m1 != null) zm.Append(m1);
                                }
                            }
                            catch { zm = null; }
                            if (zm != null && zm.Vertices.Count > 0)
                            {
                                Point3d cam = e.Viewport.CameraLocation;
                                zm.Normals.ComputeNormals();
                                zm.VertexColors.Clear();
                                for (int vi = 0; vi < zm.Vertices.Count; vi++)
                                {
                                    Point3d vp = zm.Vertices[vi];
                                    Vector3d n = zm.Normals[vi];
                                    Vector3d dir = cam - vp;
                                    if (dir.Length > 1e-9) dir.Unitize();
                                    double refl = n.IsValid ? (n * dir) : 0.0;           // 反射角余弦
                                    byte c = Math.Sin(refl * 26.0) >= 0 ? (byte)245 : (byte)30;
                                    zm.VertexColors.Add(Color.FromArgb(c, c, c));
                                }
                                var mat = new DisplayMaterial(Color.White, 0.0);
                                e.Display.DrawMeshShaded(zm, mat);
                            }
                            else
                            {
                                e.Display.DrawBrepShaded(b, new DisplayMaterial(FaceCol, 0.35));
                            }
                        }
                        else
                        {
                            e.Display.DrawBrepShaded(b, new DisplayMaterial(FaceCol, 0.35));
                        }
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
                if (InnerPoints != null)
                {
                    for (int i = 0; i < InnerPoints.Count; i++)
                        e.Display.DrawPoint(InnerPoints[i], PointStyle.Simple, 3, InnerCol);
                }
            }
            catch { }
        }
    }

    /// <summary>多边补面参数面板：边界 + 补面参数 + 边界连续性 + 输出与预览</summary>
    public class PatchFillPanel : Form
    {
        public PatchFillSettings Settings { get; private set; }
        public bool Committed { get; private set; }
        public bool LivePreview { get { return _live.Checked; } }
        public event EventHandler ValueChanged;
        public event EventHandler PickRequested;          // 选边界（旧按钮，保留兼容）
        public event EventHandler InnerPickRequested;     // 选内部曲线/点（旧按钮，保留兼容）
        public event EventHandler PickAllRequested;       // XNURBS 式「选择曲线和点」一次拾取（2026-10-12）
        public event EventHandler ClearRequested;         // 清空全部约束

        readonly InfoBar _info = new InfoBar();
        readonly IvanCheck _live = new IvanCheck();
        readonly List<CardPanel> _scrollCards = new List<CardPanel>();
        readonly List<int> _scrollCardTops = new List<int>();
        VScrollBar _scrollBar;
        int _scrollBodyTop, _scrollFooterHeight, _scrollContentHeight;
        bool _scrollSync, _scrollFocusHooked;
        HeaderBand _header;
        bool _sync;
        bool _closing;

        const int W = 434;

        ListBox _consList;                 // 约束列表（每行一条：边界/内部）
        Label _consDesc;                   // 约束摘要（XNURBS 式列表下方一行）
        FlatButton _pickAll;
        bool _hasBoundary, _hasInner;      // 未选状态（PickState/InnerPickState 语义来源）
        IvanSegmented _seg;
        List<Control> _gridRows, _g1Rows;
        List<Control> _g0AccRows, _g1AccRows, _qualityRows, _flatRows;
        IvanCheck _srcCheck, _applyAllCheck, _accCheck, _quadCheck, _trimCheck, _zebraCheck, _edgeCheck;
        ComboBox _uvCombo;
        Label _g1Hint;

        public PatchFillPanel(PatchFillSettings settings, string targetDesc)
        {
            Settings = settings.Clone();

            Text = "多边补面 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("patchfill", 16); } catch { }

            int y = 0;

            // ---- 约束（XNURBS 式：约束列表 + 「选择曲线和点」一次拾取）
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 152, Title = "约束" };
                AddScrollCard(card);

                _consList = new ListBox
                {
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = Theme.Body,
                    ForeColor = Theme.Ink,
                    BackColor = Color.White,
                    IntegralHeight = false
                };
                _consList.SetBounds(12, 26, W - 48, 66);
                card.Controls.Add(_consList);

                _pickAll = new FlatButton { Text = "选择曲线和点", Style = BtnStyle.Primary, Width = 132, Height = 28, Left = 12, Top = 98 };
                _pickAll.Click += (s, e) => { EventHandler h = PickAllRequested; if (h != null) h(this, EventArgs.Empty); };
                card.Controls.Add(_pickAll);

                var clearBtn = new FlatButton { Text = "清空", Style = BtnStyle.Secondary, Width = 60, Height = 28, Left = 152, Top = 98 };
                clearBtn.Click += (s, e) => { EventHandler h = ClearRequested; if (h != null) h(this, EventArgs.Empty); };
                card.Controls.Add(clearBtn);

                _consDesc = new Label();
                _consDesc.SetBounds(220, 100, W - 240, 24);
                _consDesc.ForeColor = Theme.InkFaint;
                _consDesc.Font = Theme.Small;
                _consDesc.Text = targetDesc;
                card.Controls.Add(_consDesc);

                y = card.Bottom + 8;
            }

            // ---- 补面参数（XNURBS 四滑杆：G0 精确度 / G1 精确度 / 质量控制 / 平坦控制）
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 240, Title = "补面参数" };
                AddScrollCard(card);
                int cy = 26;

                _g0AccRows = AddRowEx(card, "G0 精确度", "", 0.0, 1.0, 1.0, 0.01, Settings.G0Accuracy, ref cy, v =>
                {
                    Settings.G0Accuracy = v;   // → 边界行权重 1e6·10^(5(v-0.5))
                });
                _g1AccRows = AddRowEx(card, "G1 精确度", "", 0.0, 1.0, 1.0, 0.01, Settings.G1Accuracy, ref cy, v =>
                {
                    Settings.G1Accuracy = v;   // → 连续性带权重 300·10^(3(v-0.5))
                    if (_seg != null && _seg.SelectedIndex == 0) _seg.SelectedIndex = 1;   // 调它自动切 G1
                });
                _qualityRows = AddRowEx(card, "质量控制", "", 0.0, 1.0, 1.0, 0.01, Settings.QualityControl, ref cy, v =>
                {
                    Settings.QualityControl = v;
                    Settings.Smooth = 0.05 + 0.4 * v;   // → 薄板能量 λ（v=0.25 → 0.15，与旧默认一致）
                });
                _flatRows = AddRowEx(card, "平坦控制", "", 0.0, 1.0, 1.0, 0.01, Settings.FlatControl, ref cy, v =>
                {
                    Settings.FlatControl = v;
                    Settings.FitStrength = 1.0 - v;     // 高 = 更平（不贴参考），低 = 更贴相邻曲面（v=0 → 1，与旧默认一致）
                });

                _gridRows = AddRowEx(card, "控制点数", "", 4.0, 24.0, 40.0, 1.0, Settings.ControlCount, ref cy, v => Settings.ControlCount = (int)Math.Round(v));
                _g1Rows = AddRowEx(card, "G1 采样距离", "mm", 0.0, 20.0, 100000.0, 0.1, Settings.G1Distance, ref cy, v =>
                {
                    Settings.G1Distance = v;
                    if (_seg != null && _seg.SelectedIndex == 0) _seg.SelectedIndex = 1;
                });

                var fh = new Label();
                fh.SetBounds(12, cy, W - 48, 40);
                fh.ForeColor = Theme.InkFaint;
                fh.Font = Theme.Small;
                fh.Text = "G0/G1 精确度 = 边界与相切约束的权重强度；质量控制 = 曲面光顺能量；平坦控制 = 面更平（高）还是更贴相邻曲面（低）";
                card.Controls.Add(fh);
                cy += 44;

                card.Height = cy + 6;
                y = card.Bottom + 8;
            }

            // ---- 边界连续性
            {
                var card = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 118, Title = "边界连续性" };
                AddScrollCard(card);

                var seg = new IvanSegmented();
                seg.SetItems(new string[] { "G0 位置", "G1 相切", "G2 曲率" });
                seg.SelectedIndex = Settings.Continuity;
                seg.SetBounds(12, 26, W - 48, 28);
                seg.SelectedChanged += (s, e) =>
                {
                    Settings.Continuity = seg.SelectedIndex;
                    if (_g1Hint != null) _g1Hint.Text = ContinuityHint();
                    Raise();
                };
                card.Controls.Add(seg);
                _seg = seg;

                _g1Hint = new Label();
                _g1Hint.SetBounds(12, 58, W - 48, 44);
                _g1Hint.ForeColor = Theme.InkFaint;
                _g1Hint.Font = Theme.Small;
                _g1Hint.Text = ContinuityHint();
                card.Controls.Add(_g1Hint);

                y = card.Bottom + 8;
            }

            // ---- 选项（XNURBS 全套复选框 + UV 方向；2026-10-12 合并「XNURBS 对齐」与「输出与预览」）
            {
                var opt = new CardPanel { Left = 12, Top = y, Width = W - 24, Height = 258, Title = "选项" };
                AddScrollCard(opt);
                int oy = 26;

                _edgeCheck = new IvanCheck { Text = "边缘约束（结果面边界受所选边缘约束）", Checked = Settings.EdgeConstraint };
                _edgeCheck.SetBounds(12, oy, W - 48, 22);
                _edgeCheck.CheckedChanged += (s, e) => { Settings.EdgeConstraint = _edgeCheck.Checked; Raise(); };
                opt.Controls.Add(_edgeCheck);
                oy += 26;

                _applyAllCheck = new IvanCheck { Text = "将连续性应用于所有边界（关 = 只有外边界，内边界退 G0）", Checked = Settings.ApplyAllBoundaries };
                _applyAllCheck.SetBounds(12, oy, W - 48, 22);
                _applyAllCheck.CheckedChanged += (s, e) => { Settings.ApplyAllBoundaries = _applyAllCheck.Checked; Raise(); };
                opt.Controls.Add(_applyAllCheck);
                oy += 26;

                _quadCheck = new IvanCheck { Text = "优化四边面（强制 4 角切分；不勾 = 自动判盘状）", Checked = Settings.OptimizeQuad };
                _quadCheck.SetBounds(12, oy, W - 48, 22);
                _quadCheck.CheckedChanged += (s, e) => { Settings.OptimizeQuad = _quadCheck.Checked; Raise(); };
                opt.Controls.Add(_quadCheck);
                oy += 26;

                _trimCheck = new IvanCheck { Text = "修剪生成的曲面（按边界环精确修剪结果面）", Checked = Settings.TrimResult };
                _trimCheck.SetBounds(12, oy, W - 48, 22);
                _trimCheck.CheckedChanged += (s, e) => { Settings.TrimResult = _trimCheck.Checked; Raise(); };
                opt.Controls.Add(_trimCheck);
                oy += 26;

                _accCheck = new IvanCheck { Text = "满足精度要求（公差按文档绝对公差自动派生）", Checked = Settings.MeetAccuracy };
                _accCheck.SetBounds(12, oy, W - 48, 22);
                _accCheck.CheckedChanged += (s, e) => { Settings.MeetAccuracy = _accCheck.Checked; Raise(); };
                opt.Controls.Add(_accCheck);
                oy += 26;

                _live.Text = "显示预览";
                _live.Checked = true;
                _live.SetBounds(12, oy, 150, 22);
                _live.CheckedChanged += (s, e) => Raise();
                opt.Controls.Add(_live);

                _zebraCheck = new IvanCheck { Text = "斑马纹预览", Checked = Settings.ZebraPreview };
                _zebraCheck.SetBounds(168, oy, 150, 22);
                _zebraCheck.CheckedChanged += (s, e) => { Settings.ZebraPreview = _zebraCheck.Checked; Raise(); };
                opt.Controls.Add(_zebraCheck);
                oy += 26;

                _srcCheck = new IvanCheck { Text = "显示原边界（红线）", Checked = Settings.ShowSourceBoundary };
                _srcCheck.SetBounds(12, oy, W - 48, 22);
                _srcCheck.CheckedChanged += (s, e) => { Settings.ShowSourceBoundary = _srcCheck.Checked; Raise(); };
                opt.Controls.Add(_srcCheck);
                oy += 28;

                var uvLab = new Label { Text = "UV 方向:", ForeColor = Theme.Ink, Font = Theme.Body };
                uvLab.SetBounds(12, oy, 70, 22);
                opt.Controls.Add(uvLab);
                _uvCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
                _uvCombo.Items.AddRange(new object[] { "曲线法线", "自动" });
                _uvCombo.SelectedIndex = Settings.UvDirection;
                _uvCombo.SetBounds(88, oy - 2, 120, 24);
                _uvCombo.SelectedIndexChanged += (s, e) => { Settings.UvDirection = _uvCombo.SelectedIndex; Raise(); };
                opt.Controls.Add(_uvCombo);
                oy += 28;

                opt.Height = oy + 6;
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

            _header = new HeaderBand("多边补面", targetDesc, "patchfill");
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

        string ContinuityHint()
        {
            if (Settings.Continuity == 2)
                return "G2：在 G1 相切之上，再把边界外第二圈钉到相邻曲面的真实曲率延续（双环位置 + 二阶差分行）；独立曲线没有相邻面 → 退化成 G0";
            if (Settings.Continuity == 1)
                return "G1：边界来自曲面边时，用相邻面法向构造切线带（沿切平面向洞内偏移「G1 采样距离」采样）；独立曲线没有相邻面 → 只能退化成 G0";
            return "G0：只要位置连续（结果面的边精确过原边界）。选 G1 可让结果面与相邻面相切，选 G2 连曲率也一致";
        }

        // 纯显示格式：不改存储值 / 范围 / 步长 / 回调
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

        // ---- 供自检切换参数（走的是和用户拖滑杆/敲数字框完全一样的链路）
        public void SetContinuity(int mode) { if (_seg != null) _seg.SelectedIndex = mode; }
        public int ContinuityIndex { get { return _seg != null ? _seg.SelectedIndex : -1; } }
        public void SetApplyAllBoundaries(bool on) { if (_applyAllCheck != null) _applyAllCheck.Checked = on; }
        public bool ApplyAllBoundariesChecked { get { return _applyAllCheck != null && _applyAllCheck.Checked; } }
        public void SetMeetAccuracy(bool on) { if (_accCheck != null) _accCheck.Checked = on; }
        public bool MeetAccuracyChecked { get { return _accCheck != null && _accCheck.Checked; } }
        public void SetOptimizeQuad(bool on) { if (_quadCheck != null) _quadCheck.Checked = on; }
        public bool OptimizeQuadChecked { get { return _quadCheck != null && _quadCheck.Checked; } }
        public void SetUvDirection(int i) { if (_uvCombo != null && i >= 0 && i < _uvCombo.Items.Count) _uvCombo.SelectedIndex = i; }
        public int UvDirectionIndex { get { return _uvCombo != null ? _uvCombo.SelectedIndex : -1; } }
        public void SetShowSourceBoundary(bool on) { if (_srcCheck != null) _srcCheck.Checked = on; }
        public bool ShowSourceBoundaryChecked { get { return _srcCheck != null && _srcCheck.Checked; } }

        /// <summary>边界拾取按钮状态（true = 绿/可用，false = 红）</summary>
        public bool PickState { get { return _hasBoundary; } }
        public bool InnerPickState { get { return _hasInner; } }

        /// <summary>参数行当前是否可调（自检用）：0 G0精确度 / 1 G1精确度 / 2 质量控制 / 3 平坦控制 / 4 控制点数 / 5 G1采样距离</summary>
        public bool RowEnabled(int which)
        {
            List<Control> rows = null;
            switch (which)
            {
                case 0: rows = _g0AccRows; break;
                case 1: rows = _g1AccRows; break;
                case 2: rows = _qualityRows; break;
                case 3: rows = _flatRows; break;
                case 4: rows = _gridRows; break;
                case 5: rows = _g1Rows; break;
            }
            if (rows == null || rows.Count == 0) return false;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && !rows[i].Enabled) return false;
            return true;
        }

        /// <summary>按行名设置数值（与用户操作同一条链路）</summary>
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
                if (_consDesc != null) _consDesc.Text = desc;
            }
            catch { }
        }

        /// <summary>约束列表逐行刷新（XNURBS 式列表；行文本以「边界」/「内部」开头用于未选状态判断）</summary>
        public void SetConstraintRows(string[] rows)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke((Action)(() => SetConstraintRows(rows))); return; }
                if (_consList == null) return;
                _consList.BeginUpdate();
                _consList.Items.Clear();
                bool hb = false, hi = false;
                if (rows != null)
                {
                    foreach (string r0 in rows)
                    {
                        if (string.IsNullOrEmpty(r0)) continue;
                        _consList.Items.Add(r0);
                        if (r0.StartsWith("边界", StringComparison.Ordinal)) hb = true;
                        else hi = true;
                    }
                }
                _consList.EndUpdate();
                _hasBoundary = hb; _hasInner = hi;
            }
            catch { }
        }

        /// <summary>边界拾取按钮状态：没选 / 选错 / 被删 = 红；选到可用边界 = 绿</summary>
        public void SetEdgeState(bool ok)
        {
            try
            {
                if (_consDesc != null) _consDesc.ForeColor = ok ? Theme.Ink : Theme.InkFaint;
            }
            catch { }
        }

        public void SetInnerState(bool ok)
        {
            try { _hasInner = ok; } catch { }
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
