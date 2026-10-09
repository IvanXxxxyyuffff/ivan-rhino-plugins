using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;   // [assembly: Guid] 需要（否则 Guid 解析成 System.Guid）
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.PlugIns;

// Rhino 按「程序集级」[assembly: Guid] 识别插件 ID；只写类级 [Guid] 会被登记成全零 GUID。
#if RH7
[assembly: Guid("3F8A2C64-1D75-4B93-A6E2-5C70491B8D3F")]
#else
[assembly: Guid("8B1E47D2-6A35-4C09-9F82-3D6E15A7B0C4")]
#endif

namespace IvanMeshFix
{
    /// <summary>
    /// 网格修复（Rhino 7 / 8 用不同 GUID，避免同机冲突）。
    ///
    /// 这是一个**无面板**工具：装好后工具条上就一个图标，点一下即修复 ——
    /// 有选中物件就修选中的，没选中就扫描整份文件里渲染网格退化的物件。
    /// </summary>
#if RH7
    [Guid("3F8A2C64-1D75-4B93-A6E2-5C70491B8D3F")]
#else
    [Guid("8B1E47D2-6A35-4C09-9F82-3D6E15A7B0C4")]
#endif
    public class MeshFixPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            RhinoApp.WriteLine("网格修复插件已加载。命令：MeshFix（选中物件点图标即修复；未选中则扫描整份文件）");
            return LoadReturnCode.Success;
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-meshfix-selftest.flag，
        /// 下次启动 Rhino 自动跑自检并写报告到同目录 MeshFixSelfTest.txt，跑完退出。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-meshfix-selftest.flag");
                if (!File.Exists(flag)) return;
                File.Delete(flag);
                RhinoApp.Idle += SelfTestOnIdle;
            }
            catch { }
        }

        static void SelfTestOnIdle(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= SelfTestOnIdle;
                MeshFixSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, MeshFixSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("网格修复自检失败：" + ex.Message); } catch { }
            }
        }
    }

    /// <summary>
    /// 一键修复。工具条按钮的宏就是 `! _MeshFix`，所以这里绝不能弹面板 / 不能进 Get 循环，
    /// 必须一次调用跑完并直接返回。
    /// </summary>
    public class MeshFixCommand : Command
    {
        public override string EnglishName { get { return "MeshFix"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (doc == null) return Result.Failure;

            var selected = new List<RhinoObject>();
            try
            {
                foreach (RhinoObject o in doc.Objects.GetSelectedObjects(false, false))
                    if (o != null) selected.Add(o);
            }
            catch { }

            bool useSelection = selected.Count > 0;
            IEnumerable<RhinoObject> scope;
            if (useSelection)
            {
                scope = selected;
            }
            else
            {
                var all = new List<RhinoObject>();
                try
                {
                    var objs = doc.Objects.GetObjectList(ObjectType.AnyObject);
                    if (objs != null) all.AddRange(objs);
                }
                catch { }
                scope = all;
            }

            RhinoApp.WriteLine(useSelection
                ? string.Format("检查所选 {0} 个物件的渲染网格…", selected.Count)
                : "没有选中物件：扫描整份文件里渲染网格退化的物件…");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            MeshFixCore.FixStats st = MeshFixCore.Run(doc, scope);
            sw.Stop();

            foreach (string line in st.Lines) RhinoApp.WriteLine(line);

            if (st.Candidates == 0)
            {
                RhinoApp.WriteLine(useSelection
                    ? string.Format("所选物件的渲染网格都正常，无需修复（检查 {0} 个物件，{1} ms）。", st.Scanned, sw.ElapsedMilliseconds)
                    : string.Format("整份文件里没有渲染网格退化的物件，无需修复（扫描 {0} 个物件，{1} ms）。", st.Scanned, sw.ElapsedMilliseconds));
            }
            else
            {
                RhinoApp.WriteLine(string.Format(
                    "修复完成：发现退化 {0} 个，修复 {1} 个，跳过 {2} 个，仍失败 {3} 个（{4} ms）。",
                    st.Candidates, st.Fixed, st.Skipped, st.Failed, sw.ElapsedMilliseconds));
            }

            try { doc.Views.Redraw(); } catch { }
            return Result.Success;
        }
    }
}
