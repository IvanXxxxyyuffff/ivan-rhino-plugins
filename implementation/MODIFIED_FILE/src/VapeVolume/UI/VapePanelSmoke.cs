using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Rhino;

namespace VapeVolume.UI
{
    /// <summary>
    /// 烟油面板冒烟（无人值守用）：真构造面板 → 重绘两遍 → 走关闭路径。
    /// 界面代码不跑一次就不知道会不会在构造/绘制时抛异常 —— 用户点开面板直接崩、或者关不掉吃点击，都是最难受的。
    /// 这个用例是这一轮 UI 换成 WinForms 之后的验收手段（原来烟油自检只测算法，面板没人碰过）。
    /// </summary>
    internal static class VapePanelSmoke
    {
        public static string Run(RhinoDoc doc)
        {
            var sb = new StringBuilder();
            VolumeDialog dlg = null;
            try
            {
                VolumeDialog.OpenEmpty(doc);
                dlg = VolumeDialog.Current;
                if (dlg == null) { sb.AppendLine("[FAIL] OpenEmpty 后面板实例为 null"); return sb.ToString(); }
                Application.DoEvents();
                System.Threading.Thread.Sleep(420);          // 等进入动画跑完
                Application.DoEvents();

                bool okShape = dlg.ClientSize.Width >= 400 && dlg.ClientSize.Height >= 500;
                sb.AppendLine(string.Format("[{0}] 面板构造成功（{1} 个控件，{2}x{3}）",
                    okShape ? "PASS" : "FAIL", dlg.Controls.Count, dlg.ClientSize.Width, dlg.ClientSize.Height));

                try
                {
                    // 必须先真显示一下：DrawToBitmap 对「从没显示过」的窗体只会画出标题栏
                    var rect = new Rectangle(0, 0, dlg.ClientSize.Width, dlg.ClientSize.Height);
                    string png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VapePanel-smoke.png");
                    using (var bmp = new Bitmap(dlg.ClientSize.Width, dlg.ClientSize.Height))
                    {
                        dlg.DrawToBitmap(bmp, rect);
                        dlg.DrawToBitmap(bmp, rect);
                        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    sb.AppendLine("[PASS] 面板整窗重绘两遍无异常（截图 " + png + "）");
                }
                catch (Exception ex) { sb.AppendLine("[FAIL] 面板重绘异常：" + ex.Message); }

                // 关闭路径：退出动画跑完必须真的把窗体关掉（曾经 Motion 丢 done 回调 → 面板透明还吃点击 → Rhino 卡死）
                dlg.Close();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!dlg.IsDisposed && sw.ElapsedMilliseconds < 3000)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                }
                sb.AppendLine(string.Format("[{0}] 关闭动画跑完后窗体真的关掉了（{1} ms）",
                    dlg.IsDisposed ? "PASS" : "FAIL", sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                sb.AppendLine("[FAIL] 面板冒烟异常：" + ex);
            }
            return sb.ToString();
        }
    }
}
