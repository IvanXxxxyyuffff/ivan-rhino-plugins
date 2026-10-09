using System;
using Rhino;
using Rhino.Commands;
using VapeVolume.Core;

namespace VapeVolume.Commands
{
    /// <summary>
    /// VapeVolumeWatch —— 常驻实时监视开关。
    /// 打开后视口上持续显示悬停 / 选中物件的容积，改模型时数值自动刷新；
    /// 再次运行本命令关闭。
    /// 计算在主线程空闲时进行，绘制回调只画已算好的文本。
    /// </summary>
    public sealed class VapeVolumeWatchCommand : Command
    {
        static VolumeHudConduit _watch;
        static int _lastTick;

        public override string EnglishName => "VapeVolumeWatch";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (_watch != null && _watch.Enabled)
            {
                _watch.Enabled = false;
                _watch = null;
                RhinoApp.Idle -= OnIdle;
                RhinoApp.WriteLine("实时容量监视：已关闭。");
                Redraw(doc);
                return Result.Success;
            }

            VolumeSettings st = VolumeSettings.Load();

            _watch = new VolumeHudConduit { Doc = doc, Settings = st };
            _watch.Enabled = true;

            RhinoApp.Idle -= OnIdle;
            RhinoApp.Idle += OnIdle;

            RhinoApp.WriteLine("实时容量监视：已开启。悬停或选中物件即显示容积；再次运行 VapeVolumeWatch 关闭。");
            Redraw(doc);
            return Result.Success;
        }

        static void OnIdle(object sender, EventArgs e)
        {
            var hud = _watch;
            if (hud == null || !hud.Enabled) return;

            int now = Environment.TickCount;
            if (now - _lastTick < 150) return;      // 节流：最多约 6 次/秒
            _lastTick = now;

            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (hud.Refresh(doc) && doc != null)
                {
                    doc.Views.Redraw();
                }
            }
            catch
            {
                // 刷新异常不影响 Rhino
            }
        }

        static void Redraw(RhinoDoc doc)
        {
            try
            {
                if (doc != null && doc.Views != null) doc.Views.Redraw();
            }
            catch
            {
                // 重绘失败无所谓
            }
        }
    }
}
