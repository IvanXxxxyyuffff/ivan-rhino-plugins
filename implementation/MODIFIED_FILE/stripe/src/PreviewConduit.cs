using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace StripeOnSurface
{
    /// <summary>参数面板打开时的实时预览（不进文档，不占用对象）</summary>
    public class StripePreviewConduit : DisplayConduit
    {
        public List<Curve> Curves = new List<Curve>();

        static readonly Color StripeColor = Color.FromArgb(255, 0, 150, 136);

        protected override void DrawForeground(DrawEventArgs e)
        {
            List<Curve> list = Curves;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Curve c = list[i];
                if (c == null || !c.IsValid) continue;
                try
                {
                    e.Display.DrawCurve(c, StripeColor, 3);
                }
                catch { }
            }
        }

    }
}
