using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace StripeOnSurface
{
    public class StripeOnSurfaceCommand : Command
    {
        public override string EnglishName { get { return "StripeOnSurface"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var settings = new StripeSettings();
            // 面板单位是 mm：按文档单位换算
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            // 不看文档预选：永远用空目标打开面板，由面板上的「选择物件」按钮来选
            // （非阻塞：面板以浮动窗口打开，命令立即结束。之后可继续在 Rhino 里移动/编辑目标曲面，
            //   预览实时更新；点「生成」在面板关闭时写入图层。）
            var session = new StripeSession(doc, Guid.Empty, settings, StripeCore.EmptyTargetHint);
            session.Start();
            RhinoApp.WriteLine("参数面板已打开。点面板上的「选择物件」按钮选目标曲面（多重曲面会按一个整体排条纹）。");
            return Result.Success;
        }
    }

    /// <summary>面板上「选择物件」按钮调用的隐藏命令（面板是非模态的，GetObject 只能在命令上下文里跑）</summary>
    public class StripePickTargetCommand : Command
    {
        public override string EnglishName { get { return "StripePickTarget"; } }
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            StripeSession session = StripeSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 StripeOnSurface 打开参数面板。");
                return Result.Nothing;
            }

            var go = new GetObject();
            go.SetCommandPrompt("选择要在其表面生成条纹的曲面或多重曲面");
            go.GeometryFilter = ObjectType.Surface | ObjectType.PolysrfFilter;
            go.SubObjectSelect = false;
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            ObjRef objRef = go.Object(0);
            if (objRef == null || objRef.ObjectId == Guid.Empty)
            {
                session.MarkTargetInvalid("没有选到物件，请点「选择物件」重选。");
                return Result.Failure;
            }
            Brep brep = objRef.Brep();
            if (brep == null)
            {
                RhinoApp.WriteLine("所选对象不含可用曲面。");
                session.MarkTargetInvalid("所选对象不含可用曲面，请换一个。");
                return Result.Failure;
            }

            session.SetTarget(doc, objRef.ObjectId, StripeCore.DescribeObject(doc, objRef));
            RhinoApp.WriteLine("目标物件已更新：{0}", StripeCore.DescribeObject(doc, objRef));
            return Result.Success;
        }
    }

    internal static class StripeCore
    {
        /// <summary>没有目标物件时，面板头部与状态条显示的提示</summary>
        internal const string EmptyTargetHint = "未选择物件 —— 点「选择物件」按钮选目标曲面";

        internal static string DescribeObject(RhinoDoc doc, ObjRef objRef)
        {
            string name = null;
            RhinoObject obj = objRef.Object();
            if (obj != null) name = obj.Name;
            if (string.IsNullOrEmpty(name))
            {
                RhinoObject o = doc.Objects.FindId(objRef.ObjectId);
                if (o != null) name = o.Name;
            }
            string id = objRef.ObjectId.ToString();
            if (id.Length > 8) id = id.Substring(0, 8);
            return string.IsNullOrEmpty(name) ? ("目标对象 " + id) : ("目标：" + name);
        }

        internal static int EnsureLayer(RhinoDoc doc, string name)
        {
            foreach (Layer l in doc.Layers)
            {
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
            }
            var nl = new Layer();
            nl.Name = name;
            nl.Color = System.Drawing.Color.FromArgb(0, 150, 136);
            return doc.Layers.Add(nl);
        }
    }
}
