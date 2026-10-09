using System;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using VapeVolume.UI;

namespace VapeVolume.Commands
{
    /// <summary>
    /// VapePickTarget —— 容量窗口上「选择物件」按钮调用的隐藏命令。
    /// 窗口是非模态且置顶的，GetObject 只能在命令上下文里跑，所以按钮只能 RunScript 这个命令。
    ///
    /// 注意：这里是**单次拾取**，不循环调用 Get()。
    /// 官方文档明确说明：EnablePreSelect 打开时，若调用 Get() 时已有预选物件，
    /// 该物件会被立刻返回 —— 在循环里反复 Get() 会瞬间无限空转，把 Rhino 卡死。
    ///
    /// 按钮颜色口径：拿到物件但不可用（对象失效 / 没有几何 / 算不出体积）→ 面板按钮回红；
    /// 用户按 Esc 或没选到（取消拾取）→ 面板保持原状，不动颜色。
    /// </summary>
    public sealed class VapePickTargetCommand : Command
    {
        public override string EnglishName => "VapePickTarget";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            VolumeDialog dlg = VolumeDialog.Current;
            if (dlg == null)
            {
                RhinoApp.WriteLine("请先运行 VapeVolume 打开容量窗口。");
                return Result.Nothing;
            }

            var go = new GetObject();
            go.SetCommandPrompt("选择要计算烟油容量的物件");
            go.GeometryFilter = VapeVolumeCommand.PickFilter;
            go.SubObjectSelect = false;
            go.EnablePreSelect(true, true);   // 视图里已经点选好的物件直接算数（仍然是单次 Get，不会空转）
            go.AcceptNothing(true);
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();
            if (go.ObjectCount <= 0)
            {
                RhinoApp.WriteLine("没有选择物件，容量窗口保持「未选择物件」状态。");
                return Result.Nothing;
            }

            ObjRef oref = go.Object(0);
            RhinoObject obj = oref != null ? oref.Object() : null;
            if (obj == null)
            {
                dlg.MarkPickInvalid("拾取到的物件已失效，请重新选择。");
                return Result.Failure;
            }

            bool hasGeometry;
            try { hasGeometry = obj.Geometry != null; }
            catch { hasGeometry = false; }
            if (!hasGeometry)
            {
                dlg.MarkPickInvalid("拾取到的物件没有可用几何，无法计算容量，请换一个物件。");
                return Result.Failure;
            }

            // 算不出体积的情况由 SetTarget 内部置红（它才是真正跑 Compute 的地方）
            dlg.SetTarget(doc, obj);
            return Result.Success;
        }
    }
}
