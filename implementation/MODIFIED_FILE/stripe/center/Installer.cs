using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace IvanCenter
{
    // ==================================================================== 插件定义
    internal class PluginDef
    {
        public string Key;          // 目录/标识
        public string Name;         // 中文名
        public string EnName;
        public string Desc;
        public string Guid7;        // Rhino 7 注册 GUID
        public string Guid8;        // Rhino 8 注册 GUID
        public string Cmd;          // 工具条按钮要执行的命令
        public string ExtraCommands;// 其它要登记的命令（分号分隔）
        public string Res7;         // Rhino 7 版 (net48) 资源名，可为空
        public string Res8;         // Rhino 8 版 (net7.0) 资源名，可为空
        public string File7;
        public string File8;
        // Rhino 8(net7) 插件的同程序集名 .dll 资源名：Rhino 加载器按 deps.json.runtime 找 .dll，找不到回退成「零 ID」
        public string Res8Dll;
        // Rhino 8(net7) 插件的伴随文件资源名；缺了它们 Rhino 解析不出插件 ID，会按零 ID 登记导致弹窗
        public string Res8Deps;
        public string Res8Runtime;
        public string Deps8Name;    // 落盘的 deps.json 文件名（必须与程序集名一致）
        public string Runtime8Name;
        public string IconKind;     // stripe | vape

        public string GuidFor(int major)
        {
            if (major >= 8) return string.IsNullOrEmpty(Guid8) ? Guid7 : Guid8;
            return string.IsNullOrEmpty(Guid7) ? Guid8 : Guid7;
        }
    }

    internal static class Repo
    {
        public static readonly List<PluginDef> Plugins = new List<PluginDef>
        {
            new PluginDef
            {
                Key = "StripeOnSurface",
                Name = "表面条纹",
                EnName = "StripeOnSurface",
                Desc = "在曲面/多重曲面上生成条纹；端部可选圆角/平齐/完全贴合边界（贴合时条纹按边缘距离内缩、端部随边界形状裁剪）；硬边可倒可调圆角；实时调宽度、间距、角度、边缘距离（真实 mm）",
                Guid7 = "b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91",
                Guid8 = "b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91",
                Cmd = "StripeOnSurface",
                ExtraCommands = "StripeSelfTest;StripePickTarget",
                Res7 = "p_stripe7", Res8 = "p_stripe8",
                File7 = "StripeOnSurface-rh7.rhp", File8 = "StripeOnSurface.rhp",
                Res8Dll = "p_stripe8_dll",
                Res8Deps = "j_stripe8_deps", Res8Runtime = "j_stripe8_rt",
                Deps8Name = "StripeOnSurface.deps.json", Runtime8Name = "StripeOnSurface.runtimeconfig.json",
                IconKind = "stripe"
            },
            new PluginDef
            {
                Key = "VapeVolume",
                Name = "烟油容量计算器",
                EnName = "VapeVolume",
                Desc = "计算烟油瓶/仓体容量与注入量，实时显示；命令 VapeVolume / VapeVolumeWatch",
                Guid7 = "C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01",
                Guid8 = "A3F27B54-9C41-4E88-B0D6-7E5C1A93D842",
                Cmd = "VapeVolume",
                ExtraCommands = "VapeVolumeWatch;VapeVolumeSelfTest;VapePickTarget",
                Res7 = "p_vape7", Res8 = "p_vape8",
                File7 = "VapeVolume-Rhino7.rhp", File8 = "VapeVolume.rhp",
                Res8Dll = "p_vape8_dll",
                Res8Deps = "j_vape8_deps", Res8Runtime = "j_vape8_rt",
                Deps8Name = "VapeVolume.deps.json", Runtime8Name = "VapeVolume.runtimeconfig.json",
                IconKind = "vape"
            },
            new PluginDef
            {
                Key = "HalftoneDots",
                Name = "参数化阵列纹理",
                EnName = "HalftoneDots",
                Desc = "6 种阵列排布（网格/交错/六边/同心环/螺旋/抖动）× 4 种形状（圆/三角/方/六边）；同心环/螺旋/抖动 可点选圆心、从圆心向外扩散；曲面上图形不变形、自动铺满整张面",
                Guid7 = "9C4E7A21-3D58-4B06-8E97-2F1B6A3C5D08",
                Guid8 = "4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27",
                Cmd = "ParametricTexture",
                ExtraCommands = "HalftoneDots;HalftonePickTarget;HalftonePickGradient",
                Res7 = "p_half7", Res8 = "p_half8",
                File7 = "HalftoneDots-rh7.rhp", File8 = "HalftoneDots.rhp",
                Res8Dll = "p_half8_dll",
                Res8Deps = "j_half8_deps", Res8Runtime = "j_half8_rt",
                Deps8Name = "HalftoneDots.deps.json", Runtime8Name = "HalftoneDots.runtimeconfig.json",
                IconKind = "halftone"
            },
            new PluginDef
            {
                Key = "VoronoiTexture",
                Name = "泰森多边形纹",
                EnName = "VoronoiTexture",
                Desc = "在参考曲面上生成泰森多边形（Voronoi）胞元凹凸纹理：胞元像枕头一样鼓起或凹下，胞元之间圆滑沟槽过渡；实时预览，可调胞元大小、凹凸深度、过渡宽度、形状、规整度、随机种子",
                Guid7 = "7E4B1C85-9A37-4D62-8F10-2C6B5E8D4A93",
                Guid8 = "5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72",
                Cmd = "VoronoiTexture",
                ExtraCommands = "VoronoiSelfTest;VoronoiPickTarget;VoronoiPickGradient",
                Res7 = "p_vor7", Res8 = "p_vor8",
                File7 = "VoronoiTexture-rh7.rhp", File8 = "VoronoiTexture.rhp",
                Res8Dll = "p_vor8_dll",
                Res8Deps = "j_vor8_deps", Res8Runtime = "j_vor8_rt",
                Deps8Name = "VoronoiTexture.deps.json", Runtime8Name = "VoronoiTexture.runtimeconfig.json",
                IconKind = "voronoi"
            },
            new PluginDef
            {
                Key = "RadialDots",
                Name = "径向渐变圆点",
                EnName = "RadialDots",
                Desc = "按半径渐变的圆点图案：4 种阵列（同心环/螺旋/方形网格/交错网格）× 5 种图形（圆/方/三角/六边/圆方交替），尺寸从内到外按峰值位置与衰减渐变；重叠的图形自动布尔合并成一个整体，没有交集的保持独立；纯参数面板（不需要选参考面），直接在工作平面上生成",
                Guid7 = "45403763-4C11-42E3-989C-430F82FEEB77",
                Guid8 = "F54E41C9-847C-4ED5-AE5C-FD905C55A1B6",
                Cmd = "RadialDots",
                ExtraCommands = "RadialDots;RadialDotsSelfTest",
                Res7 = "p_rad7", Res8 = "p_rad8",
                File7 = "RadialDots-rh7.rhp", File8 = "RadialDots.rhp",
                Res8Dll = "p_rad8_dll",
                Res8Deps = "j_rad8_deps", Res8Runtime = "j_rad8_rt",
                Deps8Name = "RadialDots.deps.json", Runtime8Name = "RadialDots.runtimeconfig.json",
                IconKind = "radialdots"
            },
            new PluginDef
            {
                Key = "MeshFix",
                Name = "网格修复",
                EnName = "MeshFix",
                Desc = "一键修复「着色/渲染模式下复杂修剪曲面只剩边缘线、面体不显示」：把物件的自定义渲染网格参数「最大长宽比」由 0 改成 6 并重建渲染网格，参数随 3dm 保存；无面板工具 —— 工具条上点一下即修复：选中物件就修选中的，没选中就自动扫描整份文件里渲染网格退化的物件",
                Guid7 = "3F8A2C64-1D75-4B93-A6E2-5C70491B8D3F",
                Guid8 = "8B1E47D2-6A35-4C09-9F82-3D6E15A7B0C4",
                Cmd = "MeshFix",
                ExtraCommands = "MeshFixSelfTest",
                Res7 = "p_mesh7", Res8 = "p_mesh8",
                File7 = "MeshFix-rh7.rhp", File8 = "MeshFix.rhp",
                Res8Dll = "p_mesh8_dll",
                Res8Deps = "j_mesh8_deps", Res8Runtime = "j_mesh8_rt",
                Deps8Name = "MeshFix.deps.json", Runtime8Name = "MeshFix.runtimeconfig.json",
                IconKind = "meshfix"
            },
            new PluginDef
            {
                Key = "DiamondFacet",
                Name = "钻石切面",
                EnName = "DiamondFacet",
                Desc = "在平面/闭合曲线边界内生成凹凸的钻石切面：随机三角剖分（面片大小、随机种子、松弛次数可调），顶点沿法向随机高低连成一个多面体，可「固定边界」把边界上的点锁在边界上；输出可选「仅线框」或「线框 + 每个三角面独立的 NURBS 平面」；支持多选边界、实时预览",
                Guid7 = "6A1D4E27-9C83-4B15-A7F2-3D58E1B96C40",
                Guid8 = "D2F75B18-4E69-4A3C-8B51-7C0E29D6F3A8",
                Cmd = "DiamondFacet",
                ExtraCommands = "DiamondFacetSelfTest;DiamondFacetPickTarget;DiamondFacetProbe",
                Res7 = "p_dia7", Res8 = "p_dia8",
                File7 = "DiamondFacet-rh7.rhp", File8 = "DiamondFacet.rhp",
                Res8Dll = "p_dia8_dll",
                Res8Deps = "j_dia8_deps", Res8Runtime = "j_dia8_rt",
                Deps8Name = "DiamondFacet.deps.json", Runtime8Name = "DiamondFacet.runtimeconfig.json",
                IconKind = "diamond"
            },
        };

        public static string RootDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IVAN", "plugins");
            }
        }

        public static string RuiName { get { return "IVAN-CENTER.rui"; } }

        // ⚠️ 这些 guid 必须【固定不变】：
        // Rhino 把 RUI 文件的「根 guid」当作 containers.xml 里的 file_name guid，
        // 工作区的 dock_bar 又按这个 guid 引用文件。若每次安装都换新 guid，
        // 旧的 dock_bar 立刻失效 → 工具条不显示（Rhino 7/8 都一样）。
        public static string RuiFileGuid { get { return "b1c2d3e4-5f60-4718-9a2b-3c4d5e6f7081"; } }   // RUI 根 guid
        public static string RuiGroupGuid { get { return "b1c2d3e4-5f60-4718-9a2b-3c4d5e6f7082"; } }  // 工具条组
        public static string RuiBarGuid { get { return "b1c2d3e4-5f60-4718-9a2b-3c4d5e6f7083"; } }    // 工具条
        public static string RuiGroupItemGuid { get { return "b1c2d3e4-5f60-4718-9a2b-3c4d5e6f7084"; } }
        public static string RuiDockGuid { get { return "c2d3e4f5-6071-4829-ab3c-4d5e6f708192"; } }   // 工作区里的 dock_bar
        public static string RuiAppGuid { get { return "d3e4f506-7182-493a-bc4d-5e6f708192a3"; } }

        public static string RuiItemGuid(int i) { return string.Format("b1c2d3e4-5f60-4718-9a2b-3c4d5e6f71{0:D2}", 10 + i); }
        public static string RuiMacroGuid(int i) { return string.Format("b1c2d3e4-5f60-4718-9a2b-3c4d5e6f72{0:D2}", 10 + i); }

        public static string GroupName { get { return "IVAN CENTER"; } }
    }

    // ==================================================================== 图标
    // 一套「标本卡」图标家族：中性圆角底板 + 顶部受光渐变 + 单一深色系强调色图形。
    // 五个插件的色相都取深而稳的（青/靛/琥珀/蓝/紫），彼此同族不打架；16/32 两档都保证一眼能认。
    internal static class IconFactory
    {
        static readonly Color TileTop = Color.FromArgb(255, 255, 255);
        static readonly Color TileBottom = Color.FromArgb(255, 232, 238, 243);
        static readonly Color TileEdge = Color.FromArgb(255, 206, 214, 222);

        public static Bitmap Create(string kind, int size)
        {
            return IvanUi.Theme.CreateGlassIcon(kind, size);
        }

        /// <summary>中性圆角底板（所有图标共用，保证是一套）</summary>
        static void Tile(Graphics g, int size)
        {
            float pad = Math.Max(1f, size / 16f);
            var rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
            using (var path = RoundRect(rect, Math.Max(2f, size * 0.18f)))
            {
                using (var b = new LinearGradientBrush(new RectangleF(0, 0, size, size), TileTop, TileBottom, 90f))
                    g.FillPath(b, path);
                using (var p = new Pen(TileEdge, Math.Max(1f, size / 16f)))
                    g.DrawPath(p, path);
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
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

        /// <summary>径向渐变圆点：三圈点，中间一圈最大（大小随半径渐变）</summary>
        static void DrawRadialDots(Graphics g, int size)
        {
            Color ink = Color.FromArgb(255, 96, 62, 186);
            float c = size / 2f;
            float[] ringR = { 0.17f, 0.325f, 0.445f };
            float[] dotD = { 0.115f, 0.155f, 0.055f };
            using (var br = new SolidBrush(ink))
            {
                for (int ring = 0; ring < ringR.Length; ring++)
                {
                    float rad = size * ringR[ring];
                    int n = ring == 0 ? 6 : (ring == 1 ? 10 : 14);
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2.0 * Math.PI * i / n + ring * 0.34;
                        float x = c + rad * (float)Math.Cos(a), y = c + rad * (float)Math.Sin(a);
                        float d = Math.Max(1f, size * dotD[ring]);
                        g.FillEllipse(br, x - d * 0.5f, y - d * 0.5f, d, d);
                    }
                }
            }
        }

        /// <summary>条纹：三条同角度的圆头斜条</summary>
        static void DrawStripe(Graphics g, int size)
        {
            Color ink = Color.FromArgb(255, 14, 116, 144);
            float w = Math.Max(1.2f, size / 9.5f);
            using (var pen = new Pen(ink, w))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                for (int i = 0; i < 3; i++)
                {
                    float off = size * (0.30f + 0.20f * i);
                    g.DrawLine(pen, off - size * 0.24f, size * 0.80f, off + size * 0.24f, size * 0.20f);
                }
            }
        }

        /// <summary>阵列：3x3 圆点，半径从中心向外递减（就是插件的核心效果）</summary>
        static void DrawHalftone(Graphics g, int size)
        {
            Color ink = Color.FromArgb(255, 62, 76, 154);
            float c = size / 2f;
            using (var br = new SolidBrush(ink))
            {
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        float x = size * (0.30f + 0.20f * i);
                        float y = size * (0.30f + 0.20f * j);
                        float dx = x - c, dy = y - c;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy) / (c * 0.80f);
                        if (d > 1f) d = 1f;
                        float r = size * (0.115f - 0.075f * d);
                        if (r < size * 0.02f) continue;
                        g.FillEllipse(br, x - r, y - r, r * 2, r * 2);
                    }
            }
        }

        /// <summary>泰森多边形：4 站点对正方形做半平面裁剪出的真胞元拼块，块间留缝当沟槽</summary>
        static void DrawVoronoi(Graphics g, int size)
        {
            Color ink = Color.FromArgb(255, 169, 112, 26);
            var sites = new PointF[]
            {
                new PointF(0.28f, 0.30f),
                new PointF(0.74f, 0.24f),
                new PointF(0.24f, 0.74f),
                new PointF(0.72f, 0.72f),
            };

            for (int i = 0; i < sites.Length; i++)
            {
                var cell = new List<PointF>
                {
                    new PointF(0f, 0f), new PointF(1f, 0f), new PointF(1f, 1f), new PointF(0f, 1f)
                };
                for (int j = 0; j < sites.Length && cell.Count >= 3; j++)
                {
                    if (j == i) continue;
                    cell = ClipHalf(cell, sites[i], sites[j]);
                }
                if (cell.Count < 3) continue;

                float cx = 0, cy = 0;
                foreach (PointF p in cell) { cx += p.X; cy += p.Y; }
                cx /= cell.Count; cy /= cell.Count;
                const float gap = 0.055f;
                var poly = new PointF[cell.Count];
                for (int k = 0; k < cell.Count; k++)
                {
                    float dx = cx - cell[k].X, dy = cy - cell[k].Y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    float t = len < 1e-5f ? 0f : gap / len;
                    poly[k] = new PointF((cell[k].X + dx * t) * size, (cell[k].Y + dy * t) * size);
                }
                using (var br = new SolidBrush(ink)) g.FillPolygon(br, poly);
            }
        }

        /// <summary>烟油：一滴油（与面板头部的同形，实心）</summary>
        static void DrawVape(Graphics g, int size)
        {
            Color ink = Color.FromArgb(255, 22, 104, 179);
            float cx = size * 0.5f, top = size * 0.22f, bot = size * 0.80f, r = size * 0.20f;
            var path = new GraphicsPath();
            path.AddBezier(cx, top, cx + r * 1.35f, top + r * 1.5f, cx + r, bot, cx, bot);
            path.AddBezier(cx, bot, cx - r, bot, cx - r * 1.35f, top + r * 1.5f, cx, top);
            path.CloseFigure();
            using (var br = new SolidBrush(ink)) g.FillPath(br, path);
            using (var hb = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                g.FillEllipse(hb, cx - r * 0.50f, top + r * 1.15f, r * 0.42f, r * 0.55f);
            path.Dispose();
        }

        /// <summary>用「站点 a 与 b 的垂直平分线」把凸多边形裁到 a 一侧</summary>
        static List<PointF> ClipHalf(List<PointF> poly, PointF a, PointF b)
        {
            var outp = new List<PointF>();
            float mx = (a.X + b.X) * 0.5f, my = (a.Y + b.Y) * 0.5f;
            float nx = b.X - a.X, ny = b.Y - a.Y;
            for (int i = 0; i < poly.Count; i++)
            {
                PointF cur = poly[i], nxt = poly[(i + 1) % poly.Count];
                float dc = (cur.X - mx) * nx + (cur.Y - my) * ny;
                float dn = (nxt.X - mx) * nx + (nxt.Y - my) * ny;
                if (dc <= 0) outp.Add(cur);
                if ((dc < 0 && dn > 0) || (dc > 0 && dn < 0))
                {
                    float t = dc / (dc - dn);
                    outp.Add(new PointF(cur.X + (nxt.X - cur.X) * t, cur.Y + (nxt.Y - cur.Y) * t));
                }
            }
            return outp;
        }

        public static string StripBase64(List<string> kinds, int size)
        {
            int n = Math.Max(1, kinds.Count);
            using (var strip = new Bitmap(size * n, size))
            {
                using (Graphics g = Graphics.FromImage(strip))
                {
                    g.Clear(Color.Transparent);
                    for (int i = 0; i < n; i++)
                        using (Bitmap one = Create(kinds[i], size))
                            g.DrawImageUnscaled(one, i * size, 0);
                }
                using (var ms = new MemoryStream())
                {
                    strip.Save(ms, ImageFormat.Png);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }
    }

    // ==================================================================== Rhino 环境
    internal class RhinoInstall
    {
        public string Exe;
        public int Major;
        public string Dir;
    }

    // ==================================================================== 主程序
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = false, uninstall = false, uitest = false;
            if (args != null)
                foreach (string a in args)
                {
                    if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                    if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                    if (a.Equals("/uitest", StringComparison.OrdinalIgnoreCase)) uitest = true;
                }

            var form = new CenterForm();
            if (silent) Environment.Exit(form.RunSilent(uninstall));
            if (uitest) Environment.Exit(form.RunUiTest());
            Application.Run(form);
        }
    }

    internal class Installer
    {
        public readonly StringBuilder LogText = new StringBuilder();
        public bool Silent;

        public void Log(string s)
        {
            LogText.AppendLine(s);
            if (!Silent && OnLog != null) OnLog(s);
        }

        public event Action<string> OnLog;

        public static List<RhinoInstall> FindRhinos()
        {
            var list = new List<RhinoInstall>();
            RegistryKey[] roots = { Registry.LocalMachine, Registry.CurrentUser };
            foreach (RegistryKey root in roots)
            {
                using (RegistryKey k = root.OpenSubKey(@"SOFTWARE\McNeel\Rhinoceros"))
                {
                    if (k == null) continue;
                    foreach (string ver in k.GetSubKeyNames())
                    {
                        using (RegistryKey ik = k.OpenSubKey(ver + @"\Install"))
                        {
                            if (ik == null) continue;
                            string exe = ik.GetValue("ExePath") as string;
                            if (string.IsNullOrEmpty(exe))
                            {
                                string p = ik.GetValue("Path") as string;
                                if (!string.IsNullOrEmpty(p)) exe = Path.Combine(p, "Rhino.exe");
                            }
                            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
                            int major = 0;
                            try { major = FileVersionInfo.GetVersionInfo(exe).FileMajorPart; } catch { }
                            if (major <= 0) continue;
                            bool dup = false;
                            foreach (RhinoInstall x in list)
                                if (string.Equals(x.Exe, exe, StringComparison.OrdinalIgnoreCase)) dup = true;
                            if (!dup) list.Add(new RhinoInstall { Exe = exe, Major = major, Dir = Path.GetDirectoryName(exe) });
                        }
                    }
                }
            }
            return list;
        }

        public string InstallDirOf(PluginDef p) { return Path.Combine(Repo.RootDir, p.Key); }
        /// <summary>每个 Rhino 版本单独一个子目录：避免 net48 / net7 两份同名伴随文件互相覆盖</summary>
        public string VerDirOf(PluginDef p, int major)
        {
            return Path.Combine(InstallDirOf(p), major >= 8 ? "rh8" : "rh7");
        }
        public string RhpPath(PluginDef p, int major)
        {
            return Path.Combine(VerDirOf(p, major), major >= 8 ? p.File8 : p.File7);
        }
        /// <summary>net7 程序集同名的 .dll 路径：Rhino 8 加载器按 deps.json.runtime 找的就是这个 .dll，找不到会回退成「零 ID」</summary>
        public string DllPath(PluginDef p, int major)
        {
            if (major < 8) return null;
            string asmName = Path.GetFileNameWithoutExtension(p.File8);
            return Path.Combine(VerDirOf(p, major), asmName + ".dll");
        }

        public bool IsInstalled(PluginDef p)
        {
            List<RhinoInstall> rhinos = FindRhinos();
            foreach (RhinoInstall r in rhinos)
            {
                string key = string.Format(@"Software\MCNeel\Rhinoceros\{0}.0\Plug-Ins\{1}", r.Major, p.GuidFor(r.Major));
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key + @"\PlugIn"))
                {
                    if (k == null) continue;
                    string fn = k.GetValue("FileName") as string;
                    if (!string.IsNullOrEmpty(fn) && fn.IndexOf(p.Key, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        public bool Install(PluginDef p, List<RhinoInstall> rhinos)
        {
            // 先清空整个插件目录（含旧的子目录布局），保证不会残留旧版本
            try
            {
                if (Directory.Exists(InstallDirOf(p))) Directory.Delete(InstallDirOf(p), true);
            }
            catch (Exception ex) { Log("  ! 旧文件删除失败（可能 Rhino 还开着）：" + ex.Message); }

            Directory.CreateDirectory(VerDirOf(p, 7));
            Directory.CreateDirectory(VerDirOf(p, 8));

            Extract(p.Res7, RhpPath(p, 7));
            Extract(p.Res8, RhpPath(p, 8));

            // 只释放裸 .rhp（与 Rhino 自带插件一致）。
            // 插件 ID 由程序集级 [assembly: Guid] 提供；之前误加 deps.json/.dll 反而干扰加载。
            Log("  √ 释放插件文件 → " + InstallDirOf(p));

            // 清掉历史遗留的“全零 GUID”注册项（早期无 [Guid] 构建被 Rhino 自动登记的产物）
            CleanStaleZeroKey(p);

            bool any = false;
            foreach (RhinoInstall r in rhinos)
            {
                if (r.Major < 7) { Log(string.Format("  · Rhino {0} 不支持，跳过", r.Major)); continue; }
                string rhp = RhpPath(p, r.Major);
                if (!File.Exists(rhp)) { Log(string.Format("  · 缺少 Rhino {0} 版构建，跳过", r.Major)); continue; }

                string registeredPath = rhp;

                // 关键校验：插件文件内嵌的 [Guid] 必须与要写入注册表的 GUID 一致。
                // 不一致时 Rhino 会按文件内真实 ID 处理，重装后会出现“ID 已被使用”弹窗。
                if (!RhpContainsGuid(registeredPath, p.GuidFor(r.Major)))
                {
                    Log(string.Format("  ✗ {0}：插件文件的 GUID 与注册 GUID（{1}）不一致，已跳过注册（否则犀牛会弹“ID 已被使用”）。请重新构建该插件。", p.EnName, p.GuidFor(r.Major)));
                    continue;
                }

                string key = string.Format(@"Software\MCNeel\Rhinoceros\{0}.0\Plug-Ins\{1}", r.Major, p.GuidFor(r.Major));

                // 先全清：删掉这个插件在本版本下所有旧注册项（含重复项、全零 GUID 项），再重新注册
                RemoveAllRegistrationsFor(p, r);

                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key))
                {
                    k.SetValue("Name", p.Name + " (" + p.EnName + ")");
                    k.SetValue("EnglishName", p.EnName);
                    k.SetValue("Organization", "IVAN");
                    k.SetValue("AddToHelpMenu", 0, RegistryValueKind.DWord);
                    k.SetValue("LoadMode", 1, RegistryValueKind.DWord);
                    k.SetValue("Type", 16, RegistryValueKind.DWord);
                    k.SetValue("IsDotNETPlugIn", 1, RegistryValueKind.DWord);
                    k.SetValue("DirectoryInstall", 0, RegistryValueKind.DWord);
                    k.SetValue("RegPath", @"\\HKEY_CURRENT_USER\" + key);
                    using (RegistryKey pk = k.CreateSubKey("PlugIn")) pk.SetValue("FileName", registeredPath);
                    using (RegistryKey ck = k.CreateSubKey("CommandList"))
                    {
                        ck.SetValue(p.Cmd, "2;" + p.Cmd);
                        if (!string.IsNullOrEmpty(p.ExtraCommands))
                            foreach (string c in p.ExtraCommands.Split(';'))
                                if (c.Trim().Length > 0) ck.SetValue(c.Trim(), "2;" + c.Trim());
                    }
                }
                Log(string.Format("  √ 已注册到 Rhino {0}（GUID {1}）", r.Major, p.GuidFor(r.Major)));
                any = true;
            }
            return any;
        }

        /// <summary>把该插件在这个 Rhino 版本下所有注册项清干净（含重复项 / 全零 GUID 项）</summary>
        public void RemoveAllRegistrationsFor(PluginDef p, RhinoInstall r)
        {
            string root = string.Format(@"Software\MCNeel\Rhinoceros\{0}.0\Plug-Ins", r.Major);
            string dir = InstallDirOf(p);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(root, true))
                {
                    if (k == null) return;
                    string target = p.GuidFor(r.Major);
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        string fn = null;
                        try
                        {
                            using (RegistryKey pk = k.OpenSubKey(sub + @"\PlugIn")) 
                                if (pk != null) fn = pk.GetValue("FileName") as string;
                        }
                        catch { }

                        bool pointsToUs = !string.IsNullOrEmpty(fn) &&
                                          fn.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
                        bool isTarget = string.Equals(sub, target, StringComparison.OrdinalIgnoreCase);
                        bool isZero = sub == "00000000-0000-0000-0000-000000000000";

                        if ((pointsToUs && !isTarget) || (isZero && pointsToUs))
                        {
                            k.DeleteSubKeyTree(sub, false);
                            Log("  · 清除重复注册项 " + sub);
                        }
                    }
                }
            }
            catch (Exception ex) { Log("  · 清理注册项失败：" + ex.Message); }
        }

        /// <summary>清掉以前误注册在“全零 GUID”下的同名插件项（旧版遗留）</summary>
        void CleanStaleZeroKey(PluginDef p)
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Software\MCNeel\Rhinoceros", true))
                {
                    if (root == null) return;
                    foreach (string ver in root.GetSubKeyNames())
                    {
                        string path = ver + @"\Plug-Ins\00000000-0000-0000-0000-000000000000";
                        using (RegistryKey zk = root.OpenSubKey(path, true))
                        {
                            if (zk == null) continue;
                            string name = zk.GetValue("Name") as string;
                            string en = zk.GetValue("EnglishName") as string;
                            if ((name != null && name.IndexOf(p.EnName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (en != null && en.IndexOf(p.EnName, StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                root.DeleteSubKeyTree(path, false);
                                Log("  · 已清理旧的错误注册项（全零 GUID）");
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public void Uninstall(PluginDef p, List<RhinoInstall> rhinos)
        {
            foreach (RhinoInstall r in rhinos)
            {
                // 先把指向本插件目录的其它注册项（重复项、全零 GUID 遗留项）清掉：
                // 否则“卸载 → 重装”后它们会和正常项一起加载同一个 .rhp，触发“ID 已被使用”弹窗。
                RemoveAllRegistrationsFor(p, r);

                string key = string.Format(@"Software\MCNeel\Rhinoceros\{0}.0\Plug-Ins\{1}", r.Major, p.GuidFor(r.Major));
                try { Registry.CurrentUser.DeleteSubKeyTree(key, false); Log("  √ 已删除注册项 " + key); } catch { }
            }
            CleanStaleZeroKey(p);
            try
            {
                if (Directory.Exists(InstallDirOf(p))) { Directory.Delete(InstallDirOf(p), true); Log("  √ 已删除 " + InstallDirOf(p)); }
            }
            catch { }
        }

        public void Extract(string res, string dest)
        {
            if (string.IsNullOrEmpty(res)) return;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
            {
                if (s == null) throw new Exception("安装包内缺少资源：" + res);
                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
        }

        /// <summary>
        /// 校验 .rhp 内嵌的 [Guid] 特性字符串是否与要注册的 GUID 一致。
        /// 纯字节扫描（ASCII）：GuidAttribute 的参数以字符串形式存在程序集元数据里，
        /// 对 net48 / net7.0 两种构建都有效，且无需在 net48 安装器里加载 net7 程序集。
        /// </summary>
        public static bool RhpContainsGuid(string rhpPath, string guid)
        {
            try
            {
                if (string.IsNullOrEmpty(guid)) return false;
                string text = Encoding.ASCII.GetString(File.ReadAllBytes(rhpPath));
                return text.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- 工具条
        public void InstallToolbar(List<PluginDef> plugins, List<RhinoInstall> rhinos, string ruiPath)
        {
            BuildRui(plugins, ruiPath);
            Log("  √ 生成工具条 IVAN CENTER（" + plugins.Count + " 个按钮）→ " + ruiPath);

            // 在插件安装目录也放一份 .rui：自动登记万一不生效，可在 Rhino「选项 > 工具列」里手动打开它
            try
            {
                Directory.CreateDirectory(Repo.RootDir);
                string manual = Path.Combine(Repo.RootDir, Repo.RuiName);
                File.Copy(ruiPath, manual, true);
                Log("  √ 工具列文件副本（可手动加载）→ " + manual);
            }
            catch (Exception ex) { Log("  ! 复制工具列副本失败：" + ex.Message); }

            foreach (RhinoInstall r in rhinos)
            {
                string uiDir = Path.Combine(UserData(r.Major), "UI", "Plug-ins");
                Directory.CreateDirectory(uiDir);
                string ruiDst = Path.Combine(uiDir, Repo.RuiName);
                File.Copy(ruiPath, ruiDst, true);

                // 同时放一份到 UI 目录（与 Rhino 默认工具列 default.rui 同级）：
                // 自动登记万一失效，可在 Rhino「选项 > 工具列 > 打开」里直接选这个文件
                try
                {
                    string uiRoot = Path.Combine(UserData(r.Major), "UI");
                    Directory.CreateDirectory(uiRoot);
                    File.Copy(ruiPath, Path.Combine(uiRoot, Repo.RuiName), true);
                }
                catch (Exception ex) { Log("  ! 复制到 UI 目录失败：" + ex.Message); }

                int n = PatchWorkspaces(r.Major, ruiDst, plugins.Count);
                Log(string.Format("  √ Rhino {0}：工具条已登记（{1} 处配置）", r.Major, n));
            }
        }

        static string UserData(int major)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "McNeel", "Rhinoceros", major + ".0");
        }

        void BuildRui(List<PluginDef> plugins, string outPath)
        {
            var kinds = new List<string>();
            foreach (PluginDef p in plugins) kinds.Add(p.IconKind);
            string strip16 = IconFactory.StripBase64(kinds, 16);
            string strip32 = IconFactory.StripBase64(kinds, 32);

            // 全部使用固定 guid：Rhino 按根 guid 关联 containers.xml 与工作区 dock_bar，
            // 每次安装换 guid 会让旧的 dock_bar 失效、工具条不显示。
            string group = Repo.RuiGroupGuid;
            string group64 = "b1c2d3e4-5f60-4718-9a2b-3c4d5e6f7085";
            string tb = Repo.RuiBarGuid;
            string groupItem = Repo.RuiGroupItemGuid;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<RhinoUI major_ver=\"3\" minor_ver=\"0\" guid=\"" + Repo.RuiFileGuid + "\" localize=\"False\" default_language_id=\"1033\" dpi_scale=\"100\">");
            sb.AppendLine("  <menus />");
            sb.AppendLine("  <tool_bar_groups>");
            // 与 Rhino 自带插件工具条（XNurbs 等）保持同样的属性集：active_tool_bar_group 不能省
            sb.AppendLine("    <tool_bar_group guid=\"" + group + "\" dock_bar_guid32=\"00000000-0000-0000-0000-000000000000\" dock_bar_guid64=\"" + group64 + "\" active_tool_bar_group=\"" + groupItem + "\" single_file=\"False\" hide_single_tab=\"False\" point_floating=\"180,180\">");
            sb.AppendLine("      <text><locale_1033>IVAN CENTER</locale_1033><locale_2052>IVAN CENTER</locale_2052></text>");
            sb.AppendLine("      <tool_bar_group_item guid=\"" + groupItem + "\" major_version=\"1\" minor_version=\"1\">");
            sb.AppendLine("        <text><locale_1033>IVAN CENTER</locale_1033><locale_2052>IVAN CENTER</locale_2052></text>");
            sb.AppendLine("        <tool_bar_id>" + tb + "</tool_bar_id>");
            sb.AppendLine("      </tool_bar_group_item>");
            sb.AppendLine("      <dock_bar_info dpi_scale=\"100\" dock_bar=\"False\" docking=\"True\" horz=\"False\" visible=\"True\" floating=\"True\" mru_float_style=\"8192\" point_pos=\"-2,-2\" float_point=\"180,180\" rect_mru_dock_pos=\"0,0,0,0\" dock_location=\"left\" float_size=\"235,68\" />");
            sb.AppendLine("    </tool_bar_group>");
            sb.AppendLine("  </tool_bar_groups>");
            sb.AppendLine("  <tool_bars>");
            sb.AppendLine("    <tool_bar guid=\"" + tb + "\" bitmap_id=\"" + tb + "\">");
            sb.AppendLine("      <text><locale_1033>IVAN CENTER</locale_1033><locale_2052>IVAN CENTER</locale_2052></text>");
            var macros = new StringBuilder();
            var bmpItems = new StringBuilder();
            var guids = new List<string>();
            for (int bi = 0; bi < plugins.Count; bi++)
            {
                PluginDef p = plugins[bi];
                string itemGuid = Repo.RuiItemGuid(bi);      // 固定 guid（同 RUI 根 guid 的理由）
                string macroGuid = Repo.RuiMacroGuid(bi);
                guids.Add(macroGuid);
                sb.AppendLine("      <tool_bar_item guid=\"" + itemGuid + "\" display_style_from_parent=\"False\">");
                sb.AppendLine("        <left_macro_id>" + macroGuid + "</left_macro_id>");
                sb.AppendLine("      </tool_bar_item>");
                macros.AppendLine("    <macro_item guid=\"" + macroGuid + "\" bitmap_id=\"" + macroGuid + "\">");
                macros.AppendLine("      <text><locale_1033>" + p.EnName + "</locale_1033><locale_2052>" + p.Name + "</locale_2052></text>");
                macros.AppendLine("      <tooltip><locale_1033>" + p.Name + "</locale_1033><locale_2052>" + p.Desc.Replace("&", "&amp;").Replace("<", "&lt;") + "</locale_2052></tooltip>");
                macros.AppendLine("      <button_text><locale_1033>" + p.EnName + "</locale_1033><locale_2052>" + p.Name + "</locale_2052></button_text>");
                macros.AppendLine("      <script>! _" + p.Cmd + "</script>");
                macros.AppendLine("    </macro_item>");
            }
            for (int i = 0; i < guids.Count; i++)
                bmpItems.AppendLine("      <bitmap_item guid=\"" + guids[i] + "\" index=\"" + i + "\" />");

            sb.AppendLine("    </tool_bar>");
            sb.AppendLine("  </tool_bars>");
            sb.AppendLine("  <macros>");
            sb.Append(macros);
            sb.AppendLine("  </macros>");
            sb.AppendLine("  <bitmaps>");
            sb.AppendLine("    <small_bitmap item_width=\"16\" item_height=\"16\">");
            sb.Append(bmpItems);
            sb.AppendLine("      <bitmap>" + strip16 + "</bitmap>");
            sb.AppendLine("    </small_bitmap>");
            sb.AppendLine("    <large_bitmap item_width=\"32\" item_height=\"32\">");
            sb.Append(bmpItems);
            sb.AppendLine("      <bitmap>" + strip32 + "</bitmap>");
            sb.AppendLine("    </large_bitmap>");
            sb.AppendLine("  </bitmaps>");
            sb.AppendLine("</RhinoUI>");

            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
        }

        int PatchWorkspaces(int major, string ruiPath, int buttonCount)
        {
            int patched = 0;
            string settings = Path.Combine(UserData(major), "settings");
            if (!Directory.Exists(settings))
            {
                // Rhino 首次启动前没有 settings 目录，无从登记工作区 → 工具条会加载但不自动显示
                Log(string.Format("  ! Rhino {0} 还没运行过（找不到 settings 目录）：工具条文件已放好，先启动一次 Rhino {0}，再点一次「全部安装 / 更新」即可自动显示。", major));
                return 0;
            }

            string groupGuid, barGuid;
            if (!ReadRuiGuids(ruiPath, out groupGuid, out barGuid)) return 0;

            // 关键：Rhino 用 RUI 文件自身的「根 guid」作为 containers 里 file_name 的 guid，
            // 工作区 dock_bar 也按这个 guid 引用。所以这里必须用刚生成的 RUI 的根 guid
            // （BuildRui 已把它固定成 Repo.RuiFileGuid），而不是去读 containers 里的旧值——
            // 否则文件一换 guid，dock_bar 立刻失效、工具条不显示。
            string fileGuid = Repo.RuiFileGuid;

            // 采用 Rhino 自己生成的「独立浮动 dock_bar」格式（与屏幕上能显示的“曲面工具”等一致）：
            // 不带 source_group_file / source_group，只靠 guid + placement(visible=True) + tabs 引用 .rui 里的工具条
            string block = "<dock_bar guid=\"" + Repo.RuiDockGuid + "\">" +
                "<placement dock_location=\"Floating\" float_point=\"600,300\" float_size=\"" +
                (34 + 46 * Math.Max(1, buttonCount)) + ",54\" visible=\"True\" />" +
                "<tabs name=\"IVAN CENTER\" selected_item=\"" + barGuid + "\" torn_off=\"True\" display_style=\"BitmapAndText\">" +
                "<name><locale_2052>IVAN CENTER</locale_2052><locale_1033>IVAN CENTER</locale_1033></name>" +
                "<tool_bar guid=\"" + barGuid + "\" file=\"" + fileGuid + "\" />" +
                "</tabs></dock_bar>";

            // 必须与 Rhino 自己写的格式一致：source="File" 且【不要】带 plug_in_guid。
            // 带 plug_in_guid + source="PlugInFolder" 时 Rhino 认为该 rui 属于某个插件，
            // 找不到那个插件就直接跳过、根本不加载工具条（实测：Rhino 不会为它生成状态文件）。
            string entry = "<file_name guid=\"" + fileGuid + "\" source=\"File\">" + ruiPath + "</file_name>";

            foreach (string scheme in Directory.GetDirectories(settings))
            {
                // containers.xml 也要写 dock_bar：Rhino 自己生成的 dock_bar 就放在这里
                string containers = Path.Combine(scheme, "containers.xml");
                if (File.Exists(containers)) { if (PatchFile(containers, entry, block, fileGuid, ruiPath)) patched++; }
                string wsDir = Path.Combine(scheme, "workspaces");
                if (!Directory.Exists(wsDir)) continue;
                foreach (string ws in Directory.GetFiles(wsDir, "*.xml"))
                    if (PatchFile(ws, entry, block, fileGuid, ruiPath)) patched++;
            }
            return patched;
        }

        /// <summary>从 containers.xml 里读出指向该 rui 路径的 file_name guid（Rhino 自己写的那条）</summary>
        static string FindRuiFileGuid(string containersPath, string ruiPath)
        {
            try
            {
                string s = File.ReadAllText(containersPath, Encoding.UTF8);
                Match m = Regex.Match(s,
                    "<file_name[^>]*guid=\"([^\"]+)\"[^>]*>\\s*" + Regex.Escape(ruiPath) + "\\s*</file_name>",
                    RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
            catch { }
            return null;
        }

        bool PatchFile(string file, string entry, string dockBlock, string fileGuid, string ruiPath)
        {
            try
            {
                string s = File.ReadAllText(file, Encoding.UTF8);
                string orig = s;

                // 清掉旧 dock_bar：按我们固定的 dock guid，或按它引用的文件 guid
                s = Regex.Replace(s, "<dock_bar [^>]*guid=\"" + Repo.RuiDockGuid + "\"[^>]*>.*?</dock_bar>", "",
                    RegexOptions.Singleline);
                if (!string.IsNullOrEmpty(fileGuid))
                    s = Regex.Replace(s, "<dock_bar [^>]*source_group_file=\"" + fileGuid + "\"[^>]*>.*?</dock_bar>", "",
                        RegexOptions.Singleline);

                // 清掉指向本 rui 的旧 file_name（按路径匹配，避免 guid 变化留下孤儿条目）
                s = Regex.Replace(s, "<file_name[^>]*>\\s*" + Regex.Escape(ruiPath) + "\\s*</file_name>", "",
                    RegexOptions.IgnoreCase);

                // 重新登记本 rui
                if (s.Contains("<files>")) s = Regex.Replace(s, "(<files>)", m => m.Value + entry);
                else s = Regex.Replace(s, "(<plug_in_files[^>]*>)", m => m.Value + entry);

                if (dockBlock != null && s.Contains("<dock_bars"))
                    s = Regex.Replace(s, "(<dock_bars[^>]*>)", m => m.Value + dockBlock);

                if (s == orig) return false;
                string bak = file + ".ivanbak";
                if (!File.Exists(bak)) { try { File.Copy(file, bak, false); } catch { } }
                File.WriteAllText(file, s, new UTF8Encoding(true));
                return true;
            }
            catch { return false; }
        }

        public static bool ReadRuiGuids(string ruiPath, out string groupGuid, out string barGuid)
        {
            groupGuid = null; barGuid = null;
            try
            {
                string s = File.ReadAllText(ruiPath, Encoding.UTF8);
                Match m1 = Regex.Match(s, "<tool_bar_group guid=\"([^\"]+)\"");
                Match m2 = Regex.Match(s, "<tool_bar guid=\"([^\"]+)\"");
                if (m1.Success) groupGuid = m1.Groups[1].Value;
                if (m2.Success) barGuid = m2.Groups[1].Value;
                return groupGuid != null && barGuid != null;
            }
            catch { return false; }
        }

        public void RemoveToolbar(int major)
        {
            try
            {
                string ui = Path.Combine(UserData(major), "UI", "Plug-ins", Repo.RuiName);
                if (File.Exists(ui)) File.Delete(ui);
                string settings = Path.Combine(UserData(major), "settings");
                if (!Directory.Exists(settings)) return;
                foreach (string f in Directory.GetFiles(settings, "*.xml", SearchOption.AllDirectories))
                {
                    string s = File.ReadAllText(f, Encoding.UTF8);
                    if (s.IndexOf(Repo.RuiName, StringComparison.OrdinalIgnoreCase) < 0 &&
                        s.IndexOf(Repo.RuiDockGuid, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string s2 = Regex.Replace(s, "<dock_bar [^>]*guid=\"" + Repo.RuiDockGuid + "\"[^>]*>.*?</dock_bar>", "",
                        RegexOptions.Singleline);
                    s2 = Regex.Replace(s2, "<file_name[^>]*>[^<]*" + Regex.Escape(Repo.RuiName) + "[^<]*</file_name>", "");
                    if (s2 != s) File.WriteAllText(f, s2, new UTF8Encoding(true));
                }
                Log(string.Format("  √ Rhino {0}：已移除工具条登记", major));
            }
            catch { }
        }
    }
}
