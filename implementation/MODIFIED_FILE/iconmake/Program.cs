using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// 生成 IVAN-CENTER 的应用图标（多尺寸 .ico）。
// 与插件图标同源：深蓝液态玻璃底板 + 五个工具节点聚合。
namespace IconMake
{
    internal static class Program
    {
        // Native GDI+ glass icon family. Keep this renderer byte-identical in IconMake.
        public static Bitmap CreateGlassIcon(string kind, int size)
        {
            int side = Math.Max(1, size);
            bool isApp = string.Equals(kind, "app", StringComparison.OrdinalIgnoreCase);
            Color accent = GlassAccent(kind);
            Bitmap bitmap = new Bitmap(side, side);
            try
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.Clear(Color.Transparent);
        
                    if (isApp) GlassDrawAppTile(g, side, accent);
                    else GlassDrawTile(g, side, accent, false);
                    if (isApp) GlassDrawApp(g, side);
                    else if (string.Equals(kind, "halftone", StringComparison.OrdinalIgnoreCase)) GlassDrawHalftone(g, side, accent);
                    else if (string.Equals(kind, "voronoi", StringComparison.OrdinalIgnoreCase)) GlassDrawVoronoi(g, side, accent);
                    else if (string.Equals(kind, "radialdots", StringComparison.OrdinalIgnoreCase)) GlassDrawRadialDots(g, side, accent);
                    else if (string.Equals(kind, "vape", StringComparison.OrdinalIgnoreCase)) GlassDrawVape(g, side, accent);
                    else if (string.Equals(kind, "meshfix", StringComparison.OrdinalIgnoreCase)) GlassDrawMeshFix(g, side, accent);
                    else if (string.Equals(kind, "diamond", StringComparison.OrdinalIgnoreCase)) GlassDrawDiamond(g, side, accent);
                    else if (string.Equals(kind, "ripple", StringComparison.OrdinalIgnoreCase)) GlassDrawRipple(g, side, accent);
                    else GlassDrawStripe(g, side, accent);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
        
        private static Color GlassAccent(string kind)
        {
            if (string.Equals(kind, "halftone", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 70, 105, 207);
            if (string.Equals(kind, "voronoi", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 192, 138, 54);
            if (string.Equals(kind, "radialdots", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 139, 105, 188);
            if (string.Equals(kind, "vape", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 44, 151, 211);
            if (string.Equals(kind, "meshfix", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 32, 138, 96);
            if (string.Equals(kind, "diamond", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 92, 76, 208);
            if (string.Equals(kind, "ripple", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 22, 132, 152);
            if (string.Equals(kind, "app", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 38, 59, 89);
            return Color.FromArgb(255, 8, 127, 150);
        }
        
        private static Color GlassBlend(Color from, Color to, float amount)
        {
            float t = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(from.A + (to.A - from.A) * t),
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }
        
        private static Color GlassAlpha(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
        }
        
        private static GraphicsPath GlassRoundPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
        
        private static void GlassDrawTile(Graphics g, int size, Color accent, bool isApp)
        {
            float pad = Math.Max(0.65f, size * 0.055f);
            RectangleF rect = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
            float radius = Math.Max(1.3f, size * 0.22f);
        
            for (int layer = 3; layer >= 1; layer--)
            {
                float dy = size * (0.025f + layer * 0.012f);
                RectangleF shadowRect = new RectangleF(rect.X, rect.Y + dy, rect.Width, rect.Height);
                using (GraphicsPath shadow = GlassRoundPath(shadowRect, radius))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(9 + (4 - layer) * 7, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadow);
            }
        
            using (GraphicsPath tile = GlassRoundPath(rect, radius))
            {
                Color top = isApp ? Color.FromArgb(250, 48, 70, 101) : Color.FromArgb(61, 246, 251, 255);
                Color bottom = isApp ? Color.FromArgb(248, 18, 31, 49) : Color.FromArgb(27, accent.R, accent.G, accent.B);
                using (LinearGradientBrush fill = new LinearGradientBrush(rect, top, bottom, 90f))
                    g.FillPath(fill, tile);
                using (SolidBrush tint = new SolidBrush(Color.FromArgb(isApp ? 24 : 13, accent.R, accent.G, accent.B)))
                    g.FillPath(tint, tile);
                using (Pen darkEdge = new Pen(Color.FromArgb(isApp ? 215 : 115, 12, 20, 32), Math.Max(0.65f, size * 0.052f)))
                    g.DrawPath(darkEdge, tile);
                using (LinearGradientBrush edgeBrush = new LinearGradientBrush(rect,
                    Color.FromArgb(224, 255, 255, 255), Color.FromArgb(isApp ? 170 : 112, accent.R, accent.G, accent.B), 48f))
                using (Pen edge = new Pen(edgeBrush, Math.Max(0.55f, size * 0.037f)))
                    g.DrawPath(edge, tile);
            }
        
            using (GraphicsPath sheen = new GraphicsPath())
            {
                sheen.AddBezier(rect.X + radius * 0.54f, rect.Y + radius * 0.70f,
                    rect.X + radius * 0.72f, rect.Y + size * 0.045f,
                    rect.Right - radius * 0.72f, rect.Y + size * 0.045f,
                    rect.Right - radius * 0.54f, rect.Y + radius * 0.70f);
                using (Pen gloss = new Pen(Color.FromArgb(isApp ? 198 : 210, 255, 255, 255), Math.Max(0.45f, size * 0.014f)))
                {
                    gloss.StartCap = LineCap.Round;
                    gloss.EndCap = LineCap.Round;
                    g.DrawPath(gloss, sheen);
                }
            }
            using (GraphicsPath lower = new GraphicsPath())
            {
                lower.AddBezier(rect.X + radius * 0.75f, rect.Bottom - radius * 0.50f,
                    rect.X + size * 0.25f, rect.Bottom - size * 0.045f,
                    rect.Right - size * 0.25f, rect.Bottom - size * 0.045f,
                    rect.Right - radius * 0.75f, rect.Bottom - radius * 0.50f);
                using (Pen refract = new Pen(Color.FromArgb(50, 255, 255, 255), Math.Max(0.4f, size * 0.012f)))
                {
                    refract.StartCap = LineCap.Round;
                    refract.EndCap = LineCap.Round;
                    g.DrawPath(refract, lower);
                }
            }
        }
        
        private static void GlassDrawAppTile(Graphics g, int size, Color accent)
        {
            float pad = Math.Max(0.65f, size * 0.055f);
            RectangleF rect = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
            float radius = Math.Max(1.3f, size * 0.22f);
            using (GraphicsPath shadow = GlassRoundPath(new RectangleF(rect.X, rect.Y + size * 0.045f, rect.Width, rect.Height), radius))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(24, 13, 34, 61)))
                g.FillPath(shadowBrush, shadow);
        
            using (GraphicsPath tile = GlassRoundPath(rect, radius))
            {
                Color glassBlue = GlassBlend(Color.FromArgb(255, 62, 139, 220), accent, 0.10f);
                Color top = Color.FromArgb(216, 113, 186, 248);
                Color baseBlue = GlassBlend(Color.FromArgb(255, 35, 92, 170), accent, 0.10f);
                Color bottom = Color.FromArgb(210, baseBlue.R, baseBlue.G, baseBlue.B);
                using (LinearGradientBrush fill = new LinearGradientBrush(rect, top, bottom, 132f))
                    g.FillPath(fill, tile);
                using (SolidBrush haze = new SolidBrush(Color.FromArgb(24, 226, 244, 255)))
                    g.FillPath(haze, tile);
                using (LinearGradientBrush edgeBrush = new LinearGradientBrush(rect,
                    Color.FromArgb(238, 255, 255, 255), Color.FromArgb(117, 213, 237, 255), 52f))
                using (Pen edge = new Pen(edgeBrush, Math.Max(0.58f, size * 0.027f)))
                    g.DrawPath(edge, tile);
                using (Pen lowerRefraction = new Pen(Color.FromArgb(82, glassBlue.R, glassBlue.G, glassBlue.B), Math.Max(0.45f, size * 0.016f)))
                {
                    lowerRefraction.StartCap = LineCap.Round;
                    lowerRefraction.EndCap = LineCap.Round;
                    g.DrawArc(lowerRefraction, rect.X + radius * 0.55f, rect.Y + radius * 0.40f,
                        rect.Width - radius * 1.10f, rect.Height - radius * 0.80f, 18f, 142f);
                }
            }
        
            using (GraphicsPath gloss = new GraphicsPath())
            {
                gloss.AddBezier(rect.X + radius * 0.56f, rect.Y + radius * 0.66f,
                    rect.X + radius * 0.77f, rect.Y + size * 0.045f,
                    rect.Right - radius * 0.80f, rect.Y + size * 0.045f,
                    rect.Right - radius * 0.54f, rect.Y + radius * 0.66f);
                using (Pen shine = new Pen(Color.FromArgb(224, 255, 255, 255), Math.Max(0.48f, size * 0.018f)))
                {
                    shine.StartCap = LineCap.Round;
                    shine.EndCap = LineCap.Round;
                    g.DrawPath(shine, gloss);
                }
            }
            using (GraphicsPath gleam = new GraphicsPath())
            {
                gleam.AddBezier(rect.X + size * 0.20f, rect.Y + size * 0.22f,
                    rect.X + size * 0.29f, rect.Y + size * 0.15f,
                    rect.X + size * 0.40f, rect.Y + size * 0.15f,
                    rect.X + size * 0.48f, rect.Y + size * 0.18f);
                using (Pen soft = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(0.42f, size * 0.022f)))
                {
                    soft.StartCap = LineCap.Round;
                    soft.EndCap = LineCap.Round;
                    g.DrawPath(soft, gleam);
                }
            }
        }
        
        private static void GlassDrawRibbon(Graphics g, PointF a, PointF b, Color accent, float width)
        {
            float edgeWidth = Math.Max(width + 0.55f, width * 1.38f);
            using (Pen outline = new Pen(Color.FromArgb(175, 15, 28, 42), edgeWidth))
            {
                outline.StartCap = LineCap.Round;
                outline.EndCap = LineCap.Round;
                g.DrawLine(outline, a, b);
            }
            RectangleF bounds = new RectangleF(Math.Min(a.X, b.X) - width, Math.Min(a.Y, b.Y) - width,
                Math.Abs(a.X - b.X) + width * 2f, Math.Abs(a.Y - b.Y) + width * 2f);
            using (LinearGradientBrush brush = new LinearGradientBrush(bounds,
                GlassBlend(accent, Color.White, 0.62f), GlassBlend(accent, Color.Black, 0.18f), 48f))
            using (Pen ink = new Pen(brush, width))
            {
                ink.StartCap = LineCap.Round;
                ink.EndCap = LineCap.Round;
                g.DrawLine(ink, a, b);
            }
            using (Pen glint = new Pen(Color.FromArgb(158, 255, 255, 255), Math.Max(0.38f, width * 0.18f)))
            {
                glint.StartCap = LineCap.Round;
                glint.EndCap = LineCap.Round;
                g.DrawLine(glint, new PointF(a.X - width * 0.18f, a.Y - width * 0.16f), new PointF(b.X - width * 0.18f, b.Y - width * 0.16f));
            }
        }
        
        private static void GlassDrawStripe(Graphics g, int size, Color accent)
        {
            float cx = size * 0.5f, cy = size * 0.5f;
            float width = Math.Max(1.05f, size * 0.082f);
            for (int i = 0; i < 3; i++)
            {
                float offset = (i - 1) * size * 0.18f;
                PointF a = new PointF(cx + offset - size * 0.085f, cy + size * 0.17f);
                PointF b = new PointF(cx + offset + size * 0.085f, cy - size * 0.17f);
                GlassDrawRibbon(g, a, b, accent, width);
            }
        }
        
        private static void GlassDrawSphere(Graphics g, RectangleF rect, Color accent)
        {
            RectangleF shadow = new RectangleF(rect.X + rect.Width * 0.08f, rect.Y + rect.Height * 0.13f, rect.Width, rect.Height);
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(100, 13, 24, 39)))
                g.FillEllipse(shade, shadow);
            using (GraphicsPath orb = new GraphicsPath())
            {
                orb.AddEllipse(rect);
                using (PathGradientBrush fill = new PathGradientBrush(orb))
                {
                    fill.CenterPoint = new PointF(rect.X + rect.Width * 0.34f, rect.Y + rect.Height * 0.28f);
                    fill.CenterColor = GlassBlend(accent, Color.White, 0.48f);
                    fill.SurroundColors = new Color[] { GlassBlend(accent, Color.Black, 0.26f) };
                    g.FillPath(fill, orb);
                }
                using (Pen edge = new Pen(Color.FromArgb(178, 24, 37, 52), Math.Max(0.48f, rect.Width * 0.075f)))
                    g.DrawPath(edge, orb);
            }
            RectangleF glint = new RectangleF(rect.X + rect.Width * 0.18f, rect.Y + rect.Height * 0.15f,
                Math.Max(0.25f, rect.Width * 0.20f), Math.Max(0.25f, rect.Height * 0.13f));
            using (SolidBrush shine = new SolidBrush(Color.FromArgb(185, 255, 255, 255)))
                g.FillEllipse(shine, glint);
        }
        
        private static void GlassDrawHalftone(Graphics g, int size, Color accent)
        {
            float center = size * 0.5f;
            float[] offset = { -0.17f, 0f, 0.17f };
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    float dx = offset[col], dy = offset[row];
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy) / 0.24f;
                    float radius = size * (0.044f + 0.043f * Math.Max(0f, 1f - Math.Min(1f, distance)));
                    GlassDrawSphere(g, new RectangleF(center + dx * size - radius, center + dy * size - radius, radius * 2f, radius * 2f), accent);
                }
        }
        
        private static GraphicsPath GlassCellPath(RectangleF r)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(r.X + r.Width * 0.18f, r.Y + r.Height * 0.035f,
                r.X + r.Width * 0.44f, r.Y - r.Height * 0.02f,
                r.X + r.Width * 0.79f, r.Y + r.Height * 0.035f,
                r.X + r.Width * 0.97f, r.Y + r.Height * 0.20f);
            p.AddBezier(r.X + r.Width * 0.97f, r.Y + r.Height * 0.20f,
                r.X + r.Width * 1.02f, r.Y + r.Height * 0.47f,
                r.X + r.Width * 0.99f, r.Y + r.Height * 0.78f,
                r.X + r.Width * 0.82f, r.Y + r.Height * 0.96f);
            p.AddBezier(r.X + r.Width * 0.82f, r.Y + r.Height * 0.96f,
                r.X + r.Width * 0.55f, r.Y + r.Height * 1.02f,
                r.X + r.Width * 0.20f, r.Y + r.Height * 0.98f,
                r.X + r.Width * 0.03f, r.Y + r.Height * 0.78f);
            p.AddBezier(r.X + r.Width * 0.03f, r.Y + r.Height * 0.78f,
                r.X - r.Width * 0.02f, r.Y + r.Height * 0.52f,
                r.X + r.Width * 0.00f, r.Y + r.Height * 0.22f,
                r.X + r.Width * 0.18f, r.Y + r.Height * 0.035f);
            p.CloseFigure();
            return p;
        }
        
        private static void GlassDrawCell(Graphics g, RectangleF rect, Color accent)
        {
            using (GraphicsPath cell = GlassCellPath(rect))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(rect,
                    GlassBlend(accent, Color.White, 0.48f), GlassBlend(accent, Color.Black, 0.24f), 105f))
                    g.FillPath(fill, cell);
                using (Pen edge = new Pen(Color.FromArgb(186, 20, 31, 43), Math.Max(0.48f, rect.Width * 0.075f)))
                    g.DrawPath(edge, cell);
            }
            using (GraphicsPath gleam = new GraphicsPath())
            {
                gleam.AddBezier(rect.X + rect.Width * 0.19f, rect.Y + rect.Height * 0.18f,
                    rect.X + rect.Width * 0.39f, rect.Y + rect.Height * 0.10f,
                    rect.X + rect.Width * 0.67f, rect.Y + rect.Height * 0.11f,
                    rect.X + rect.Width * 0.82f, rect.Y + rect.Height * 0.20f);
                using (Pen hi = new Pen(Color.FromArgb(188, 255, 255, 255), Math.Max(0.35f, rect.Width * 0.035f)))
                {
                    hi.StartCap = LineCap.Round;
                    hi.EndCap = LineCap.Round;
                    g.DrawPath(hi, gleam);
                }
            }
        }
        
        private static void GlassDrawVoronoi(Graphics g, int size, Color accent)
        {
            float cx = size * 0.5f, cy = size * 0.5f;
            float w = size * 0.205f, h = size * 0.19f;
            float dx = size * 0.025f, dy = size * 0.025f;
            GlassDrawCell(g, new RectangleF(cx - dx - w, cy - dy - h, w, h), GlassBlend(accent, Color.White, 0.10f));
            GlassDrawCell(g, new RectangleF(cx + dx, cy - dy - h, w, h), GlassBlend(accent, Color.White, 0.28f));
            GlassDrawCell(g, new RectangleF(cx - dx - w, cy + dy, w, h), GlassBlend(accent, Color.Black, 0.10f));
            GlassDrawCell(g, new RectangleF(cx + dx, cy + dy, w, h), GlassBlend(accent, Color.White, 0.18f));
            if (size >= 26)
            {
                float r = size * 0.033f;
                GlassDrawSphere(g, new RectangleF(cx - r, cy - r, r * 2f, r * 2f), accent);
            }
        }
        
        private static void GlassDrawRadialDots(Graphics g, int size, Color accent)
        {
            float cx = size * 0.5f, cy = size * 0.5f;
            float[] rings = { 0.135f, 0.255f, 0.365f };
            int[] counts = { 8, 12, 16 };
            float[] diameters = { 0.086f, 0.069f, 0.056f };
            for (int ring = 0; ring < rings.Length; ring++)
            {
                float radius = size * rings[ring];
                float dotRadius = size * diameters[ring] * 0.5f;
                for (int i = 0; i < counts[ring]; i++)
                {
                    double angle = 2.0 * Math.PI * i / counts[ring] + ring * 0.16;
                    float x = cx + radius * (float)Math.Cos(angle);
                    float y = cy + radius * (float)Math.Sin(angle);
                    GlassDrawSphere(g, new RectangleF(x - dotRadius, y - dotRadius, dotRadius * 2f, dotRadius * 2f), accent);
                }
            }
            float hub = Math.Max(1.1f, size * 0.047f);
            GlassDrawSphere(g, new RectangleF(cx - hub, cy - hub, hub * 2f, hub * 2f), accent);
        }
        
        private static GraphicsPath GlassDropPath(RectangleF r)
        {
            GraphicsPath path = new GraphicsPath();
            float cx = r.X + r.Width * 0.5f;
            float top = r.Y + r.Height * 0.02f;
            float bottom = r.Bottom - r.Height * 0.02f;
            float half = r.Width * 0.5f;
            path.AddBezier(cx, top, cx + half * 0.10f, top + r.Height * 0.16f,
                r.Right + half * 0.02f, r.Y + r.Height * 0.47f, r.Right - half * 0.03f, r.Y + r.Height * 0.66f);
            path.AddBezier(r.Right - half * 0.03f, r.Y + r.Height * 0.66f,
                r.Right - half * 0.05f, bottom, cx + half * 0.48f, bottom, cx, bottom);
            path.AddBezier(cx, bottom, cx - half * 0.48f, bottom, r.X + half * 0.05f, bottom,
                r.X + half * 0.03f, r.Y + r.Height * 0.66f);
            path.AddBezier(r.X + half * 0.03f, r.Y + r.Height * 0.66f,
                r.X - half * 0.02f, r.Y + r.Height * 0.47f, cx - half * 0.10f, top + r.Height * 0.16f, cx, top);
            path.CloseFigure();
            return path;
        }
        
        private static void GlassDrawVape(Graphics g, int size, Color accent)
        {
            RectangleF drop = new RectangleF(size * 0.31f, size * 0.235f, size * 0.38f, size * 0.54f);
            using (GraphicsPath path = GlassDropPath(drop))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(drop,
                    GlassBlend(accent, Color.White, 0.63f), GlassBlend(accent, Color.Black, 0.28f), 125f))
                    g.FillPath(fill, path);
                using (Pen edge = new Pen(Color.FromArgb(225, 238, 250, 255), Math.Max(0.65f, size * 0.045f)))
                    g.DrawPath(edge, path);
            }
            using (GraphicsPath shine = new GraphicsPath())
            {
                shine.AddBezier(size * 0.435f, size * 0.48f, size * 0.405f, size * 0.57f,
                    size * 0.42f, size * 0.67f, size * 0.47f, size * 0.70f);
                using (Pen hi = new Pen(Color.FromArgb(222, 255, 255, 255), Math.Max(0.65f, size * 0.055f)))
                {
                    hi.StartCap = LineCap.Round;
                    hi.EndCap = LineCap.Round;
                    g.DrawPath(hi, shine);
                }
            }
            float lineWidth = Math.Max(0.45f, size * 0.022f);
            using (Pen core = new Pen(Color.FromArgb(214, 244, 251, 255), lineWidth))
            {
                core.StartCap = LineCap.Round;
                core.EndCap = LineCap.Round;
                g.DrawLine(core, size * 0.485f, size * 0.54f, size * 0.515f, size * 0.54f);
                g.DrawLine(core, size * 0.475f, size * 0.585f, size * 0.525f, size * 0.585f);
                g.DrawLine(core, size * 0.485f, size * 0.63f, size * 0.515f, size * 0.63f);
            }
        }
        


        /// <summary>网格修复：三角网格补片（2×2 格 → 8 个三角形）+ 右下角对勾徽标</summary>
        private static void GlassDrawMeshFix(Graphics g, int size, Color accent)
        {
            float left = size * 0.155f, top = size * 0.175f;
            float w = size * 0.55f, h = size * 0.53f;
            var v = new PointF[3, 3];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    v[r, c] = new PointF(left + w * c * 0.5f, top + h * r * 0.5f);
            float seam = Math.Max(0.45f, size * 0.024f);
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                {
                    PointF a = v[r, c], b = v[r, c + 1], d = v[r + 1, c], e = v[r + 1, c + 1];
                    using (SolidBrush light = new SolidBrush(GlassBlend(accent, Color.White, 0.62f - 0.20f * (r + c))))
                        g.FillPolygon(light, new PointF[] { a, b, e });
                    using (SolidBrush dark = new SolidBrush(GlassBlend(accent, Color.Black, 0.04f + 0.22f * (r + c))))
                        g.FillPolygon(dark, new PointF[] { a, e, d });
                }
            using (Pen edge = new Pen(Color.FromArgb(206, 14, 26, 38), seam))
            {
                edge.LineJoin = LineJoin.Round;
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 2; c++)
                    {
                        PointF a = v[r, c], b = v[r, c + 1], d = v[r + 1, c], e = v[r + 1, c + 1];
                        g.DrawPolygon(edge, new PointF[] { a, b, e });
                        g.DrawPolygon(edge, new PointF[] { a, e, d });
                    }
            }

            float br = size * 0.205f;
            float bx = size * 0.755f, by = size * 0.775f;
            using (GraphicsPath badge = new GraphicsPath())
            {
                badge.AddEllipse(bx - br, by - br, br * 2f, br * 2f);
                using (SolidBrush fill = new SolidBrush(GlassBlend(accent, Color.White, 0.90f)))
                    g.FillPath(fill, badge);
                using (Pen ring = new Pen(Color.FromArgb(216, 255, 255, 255), Math.Max(0.5f, size * 0.028f)))
                    g.DrawPath(ring, badge);
                using (Pen tick = new Pen(GlassBlend(accent, Color.Black, 0.26f), Math.Max(0.8f, size * 0.068f)))
                {
                    tick.StartCap = LineCap.Round;
                    tick.EndCap = LineCap.Round;
                    tick.LineJoin = LineJoin.Round;
                    g.DrawLines(tick, new PointF[]
                    {
                        new PointF(bx - br * 0.56f, by + br * 0.02f),
                        new PointF(bx - br * 0.15f, by + br * 0.45f),
                        new PointF(bx + br * 0.58f, by - br * 0.43f)
                    });
                }
            }
        }
        /// <summary>钻石切面：一圈明暗错落的三角刻面 + 细边线（与参考图的切割面一致）</summary>
        private static void GlassDrawDiamond(Graphics g, int size, Color accent)
        {
            float cx = size * 0.5f, cy = size * 0.52f;
            float r = size * 0.34f;
            double[] ang = { -96.0, -22.0, 46.0, 122.0, 196.0 };
            float[] rad = { 1.00f, 0.88f, 1.02f, 0.86f, 0.96f };
            var ring = new PointF[5];
            for (int i = 0; i < 5; i++)
            {
                double a = ang[i] * Math.PI / 180.0;
                ring[i] = new PointF(cx + (float)(Math.Cos(a) * r * rad[i]), cy + (float)(Math.Sin(a) * r * rad[i]));
            }
            var hub = new PointF(cx - r * 0.12f, cy - r * 0.10f);
            float[] tone = { 0.64f, 0.28f, -0.10f, 0.12f, 0.44f };
            for (int i = 0; i < 5; i++)
            {
                PointF a = ring[i], b = ring[(i + 1) % 5];
                Color face = tone[i] >= 0f ? GlassBlend(accent, Color.White, tone[i]) : GlassBlend(accent, Color.Black, -tone[i]);
                using (SolidBrush fill = new SolidBrush(face))
                    g.FillPolygon(fill, new PointF[] { a, b, hub });
            }
            float seam = Math.Max(0.45f, size * 0.024f);
            using (Pen edge = new Pen(Color.FromArgb(206, 14, 26, 38), seam))
            {
                edge.LineJoin = LineJoin.Round;
                for (int i = 0; i < 5; i++) g.DrawLine(edge, hub, ring[i]);
                var outline = new PointF[6];
                for (int i = 0; i < 5; i++) outline[i] = ring[i];
                outline[5] = ring[0];
                g.DrawLines(edge, outline);
            }
        }

        /// <summary>水波纹：三道错相的正弦水波 + 波峰高光（一眼是「水波纹」）</summary>
        private static void GlassDrawRipple(Graphics g, int size, Color accent)
        {
            float cx = size * 0.5f;
            float w = size * 0.64f;
            float amp = size * 0.052f;
            float stroke = Math.Max(1.0f, size * 0.062f);
            float[] ys = { size * 0.33f, size * 0.50f, size * 0.67f };
            float[] phase = { 0.0f, 0.55f, 1.1f };
            using (var path = new GraphicsPath())
            {
                for (int k = 0; k < ys.Length; k++)
                {
                    int n = 36;
                    var pts = new PointF[n + 1];
                    for (int i = 0; i <= n; i++)
                    {
                        float t = i / (float)n;
                        float x = cx - w * 0.5f + w * t;
                        float y = ys[k] + (float)Math.Sin((t * 2.0 + phase[k]) * Math.PI) * amp;
                        pts[i] = new PointF(x, y);
                    }
                    path.StartFigure();
                    path.AddCurve(pts);
                }
                using (Pen dark = new Pen(Color.FromArgb(196, 14, 26, 38), stroke + Math.Max(0.7f, size * 0.028f)))
                using (Pen main = new Pen(accent, stroke))
                {
                    dark.LineJoin = LineJoin.Round; main.LineJoin = LineJoin.Round;
                    dark.StartCap = LineCap.Round; dark.EndCap = LineCap.Round;
                    main.StartCap = LineCap.Round; main.EndCap = LineCap.Round;
                    g.DrawPath(dark, path);
                    g.DrawPath(main, path);
                }
            }
            using (Pen hi = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(0.6f, stroke * 0.32f)))
            {
                hi.StartCap = LineCap.Round; hi.EndCap = LineCap.Round;
                g.DrawLine(hi, cx - w * 0.30f, ys[1] - amp * 0.80f, cx - w * 0.08f, ys[1] - amp * 0.92f);
                g.DrawLine(hi, cx + w * 0.16f, ys[2] - amp * 0.80f, cx + w * 0.36f, ys[2] - amp * 0.90f);
            }
        }

        private static void GlassDrawMiniNode(Graphics g, PointF center, float side, Color color)
        {
            RectangleF r = new RectangleF(center.X - side * 0.5f, center.Y - side * 0.5f, side, side);
            float radius = side * 0.28f;
            using (GraphicsPath path = GlassRoundPath(r, radius))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(r,
                    GlassBlend(color, Color.White, 0.48f), GlassBlend(color, Color.Black, 0.18f), 90f))
                    g.FillPath(fill, path);
                using (Pen edge = new Pen(Color.FromArgb(205, 10, 19, 31), Math.Max(0.48f, side * 0.12f)))
                    g.DrawPath(edge, path);
            }
            using (Pen glint = new Pen(Color.FromArgb(210, 255, 255, 255), Math.Max(0.35f, side * 0.07f)))
            {
                glint.StartCap = LineCap.Round;
                glint.EndCap = LineCap.Round;
                g.DrawLine(glint, r.X + side * 0.28f, r.Y + side * 0.27f, r.Right - side * 0.30f, r.Y + side * 0.27f);
            }
        }
        
        private static void GlassDrawApp(Graphics g, int size)
        {
            float c = size * 0.5f;
            float stroke = Math.Max(1.15f, size * 0.083f);
            float top = size * 0.325f;
            float bottom = size * 0.675f;
            float ix = size * 0.345f;
            float cap = size * 0.075f;
            float vx1 = size * 0.475f;
            float vx2 = size * 0.615f;
            float vx3 = size * 0.755f;
            using (GraphicsPath monogram = new GraphicsPath())
            {
                monogram.AddLine(ix, top, ix, bottom);
                monogram.StartFigure();
                monogram.AddLine(ix - cap, top, ix + cap, top);
                monogram.StartFigure();
                monogram.AddLine(ix - cap, bottom, ix + cap, bottom);
                monogram.StartFigure();
                monogram.AddLine(vx1, top, vx2, bottom);
                monogram.AddLine(vx2, bottom, vx3, top);
                using (Pen iv = new Pen(Color.FromArgb(246, 255, 255, 255), stroke))
                {
                    iv.StartCap = LineCap.Round;
                    iv.EndCap = LineCap.Round;
                    iv.LineJoin = LineJoin.Round;
                    g.DrawPath(iv, monogram);
                }
            }
        }

        static int Main(string[] args)
        {
            // 附加模式：--kind <种类> <输出.png> → 导出单个玻璃图标（人工核对图标设计用）
            if (args != null && args.Length >= 3 && args[0] == "--kind")
            {
                using (Bitmap one = CreateGlassIcon(args[1], 128))
                    one.Save(args[2], ImageFormat.Png);
                return 0;
            }
            string outPath = args != null && args.Length > 0 ? args[0] : "app.ico";
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var blobs = new List<byte[]>();
            var used = new List<int>();
            foreach (int s in sizes)
            {
                using (Bitmap bmp = Draw(s))
                {
                    // 256 用 PNG（Vista 起支持，体积小）；小尺寸必须用经典 DIB/BMP ——
                    // 全用 PNG 条目的话 Windows 的图标加载器（含 .NET 的 Icon 类）直接读不出来。
                    if (s >= 256)
                    {
                        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); blobs.Add(ms.ToArray()); }
                    }
                    else
                    {
                        blobs.Add(ToDib(bmp));
                    }
                    used.Add(s);
                }
            }
            WriteIco(outPath, used, blobs);
            Console.WriteLine("wrote " + outPath + " (" + used.Count + " sizes, " + new FileInfo(outPath).Length + " bytes)");
            return 0;
        }

        /// <summary>32bpp 的 DIB：BITMAPINFOHEADER + 自下而上的 BGRA + 全 0 的 AND 掩码（透明度靠 alpha）</summary>
        static byte[] ToDib(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40);                 // biSize
                bw.Write(w);                  // biWidth
                bw.Write(h * 2);              // biHeight = XOR + AND
                bw.Write((short)1);           // biPlanes
                bw.Write((short)32);          // biBitCount
                bw.Write(0);                  // biCompression = BI_RGB
                bw.Write(0);                  // biSizeImage
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var buf = new byte[w * 4];
                    for (int y = h - 1; y >= 0; y--)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buf, 0, buf.Length);
                        bw.Write(buf);
                    }
                }
                finally { bmp.UnlockBits(data); }

                int rowBytes = ((w + 31) / 32) * 4;      // 1bpp，按 4 字节对齐
                bw.Write(new byte[rowBytes * h]);
                bw.Flush();
                return ms.ToArray();
            }
        }

        static Bitmap Draw(int size)
        {
            return CreateGlassIcon("app", size);
        }

        static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>写 ICO：每个尺寸一条记录（小尺寸 DIB / 256 用 PNG）</summary>
        static void WriteIco(string path, List<int> sizes, List<byte[]> data)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);              // reserved
                w.Write((ushort)1);              // type = icon
                w.Write((ushort)sizes.Count);
                int offset = 6 + 16 * sizes.Count;
                for (int i = 0; i < sizes.Count; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));    // width
                    w.Write((byte)(s >= 256 ? 0 : s));    // height
                    w.Write((byte)0);                     // colors
                    w.Write((byte)0);                     // reserved
                    w.Write((ushort)1);                   // planes
                    w.Write((ushort)32);                  // bitcount
                    w.Write(data[i].Length);
                    w.Write(offset);
                    offset += data[i].Length;
                }
                for (int i = 0; i < data.Count; i++) w.Write(data[i]);
            }
        }
    }
}
