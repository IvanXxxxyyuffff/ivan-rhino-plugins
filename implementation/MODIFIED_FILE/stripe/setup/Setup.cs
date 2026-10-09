using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StripeOnSurfaceSetup
{
    internal class RhinoInstall
    {
        public string Exe;
        public int Major;
        public string Dir;      // ...\System
    }

    internal static class Program
    {
        public const string PluginGuid = "b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91";
        public const string FileGuid = "a7f3c1e2-4b58-4d6a-9c02-1e5b7d9a3f40";
        public const string DockGuid = "c4d2e8f1-6a37-4b95-8e10-2f7c9a4b6d13";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = false, uninstall = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase) || a.Equals("-silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                    if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                }
            }

            if (silent)
            {
                var f = new SetupForm();
                Environment.Exit(f.RunSilent(uninstall));
            }
            Application.Run(new SetupForm());
        }
    }

    internal class SetupForm : Form
    {
        readonly TextBox _log = new TextBox();
        readonly Button _install = new Button();
        readonly Button _uninstall = new Button();
        readonly Button _close = new Button();
        readonly CheckBox _launch = new CheckBox();

        public SetupForm()
        {
            Text = "表面条纹插件 安装程序  ·  StripeOnSurface";
            ClientSize = new Size(620, 430);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = SystemIcons.Application; } catch { }

            var head = new Label
            {
                Text = "把「表面条纹」插件装进本机所有 Rhino（自动注册插件 + 自动出现工具栏图标）",
                Left = 14,
                Top = 12,
                Width = 590,
                Height = 22
            };
            Controls.Add(head);

            _log.SetBounds(14, 40, 590, 320);
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.White;
            _log.Font = new Font("Consolas", 9F);
            Controls.Add(_log);

            _launch.Text = "装完启动 Rhino";
            _launch.Checked = true;
            _launch.SetBounds(14, 372, 140, 24);
            Controls.Add(_launch);

            _install.Text = "安装 / 更新";
            _install.SetBounds(300, 370, 96, 28);
            _install.Click += (s, e) => Run(true);
            Controls.Add(_install);

            _uninstall.Text = "卸载";
            _uninstall.SetBounds(404, 370, 96, 28);
            _uninstall.Click += (s, e) => Run(false);
            Controls.Add(_uninstall);

            _close.Text = "退出";
            _close.SetBounds(508, 370, 96, 28);
            _close.Click += (s, e) => Close();
            Controls.Add(_close);

            Shown += (s, e) =>
            {
                Log("表面条纹插件安装程序 v1.0");
                Log("点击「安装 / 更新」开始。（安装前请先关闭 Rhino）");
                Log("");
            };
        }

        void Log(string s)
        {
            _sb.AppendLine(s);
            if (_silent) return;
            _log.AppendText(s + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
            Application.DoEvents();
        }

        readonly StringBuilder _sb = new StringBuilder();
        bool _silent;

        /// <summary>无人值守模式：/silent [ /uninstall ]</summary>
        public int RunSilent(bool uninstall)
        {
            _silent = true;
            try
            {
                if (Process.GetProcessesByName("Rhino").Length > 0)
                {
                    Log("Rhino 正在运行，静默安装中止（请先关闭 Rhino）。");
                    Dump();
                    return 2;
                }
                if (uninstall) DoUninstall(); else DoInstall();
                Dump();
                return 0;
            }
            catch (Exception ex)
            {
                Log("出错：" + ex);
                Dump();
                return 1;
            }
        }

        void Dump()
        {
            try
            {
                string p = Path.Combine(Path.GetTempPath(), "StripeOnSurfaceSetup.log");
                File.WriteAllText(p, _sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        void Run(bool install)
        {
            _install.Enabled = _uninstall.Enabled = false;
            try
            {
                if (!EnsureRhinoClosed()) return;
                if (install) DoInstall(); else DoUninstall();
            }
            catch (Exception ex)
            {
                Log("出错：" + ex.Message);
            }
            finally
            {
                _install.Enabled = _uninstall.Enabled = true;
            }
        }

        bool EnsureRhinoClosed()
        {
            for (int i = 0; i < 60; i++)
            {
                if (Process.GetProcessesByName("Rhino").Length == 0) return true;
                if (i == 0)
                {
                    var r = MessageBox.Show(
                        "检测到 Rhino 正在运行。\n\n安装/卸载需要修改 Rhino 的工具列配置，请先完全关闭 Rhino，然后点「确定」继续。",
                        "请先关闭 Rhino", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (r != DialogResult.OK) { Log("已取消（Rhino 仍在运行）。"); return false; }
                }
                System.Threading.Thread.Sleep(1000);
                Application.DoEvents();
            }
            Log("等待 Rhino 关闭超时，已取消。请关闭 Rhino 后重试。");
            return false;
        }

        // ==================================================================
        static string UserData(int major)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "McNeel", "Rhinoceros", major + ".0");
        }

        static string PayloadDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StripeOnSurface"); }
        }

        void DoInstall()
        {
            Log("=== 开始安装 ===");
            Directory.CreateDirectory(PayloadDir);

            string rh7 = Path.Combine(PayloadDir, "StripeOnSurface-rh7.rhp");
            string rh8 = Path.Combine(PayloadDir, "StripeOnSurface-rh8.rhp");
            string rui = Path.Combine(PayloadDir, "StripeOnSurface.rui");
            Extract("p_rh7", rh7);
            Extract("p_rh8", rh8);
            Extract("p_rui", rui);
            Log("插件文件已释放到：" + PayloadDir);

            List<RhinoInstall> rhinos = FindRhinos();
            if (rhinos.Count == 0)
            {
                Log("× 没有在本机找到 Rhino 安装记录（注册表）。");
                Log("  请先安装并至少运行一次 Rhino，再运行本程序。");
                return;
            }

            foreach (RhinoInstall r in rhinos)
            {
                Log("");
                Log(string.Format("发现 Rhino {0}  ->  {1}", r.Major, r.Exe));
                string rhp = r.Major >= 8 ? rh8 : rh7;
                if (r.Major < 7)
                {
                    Log("  × 版本过低（仅支持 Rhino 7 / 8），跳过");
                    continue;
                }
                RegisterPlugin(r, rhp);
                InstallToolbar(r, rui);
            }

            Log("");
            Log("=== 安装完成 ===");
            Log("启动 Rhino 后：屏幕左上角会出现「表面条纹」浮动工具条（只有一个图标按钮）。");
            Log("点图标 = 选中曲面生成条纹；命令行输入 StripeOnSurface 同样可用。");

            if (_launch.Checked)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(rhinos[0].Exe) { WorkingDirectory = rhinos[0].Dir });
                    Log("已启动 Rhino。");
                }
                catch (Exception ex) { Log("启动 Rhino 失败：" + ex.Message); }
            }
        }

        void DoUninstall()
        {
            Log("=== 开始卸载 ===");
            foreach (RhinoInstall r in FindRhinos())
            {
                string hive = string.Format(@"Software\MCNeel\Rhinoceros\{0}.0\Plug-Ins\{1}", r.Major, Program.PluginGuid);
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(hive, true))
                    {
                        if (k != null) { Registry.CurrentUser.DeleteSubKeyTree(hive, false); Log("已删除注册表项：" + hive); }
                    }
                }
                catch (Exception ex) { Log("删除注册表失败：" + ex.Message); }

                try
                {
                    string ui = Path.Combine(UserData(r.Major), "UI", "Plug-ins", "StripeOnSurface.rui");
                    if (File.Exists(ui)) { File.Delete(ui); Log("已删除工具条文件：" + ui); }
                    RemoveWorkspaceEntries(r.Major, Log);
                }
                catch (Exception ex) { Log("清理工具列失败：" + ex.Message); }
            }
            try
            {
                if (Directory.Exists(PayloadDir)) { Directory.Delete(PayloadDir, true); Log("已删除：" + PayloadDir); }
            }
            catch { }
            Log("");
            Log("=== 卸载完成 ===");
        }

        // ==================================================================
        static void Extract(string logicalName, string dest)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName))
            {
                if (s == null) throw new Exception("安装包内缺少资源：" + logicalName);
                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
        }

        static List<RhinoInstall> FindRhinos()
        {
            var list = new List<RhinoInstall>();
            RegistryKey[] roots = { Registry.LocalMachine, Registry.CurrentUser };
            foreach (RegistryKey root in roots)
            {
                using (RegistryKey k = root.OpenSubKey(@"SOFTWARE\McNeel\Rhinoceros"))
                {
                    if (k == null) continue;
                    foreach (string verName in k.GetSubKeyNames())
                    {
                        using (RegistryKey ik = k.OpenSubKey(verName + @"\Install"))
                        {
                            if (ik == null) continue;
                            string exe = ik.GetValue("ExePath") as string;
                            if (string.IsNullOrEmpty(exe))
                            {
                                string p = ik.GetValue("Path") as string;
                                if (!string.IsNullOrEmpty(p)) exe = Path.Combine(p, "Rhino.exe");
                            }
                            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;

                            int major = 0;
                            try { major = FileVersionInfo.GetVersionInfo(exe).FileMajorPart; } catch { }
                            if (major <= 0) continue;

                            bool dup = false;
                            foreach (RhinoInstall x in list)
                                if (string.Equals(x.Exe, exe, StringComparison.OrdinalIgnoreCase)) dup = true;
                            if (dup) continue;

                            list.Add(new RhinoInstall { Exe = exe, Major = major, Dir = Path.GetDirectoryName(exe) });
                        }
                    }
                }
            }
            return list;
        }

        void RegisterPlugin(RhinoInstall r, string rhp)
        {
            string key = string.Format(@"Software\MCNeel\Rhinoceros\{0}.0\Plug-Ins\{1}", r.Major, Program.PluginGuid);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key))
            {
                k.SetValue("Name", "表面条纹 (StripeOnSurface)");
                k.SetValue("EnglishName", "StripeOnSurface");
                k.SetValue("Organization", "");
                k.SetValue("AddToHelpMenu", 0, RegistryValueKind.DWord);
                k.SetValue("LoadMode", 1, RegistryValueKind.DWord);
                k.SetValue("Type", 16, RegistryValueKind.DWord);
                k.SetValue("IsDotNETPlugIn", 1, RegistryValueKind.DWord);
                k.SetValue("DirectoryInstall", 0, RegistryValueKind.DWord);
                k.SetValue("RegPath", @"\\HKEY_CURRENT_USER\" + key);

                using (RegistryKey pk = k.CreateSubKey("PlugIn")) pk.SetValue("FileName", rhp);
                using (RegistryKey ck = k.CreateSubKey("CommandList"))
                {
                    ck.SetValue("StripeOnSurface", "2;StripeOnSurface");
                    ck.SetValue("StripeSelfTest", "2;StripeSelfTest");
                }
            }
            Log("  √ 已注册插件：" + key);
        }

        void InstallToolbar(RhinoInstall r, string ruiSrc)
        {
            string userData = UserData(r.Major);

            // 1) 经典位置（Rhino 7 用）
            string uiDir = Path.Combine(userData, @"UI\Plug-ins");
            Directory.CreateDirectory(uiDir);
            string ruiDst = Path.Combine(uiDir, "StripeOnSurface.rui");
            File.Copy(ruiSrc, ruiDst, true);
            Log("  √ 工具条文件：" + ruiDst);

            // 2) Rhino 8 工作区登记（<files> + <dock_bars>）
            int patched = PatchWorkspaces(r.Major, ruiDst, Log);
            if (patched == 0)
            {
                Log(string.Format("  ! 还没找到 Rhino {0} 的界面配置文件（一般是首次安装、Rhino 还没启动过）。", r.Major));
                Log("    先启动一次 Rhino，再运行本程序，图标就会自动出现。");
            }
        }

        // ==================================================================
        static int PatchWorkspaces(int major, string ruiPath, Action<string> log)
        {
            int patched = 0;
            string settings = Path.Combine(UserData(major), "settings");
            if (!Directory.Exists(settings)) return 0;

            foreach (string schemeDir in Directory.GetDirectories(settings))
            {
                string containers = Path.Combine(schemeDir, "containers.xml");
                if (File.Exists(containers))
                {
                    if (InjectFileEntry(containers, ruiPath, "<plug_in_files>", log)) patched++;
                }
                string wsDir = Path.Combine(schemeDir, "workspaces");
                if (!Directory.Exists(wsDir)) continue;
                foreach (string ws in Directory.GetFiles(wsDir, "*.xml"))
                {
                    bool a = InjectFileEntry(ws, ruiPath, "<files>", log);
                    bool b = InjectDockBar(ws, ruiPath, log);
                    if (a || b) patched++;
                }
            }
            if (patched == 0)
                log("  ! 未能写入工作区配置（可能 Rhino 从未启动过，或配置只读）");
            return patched;
        }

        static bool InjectFileEntry(string file, string ruiPath, string anchor, Action<string> log)
        {
            try
            {
                string s = File.ReadAllText(file, Encoding.UTF8);
                if (s.Contains("StripeOnSurface")) return false;
                string entry = string.Format(
                    "<file_name guid=\"{0}\" plug_in_guid=\"{1}\" source=\"PlugInFolder\">{2}</file_name>",
                    Program.FileGuid, Program.PluginGuid, ruiPath);
                string outp = Replace(s, Regex.Escape(anchor), m => m.Value + entry);
                if (outp == s) return false;
                Backup(file);
                File.WriteAllText(file, outp, new UTF8Encoding(true));
                log("  √ 已登记到：" + file);
                return true;
            }
            catch (Exception ex)
            {
                log("  × 写入失败 " + file + " : " + ex.Message);
                return false;
            }
        }

        static bool InjectDockBar(string file, string ruiPath, Action<string> log)
        {
            try
            {
                string s = File.ReadAllText(file, Encoding.UTF8);
                if (s.Contains(Program.DockGuid)) return false;

                string groupGuid, barGuid;
                if (!ReadRuiGuids(ruiPath, out groupGuid, out barGuid)) return false;

                string block = string.Format(
                    "<dock_bar guid=\"{0}\" source_group_file=\"{1}\" source_group=\"{2}\">" +
                    "<placement dock_location=\"Floating\" recent_dock_location=\"Left\" float_point=\"180,180\" float_size=\"90,52\" />" +
                    "<tabs name=\"表面条纹\" selected_item=\"{3}\" display_style=\"BitmapAndText\">" +
                    "<name><locale_2052>表面条纹</locale_2052><locale_1033>StripeOnSurface</locale_1033></name>" +
                    "<tool_bar guid=\"{3}\" file=\"{1}\" side_bar_file=\"{1}\" />" +
                    "</tabs></dock_bar>",
                    Program.DockGuid, Program.FileGuid, groupGuid, barGuid);

                string outp = Replace(s, "(<dock_bars[^>]*>)", m => m.Value + block);
                if (outp == s) return false;
                Backup(file);
                File.WriteAllText(file, outp, new UTF8Encoding(true));
                log("  √ 已加入工具条显示： " + Path.GetFileName(file));
                return true;
            }
            catch (Exception ex)
            {
                log("  × 写入工具条失败 " + file + " : " + ex.Message);
                return false;
            }
        }

        static void RemoveWorkspaceEntries(int major, Action<string> log)
        {
            string settings = Path.Combine(UserData(major), "settings");
            if (!Directory.Exists(settings)) return;
            foreach (string f in Directory.GetFiles(settings, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    string s = File.ReadAllText(f, Encoding.UTF8);
                    if (!s.Contains("StripeOnSurface") && !s.Contains(Program.DockGuid)) continue;
                    string outp = Regex.Replace(s, "<file_name[^>]*StripeOnSurface[^<]*</file_name>", "");
                    outp = Regex.Replace(outp, "<dock_bar[^>]*source_group_file=\"[^\"]*\"[^>]*>.*?</dock_bar>", "",
                        RegexOptions.Singleline);
                    if (outp == s) continue;
                    Backup(f);
                    File.WriteAllText(f, outp, new UTF8Encoding(true));
                    log("  √ 已从配置移除：" + f);
                }
                catch { }
            }
        }

        static bool ReadRuiGuids(string ruiPath, out string groupGuid, out string barGuid)
        {
            groupGuid = null;
            barGuid = null;
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.Load(ruiPath);
                System.Xml.XmlNode g = doc.SelectSingleNode("/RhinoUI/tool_bar_groups/tool_bar_group");
                System.Xml.XmlNode t = doc.SelectSingleNode("/RhinoUI/tool_bars/tool_bar");
                if (g != null) groupGuid = g.Attributes["guid"].Value;
                if (t != null) barGuid = t.Attributes["guid"].Value;
                return groupGuid != null && barGuid != null;
            }
            catch { return false; }
        }

        static string Replace(string input, string pattern, MatchEvaluator ev)
        {
            return Regex.Replace(input, pattern, ev, RegexOptions.None);
        }

        static void Backup(string file)
        {
            try
            {
                string bak = file + ".bak";
                if (!File.Exists(bak)) File.Copy(file, bak, false);
            }
            catch { }
        }
    }
}
