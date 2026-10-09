using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using IvanUi;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using VapeVolume.Core;

namespace VapeVolume.UI
{
    /// <summary>
    /// 烟油容量窗口（非模态，WinForms + 与另外四个插件共用的 IvanUi 自绘控件）。
    ///   · 打开即自动在模型上生成两个活标注：容量、长宽高；
    ///   · 换参数时标注与视口里的雾化芯预览实时跟着变；
    ///   · 转换率默认按「最终算出来的容量」反推档位；
    ///   · 双击模型上的标注可以重新打开这个窗口继续调。
    /// 计算 / 标注同步 / 拾取命令入口与原 Eto 版逐行一致，本次只把界面控件换成同一套 Ivan 控件家族。
    /// </summary>
    public sealed class VolumeDialog : Form
    {
        const int W = 434;              // 客户区宽（与条纹 / 阵列 / 圆点 / 泰森一致）
        const int InnerW = W - 48;      // 卡片内容宽：卡片 Left/Right 各 12 边距 + 卡片内 12 留白

        static VolumeDialog _current;

        readonly RhinoDoc _doc;
        Guid _sourceId;                    // 可变：窗口上「选择物件」可以换目标
        readonly VolumeSettings _st;
        readonly UnitContext _units;
        readonly MeshingParameters _mp;

        // 物件在拖拽/缩放的瞬间会被 Rhino 换掉实例，所以按 Id 重新取，不长期持有旧实例
        RhinoObject _obj;
        VolumeReport _rep;
        string _lastGeomKey = "";

        // ── 同一套 Ivan 控件家族（PanelTheme.cs / IvanUi） ──
        readonly InfoBar _info = new InfoBar();            // 状态条：原 _note 文案走它
        readonly Label _value = new Label();               // 容量大字
        readonly Label _formula = new Label();             // 算式明细
        readonly Label _rateHint = new Label();            // 转换率推荐说明
        readonly Label _detail = new Label();              // 材料/壳体明细
        readonly Label _coilInfo = new Label();            // 雾化芯扣除说明
        FlatButton _pick;                                  // 选择物件（未选红 / 已选绿）
        CardPanel _pickCard;
        IvanSlider _rateBar, _wallBar;
        NumericUpDown _rateNum, _wallNum, _coilNum;
        IvanSegmented _segMode, _segAxis;
        IvanCheck _rateAuto, _coilOn;
        HeaderBand _header;
        bool _sync;                                        // 数值行「滑杆 ↔ 数字框」互写保护
        bool _closing;

        double _baseMl;
        double _netMl;
        double _coilMl;
        bool _approximate;
        bool _busy;

        // 目标物件被删除的复查：删除事件 250ms 后复查 Id —— Move / Transform 常常是「先删旧对象、再加新对象」，
        // 立刻当删除处理会把移动误报成删除
        readonly Timer _recheck = new Timer();
        Guid _recheckId = Guid.Empty;
        bool _subscribedDocEvents;

        Brep _liquidSolid;

        string _cupKey = "";
        CupResult _cup;
        string _coilKey = "";
        CoilResult _coil;
        LiveSpec _syncedSpec;

        /// <summary>当前打开的容量窗口（没有则为 null）。</summary>
        public static VolumeDialog Current
        {
            get { return _current; }
        }

        /// <summary>
        /// 物件几何变了（拖拽、缩放、改控制点…）时由空闲刷新器调用：
        /// 重新取物件、重算体积与扣壁厚，视口预览与标注随之更新。
        /// 用几何指纹挡掉没必要的重算。
        /// </summary>
        public void RefreshFromDocument()
        {
            try
            {
                if (_doc == null || _sourceId == Guid.Empty) return;

                RhinoObject obj = _doc.Objects.FindId(_sourceId);
                if (obj == null || obj.IsDeleted) return;

                string key = GeometryKey(obj);
                if (key == _lastGeomKey) return;
                _lastGeomKey = key;

                _obj = obj;

                VolumeReport rep = VolumeEngine.Compute(_obj, _units, _mp);
                if (rep == null || !rep.Ok) return;
                _rep = rep;

                // 几何变了，缓存必须全清，否则会拿旧的扣壁厚结果
                _cupKey = "";
                _cup = null;
                _coilKey = "";
                _coil = null;

                Recalculate();
                try { _doc.Views.Redraw(); } catch { }
            }
            catch
            {
                // 刷新失败不影响使用
            }
        }

        static string GeometryKey(RhinoObject obj)
        {
            try
            {
                uint serial = 0;
                try { serial = obj.RuntimeSerialNumber; } catch { serial = 0; }

                BoundingBox bb = obj.Geometry != null ? obj.Geometry.GetBoundingBox(true) : BoundingBox.Unset;
                if (!bb.IsValid) return serial.ToString(CultureInfo.InvariantCulture);

                Point3d c = bb.Center;
                return string.Format(CultureInfo.InvariantCulture, "{0};{1:F5},{2:F5},{3:F5};{4:F5}",
                    serial, c.X, c.Y, c.Z, bb.Diagonal.Length);
            }
            catch
            {
                return "?";
            }
        }

        // ───────────────────── 目标被删除的复查 ─────────────────────

        /// <summary>
        /// 删除事件不能立刻当「真删了」：Move / Transform 这类操作在 Rhino 里常常是
        /// 「先删旧对象、再加新对象」，先记下 Id，250ms 后复查一次再决定。
        /// </summary>
        void OnDeleteObject(object sender, RhinoObjectEventArgs e)
        {
            try
            {
                if (_closing || _sourceId == Guid.Empty) return;
                // 物件 Id 是全局唯一 GUID，直接按 Id 匹配即可（RhinoObjectEventArgs 没有 Document 属性）
                if (e.ObjectId != _sourceId) return;

                _recheckId = e.ObjectId;
                _recheck.Stop();
                _recheck.Start();
            }
            catch
            {
                // 删除事件处理失败不影响 Rhino
            }
        }

        void RecheckTick()
        {
            try { _recheck.Stop(); } catch { }
            if (_closing) return;
            try
            {
                Guid id = _recheckId;
                _recheckId = Guid.Empty;
                if (id == Guid.Empty || _doc == null) return;

                RhinoObject obj = _doc.Objects.FindId(id);
                if (obj != null && !obj.IsDeleted)
                {
                    // 还在：只是移动 / 替换几何 —— 强制重算一次，颜色不动
                    _lastGeomKey = "";
                    RefreshFromDocument();
                    return;
                }

                // 真没了：清掉目标状态，按钮回红并提示重选
                _obj = null;
                _rep = null;
                _sourceId = Guid.Empty;
                _lastGeomKey = "";
                _cupKey = "";
                _cup = null;
                _coilKey = "";
                _coil = null;
                _syncedSpec = null;
                try { ViewportPreview.Hide(_doc); } catch { }
                Recalculate();          // 先把数值区清空（状态条这时会被写成空目标提示）
                MarkPickInvalid("目标物件已被删除，请点「选择物件」重选。");
                RhinoApp.WriteLine("目标物件已被删除，请点「选择物件」重选。");
            }
            catch
            {
                // 复查失败不影响使用
            }
        }

        // ───────────────────── 打开入口 ─────────────────────

        /// <summary>没有选择物件时的提示（面板头部与状态条共用）。</summary>
        public const string EmptyTargetHint = "未选择物件 —— 点「选择物件」按钮选目标（也可以直接在视图里点选后再点按钮）";

        /// <summary>从物件打开（命令点选对象时用）。</summary>
        public static void OpenFor(RhinoDoc doc, RhinoObject obj, VolumeReport rep, VolumeSettings st)
        {
            ShowInternal(doc, obj, rep, st, null);
        }

        /// <summary>
        /// 没有物件时打开（命令里没有预选物件时用）：窗口照常打开，显示「未选择物件」，
        /// 由窗口上的「选择物件」按钮（VapePickTarget 命令）再选。
        /// </summary>
        public static void OpenEmpty(RhinoDoc doc)
        {
            if (doc == null) return;
            VolumeSettings st;
            try { st = VolumeSettings.Load(); }
            catch { st = new VolumeSettings(); }
            ShowInternal(doc, null, null, st, null);
        }

        /// <summary>
        /// 「选择物件」按钮的状态：未选择 / 选到但算不出 / 目标被删除 = 红底白字，
        /// 选到且能算出结果 = 绿底白字。
        /// </summary>
        void SetPickButtonState(bool hasTarget)
        {
            try
            {
                if (_pick != null) _pick.Picked = hasTarget;
            }
            catch
            {
                // 换色失败不影响使用
            }
        }

        /// <summary>
        /// 拾取到的物件不可用（对象失效 / 没有几何 / 算不出体积）时由拾取命令调用：
        /// 按钮回红并把原因写进状态条 —— 按钮只有「选到且能算出结果」才允许是绿的。
        /// </summary>
        public void MarkPickInvalid(string why)
        {
            try
            {
                SetPickButtonState(false);
                SetInfo(string.IsNullOrEmpty(why) ? "拾取到的物件无法用于计算，请重新选择。" : why);
            }
            catch
            {
                // 状态提示失败不影响 Rhino
            }
        }

        /// <summary>换目标物件（窗口上的「选择物件」按钮 → VapePickTarget 命令）：重取报告、清缓存、刷新界面。</summary>
        public void SetTarget(RhinoDoc doc, RhinoObject obj)
        {
            if (obj == null) return;
            try
            {
                _obj = obj;
                _sourceId = obj.Id;
                _lastGeomKey = GeometryKey(obj);

                VolumeReport rep = null;
                try { rep = VolumeEngine.Compute(obj, _units, _mp); }
                catch (Exception ex) { RhinoApp.WriteLine("计算失败：" + ex.Message); }
                _rep = rep;

                // 能算出结果才算「选好了」：算不出体积的物件按钮保持红色，
                // 失败原因由 Recalculate 写进状态条（_rep.Error）
                SetPickButtonState(rep != null && rep.Ok);

                // 换了物件，缓存必须全清，否则会拿旧的扣壁厚 / 雾化芯结果
                _cupKey = "";
                _cup = null;
                _coilKey = "";
                _coil = null;
                _syncedSpec = null;

                if (_header != null) _header.SetSub(TargetTitle(obj, rep));
                Recalculate();
                try { _doc.Views.Redraw(); } catch { }

                if (rep != null && rep.Ok)
                    RhinoApp.WriteLine("目标物件已更新：{0}", rep.Name);
                else
                    RhinoApp.WriteLine("目标物件已更新，但无法计算容量：{0}", rep != null ? rep.Error : "未知原因");
            }
            catch (Exception ex)
            {
                // 意外失败时也保持红色：宁可让用户重选，也不要停在「绿但没结果」
                SetPickButtonState(false);
                RhinoApp.WriteLine("切换目标失败：" + ex.Message);
            }
        }

        static string TargetTitle(RhinoObject obj, VolumeReport rep)
        {
            if (obj == null) return "物件：未选择";
            return "物件：" + (rep != null && rep.Ok ? rep.Name : "（无法计算）");
        }

        /// <summary>从标注打开（双击标注时用，参数取标注上记录的）。</summary>
        public static void OpenFor(RhinoDoc doc, LiveSpec spec)
        {
            if (doc == null || spec == null) return;

            RhinoObject obj = doc.Objects.FindId(spec.SourceId);
            if (obj == null || obj.IsDeleted)
            {
                RhinoApp.WriteLine("这个标注的来源物件已经不在了。");
                return;
            }

            var st = new VolumeSettings
            {
                CupMode = spec.CupMode,
                WallMm = spec.WallMm,
                RateAuto = spec.RatePercent < 0,
                RatePercent = spec.RatePercent < 0 ? 65 : Fmt.Clamp(spec.RatePercent, 50, 75),
                CoilEnabled = spec.CoilOn,
                CoilDiameterMm = spec.CoilDiameterMm,
                CoilAxisIndex = spec.CoilAxisIndex,
                Decimals = spec.Decimals
            };

            UnitContext units = st.CreateUnitContext(doc);
            VolumeReport rep = VolumeEngine.Compute(obj, units, st.Meshing);

            ShowInternal(doc, obj, rep, st, spec);
        }

        static void ShowInternal(RhinoDoc doc, RhinoObject obj, VolumeReport rep, VolumeSettings st, LiveSpec spec)
        {
            try
            {
                if (_current != null)
                {
                    try { _current.Close(); } catch { }
                    _current = null;
                }

                var dlg = new VolumeDialog(doc, obj, rep, st);
                _current = dlg;
                dlg.FormClosed += (s, e) =>
                {
                    try { ViewportPreview.Hide(doc); } catch { }
                    if (ReferenceEquals(_current, dlg)) _current = null;
                };

                dlg.Show();          // 非模态：视口还能转、雾化芯预览能实时刷
                try { dlg.BringToFront(); } catch { }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("容量窗口打开失败：" + ex.Message);
            }
        }

        // ───────────────────── 构造（布局照 DESIGN-CONSISTENCY §3/§4，控件全部来自 IvanUi） ─────────────────────

        VolumeDialog(RhinoDoc doc, RhinoObject obj, VolumeReport rep, VolumeSettings st)
        {
            _doc = doc;
            _obj = obj;
            _sourceId = obj != null ? obj.Id : Guid.Empty;
            _lastGeomKey = obj != null ? GeometryKey(obj) : "";
            _rep = rep;
            _st = st;
            _units = st.CreateUnitContext(doc);
            _mp = st.Meshing;

            Text = "烟油容量 · 参数";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;              // 永远置顶：点 Rhino 视口也不会被顶到后面
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            try { Icon = Theme.MakeFormIcon("vape", 16); } catch { }

            // ── 首张固定「选择」卡片：Left=12 / Top=56 / 高 44，按钮固定 (12,8,140,28)，未选红 / 已选绿 ──
            _pickCard = new CardPanel { Left = 12, Top = 56, Width = W - 24, Height = 44 };
            Controls.Add(_pickCard);
            _pick = new FlatButton { Text = "选择物件", Style = BtnStyle.Secondary, PickStyle = true, Picked = false, Width = 140, Height = 28 };
            _pick.SetBounds(12, 8, 140, 28);
            _pick.Click += (s, e) =>
            {
                try { RhinoApp.RunScript("_-VapePickTarget", false); } catch { }
            };
            _pickCard.Controls.Add(_pick);
            SetPickButtonState(obj != null && rep != null && rep.Ok);   // 空目标 / 算不出体积 = 红底白字

            int y = 108;

            // ── 容量结果：数值大字 + 明细文案（文案照搬原版） ──
            var hero = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "容量结果" };
            Controls.Add(hero);

            var mark = new VapeMark();
            mark.SetBounds(12, 32, 36, 36);
            hero.Controls.Add(mark);

            StyleLabel(_value, Theme.Big, Theme.Ink, ContentAlignment.MiddleLeft);
            _value.Text = "—";
            _value.SetBounds(56, 30, InnerW - 44, 40);
            hero.Controls.Add(_value);

            StyleLabel(_formula, Theme.Small, Theme.InkSoft, ContentAlignment.TopLeft);
            _formula.SetBounds(12, 72, InnerW, 30);
            hero.Controls.Add(_formula);

            StyleLabel(_rateHint, Theme.Small, Theme.InkFaint, ContentAlignment.TopLeft);
            _rateHint.SetBounds(12, 104, InnerW, 18);
            hero.Controls.Add(_rateHint);

            StyleLabel(_detail, Theme.Small, Theme.InkSoft, ContentAlignment.TopLeft);
            _detail.SetBounds(12, 124, InnerW, 34);
            hero.Controls.Add(_detail);

            hero.Height = 166;
            y = hero.Bottom + 8;

            // ── 计算方式：原 RadioButtonList 两项按原顺序搬进 IvanSegmented；壁厚走数值行 ──
            var modeCard = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "计算方式" };
            Controls.Add(modeCard);

            _segMode = new IvanSegmented();
            _segMode.SetItems("油杯体积（扣壁厚后计算）", "油的体积（直接测量）");
            _segMode.SelectedIndex = st.CupMode ? 0 : 1;      // 先定初值再挂事件，避免构造期重算
            _segMode.SetBounds(12, 30, InnerW, 28);
            _segMode.SelectedChanged += (s, e) => Recalculate();
            modeCard.Controls.Add(_segMode);

            int modeY = 64;
            AddRow(modeCard, "壁厚", "mm", 0.0, 20.0, 0.05, 2, Math.Max(0.0, st.WallMm), ref modeY,
                   v => Recalculate(), out _wallBar, out _wallNum);
            modeCard.Height = modeY + 8;
            y = modeCard.Bottom + 8;

            // ── 转换率：滑块 50–75 / 步长 1（原范围与步长不变），自动推荐走 IvanCheck ──
            var rateCard = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "转换率" };
            Controls.Add(rateCard);

            int rateY = 30;
            AddRow(rateCard, "转换率", "%", 50.0, 75.0, 1.0, 0, Fmt.Clamp(st.RatePercent, 50, 75), ref rateY,
                   v => UpdateNumber(), out _rateBar, out _rateNum);

            _rateAuto = new IvanCheck { Text = "自动推荐", Checked = st.RateAuto };
            _rateAuto.SetBounds(12, rateY + 4, InnerW, 22);
            _rateAuto.CheckedChanged += (s, e) =>
            {
                ApplyRateRecommendation();
                UpdateNumber();
            };
            rateCard.Controls.Add(_rateAuto);

            rateCard.Height = rateY + 4 + 22 + 8;
            y = rateCard.Bottom + 8;

            // ── 雾化芯：勾选 + 直径数值行 + 轴向分段（原项 X / Y / Z 竖直 顺序不变） ──
            var coilCard = new CardPanel { Left = 12, Top = y, Width = W - 24, Title = "雾化芯" };
            Controls.Add(coilCard);

            _coilOn = new IvanCheck { Text = "扣除雾化芯体积（视口里会实时预览那个圆柱）", Checked = st.CoilEnabled };
            _coilOn.SetBounds(12, 30, InnerW, 22);
            _coilOn.CheckedChanged += (s, e) => Recalculate();
            coilCard.Controls.Add(_coilOn);

            int coilY = 54;
            AddRow(coilCard, "芯直径", "mm", 0.0, 50.0, 0.1, 2, Fmt.ClampDouble(st.CoilDiameterMm, 0.0, 50.0), ref coilY,
                   v => Recalculate(), out _, out _coilNum);

            var axisLabel = new Label();
            StyleLabel(axisLabel, Theme.Body, Theme.InkSoft, ContentAlignment.MiddleLeft);
            axisLabel.Text = "轴向";
            axisLabel.SetBounds(10, coilY + 5, 84, 18);
            coilCard.Controls.Add(axisLabel);

            _segAxis = new IvanSegmented();
            _segAxis.SetItems("X", "Y", "Z 竖直");
            _segAxis.SelectedIndex = Fmt.Clamp(st.CoilAxisIndex, 0, 2);   // 先定初值再挂事件
            _segAxis.SetBounds(96, coilY, 186, 28);
            _segAxis.SelectedChanged += (s, e) => Recalculate();
            coilCard.Controls.Add(_segAxis);
            coilY += 28;

            StyleLabel(_coilInfo, Theme.Small, Theme.InkFaint, ContentAlignment.TopLeft);
            _coilInfo.SetBounds(12, coilY + 4, InnerW, 32);
            coilCard.Controls.Add(_coilInfo);
            coilY += 4 + 32;

            coilCard.Height = coilY + 8;
            y = coilCard.Bottom + 8;

            // ── 状态条：原 _note 文案走 InfoBar ──
            _info.Text = "就绪";
            Controls.Add(_info);
            _info.SetBounds(12, y, W - 24, 44);
            y = _info.Bottom + 8;

            // ── 底部按钮：主按钮 96×32 在 W-12-96-104，次按钮「关闭」在 W-12-96 ──
            var rebuild = new FlatButton { Text = "重新生成标注", Style = BtnStyle.Primary, Width = 96, Height = 32, Left = W - 12 - 96 - 104 };
            rebuild.Click += (s, e) =>
            {
                if (_obj == null)
                {
                    SetInfo("还没有选择物件。");
                    return;
                }
                _syncedSpec = null;
                SyncAnnotations();
                SetInfo("标注已更新。");
            };
            Controls.Add(rebuild);

            var close = new FlatButton { Text = "关闭", Style = BtnStyle.Ghost, Width = 96, Height = 32, Left = W - 12 - 96 };
            close.Click += (s, e) => CloseAnimated();
            Controls.Add(close);

            rebuild.Top = y;
            close.Top = y;
            ClientSize = new System.Drawing.Size(W, y + 32 + 12);

            var header = new HeaderBand("烟油容量", TargetTitle(obj, rep), "vape");
            header.SetBounds(0, 0, W, 48);
            Controls.Add(header);
            _header = header;

            _pickCard.BringToFront();
            _info.BringToFront();
            rebuild.BringToFront();
            close.BringToFront();
            header.BringToFront();

            AcceptButton = rebuild;
            CancelButton = close;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) CloseAnimated(); };

            // 目标被删除时按钮必须回红：订阅删除事件，面板关闭时在 OnFormClosed 里退订
            _recheck.Interval = 250;
            _recheck.Tick += (s, e) => RecheckTick();
            _recheck.Stop();
            try
            {
                RhinoDoc.DeleteRhinoObject += OnDeleteObject;
                _subscribedDocEvents = true;
            }
            catch
            {
                _subscribedDocEvents = false;
            }

            PlaceNearRhino();
            Recalculate();
        }

        // ── 数值行：标签 84 + 滑杆 186 + 数字框 86，行高 32（照 StripePanel / HalftoneUi 的 AddRow） ──
        void AddRow(CardPanel card, string name, string unit, double min, double max, double step, int decimals,
                    double value, ref int y, Action<double> setter, out IvanSlider bar, out NumericUpDown num)
        {
            var label = new Label();
            StyleLabel(label, Theme.Body, Theme.InkSoft, ContentAlignment.MiddleLeft);
            label.Text = name + unit;
            label.SetBounds(10, y + 6, 84, 18);
            card.Controls.Add(label);

            bar = new IvanSlider
            {
                Minimum = min,
                Maximum = max,
                Step = step,
                Value = ClampD(value, min, max)
            };
            bar.SetBounds(96, y, 186, 26);
            card.Controls.Add(bar);

            num = new NumericUpDown
            {
                DecimalPlaces = decimals,                       // 纯显示位数：范围 / 步长 / 回调都不改
                Increment = (decimal)step,
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Body,
                BackColor = Theme.Surface
            };
            num.SetBounds(288, y + 2, 86, 24);
            num.Value = (decimal)Math.Round(ClampD(value, min, max), decimals);
            card.Controls.Add(num);

            IvanSlider localBar = bar;
            NumericUpDown localNum = num;
            localBar.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                _sync = true;
                try
                {
                    double v = localBar.Value;
                    localNum.Value = (decimal)Math.Round(ClampD(v, min, max), decimals);
                    setter(v);
                }
                finally { _sync = false; }
            };

            localNum.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                _sync = true;
                try
                {
                    double v = (double)localNum.Value;
                    localBar.Value = ClampD(v, localBar.Minimum, localBar.Maximum);
                    setter(v);
                }
                finally { _sync = false; }
            };

            y += 32;
        }

        static void StyleLabel(Label label, System.Drawing.Font font, System.Drawing.Color color, ContentAlignment align)
        {
            label.AutoSize = false;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = System.Drawing.Color.Transparent;
            label.TextAlign = align;
        }

        static double ClampD(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        double Rate { get { return _rateBar.Value / 100.0; } }

        bool CupMode { get { return _segMode.SelectedIndex <= 0; } }

        // ── 动效四件套：进入淡入 + 上移 8px，退出 150ms ease-in 后 Close() ──
        sealed class HeaderBand : Control
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
                Theme.DrawHeader(e.Graphics, new System.Drawing.Rectangle(0, 0, Width, Height), _title, _sub, _kind);
            }
        }

        void PlaceNearRhino()
        {
            try
            {
                IntPtr h = RhinoApp.MainWindowHandle();
                Screen scr = h != IntPtr.Zero ? Screen.FromHandle(h) : Screen.PrimaryScreen;
                System.Drawing.Rectangle wa = scr.WorkingArea;
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

        // 退出更快一档（150ms / ease-in）；done 回调里必须真的 Close()，否则面板关不掉
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

        /// <summary>面板关闭：停掉删除复查定时器并退订文档事件，避免事件泄漏。</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closing = true;
            try { _recheck.Stop(); _recheck.Dispose(); } catch { }
            if (_subscribedDocEvents)
            {
                _subscribedDocEvents = false;
                try { RhinoDoc.DeleteRhinoObject -= OnDeleteObject; } catch { }
            }
            base.OnFormClosed(e);
        }

        /// <summary>状态条（InfoBar）就地更新；跨线程调用时转回 UI 线程。</summary>
        void SetInfo(string text)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke((Action)(() => { if (!IsDisposed) _info.Text = text; })); return; }
                _info.Text = text;
            }
            catch { }
        }

        /// <summary>与安装器 vape 家族图标同形：水滴符号置于冰蓝色磨砂圆角底板（原 Eto 绘制的 GDI+ 版，几何与配色照搬）。</summary>
        sealed class VapeMark : Control
        {
            public VapeMark()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = System.Drawing.Color.Transparent;
                Width = 36;
                Height = 36;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float size = Math.Min(Width, Height);
                float pad = Math.Max(0.65f, size * 0.055f);
                var rect = new RectangleF((Width - size) * 0.5f + pad, (Height - size) * 0.5f + pad,
                    size - pad * 2f, size - pad * 2f);
                float radius = Math.Max(1.3f, size * 0.22f);

                for (int layer = 3; layer >= 1; layer--)
                {
                    float dy = size * (0.025f + layer * 0.012f);
                    var shadowRect = new RectangleF(rect.X, rect.Y + dy, rect.Width, rect.Height);
                    using (var shadow = GlassRoundPath(shadowRect, radius))
                    using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 0, 0, 9 + (4 - layer) * 7)))
                        g.FillPath(brush, shadow);
                }

                using (var tile = GlassRoundPath(rect, radius))
                {
                    using (var fill = new LinearGradientBrush(rect,
                        System.Drawing.Color.FromArgb(246, 251, 255, 61), System.Drawing.Color.FromArgb(44, 151, 211, 27), 90f))
                        g.FillPath(fill, tile);
                    using (var tint = new SolidBrush(System.Drawing.Color.FromArgb(44, 151, 211, 13)))
                        g.FillPath(tint, tile);
                    using (var darkEdge = new Pen(System.Drawing.Color.FromArgb(12, 20, 32, 115), Math.Max(0.65f, size * 0.052f)))
                        g.DrawPath(darkEdge, tile);
                    using (var edgeBrush = new LinearGradientBrush(rect,
                        System.Drawing.Color.FromArgb(255, 255, 255, 224), System.Drawing.Color.FromArgb(44, 151, 211, 112), 48f))
                    using (var edge = new Pen(edgeBrush, Math.Max(0.55f, size * 0.037f)))
                        g.DrawPath(edge, tile);
                }

                using (var sheen = new GraphicsPath())
                {
                    sheen.AddBezier(new PointF(rect.X + radius * 0.54f, rect.Y + radius * 0.70f),
                        new PointF(rect.X + radius * 0.72f, rect.Y + size * 0.045f),
                        new PointF(rect.Right - radius * 0.72f, rect.Y + size * 0.045f),
                        new PointF(rect.Right - radius * 0.54f, rect.Y + radius * 0.70f));
                    using (var gloss = new Pen(System.Drawing.Color.FromArgb(255, 255, 255, 210), Math.Max(0.45f, size * 0.014f)))
                    {
                        gloss.StartCap = LineCap.Round;
                        gloss.EndCap = LineCap.Round;
                        g.DrawPath(gloss, sheen);
                    }
                }
                using (var lower = new GraphicsPath())
                {
                    lower.AddBezier(new PointF(rect.X + radius * 0.75f, rect.Bottom - radius * 0.50f),
                        new PointF(rect.X + size * 0.25f, rect.Bottom - size * 0.045f),
                        new PointF(rect.Right - size * 0.25f, rect.Bottom - size * 0.045f),
                        new PointF(rect.Right - radius * 0.75f, rect.Bottom - radius * 0.50f));
                    using (var refract = new Pen(System.Drawing.Color.FromArgb(255, 255, 255, 50), Math.Max(0.4f, size * 0.012f)))
                    {
                        refract.StartCap = LineCap.Round;
                        refract.EndCap = LineCap.Round;
                        g.DrawPath(refract, lower);
                    }
                }

                var dropRect = new RectangleF(Width * 0.31f, Height * 0.235f, Width * 0.38f, Height * 0.54f);
                using (var drop = GlassDropPath(dropRect))
                {
                    using (var fill = new LinearGradientBrush(dropRect,
                        System.Drawing.Color.FromArgb(177, 216, 239), System.Drawing.Color.FromArgb(32, 109, 152), 125f))
                        g.FillPath(fill, drop);
                    using (var edge = new Pen(System.Drawing.Color.FromArgb(238, 250, 255, 225), Math.Max(0.65f, size * 0.045f)))
                        g.DrawPath(edge, drop);
                }
                using (var shine = new GraphicsPath())
                {
                    shine.AddBezier(new PointF(size * 0.435f, size * 0.48f), new PointF(size * 0.405f, size * 0.57f),
                        new PointF(size * 0.42f, size * 0.67f), new PointF(size * 0.47f, size * 0.70f));
                    using (var hi = new Pen(System.Drawing.Color.FromArgb(255, 255, 255, 222), Math.Max(0.65f, size * 0.055f)))
                    {
                        hi.StartCap = LineCap.Round;
                        hi.EndCap = LineCap.Round;
                        g.DrawPath(hi, shine);
                    }
                }
                using (var core = new Pen(System.Drawing.Color.FromArgb(244, 251, 255, 214), Math.Max(0.45f, size * 0.022f)))
                {
                    core.StartCap = LineCap.Round;
                    core.EndCap = LineCap.Round;
                    g.DrawLine(core, size * 0.485f, size * 0.54f, size * 0.515f, size * 0.54f);
                    g.DrawLine(core, size * 0.475f, size * 0.585f, size * 0.525f, size * 0.585f);
                    g.DrawLine(core, size * 0.485f, size * 0.63f, size * 0.515f, size * 0.63f);
                }
                base.OnPaint(e);
            }
        }

        static GraphicsPath GlassRoundPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        static GraphicsPath GlassDropPath(RectangleF r)
        {
            var path = new GraphicsPath();
            float cx = r.X + r.Width * 0.5f;
            float top = r.Y + r.Height * 0.02f;
            float bottom = r.Bottom - r.Height * 0.02f;
            float half = r.Width * 0.5f;
            path.AddBezier(new PointF(cx, top), new PointF(cx + half * 0.10f, top + r.Height * 0.16f),
                new PointF(r.Right + half * 0.02f, r.Y + r.Height * 0.47f),
                new PointF(r.Right - half * 0.03f, r.Y + r.Height * 0.66f));
            path.AddBezier(new PointF(r.Right - half * 0.03f, r.Y + r.Height * 0.66f),
                new PointF(r.Right - half * 0.05f, bottom), new PointF(cx + half * 0.48f, bottom), new PointF(cx, bottom));
            path.AddBezier(new PointF(cx, bottom), new PointF(cx - half * 0.48f, bottom),
                new PointF(r.X + half * 0.05f, bottom), new PointF(r.X + half * 0.03f, r.Y + r.Height * 0.66f));
            path.AddBezier(new PointF(r.X + half * 0.03f, r.Y + r.Height * 0.66f),
                new PointF(r.X - half * 0.02f, r.Y + r.Height * 0.47f),
                new PointF(cx - half * 0.10f, top + r.Height * 0.16f), new PointF(cx, top));
            path.CloseFigure();
            return path;
        }

        // 数值稳定 300ms 后给一次轻微强调色脉冲；拖动过程中不闪（高频操作不做动画）
        Timer _pulse;
        int _pulseStep;
        DateTime _lastValueAt = DateTime.MinValue;

        void MaybePulseValue()
        {
            try
            {
                DateTime now = DateTime.Now;
                bool quiet = (now - _lastValueAt).TotalMilliseconds > 300;
                _lastValueAt = now;
                if (!quiet) return;
                if (_pulse == null)
                {
                    _pulse = new Timer { Interval = 30 };
                    _pulse.Tick += (s, e) =>
                    {
                        _pulseStep++;
                        double t = Math.Min(1.0, _pulseStep / 7.0);      // ≈200ms，进入快、回落稳
                        try { _value.ForeColor = MixColor(Theme.Accent, Theme.Ink, t); } catch { }
                        if (t >= 1.0) _pulse.Stop();
                    };
                }
                _pulseStep = 0;
                _pulse.Stop();
                _pulse.Start();
            }
            catch { }
        }

        static System.Drawing.Color MixColor(System.Drawing.Color a, System.Drawing.Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return System.Drawing.Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // ───────────────────── 计算 ─────────────────────

        void Recalculate()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (_rep == null || !_rep.Ok)
                {
                    _baseMl = 0.0;
                    _netMl = 0.0;
                    _coilMl = 0.0;
                    _liquidSolid = null;
                    _coilInfo.Text = "";
                    SetInfo(_rep != null ? _rep.Error : EmptyTargetHint);
                    _value.Text = "—";
                    _formula.Text = "";
                    _detail.Text = "";
                    return;
                }

                BoundingBox box = BoundingBox.Unset;
                try
                {
                    if (_obj.Geometry != null) box = _obj.Geometry.GetBoundingBox(true);
                }
                catch
                {
                    box = BoundingBox.Unset;
                }

                if (CupMode)
                {
                    _wallBar.Enabled = true;
                    _wallNum.Enabled = true;
                    double wall = (double)_wallNum.Value;
                    string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}", _obj.Id, wall);
                    if (_cup == null || _cupKey != key)
                    {
                        _cup = VolumeEngine.ComputeCup(_obj, _units, wall, _mp);
                        _cupKey = key;
                    }

                    if (_cup != null && _cup.Ok)
                    {
                        _baseMl = _cup.BaseMl;
                        _approximate = _cup.Approximate;
                        _liquidSolid = _cup.LiquidSolid;
                        SetInfo(_cup.Note);
                    }
                    else
                    {
                        _baseMl = 0.0;
                        _liquidSolid = null;
                        _approximate = false;
                        SetInfo("壁厚扣减失败：" + (_cup != null ? _cup.Error : "未知原因"));
                    }
                }
                else
                {
                    _wallBar.Enabled = false;
                    _wallNum.Enabled = false;
                    _baseMl = _rep.HasCavity ? _rep.LiquidMl : _rep.MaterialMl;
                    _approximate = _rep.Approximate;
                    _liquidSolid = OilSolid();
                    SetInfo(_rep.HasCavity
                        ? "物件里已有封闭内腔，取内腔容积。" + _rep.Note
                        : _rep.Note);
                }

                ComputeCoil(box);
                ApplyRateRecommendation();
                UpdateCoilPreview();

                _detail.Text = string.Format(CultureInfo.InvariantCulture,
                    "材料体积 {0}　　外形包裹 {1}\n壳体数 {2}　{3}　单位：{4}",
                    Fmt.Ml(_rep.MaterialMl, _st.Decimals),
                    Fmt.Ml(_rep.EnvelopeMl, _st.Decimals),
                    _rep.ShellCount,
                    _rep.Closed ? "封闭实体" : "含未闭合面（近似）",
                    UnitContext.Name(_units.EffectiveUnits));

                UpdateNumber();
                SyncAnnotations();
            }
            catch (Exception ex)
            {
                SetInfo("计算异常：" + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        Brep OilSolid()
        {
            try
            {
                Brep brep = VolumeEngine.ToBrepAny(_obj.Geometry);
                if (brep == null) return null;
                if (_rep.HasCavity)
                {
                    Brep cavity = VolumeEngine.FindCavitySolid(brep, VolumeEngine.SafeTolerance(), _mp);
                    if (cavity != null) return cavity;
                }
                return brep;
            }
            catch
            {
                return null;
            }
        }

        void ComputeCoil(BoundingBox box)
        {
            _coilMl = 0.0;
            _coil = null;

            if (_coilOn.Checked != true)
            {
                _netMl = _baseMl;
                _coilInfo.Text = "未扣除雾化芯。";
                return;
            }

            double dia = (double)_coilNum.Value;
            int axisIndex = Fmt.Clamp(_segAxis.SelectedIndex, 0, 2);
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}",
                _obj.Id, dia, axisIndex, _liquidSolid != null);

            if (_coil == null || _coilKey != key)
            {
                _coil = VolumeEngine.ComputeCoil(_liquidSolid, box, dia,
                                                 AxisFromIndex(axisIndex), _units, _mp);
                _coilKey = key;
            }

            if (_coil != null && _coil.Ok)
            {
                _coilMl = _coil.VolumeMl;
                _coilInfo.Text = (_coil.Approximate ? "≈ " : "") + _coil.Note;
            }
            else
            {
                _coilInfo.Text = "雾化芯扣除失败：" + (_coil != null ? _coil.Error : "未知原因");
            }

            _netMl = Math.Max(0.0, _baseMl - _coilMl);
        }

        static CoilAxis AxisFromIndex(int i)
        {
            if (i == 0) return CoilAxis.X;
            if (i == 1) return CoilAxis.Y;
            return CoilAxis.Z;
        }

        /// <summary>视口实时预览：扣壁厚后的核心实体 + 雾化芯圆柱。</summary>
        void UpdateCoilPreview()
        {
            try
            {
                // 油杯模式下把「扣完壁厚的内部核心」画出来；油模式不画（那就是物件本身）
                bool showCore = CupMode && _liquidSolid != null;
                bool showCoil = _coilOn.Checked == true && (double)_coilNum.Value > 0.0;

                ViewportPreview.Show(_doc, _obj.Id,
                                     showCore, _liquidSolid,
                                     showCoil, (double)_coilNum.Value, AxisFromIndex(_segAxis.SelectedIndex));
            }
            catch
            {
                // 预览失败不影响计算
            }
        }

        /// <summary>
        /// 按「最终算出来的容量」反推转换率档位：
        /// 最终容量 = 净容积 × 转换率，而档位又由最终容量决定，是循环依赖 ——
        /// 由 Fmt.RecommendRateForNet 用「从高到低取满足一致性的档位」解掉。
        /// </summary>
        void ApplyRateRecommendation()
        {
            try
            {
                int rec = Fmt.RecommendRateForNet(_netMl);
                double finalMl = _netMl * rec / 100.0;
                _rateHint.Text = string.Format(CultureInfo.InvariantCulture,
                    "推荐 {0}%（净容积 {1} × {0}% = 最终 {2}，落在 {3}）",
                    rec,
                    Fmt.Ml(_netMl, _st.Decimals),
                    Fmt.Ml(finalMl, _st.Decimals),
                    Fmt.RecommendReason(finalMl));

                bool auto = _rateAuto.Checked == true;
                _rateBar.Enabled = !auto;
                _rateNum.Enabled = !auto;

                if (auto && _rateBar.Value != rec)
                {
                    _sync = true;
                    try
                    {
                        _rateBar.Value = rec;
                        _rateNum.Value = rec;     // 原来那行 _rateText.Text = rec + " %"
                    }
                    finally { _sync = false; }
                }
            }
            catch
            {
                // 推荐失败不影响主流程
            }
        }

        // ───────────────────── 标注同步 ─────────────────────

        LiveSpec BuildSpec(string kind)
        {
            return new LiveSpec
            {
                SourceId = _obj.Id,
                Kind = kind,
                CupMode = CupMode,
                WallMm = Fmt.ClampDouble((double)_wallNum.Value, 0.0, 20.0),
                RatePercent = (_rateAuto.Checked == true) ? -1 : Fmt.Clamp((int)Math.Round(_rateBar.Value), 50, 75),
                CoilOn = _coilOn.Checked == true,
                CoilDiameterMm = Fmt.ClampDouble((double)_coilNum.Value, 0.0, 50.0),
                CoilAxisIndex = Fmt.Clamp(_segAxis.SelectedIndex, 0, 2),
                Decimals = _st.Decimals
            };
        }

        /// <summary>把界面状态同步到模型上的两个活标注（参数没变就不动）。</summary>
        void SyncAnnotations()
        {
            try
            {
                LiveSpec capSpec = BuildSpec(LiveAnnotations.KindCapacity);
                if (_syncedSpec != null && _syncedSpec.SameAs(capSpec)) return;
                _syncedSpec = capSpec;

                Point3d capPoint = Point3d.Origin;
                try
                {
                    BoundingBox bb = _obj.Geometry.GetBoundingBox(true);
                    if (bb.IsValid) capPoint = LiveAnnotations.CapacityLabelPoint(bb);
                }
                catch
                {
                    capPoint = Point3d.Origin;
                }

                LiveAnnotations.CreateOrUpdate(_doc, capSpec, capPoint, _value.Text);

                LiveSpec dimSpec = capSpec.Clone();
                dimSpec.Kind = LiveAnnotations.KindDimensions;

                Point3d dimPoint;
                string dimText = LiveAnnotations.DimensionsText(_doc, _obj, out dimPoint);
                if (dimText != null)
                {
                    LiveAnnotations.CreateOrUpdate(_doc, dimSpec, dimPoint, dimText);
                }

                LiveAnnotations.Refresh(_doc);
                try { _doc.Views.Redraw(); } catch { }
            }
            catch
            {
                // 标注同步失败不影响计算
            }
        }

        // ───────────────────── 显示更新 ─────────────────────

        void UpdateNumber()
        {
            double final = _netMl * Rate;
            string prefix = _approximate ? "≈ " : "";
            _value.Text = _baseMl > 0.0 ? prefix + Fmt.Ml(final, _st.Decimals) : "—";
            MaybePulseValue();

            if (_baseMl > 0.0)
            {
                if (_coilOn.Checked == true && _coilMl > 0.0)
                {
                    _formula.Text = string.Format(CultureInfo.InvariantCulture,
                        "= （基准 {0} − 雾化芯 {1}） × 转换率 {2:0}%　→ 净容积 {3}",
                        Fmt.Ml(_baseMl, _st.Decimals),
                        Fmt.Ml(_coilMl, _st.Decimals),
                        _rateBar.Value,
                        Fmt.Ml(_netMl, _st.Decimals));
                }
                else
                {
                    _formula.Text = string.Format(CultureInfo.InvariantCulture,
                        "= 基准容积 {0} × 转换率 {1:0}%",
                        Fmt.Ml(_baseMl, _st.Decimals), _rateBar.Value);
                }
            }
            else
            {
                _formula.Text = "";
            }

            PushSettings();
        }

        void PushSettings()
        {
            try
            {
                _st.RatePercent = Fmt.Clamp((int)Math.Round(_rateBar.Value), 50, 75);
                _st.RateAuto = _rateAuto.Checked == true;
                _st.CupMode = CupMode;
                _st.WallMm = Fmt.ClampDouble((double)_wallNum.Value, 0.0, 20.0);
                _st.CoilEnabled = _coilOn.Checked == true;
                _st.CoilDiameterMm = Fmt.ClampDouble((double)_coilNum.Value, 0.0, 50.0);
                _st.CoilAxisIndex = Fmt.Clamp(_segAxis.SelectedIndex, 0, 2);
            }
            catch
            {
                // 写回失败不影响使用
            }
        }
    }
}
