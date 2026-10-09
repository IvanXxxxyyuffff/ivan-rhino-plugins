using System;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.PlugIns;

// Rhino 8 的插件加载器按「程序集级」[assembly: Guid] 识别插件 ID（库存插件如 Grasshopper/IronPython 都是这样）；
// 只有类级 [Guid] 时 Rhino 读不到 ID，会把插件登记成全零 GUID，随后其它插件加载就报「ID 已被使用」。
[assembly: Guid("B7A1C3D2-5E64-4F7A-9B21-3C0D5E7F8A91")]

namespace StripeOnSurface
{
    /// <summary>插件入口（.rhp）</summary>
    [Guid("B7A1C3D2-5E64-4F7A-9B21-3C0D5E7F8A91")]
    public class StripeOnSurfacePlugin : PlugIn
    {
        static StripeOnSurfacePlugin _instance;

        public StripeOnSurfacePlugin()
        {
            _instance = this;
        }

        public static StripeOnSurfacePlugin Instance
        {
            get { return _instance; }
        }

        public override PlugInLoadTime LoadTime
        {
            get { return PlugInLoadTime.AtStartup; }
        }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            try { ShowToolbar(); }
            catch (Exception ex) { RhinoApp.WriteLine("表面条纹工具条加载失败：" + ex.Message); }

            LogLoad();
            MaybeRunSelfTest();
            RhinoApp.WriteLine("表面条纹插件已加载。命令：StripeOnSurface（生成条纹）/ StripeSelfTest（自检）");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放一个 run-selftest.flag，
        /// 下次启动 Rhino 会自动跑几何自检并把报告写到同目录 StripeSelfTest.txt（跑完删除标志）。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
                string flag = System.IO.Path.Combine(dir, "run-selftest.flag");
                string keep = System.IO.Path.Combine(dir, "run-selftest-keep.flag");
                bool dump = System.IO.File.Exists(keep);
                if (!System.IO.File.Exists(flag) && !dump) return;
                if (System.IO.File.Exists(flag)) System.IO.File.Delete(flag);
                if (dump)
                {
                    System.IO.File.Delete(keep);
                    StripeSelfTestCommand.DumpGeometry = true;
                    StripeSelfTestCommand.DumpDocPath = System.IO.Path.Combine(dir, "StripeSelfTest.3dm");
                }
                _selftestExit = !dump;
                RhinoApp.Idle += SelfTestOnIdle;
            }
            catch { }
        }

        static bool _selftestExit = true;

        static void SelfTestOnIdle(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= SelfTestOnIdle;
                StripeSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, StripeSelfTestCommand.DefaultReportPath, _selftestExit);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("自检失败：" + ex.Message); } catch { }
            }
        }

        /// <summary>加载日志：排查「插件没加载/命令不存在」时先看这个文件</summary>
        static void LogLoad()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                System.IO.Directory.CreateDirectory(dir);
                string ver = "";
                try { ver = RhinoApp.Version.ToString(); } catch { }
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "StripeOnSurface-load.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  Rhino " + ver +
                    "  已加载  " + System.Reflection.Assembly.GetExecutingAssembly().Location + "\r\n");
            }
            catch { }
        }

        /// <summary>把自带工具条（同目录同名 .rui）安装到 Rhino 的插件工具条目录，安装后图标自动出现</summary>
        static void ShowToolbar()
        {
            string asmPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dir = System.IO.Path.GetDirectoryName(asmPath);
            if (string.IsNullOrEmpty(dir)) return;
            string src = System.IO.Path.Combine(dir, "StripeOnSurface.rui");
            if (!System.IO.File.Exists(src)) return;

            int major = 8;
            try { major = RhinoApp.Version.Major; } catch { }

            string destDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"McNeel\Rhinoceros" + System.IO.Path.DirectorySeparatorChar + major + @".0\UI\Plug-ins");
            try
            {
                System.IO.Directory.CreateDirectory(destDir);
                string dst = System.IO.Path.Combine(destDir, "StripeOnSurface.rui");
                if (!System.IO.File.Exists(dst) ||
                    System.IO.File.GetLastWriteTimeUtc(dst) < System.IO.File.GetLastWriteTimeUtc(src))
                {
                    System.IO.File.Copy(src, dst, true);
                    RhinoApp.WriteLine("表面条纹工具条已安装：" + dst + "（若未显示，右键工具栏勾选 StripeOnSurface）");
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("工具条安装失败：" + ex.Message);
            }
        }
    }
}
