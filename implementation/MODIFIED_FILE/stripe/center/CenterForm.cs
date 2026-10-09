using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using IvanUi;

namespace IvanCenter
{
    internal class CenterForm : Form
    {
        readonly Installer _inst = new Installer();
        readonly Panel _list = new Panel();
        readonly TextBox _log = new TextBox();
        readonly Label _status = new Label();
        readonly ProgressStrip _progress = new ProgressStrip();
        readonly Dictionary<string, PluginRow> _rows = new Dictionary<string, PluginRow>();
        readonly FlatButton _all = new FlatButton();
        readonly FlatButton _rmAll = new FlatButton();
        readonly Label _pageTitle = new Label();
        readonly Label _pageSub = new Label();

        const int W = 1000;
        const int SidebarWidth = 204;
        int _rowHeight = 70;

        public CenterForm()
        {
            Text = "IVAN CENTER · IVAN 插件中心";
            var work = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea : new Rectangle(0, 0, W, 840);
            int clientWidth = Math.Min(W, Math.Max(860, work.Width - 32));
            int clientHeight = Math.Min(800, Math.Max(620, work.Height - 46));
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(clientWidth, clientHeight);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimumSize = new Size(860, 620);
            MaximumSize = new Size(work.Width, work.Height);
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            // 窗体图标必须显式设：不设的话 WinForms 用框架自带的默认窗体图标，EXE 内嵌图标不会自动生效
            try { Icon = Theme.MakeFormIcon("app", 32); } catch { }
            try { Theme.ApplyWindowMaterial(this); } catch { }
            _inst.OnLog += s => AppendLog(s);

            var header = new HeaderBand("IVAN CENTER", "Rhino 插件套件", "app");
            header.SetBounds(0, 0, SidebarWidth, ClientSize.Height);
            header.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(header);

            int mainLeft = SidebarWidth + 24;
            int mainWidth = ClientSize.Width - mainLeft - 22;
            _pageTitle.Text = "插件中心";
            _pageTitle.SetBounds(mainLeft, 14, mainWidth, 28);
            _pageTitle.Font = Theme.Title;
            _pageTitle.ForeColor = Theme.Ink;
            Controls.Add(_pageTitle);
            _pageSub.Text = "安装、更新与管理你的曲面创作工具。";
            _pageSub.SetBounds(mainLeft, 43, mainWidth, 18);
            _pageSub.Font = Theme.Small;
            _pageSub.ForeColor = Theme.InkSoft;
            Controls.Add(_pageSub);

            // 行高按插件个数算（新增插件不用手改数字）：6 行时 800 高的客户区仍留得下日志卡片
            int rowCount = Math.Max(1, Repo.Plugins.Count);
            _rowHeight = Math.Max(60, Math.Min(78, (ClientSize.Height - 360) / rowCount));
            int rowsHeight = _rowHeight * rowCount;
            int cardTop = 72;
            int cardHeight = 32 + rowsHeight + 10;
            var card = new CardPanel { Left = mainLeft, Top = cardTop, Width = mainWidth, Height = cardHeight, Title = "插件套件 · " + rowCount.ToString("D2") };
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(card);

            _list.SetBounds(12, 30, card.Width - 24, rowsHeight + 8);
            _list.BackColor = Theme.SurfaceAlt;
            _list.AutoScroll = true;
            card.Controls.Add(_list);

            int actionY = card.Bottom + 12;
            _all.Text = "全部安装 / 更新";
            _all.Style = BtnStyle.Primary;
            _all.SetBounds(mainLeft, actionY, 152, 36);
            _all.Click += (s, e) => DoAll(true);
            Controls.Add(_all);

            _rmAll.Text = "全部卸载";
            _rmAll.Style = BtnStyle.Secondary;
            _rmAll.SetBounds(mainLeft + 162, actionY, 104, 36);
            _rmAll.Click += (s, e) => DoAll(false);
            Controls.Add(_rmAll);

            var open = new FlatButton { Text = "打开目录", Style = BtnStyle.Ghost, Width = 104, Height = 36 };
            open.SetBounds(mainLeft + 278, actionY, 104, 36);
            open.Click += (s, e) =>
            {
                try { Directory.CreateDirectory(Repo.RootDir); Process.Start("explorer.exe", Repo.RootDir); } catch { }
            };
            Controls.Add(open);

            var close = new FlatButton { Text = "退出", Style = BtnStyle.Ghost, Width = 76, Height = 36 };
            close.SetBounds(mainLeft + mainWidth - 76, actionY, 76, 36);
            close.Click += (s, e) => Close();
            Controls.Add(close);

            int footerY = actionY + 48;
            _progress.SetBounds(mainLeft, footerY, mainWidth, 6);
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_progress);

            _status.Text = "正在检测本机 Rhino 与插件状态…";
            _status.SetBounds(mainLeft, footerY + 12, mainWidth, 20);
            _status.ForeColor = Theme.InkSoft;
            _status.Font = Theme.Small;
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_status);

            int logTop = footerY + 38;
            var logCard = new CardPanel { Left = mainLeft, Top = logTop, Width = mainWidth, Height = ClientSize.Height - logTop - 12, Title = "安装日志" };
            logCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(logCard);
            _log.SetBounds(12, 30, logCard.Width - 24, Math.Max(24, logCard.Height - 40));
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Theme.SurfaceAlt;
            _log.BorderStyle = BorderStyle.None;
            _log.Font = new Font("Consolas", 8.75F);
            _log.ForeColor = Theme.InkSoft;
            logCard.Controls.Add(_log);

            Load += (s, e) =>
            {
                BuildRows();
                RefreshStatus();
                AppendLog("IVAN CENTER 就绪。点「全部安装 / 更新」把插件装进本机所有 Rhino。");
            };
        }

        class HeaderBand : Control
        {
            readonly string _title, _sub, _kind;
            readonly Bitmap _brandIcon;
            public HeaderBand(string title, string sub, string kind)
            {
                _title = title; _sub = sub; _kind = kind;
                _brandIcon = IconFactory.Create(kind, 48);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Canvas;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var b = new LinearGradientBrush(r, Color.FromArgb(246, 250, 253), Color.FromArgb(226, 237, 247), 90f))
                    g.FillRectangle(b, r);
                using (var p = new Pen(Color.FromArgb(210, 255, 255, 255), 1f))
                    g.DrawLine(p, Width - 1, 0, Width - 1, Height);
                if (_brandIcon != null) g.DrawImage(_brandIcon, 20, 24, 48, 48);
                using (var f = Theme.BodyBold)
                using (var b = new SolidBrush(Theme.Ink))
                    g.DrawString(_title, f, b, 20, 88);
                using (var f = Theme.Small)
                using (var b = new SolidBrush(Theme.InkSoft))
                    g.DrawString(_sub, f, b, 20, 108);

                using (var p = new Pen(Color.FromArgb(150, Theme.Border), 1f))
                    g.DrawLine(p, 20, 144, Width - 20, 144);
                using (var f = Theme.Small)
                using (var b = new SolidBrush(Theme.InkFaint))
                    g.DrawString("工作台", f, b, 20, 166);
                using (var path = Theme.Rounded(new Rectangle(12, 188, Width - 24, 40), 10))
                using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                using (var p = new Pen(Color.FromArgb(220, 255, 255, 255), 1f))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                using (var dot = new SolidBrush(Theme.Accent)) g.FillEllipse(dot, 25, 204, 8, 8);
                using (var f = Theme.BodyBold)
                using (var b = new SolidBrush(Theme.Accent))
                    g.DrawString("插件中心", f, b, 42, 198);

                using (var f = Theme.Small)
                using (var b = new SolidBrush(Theme.InkFaint))
                    g.DrawString("为 Rhino 设计", f, b, 20, Height - 70);
                using (var f = Theme.BodyBold)
                using (var b = new SolidBrush(Theme.InkSoft))
                    g.DrawString("Rhino 7 / 8", f, b, 20, Height - 49);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _brandIcon != null) _brandIcon.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>细进度条：目标值变化时用 300ms ease-out 追上去（不是硬跳）</summary>
        internal class ProgressStrip : Control
        {
            double _shown, _target;
            public ProgressStrip()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Canvas;
            }
            public void Set(double v)
            {
                if (v < 0) v = 0; if (v > 1) v = 1;
                _target = v;
                double from = _shown;
                Motion.To(this, "bar", from, v, Motion.Slow, Motion.EaseOut, x => { _shown = x; Invalidate(); });
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width, Height);
                using (var b = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
                using (var path = Theme.Rounded(r, Height / 2)) g.FillPath(b, path);
                int w = (int)Math.Round(Width * _shown);
                if (w > 2)
                {
                    var fr = new Rectangle(0, 0, w, Height);
                    using (var b = new LinearGradientBrush(fr, Theme.Accent, Color.FromArgb(92, 157, 250), 0f))
                    using (var path = Theme.Rounded(fr, Height / 2)) g.FillPath(b, path);
                }
            }
        }

        /// <summary>一行插件：图标 + 名称 + 说明 + 状态（颜色和文字同时表达）+ 两个按钮</summary>
        internal class PluginRow : Control
        {
            public PluginDef Def;
            public string StateText = "检测中…";
            public FlatButton Install, Remove;
            readonly Bitmap _icon;
            double _pulse = 1;

            public PluginRow(PluginDef def, int width, int height)
            {
                Def = def;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.SurfaceAlt;
                Width = width; Height = height;
                _icon = IconFactory.Create(def.IconKind, 64);

                // 说明在两行里放不下就省略号，完整说明挂 tooltip
                var tip = new ToolTip();
                tip.SetToolTip(this, def.Name + Environment.NewLine + def.Desc);

                Install = new FlatButton { Text = "安装", Style = BtnStyle.Primary, Width = 78, Height = 30 };
                Install.SetBounds(width - 78 - 78 - 16, (height - 30) / 2, 78, 30);
                Controls.Add(Install);

                Remove = new FlatButton { Text = "卸载", Style = BtnStyle.Secondary, Width = 72, Height = 30 };
                Remove.SetBounds(width - 72 - 8, (height - 30) / 2, 72, 30);
                Controls.Add(Remove);
            }

            public void SetState(string text, bool installed)
            {
                StateText = text;
                Install.Text = installed ? "更新" : "安装";
                Invalidate();
                Motion.To(this, "pulse", 0, 1, Motion.Base, Motion.EaseOut, v => { _pulse = 0.35 + 0.65 * v; Invalidate(); });
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var path = Theme.Rounded(bounds, 12))
                using (var b = new SolidBrush(Color.FromArgb(222, 255, 255, 255)))
                using (var p = new Pen(Color.FromArgb(228, 255, 255, 255), 1f))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                g.DrawImage(_icon, 10, (Height - _icon.Height) / 2);

                using (var f = Theme.BodyBold)
                using (var b = new SolidBrush(Theme.Ink))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(Def.Name + "  ·  " + Def.EnName, f, b, new RectangleF(86, 5, Width - 86 - 176, 18), sf);

                // 标题、说明、状态使用独立的垂直槽位；说明最多两行，避免不同 DPI / 长文案叠字。
                using (var f = Theme.Small)
                using (var b = new SolidBrush(Theme.InkFaint))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit })
                    g.DrawString(Def.Desc, f, b, new RectangleF(86, 25, Width - 86 - 176, Math.Max(18, Height - 47)), sf);

                bool ok = StateText.IndexOf("已安装", StringComparison.Ordinal) >= 0;
                Color st = ok ? Theme.Ok : Theme.InkFaint;
                int sy = Height - 18;
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, _pulse + 0.25)), st)))
                    g.FillEllipse(b, 87, sy + 4, 6, 6);
                using (var f = Theme.Small)
                using (var b = new SolidBrush(st))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(StateText, f, b, new RectangleF(100, sy, Width - 100 - 176, 16), sf);

                using (var p = new Pen(Color.FromArgb(228, Theme.Border), 1f))
                    g.DrawLine(p, 12, Height - 1, Width - 12, Height - 1);
            }
        }

        void BuildRows()
        {
            int y = 4;
            int i = 0;
            foreach (PluginDef p in Repo.Plugins)
            {
                var row = new PluginRow(p, _list.Width - 24, _rowHeight);
                row.Left = 4;
                row.Top = y;
                PluginDef pd = p;
                row.Install.Click += (s, e) => InstallOne(pd);
                row.Remove.Click += (s, e) => UninstallOne(pd);
                _list.Controls.Add(row);
                _rows[p.Key] = row;

                // 行进入：淡入 + 上移 8px，按 60ms 错开（stagger）
                int idx = i++;
                if (Motion.Enabled)
                {
                    int top0 = row.Top;
                    row.Top = top0 + 8;
                    var t = new System.Windows.Forms.Timer { Interval = 40 + idx * 60 };
                    t.Tick += (s, e) =>
                    {
                        t.Stop(); t.Dispose();
                        Motion.To(row, "enter", 0, 1, Motion.Slow, Motion.EaseOut, v =>
                        {
                            row.Top = (int)Math.Round(top0 + 8 * (1 - v));
                        });
                    };
                    t.Start();
                }
                y += _rowHeight;
            }
        }

        void AppendLog(string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => AppendLog(s))); } catch { } return; }
            _log.AppendText(s + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        void SetStatus(string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => SetStatus(s))); } catch { } return; }
            _status.Text = s;
        }

        void RefreshStatus()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => RefreshStatus())); } catch { } return; }
            int rhinoCount = Installer.FindRhinos().Count;
            int installed = 0;
            foreach (PluginDef p in Repo.Plugins)
            {
                bool ok = _inst.IsInstalled(p);
                if (ok) installed++;
                PluginRow r;
                if (_rows.TryGetValue(p.Key, out r)) r.SetState(ok ? "● 已安装" : "○ 未安装", ok);
            }
            _status.Text = string.Format("本机检测到 {0} 个 Rhino 安装；已安装插件 {1}/{2}。工具条：IVAN CENTER（点图标即用命令）",
                rhinoCount, installed, Repo.Plugins.Count);
        }

        bool EnsureRhinoClosed()
        {
            for (int i = 0; i < 120; i++)
            {
                if (Process.GetProcessesByName("Rhino").Length == 0) return true;
                if (i == 0)
                {
                    var r = MessageBox.Show(this,
                        "检测到 Rhino 正在运行。\n\n安装/卸载需要写入 Rhino 的工具条配置，请先完全关闭 Rhino 后点「确定」。",
                        "请先关闭 Rhino", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (r != DialogResult.OK) { AppendLog("已取消（Rhino 仍在运行）。"); return false; }
                }
                Thread.Sleep(1000);
                Application.DoEvents();
            }
            AppendLog("等待 Rhino 关闭超时，已取消。");
            return false;
        }

        bool _busy;

        /// <summary>忙碌期间禁用按钮（提交后禁用，别让人重复点）</summary>
        void SetBusy(bool busy)
        {
            _busy = busy;
            _all.Enabled = !busy;
            _rmAll.Enabled = !busy;
            foreach (PluginRow r in _rows.Values)
            {
                r.Install.Enabled = !busy;
                r.Remove.Enabled = !busy;
            }
            _all.Text = busy ? "处理中…" : "全部安装 / 更新";
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        }

        void InstallOne(PluginDef p)
        {
            if (_busy) return;
            if (!EnsureRhinoClosed()) return;
            var rhinos = Installer.FindRhinos();
            if (rhinos.Count == 0) { AppendLog("× 没有找到 Rhino 安装记录，请先运行一次 Rhino。"); return; }

            SetBusy(true);
            _progress.Set(0.1);
            RunAsync(() =>
            {
                AppendLog("=== 安装 " + p.Name + " ===");
                _inst.Install(p, rhinos);
                _progress.Set(0.6);
                RebuildToolbar(rhinos);
                _progress.Set(1.0);
                RefreshStatus();
                AppendLog("完成。启动 Rhino 后点左上角 IVAN CENTER 工具条上的图标即可使用「" + p.Name + "」。");
                SetStatus("完成：" + p.Name);
            });
        }

        void UninstallOne(PluginDef p)
        {
            if (_busy) return;
            if (!EnsureRhinoClosed()) return;
            var rhinos = Installer.FindRhinos();
            SetBusy(true);
            _progress.Set(0.1);
            RunAsync(() =>
            {
                AppendLog("=== 卸载 " + p.Name + " ===");
                _inst.Uninstall(p, rhinos);
                _progress.Set(0.6);
                RebuildToolbar(rhinos);
                _progress.Set(1.0);
                RefreshStatus();
                AppendLog("完成。");
                SetStatus("已卸载：" + p.Name);
            });
        }

        void DoAll(bool install)
        {
            if (_busy) return;
            if (!EnsureRhinoClosed()) return;
            var rhinos = Installer.FindRhinos();
            if (rhinos.Count == 0) { AppendLog("× 没有找到 Rhino 安装记录，请先运行一次 Rhino。"); return; }

            SetBusy(true);
            _progress.Set(0);
            RunAsync(() =>
            {
                AppendLog(install ? "=== 全部安装 / 更新 ===" : "=== 全部卸载 ===");
                int n = Repo.Plugins.Count;
                for (int i = 0; i < n; i++)
                {
                    if (install) _inst.Install(Repo.Plugins[i], rhinos); else _inst.Uninstall(Repo.Plugins[i], rhinos);
                    _progress.Set((i + 1) / (double)(n + 1));
                }
                RebuildToolbar(rhinos);
                _progress.Set(1.0);
                RefreshStatus();
                AppendLog("完成。" + (install ? "启动 Rhino 后左上角 IVAN CENTER 工具条上会出现各插件图标。" : ""));
                SetStatus(install ? "全部安装 / 更新完成" : "全部卸载完成");
            });
        }

        /// <summary>放到后台线程跑：安装过程要写文件/注册表，跑在主线程会把界面冻住、进度条也就动不了</summary>
        void RunAsync(Action work)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { work(); }
                catch (Exception ex) { AppendLog("出错：" + ex.Message); SetStatus("出错：" + ex.Message); }
                finally
                {
                    try { BeginInvoke((Action)(() => SetBusy(false))); } catch { }
                }
            });
        }

        void RebuildToolbar(List<RhinoInstall> rhinos)
        {
            var list = new List<PluginDef>();
            foreach (PluginDef p in Repo.Plugins) if (_inst.IsInstalled(p)) list.Add(p);

            if (list.Count == 0)
            {
                foreach (RhinoInstall r in rhinos) _inst.RemoveToolbar(r.Major);
                return;
            }
            string rui = Path.Combine(Path.GetTempPath(), Repo.RuiName);
            _inst.InstallToolbar(list, rhinos, rui);
        }

        // ---------------------------------------------------------------- 界面冒烟（/uitest）
        // 自绘控件里最容易犯的错是「把控件自己的字体/画笔画掉了」，第一遍看着正常、第二遍就抛
        // GDI+「参数无效」，WinForms 会把控件画成红叉。所以这里强制整窗重绘两遍 + 逐控件重绘。
        public int RunUiTest()
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                Show();
                Application.DoEvents();
                var rect = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
                using (var bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
                {
                    DrawToBitmap(bmp, rect);
                    DrawToBitmap(bmp, rect);        // 第二遍才会暴露「字体被 Dispose」这类问题
                    bmp.Save(Path.Combine(Path.GetTempPath(), "IVANCENTER-uitest.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                int n = 0;
                foreach (Control c in Controls)
                {
                    c.Refresh();
                    Application.DoEvents();
                    n++;
                }
                sb.AppendLine("UI 冒烟通过：两次整窗重绘 + " + n + " 个顶层控件重绘，无异常");
                sb.AppendLine("控件数 " + Controls.Count + "，客户区 " + ClientSize.Width + "x" + ClientSize.Height);
            }
            catch (Exception ex)
            {
                code = 1;
                sb.AppendLine("UI 冒烟失败：" + ex.GetType().Name + " " + ex.Message);
                sb.AppendLine(ex.StackTrace);
            }
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "IVANCENTER-uitest.log"), sb.ToString(), new UTF8Encoding(true)); }
            catch { }
            return code;
        }

        // ---------------------------------------------------------------- 静默模式
        public int RunSilent(bool uninstall)
        {
            _inst.Silent = true;
            try
            {
                if (Process.GetProcessesByName("Rhino").Length > 0)
                {
                    _inst.Log("Rhino 正在运行，静默安装中止。");
                    Dump(); return 2;
                }
                var rhinos = Installer.FindRhinos();
                if (rhinos.Count == 0) { _inst.Log("没有找到 Rhino。"); Dump(); return 3; }

                foreach (RhinoInstall r in rhinos)
                    _inst.Log(string.Format("  · 发现 Rhino {0}：{1}", r.Major, r.Exe));

                foreach (PluginDef p in Repo.Plugins)
                    if (uninstall) _inst.Uninstall(p, rhinos); else _inst.Install(p, rhinos);

                var list = new List<PluginDef>();
                foreach (PluginDef p in Repo.Plugins) if (_inst.IsInstalled(p)) list.Add(p);
                if (list.Count > 0)
                    _inst.InstallToolbar(list, rhinos, Path.Combine(Path.GetTempPath(), Repo.RuiName));
                else
                    foreach (RhinoInstall r in rhinos) _inst.RemoveToolbar(r.Major);

                Dump();
                return 0;
            }
            catch (Exception ex)
            {
                _inst.Log("出错：" + ex);
                Dump();
                return 1;
            }
        }

        void Dump()
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "IVANCENTER.log"), _inst.LogText.ToString(), new UTF8Encoding(true)); }
            catch { }
        }
    }
}
