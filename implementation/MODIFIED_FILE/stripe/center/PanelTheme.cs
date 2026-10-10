using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// IVAN 插件套件统一界面语言（finesse 口径）
//
// 动效纪律（来自 finesse skill）：
//   · 只动「位置 / 不透明度 / 颜色」，不动尺寸类布局属性；
//   · 时长取统一 token：150ms 微反馈（hover/press/勾选）、200ms 常规（状态切换）、300ms 大面板进入；
//   · 进入用 ease-out，退出用 ease-in 且更快一档；
//   · 可中断：同一个槽位的新动画从「当前值」接着走，不从头开始；
//   · 高频操作不做动画（拖滑块 1:1 跟手，松手才落位）；
//   · 尊重系统「减少动画」设置（SPI_GETCLIENTAREAANIMATION），关闭时全部瞬时到位、终态不变；
//   · 反馈就地（面板内的状态条），不弹跨屏 toast。
namespace IvanUi
{
    internal static class Theme
    {
        // ---- 颜色：近黑正文、三级文字层级、克制的强调色 ----
        public static readonly Color Ink = Color.FromArgb(37, 51, 69);
        public static readonly Color InkSoft = Color.FromArgb(103, 121, 140);
        public static readonly Color InkFaint = Color.FromArgb(141, 157, 171);
        public static readonly Color Surface = Color.FromArgb(249, 252, 255);
        public static readonly Color SurfaceAlt = Color.FromArgb(236, 243, 249);
        public static readonly Color Canvas = Color.FromArgb(239, 246, 252);
        public static readonly Color Border = Color.FromArgb(218, 229, 238);
        public static readonly Color BorderStrong = Color.FromArgb(184, 203, 221);

        public static readonly Color Accent = Color.FromArgb(52, 125, 241);
        public static readonly Color AccentHover = Color.FromArgb(79, 149, 250);
        public static readonly Color AccentPress = Color.FromArgb(42, 107, 216);
        public static readonly Color AccentSoft = Color.FromArgb(228, 239, 253);

        public static readonly Color Ok = Color.FromArgb(31, 138, 84);
        public static readonly Color Warn = Color.FromArgb(176, 116, 16);
        public static readonly Color Danger = Color.FromArgb(186, 58, 48);

        public static readonly Color HeaderTop = Color.FromArgb(250, 253, 255);
        public static readonly Color HeaderBottom = Color.FromArgb(223, 236, 247);

        // ---- 尺寸 token ----
        public const int RadiusCard = 14;
        public const int RadiusControl = 9;
        public const int PadCard = 12;
        public const int RowHeight = 30;

        // ---- 字体：层级靠字重和颜色区分，不靠字号堆叠 ----
        static string UiFamily()
        {
            try
            {
                foreach (string f in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" })
                {
                    var ff = new FontFamily(f);
                    if (ff != null) return f;
                }
            }
            catch { }
            return "Segoe UI";
        }

        static readonly string _fam = UiFamily();
        public static Font Body { get { return new Font(_fam, 9F, FontStyle.Regular, GraphicsUnit.Point); } }
        public static Font BodyBold { get { return new Font(_fam, 9F, FontStyle.Bold, GraphicsUnit.Point); } }
        public static Font Small { get { return new Font(_fam, 8.25F, FontStyle.Regular, GraphicsUnit.Point); } }
        public static Font Title { get { return new Font(_fam, 11F, FontStyle.Bold, GraphicsUnit.Point); } }
        public static Font Big { get { return new Font(_fam, 22F, FontStyle.Bold, GraphicsUnit.Point); } }

        /// <summary>圆角矩形路径（同心圆角：外圆角 = 内圆角 + 内边距）</summary>
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            if (d < 3) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>面板头部：深色渐变带 + 标题 + 副标题 + 小图标</summary>
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

        public static void DrawHeader(Graphics g, Rectangle r, string title, string sub, string kind)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(r, HeaderTop, HeaderBottom, 18f)) g.FillRectangle(b, r);
            using (var light = new Pen(Color.FromArgb(230, Color.White), 1f)) g.DrawLine(light, r.Left, r.Top, r.Right, r.Top);
            using (var edge = new Pen(Color.FromArgb(155, Border), 1f)) g.DrawLine(edge, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            using (var icon = CreateGlassIcon(kind, 36)) g.DrawImage(icon, r.X + 12, r.Y + (r.Height - 36) / 2, 36, 36);
            using (var f = Title)
            using (var brush = new SolidBrush(Ink)) g.DrawString(title, f, brush, r.X + 59, r.Y + 5);
            using (var f = Small)
            using (var brush = new SolidBrush(InkSoft))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(sub, f, brush, new RectangleF(r.X + 59, r.Y + 28, Math.Max(1, r.Width - 71), 17), sf);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        public static void ApplyWindowMaterial(Form form)
        {
            if (form == null || form.IsDisposed) return;
            try
            {
                int rounded = 2;
                DwmSetWindowAttribute(form.Handle, 33, ref rounded, sizeof(int));
                // Optional Windows 11 material; older systems retain the readable GDI glass fallback.
                int backdrop = 2;
                DwmSetWindowAttribute(form.Handle, 38, ref backdrop, sizeof(int));
            }
            catch { }
        }

        /// <summary>插件小图标（与工具条图标同一套视觉语言，单色描线）</summary>
        public static void DrawGlyph(Graphics g, Rectangle r, string kind, Color col)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var icon = CreateGlassIcon(kind, Math.Max(r.Width, r.Height))) g.DrawImage(icon, r);
        }

        /// <summary>
        /// 插件小图标做成窗体图标（标题栏用）。
        /// 注意：WinForms 窗体不显式设 Icon 时用的是框架自带的默认窗体图标，
        /// 跟 EXE 内嵌图标是两回事 —— 必须显式赋值。
        /// </summary>
        public static Icon MakeFormIcon(string kind, int size)
        {
            try
            {
                using (var bmp = new Bitmap(size, size))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        DrawGlyph(g, new Rectangle(0, 0, size, size), kind, Accent);
                    }
                    IntPtr h = bmp.GetHicon();
                    try { return (Icon)Icon.FromHandle(h).Clone(); }
                    finally { DestroyIcon(h); }
                }
            }
            catch { return null; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr handle);

        /// <summary>状态色（错误/警告/正常）。颜色只是辅助，文字本身也说明状态。</summary>
        public static Color StateColor(string text)
        {
            if (string.IsNullOrEmpty(text)) return InkSoft;
            if (text.IndexOf("失败", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("异常", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("不支持", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("退化", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("已被删除", StringComparison.Ordinal) >= 0) return Danger;
            if (text.IndexOf("请", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("过大", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("过小", StringComparison.Ordinal) >= 0) return Warn;
            return Ok;
        }
    }

    /// <summary>
    /// 统一动画时钟：一个 16ms 定时器驱动全部补间（不是每控件一个 Timer）。
    /// 同一 (控件, 槽位) 的新动画从当前值继续 —— 可中断、可重定向。
    /// </summary>
    internal static class Motion
    {
        class Anim
        {
            public Control C; public string Slot;
            public double From, To; public long T0; public int Dur;
            public Func<double, double> Ease; public Action<double> Apply; public Action Done;
            public double Value(double p) { return From + (To - From) * Ease(p); }
        }

        static readonly List<Anim> _anims = new List<Anim>();
        static readonly Timer _timer = new Timer { Interval = 16 };
        static readonly Stopwatch _clock = Stopwatch.StartNew();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);
        const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

        static int _enabled = -1;
        /// <summary>系统是否允许动画（Windows「显示动画」设置）。关掉时全部瞬时到位，终态不变。</summary>
        public static bool Enabled
        {
            get
            {
                if (_enabled < 0)
                {
                    try
                    {
                        int v;
                        _enabled = SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out v, 0) ? (v != 0 ? 1 : 0) : 1;
                    }
                    catch { _enabled = 1; }
                }
                return _enabled == 1;
            }
        }

        // finesse 缓动：进入 ease-out（快出慢停）、退出 ease-in、屏上位移 ease-in-out
        public static double EaseOut(double t) { return 1 - Math.Pow(1 - t, 3); }
        public static double EaseIn(double t) { return t * t * t; }
        public static double EaseInOut(double t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
        public static double Linear(double t) { return t; }

        public const int Micro = 150;    // hover / press / 勾选
        public const int Base = 200;     // 状态切换 / 小展开
        public const int Slow = 300;     // 大面板进入
        public const int Exit = 150;     // 退出永远比进入快一档

        static double Current(Control c, string slot)
        {
            for (int i = 0; i < _anims.Count; i++)
            {
                Anim a = _anims[i];
                if (!ReferenceEquals(a.C, c) || a.Slot != slot) continue;
                double p = a.Dur <= 0 ? 1 : Math.Min(1, (_clock.ElapsedMilliseconds - a.T0) / (double)a.Dur);
                return a.Value(p);
            }
            return double.NaN;
        }

        /// <summary>补间到目标值；从当前值接着走（可中断）。</summary>
        public static void To(Control c, string slot, double fromDefault, double to, int ms,
                              Func<double, double> ease, Action<double> apply, Action done = null)
        {
            double cur = Current(c, slot);
            if (!Enabled || ms <= 0)
            {
                try { apply(to); } catch { }
                if (done != null) { try { done(); } catch { } }
                return;
            }
            double from = double.IsNaN(cur) ? fromDefault : cur;
            for (int i = _anims.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_anims[i].C, c) && _anims[i].Slot == slot) _anims.RemoveAt(i);
            if (Math.Abs(to - from) < 1e-6)
            {
                try { apply(to); } catch { }
                if (done != null) { try { done(); } catch { } }
                return;
            }

            _anims.Add(new Anim
            {
                C = c, Slot = slot, From = from, To = to,
                T0 = _clock.ElapsedMilliseconds, Dur = ms, Ease = ease, Apply = apply, Done = done
            });
            if (!_timer.Enabled)
            {
                _timer.Tick -= OnTick;
                _timer.Tick += OnTick;
                _timer.Start();
            }
        }

        static void OnTick(object sender, EventArgs e)
        {
            long now = _clock.ElapsedMilliseconds;
            for (int i = _anims.Count - 1; i >= 0; i--)
            {
                Anim a = _anims[i];
                double p = a.Dur <= 0 ? 1 : Math.Min(1, (now - a.T0) / (double)a.Dur);
                try { a.Apply(a.Value(p)); } catch { }
                if (p >= 1)
                {
                    _anims.RemoveAt(i);
                    if (a.Done != null) { try { a.Done(); } catch { } }   // 漏了这句：补间跑完必须回调，否则 Close() 永远不执行
                }
            }
            if (_anims.Count == 0) _timer.Stop();
        }
    }

    /// <summary>
    /// 带动画的控件基类：记住每个槽位的目标值，补间由 Motion 统一驱动。
    /// 不用 ref 参数进 lambda（C# 不允许），改成「记住上次目标 + 回调写回字段」。
    /// </summary>
    internal abstract class AnimControl : Control
    {
        readonly Dictionary<string, double> _last = new Dictionary<string, double>();

        protected AnimControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Body;
        }

        /// <summary>把某个视觉量补间到目标值（默认 150ms / ease-out，可中断）</summary>
        protected void AnimTo(string slot, double to, Action<double> apply, int ms = Motion.Micro)
        {
            double from;
            if (!_last.TryGetValue(slot, out from)) from = to;
            _last[slot] = to;
            Motion.To(this, slot, from, to, ms, Motion.EaseOut, apply);
        }
    }

    internal enum BtnStyle { Primary, Secondary, Ghost }

    /// <summary>圆角扁平按钮：hover/press 就地反馈（150ms），键盘可触发，焦点环不隐藏</summary>
    internal class FlatButton : AnimControl, IButtonControl
    {
        BtnStyle _style = BtnStyle.Secondary;
        double _hover, _press;
        bool _isDefault;
        bool _pickStyle, _picked;

        /// <summary>选择类按钮（选择物件 / 选择渐变物件）：位置统一、状态用颜色表达</summary>
        public bool PickStyle { get { return _pickStyle; } set { _pickStyle = value; Invalidate(); } }
        /// <summary>是否已经选到了：false = 红（还没选）、true = 绿（已选）</summary>
        public bool Picked { get { return _picked; } set { if (_picked != value) { _picked = value; Invalidate(); } } }

        public BtnStyle Style { get { return _style; } set { _style = value; Invalidate(); } }
        public bool IsDefault { get { return _isDefault; } set { _isDefault = value; Invalidate(); } }

        public FlatButton()
        {
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = 30;
        }

        DialogResult IButtonControl.DialogResult { get { return DialogResult.None; } set { } }
        void IButtonControl.NotifyDefault(bool value) { IsDefault = value; }
        void IButtonControl.PerformClick() { OnClick(EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, text, edge;
            if (_pickStyle)
            {
                // 未选择 = 红、已选择 = 绿（醒目，四个插件统一口径）
                Color baseCol = _picked ? Color.FromArgb(255, 56, 158, 92) : Color.FromArgb(255, 206, 76, 76);
                Color hiCol = _picked ? Color.FromArgb(255, 74, 182, 112) : Color.FromArgb(255, 226, 96, 96);
                fill = Theme.Mix(baseCol, hiCol, Math.Max(_hover, _press));
                text = Color.White;
                edge = fill;
            }
            else if (_style == BtnStyle.Primary)
            {
                fill = _hover > 0 && _press <= 0
                    ? Theme.Mix(Theme.Accent, Theme.AccentHover, _hover)
                    : Theme.Mix(Theme.Accent, Theme.AccentPress, _press);
                text = Color.White; edge = fill;
            }
            else if (_style == BtnStyle.Ghost)
            {
                fill = Theme.Mix(Color.FromArgb(0, 255, 255, 255), Theme.AccentSoft, Math.Max(_hover, _press));
                text = Theme.Mix(Theme.InkSoft, Theme.Ink, Math.Max(_hover, _press));
                edge = Color.Transparent;
            }
            else
            {
                fill = Theme.Mix(Theme.Surface, Theme.Mix(Theme.SurfaceAlt, Theme.AccentSoft, 0.45), Math.Max(_hover, _press));
                text = Theme.Ink;
                edge = Theme.Mix(Theme.Border, Theme.BorderStrong, Math.Max(_hover, _press));
            }

            int inset = (int)Math.Round(_press * 1.0);   // 按下时整体下沉 1px（物理感）
            var rr = new Rectangle(r.X, r.Y + inset, r.Width, r.Height - inset);
            using (var path = Theme.Rounded(rr, Theme.RadiusControl))
            {
                if (fill.A > 0)
                {
                    Color top = _pickStyle ? Theme.Mix(fill, Color.White, 0.12) : Theme.Mix(fill, Color.White, _style == BtnStyle.Primary ? 0.17 : 0.45);
                    using (var b = new LinearGradientBrush(rr, top, fill, 90f)) g.FillPath(b, path);
                    using (var high = new Pen(Color.FromArgb(_style == BtnStyle.Ghost ? 0 : 105, Color.White), 1f))
                    using (var highPath = Theme.Rounded(new Rectangle(rr.X + 1, rr.Y + 1, Math.Max(1, rr.Width - 2), Math.Max(1, rr.Height - 2)), Theme.RadiusControl - 1))
                        g.DrawPath(high, highPath);
                }
                if (edge.A > 0) using (var p = new Pen(edge, 1f)) g.DrawPath(p, path);
            }
            var f = Font;   // 注意：不能 using —— 那会把控件自己的字体 Dispose 掉，第二次重绘就抛「参数无效」
            using (var b = new SolidBrush(text))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Text, f, b, new RectangleF(rr.X, rr.Y, rr.Width, rr.Height), sf);

            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Accent, 2f))
                using (var path = Theme.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), Theme.RadiusControl - 1))
                    g.DrawPath(p, path);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); AnimTo("hover", 1, v => { _hover = v; Invalidate(); }); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); AnimTo("hover", 0, v => { _hover = v; Invalidate(); }); AnimTo("press", 0, v => { _press = v; Invalidate(); }); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left) AnimTo("press", 1, v => { _press = v; Invalidate(); });
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) AnimTo("press", 0, v => { _press = v; Invalidate(); });
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); AnimTo("press", 0, v => { _press = v; Invalidate(); }); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>
    /// 自绘滑块：拖的时候 1:1 跟手（不做动画），松手才按步长落位。
    /// 悬停时滑块微微变大（150ms），按下再收一点 —— 反馈就地、不抢焦点。
    /// </summary>
    internal class IvanSlider : AnimControl
    {
        double _min, _max = 1, _value;
        double _step = 0.01;
        double _hover, _press;
        bool _drag;

        public event EventHandler ValueChanged;

        public double Minimum { get { return _min; } set { _min = value; Sync(); } }
        public double Maximum { get { return _max; } set { _max = value; Sync(); } }
        public double Step { get { return _step; } set { _step = Math.Max(value, 1e-9); } }
        public double Value
        {
            get { return _value; }
            set { SetValue(value, false); }
        }

        public IvanSlider()
        {
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = 26;
        }

        void Sync()
        {
            if (_max < _min) { double t = _min; _min = _max; _max = t; }
            if (_value < _min) _value = _min;
            if (_value > _max) _value = _max;
            Invalidate();
        }

        void SetValue(double v, bool fromUser)
        {
            double step = _step;
            if (step > 1e-12) v = Math.Round(v / step) * step;
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            if (Math.Abs(v - _value) < 1e-12) return;
            _value = v;
            Invalidate();
            if (fromUser && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        double Ratio() { double d = _max - _min; return d > 1e-12 ? (_value - _min) / d : 0; }

        int ThumbX()
        {
            int pad = 8;
            int w = Math.Max(1, Width - pad * 2);
            return pad + (int)Math.Round(w * Ratio());
        }

        void SetFromMouse(int x)
        {
            int pad = 8;
            int w = Math.Max(1, Width - pad * 2);
            double t = (x - pad) / (double)w;
            if (t < 0) t = 0; if (t > 1) t = 1;
            SetValue(_min + t * (_max - _min), true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = Height / 2;
            int pad = 8;
            int w = Math.Max(1, Width - pad * 2);
            int tx = ThumbX();

            var track = new Rectangle(pad, cy - 2, w, 4);
            using (var b = new SolidBrush(Theme.Border))
            using (var path = Theme.Rounded(track, 2)) g.FillPath(b, path);
            if (tx > pad)
            {
                var fillR = new Rectangle(pad, cy - 2, tx - pad, 4);
                using (var b = new SolidBrush(Theme.Accent))
                using (var path = Theme.Rounded(fillR, 2)) g.FillPath(b, path);
            }
            float rad = 6.5f + (float)(_hover * 1.5 - _press * 1.2);   // hover 变大 / 按下收一点
            using (var shadow = new SolidBrush(Color.FromArgb(26, 57, 82, 109)))
                g.FillEllipse(shadow, tx - rad - 1, cy - rad + 1, rad * 2 + 2, rad * 2 + 2);
            using (var b = new LinearGradientBrush(new RectangleF(tx - rad, cy - rad, rad * 2, rad * 2), Color.White, Color.FromArgb(230, 241, 250), 90f))
                g.FillEllipse(b, tx - rad, cy - rad, rad * 2, rad * 2);
            using (var p = new Pen(Theme.Mix(Theme.BorderStrong, Theme.Accent, Math.Max(_hover, _press)), 1.15f))
                g.DrawEllipse(p, tx - rad, cy - rad, rad * 2, rad * 2);

            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Accent, 1f))
                    g.DrawEllipse(p, tx - rad - 3, cy - rad - 3, (rad + 3) * 2, (rad + 3) * 2);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); AnimTo("hover", 1, v => { _hover = v; Invalidate(); }); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); AnimTo("hover", 0, v => { _hover = v; Invalidate(); }); AnimTo("press", 0, v => { _press = v; Invalidate(); }); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            AnimTo("press", 1, v => { _press = v; Invalidate(); });
            SetFromMouse(e.X);       // 1:1 跟手
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) SetFromMouse(e.X);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = false;
            AnimTo("press", 0, v => { _press = v; Invalidate(); });
            SetFromMouse(e.X);       // 松手按步长落位
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SetValue(_value + _step * (e.Delta > 0 ? 1 : -1), true);
        }
        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down ||
                keyData == Keys.PageUp || keyData == Keys.PageDown || keyData == Keys.Home || keyData == Keys.End)
                return true;
            return base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double big = Math.Max(_step, (_max - _min) / 20.0);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { SetValue(_value - _step, true); e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { SetValue(_value + _step, true); e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { SetValue(_value - big, true); e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp) { SetValue(_value + big, true); e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { SetValue(_min, true); e.Handled = true; }
            else if (e.KeyCode == Keys.End) { SetValue(_max, true); e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>自绘勾选框：勾出现时有一个 150ms 的缩放+淡入，按下时整块下沉一点</summary>
    internal class IvanCheck : AnimControl
    {
        bool _checked;
        double _hover, _press, _tick;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                AnimTo("tick", value ? 1 : 0, v => { _tick = v; Invalidate(); });
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public IvanCheck()
        {
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = 22;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = 27;
            int by = (Height - 16) / 2 + (int)Math.Round(_press);
            var r = new Rectangle(0, by, box, 16);
            Color edge = Theme.Mix(Theme.BorderStrong, Theme.Accent, Math.Max(_hover, _tick));
            Color fill = Theme.Mix(Theme.BorderStrong, Theme.Accent, _tick);
            using (var path = Theme.Rounded(r, 8))
            {
                using (var b = new LinearGradientBrush(r, Theme.Mix(fill, Color.White, 0.12), fill, 90f)) g.FillPath(b, path);
                using (var p = new Pen(edge, 1f)) g.DrawPath(p, path);
            }
            float knobX = 1.5f + 11f * (float)_tick;
            using (var shadow = new SolidBrush(Color.FromArgb(28, 47, 72, 96))) g.FillEllipse(shadow, knobX, by + 2, 13, 13);
            using (var knob = new LinearGradientBrush(new RectangleF(knobX, by + 1.5f, 13, 13), Color.White, Color.FromArgb(238, 245, 252), 90f))
                g.FillEllipse(knob, knobX, by + 1.5f, 13, 13);
            var f = Font;   // 注意：不能 using —— 那会把控件自己的字体 Dispose 掉，第二次重绘就抛「参数无效」
            using (var b = new SolidBrush(Enabled ? Theme.Ink : Theme.InkFaint))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Text, f, b, new RectangleF(box + 7, 0, Width - box - 7, Height), sf);

            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Accent, 1.5f))
                using (var path = Theme.Rounded(new Rectangle(-2, by - 2, box + 4, box + 4), 6))
                    g.DrawPath(p, path);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); AnimTo("hover", 1, v => { _hover = v; Invalidate(); }); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); AnimTo("hover", 0, v => { _hover = v; Invalidate(); }); AnimTo("press", 0, v => { _press = v; Invalidate(); }); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) AnimTo("press", 1, v => { _press = v; Invalidate(); }); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            AnimTo("press", 0, v => { _press = v; Invalidate(); });
            if (ClientRectangle.Contains(e.Location)) Checked = !Checked;
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>
    /// 互斥分段选择器（单面 / 多面 这类范围选择）：选中块滑动到位（200ms ease-in-out，可中断）。
    /// 键盘左右键可切换；焦点环不隐藏。
    /// </summary>
    internal class IvanSegmented : AnimControl
    {
        string[] _items = new string[0];
        int _selected;
        double _pos;
        double _hover = -1;

        public event EventHandler SelectedChanged;

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _items.Length || value == _selected) return;
                int prev = _selected;
                _selected = value;
                Motion.To(this, "pos", prev, value, Motion.Base, Motion.EaseInOut, v => { _pos = v; Invalidate(); });
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        public void SetItems(params string[] items)
        {
            _items = items ?? new string[0];
            if (_selected >= _items.Length) _selected = Math.Max(0, _items.Length - 1);
            _pos = _selected;
            Invalidate();
        }

        public IvanSegmented()
        {
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = 28;
        }

        int ItemWidth() { return _items.Length == 0 ? Width : Math.Max(1, (Width - 4) / _items.Length); }
        int IndexAt(int x) { int i = (x - 2) / Math.Max(1, ItemWidth()); return Math.Max(0, Math.Min(_items.Length - 1, i)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_items.Length == 0) return;
            int iw = ItemWidth();
            var outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(outer, Theme.RadiusControl))
            {
                using (var b = new LinearGradientBrush(outer, Theme.SurfaceAlt, Theme.Mix(Theme.SurfaceAlt, Color.White, 0.35), 90f)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border, 1f)) g.DrawPath(p, path);
            }
            var hi = new Rectangle(2 + (int)Math.Round(_pos * iw), 2, iw, Height - 4);
            using (var path = Theme.Rounded(hi, Theme.RadiusControl - 2))
            {
                using (var b = new LinearGradientBrush(hi, Color.White, Theme.Surface, 90f)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.22), 1f)) g.DrawPath(p, path);
            }
            for (int i = 0; i < _items.Length; i++)
            {
                var r = new Rectangle(2 + i * iw, 2, iw, Height - 4);
                bool sel = i == _selected;
                bool hov = i == (int)Math.Round(_hover);
                using (var f = sel ? Theme.BodyBold : Theme.Body)
                using (var b = new SolidBrush(sel ? Theme.Accent : (hov ? Theme.Ink : Theme.InkSoft)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(_items[i], f, b, r, sf);
            }
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Accent, 1.5f))
                using (var path = Theme.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), Theme.RadiusControl))
                    g.DrawPath(p, path);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.X);
            if (Math.Abs(_hover - i) > 1e-6) { _hover = i; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left) SelectedIndex = IndexAt(e.X);
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { SelectedIndex = Math.Min(_items.Length - 1, _selected + 1); e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>白卡片：圆角 + 发丝边。分组靠间距和留白表达，不靠堆边框。</summary>
    internal class CardPanel : Panel
    {
        public string Title;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var canvas = new SolidBrush(Parent == null ? Theme.Canvas : Parent.BackColor)) g.FillRectangle(canvas, ClientRectangle);
            var r = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var path = Theme.Rounded(r, Theme.RadiusCard))
            {
                using (var fill = new LinearGradientBrush(r, Color.White, Theme.SurfaceAlt, 78f)) g.FillPath(fill, path);
                using (var edge = new Pen(Theme.Border, 1f)) g.DrawPath(edge, path);
                using (var high = new Pen(Color.FromArgb(225, Color.White), 1f))
                using (var hp = Theme.Rounded(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), Theme.RadiusCard - 1)) g.DrawPath(high, hp);
            }
            if (!string.IsNullOrEmpty(Title))
            {
                using (var f = Theme.BodyBold)
                using (var b = new SolidBrush(Theme.Ink))
                    g.DrawString(Title, f, b, Theme.PadCard, 8);
            }
            base.OnPaint(e);
        }
    }

    /// <summary>
    /// 就地状态条：圆点 + 文字。状态用颜色**和**文字同时表达；
    /// 文字立即更新（不做延迟），只有底色做一个 200ms 的轻微脉冲。
    /// </summary>
    internal class InfoBar : AnimControl
    {
        string _text = "";
        double _pulse = 1;

        public InfoBar()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
            Font = Theme.Small;
        }

        public override string Text
        {
            get { return _text; }
            set
            {
                if (_text == value) return;
                _text = value ?? "";
                Invalidate();
                Motion.To(this, "pulse", 0, 1, Motion.Base, Motion.EaseOut, v => { _pulse = 0.35 + 0.65 * v; Invalidate(); });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color st = Theme.StateColor(_text);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(r, Theme.RadiusControl))
            {
                using (var b = new SolidBrush(Theme.Mix(Color.White, Color.FromArgb(30, st), 0.45 * _pulse)))
                    g.FillPath(b, path);
                using (var p = new Pen(Theme.Mix(Theme.Border, Color.FromArgb(130, st), 0.55 * _pulse), 1f))
                    g.DrawPath(p, path);
            }
            int cy = Height / 2;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, _pulse + 0.25)), st)))
                g.FillEllipse(b, 8, cy - 3, 6, 6);
            var f = Font;   // 注意：不能 using —— 那会把控件自己的字体 Dispose 掉，第二次重绘就抛「参数无效」
            using (var b = new SolidBrush(Theme.Ink))
                g.DrawString(_text, f, b, new RectangleF(20, 0, Width - 26, Height),
                    new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
        }
    }

    /// <summary>图标 + 文字的小图块（阵列方式 / 图形形状）：选中用强调色，悬停轻抬 1px</summary>
    internal class IvanTile : AnimControl
    {
        public int Index;
        public bool Selected;
        readonly Bitmap _icon;
        double _hover, _press;

        public event EventHandler Picked;

        public IvanTile(int index, string text, Bitmap icon)
        {
            Index = index;
            _icon = icon;
            Text = text;
            Cursor = Cursors.Hand;
            Font = Theme.Small;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int lift = (int)Math.Round(_hover * 1.0 - _press * 1.0);
            var r = new Rectangle(0, lift, Width - 1, Height - 1);
            Color fill = Selected ? Theme.AccentSoft : Theme.Mix(Theme.Surface, Theme.SurfaceAlt, _hover);
            Color edge = Selected ? Theme.Accent : Theme.Mix(Theme.Border, Theme.BorderStrong, _hover);
            using (var path = Theme.Rounded(r, Theme.RadiusControl))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(edge, Selected ? 1.6f : 1f)) g.DrawPath(p, path);
            }
            int ix = r.X + 5;
            if (_icon != null) g.DrawImage(_icon, ix, r.Y + (r.Height - _icon.Height) / 2);
            int textLeft = ix + (_icon != null ? _icon.Width + 5 : 0);
            using (var b = new SolidBrush(Selected ? Theme.Accent : Theme.Ink))
            using (var sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter
            })
                g.DrawString(Text, Font, b, new RectangleF(textLeft, r.Y, Width - textLeft - 4, r.Height), sf);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); AnimTo("hover", 1, v => { _hover = v; Invalidate(); }); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); AnimTo("hover", 0, v => { _hover = v; Invalidate(); }); AnimTo("press", 0, v => { _press = v; Invalidate(); }); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            AnimTo("press", 1, v => { _press = v; Invalidate(); });
            if (Picked != null) Picked(this, EventArgs.Empty);
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); AnimTo("press", 0, v => { _press = v; Invalidate(); }); }
    }
}
