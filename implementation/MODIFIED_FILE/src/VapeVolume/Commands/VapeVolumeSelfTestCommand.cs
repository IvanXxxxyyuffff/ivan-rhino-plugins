using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using VapeVolume.Core;

namespace VapeVolume.Commands
{
    /// <summary>
    /// VapeVolumeSelfTest —— 自检命令（Rhino 7 / 8 通用）。
    /// 在当前文档临时造几个测试形体，跑一遍核心算法，把结果打到命令行并写一份报告文件，
    /// 最后把测试形体删掉（整个过程合并为一步撤销）。
    /// </summary>
    public sealed class VapeVolumeSelfTestCommand : Command
    {
        public override string EnglishName => "VapeVolumeSelfTest";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var log = new List<string>();
            var created = new List<Guid>();

            uint undo = 0;
            try { undo = doc.BeginUndoRecord("VapeVolume 自检"); } catch { undo = 0; }

            try
            {
                VolumeSelfTest.Run(doc, log, created);
            }
            catch (Exception ex)
            {
                log.Add("EXCEPTION: " + ex.Message);
            }

            // 清掉测试形体
            foreach (var id in created)
            {
                try { doc.Objects.Delete(id, true); } catch { }
            }
            try { if (undo != 0) doc.EndUndoRecord(undo); } catch { }
            try { doc.Views.Redraw(); } catch { }

            RhinoApp.WriteLine("");
            RhinoApp.WriteLine("═══ VapeVolume 自检 ═══");
            for (int i = 0; i < log.Count; i++)
            {
                RhinoApp.WriteLine(log[i]);
            }
            RhinoApp.WriteLine("═══ 自检结束，测试形体已清除 ═══");

            WriteReport(log);
            return Result.Success;
        }

        static void WriteReport(List<string> log)
        {
            string text = string.Join(Environment.NewLine, log.ToArray()) + Environment.NewLine;
            string[] paths =
            {
                @"D:\UserData\Desktop\VapeVolume自检结果.txt",
                @"C:\zct\live_result.txt"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    System.IO.File.WriteAllText(paths[i], text, System.Text.Encoding.UTF8);
                    RhinoApp.WriteLine("自检报告已写入 " + paths[i]);
                }
                catch
                {
                    // 写不进去也不影响命令行输出
                }
            }
        }
    }
}
