using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Rhino 8 的插件加载器按「程序集级」[assembly: Guid] 识别插件 ID（库存插件如 Grasshopper/IronPython 都是这样）；
// 只有类级 [Guid] 时 Rhino 读不到 ID，会把插件登记成全零 GUID，随后其它插件加载就报「ID 已被使用」。
#if NET48
// Rhino 7 版用独立 GUID：两个版本同时装在机器上时不会被互相误认
[assembly: Guid("C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01")]
#else
// Rhino 8 版
[assembly: Guid("A3F27B54-9C41-4E88-B0D6-7E5C1A93D842")]
#endif

namespace VapeVolume
{
    /// <summary>
    /// 烟油容量计算器（VapeVolume）Rhino 8 插件入口。
    /// 命令：VapeVolume（选物件算毫升，悬停实时显示）、VapeVolumeWatch（常驻实时监视开关）。
    /// </summary>
#if NET48
    // Rhino 7 版用独立 GUID：两个版本同时装在机器上时不会被互相误认
    [Guid("C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01")]
#else
    // Rhino 8 版
    [Guid("A3F27B54-9C41-4E88-B0D6-7E5C1A93D842")]
#endif
    public sealed class VapeVolumePlugIn : PlugIn
    {
        public VapeVolumePlugIn()
        {
            Instance = this;
        }

        /// <summary>插件单例，用于访问持久化设置。</summary>
        public static VapeVolumePlugIn Instance { get; private set; }

        /// <summary>随 Rhino 启动加载，好在打开含活标注的文件时立刻开始刷新。</summary>
        public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            Rhino.RhinoApp.WriteLine(
                "烟油容量计算器 VapeVolume 已加载：" +
                "VapeVolume = 选物件算毫升（悬停实时显示）；" +
                "VapeVolumeWatch = 常驻实时监视开关。");

            // 活标注刷新器：物件尺寸一变，标注自动重算
            VapeVolume.Core.LiveAnnotationWatch.Install();

            WriteLoadLog();
            MaybeRunSelfTest();
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-vape-selftest.flag，
        /// 下次启动 Rhino 自动跑核心自检 + **面板冒烟**，写报告到同目录 VapeVolumeSelfTest.txt，跑完退出。
        /// （烟油原来只能手动敲命令跑自检、而且只测算法 —— UI 改完没人点开过是盲区。）
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
                string flag = System.IO.Path.Combine(dir, "run-vape-selftest.flag");
                if (!System.IO.File.Exists(flag)) return;
                System.IO.File.Delete(flag);
                Rhino.RhinoApp.Idle += SelfTestOnIdle;
            }
            catch { }
        }

        static void SelfTestOnIdle(object sender, EventArgs e)
        {
            try
            {
                Rhino.RhinoApp.Idle -= SelfTestOnIdle;
                Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
                var log = new List<string>();
                var created = new List<Guid>();
                uint undo = 0;
                try { undo = doc.BeginUndoRecord("VapeVolume 自检"); } catch { undo = 0; }
                try { VapeVolume.Core.VolumeSelfTest.Run(doc, log, created); }
                catch (Exception ex) { log.Add("EXCEPTION: " + ex.Message); }
                foreach (var id in created) { try { doc.Objects.Delete(id, true); } catch { } }
                try { if (undo != 0) doc.EndUndoRecord(undo); } catch { }
                try { doc.Views.Redraw(); } catch { }

                log.Add("");
                log.Add("── 面板冒烟（UI 换 WinForms 后的验收）──");
                log.Add(VapeVolume.UI.VapePanelSmoke.Run(doc).TrimEnd());

                int pass = 0, fail = 0;
                for (int i = 0; i < log.Count; i++)
                {
                    // 一条 log 里可能塞了多行（面板冒烟整段），逐行统计才不会把 3 项算成 1 项
                    string[] parts = log[i].Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int k = 0; k < parts.Length; k++)
                    {
                        if (parts[k].StartsWith("[PASS]")) pass++;
                        else if (parts[k].StartsWith("[FAIL]")) fail++;
                    }
                }
                log.Add(string.Format("RESULT: {0} 通过 / {1} 失败 -> {2}", pass, fail, fail == 0 ? "PASS" : "FAIL"));

                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
                string text = string.Join(Environment.NewLine, log.ToArray()) + Environment.NewLine;
                try { System.IO.Directory.CreateDirectory(dir); } catch { }
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "VapeVolumeSelfTest.txt"), text, System.Text.Encoding.UTF8);
                Rhino.RhinoApp.WriteLine(text);
                try { doc.Modified = false; } catch { }
            }
            catch { }
            Rhino.RhinoApp.Exit();
        }

        /// <summary>
        /// 往 %TEMP%\VapeVolume_load.log 追加一行加载记录。
        /// 用来确认插件到底有没有被 Rhino 加载（排查“装了但命令不存在”最直接）。
        /// </summary>
        static void WriteLoadLog()
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VapeVolume_load.log");
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[{0:yyyy-MM-dd HH:mm:ss}] VapeVolume v{1} 已加载 | Rhino {2} | 文件={3} | 64bit={4}",
                    DateTime.Now,
                    typeof(VapeVolumePlugIn).Assembly.GetName().Version,
                    Rhino.RhinoApp.Version,
                    typeof(VapeVolumePlugIn).Assembly.Location,
                    IntPtr.Size == 8);
                System.IO.File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // 写日志失败不影响插件加载
            }
        }
    }
}
