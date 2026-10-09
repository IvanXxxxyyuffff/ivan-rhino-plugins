using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;

namespace VapeVolume.Commands
{
    /// <summary>
    /// VapeVolume —— 主命令。
    /// 启动流程固定为「先开窗口，再在窗口里选物件」：
    /// 命令**不读**文档里的预选物件，永远以空目标打开窗口，
    /// 目标一律由窗口上的「选择物件」按钮（→ VapePickTarget 命令）来选。
    ///
    /// 窗口显示 mL、转换率滑块（50–75%）、油杯 / 油的体积 两种模式；选「油杯」时按壁厚（默认 0.6mm）向内扣减后计算。
    /// </summary>
    public sealed class VapeVolumeCommand : Command
    {
        public override string EnglishName => "VapeVolume";

        /// <summary>可参与计算的物件类型（与拾取命令的 GeometryFilter 保持一致）。</summary>
        internal static readonly ObjectType PickFilter =
            ObjectType.Surface | ObjectType.Brep | ObjectType.Mesh | ObjectType.Extrusion | ObjectType.SubD;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // 去掉「文档里有预选物件就直接用」的分支：不再调用 GetSelectedObjects，
            // 永远以空目标打开容量窗口，由窗口上的「选择物件」按钮（VapePickTarget）来定目标。
            VapeVolume.UI.VolumeDialog.OpenEmpty(doc);
            RhinoApp.WriteLine("点面板上的「选择物件」按钮选目标（也可以直接在视图里点选后再点按钮）。");
            return Result.Success;
        }
    }
}
