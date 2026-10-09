using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace VoronoiTexture
{
    /// <summary>泰森多边形纹参数（长度单位 mm）</summary>
    public class VoronoiSettings
    {
        public double CellSize = 18.0;   // 胞元尺寸 mm（种子平均间距，越小胞元越多）
        public double Depth = 2.0;       // 凹凸深度 mm：正 = 凸起，负 = 凹陷
        public double EdgeWidth = 2.5;   // 过渡宽度 mm（胞元之间凹槽的宽度）
        public double Shape = 1.0;       // 凸起形状：<1 更圆润，>1 顶面更平、边更陡
        /// <summary>胞元造型：0 = 平顶（默认，老行为：内部是平顶，只到边界 EdgeWidth 内平滑收边）；
        /// 1 = 穹顶（鹅卵石：胞元中间鼓起来、边界自然收到 0，不使用 EdgeWidth、没有平顶/沟槽平底）</summary>
        public int CellShape = 0;
        /// <summary>穹顶圆度（只在 CellShape = 1 时有效）：高度按 t^DomePower 算，t = 到胞元边界的归一化距离
        /// （0 = 在边界上，1 = 在胞元中心）。1 = 线性到边；越小越饱满（鼓面更圆、中心更平），越大越尖</summary>
        public double DomePower = 1.0;
        public int Relax = 2;            // 规整度：Lloyd 松弛次数（0 = 最随机，越大越均匀）
        public int Seed = 1;             // 随机种子
        /// <summary>细胞壁壁厚 mm（0 = 无壁，胞元直接相接）：每个胞元向内缩 壁厚/2，
        /// 相邻两胞元之间留出「壁厚」宽的一条平底壁；中心连线与线框都用内缩后的多边形。</summary>
        public double WallThickness = 0.0;
        public double EdgeFade = 0.0;    // 边界收平宽度 mm（只作用于整个多重曲面的外边界，接缝不受影响）
        public double Step = 0.4;        // 网格采样间距 mm（越小越精细）
        public double Tol = 0.01;
        public double MmToModel = 1.0;   // mm → 文档模型单位

        /// <summary>生成范围：-1 = 整个多重曲面（所有面）；&gt;=0 = 只生成该面（面板的「单面 / 多面」）</summary>
        public int OnlyFace = -1;
        /// <summary>命令里点选到的面序号（面板切「单面」时用它）</summary>
        public int PickedFace = -1;
        /// <summary>参考对象的面数（面板用来判断「参考面」选项要不要禁用）</summary>
        public int FaceCount = 1;
        /// <summary>输出拓扑：false = 整片连续一张面；true = 每个胞元各自一张面（分开放，便于单独选中/上色/偏移）</summary>
        public bool PerCell = false;
        /// <summary>多重曲面的内部接缝要不要平滑过渡（true = 跨面平滑成一张连续面，不保留折痕）</summary>
        public bool SmoothSeam = true;
        /// <summary>输出多重曲面（Brep/NURBS）而不是网格：把网格逐三角面转成曲面（面多、不光滑，但下游能布尔/倒角）</summary>
        public bool NurbOutput = false;

        /// <summary>输出二选一：0 = 只出胞元线框（曲线）；1 = 只出网格面（合并焊接成一张连续网格）</summary>
        public int OutputMode = 1;
        /// <summary>「网格面」模式下：勾了就在每个线框三角面内再细分（片内 n×n）；不勾则每片 1 张三角面（网格拓扑 = 线框本身）</summary>
        public bool Smooth = false;
        /// <summary>平滑度 %（80~100）：片内细分档位的均匀度（100% = 档位按三角面最长边配平 → 网格边长一致；越低大面越粗）</summary>
        public double SmoothAdaptive = 95.0;
        /// <summary>细分程度：片内细分后的目标总面数</summary>
        public int SmoothQuadCount = 5000;
        /// <summary>
        /// 内部开关（不进面板）：是否现在提取胞元边界线框。
        /// 只有「线框」输出模式要它；提取要按格子查最近种子，面模式跳过省一遍时间。
        /// </summary>
        internal bool WantWireframe = true;
        /// <summary>
        /// 内部开关（不进面板）：是否**现在**构建胞元 NURBS 曲面。
        /// 预览（实时重建）必须保持 false —— 建面 + 修剪很贵，每次改参数都算会假死；
        /// 只有点「生成」那一次由会话置 true。
        /// </summary>
        internal bool WantCellSurfaces = false;
        /// <summary>胞元曲面的时间预算（秒）：到点就停并如实报告建了多少个</summary>
        public double CellSurfaceBudgetSec = 12.0;

        /// <summary>胞元渐变：从参考物件向外，胞元逐渐变小（或变大）</summary>
        public bool GradientOn = false;
        /// <summary>渐变参考点（点选的渐变物件换算出来的中心点）</summary>
        public Point3d GradientPoint = Point3d.Unset;
        /// <summary>渐变方向：0 = 靠近物件处胞元更密（胞元更小）；1 = 靠近物件处胞元更稀（胞元更大）。远处都回到基础「胞元尺寸」</summary>
        public int GradientNear = 0;
        /// <summary>渐变幅度（0 = 均匀；越大近处偏离基础尺寸越多）。符号由 GradientNear 决定，这里存幅度</summary>
        public double GradientAmount = 0.6;
        /// <summary>渐变影响半径（mm，0 = 自动取参考点到面最远角的距离）</summary>
        public double GradientRadius = 0.0;

        public VoronoiSettings Clone() { return (VoronoiSettings)MemberwiseClone(); }
    }

    /// <summary>单个面的生成结果</summary>
    public class VoronoiFaceResult
    {
        public int FaceIndex;
        public bool Planar;
        public Mesh Mesh;
        public int CellCount;
        public string Note = "";
        /// <summary>诊断信息：各阶段的真实数字（定位「生成不出网格」这类问题用）</summary>
        public string Diag = "";
        public double MinOffset = double.MaxValue;   // 实测最小高度偏移
        public double MaxOffset = double.MinValue;   // 实测最大高度偏移
        /// <summary>按胞元切分出来的网格（PerCell 模式）：每个元素 = 一个胞元，边界处共用同一批顶点</summary>
        public List<Mesh> CellMeshes = new List<Mesh>();
        /// <summary>边界顶点：参考面点（落在面边界上）与实际 3D 位置。自检量接缝用。</summary>
        public List<Point3d> BoundaryRef = new List<Point3d>();
        public List<Point3d> BoundaryPos = new List<Point3d>();
        /// <summary>接缝上「可以焊接」的顶点位置（输入曲面在该处顺接：共面/相切）—— 合并成整体一张面时按它焊接</summary>
        public List<Point3d> SeamWeldPos = new List<Point3d>();
        /// <summary>「按胞元输出 NURBS」时每个胞元一张曲面（上表面，G0 拼合；边缘贴合原曲面）</summary>
        public List<Brep> CellSurfaces = new List<Brep>();
        /// <summary>胞元曲面统计：成功修剪 / 未修剪 / 跳过</summary>
        public int NurbsTrimmed, NurbsUntrimmed, NurbsSkipped;

        /// <summary>胞元边界线（Voronoi 网格线）：**从胞元多边形取边**得到的真直线段，落在参考平面上</summary>
        public List<Curve> Wireframe = new List<Curve>();
        /// <summary>线框里属于「区域外轮廓」的段数（贴着边界的胞元边 = 边界本身，不是台阶）</summary>
        public int WireOutline;
        /// <summary>胞元边的「考虑次数」（每个胞元的多边形边数之和；共享边会被相邻两个胞元各算一次）</summary>
        public int WireEdgeSeen;
        /// <summary>考虑过的胞元边里「真的出了段」的次数（考虑次数 = 出段 + 去重 + 丢弃）</summary>
        public int WireEmitted;
        /// <summary>共享边去重跳过的次数（同一条边只出一条）</summary>
        public int WireDedup;
        /// <summary>线框端点被裁到区域边界上的次数（部分在区域外 → 在边界处截断，仍是直线）</summary>
        public int WireSnapped;
        /// <summary>线框被丢掉的段数（整段在区域外 / 零长度 / 重合）</summary>
        public int WireDropped;
        /// <summary>胞元中心点（3D，落在参考面上）：高度场以它为准（中心 = 设定深度），自检也用它量。
        /// **一定落在区域内**（= 胞元多边形 ∩ 区域 的采样均值），中心连线与扇形网格的峰顶都用它。</summary>
        public List<Point3d> CellCenters = new List<Point3d>();

        /// <summary>
        /// 扇形网格的外圈点（去重后，落在基准面上）：= 胞元多边形被区域裁过的边段端点 + 区域外轮廓上的补点，
        /// 与线框（胞元边 + 外轮廓）是同一批点。自检用它证明「网格 = 用线框的点建的」。
        /// </summary>
        public List<Point3d> FanOuterPts = new List<Point3d>();
        /// <summary>扇形网格的峰顶（胞元中心，满高）：每个胞元一个（中心看得到整个外圈环时）</summary>
        public List<Point3d> FanApexPts = new List<Point3d>();
        /// <summary>径向层数 R（= 胞元尺寸/采样间距，夹到 2..12）：层点 = 中心 + (k/R)×(外圈点 - 中心)</summary>
        public int FanLayers;
        /// <summary>外圈点被引用的次数（各胞元外圈环点数之和；相邻胞元共用同一批顶点，去重后是 FanOuterPts）</summary>
        public int FanOuterRefs;
        /// <summary>中心看不到的外圈点个数（凹区域：中心到它的射线被区域裁掉一段）。0 = 纯扇形</summary>
        public int FanBlocked;
        /// <summary>外圈环里用区域外轮廓补齐的缺口个数（区域边界切过胞元时，边界那一段要补上，否则网格有洞）</summary>
        public int FanArcs;
        /// <summary>没建出外圈环、被跳过的胞元数（正常为 0；>0 说明网格上会有洞）</summary>
        public int FanSkipped;
        /// <summary>各胞元外圈环的面积之和（投影）：外圈环拼起来就是区域，所以它应该 = RegionArea（差得多 = 环拼错了）</summary>
        public double FanRingArea;
        /// <summary>中心连线（每个胞元的每个（内缩后）边界顶点 → 该胞元中心）。
        /// **单独一条列表**：线框（Wireframe）只放胞元边，方便调用方分别处理/断言。</summary>
        public List<Curve> Spokes = new List<Curve>();
        /// <summary>中心连线条数（= Spokes.Count，诊断/自检用）</summary>
        public int WireSpokes;
        /// <summary>高度场里退回「到胞元边界的距离场」的采样点个数（多面接缝处邻面胞元算不了中心场时用，如实报出来）</summary>
        public int CellFallback;
        /// <summary>
        /// 网格的**投影面积**（把网格顶点按参考平面投到 2D 后求和）。
        /// 判「网格有没有溢出边界」要看这个 —— 3D 表面积被起伏抬高（深度/过渡宽度越大抬得越多），不能拿来比边界面积。
        /// </summary>
        public double ProjectedArea;
        /// <summary>区域多边形的面积（投影面积的对照值）</summary>
        public double RegionArea;

        public int VertexCount { get { return Mesh == null ? 0 : Mesh.Vertices.Count; } }
        public int FaceCount { get { return Mesh == null ? 0 : Mesh.Faces.Count; } }
    }

    /// <summary>xorshift32：自带随机数，保证 Rhino 7/8 同一 seed 结果完全一致</summary>
    internal class Rng
    {
        uint _s;
        public Rng(int seed)
        {
            unchecked { _s = (uint)seed * 2654435761u + 1013904223u; }
            if (_s == 0) _s = 0x9E3779B9;
        }
        public double Next()
        {
            unchecked
            {
                _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
                return (_s & 0xFFFFFF) / (double)0x1000000;
            }
        }
        /// <summary>-1..1</summary>
        public double NextSigned() { return Next() * 2.0 - 1.0; }
    }

    /// <summary>
    /// 2D 区域：环点串 + 网格加速的距离/内外查询。
    /// 内外查询用「洪泛填充 + 边界格精确扫描」：复杂曲面（边界很长）时，
    /// 逐点对全部边界线段做射线求交会退化成 O(节点数×线段数)，实测能把生成拖到几分钟。
    /// </summary>
    internal class Region2D
    {
        public readonly List<Point2d[]> Rings = new List<Point2d[]>();
        readonly List<bool> _closed = new List<bool>();
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;

        readonly List<Point2d> _sa = new List<Point2d>();
        readonly List<Point2d> _sb = new List<Point2d>();
        Dictionary<long, List<int>> _grid;
        double _cell = 1.0;

        // 内外索引（洪泛）
        sbyte[] _ins;              // 0=未定 1=外 3=内 2=边界格（走精确扫描）
        int _ix0, _iy0, _ixn, _iyn;
        double _icell = 1.0;

        public double W { get { return MaxX - MinX; } }
        public double H { get { return MaxY - MinY; } }
        public double Diagonal { get { double w = W, h = H; return Math.Sqrt(w * w + h * h); } }

        public void AddRing(List<Point2d> pts) { AddRing(pts, true); }

        public void AddRing(List<Point2d> pts, bool closed)
        {
            if (pts == null || pts.Count < 2) return;
            var clean = new List<Point2d>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Point2d p = pts[i];
                if (double.IsNaN(p.X) || double.IsNaN(p.Y)) continue;
                if (double.IsInfinity(p.X) || double.IsInfinity(p.Y)) continue;
                if (clean.Count > 0 && clean[clean.Count - 1].DistanceTo(p) < 1e-12) continue;
                clean.Add(p);
            }
            if (closed && clean.Count > 1 && clean[0].DistanceTo(clean[clean.Count - 1]) < 1e-12)
                clean.RemoveAt(clean.Count - 1);
            if (clean.Count < 2) return;
            Rings.Add(clean.ToArray());
            _closed.Add(closed);
            for (int i = 0; i < clean.Count; i++)
            {
                Point2d p = clean[i];
                if (p.X < MinX) MinX = p.X;
                if (p.Y < MinY) MinY = p.Y;
                if (p.X > MaxX) MaxX = p.X;
                if (p.Y > MaxY) MaxY = p.Y;
            }
        }

        /// <summary>所有环的 2D 周长（用来和 3D 实际长度核对比例尺）</summary>
        public double Perimeter2d()
        {
            double sum = 0;
            for (int r = 0; r < Rings.Count; r++)
            {
                Point2d[] ring = Rings[r];
                int n = _closed[r] ? ring.Length : ring.Length - 1;
                for (int i = 0; i < n; i++)
                    sum += ring[i].DistanceTo(ring[(i + 1) % ring.Length]);
            }
            return sum;
        }

        public void BuildGrid(double cell)
        {
            _cell = Math.Max(cell, 1e-6);
            _grid = new Dictionary<long, List<int>>();
            _sa.Clear(); _sb.Clear();
            for (int r = 0; r < Rings.Count; r++)
            {
                Point2d[] ring = Rings[r];
                int n = _closed[r] ? ring.Length : ring.Length - 1;
                for (int i = 0; i < n; i++)
                {
                    Point2d a = ring[i], b = ring[(i + 1) % ring.Length];
                    if (a.DistanceTo(b) < 1e-12) continue;
                    int idx = _sa.Count; _sa.Add(a); _sb.Add(b);
                    int x0 = CellI(Math.Min(a.X, b.X)), x1 = CellI(Math.Max(a.X, b.X));
                    int y0 = CellI(Math.Min(a.Y, b.Y)), y1 = CellI(Math.Max(a.Y, b.Y));
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                        {
                            long key = Key(x, y);
                            List<int> list;
                            if (!_grid.TryGetValue(key, out list)) { list = new List<int>(2); _grid[key] = list; }
                            list.Add(idx);
                        }
                }
            }
        }

        int CellI(double v) { return (int)Math.Floor(v / _cell); }
        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }

        public double Distance(Point2d p, double maxR)
        {
            if (_grid == null) return double.MaxValue;
            int rr = (int)Math.Ceiling(maxR / _cell);
            if (rr < 1) rr = 1;
            if (rr > 96) rr = 96;
            int cx = CellI(p.X), cy = CellI(p.Y);
            double best = double.MaxValue;
            for (int x = cx - rr; x <= cx + rr; x++)
                for (int y = cy - rr; y <= cy + rr; y++)
                {
                    List<int> list;
                    if (!_grid.TryGetValue(Key(x, y), out list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        int s = list[i];
                        double d = DistPointSeg(p, _sa[s], _sb[s]);
                        if (d < best) best = d;
                    }
                }
            return best;
        }

        /// <summary>区域内最近的一个边界点（用于把网格边缘吸附到边界上）</summary>
        public bool ClosestBoundaryPoint(Point2d p, double maxR, out Point2d q)
        {
            q = p;
            if (_grid == null) return false;
            int rr = (int)Math.Ceiling(maxR / _cell);
            if (rr < 1) rr = 1;
            if (rr > 96) rr = 96;
            int cx = CellI(p.X), cy = CellI(p.Y);
            double best = double.MaxValue;
            bool found = false;
            for (int x = cx - rr; x <= cx + rr; x++)
                for (int y = cy - rr; y <= cy + rr; y++)
                {
                    List<int> list;
                    if (!_grid.TryGetValue(Key(x, y), out list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        int s = list[i];
                        Point2d c = ClosestOnSeg(p, _sa[s], _sb[s]);
                        double d = c.DistanceTo(p);
                        if (d < best) { best = d; q = c; found = true; }
                    }
                }
            return found && best <= maxR;
        }

        /// <summary>建立内外索引：含线段的格子标 2（走精确扫描），其余从四周洪泛标记「外」，剩下的就是「内」</summary>
        public void BuildInsideIndex(double cellHint)
        {
            _ins = null;
            if (_sa.Count == 0) return;
            _icell = Math.Max(cellHint, 1e-6);
            _ix0 = (int)Math.Floor(MinX / _icell) - 1;
            _iy0 = (int)Math.Floor(MinY / _icell) - 1;
            _ixn = (int)Math.Ceiling(MaxX / _icell) - _ix0 + 2;
            _iyn = (int)Math.Ceiling(MaxY / _icell) - _iy0 + 2;
            if (_ixn < 3 || _iyn < 3) return;
            long total = (long)_ixn * _iyn;
            if (total > 4000000) return;

            _ins = new sbyte[total];
            // 边界格（含线段，向外扩一格防止洪泛从缝里漏出去）
            for (int s = 0; s < _sa.Count; s++)
            {
                Point2d a = _sa[s], b = _sb[s];
                int x0 = (int)Math.Floor(Math.Min(a.X, b.X) / _icell) - _ix0 - 1;
                int x1 = (int)Math.Floor(Math.Max(a.X, b.X) / _icell) - _ix0 + 1;
                int y0 = (int)Math.Floor(Math.Min(a.Y, b.Y) / _icell) - _iy0 - 1;
                int y1 = (int)Math.Floor(Math.Max(a.Y, b.Y) / _icell) - _iy0 + 1;
                for (int x = Math.Max(0, x0); x <= Math.Min(_ixn - 1, x1); x++)
                    for (int y = Math.Max(0, y0); y <= Math.Min(_iyn - 1, y1); y++)
                        _ins[y * _ixn + x] = 2;
            }

            // 洪泛：从四周往内标「外」
            var stack = new Stack<int>();
            for (int x = 0; x < _ixn; x++)
            {
                TryPush(stack, x, 0);
                TryPush(stack, x, _iyn - 1);
            }
            for (int y = 0; y < _iyn; y++)
            {
                TryPush(stack, 0, y);
                TryPush(stack, _ixn - 1, y);
            }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % _ixn, y = i / _ixn;
                TryPush(stack, x - 1, y); TryPush(stack, x + 1, y);
                TryPush(stack, x, y - 1); TryPush(stack, x, y + 1);
            }

            // 剩下未定的格子 = 内
            for (int i = 0; i < _ins.Length; i++) if (_ins[i] == 0) _ins[i] = 3;
        }

        void TryPush(Stack<int> st, int x, int y)
        {
            if (x < 0 || y < 0 || x >= _ixn || y >= _iyn) return;
            int i = y * _ixn + x;
            if (_ins[i] != 0) return;
            _ins[i] = 1;
            st.Push(i);
        }

        public bool Inside(Point2d p)
        {
            if (_ins != null)
            {
                int x = (int)Math.Floor(p.X / _icell) - _ix0;
                int y = (int)Math.Floor(p.Y / _icell) - _iy0;
                if (x >= 0 && y >= 0 && x < _ixn && y < _iyn)
                {
                    sbyte v = _ins[y * _ixn + x];
                    if (v == 1) return false;
                    if (v == 3) return true;
                }
            }
            return InsideExact(p);
        }

        public bool InsideExact(Point2d p)
        {
            if (_sa.Count == 0) return false;
            int cnt = 0;
            for (int i = 0; i < _sa.Count; i++)
            {
                Point2d a = _sa[i], b = _sb[i];
                if ((a.Y > p.Y) == (b.Y > p.Y)) continue;
                double x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (x > p.X) cnt++;
            }
            return (cnt & 1) == 1;
        }

        static Point2d ClosestOnSeg(Point2d p, Point2d a, Point2d b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            double t = 0;
            if (len2 > 1e-20) { t = (wx * vx + wy * vy) / len2; if (t < 0) t = 0; else if (t > 1) t = 1; }
            return new Point2d(a.X + t * vx, a.Y + t * vy);
        }

        static double DistPointSeg(Point2d p, Point2d a, Point2d b)
        {
            return ClosestOnSeg(p, a, b).DistanceTo(p);
        }
    }

    /// <summary>
    /// 二维工作空间：把面上的点映射到一块「近似等距」的平面坐标。
    /// 平面面直接用面平面投影；曲面用 UV 参数域 + 比例尺（不用切平面投影：
    /// 曲面会弯离切平面，边界在切平面里向内收缩）。
    /// </summary>
    internal class SurfaceMap
    {
        public bool Planar;
        public Plane Frame;
        public BrepFace Face;
        public double U0, V0;
        public double SU = 1, SV = 1;

        static double Dot3(Vector3d a, Vector3d b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        public static SurfaceMap Create(BrepFace face)
        {
            if (face == null) return null;
            var m = new SurfaceMap();
            m.Face = face;

            double u0, v0;
            if (!GetFaceCenterUV(face, out u0, out v0)) return null;
            m.U0 = u0; m.V0 = v0;

            Plane pl = Plane.Unset;
            bool planar = false;
            try { planar = face.TryGetPlane(out pl, 1e-3) && pl.IsValid; } catch { planar = false; }

            if (planar)
            {
                Point3d org = face.PointAt(u0, v0);
                Vector3d nz = face.NormalAt(u0, v0);
                if (nz.IsValid && nz.Unitize())
                {
                    Vector3d x = WorldReference(nz);
                    Vector3d y = Vector3d.CrossProduct(nz, x);
                    if (y.Unitize())
                    {
                        var fr = new Plane(org, x, y);
                        if (fr.IsValid) { m.Frame = fr; m.Planar = true; }
                    }
                }
            }

            if (!m.Planar)
            {
                ComputeScales(face, out m.SU, out m.SV);
                Point3d org = face.PointAt(u0, v0);
                Vector3d nz = face.NormalAt(u0, v0);
                if (nz.IsValid && nz.Unitize())
                {
                    Vector3d x = WorldReference(nz);
                    Vector3d y = Vector3d.CrossProduct(nz, x);
                    if (y.Unitize()) m.Frame = new Plane(org, x, y);
                }
            }
            return m;
        }

        /// <summary>
        /// 每轴比例尺（模型单位 / uv）。只在**修剪区域实际用到的 uv 范围**里采样：
        /// 取整个定义域的平均，在「参数化极不均匀」或「大底面只用了小块」的曲面上
        /// 会严重偏离局部比例尺，导致 2D 区域尺寸算错、纹理退化。
        /// </summary>
        static void ComputeScales(BrepFace face, out double su, out double sv)
        {
            su = 1; sv = 1;
            try
            {
                Interval ru, rv;
                if (!TrimUvRange(face, out ru, out rv))
                {
                    ru = face.Domain(0); rv = face.Domain(1);
                }
                double hu = Math.Max((ru.Max - ru.Min) * 1e-3, 1e-9);
                double hv = Math.Max((rv.Max - rv.Min) * 1e-3, 1e-9);
                double accU = 0, accV = 0; int n = 0;
                for (int i = 0; i <= 6; i++)
                    for (int j = 0; j <= 6; j++)
                    {
                        double u = ru.ParameterAt(i / 6.0), v = rv.ParameterAt(j / 6.0);
                        Point3d P = face.PointAt(u, v);
                        Point3d Pu = face.PointAt(u + hu, v);
                        Point3d Pv = face.PointAt(u, v + hv);
                        if (!P.IsValid || !Pu.IsValid || !Pv.IsValid) continue;
                        double du = Pu.DistanceTo(P) / hu;
                        double dv = Pv.DistanceTo(P) / hv;
                        if (double.IsNaN(du) || double.IsNaN(dv) || double.IsInfinity(du) || double.IsInfinity(dv)) continue;
                        if (!(du > 1e-9) || !(dv > 1e-9)) continue;   // 极点/退化处跳过
                        accU += du; accV += dv; n++;
                    }
                if (n > 0) { su = accU / n; sv = accV / n; }
            }
            catch { }
            if (!(su > 1e-9) || double.IsNaN(su) || double.IsInfinity(su)) su = 1;
            if (!(sv > 1e-9) || double.IsNaN(sv) || double.IsInfinity(sv)) sv = 1;
        }

        /// <summary>修剪区域在 uv 空间的范围（所有环的 2D 曲线包围盒）</summary>
        static bool TrimUvRange(BrepFace face, out Interval ru, out Interval rv)
        {
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            bool any = false;
            try
            {
                foreach (BrepLoop loop in face.Loops)
                {
                    Curve c2 = loop.To2dCurve();
                    if (c2 == null) continue;
                    BoundingBox bb = c2.GetBoundingBox(false);
                    if (!bb.IsValid) continue;
                    if (bb.Min.X < u0) u0 = bb.Min.X;
                    if (bb.Max.X > u1) u1 = bb.Max.X;
                    if (bb.Min.Y < v0) v0 = bb.Min.Y;
                    if (bb.Max.Y > v1) v1 = bb.Max.Y;
                    any = true;
                }
            }
            catch { any = false; }
            if (!any || !(u1 > u0) || !(v1 > v0)) { ru = Interval.Unset; rv = Interval.Unset; return false; }
            ru = new Interval(u0, u1);
            rv = new Interval(v0, v1);
            return true;
        }

        static bool GetFaceCenterUV(BrepFace face, out double u, out double v)
        {
            u = 0; v = 0;
            Point3d c = Point3d.Unset;
            try
            {
                var acc = new Point3d(0, 0, 0);
                double sum = 0;
                foreach (BrepLoop loop in face.Loops)
                {
                    Point3d[] pts = Voronoi.SamplePoints(loop.To3dCurve(), 32);
                    if (pts == null) continue;
                    for (int i = 0; i < pts.Length; i++) { acc += pts[i]; sum += 1; }
                }
                if (sum > 0) c = new Point3d(acc.X / sum, acc.Y / sum, acc.Z / sum);
            }
            catch { c = Point3d.Unset; }

            if (!c.IsValid)
            {
                try { c = face.PointAt(face.Domain(0).Mid, face.Domain(1).Mid); }
                catch { c = Point3d.Unset; }
            }
            if (c.IsValid && face.ClosestPoint(c, out u, out v)) return true;
            // 兜底：直接用定义域中点当参考 uv（比整面放弃好）
            try
            {
                u = face.Domain(0).Mid; v = face.Domain(1).Mid;
                return face.PointAt(u, v).IsValid;
            }
            catch { return false; }
        }

        static Vector3d WorldReference(Vector3d z)
        {
            var gx = new Vector3d(1, 0, 0);
            var p = gx - z * Dot3(gx, z);
            if (p.Length < 1e-6) { var gz = new Vector3d(0, 0, 1); p = gz - z * Dot3(gz, z); }
            if (p.Length < 1e-6) { var gy = new Vector3d(0, 1, 0); p = gy - z * Dot3(gy, z); }
            if (!p.Unitize()) p = new Vector3d(1, 0, 0);
            return p;
        }

        public Point2d To2d(Point3d p)
        {
            if (Planar)
            {
                Vector3d v = p - Frame.Origin;
                return new Point2d(Dot3(v, Frame.XAxis), Dot3(v, Frame.YAxis));
            }
            double u, v2;
            if (!Face.ClosestPoint(p, out u, out v2)) return Point2d.Unset;
            return new Point2d((u - U0) * SU, (v2 - V0) * SV);
        }

        public Point3d To3d(double x, double y)
        {
            if (Planar) return Frame.PointAt(x, y);
            return Face.PointAt(U0 + x / SU, V0 + y / SV);
        }

        /// <summary>该处的曲面法向（用于沿法向偏移出纹理高度）</summary>
        public Vector3d NormalAt(double x, double y)
        {
            if (Planar) return Frame.ZAxis;
            Vector3d n = Face.NormalAt(U0 + x / SU, V0 + y / SV);
            if (!n.IsValid || !n.Unitize()) return Frame.ZAxis;
            return n;
        }

        /// <summary>纹理点 = 曲面上该点 + 法向 × 高度</summary>
        public Point3d OffsetPoint(double x, double y, double h)
        {
            Point3d P = To3d(x, y);
            if (Math.Abs(h) < 1e-12) return P;
            return P + NormalAt(x, y) * h;
        }
    }

    /// <summary>
    /// 全局种子点云（3D）。整个多重曲面共用一套种子，距离按 3D 空间算 ——
    /// 这样相邻面在共享边两侧算出的高度场是同一个连续函数，胞元自然跨面连成一片。
    /// </summary>
    internal class SeedCloud3
    {
        readonly List<Point3d> _pts = new List<Point3d>();
        readonly List<int> _tag = new List<int>();
        Dictionary<long, List<int>> _b = new Dictionary<long, List<int>>();
        double _cell = 1.0;
        double _minX, _minY, _minZ, _maxX, _maxY, _maxZ;
        bool _sealed;

        public int Count { get { return _pts.Count; } }

        public void Add(Point3d p, int tag)
        {
            if (!p.IsValid) return;
            if (_pts.Count == 0)
            {
                _minX = _maxX = p.X; _minY = _maxY = p.Y; _minZ = _maxZ = p.Z;
            }
            else
            {
                if (p.X < _minX) _minX = p.X; if (p.X > _maxX) _maxX = p.X;
                if (p.Y < _minY) _minY = p.Y; if (p.Y > _maxY) _maxY = p.Y;
                if (p.Z < _minZ) _minZ = p.Z; if (p.Z > _maxZ) _maxZ = p.Z;
            }
            int idx = _pts.Count;
            _pts.Add(p); _tag.Add(tag);
            List<int> l;
            if (!_b.TryGetValue(Key(CellI(p.X), CellI(p.Y), CellI(p.Z)), out l))
            { l = new List<int>(2); _b[Key(CellI(p.X), CellI(p.Y), CellI(p.Z))] = l; }
            l.Add(idx);
        }

        public void SetCell(double cell)
        {
            double c = Math.Max(cell, 1e-6);
            if (Math.Abs(c - _cell) < 1e-12 || _pts.Count == 0) { _cell = c; return; }
            // 换格子大小：重建哈希
            _cell = c;
            _b = new Dictionary<long, List<int>>();
            for (int i = 0; i < _pts.Count; i++)
            {
                Point3d p = _pts[i];
                long k = Key(CellI(p.X), CellI(p.Y), CellI(p.Z));
                List<int> l;
                if (!_b.TryGetValue(k, out l)) { l = new List<int>(2); _b[k] = l; }
                l.Add(i);
            }
        }

        public void Seal() { _sealed = true; }

        /// <summary>某个面名下保留了多少个种子（≈ 该面的胞元数）</summary>
        public int CountByTag(int tag)
        {
            int n = 0;
            for (int i = 0; i < _tag.Count; i++) if (_tag[i] == tag) n++;
            return n;
        }

        static long Key(int x, int y, int z)
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }
        int CellI(double v) { return (int)Math.Floor(v / _cell); }

        /// <summary>最近距离 d1 与次近距离 d2；两者之差的一半就是到胞元边界的距离</summary>
        public void Nearest2(Point3d p, out double d1, out double d2)
        {
            int idx;
            Nearest2(p, out d1, out d2, out idx);
        }

        /// <summary>最近距离 d1 / 次近距离 d2 / 最近种子序号（-1 = 没找到）</summary>
        public void Nearest2(Point3d p, out double d1, out double d2, out int index)
        {
            d1 = double.MaxValue; d2 = double.MaxValue;
            index = -1;
            if (_pts.Count == 0) return;
            int cx = CellI(p.X), cy = CellI(p.Y), cz = CellI(p.Z);
            double ext = Math.Max(Math.Max(_maxX - _minX, _maxY - _minY), _maxZ - _minZ);
            int maxR = (int)Math.Ceiling(ext / _cell) + 2;
            if (maxR > 64) maxR = 64;
            for (int r = 0; r <= maxR; r++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                    for (int y = cy - r; y <= cy + r; y++)
                        for (int z = cz - r; z <= cz + r; z++)
                        {
                            if (r > 0 && Math.Abs(x - cx) != r && Math.Abs(y - cy) != r && Math.Abs(z - cz) != r) continue;
                            List<int> l;
                            if (!_b.TryGetValue(Key(x, y, z), out l)) continue;
                            for (int i = 0; i < l.Count; i++)
                            {
                                double d = _pts[l[i]].DistanceTo(p);
                                if (d < d1) { d2 = d1; d1 = d; index = l[i]; }
                                else if (d < d2) d2 = d;
                            }
                        }
                if (r > 0 && d2 < double.MaxValue && d2 <= r * _cell) break;
            }
        }

        /// <summary>最近的种子序号（找不到返回 -1）—— 按胞元切分网格时用</summary>
        public int NearestIndex(Point3d p)
        {
            int best = -1; double bd = double.MaxValue;
            if (_pts.Count == 0) return -1;
            int cx = CellI(p.X), cy = CellI(p.Y), cz = CellI(p.Z);
            double ext = Math.Max(Math.Max(_maxX - _minX, _maxY - _minY), _maxZ - _minZ);
            int maxR = (int)Math.Ceiling(ext / _cell) + 2;
            if (maxR > 64) maxR = 64;
            for (int r = 0; r <= maxR; r++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                    for (int y = cy - r; y <= cy + r; y++)
                        for (int z = cz - r; z <= cz + r; z++)
                        {
                            if (r > 0 && Math.Abs(x - cx) != r && Math.Abs(y - cy) != r && Math.Abs(z - cz) != r) continue;
                            List<int> l;
                            if (!_b.TryGetValue(Key(x, y, z), out l)) continue;
                            for (int i = 0; i < l.Count; i++)
                            {
                                double d = _pts[l[i]].DistanceTo(p);
                                if (d < bd) { bd = d; best = l[i]; }
                            }
                        }
                if (best >= 0 && bd <= r * _cell) break;
            }
            return best;
        }

        /// <summary>半径 r 内是否有「别的面」的种子（跨面去重时用）</summary>
        public bool AnyOtherFaceWithin(Point3d p, double r, int myFace)
        {
            if (_pts.Count == 0) return false;
            int rr = (int)Math.Ceiling(r / _cell);
            if (rr < 1) rr = 1;
            int cx = CellI(p.X), cy = CellI(p.Y), cz = CellI(p.Z);
            double r2 = r * r;
            for (int x = cx - rr; x <= cx + rr; x++)
                for (int y = cy - rr; y <= cy + rr; y++)
                    for (int z = cz - rr; z <= cz + rr; z++)
                    {
                        List<int> l;
                        if (!_b.TryGetValue(Key(x, y, z), out l)) continue;
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (_tag[l[i]] == myFace) continue;
                            Point3d q = _pts[l[i]];
                            double dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
                            if (dx * dx + dy * dy + dz * dz <= r2) return true;
                        }
                    }
            return false;
        }
    }

    /// <summary>
    /// 一个胞元的几何（线框取边、高度场定基准都要用）：
    /// Poly = 半平面裁剪出来的 Voronoi 胞元多边形（凸，未按区域裁剪）；Center = 面积质心；Inradius = 质心到边的最短距离。
    /// </summary>
    internal class CellGeom
    {
        public List<Point2d> Poly = new List<Point2d>();
        public Point2d Center;
        public double Inradius = 1e-9;
        /// <summary>按「细胞壁壁厚」向内缩之后的多边形（壁厚 = 0 时就是 Poly）；线框与中心连线都用它</summary>
        public List<Point2d> InsetPoly;
        /// <summary>内缩多边形的内切半径（高度场用它归一化：壁宽范围内高度恒 0）</summary>
        public double InsetInradius = 1e-9;
    }

    /// <summary>
    /// 共享边在本面 2D 空间里的折线 + 对应的 3D 采样点 + 邻面法向（接缝斜接用）。
    /// Pts 与 Pts3 一一对应；3D 采样点直接来自边曲线，**相邻两个面拿到的是同一批点**
    /// （同一条边、同样的段数）—— 接缝吸附到这批公共点，两边的顶点位置才能严丝合缝。
    /// </summary>
    internal class SharedEdge
    {
        public List<Point2d> Pts = new List<Point2d>();
        public List<Point3d> Pts3 = new List<Point3d>();
        public Vector3d NbrNormal = Vector3d.Zero;
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;

        public void Add(Point2d q, Point3d q3)
        {
            Pts.Add(q);
            Pts3.Add(q3);
            if (q.X < MinX) MinX = q.X;
            if (q.Y < MinY) MinY = q.Y;
            if (q.X > MaxX) MaxX = q.X;
            if (q.Y > MaxY) MaxY = q.Y;
        }

        /// <summary>点到这条折线的最短距离（线段距离，不是顶点距离 —— 采样点之间要能命中）</summary>
        public double DistanceTo(Point2d p, double tol)
        {
            if (Pts.Count == 0) return double.MaxValue;
            if (p.X < MinX - tol || p.X > MaxX + tol || p.Y < MinY - tol || p.Y > MaxY + tol) return double.MaxValue;
            if (Pts.Count == 1) return Pts[0].DistanceTo(p);
            double best = double.MaxValue;
            for (int i = 0; i + 1 < Pts.Count; i++)
            {
                double d = DistSeg(p, Pts[i], Pts[i + 1]);
                if (d < best) best = d;
            }
            return best;
        }

        static double DistSeg(Point2d p, Point2d a, Point2d b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            double t = 0;
            if (len2 > 1e-20) { t = (wx * vx + wy * vy) / len2; if (t < 0) t = 0; else if (t > 1) t = 1; }
            double dx = wx - t * vx, dy = wy - t * vy;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// 本面所有共享边采样点的空间索引（2D 查询 → 3D 公共点 + 这条边是不是「顺接」）。
    /// 网格点吸附到共享边时走这里：相邻面拿到的是同一批 3D 点，
    /// 于是接缝两侧的顶点位置完全重合（不是两条各算各的折线）。
    /// </summary>
    internal class SeamIndex
    {
        /// <summary>输入面之间的夹角小于它就算「同一个面延续过去」（cos 35°）—— 决定接缝要不要焊接平滑</summary>
        internal const double SmoothCos = 0.8191520442889918;

        readonly List<Point2d> _p2 = new List<Point2d>();
        readonly List<Point3d> _p3 = new List<Point3d>();
        readonly List<bool> _smooth = new List<bool>();
        Dictionary<long, List<int>> _g = new Dictionary<long, List<int>>();
        double _cell = 1.0;

        public int Count { get { return _p2.Count; } }

        public void Build(List<SharedEdge> shared, double cell, Vector3d nThis)
        {
            _cell = Math.Max(cell, 1e-9);
            for (int i = 0; i < shared.Count; i++)
            {
                SharedEdge se = shared[i];
                bool smooth = true;
                if (se.NbrNormal != Vector3d.Zero && nThis != Vector3d.Zero)
                {
                    double d = nThis.X * se.NbrNormal.X + nThis.Y * se.NbrNormal.Y + nThis.Z * se.NbrNormal.Z;
                    smooth = Math.Abs(d) >= SmoothCos;      // 两面近乎共面/相切 -> 顺接
                }
                for (int k = 0; k < se.Pts.Count && k < se.Pts3.Count; k++)
                {
                    Point2d q = se.Pts[k];
                    if (double.IsNaN(q.X) || double.IsNaN(q.Y)) continue;
                    int idx = _p2.Count;
                    _p2.Add(q);
                    _p3.Add(se.Pts3[k]);
                    _smooth.Add(smooth);
                    long key = Key(CellI(q.X), CellI(q.Y));
                    List<int> l;
                    if (!_g.TryGetValue(key, out l)) { l = new List<int>(2); _g[key] = l; }
                    l.Add(idx);
                }
            }
        }

        int CellI(double v) { return (int)Math.Floor(v / _cell); }
        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }

        /// <summary>离 p 最近的公共采样点（超出 maxR 返回 false）</summary>
        public bool Nearest(Point2d p, double maxR, out Point2d q2, out Point3d q3, out bool smooth)
        {
            q2 = p; q3 = Point3d.Unset; smooth = false;
            if (_p2.Count == 0) return false;
            int cx = CellI(p.X), cy = CellI(p.Y);
            double best = maxR;
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                {
                    List<int> l;
                    if (!_g.TryGetValue(Key(x, y), out l)) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        double d = _p2[l[i]].DistanceTo(p);
                        if (d < best) { best = d; q2 = _p2[l[i]]; q3 = _p3[l[i]]; smooth = _smooth[l[i]]; }
                    }
                }
            return q3.IsValid;
        }
    }

    /// <summary>
    /// 胞元渐变（只控**疏密**，不控深度）：局部胞元尺寸按「到参考点的距离」缩放。
    /// 近处偏离基础尺寸、远处回到基础尺寸：k = 1 + Amount*(1 - sm)，sm 是从近到远的平滑权重。
    /// 密集（GradientNear = 0）→ Amount &lt; 0 → 近处 k &lt; 1（胞元更小、种子更密）；
    /// 稀疏（GradientNear = 1）→ Amount &gt; 0 → 近处 k &gt; 1（胞元更大、种子更稀）。
    /// </summary>
    internal class Gradient
    {
        public bool On;
        public Point3d Center = Point3d.Unset;
        public double Radius = 1.0;
        public double Amount;

        public static Gradient Create(VoronoiSettings s, SurfaceMap map, Region2D region, double u2m)
        {
            var g = new Gradient();
            if (!s.GradientOn || !s.GradientPoint.IsValid || region == null) return g;
            // 幅度只取大小，方向由「密集 / 稀疏」定：密集 = 近处更小（负），稀疏 = 近处更大（正）
            double amp = Math.Abs(s.GradientAmount);
            if (amp > 0.95) amp = 0.95;
            // 幅度 0 = 均匀：**完全短路**（On 保持 false → 走与无渐变一致的种子生成路径、ScaleAt 恒为 1）
            if (amp < 1e-9) return g;
            g.On = true;
            g.Center = s.GradientPoint;
            g.Amount = (s.GradientNear == 1) ? amp : -amp;

            double r = Math.Abs(s.GradientRadius) * u2m;
            if (!(r > 1e-9))
            {
                // 自动半径：参考点到面四角的最远距离（按 2D 包围盒四角换算回 3D）
                double best = 0;
                Point2d[] corners =
                {
                    new Point2d(region.MinX, region.MinY), new Point2d(region.MaxX, region.MinY),
                    new Point2d(region.MaxX, region.MaxY), new Point2d(region.MinX, region.MaxY)
                };
                for (int i = 0; i < corners.Length; i++)
                {
                    Point3d q = map.OffsetPoint(corners[i].X, corners[i].Y, 0.0);
                    if (!q.IsValid) continue;
                    double d = q.DistanceTo(g.Center);
                    if (d > best) best = d;
                }
                r = best > 1e-9 ? best : 1.0;
            }
            g.Radius = r;
            return g;
        }

        /// <summary>该点处的**局部胞元尺寸**比例（1 = 基础「胞元尺寸」；&lt;1 = 更密，&gt;1 = 更稀）</summary>
        public double ScaleAt(Point3d p)
        {
            if (!On) return 1.0;
            double t = p.DistanceTo(Center) / Radius;
            if (t > 1) t = 1;
            if (t < 0) t = 0;
            // 影响只覆盖半径的前 60%：再往外 sm = 1 → k = 1（远处**严格**回基础「胞元尺寸」）
            double u = t * (1.0 / 0.6);
            if (u > 1) u = 1;
            double sm = u * u * (3 - 2 * u);          // 平滑过渡，避免同心环状的硬边
            double k = 1.0 + Amount * (1.0 - sm);
            if (k < 0.2) k = 0.2;
            if (k > 5.0) k = 5.0;
            return k;
        }

        /// <summary>该点处的间距比例（2D 坐标先映射回 3D）</summary>
        public double ScaleAt2d(SurfaceMap map, Point2d q)
        {
            if (!On) return 1.0;
            Point3d p = map.OffsetPoint(q.X, q.Y, 0.0);
            return p.IsValid ? ScaleAt(p) : 1.0;
        }
    }

    /// <summary>
    /// 解析出来的平面边界（用户口径：只支持平面）。
    /// 接受闭合平面曲线 / 平面曲面 / 平面 Brep（取面积最大的平面面外轮廓）；弯曲面、开口曲线、网格、细分物件一律拒绝。
    /// </summary>
    internal class PlanarBoundary
    {
        public Plane Plane;
        public Curve Outline;                // 平面闭合曲线（世界坐标）
        public double Area;
        public int InnerLoops;
        public string Desc = "";
        public Guid SourceId = Guid.Empty;

        /// <summary>把一个物件解析成平面边界；失败时 why 写明原因（面板据此把拾取按钮置红）</summary>
        public static bool TryResolve(RhinoObject obj, out PlanarBoundary b, out string why)
        {
            b = null; why = "";
            if (obj == null) { why = "对象不存在"; return false; }

            double tol = 1e-6;
            try { if (RhinoDoc.ActiveDoc != null) tol = Math.Max(1e-9, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance); }
            catch { }

            Curve outline = null;
            Plane plane = Plane.Unset;
            int inner = 0;

            var curve = obj.Geometry as Curve;
            if (curve != null)
            {
                if (!curve.IsClosed) { why = "曲线不闭合"; return false; }
                if (!curve.IsPlanar(tol * 10.0)) { why = "曲线不在同一平面上"; return false; }
                if (!curve.TryGetPlane(out plane)) { why = "曲线无法拟合出平面"; return false; }
                outline = curve.DuplicateCurve();
            }
            else
            {
                var brep = obj.Geometry as Brep;
                if (brep == null)
                {
                    // 挤出物件（Extrusion）/ 曲面（Surface）不是 Brep 类型，但能转成 Brep —— 用 TryConvertBrep 收进来
                    // （踩过：挤出物件被 `as Brep` 判成「不是曲线或曲面」，用户选平面挤出体时直接失败）
                    try { brep = Brep.TryConvertBrep(obj.Geometry); } catch { brep = null; }
                }
                if (brep == null)
                {
                    string kind = obj.Geometry == null ? "空几何" : obj.Geometry.GetType().Name;
                    why = "不是曲线或曲面（" + kind + " 不支持：网格/细分物件请先转成 NURBS 曲面，或直接选它的平面边界曲线）";
                    return false;
                }
                if (brep.Faces.Count == 0) { why = "曲面没有面"; return false; }

                BrepFace best = null;
                double bestArea = -1.0;
                for (int i = 0; i < brep.Faces.Count; i++)
                {
                    BrepFace f = brep.Faces[i];
                    if (!f.IsPlanar(tol * 10.0)) continue;
                    double a = FaceArea(f);
                    if (a > bestArea) { bestArea = a; best = f; }
                }
                if (best == null) { why = "没有平面面（弯曲面不支持）"; return false; }
                if (!best.TryGetPlane(out plane)) { why = "平面面无法取出平面"; return false; }
                outline = best.OuterLoop != null ? best.OuterLoop.To3dCurve() : null;
                if (outline == null) { why = "取不到外轮廓"; return false; }
                if (!outline.IsClosed) { why = "外轮廓不闭合"; return false; }
                inner = Math.Max(0, best.Loops.Count - 1);
            }
            if (outline == null) { why = "取不到边界曲线"; return false; }

            double dev = MaxPlaneDeviation(outline, plane);
            double diag = CurveDiag(outline, plane);
            if (dev > Math.Max(1e-7, diag * 1e-4))
            {
                why = string.Format(CultureInfo.InvariantCulture, "轮廓偏离平面 {0:0.###e+0}（不是平面边界）", dev);
                return false;
            }

            double area = CurveArea(outline);
            if (!(area > 0)) { why = "边界面积为零"; return false; }

            b = new PlanarBoundary
            {
                Plane = plane,
                Outline = outline,
                Area = area,
                InnerLoops = inner,
                SourceId = obj.Id,
                Desc = Describe(obj)
            };
            return true;
        }

        /// <summary>把边界曲线变成单张平面 Brep —— 现有几何核心 Voronoi.Generate 直接吃它（几何核心不重写）</summary>
        public Brep ToBrep(double tol)
        {
            if (Outline == null) return null;
            try
            {
                Brep[] bps = Brep.CreatePlanarBreps(new Curve[] { Outline }, Math.Max(1e-6, tol));
                if (bps == null || bps.Length == 0) return null;
                Brep best = null;
                double bestArea = -1.0;
                for (int i = 0; i < bps.Length; i++)
                {
                    if (bps[i] == null || bps[i].Faces.Count == 0) continue;
                    double a = 0;
                    try { a = bps[i].GetArea(); } catch { }
                    if (a > bestArea) { bestArea = a; best = bps[i]; }
                }
                return best;
            }
            catch { return null; }
        }

        static double FaceArea(BrepFace f)
        {
            try
            {
                Brep one = f.DuplicateFace(false);
                if (one == null) return 0.0;
                AreaMassProperties amp = AreaMassProperties.Compute(one);
                return amp != null ? amp.Area : 0.0;
            }
            catch { return 0.0; }
        }

        internal static double CurveArea(Curve c)
        {
            try
            {
                AreaMassProperties amp = AreaMassProperties.Compute(c);
                return amp != null ? Math.Abs(amp.Area) : 0.0;
            }
            catch { return 0.0; }
        }

        static double MaxPlaneDeviation(Curve outline, Plane plane)
        {
            double max = 0;
            try
            {
                Point3d[] pts;
                if (outline.DivideByCount(32, true, out pts) == null || pts == null) return 0;
                for (int i = 0; i < pts.Length; i++)
                {
                    double d = Math.Abs(plane.DistanceTo(pts[i]));
                    if (d > max) max = d;
                }
            }
            catch { }
            return max;
        }

        static double CurveDiag(Curve outline, Plane plane)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            try
            {
                Point3d[] pts;
                if (outline.DivideByCount(32, true, out pts) == null || pts == null) return 1.0;
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector3d d = pts[i] - plane.Origin;
                    double x = Vector3d.Multiply(d, plane.XAxis), y = Vector3d.Multiply(d, plane.YAxis);
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            }
            catch { return 1.0; }
            double dx = x1 - x0, dy = y1 - y0;
            double d2 = Math.Sqrt(dx * dx + dy * dy);
            return d2 > 0 ? d2 : 1.0;
        }

        internal static string Describe(RhinoObject obj)
        {
            if (obj == null) return "未选择";
            string name = obj.Name;
            string id = obj.Id.ToString();
            if (id.Length > 8) id = id.Substring(0, 8);
            string kind = obj.Geometry is Curve ? "曲线" : "曲面";
            return string.IsNullOrEmpty(name) ? (kind + " " + id) : (kind + "：" + name);
        }
    }

    /// <summary>一键平滑的结果（QuadRemesh 出来的四边面网格 + 细分物件）</summary>
    public class VoronoiSmoothResult
    {
        public bool Ok;
        public Mesh Mesh;
        public SubD SubD;
        public int Faces;
        public string Note = "";
    }

    public static class Voronoi
    {
        internal static double Dot(Vector3d a, Vector3d b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        internal static Point3d[] SamplePoints(Curve c, int n)
        {
            if (c == null) return null;
            double[] ts = c.DivideByCount(n, true);
            if (ts == null || ts.Length == 0) return null;
            var pts = new Point3d[ts.Length];
            for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
            return pts;
        }

        static double Smoothstep(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * (3 - 2 * t);
        }

        /// <summary>每个面的中间产物</summary>
        internal class FaceCtx
        {
            public SurfaceMap Map;
            public Region2D Region;
            public Region2D Free;                 // 只含自由边（收平用）
            public List<SharedEdge> Shared = new List<SharedEdge>();
            public string Note = "";
            public string Diag = "";
            public bool Ok { get { return Map != null && Region != null && Region.Rings.Count > 0; } }
        }

        public static List<VoronoiFaceResult> Generate(Brep brep, VoronoiSettings settings, out string report)
        {
            var results = new List<VoronoiFaceResult>();
            var sb = new StringBuilder();
            if (brep == null) { report = "无有效几何体"; return results; }

            VoronoiSettings s = settings.Clone();
            s.CellSize = Math.Max(s.CellSize, 0.5);
            s.EdgeWidth = Math.Max(s.EdgeWidth, 1e-3);
            s.Shape = Math.Max(s.Shape, 0.05);
            if (s.CellShape != 1) s.CellShape = 0;             // 只认 0（平顶）/ 1（穹顶），其余按平顶
            if (!(s.DomePower > 0.05)) s.DomePower = 0.05;     // 幂次 <= 0 / NaN 会把高度场算成 NaN，兜底
            if (s.DomePower > 20.0) s.DomePower = 20.0;
            s.Step = Math.Max(s.Step, 0.02);
            if (!(s.MmToModel > 0)) s.MmToModel = 1.0;
            s.Tol = Math.Max(s.Tol, 1e-5);
            if (s.Relax < 0) s.Relax = 0;
            if (s.Relax > 30) s.Relax = 30;

            double u2m = s.MmToModel;
            double cell = s.CellSize * u2m;
            double depth = s.Depth * u2m;
            double edgeW = s.EdgeWidth * u2m;
            double step = s.Step * u2m;

            // 「输出多重曲面(NURBS)」模式：只要 G0（边界重合），不追求接缝平滑 ——
            // 于是可以①不做接缝法向混合 ②把采样间距放宽（浮雕特征尺度是沟槽宽度/胞元尺寸），
            // 网格面数降一个数量级 → 生成快得多，转出来的多重曲面也可控、还能合并共面。
            string nurbNote = "";
            if (s.NurbOutput)
            {
                s.SmoothSeam = false;                        // G0 即可（公共采样点已保证边界逐位重合）
                double want = Math.Max(step, Math.Max(edgeW * 0.25, cell * 0.12));
                if (want > step * 1.001)
                {
                    nurbNote = string.Format("NURBS 输出模式：采样间距 {0:0.###} → {1:0.###} mm（只要 G0、边界重合，放宽采样换速度）",
                        step / Math.Max(u2m, 1e-9), want / Math.Max(u2m, 1e-9));
                    step = want;
                }
            }
            double fade = Math.Max(s.EdgeFade * u2m, 0.0);
            double wall = Math.Max(s.WallThickness * u2m, 0.0);   // 细胞壁壁厚（模型单位）
            double tol = Math.Max(s.Tol * u2m, 1e-4);

            int nf = brep.Faces.Count;
            // 单面 / 多面：单面时只处理点选的那一个面，并且不参与接缝斜接、边界按自由边处理
            int only = (s.OnlyFace >= 0 && s.OnlyFace < nf) ? s.OnlyFace : -1;
            bool singleFace = only >= 0 && nf > 1;
            var ctx = new FaceCtx[nf];

            // ---------------- 1) 每个面：参考坐标系 / 边界区域 / 自由边 / 共享边
            for (int fi = 0; fi < nf; fi++)
            {
                if (singleFace && fi != only) continue;
                ctx[fi] = new FaceCtx();
                var dg = new StringBuilder();
                try
                {
                    BrepFace face = brep.Faces[fi];
                    SurfaceMap map = SurfaceMap.Create(face);
                    if (map == null) { ctx[fi].Note = "无法建立参考坐标系，跳过"; ctx[fi].Diag = "map=null"; continue; }
                    ctx[fi].Map = map;

                    if (!map.Planar && FaceWraps(face))
                    {
                        ctx[fi].Note = "整圈环绕的曲面暂不支持，请把面裁成一段（例如 120° 弧片）再生成";
                        ctx[fi].Diag = "wraps=true";
                        continue;
                    }

                    Region2D region = BuildRegion(map, tol);
                    if (region.Rings.Count == 0) { ctx[fi].Note = "无有效边界"; ctx[fi].Diag = "rings=0"; continue; }

                    // 比例尺自检：2D 周长应该和 3D 边界的真实长度一致（等距映射）。
                    // 差得远说明比例尺不可靠（参数化异常），按实测比值统一纠正后重建。
                    double p2 = region.Perimeter2d();
                    double l3 = FaceBoundaryLength(face);
                    if (p2 > 1e-9 && l3 > 1e-9)
                    {
                        double k = l3 / p2;
                        if (k < 0.6 || k > 1.7)
                        {
                            map.SU *= k; map.SV *= k;
                            region = BuildRegion(map, tol);
                            dg.AppendFormat("scaleFix={0:0.###} ", k);
                        }
                    }

                    ctx[fi].Region = region;
                    region.BuildGrid(Math.Max(cell * 0.25, Math.Max(region.Diagonal / 512.0, 1e-4)));
                    region.BuildInsideIndex(Math.Max(step, region.Diagonal / 512.0));

                    ctx[fi].Free = BuildFreeRegion(map, brep, face, step, singleFace);
                    if (!singleFace) ctx[fi].Shared = BuildSharedEdges(map, brep, face, step);

                    try
                    {
                        int spts = 0;
                        for (int k = 0; k < ctx[fi].Shared.Count; k++) spts += ctx[fi].Shared[k].Pts.Count;
                        Interval du = face.Domain(0), dv = face.Domain(1);
                        dg.AppendFormat("planar={0} dom=[{1:0.###},{2:0.###}]x[{3:0.###},{4:0.###}] SU={5:0.####} SV={6:0.####} uv0=({7:0.###},{8:0.###}) rings={9} 2D范围 {10:0.###}x{11:0.###} 自由边{12} 共享边{13}({14}点)",
                            map.Planar, du.Min, du.Max, dv.Min, dv.Max, map.SU, map.SV, map.U0, map.V0,
                            region.Rings.Count, region.W, region.H, ctx[fi].Free.Rings.Count, ctx[fi].Shared.Count, spts);
                        dg.Append(" 邻面法向[");
                        for (int k = 0; k < ctx[fi].Shared.Count && k < 6; k++)
                        {
                            Vector3d nn = ctx[fi].Shared[k].NbrNormal;
                            dg.AppendFormat("({0:0.##},{1:0.##},{2:0.##})", nn.X, nn.Y, nn.Z);
                        }
                        dg.Append("] 本面法向(");
                        Vector3d mine = map.NormalAt(0, 0);
                        dg.AppendFormat("{0:0.##},{1:0.##},{2:0.##})", mine.X, mine.Y, mine.Z);
                    }
                    catch { }
                    ctx[fi].Diag = dg.ToString();
                }
                catch (Exception ex)
                {
                    ctx[fi].Note = "异常: " + ex.Message;
                    ctx[fi].Diag = dg.ToString();
                }
            }

            // ---------------- 2) 种子：每面在自己的 2D 空间生成（保持单面行为），映射到 3D 后跨面去重
            var seeds2d = new List<Point2d>[nf];
            var grads = new Gradient[nf];
            var cellsOf = new CellGeom[nf][];          // 每面：胞元几何（线框取边 + 高度场基准）
            var cloudFace = new List<int>();           // 种子云序号 -> 面序号
            var cloudLocal = new List<int>();          // 种子云序号 -> 该面内的种子序号
            var cloud = new SeedCloud3();
            double thin = cell * 0.6;
            for (int fi = 0; fi < nf; fi++)
            {
                if (ctx[fi] == null || !ctx[fi].Ok) continue;
                string note;
                grads[fi] = Gradient.Create(s, ctx[fi].Map, ctx[fi].Region, u2m);
                List<Point2d> seeds = grads[fi].On
                    ? MakeSeedsGradient(ctx[fi].Region, cell, s.Seed, grads[fi], ctx[fi].Map, out note)
                    : MakeSeeds(ctx[fi].Region, cell, s.Relax, s.Seed, out note);
                seeds2d[fi] = seeds;
                if (seeds == null) { ctx[fi].Note = string.IsNullOrEmpty(note) ? ctx[fi].Note : note; continue; }
                cellsOf[fi] = BuildCells(seeds, cell, wall, ctx[fi].Region);

                for (int i = 0; i < seeds.Count; i++)
                {
                    Point3d p3 = ctx[fi].Map.OffsetPoint(seeds[i].X, seeds[i].Y, 0.0);
                    if (!p3.IsValid) continue;
                    // 跨面重叠区只留一份，避免接缝处种子密度翻倍
                    if (nf > 1 && cloud.AnyOtherFaceWithin(p3, thin, fi)) continue;
                    int before = cloud.Count;
                    cloud.Add(p3, fi);
                    if (cloud.Count > before) { cloudFace.Add(fi); cloudLocal.Add(i); }
                }
                ctx[fi].Diag += string.Format("seeds={0} ", seeds.Count);
            }
            cloud.SetCell(cell);
            cloud.Seal();
            int[] cloudFaceArr = cloudFace.ToArray();
            int[] cloudLocalArr = cloudLocal.ToArray();

            // ---------------- 3) 采样网格 -> 高度场 -> 网格
            for (int fi = 0; fi < nf; fi++)
            {
                if (singleFace && fi != only) continue;
                var res = new VoronoiFaceResult { FaceIndex = fi };
                results.Add(res);
                FaceCtx c = ctx[fi];
                if (c == null) { res.Note = "无结果"; continue; }
                res.Planar = c.Map != null && c.Map.Planar;
                res.Note = c.Note;
                res.Diag = c.Diag;
                if (!c.Ok || seeds2d[fi] == null || seeds2d[fi].Count < 2)
                {
                    if (string.IsNullOrEmpty(res.Note)) res.Note = "胞元尺寸过大或区域过小，未生成纹理";
                    continue;
                }

                try
                {
                    double minH = 0, maxH = 0;
                    // 输出二选一：只有「网格面」模式建网格；线框模式不建（用户口径：二选一，不混着给）
                    if (s.OutputMode == 1)
                        res.Mesh = BuildFanMesh(c, cell, depth, edgeW, s.Shape, s.CellShape, s.DomePower, fade, step,
                                                res, s.PerCell, cellsOf[fi], grads[fi],
                                                s.Smooth, s.SmoothQuadCount, s.SmoothAdaptive, wall, out minH, out maxH);
                    res.CellCount = cloud.CountByTag(fi);
                    // 胞元中心点（3D，落在参考面上）：高度场以它为准，自检/诊断也用它
                    if (cellsOf[fi] != null)
                    {
                        for (int k = 0; k < cellsOf[fi].Length; k++)
                        {
                            CellGeom cg = cellsOf[fi][k];
                            if (cg == null || !cg.Center.IsValid) continue;
                            // 中心点放在**峰高**（沿法向抬到设定深度）：调凹凸深度时中心点跟着上下动（用户口径）
                            Point3d cp = c.Map.OffsetPoint(cg.Center.X, cg.Center.Y, depth);
                            if (cp.IsValid) res.CellCenters.Add(cp);
                        }
                    }
                    // 线框：按种子对取共享边（每条 Voronoi 边只生成一次，真直线、不靠采样网格）
                    if (s.WantWireframe) BuildWireframe(res, c.Region, c.Map, cellsOf[fi], seeds2d[fi], cell, step, wall, depth);
                    if (s.OutputMode == 1)
                        res.Diag += string.Format("seeds保留={0} 层数={1} 外圈点={2}(引用{3}) 峰顶={4} 补弧={5} 被裁点={6} 跳过胞元={7} ",
                            res.CellCount, res.FanLayers, res.FanOuterPts.Count, res.FanOuterRefs, res.FanApexPts.Count,
                            res.FanArcs, res.FanBlocked, res.FanSkipped);
                    else
                        res.Diag += string.Format("seeds保留={0} ", res.CellCount);
                    if (s.OutputMode == 1 && (res.Mesh == null || res.Mesh.Faces.Count == 0))
                    {
                        res.Note = "胞元尺寸过大或区域过小，未生成扇形网格";
                        res.Diag += "mesh=empty";
                        continue;
                    }
                    res.MinOffset = minH;
                    res.MaxOffset = maxH;
                    if (res.FanSkipped > 0)
                        res.Note = string.Format(CultureInfo.InvariantCulture,
                            "{0} 个胞元没建出外圈环（网格上会有洞）：区域边界太碎或内外判定异常", res.FanSkipped);
                    // 高度场基准写清楚（用户口径：用胞元中心距离，不是相邻种子的距离场）
                    if (s.OutputMode == 1)
                    {
                        string hbase = s.Smooth
                            ? "高度场：胞元中心距离（中心 = 设定深度，胞元边 = 0）；网格 = 线框的每个三角面各一张网格片，片内细分 n×n（细分程度 / 平滑度 控制）"
                            : "高度场：胞元中心距离（中心 = 设定深度，胞元边 = 0）；网格 = 线框的每个三角面各一张网格片（未细分，勾「一键平滑」再细分）";
                        res.Note = string.IsNullOrEmpty(res.Note) ? hbase : res.Note + "；" + hbase;
                    }
                    // 溢出边界的自诊断：投影面积（footprint）本该 = 区域面积（扇形剖分是精确覆盖，差得远就是有问题）。
                    if (res.RegionArea > 1e-9 && res.ProjectedArea > res.RegionArea * 1.02)
                    {
                        string warn = string.Format(CultureInfo.InvariantCulture,
                            "网格投影面积比边界面积大 {0:0.0#}%（投影 {1:0.#} / 边界 {2:0.#}）：外圈环越出了边界，请检查边界曲线（窄缝或自交边界会让内外判定出错）",
                            (res.ProjectedArea - res.RegionArea) / res.RegionArea * 100.0, res.ProjectedArea, res.RegionArea);
                        res.Note = string.IsNullOrEmpty(res.Note) ? warn : res.Note + "；" + warn;
                    }
                    res.Diag += string.Format("mesh={0}顶点/{1}面 h=[{2:0.###},{3:0.###}] 线框={4}条(胞元边{5}/外轮廓{6} 考虑{7}=出段{8}+去重{9}+丢{10} 裁边{11}) 投影={12:0.#}/{13:0.#}",
                        res.VertexCount, res.FaceCount, minH, maxH, res.Wireframe.Count,
                        res.Wireframe.Count - res.WireOutline, res.WireOutline, res.WireEdgeSeen, res.WireEmitted, res.WireDedup,
                        res.WireDropped, res.WireSnapped, res.ProjectedArea, res.RegionArea);

                    // 高度场退化（几乎全平）说明比例尺/种子出了问题，明确告诉用户，别让人以为「生成了」
                    if (s.OutputMode == 1 && Math.Abs(depth) > 1e-9 && (maxH - minH) < Math.Abs(depth) * 0.02)
                    {
                        res.Note = "该面纹理高度场退化（几乎全平）：曲面参数化异常或胞元尺寸相对面太大，可换更小的胞元尺寸试试";
                        res.Diag += " flat!";
                    }
                }
                catch (Exception ex)
                {
                    res.Note = "异常: " + ex.Message;
                    res.Diag += " exception=" + ex.Message;
                }

                // 「按胞元输出 NURBS」：每个胞元一张曲面（G0 拼合、边缘贴原曲面）。
                // 注意：只在会话明确要求时（点「生成」那一次）才建，预览一律不算。
                if (s.NurbOutput && s.WantCellSurfaces && c.Ok && seeds2d[fi] != null && seeds2d[fi].Count > 1)
                {
                    try
                    {
                        int tr, un, sk; string cnote;
                        res.CellSurfaces = CellNurbs.Build(c, cloud, seeds2d[fi], cell, depth, edgeW, s.Shape,
                                                           s.CellShape, s.DomePower, grads[fi], u2m, tol,
                                                           s.CellSurfaceBudgetSec,
                                                           out tr, out un, out sk, out cnote);
                        res.NurbsTrimmed = tr; res.NurbsUntrimmed = un; res.NurbsSkipped = sk;
                        res.Diag += string.Format(" 胞元曲面={0}(修剪{1}/未修剪{2}/跳过{3})", res.CellSurfaces.Count, tr, un, sk);
                        if (!string.IsNullOrEmpty(cnote))
                            res.Note = string.IsNullOrEmpty(res.Note) ? cnote : res.Note + "；" + cnote;
                    }
                    catch (Exception ex) { res.Note = string.IsNullOrEmpty(res.Note) ? ("胞元曲面失败: " + ex.Message) : res.Note; }
                }
            }

            // ---------------- 报告
            for (int fi = 0; fi < results.Count; fi++)
            {
                VoronoiFaceResult r = results[fi];
                sb.AppendFormat("面{0}[{1}] 胞元 {2} 个，网格 {3} 顶点/{4} 面{5}{6}\r\n",
                    fi, r.Planar ? "平面" : "曲面", r.CellCount, r.VertexCount, r.FaceCount,
                    r.MaxOffset > double.MinValue ? string.Format("，高度 {0:0.###} ~ {1:0.###}", r.MinOffset, r.MaxOffset) : "",
                    string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note);
                if (!string.IsNullOrEmpty(r.Diag)) sb.AppendFormat("    diag: {0}\r\n", r.Diag);
            }
            if (!string.IsNullOrEmpty(nurbNote)) sb.Insert(0, nurbNote + "\r\n");
            report = sb.ToString();
            return results;
        }

        // ---------------------------------------------------------------- 面的辅助数据
        static double FaceBoundaryLength(BrepFace face)
        {
            double sum = 0;
            try
            {
                foreach (BrepLoop loop in face.Loops)
                {
                    Curve c = loop.To3dCurve();
                    if (c == null) continue;
                    double l = c.GetLength();
                    if (l > 0 && !double.IsNaN(l) && !double.IsInfinity(l)) sum += l;
                }
            }
            catch { }
            return sum;
        }

        static bool FaceWraps(BrepFace face)
        {
            try
            {
                foreach (BrepLoop loop in face.Loops)
                {
                    Curve c2 = loop.To2dCurve();
                    if (c2 == null) continue;
                    if (!c2.IsClosed) return true;
                }
            }
            catch { }
            return false;
        }

        // ---------------------------------------------------------------- 边界
        static Region2D BuildRegion(SurfaceMap map, double step)
        {
            var region = new Region2D();
            if (map.Planar)
            {
                foreach (BrepLoop loop in map.Face.Loops)
                {
                    Curve c3 = loop.To3dCurve();
                    if (c3 == null)
                    {
                        // 兜底：修剪面偶尔给不出 3D 环，用 2D 环按比例尺换算
                        Curve c2b = loop.To2dCurve();
                        if (c2b == null) continue;
                        double len2 = c2b.GetLength();
                        if (!(len2 > 0)) len2 = 1.0;
                        double scale2 = Math.Max((map.SU + map.SV) * 0.5, 1e-9);
                        int nb = Math.Max(16, Math.Min(4000, (int)Math.Round(len2 / Math.Max(step / scale2, 1e-9))));
                        Point3d[] spb = SamplePoints(c2b, nb);
                        if (spb == null) continue;
                        var ptsb = new List<Point2d>();
                        for (int i = 0; i < spb.Length - 1; i++)
                        {
                            var qb = new Point2d((spb[i].X - map.U0) * map.SU, (spb[i].Y - map.V0) * map.SV);
                            if (ptsb.Count > 0 && ptsb[ptsb.Count - 1].DistanceTo(qb) < 1e-12) continue;
                            ptsb.Add(qb);
                        }
                        region.AddRing(ptsb);
                        continue;
                    }
                    var pts = new List<Point2d>();
                    double len = c3.GetLength();
                    if (!(len > 0)) len = 1.0;
                    int n = (int)Math.Round(len / Math.Max(step, 1e-6));
                    n = Math.Max(16, Math.Min(4000, n));
                    Point3d[] sp = SamplePoints(c3, n);
                    if (sp == null) continue;
                    for (int i = 0; i < sp.Length - 1; i++)
                    {
                        Point2d q = map.To2d(sp[i]);
                        if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(q) < 1e-12) continue;
                        pts.Add(q);
                    }
                    region.AddRing(pts);
                }
                return region;
            }

            double scale = Math.Max((map.SU + map.SV) * 0.5, 1e-9);
            double stepUV = Math.Max(step / scale, 1e-9);
            foreach (BrepLoop loop in map.Face.Loops)
            {
                Curve c2 = loop.To2dCurve();
                if (c2 == null) continue;
                double len = c2.GetLength();
                if (!(len > 0)) len = 1.0;
                int n = (int)Math.Round(len / stepUV);
                n = Math.Max(16, Math.Min(4000, n));
                Point3d[] sp = SamplePoints(c2, n);
                if (sp == null) continue;
                var pts = new List<Point2d>();
                for (int i = 0; i < sp.Length - 1; i++)
                {
                    var q = new Point2d((sp[i].X - map.U0) * map.SU, (sp[i].Y - map.V0) * map.SV);
                    if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(q) < 1e-12) continue;
                    pts.Add(q);
                }
                region.AddRing(pts);
            }
            return region;
        }

        /// <summary>
        /// 只由「自由边（只属于一个面的边）」构成的区域：边界收平只作用于整个多重曲面的外边界。
        /// allFree = 单面模式：把这个面的所有边都当外边界（独立成片时按自由边收平更符合直觉）。
        /// </summary>
        static Region2D BuildFreeRegion(SurfaceMap map, Brep brep, BrepFace face, double step, bool allFree)
        {
            var region = new Region2D();
            try
            {
                int[] eis = face.AdjacentEdges();
                if (eis == null) return region;
                foreach (int ei in eis)
                {
                    if (ei < 0 || ei >= brep.Edges.Count) continue;
                    BrepEdge e = brep.Edges[ei];
                    if (!allFree)
                    {
                        int[] af = e.AdjacentFaces();
                        if (af != null && af.Length > 1) continue;   // 内部共享边：不参与收平
                    }

                    Curve c3 = e.DuplicateCurve();
                    if (c3 == null) continue;
                    double len = c3.GetLength();
                    if (!(len > 0)) len = 1.0;
                    int n = (int)Math.Round(len / Math.Max(step, 1e-6));
                    n = Math.Max(8, Math.Min(2000, n));
                    Point3d[] sp = SamplePoints(c3, n);
                    if (sp == null) continue;
                    var pts = new List<Point2d>();
                    for (int i = 0; i < sp.Length; i++)
                    {
                        Point2d q = map.To2d(sp[i]);
                        if (double.IsNaN(q.X) || double.IsNaN(q.Y)) continue;
                        if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(q) < 1e-12) continue;
                        pts.Add(q);
                    }
                    region.AddRing(pts, false);
                }
            }
            catch { }
            if (region.Rings.Count > 0)
                region.BuildGrid(Math.Max(step * 0.5, 1e-4));
            return region;
        }

        /// <summary>本面所有内部共享边（2D 折线 + 邻面法向），接缝斜接用</summary>
        static List<SharedEdge> BuildSharedEdges(SurfaceMap map, Brep brep, BrepFace face, double step)
        {
            var list = new List<SharedEdge>();
            try
            {
                int[] eis = face.AdjacentEdges();
                if (eis == null) return list;
                foreach (int ei in eis)
                {
                    if (ei < 0 || ei >= brep.Edges.Count) continue;
                    BrepEdge e = brep.Edges[ei];
                    int[] af = e.AdjacentFaces();
                    if (af == null || af.Length < 2) continue;

                    int nbr = -1;
                    for (int k = 0; k < af.Length; k++) if (af[k] != face.FaceIndex) { nbr = af[k]; break; }
                    if (nbr < 0) nbr = face.FaceIndex;    // 接缝：邻面就是自己

                    Curve c3 = e.DuplicateCurve();
                    if (c3 == null) continue;
                    double elen = c3.GetLength();
                    if (!(elen > 0)) elen = 1.0;
                    // 按步长密度采样：采样太稀（比如固定 16 段）时，边界顶点离最近采样点
                    // 可能有好几毫米，斜接查找就命中不了 —— 必须和网格分辨率同量级。
                    int en = (int)Math.Round(elen / Math.Max(step, 1e-6));
                    en = Math.Max(8, Math.Min(3000, en));
                    Point3d[] sp = SamplePoints(c3, en);
                    if (sp == null) continue;

                    var se = new SharedEdge();
                    for (int i = 0; i < sp.Length; i++)
                    {
                        Point2d q = map.To2d(sp[i]);
                        if (double.IsNaN(q.X) || double.IsNaN(q.Y)) continue;
                        // 2D 与 3D 同步入列：这两个面拿到的是同一批 3D 点（同一条边、同样段数）
                        se.Add(q, sp[i]);
                    }
                    if (se.Pts.Count < 2) continue;

                    BrepFace nf = brep.Faces[nbr];
                    double u, v;
                    Point3d mid = c3.PointAt(c3.Domain.Mid);
                    Vector3d n = Vector3d.Zero;
                    if (nf.ClosestPoint(mid, out u, out v))
                    {
                        n = nf.NormalAt(u, v);
                        if (!n.IsValid || !n.Unitize()) n = Vector3d.Zero;
                    }
                    se.NbrNormal = n;
                    list.Add(se);
                }
            }
            catch { }
            return list;
        }

        // ---------------------------------------------------------------- 变密度种子（胞元渐变）
        /// <summary>
        /// 按「位置相关的目标间距」做泊松盘采样：候选点按随机顺序试，周围没有更近的已接受点就接受。
        /// 结果是从参考物件向外间距逐渐变化的胞元分布（渐变模式下不做 Lloyd —— 那会把密度重新均匀化）。
        /// </summary>
        static List<Point2d> MakeSeedsGradient(Region2D region, double cell, int seed, Gradient grad,
                                               SurfaceMap map, out string note)
        {
            note = "";
            var rnd = new Rng(seed);
            // 候选网格要按「最密处」的间距取：密集模式（Amount < 0）近处胞元更小，候选点得更细
            double minK = grad != null ? Math.Max(0.2, 1.0 + Math.Min(0.0, grad.Amount)) : 1.0;
            // 先扫一遍估最小间距（候选网格按最密处取）
            double pad = cell * 2;
            double x0 = region.MinX - pad, y0 = region.MinY - pad;
            int nx = (int)Math.Ceiling((region.W + 2 * pad) / (cell * minK * 0.45)) + 1;
            int ny = (int)Math.Ceiling((region.H + 2 * pad) / (cell * minK * 0.45)) + 1;
            if ((long)nx * ny > 60000) { note = "胞元渐变下候选点过多，请调大胞元尺寸"; return null; }

            var cand = new List<Point2d>(nx * ny);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    cand.Add(new Point2d(x0 + i * cell * 0.45 + rnd.NextSigned() * cell * 0.2,
                                         y0 + j * cell * 0.45 + rnd.NextSigned() * cell * 0.2));
            // 随机顺序（确定性：同一个种子结果可复现）
            for (int i = cand.Count - 1; i > 0; i--)
            {
                int k = (int)(rnd.Next() * (i + 1));
                if (k < 0) k = 0;
                if (k > i) k = i;
                Point2d t = cand[i]; cand[i] = cand[k]; cand[k] = t;
            }

            var keep = new List<Point2d>(cand.Count / 4);
            double hashCell = cell;
            var hash = new Dictionary<long, List<int>>();
            for (int ci = 0; ci < cand.Count; ci++)
            {
                Point2d q = cand[ci];
                if (!region.Inside(q)) continue;
                double r = cell * grad.ScaleAt2d(map, q) * 0.85;
                if (!(r > 1e-9)) continue;
                int hx = (int)Math.Floor(q.X / hashCell), hy = (int)Math.Floor(q.Y / hashCell);
                int rr = (int)Math.Ceiling(r / hashCell) + 1;
                bool clash = false;
                for (int ax = hx - rr; ax <= hx + rr && !clash; ax++)
                    for (int ay = hy - rr; ay <= hy + rr && !clash; ay++)
                    {
                        List<int> lst;
                        if (!hash.TryGetValue(HashKey(ax, ay), out lst)) continue;
                        for (int k = 0; k < lst.Count; k++)
                            if (keep[lst[k]].DistanceTo(q) < r) { clash = true; break; }
                    }
                if (clash) continue;
                int idx = keep.Count;
                keep.Add(q);
                long hk = HashKey(hx, hy);
                List<int> l2;
                if (!hash.TryGetValue(hk, out l2)) { l2 = new List<int>(2); hash[hk] = l2; }
                l2.Add(idx);
            }
            if (keep.Count < 2) { note = "胞元渐变下没生成出种子，请调大胞元尺寸"; return null; }
            return keep;
        }

        static long HashKey(int x, int y) { return ((long)x << 32) ^ (uint)y; }

        // ---------------------------------------------------------------- 种子 + 松弛
        static List<Point2d> MakeSeeds(Region2D region, double cell, int relax, int seed, out string note)
        {
            note = "";
            var rnd = new Rng(seed);
            var list = new List<Point2d>();
            double pad = cell;
            double x0 = region.MinX - pad, y0 = region.MinY - pad;
            int nx = (int)Math.Ceiling((region.W + 2 * pad) / cell) + 1;
            int ny = (int)Math.Ceiling((region.H + 2 * pad) / cell) + 1;
            if (nx < 1) nx = 1;
            if (ny < 1) ny = 1;
            // 种子上限：不再直接失败，而是把胞元尺寸放大到能放下（否则「面一大就生成不出」）
            double cellUsed = cell;
            if ((long)nx * ny > 20000)
            {
                double k = Math.Sqrt((long)nx * ny / 20000.0);
                cellUsed = cell * k;
                pad = cellUsed;
                x0 = region.MinX - pad; y0 = region.MinY - pad;
                nx = (int)Math.Ceiling((region.W + 2 * pad) / cellUsed) + 1;
                ny = (int)Math.Ceiling((region.H + 2 * pad) / cellUsed) + 1;
                if (nx < 1) nx = 1;
                if (ny < 1) ny = 1;
                note = string.Format("胞元尺寸已自动放大到 {0:0.##} mm（原尺寸在这张面上会超过 2 万个种子）", cellUsed);
            }
            cell = cellUsed;

            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double x = x0 + i * cell + rnd.NextSigned() * cell * 0.4;
                    double y = y0 + j * cell + rnd.NextSigned() * cell * 0.4;
                    list.Add(new Point2d(x, y));
                }

            if (relax > 0)
            {
                // Lloyd 松弛在「有限点集」上会把外围种子往中心收：点云整体缩小、四角变空，
                // 结果就是规整度越高、越多种子落到区域外被丢掉，边缘没种子、纹理铺不满参考面。
                // 所以把最外一圈种子钉住（覆盖范围由它决定），只让内部种子自由松弛。
                var pinned = new bool[list.Count];
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int k = j * nx + i;
                        if (k >= pinned.Length) continue;
                        pinned[k] = (i == 0 || j == 0 || i == nx - 1 || j == ny - 1);
                    }

                var arr = list.ToArray();
                for (int it = 0; it < relax; it++)
                {
                    var next = new Point2d[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (pinned[i]) { next[i] = arr[i]; continue; }
                        Point2d c = CellCentroid(arr, i, cell * 3.0);
                        next[i] = c.IsValid ? c : arr[i];
                    }
                    arr = next;
                }
                list = new List<Point2d>(arr);
            }

            // 只保留真正落在参考面上的种子：
            // 生成时按区域外扩一圈（让松弛有边界条件），但区域外的种子在多面体上会飘在
            // 相邻面的空间里，按 3D 距离算会造出假的近邻 —— 必须裁掉。
            var keep = new List<Point2d>(list.Count);
            double lim = cell * 0.05;
            foreach (Point2d p in list)
            {
                if (region.Inside(p)) { keep.Add(p); continue; }
                if (lim > 1e-12 && region.Distance(p, lim) <= lim) keep.Add(p);
            }
            if (keep.Count < 2)
            {
                // 兜底：裁剪（内外判定）出问题时不要整面空掉，退回未裁剪的点集 —— 至少能出纹理
                var fallback = new List<Point2d>(list.Count);
                double lim2 = cell * 1.5;
                foreach (Point2d p in list)
                {
                    if (p.X < region.MinX - lim2 || p.X > region.MaxX + lim2 ||
                        p.Y < region.MinY - lim2 || p.Y > region.MaxY + lim2) continue;
                    fallback.Add(p);
                }
                if (fallback.Count < 2) { note = "胞元尺寸过大或区域过小，未生成纹理"; return null; }
                note = string.IsNullOrEmpty(note) ? "内外判定异常，已退回未裁剪的种子点集" : note;
                return fallback;
            }
            return keep;
        }

        /// <summary>第 i 个种子的 Voronoi 胞元（用其余种子的中垂线依次裁剪）的重心</summary>
        static Point2d CellCentroid(Point2d[] seeds, int i, double halfSize)
        {
            List<Point2d> poly = CellPolygon(seeds, i, halfSize, null, 0.0);
            if (poly.Count < 3) return Point2d.Unset;
            return PolyCentroid(poly);
        }

        /// <summary>
        /// 第 i 个种子的 Voronoi 胞元多边形：从「种子 ± halfSize 的方框」开始，用其余种子的中垂线半平面裁剪（凸）。
        /// nearGrid / nearRadius 不为空时只裁附近种子（胞元不会超过半个最近邻距，远处种子的中垂线裁不到它）—— 省掉 O(n²)。
        /// </summary>
        static List<Point2d> CellPolygon(Point2d[] seeds, int i, double halfSize,
                                         Dictionary<long, List<int>> nearGrid, double nearRadius)
        {
            var poly = new List<Point2d>(8)
            {
                new Point2d(seeds[i].X - halfSize, seeds[i].Y - halfSize),
                new Point2d(seeds[i].X + halfSize, seeds[i].Y - halfSize),
                new Point2d(seeds[i].X + halfSize, seeds[i].Y + halfSize),
                new Point2d(seeds[i].X - halfSize, seeds[i].Y + halfSize)
            };
            if (nearGrid == null)
            {
                for (int j = 0; j < seeds.Length && poly.Count >= 3; j++)
                {
                    if (j == i) continue;
                    poly = ClipHalf(poly, seeds[i], seeds[j]);
                }
                return poly;
            }

            double gc = Math.Max(nearRadius * 0.25, 1e-9);
            int gx = (int)Math.Floor(seeds[i].X / gc), gy = (int)Math.Floor(seeds[i].Y / gc);
            int nr = (int)Math.Ceiling(nearRadius / gc);
            double r2 = nearRadius * nearRadius;
            for (int ax = gx - nr; ax <= gx + nr && poly.Count >= 3; ax++)
                for (int ay = gy - nr; ay <= gy + nr && poly.Count >= 3; ay++)
                {
                    List<int> l;
                    if (!nearGrid.TryGetValue(HashKey(ax, ay), out l)) continue;
                    for (int k = 0; k < l.Count && poly.Count >= 3; k++)
                    {
                        int j = l[k];
                        if (j == i) continue;
                        double dx = seeds[j].X - seeds[i].X, dy = seeds[j].Y - seeds[i].Y;
                        if (dx * dx + dy * dy > r2) continue;
                        poly = ClipHalf(poly, seeds[i], seeds[j]);
                    }
                }
            return poly;
        }

        /// <summary>多边形的面积质心（凸多边形：一定在内部）</summary>
        static Point2d PolyCentroid(List<Point2d> poly)
        {
            if (poly == null || poly.Count < 3) return Point2d.Unset;
            double a2 = 0, cx = 0, cy = 0;
            for (int k = 0; k < poly.Count; k++)
            {
                Point2d a = poly[k], b = poly[(k + 1) % poly.Count];
                double cr = a.X * b.Y - b.X * a.Y;
                a2 += cr; cx += (a.X + b.X) * cr; cy += (a.Y + b.Y) * cr;
            }
            if (Math.Abs(a2) < 1e-12) return Point2d.Unset;
            return new Point2d(cx / (3 * a2), cy / (3 * a2));
        }

        /// <summary>点到多边形边的最短距离（高度场用它给平顶的过渡宽度兜底，保证中心一定到满高）</summary>
        static double PolyInradius(List<Point2d> poly, Point2d c)
        {
            if (poly == null || poly.Count < 3 || !c.IsValid) return 1e-9;
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a = poly[i], b = poly[(i + 1) % poly.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double l2 = ex * ex + ey * ey;
                double t = l2 > 1e-18 ? ((c.X - a.X) * ex + (c.Y - a.Y) * ey) / l2 : 0.0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double dx = c.X - (a.X + t * ex), dy = c.Y - (a.Y + t * ey);
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < best) best = d;
            }
            return best == double.MaxValue ? 1e-9 : Math.Max(best, 1e-9);
        }

        /// <summary>
        /// 每个种子的胞元几何（凸多边形 + 质心 + 内切半径）。只按附近种子裁剪：
        /// 胞元一定落在「种子 ± 1 个最近邻距」内，所以半径 4×胞元尺寸外的中垂线裁不到它。
        /// region 不为空时中心改成「多边形 ∩ 区域」的中心（质心会落在区域外 —— 边界胞元被区域切掉一半），
        /// 内切半径同时受区域边界限制（保证峰顶落在区域内）。
        /// </summary>
        static CellGeom[] BuildCells(List<Point2d> seeds, double cell, double wall, Region2D region)
        {
            var arr = seeds.ToArray();
            var cells = new CellGeom[arr.Length];
            double half = Math.Max(cell * 3.0, 1e-6);
            double near = Math.Max(cell * 4.0, 1e-6);
            var grid = new Dictionary<long, List<int>>(arr.Length * 2);
            double gc = Math.Max(cell, 1e-9);
            for (int i = 0; i < arr.Length; i++)
            {
                long k = HashKey((int)Math.Floor(arr[i].X / gc), (int)Math.Floor(arr[i].Y / gc));
                List<int> l;
                if (!grid.TryGetValue(k, out l)) { l = new List<int>(4); grid[k] = l; }
                l.Add(i);
            }
            for (int i = 0; i < arr.Length; i++)
            {
                var g = new CellGeom();
                g.Poly = CellPolygon(arr, i, half, grid, near);
                g.Center = PolyCentroid(g.Poly);
                g.Inradius = PolyInradius(g.Poly, g.Center);
                // 中心必须落在区域内：胞元凸多边形只按 ±3×胞元裁过，边界胞元的质心会跑到区域外
                //（用户截图：框外一圈中心点、连线朝框外跑）。改成「多边形 ∩ 区域」的中心。
                g.Center = RegionAwareCenter(g.Poly, g.Center, arr[i], region, cell);
                // 内切半径同时受区域边界限制：中心到区域边界比到胞元边更近时，峰顶按区域边界收
                //（否则「中心 = 满高」会顶在区域边上，网格在边界处不归零）
                double dReg = region != null ? region.Distance(g.Center, cell * 3.0) : double.MaxValue;
                double dPoly = PolyInradius(g.Poly, g.Center);
                g.Inradius = dReg == double.MaxValue ? dPoly : Math.Min(dPoly, dReg);
                // 细胞壁：每个胞元向内缩 壁厚/2 → 相邻两胞元之间正好留出「壁厚」宽的平底壁
                if (wall > 1e-9 && g.Poly.Count >= 3 && g.Center.IsValid)
                {
                    List<Point2d> ins = InsetPoly(g.Poly, g.Center, wall * 0.5);
                    if (ins != null && ins.Count >= 3) g.InsetPoly = ins;
                }
                if (g.InsetPoly == null) g.InsetPoly = g.Poly;
                double dInset = PolyInradius(g.InsetPoly, g.Center);
                g.InsetInradius = dReg == double.MaxValue ? dInset : Math.Min(dInset, dReg);
                cells[i] = g;
            }
            return cells;
        }

        /// <summary>
        /// 落在区域内的胞元中心：在胞元多边形的包围盒上撒 K×K 采样，保留「在多边形内 且 在区域内」的点取平均。
        /// 质心本来就在区域内时直接返回（不采样，保持原行为）。
        /// 均值又落回区域外（区域非凸，例如 L 形的凹口）时取离均值最近的那个采样点；
        /// 一个采样点都没有（胞元几乎全在区域外）时退回「区域边界最近点往胞元种子方向挪一点」。
        /// </summary>
        static Point2d RegionAwareCenter(List<Point2d> poly, Point2d centroid, Point2d seed, Region2D region, double cell)
        {
            if (region == null || poly == null || poly.Count < 3 || !centroid.IsValid) return centroid;
            if (region.Inside(centroid)) return centroid;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d p = poly[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            const int K = 12;
            double sx = (maxX - minX) / K, sy = (maxY - minY) / K;
            var inside = new List<Point2d>(K * K / 2);
            double sumX = 0, sumY = 0;
            for (int i = 0; i < K; i++)
                for (int j = 0; j < K; j++)
                {
                    var q = new Point2d(minX + (i + 0.5) * sx, minY + (j + 0.5) * sy);
                    if (!InsideConvexPoly(poly, q)) continue;
                    if (!region.Inside(q)) continue;
                    inside.Add(q);
                    sumX += q.X; sumY += q.Y;
                }
            if (inside.Count > 0)
            {
                var avg = new Point2d(sumX / inside.Count, sumY / inside.Count);
                if (region.Inside(avg)) return avg;
                Point2d best = inside[0];
                double bd = double.MaxValue;
                for (int i = 0; i < inside.Count; i++)
                {
                    double d = inside[i].DistanceTo(avg);
                    if (d < bd) { bd = d; best = inside[i]; }
                }
                return best;
            }

            // 兜底：区域边界上最近点，朝种子（一定在区域内）方向挪一点，确保落在区域里而不是压在边界上
            Point2d q2;
            if (region.ClosestBoundaryPoint(centroid, Math.Max(cell * 3.0, 1e-6), out q2))
            {
                double dx = seed.X - q2.X, dy = seed.Y - q2.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1e-12)
                {
                    double eps = Math.Min(cell * 1e-3, len * 0.5);
                    var r = new Point2d(q2.X + dx / len * eps, q2.Y + dy / len * eps);
                    if (region.Inside(r)) return r;
                }
                if (region.Inside(q2)) return q2;
            }
            return centroid;
        }

        /// <summary>点在凸多边形内（含边界）：区域内的中心采样筛选用</summary>
        static bool InsideConvexPoly(List<Point2d> poly, Point2d p)
        {
            if (poly == null || poly.Count < 3) return false;
            int sign = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a = poly[i], b = poly[(i + 1) % poly.Count];
                double cr = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (Math.Abs(cr) < 1e-12) continue;
                int s = cr > 0 ? 1 : -1;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }

        /// <summary>
        /// 凸多边形向内偏移 t（细胞壁用）：每条边沿「指向中心」的法向平移 t，再求相邻偏移线的交点。
        /// 偏移过多（内缩后没有面积）时返回 null，调用方回退成原多边形。
        /// </summary>
        static List<Point2d> InsetPoly(List<Point2d> poly, Point2d c, double t)
        {
            if (poly == null || poly.Count < 3 || !(t > 1e-12)) return poly;
            int n = poly.Count;
            var A = new double[n]; var B = new double[n]; var C = new double[n];
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = poly[i], p1 = poly[(i + 1) % n];
                double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
                double L = Math.Sqrt(dx * dx + dy * dy);
                if (L < 1e-12) continue;
                double nx = -dy / L, ny = dx / L;                       // 边法向
                if ((c.X - p0.X) * nx + (c.Y - p0.Y) * ny < 0) { nx = -nx; ny = -ny; }   // 朝中心
                A[m] = nx; B[m] = ny; C[m] = -(nx * p0.X + ny * p0.Y + t);               // 沿法向平移 t
                m++;
            }
            if (m < 3) return null;
            var outp = new List<Point2d>(m);
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m;
                double det = A[i] * B[j] - A[j] * B[i];
                if (Math.Abs(det) < 1e-12) return null;
                double x = (B[i] * C[j] - B[j] * C[i]) / det;
                double y = (A[j] * C[i] - A[i] * C[j]) / det;
                outp.Add(new Point2d(x, y));
            }
            return outp;
        }

        /// <summary>点到（凸）多边形边界的最短距离：边中点、角上都是 0（高度场用它当基准）</summary>
        static double DistToPolyEdge(List<Point2d> poly, Point2d p)
        {
            if (poly == null || poly.Count < 2) return 0;
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a = poly[i], b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double l2 = dx * dx + dy * dy;
                double t = l2 > 1e-18 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2 : 0.0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double ex = p.X - (a.X + t * dx), ey = p.Y - (a.Y + t * dy);
                double d = Math.Sqrt(ex * ex + ey * ey);
                if (d < best) best = d;
            }
            return best == double.MaxValue ? 0 : best;
        }

        /// <summary>保留「离 a 比离 b 近」的一侧（Sutherland–Hodgman）</summary>
        static List<Point2d> ClipHalf(List<Point2d> poly, Point2d a, Point2d b)
        {
            var outp = new List<Point2d>(poly.Count + 2);
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            double nx = b.X - a.X, ny = b.Y - a.Y;
            double nl = Math.Sqrt(nx * nx + ny * ny);
            if (nl < 1e-12) return poly;
            nx /= nl; ny /= nl;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d p = poly[i], q = poly[(i + 1) % poly.Count];
                double dp = (p.X - mx) * nx + (p.Y - my) * ny;   // <=0 表示靠近 a
                double dq = (q.X - mx) * nx + (q.Y - my) * ny;
                if (dp <= 0) outp.Add(p);
                if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))
                {
                    double t = dp / (dp - dq);
                    outp.Add(new Point2d(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                }
            }
            return outp;
        }

        // ---------------------------------------------------------------- 高度场 + 网格（旧采样网格路径，已不在输出里用）
        /// <summary>
        /// 旧的「采样网格 + 高度场」路径：按采样间距铺满区域、逐点算高度场。
        /// **已不再被 Generate 调用**（用户口径：网格必须用生成的线框点建 → BuildFanMesh），
        /// 保留代码只为对照/回退；采样间距现在只用于线框与扇形的裁剪、分层精度。
        /// </summary>
        static Mesh BuildMesh(FaceCtx c, SeedCloud3 cloud, double cell,
                              double depth, double edgeW, double shape, int cellShape, double domePower,
                              double fade, double step,
                              VoronoiFaceResult res, bool perCell, bool smoothSeam,
                              CellGeom[] cells, int[] cloudFace, int[] cloudLocal, int faceIndex,
                              Gradient grad, double u2m,
                              out double minH, out double maxH,
                              ref int nxOut, ref int nyOut, ref int skippedOut, ref int snappedOut,
                              ref int miteredOut, ref int miterMissOut, ref double minSharedDistOut)
        {
            minH = 0; maxH = 0;
            SurfaceMap map = c.Map;
            Region2D region = c.Region;
            double x0 = region.MinX, y0 = region.MinY;
            double w = region.W, h = region.H;
            if (!(w > 0) || !(h > 0)) return null;

            int nx = (int)Math.Ceiling(w / step);
            int ny = (int)Math.Ceiling(h / step);
            // 总节点数上限，防止参数极端时卡死
            long total = (long)(nx + 1) * (ny + 1);
            // 节点上限：超过就把采样间距放大（否则内存/时间失控）。**必须报出来** ——
            // 用户设 0.1mm 采样 + 2mm 胞元时，网格被静默放大到 ~1mm，每个胞元只剩两个采样点，
            // 表现就是胞元边界一圈明显锯齿。上限同时提到 300 万节点（约 2000x1500，几秒量级；再往上预览就会明显变卡）。
            if (total > 3000000)
            {
                double stepWanted = step;
                double k = Math.Sqrt(total / 3000000.0);
                step *= k;
                nx = (int)Math.Ceiling(w / step);
                ny = (int)Math.Ceiling(h / step);
                if (res != null)
                {
                    string warn = string.Format(
                        "采样间距已自动放大：{0:0.###} → {1:0.###} mm（原间距在这张面上要 {2} 万个节点）。胞元边界的锯齿就是这个引起的 —— 想更细腻就把「采样间距」调到「过渡宽度 ÷ 5」以下，或把胞元尺寸调大。",
                        stepWanted / Math.Max(u2m, 1e-9), step / Math.Max(u2m, 1e-9), total / 10000);
                    res.Note = string.IsNullOrEmpty(res.Note) ? warn : res.Note + "；" + warn;
                }
            }
            if (nx < 1) nx = 1;
            if (ny < 1) ny = 1;
            nxOut = nx; nyOut = ny;
            double sx = w / nx, sy = h / ny;

            var mesh = new Mesh();
            var idx = new int[nx + 1, ny + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++) idx[i, j] = -1;

            double loH = double.MaxValue, hiH = double.MinValue;
            bool hasFree = c.Free != null && c.Free.Rings.Count > 0;
            double minSharedDist = double.MaxValue;
            double blendW = Math.Max(cell * 0.5, edgeW * 2.0);   // 接缝平滑的过渡宽度

            // 共享边公共采样点索引：网格点吸附到接缝时用「公共点」，
            // 相邻两个面吸到同一批点上，接缝两侧的顶点才会完全重合
            SeamIndex seam = null;
            if (c.Shared.Count > 0)
            {
                seam = new SeamIndex();
                seam.Build(c.Shared, Math.Max(step * 2.0, 1e-9), map.NormalAt(0, 0));
            }

            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++)
                {
                    var p = new Point2d(x0 + i * sx, y0 + j * sy);
                    bool inside = region.Inside(p);
                    bool onBoundary = false;
                    bool seamSnap = false;
                    Point3d P = Point3d.Unset;
                    if (!inside)
                    {
                        // 边界外但很靠近：吸附到边界上，让网格边缘贴住面边界。
                        // 靠近共享边时优先吸附「公共采样点」—— 各面各吸各的最近点会错开半格，
                        // 拼起来就是两张面；用公共点两边完全对齐。
                        Point2d q2; Point3d q3; bool smoothEdge;
                        if (seam != null && seam.Nearest(p, step * 1.5, out q2, out q3, out smoothEdge))
                        {
                            p = q2; P = q3;
                            inside = true;
                            onBoundary = true;
                            snappedOut++;
                            // 输入面在这里顺接（共面/相切）→ 该顶点可以焊接（合并成整体一张面时平滑）
                            seamSnap = smoothSeam || smoothEdge;
                        }
                        else
                        {
                            double dEdge = region.Distance(p, step * 1.5);
                            Point2d q;
                            if (!(dEdge <= step * 1.5 && region.ClosestBoundaryPoint(p, step * 1.5, out q))) { skippedOut++; continue; }
                            p = q;
                            P = map.To3d(p.X, p.Y);
                            inside = true;
                            onBoundary = true;
                            snappedOut++;
                        }
                    }
                    else
                    {
                        if (region.Distance(p, step * 0.02) <= step * 0.02)
                            onBoundary = true;   // 正好压在边界上的网格点（也要按边界处理）
                        P = map.To3d(p.X, p.Y);
                    }

                    if (!P.IsValid) { skippedOut++; continue; }

                    double d1, d2; int cellIdx;
                    cloud.Nearest2(P, out d1, out d2, out cellIdx);
                    if (d2 == double.MaxValue) d2 = d1;
                    if (d1 == double.MaxValue) { skippedOut++; continue; }
                    double gap = (d2 - d1) * 0.5;

                    // 高度场基准 = **本胞元中心点到采样点的距离**（不是相邻种子的距离场）：
                    //   中心处 = 设定深度，胞元边缘 = 0（相邻胞元在共享边上都是 0 → 不裂开）。
                    // 局部胞元尺寸比例（渐变疏密）只用来缩放过渡宽度。
                    double gradK = 1.0;
                    if (grad != null && grad.On)
                    {
                        double k = grad.ScaleAt(P);
                        if (k > 1e-6) gradK = k;
                    }
                    CellGeom cg = CellOf(cells, cloudFace, cloudLocal, cellIdx, faceIndex);
                    double f;
                    if (cg != null && cg.Poly.Count >= 3 && cg.Center.IsValid)
                    {
                        // 归一化用**内切半径**（中心到最近胞元边的距离，每个胞元算一次）：
                        //   中心 dC = 0 → 满高；边中点 dC = inradius → 0；角上 dC > inradius → 夹到 0（不裂开、不残留）。
                        var q = new Point2d(p.X, p.Y);
                        // 高度场基准 = **到（内缩后）胞元多边形边界的距离**（用户口径：凸起要跟线框的胞元形状对得上）：
                        //   中心（dPoly = 内切半径）→ 满高；胞元边上（含角上）dPoly = 0 → 0；平台形状 = 胞元多边形本身。
                        //   ⚠ 不能用「到中心的距离 / 内切半径」——那会把平台变成内切圆，网格的鼓包跟线框的多边形对不上。
                        List<Point2d> hp = (cg.InsetPoly != null && cg.InsetPoly.Count >= 3) ? cg.InsetPoly : cg.Poly;
                        double inv = cg.InsetInradius > 1e-9 ? cg.InsetInradius : cg.Inradius;   // 有壁厚时用内缩后的
                        if (!(inv > 1e-9)) inv = 1e-9;
                        double dPoly = DistToPolyEdge(hp, q);
                        if (cellShape == 1)
                        {
                            // 穹顶：按到边界的归一化距离取幂（中心 1、边/角 0）
                            double t = dPoly / inv;
                            if (t > 1) t = 1;
                            if (t < 0) t = 0;
                            f = domePower != 1.0 ? Math.Pow(t, domePower) : t;
                        }
                        else
                        {
                            // 平顶：离胞元边界 EdgeWidth 内平滑收边，内部是平顶（参数语义不变）。
                            // 过渡宽度取 min(EdgeWidth, 内切半径)：小胞元也能保证中心到满高（用户口径：中心 = 设定深度）
                            double wCell = edgeW * gradK;
                            if (wCell > inv) wCell = inv;
                            double t = wCell > 1e-9 ? dPoly / wCell : 1.0;
                            if (t > 1) t = 1;                       // 平顶
                            f = Smoothstep(t);                      // t = 0（边/角）→ 0
                            if (shape != 1.0) f = Math.Pow(f, shape);
                        }
                    }
                    else
                    {
                        // 兜底（多面接缝：最近种子属于别的面，中心场算不了）→ 退回「到胞元边界的距离场」，如实计数
                        res.CellFallback++;
                        if (cellShape == 1)
                        {
                            double half = cell * 0.5 * gradK;
                            if (!(half > 1e-9)) half = 1e-9;
                            double t = gap / half;
                            if (t > 1) t = 1;
                            if (t < 0) t = 0;
                            f = domePower != 1.0 ? Math.Pow(t, domePower) : t;
                        }
                        else
                        {
                            double wFall = edgeW * gradK;
                            double t = wFall > 1e-9 ? gap / wFall : 1.0;
                            if (t > 1) t = 1;
                            f = Smoothstep(t);
                            if (shape != 1.0) f = Math.Pow(f, shape);
                        }
                    }
                    double hh = depth * f;

                    if (fade > 1e-9 && hasFree)
                    {
                        double dFree = c.Free.Distance(p, fade * 1.2 + step);
                        double tb = dFree / fade;
                        if (tb > 1) tb = 1;
                        hh *= Smoothstep(tb);
                    }

                    Point3d v;
                    Vector3d nThis = map.NormalAt(p.X, p.Y);
                    if (smoothSeam && c.Shared.Count > 0)
                    {
                        // 跨面平滑：接缝附近把法向朝邻面混过去（接缝处两边都取 50/50 → 位置完全一致），
                        // 于是多重曲面内部不留折痕，整片就是一张连续曲面。
                        Vector3d nUse = SeamBlendNormal(c.Shared, p, blendW, nThis);
                        v = P + nUse * hh;
                    }
                    else if (onBoundary && c.Shared.Count > 0)
                    {
                        bool hit; double md;
                        Point3d mv = MiterPoint(map, c.Shared, p, P, hh, step, out hit, out md);
                        if (hit) miteredOut++; else miterMissOut++;
                        if (md < minSharedDist) minSharedDist = md;
                        v = mv;
                    }
                    else
                        v = P + nThis * hh;

                    if (!v.IsValid) { skippedOut++; continue; }
                    idx[i, j] = mesh.Vertices.Add(v);
                    // 记「实际顶点位置」（不是曲面参考点）：合并成整体一张面时按它焊接。
                    // 两边用的是同一个公共采样点 + 同一个高度场 + 对称的偏移方向 → 位置完全重合。
                    if (seamSnap && res != null) res.SeamWeldPos.Add(v);
                    if (hh < loH) loH = hh;
                    if (hh > hiH) hiH = hh;
                    if (onBoundary)
                    {
                        res.BoundaryRef.Add(P);
                        res.BoundaryPos.Add(v);
                    }
                }

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    int a = idx[i, j], b = idx[i + 1, j], c2 = idx[i + 1, j + 1], d = idx[i, j + 1];
                    if (a < 0 || b < 0 || c2 < 0 || d < 0) continue;
                    if (a != b && b != c2 && c2 != d && d != a) { mesh.Faces.AddFace(a, b, c2, d); continue; }
                    // 相邻网格点吸到同一个公共采样点会重合：按剩下不重复的顶点出三角形，别丢洞
                    if (a == b) { if (a != c2 && a != d && c2 != d) mesh.Faces.AddFace(a, c2, d); continue; }
                    if (b == c2) { if (a != b && b != d && a != d) mesh.Faces.AddFace(a, b, d); continue; }
                    if (c2 == d) { if (a != b && a != c2 && b != c2) mesh.Faces.AddFace(a, b, c2); continue; }
                    if (a != b && b != c2 && a != c2) mesh.Faces.AddFace(a, b, c2);   // d == a
                }

            // ---- 按胞元切分（输出拓扑 = 每胞元一张面）
            // 每个网格面归给「它中心最近的那个种子」；相邻胞元共用同一批顶点，
            // 所以分开之后边界严丝合缝（不是各算各的）。必须在 Compact() 之前做。
            if (perCell && res != null)
            {
                var groups = new Dictionary<int, List<int>>();
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        int a = idx[i, j], b = idx[i + 1, j], c2 = idx[i + 1, j + 1], d = idx[i, j + 1];
                        if (a < 0 || b < 0 || c2 < 0 || d < 0) continue;
                        Point3d pa = mesh.Vertices[a], pb = mesh.Vertices[b], pc = mesh.Vertices[c2], pd = mesh.Vertices[d];
                        var cen = new Point3d((pa.X + pb.X + pc.X + pd.X) * 0.25,
                                              (pa.Y + pb.Y + pc.Y + pd.Y) * 0.25,
                                              (pa.Z + pb.Z + pc.Z + pd.Z) * 0.25);
                        int sd = cloud.NearestIndex(cen);
                        if (sd < 0) continue;
                        List<int> lst;
                        if (!groups.TryGetValue(sd, out lst)) { lst = new List<int>(); groups[sd] = lst; }
                        lst.Add(i * ny + j);
                    }
                foreach (KeyValuePair<int, List<int>> kv in groups)
                {
                    var sub = new Mesh();
                    var vmap = new Dictionary<int, int>();
                    for (int q = 0; q < kv.Value.Count; q++)
                    {
                        int code = kv.Value[q];
                        int i = code / ny, j = code % ny;
                        int[] quad = { idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1] };
                        int[] nq = new int[4];
                        for (int k = 0; k < 4; k++)
                        {
                            int newIdx;
                            if (!vmap.TryGetValue(quad[k], out newIdx))
                            {
                                newIdx = sub.Vertices.Add(mesh.Vertices[quad[k]]);
                                vmap[quad[k]] = newIdx;
                            }
                            nq[k] = newIdx;
                        }
                        sub.Faces.AddFace(nq[0], nq[1], nq[2], nq[3]);
                    }
                    if (sub.Faces.Count == 0) continue;
                    sub.Normals.ComputeNormals();
                    sub.Compact();
                    res.CellMeshes.Add(sub);
                }
            }

            // ---- 投影面积（判溢出边界用）：把每个四边形投到参考平面上求面积后求和。
            //      区域外的采样点已经被吸附到边界上 → 正常情况下投影面积 = 区域面积（±采样台阶）。
            if (res != null && map.Planar)
            {
                double proj = 0;
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        int a = idx[i, j], b = idx[i + 1, j], c2 = idx[i + 1, j + 1], d = idx[i, j + 1];
                        if (a < 0 || b < 0 || c2 < 0 || d < 0) continue;
                        proj += ProjQuadArea(map, mesh.Vertices[a], mesh.Vertices[b], mesh.Vertices[c2], mesh.Vertices[d]);
                    }
                res.ProjectedArea = proj;
                res.RegionArea = RingsArea(region);
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            minH = loH == double.MaxValue ? 0 : loH;
            maxH = hiH == double.MinValue ? 0 : hiH;
            minSharedDistOut = minSharedDist;
            return mesh;
        }

        /// <summary>最近种子对应的胞元几何（种子属于别的面 / 序号越界 → null，走距离场兜底）</summary>
        static CellGeom CellOf(CellGeom[] cells, int[] cloudFace, int[] cloudLocal, int cloudIdx, int faceIndex)
        {
            if (cells == null || cloudFace == null || cloudLocal == null) return null;
            if (cloudIdx < 0 || cloudIdx >= cloudFace.Length) return null;
            if (cloudFace[cloudIdx] != faceIndex) return null;
            int li = cloudLocal[cloudIdx];
            if (li < 0 || li >= cells.Length) return null;
            return cells[li];
        }

        // ============================================================ 胞元扇形网格（网格点 = 线框点 + 中心）

        /// <summary>
        /// 胞元扇形网格（用户口径：**「把每一个三角形生成单一的网格片，然后再这个基础上细分网格」**）：
        ///   ① 外圈环 = 胞元多边形逐边裁到区域（与线框同一套 ClipSegToRegion），缺口用**区域外轮廓**补齐
        ///      → 环上每个点都是线框端点（胞元边段端点 / 外轮廓点）；
        ///   ② **每个线框三角面 = 一张网格片**：面 = (中心, 环点 j, 环点 j+1)，就是线框里「中心 → 各顶点」的连线所围的那个三角形；
        ///   ③ 勾「一键平滑」时在片内细分：重心格点拆成 n×n 张三角面（n 由「细分程度 / 平滑度」定）；
        ///      不勾则每片 1 张三角面（网格拓扑 = 线框本身）。
        /// 高度：h = 深度 × f(dPoly)，dPoly = 该点到外圈环的距离（中心用内切半径 → 满高；外圈 = 0）；f 沿用平顶/穹顶曲线。
        /// 相邻片共享边上的点由同一段端点插值 → 位置重合，按同坐标合并成一个顶点 → 合并后是一张连续网格（不裂缝）。
        /// ⚠ 旧的「径向同心环 + 层数 R」剖分已作废（那是星芒状、越靠中心越碎，用户实测「拓扑结构完全不对」）。
        /// </summary>
        static Mesh BuildFanMesh(FaceCtx c, double cell, double depth, double edgeW, double shape,
                                 int cellShape, double domePower, double fade, double step,
                                 VoronoiFaceResult res, bool perCell, CellGeom[] cells, Gradient grad,
                                 bool subdivide, int targetFaces, double adaptive, double wall,
                                 out double minH, out double maxH)
        {
            minH = 0; maxH = 0;
            if (c == null || c.Map == null || c.Region == null || cells == null) return null;
            SurfaceMap map = c.Map;
            Region2D region = c.Region;
            bool hasFree = c.Free != null && c.Free.Rings.Count > 0;
            double loH = double.MaxValue, hiH = double.MinValue;

            int target = targetFaces;
            if (target < 50) target = 50;
            if (target > 500000) target = 500000;
            double beta = (adaptive - 50.0) / 50.0;      // 平滑度 100% → 1（档位按边长配平 → 网格边长一致）
            if (beta < 0.0) beta = 0.0;
            if (beta > 1.0) beta = 1.0;

            // 区域外轮廓（去共线的简化环）= 线框「外轮廓」用的同一批点：外圈环的缺口靠它补齐
            var rings = new List<Point2d[]>();
            for (int r = 0; r < region.Rings.Count; r++)
            {
                List<Point2d> sr = SimplifyRing(region.Rings[r]);
                if (sr.Count >= 3) rings.Add(sr.ToArray());
            }

            var vmap = new Dictionary<(long, long), int>();     // 量化 2D 位置 → 主网格顶点（相邻片/相邻胞元共享）
            var outerSeen = new Dictionary<(long, long), int>(); // 量化 2D 位置 → res.FanOuterPts 下标（去重）
            double vq = Math.Max(1e-7, region.Diagonal * 1e-9);
            double hTol = Math.Max(step * 1e-3, 1e-9);
            double joinTol = Math.Max(1e-9, step * 1e-6);

            // ---- ① 收集：每个胞元的外圈环 + 每个线框三角面的最长边（决定片内细分档位）
            var work = new List<FanCellWork>();
            double sumEdge = 0;
            int triTotal = 0;
            for (int ci = 0; ci < cells.Length; ci++)
            {
                CellGeom cg = cells[ci];
                if (cg == null || cg.Poly == null || cg.Poly.Count < 3 || !cg.Center.IsValid) continue;
                // ⚠ 网格铺的是**整个胞元多边形**（不是内缩后的）：加了壁厚以后，胞元之间靠这段
                //   「壁厚带（高度恒 0 的平面）」连成一张，不留缝（用户实测：只铺内缩多边形会一块块断开）
                List<Point2d> poly = cg.Poly;
                int arcs;
                List<Point2d> ring = BuildCellOuterRing(region, rings, poly, cg.Center, step, joinTol, out arcs);
                if (ring == null || ring.Count < 3)
                {
                    if (res != null) res.FanSkipped++;
                    continue;
                }
                if (res != null)
                {
                    res.FanArcs += arcs;
                    res.FanRingArea += Math.Abs(PolySignedArea(ring));
                    res.FanOuterRefs += ring.Count;
                }
                var w = new FanCellWork { Cg = cg, Ring = ring, Longest = new double[ring.Count] };
                for (int j = 0; j < ring.Count; j++)
                {
                    int j2 = (j + 1) % ring.Count;
                    double le = Math.Max(cg.Center.DistanceTo(ring[j]),
                                Math.Max(ring[j].DistanceTo(ring[j2]), ring[j2].DistanceTo(cg.Center)));
                    if (!(le > 0)) le = 1e-9;
                    w.Longest[j] = le;
                    sumEdge += le;
                    triTotal++;
                }
                work.Add(w);
            }
            if (work.Count == 0 || triTotal == 0) return null;

            // 目标面数 → 每片档位：n_j = round(m0 · (L_j/平均边长)^β)，m0 = sqrt(目标面数 / Σ (L_j/平均边长)^(2β))
            double meanEdge = sumEdge / triTotal;
            if (!(meanEdge > 0)) meanEdge = 1.0;
            double sumPow = 0;
            for (int wi = 0; wi < work.Count; wi++)
                for (int j = 0; j < work[wi].Longest.Length; j++)
                    sumPow += Math.Pow(work[wi].Longest[j] / meanEdge, 2.0 * beta);
            if (!(sumPow > 0)) sumPow = triTotal;
            double m0 = subdivide ? Math.Sqrt(target / sumPow) : 1.0;

            // ---- ② 建网格：每个线框三角面一张网格片，片内重心格点细分
            var mesh = new Mesh();
            var p2s = new List<Point2d>();                      // 顶点下标 → 2D 位置（凹区域越界守卫用）
            int maxGrid = 1;
            for (int wi = 0; wi < work.Count; wi++)
            {
                FanCellWork w = work[wi];
                CellGeom cg = w.Cg;
                List<Point2d> ring = w.Ring;
                int n = ring.Count;
                double inv = cg.InsetInradius > 1e-9 ? cg.InsetInradius : cg.Inradius;
                if (!(inv > 1e-9)) inv = 1e-9;
                double wallHalf = wall > 0 ? wall * 0.5 : 0.0;   // 每个胞元向内缩 壁厚/2 → 相邻两胞元的平带拼成整条壁

                Mesh sub = perCell ? new Mesh() : null;
                var subMap = perCell ? new Dictionary<int, int>() : null;

                int Sub(int vi)
                {
                    if (sub == null) return vi;
                    int si;
                    if (subMap.TryGetValue(vi, out si)) return si;
                    si = sub.Vertices.Add(mesh.Vertices.Point3dAt(vi));
                    subMap[vi] = si;
                    return si;
                }
                void Tri(int a, int b, int cc)
                {
                    if (a < 0 || b < 0 || cc < 0) return;
                    if (a == b || b == cc || a == cc) return;      // 退化三角面直接丢
                    mesh.Faces.AddFace(a, b, cc);
                    if (sub != null) sub.Faces.AddFace(Sub(a), Sub(b), Sub(cc));
                }
                int Vert(Point2d p2, double dPoly)
                {
                    double gradK = 1.0;
                    if (grad != null && grad.On)
                    {
                        Point3d gp = map.To3d(p2.X, p2.Y);
                        if (gp.IsValid) { double gk = grad.ScaleAt(gp); if (gk > 1e-6) gradK = gk; }
                    }
                    double hh = depth * FanProfile(dPoly, inv, edgeW, shape, cellShape, domePower, gradK);
                    if (fade > 1e-9 && hasFree)
                    {
                        double dFree = c.Free.Distance(p2, fade * 1.2 + step);
                        double tb = dFree / fade;
                        if (tb > 1) tb = 1;
                        hh *= Smoothstep(tb);
                    }
                    Point3d v = map.OffsetPoint(p2.X, p2.Y, hh);
                    if (!v.IsValid) return -1;
                    if (hh < loH) loH = hh;
                    if (hh > hiH) hiH = hh;
                    // 同一个 2D 位置 = 同一个顶点（相邻片/相邻胞元共享 → 拓扑连通、无裂缝）；
                    // 位置相同但高度差得远（退化情形）时才另起一个顶点
                    var key = Key2d(p2, vq);
                    int vi;
                    if (vmap.TryGetValue(key, out vi) && mesh.Vertices.Point3dAt(vi).DistanceTo(v) <= hTol) return vi;
                    vi = mesh.Vertices.Add(v);
                    p2s.Add(p2);
                    vmap[key] = vi;
                    return vi;
                }

                bool allVisible = true;
                for (int j = 0; j < n; j++)
                {
                    if (RadialVisibleStart(region, cg.Center, ring[j], step) > 1e-9)
                    { allVisible = false; if (res != null) res.FanBlocked++; }
                }
                bool guard = !allVisible;      // 凹区域：越出区域的子面丢掉（网格不越界）

                if (res != null)
                {
                    for (int j = 0; j < n; j++)      // 外圈点 = 线框端点（去重登记）
                    {
                        var ok = Key2d(ring[j], vq);
                        if (!outerSeen.ContainsKey(ok))
                        {
                            outerSeen[ok] = res.FanOuterPts.Count;
                            res.FanOuterPts.Add(map.To3d(ring[j].X, ring[j].Y));
                        }
                    }
                }

                for (int j = 0; j < n; j++)
                {
                    int j2 = (j + 1) % n;
                    Point2d P0 = cg.Center, P1 = ring[j], P2 = ring[j2];
                    int g = 1;
                    if (subdivide)
                    {
                        g = (int)Math.Round(m0 * Math.Pow(w.Longest[j] / meanEdge, beta));
                        if (g < 1) g = 1;
                        if (g > MeshPatchGridMax) g = MeshPatchGridMax;
                    }
                    if (g > maxGrid) maxGrid = g;

                    var idx = new int[(g + 1) * (g + 2) / 2];
                    for (int ii = 0; ii <= g; ii++)
                        for (int jj = 0; ii + jj <= g; jj++)
                        {
                            var p2 = new Point2d(P0.X + (P1.X - P0.X) * ii / g + (P2.X - P0.X) * jj / g,
                                                 P0.Y + (P1.Y - P0.Y) * ii / g + (P2.Y - P0.Y) * jj / g);
                            double dp = (ii == 0 && jj == 0) ? inv
                                        : Math.Max(0.0, DistToPolyEdge(ring, p2) - wallHalf);   // 壁厚带内 → 0（平带）
                            idx[PatchGIdx(ii, jj, g)] = Vert(p2, dp);
                        }
                    if (res != null && j == 0 && allVisible && idx[PatchGIdx(0, 0, g)] >= 0)
                        res.FanApexPts.Add(mesh.Vertices.Point3dAt(idx[PatchGIdx(0, 0, g)]));   // 峰顶 = 扇心顶点（含渐变/收平后的真实高度）

                    for (int ii = 0; ii < g; ii++)
                        for (int jj = 0; ii + jj < g; jj++)
                        {
                            int a = idx[PatchGIdx(ii, jj, g)];
                            int b = idx[PatchGIdx(ii + 1, jj, g)];
                            int c1 = idx[PatchGIdx(ii, jj + 1, g)];
                            if (guard && OutsideRegion(p2s, a, b, c1, region)) continue;
                            Tri(a, b, c1);
                            if (ii + jj + 2 <= g)
                            {
                                int d1 = idx[PatchGIdx(ii + 1, jj + 1, g)];
                                if (guard && OutsideRegion(p2s, b, d1, c1, region)) continue;
                                Tri(b, d1, c1);
                            }
                        }
                }

                if (sub != null && sub.Faces.Count > 0)
                {
                    sub.Normals.ComputeNormals();
                    sub.Compact();
                    if (res != null) res.CellMeshes.Add(sub);
                }
            }

            if (res != null) res.FanLayers = maxGrid;

            // 投影面积（判溢出边界用）：每个三角面投到参考平面上求面积后求和（正常 = 区域面积）
            if (res != null && map.Planar)
            {
                double proj = 0;
                for (int f = 0; f < mesh.Faces.Count; f++)
                {
                    MeshFace mf = mesh.Faces[f];
                    if (mf.IsQuad) continue;
                    proj += ProjTriArea(map, mesh.Vertices[mf.A], mesh.Vertices[mf.B], mesh.Vertices[mf.C]);
                }
                res.ProjectedArea = proj;
                res.RegionArea = RingsArea(region);
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            minH = loH == double.MaxValue ? 0 : loH;
            maxH = hiH == double.MinValue ? 0 : hiH;
            return mesh;
        }

        /// <summary>单张网格片每边最多细分 64 段（n×n 张三角面）</summary>
        internal const int MeshPatchGridMax = 64;

        /// <summary>一个胞元建网格所需的中间数据（外圈环 + 每个线框三角面的最长边）</summary>
        sealed class FanCellWork
        {
            public CellGeom Cg;
            public List<Point2d> Ring;
            public double[] Longest;
        }

        /// <summary>重心格点 v(i,j)（i+j≤n）在一维数组里的下标</summary>
        static int PatchGIdx(int i, int j, int n) { return i * (n + 1) - i * (i - 1) / 2 + j; }

        /// <summary>子三角面重心是否落在区域外（凹区域守卫；顶点下标 → 2D 位置表）</summary>
        static bool OutsideRegion(List<Point2d> p2s, int a, int b, int cc, Region2D region)
        {
            if (a < 0 || b < 0 || cc < 0) return true;
            if (a >= p2s.Count || b >= p2s.Count || cc >= p2s.Count) return false;
            Point2d c2 = new Point2d((p2s[a].X + p2s[b].X + p2s[cc].X) / 3.0,
                                     (p2s[a].Y + p2s[b].Y + p2s[cc].Y) / 3.0);
            return !region.Inside(c2);
        }

        /// <summary>凸起形状曲线（平顶 / 穹顶）—— 口径与旧采样网格一致，只是基准距离 dPoly 改由扇形径向给</summary>
        static double FanProfile(double dPoly, double inv, double edgeW, double shape, int cellShape, double domePower, double gradK)
        {
            if (dPoly < 0) dPoly = 0;
            if (cellShape == 1)
            {
                double t = dPoly / inv;
                if (t > 1) t = 1;
                return domePower != 1.0 ? Math.Pow(t, domePower) : t;
            }
            double wCell = edgeW * gradK;
            if (wCell > inv) wCell = inv;                 // 小胞元也要保证中心到满高
            double tt = wCell > 1e-9 ? dPoly / wCell : 1.0;
            if (tt > 1) tt = 1;                           // 平顶
            double f = Smoothstep(tt);
            return shape != 1.0 ? Math.Pow(f, shape) : f;
        }

        /// <summary>射线（中心 → 环点）在区域内可见部分的起点参数（0 = 整条都在区域内）</summary>
        static double RadialVisibleStart(Region2D region, Point2d c, Point2d o, double step)
        {
            var spans = ClipSegToRegion(region, c, o, step);
            if (spans.Count == 0) return 1.0;
            double t = spans[spans.Count - 1][0];          // 最后一段一定贴到环点那一端
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return t;
        }

        /// <summary>
        /// 胞元的外圈环（闭合，CCW）= 「胞元多边形 ∩ 区域」的边界：
        ///   ① 多边形逐边裁到区域（与线框同一套 ClipSegToRegion）→ 区域内的边段，按原顺序串成链；
        ///   ② 链与链之间的缺口（多边形边界在区域外的那几段）用**区域外轮廓**补齐（与线框的「外轮廓」同一批点）；
        /// 于是环上每个点都是线框端点 —— 网格与线框同源。返回 null 表示这段胞元没建出环。
        /// </summary>
        static List<Point2d> BuildCellOuterRing(Region2D region, List<Point2d[]> rings, List<Point2d> poly,
                                                Point2d center, double step, double joinTol, out int arcs)
        {
            arcs = 0;
            if (region == null || poly == null || poly.Count < 3) return null;

            // ① 逐边裁剪（与线框的 AddCellEdge/ClipSegToRegion 完全同一套）
            var segs = new List<Point2d[]>();
            for (int k = 0; k < poly.Count; k++)
            {
                Point2d a = poly[k], b = poly[(k + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                if (dx * dx + dy * dy < 1e-24) continue;
                var spans = ClipSegToRegion(region, a, b, step);
                for (int s = 0; s < spans.Count; s++)
                {
                    double t0 = spans[s][0], t1 = spans[s][1];
                    if (!(t1 - t0 > 1e-9)) continue;
                    segs.Add(new Point2d[]
                    {
                        new Point2d(a.X + dx * t0, a.Y + dy * t0),
                        new Point2d(a.X + dx * t1, a.Y + dy * t1)
                    });
                }
            }
            if (segs.Count == 0) return RingFromOutlineOnly(rings, poly, center, step);

            // 相邻段端点重合（同一条边的相邻段之间不会重合）→ 串成链；断开就另起一条
            var chains = new List<List<Point2d>>();
            var cur = new List<Point2d> { segs[0][0], segs[0][1] };
            for (int i = 1; i < segs.Count; i++)
            {
                if (cur[cur.Count - 1].DistanceTo(segs[i][0]) <= joinTol)
                {
                    if (cur[cur.Count - 1].DistanceTo(segs[i][1]) > joinTol) cur.Add(segs[i][1]);
                }
                else
                {
                    chains.Add(cur);
                    cur = new List<Point2d> { segs[i][0], segs[i][1] };
                }
            }
            chains.Add(cur);

            // 首尾相接（整圈都在区域内）→ 合成一条闭合链
            bool closed = chains.Count > 1 &&
                          chains[0][0].DistanceTo(chains[chains.Count - 1][chains[chains.Count - 1].Count - 1]) <= joinTol;
            if (closed)
            {
                List<Point2d> last = chains[chains.Count - 1];
                for (int i = 1; i < chains[0].Count; i++) last.Add(chains[0][i]);
                chains.RemoveAt(0);
            }

            var ring = new List<Point2d>(chains[0]);
            for (int i = 1; i < chains.Count; i++)
            {
                List<Point2d> arc = RegionArc(rings, ring[ring.Count - 1], chains[i][0], poly, region);
                if (arc != null) { for (int q = 1; q < arc.Count - 1; q++) ring.Add(arc[q]); arcs++; }
                for (int q = 0; q < chains[i].Count; q++) ring.Add(chains[i][q]);
            }
            if (chains.Count > 1)
            {
                List<Point2d> arc = RegionArc(rings, ring[ring.Count - 1], ring[0], poly, region);
                if (arc != null) { for (int q = 1; q < arc.Count - 1; q++) ring.Add(arc[q]); arcs++; }
            }
            else
            {
                // 只有一条链：不闭合说明首尾之间还有一个缺口，用区域外轮廓补上
                if (ring[0].DistanceTo(ring[ring.Count - 1]) > joinTol)
                {
                    List<Point2d> arc = RegionArc(rings, ring[ring.Count - 1], ring[0], poly, region);
                    if (arc != null) { for (int q = 1; q < arc.Count - 1; q++) ring.Add(arc[q]); arcs++; }
                }
                else ring.RemoveAt(ring.Count - 1);      // 闭合链：去掉重复的收尾点
            }

            // 去掉重复/退化的相邻点（补弧处的端点会重合）
            var outp = new List<Point2d>(ring.Count);
            for (int i = 0; i < ring.Count; i++)
            {
                if (outp.Count > 0 && outp[outp.Count - 1].DistanceTo(ring[i]) <= joinTol) continue;
                outp.Add(ring[i]);
            }
            while (outp.Count > 1 && outp[0].DistanceTo(outp[outp.Count - 1]) <= joinTol) outp.RemoveAt(outp.Count - 1);
            if (outp.Count < 3) return null;
            if (PolySignedArea(outp) < 0) outp.Reverse();      // 统一成 CCW（法向朝参考面法向那一侧）
            return outp;
        }

        /// <summary>
        /// 区域外轮廓上「从 a 到 b」的那段边界折线（含 a、b，中间是环顶点，按环的顺序）。
        /// a、b 可能落在同一条环段中间（矩形边界就 4 个角点），所以按「段序号 + 段内参数」定位。
        /// 两条候选弧里优先取：① 整段落在胞元多边形内 ② 行进方向左侧是区域内部；得分相同取点数少的（= 短弧）。
        /// </summary>
        static List<Point2d> RegionArc(List<Point2d[]> rings, Point2d a, Point2d b, List<Point2d> poly, Region2D region)
        {
            if (rings == null || rings.Count == 0) return null;
            List<Point2d> best = null;
            int bestScore = int.MinValue, bestLen = int.MaxValue;
            for (int r = 0; r < rings.Count; r++)
            {
                Point2d[] ring = rings[r];
                int n = ring.Length;
                if (n < 2) continue;
                int sa, sb; double ta, tb;
                if (!RingPos(ring, a, out sa, out ta) || !RingPos(ring, b, out sb, out tb)) continue;
                for (int dir = 0; dir < 2; dir++)
                {
                    var mid = new List<Point2d>();
                    bool direct = (sa == sb) && (dir == 0 ? tb >= ta - 1e-12 : tb <= ta + 1e-12);
                    if (!direct)
                    {
                        int i = sa, guard = 0;
                        bool ok = false;
                        while (guard++ <= n + 1)
                        {
                            i = dir == 0 ? (i + 1) % n : (i - 1 + n) % n;
                            mid.Add(ring[i]);
                            if (i == sb) { ok = true; break; }
                        }
                        if (!ok) continue;
                    }
                    int score = 0;
                    if (ArcInsidePoly(mid, poly)) score += 2;
                    if (ArcLeftInRegion(mid, a, b, region)) score += 1;
                    var cand = new List<Point2d>(mid.Count + 2) { a };
                    cand.AddRange(mid);
                    cand.Add(b);
                    if (score > bestScore || (score == bestScore && mid.Count < bestLen))
                    { bestScore = score; bestLen = mid.Count; best = cand; }
                }
            }
            return best;
        }

        /// <summary>环上离 p 最近的位置：段序号 + 段内参数（0..1）</summary>
        static bool RingPos(Point2d[] ring, Point2d p, out int seg, out double t)
        {
            seg = -1; t = 0;
            int n = ring.Length;
            if (n < 2) return false;
            double bd = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Point2d a = ring[i], b = ring[(i + 1) % n];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double l2 = dx * dx + dy * dy;
                double tt = l2 > 1e-20 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2 : 0;
                if (tt < 0) tt = 0; else if (tt > 1) tt = 1;
                double ex = p.X - (a.X + tt * dx), ey = p.Y - (a.Y + tt * dy);
                double d = Math.Sqrt(ex * ex + ey * ey);
                if (d < bd) { bd = d; seg = i; t = tt; }
            }
            return seg >= 0;
        }

        /// <summary>弧的中间点是否都落在胞元多边形内（胞元多边形是凸的）</summary>
        static bool ArcInsidePoly(List<Point2d> mid, List<Point2d> poly)
        {
            if (mid.Count == 0) return true;
            int step = Math.Max(1, mid.Count / 6);
            int bad = 0, tot = 0;
            for (int i = 0; i < mid.Count; i += step) { tot++; if (!InsideConvexPoly(poly, mid[i])) bad++; }
            return tot == 0 || bad == 0;
        }

        /// <summary>弧的行进方向左侧是不是区域内部（区域内部在左侧 = 这条弧是交集边界，不是往区域外绕）</summary>
        static bool ArcLeftInRegion(List<Point2d> mid, Point2d a, Point2d b, Region2D region)
        {
            if (region == null) return false;
            var pts = new List<Point2d>(mid.Count + 2) { a };
            pts.AddRange(mid);
            pts.Add(b);
            int step = Math.Max(1, pts.Count / 6);
            int bad = 0, tot = 0;
            for (int i = 0; i + 1 < pts.Count; i += step)
            {
                Point2d p0 = pts[i], p1 = pts[i + 1];
                double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (!(len > 1e-12)) continue;
                double d = Math.Min(len * 0.25, 1e-3);          // 往左偏一点（局部法向）
                double nx = -dy / len, ny = dx / len;
                tot++;
                if (!region.Inside(new Point2d((p0.X + p1.X) * 0.5 + nx * d, (p0.Y + p1.Y) * 0.5 + ny * d))) bad++;
            }
            return tot > 0 && bad == 0;
        }

        /// <summary>多边形有向面积（CCW 为正）</summary>
        static double PolySignedArea(List<Point2d> poly)
        {
            double s2 = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a = poly[i], b = poly[(i + 1) % poly.Count];
                s2 += a.X * b.Y - b.X * a.Y;
            }
            return s2 * 0.5;
        }

        /// <summary>胞元多边形的边全在区域外（区域的一个角戳进胞元里）：外圈环 = 区域轮廓落在胞元内的那一段</summary>
        static List<Point2d> RingFromOutlineOnly(List<Point2d[]> rings, List<Point2d> poly, Point2d center, double step)
        {
            if (rings == null) return null;
            List<Point2d> best = null;
            for (int r = 0; r < rings.Count; r++)
            {
                Point2d[] ring = rings[r];
                int n = ring.Length;
                if (n < 3) continue;
                var run = new List<Point2d>();
                for (int i = 0; i < n * 2; i++)
                {
                    Point2d p = ring[i % n];
                    if (InsideConvexPoly(poly, p)) run.Add(p);
                    else
                    {
                        if (run.Count >= 3 && (best == null || run.Count > best.Count)) best = new List<Point2d>(run);
                        run.Clear();
                    }
                    if (run.Count >= n) { best = new List<Point2d>(run); break; }
                }
                if (run.Count >= 3 && (best == null || run.Count > best.Count)) best = new List<Point2d>(run);
            }
            if (best != null && best.Count >= 3)
            {
                // 用直线把首尾接起来（交点近似取首尾点）—— 这块区域本来就只是区域轮廓的一小段
                if (PolySignedArea(best) < 0) best.Reverse();
                return best;
            }
            return null;
        }

        /// <summary>2D 位置的量化键（同一个位置 = 同一个顶点；整数对，不做哈希 —— 哈希撞了会把两个点误当同一个）</summary>
        static (long, long) Key2d(Point2d p, double q)
        {
            if (!(q > 0)) q = 1e-6;
            return ((long)Math.Round(p.X / q), (long)Math.Round(p.Y / q));
        }

        /// <summary>三角形按参考平面投到 2D 后的面积（|shoelace| / 2）</summary>
        static double ProjTriArea(SurfaceMap map, Point3d a, Point3d b, Point3d c)
        {
            Point2d qa = map.To2d(a), qb = map.To2d(b), qc = map.To2d(c);
            double s2 = (qb.X - qa.X) * (qc.Y - qa.Y) - (qb.Y - qa.Y) * (qc.X - qa.X);
            return Math.Abs(s2) * 0.5;
        }

        // ============================================================ 胞元线框（真直线）

        /// <summary>
        /// 胞元线框：**按种子对（i&lt;j）取共享边** —— 每条 Voronoi 边只生成一次（结构上去重，不靠浮点容差）。
        /// 共享边 = 两个种子的中垂线夹在两个胞元多边形里的那一段（凸多边形上就是中垂线的交线段）。
        /// 再按区域裁剪：整段在外丢并计数；跨边界在边界处截断（截断点仍在同一条直线上，不折）。
        /// 最后把区域外轮廓本身也出出来（贴着边界的胞元边就是边界本身，不是台阶）。
        /// </summary>
        static void BuildWireframe(VoronoiFaceResult res, Region2D region, SurfaceMap map,
                                   CellGeom[] cells, List<Point2d> seeds, double cell, double step, double wall, double depth)
        {
            if (res == null || cells == null || seeds == null || seeds.Count < 2) return;
            double q = SegQuantum(region);                  // 端点吸附/去重的统一格（相对模型尺度）
            var seen = new HashSet<long>();
            var arr = seeds.ToArray();

            // 有壁厚：每条胞元边不再是共享边（两侧各自内缩）→ 按「每个胞元的内缩多边形」逐边输出
            if (wall > 1e-9)
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    CellGeom cg = cells[i];
                    if (cg == null) continue;
                    List<Point2d> poly = cg.InsetPoly != null && cg.InsetPoly.Count >= 3 ? cg.InsetPoly : cg.Poly;
                    if (poly == null || poly.Count < 3) continue;
                    for (int k = 0; k < poly.Count; k++)
                        AddCellEdge(res, seen, region, map, poly[k], poly[(k + 1) % poly.Count], step, q);
                }
                AddSpokes(res, region, map, cells, step, depth);
                return;
            }

            // 附近种子索引（和胞元裁剪同一套：4×胞元尺寸内的中垂线才可能切到胞元）
            double near = Math.Max(cell * 4.0, 1e-6);
            double gc = Math.Max(cell, 1e-9);
            var grid = new Dictionary<long, List<int>>(arr.Length * 2);
            for (int i = 0; i < arr.Length; i++)
            {
                long k = HashKey((int)Math.Floor(arr[i].X / gc), (int)Math.Floor(arr[i].Y / gc));
                List<int> l;
                if (!grid.TryGetValue(k, out l)) { l = new List<int>(4); grid[k] = l; }
                l.Add(i);
            }

            int nr = (int)Math.Ceiling(near / gc);
            double r2 = near * near;
            for (int i = 0; i < arr.Length; i++)
            {
                CellGeom ci = cells[i];
                if (ci == null || ci.Poly.Count < 3) continue;
                int gx = (int)Math.Floor(arr[i].X / gc), gy = (int)Math.Floor(arr[i].Y / gc);
                for (int ax = gx - nr; ax <= gx + nr; ax++)
                    for (int ay = gy - nr; ay <= gy + nr; ay++)
                    {
                        List<int> l;
                        if (!grid.TryGetValue(HashKey(ax, ay), out l)) continue;
                        for (int k = 0; k < l.Count; k++)
                        {
                            int j = l[k];
                            if (j <= i) continue;                       // 每条边只在 i<j 时生成一次
                            double dx = arr[j].X - arr[i].X, dy = arr[j].Y - arr[i].Y;
                            if (dx * dx + dy * dy > r2) continue;
                            double len = Math.Sqrt(dx * dx + dy * dy);
                            if (!(len > 1e-12)) continue;
                            // 中垂线：过两 seed 中点，方向垂直于 seed 连线
                            var mid = new Point2d((arr[i].X + arr[j].X) * 0.5, (arr[i].Y + arr[j].Y) * 0.5);
                            double tx = -dy / len, ty = dx / len;
                            double a0, a1, b0, b1;
                            if (!LineClipPoly(mid, tx, ty, ci.Poly, out a0, out a1)) continue;      // 不相邻
                            CellGeom cj = cells[j];
                            if (cj == null || cj.Poly.Count < 3) continue;
                            if (!LineClipPoly(mid, tx, ty, cj.Poly, out b0, out b1)) continue;      // 两侧都要夹住
                            double t0 = Math.Max(a0, b0), t1 = Math.Min(a1, b1);
                            if (!(t1 > t0)) continue;                                              // 公共段为空
                            res.WireEdgeSeen++;
                            var pa = new Point2d(mid.X + tx * t0, mid.Y + ty * t0);
                            var pb = new Point2d(mid.X + tx * t1, mid.Y + ty * t1);
                            AddCellEdge(res, seen, region, map, pa, pb, step, q);
                        }
                    }
            }

            // 区域外轮廓（只出闭合环）：贴边的胞元边就是它，不是台阶
            for (int r = 0; r < region.Rings.Count; r++)
            {
                List<Point2d> ring = SimplifyRing(region.Rings[r]);
                if (ring.Count < 3) continue;
                for (int k = 0; k < ring.Count; k++)
                {
                    Point2d a = ring[k], b = ring[(k + 1) % ring.Count];
                    if (AddSeg(res, seen, map, a, b, q)) res.WireOutline++;
                }
            }

            AddSpokes(res, region, map, cells, step, depth);

        }

        /// <summary>端点吸附/去重的统一格：按模型尺度取（1e-6×对角线，至少 1e-6 mm）</summary>
        static double SegQuantum(Region2D region)
        {
            double diag = region != null ? region.Diagonal : 1.0;
            return Math.Max(1e-6, diag * 1e-6);
        }

        /// <summary>中垂线（过 p、方向 (ux,uy)）与凸多边形的交线段参数区间；不相交返回 false</summary>
        static bool LineClipPoly(Point2d p, double ux, double uy, List<Point2d> poly, out double t0, out double t1)
        {
            t0 = double.MaxValue; t1 = double.MinValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a = poly[i], b = poly[(i + 1) % poly.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double den = ux * ey - uy * ex;
                if (Math.Abs(den) < 1e-14) continue;                  // 与这条边平行
                double ax = a.X - p.X, ay = a.Y - p.Y;
                double t = (ax * ey - ex * ay) / den;                 // 沿中垂线的距离
                double s = (uy * ax - ux * ay) / den;                 // 在边上的参数
                if (s < -1e-9 || s > 1 + 1e-9) continue;
                if (t < t0) t0 = t;
                if (t > t1) t1 = t;
            }
            return t1 > t0;
        }

        /// <summary>去掉共线的中间点（矩形外轮廓 800 个采样点 → 4 个角），让外轮廓也是干净直线</summary>
        static List<Point2d> SimplifyRing(Point2d[] ring)
        {
            var outp = new List<Point2d>();
            if (ring == null || ring.Length < 3) return outp;
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                Point2d prev = ring[(i + n - 1) % n], cur = ring[i], next = ring[(i + 1) % n];
                double ax = cur.X - prev.X, ay = cur.Y - prev.Y;
                double bx = next.X - cur.X, by = next.Y - cur.Y;
                double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                if (la < 1e-12 || lb < 1e-12) continue;
                // 方向几乎一致 → 中间点去掉（阈值 1e-6 rad 量级，只吃采样噪声，不吃真拐角）
                if (Math.Abs(ax * by - ay * bx) / (la * lb) < 1e-6) continue;
                outp.Add(cur);
            }
            return outp;
        }

        /// <summary>
        /// 把一条胞元边按区域裁剪后加进线框（0/1/2 段）。
        /// 每次「考虑」只会记一个结果：出段（WireEmitted）/ 去重（WireDedup）/ 丢弃（WireDropped）—— 账目自洽。
        /// </summary>
        /// <summary>
        /// 中心连线：每个胞元的每个（内缩后）边界顶点 → 该胞元中心。
        /// 中心点在**峰高**（沿法向抬到设定深度），边界点在基准面（h=0）—— 所以连线是「峰顶 → 边界」的斜线。
        /// ⚠ 每条连线都要**按区域裁剪**（细胞的凸多边形可能伸到边界外，直接连会画出区域外的长线）。
        /// </summary>
        static void AddSpokes(VoronoiFaceResult res, Region2D region, SurfaceMap map, CellGeom[] cells, double step, double depth)
        {
            if (res == null || cells == null) return;
            for (int i = 0; i < cells.Length; i++)
            {
                CellGeom cg = cells[i];
                if (cg == null || !cg.Center.IsValid) continue;
                List<Point2d> poly = cg.InsetPoly != null && cg.InsetPoly.Count >= 3 ? cg.InsetPoly : cg.Poly;
                if (poly == null || poly.Count < 3) continue;
                for (int k = 0; k < poly.Count; k++)
                {
                    Point2d v = poly[k];
                    Point2d c = cg.Center;
                    var spans = ClipSegToRegion(region, c, v, Math.Max(step, 1e-6));
                    for (int s = 0; s < spans.Count; s++)
                    {
                        double t0 = spans[s][0], t1 = spans[s][1];
                        if (!(t1 - t0 > 1e-9)) continue;
                        var a2 = new Point2d(c.X + (v.X - c.X) * t0, c.Y + (v.Y - c.Y) * t0);
                        var b2 = new Point2d(c.X + (v.X - c.X) * t1, c.Y + (v.Y - c.Y) * t1);
                        // 近端（靠近中心的那头）在峰高、远端在基准面：按参数线性插高度，连线才贴在鼓包上
                        double hA = depth * (1.0 - t0), hB = depth * (1.0 - t1);
                        Point3d pa = map.OffsetPoint(a2.X, a2.Y, hA);
                        Point3d pb = map.OffsetPoint(b2.X, b2.Y, hB);
                        if (!pa.IsValid || !pb.IsValid) continue;
                        if (pa.DistanceTo(pb) < 1e-9) continue;
                        res.Spokes.Add(new LineCurve(pa, pb));
                        res.WireSpokes++;
                    }
                }
            }
        }

        static void AddCellEdge(VoronoiFaceResult res, HashSet<long> seen, Region2D region, SurfaceMap map,
                                Point2d a, Point2d b, double step, double q)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (!(len > 1e-12)) { res.WireDropped++; return; }

            bool ina = region.Inside(a), inb = region.Inside(b);
            if (ina && inb)
            {
                // 两端都在区域内：抽几个中间点确认没从凹口里穿出去（胞元边很短，采样够用）
                int n = Math.Min(8, Math.Max(2, (int)Math.Ceiling(len / Math.Max(step, 1e-6))));
                bool all = true;
                for (int k = 1; k < n && all; k++)
                {
                    double t = k / (double)n;
                    if (!region.Inside(new Point2d(a.X + dx * t, a.Y + dy * t))) all = false;
                }
                if (all)
                {
                    if (AddSeg(res, seen, map, a, b, q)) res.WireEmitted++;
                    else res.WireDedup++;
                    return;
                }
            }

            // 跨边界（或从凹口穿出去）：按区域把线段切成若干段，切点用二分找到边界上（切点仍在这条直线上）
            var spans = ClipSegToRegion(region, a, b, step);
            if (spans.Count == 0) { res.WireDropped++; return; }
            bool any = false, dup = false;
            for (int k = 0; k < spans.Count; k++)
            {
                var p0 = new Point2d(a.X + dx * spans[k][0], a.Y + dy * spans[k][0]);
                var p1 = new Point2d(a.X + dx * spans[k][1], a.Y + dy * spans[k][1]);
                if (p0.DistanceTo(p1) < 1e-12) continue;
                res.WireSnapped += 2;      // 这一段的两端都是裁到边界上的
                if (AddSeg(res, seen, map, p0, p1, q)) any = true; else dup = true;
            }
            if (any) res.WireEmitted++;
            else if (dup) res.WireDedup++;
            else res.WireDropped++;
        }

        /// <summary>线段按区域裁剪：返回落在区域内的参数区间 [t0,t1]（边界交点用二分求，落在边界线上）</summary>
        static List<double[]> ClipSegToRegion(Region2D region, Point2d a, Point2d b, double step)
        {
            var outp = new List<double[]>();
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int n = Math.Min(64, Math.Max(8, (int)Math.Ceiling(len / Math.Max(step * 0.5, 1e-6))));

            bool prevIn = region.Inside(a);
            double prevT = 0, startT = prevIn ? 0 : -1;
            for (int k = 1; k <= n; k++)
            {
                double t = k / (double)n;
                bool inNow = region.Inside(new Point2d(a.X + dx * t, a.Y + dy * t));
                if (inNow != prevIn)
                {
                    double lo = prevT, hi = t;
                    for (int it = 0; it < 30; it++)
                    {
                        double mid = (lo + hi) * 0.5;
                        bool im = region.Inside(new Point2d(a.X + dx * mid, a.Y + dy * mid));
                        if (im == prevIn) lo = mid; else hi = mid;
                    }
                    double cross = (lo + hi) * 0.5;
                    if (inNow) startT = cross;
                    else if (startT >= 0) { outp.Add(new double[] { startT, cross }); startT = -1; }
                }
                prevIn = inNow; prevT = t;
            }
            if (startT >= 0) outp.Add(new double[] { startT, 1.0 });
            return outp;
        }

        /// <summary>
        /// 加一条直线段：端点**先吸附到统一量化格 q**（同一条边从两侧来的时候坐标完全一致、键才稳），
        /// 再用「两端点量化后的无序点对」去重；吸附后重合/退化的直接丢。
        /// </summary>
        static bool AddSeg(VoronoiFaceResult res, HashSet<long> seen, SurfaceMap map, Point2d a, Point2d b, double q)
        {
            Point3d pa = map.To3d(a.X, a.Y);
            Point3d pb = map.To3d(b.X, b.Y);
            if (!pa.IsValid || !pb.IsValid) return false;
            if (!(q > 0)) q = 1e-6;
            pa = new Point3d(Math.Round(pa.X / q) * q, Math.Round(pa.Y / q) * q, Math.Round(pa.Z / q) * q);
            pb = new Point3d(Math.Round(pb.X / q) * q, Math.Round(pb.Y / q) * q, Math.Round(pb.Z / q) * q);
            if (pa.DistanceTo(pb) < q) return false;          // 吸附后重合/退化
            long ka = QuantKey(pa, q), kb = QuantKey(pb, q);
            if (ka == kb) return false;
            long key = ka < kb ? (ka * 1000003L) ^ kb : (kb * 1000003L) ^ ka;
            if (!seen.Add(key)) return false;
            res.Wireframe.Add(new LineCurve(pa, pb));
            return true;
        }

        /// <summary>四边形按参考平面投到 2D 后的面积（|shoelace| / 2）</summary>
        static double ProjQuadArea(SurfaceMap map, Point3d a, Point3d b, Point3d c, Point3d d)
        {
            Point2d qa = map.To2d(a), qb = map.To2d(b), qc = map.To2d(c), qd = map.To2d(d);
            double s2 = qa.X * qb.Y - qb.X * qa.Y
                      + qb.X * qc.Y - qc.X * qb.Y
                      + qc.X * qd.Y - qd.X * qc.Y
                      + qd.X * qa.Y - qa.X * qd.Y;
            return Math.Abs(s2) * 0.5;
        }

        /// <summary>区域多边形面积：各环 |shoelace| 之和（按外轮廓生成时就是外轮廓面积）</summary>
        static double RingsArea(Region2D region)
        {
            double sum = 0;
            for (int r = 0; r < region.Rings.Count; r++)
            {
                Point2d[] ring = region.Rings[r];
                if (ring == null || ring.Length < 3) continue;
                double s2 = 0;
                for (int i = 0; i < ring.Length; i++)
                {
                    Point2d p = ring[i], q = ring[(i + 1) % ring.Length];
                    s2 += p.X * q.Y - q.X * p.Y;
                }
                sum += Math.Abs(s2) * 0.5;
            }
            return sum;
        }

        /// <summary>
        /// 接缝平滑的混合权重：接缝上 0.5（两边各一半 → 位置一致），离接缝 blendW 以外 0。
        /// 同时返回最近那条共享边的邻面法向。
        /// </summary>
        /// <summary>
        /// 接缝平滑：把「附近所有共享边」的邻面法向按距离加权混进来（不是只取最近那条）。
        /// 角点处两条共享边等距，如果只取一条，两个面可能各挑一条、方向对不上 → 接缝裂开 3mm；
        /// 加权求和是**对称**的：同一位置两个面得到同一个混合法向，接缝就严丝合缝。
        /// </summary>
        static Vector3d SeamBlendNormal(List<SharedEdge> shared, Point2d p, double blendW, Vector3d nThis)
        {
            if (blendW <= 1e-9) return nThis;
            Vector3d sum = nThis;
            double total = 1.0;
            for (int i = 0; i < shared.Count; i++)
            {
                SharedEdge se = shared[i];
                if (se.NbrNormal == Vector3d.Zero) continue;
                double d = se.DistanceTo(p, blendW);
                if (d > blendW) continue;
                double t = d / blendW;
                double sm = t * t * (3 - 2 * t);
                double wt = 1.0 - sm;              // 贴到边上 1（与「本面」等权 → 正好 50:50）、到 blendW 处 0
                if (wt <= 1e-9) continue;
                sum = sum + se.NbrNormal * wt;
                total += wt;
            }
            if (total <= 1e-9) return nThis;
            Vector3d r = sum / total;
            if (!r.IsValid || !r.Unitize()) return nThis;
            return r;
        }

        /// <summary>
        /// 接缝处的斜接：两个面各自沿自己的法向偏移，在共享边上会叉开一条缝
        /// （90° 缝在 3mm 深度下差 4.24mm）。把边界点放到「两个偏移平面的交线」上，
        /// 两边算出的位置就重合，整片纹理在接缝处是严丝合缝的。
        /// 交线：P + h/(1+n1·n2) · (n1 + n2)
        /// </summary>
        static Point3d MiterPoint(SurfaceMap map, List<SharedEdge> shared, Point2d p, Point3d P, double hh, double step,
                                  out bool hit, out double minDist)
        {
            hit = false; minDist = double.MaxValue;
            if (Math.Abs(hh) < 1e-12) return P;
            Vector3d n1 = map.NormalAt(p.X, p.Y);

            // 容差内的所有共享边（角点处会同时命中两条）
            double tol = step * 0.6;
            SharedEdge best = null;
            double bestD = tol;
            var hits = new List<SharedEdge>(3);
            for (int i = 0; i < shared.Count; i++)
            {
                SharedEdge se = shared[i];
                if (se.NbrNormal == Vector3d.Zero) continue;
                double d = se.DistanceTo(p, tol);
                if (d < minDist) minDist = d;
                if (d < bestD) { bestD = d; best = se; }
                if (d < tol) hits.Add(se);
            }
            if (best == null) return P + n1 * hh;
            hit = true;

            // 角点：解「几个偏移平面的交点」。三面各自算出的是同一个点，角上也严丝合缝；
            // 只用一条边的两平面公式在角上会和邻面差一截（立方体角上是 3√2）。
            if (hits.Count >= 2)
            {
                var ns = new List<Vector3d>(3);
                ns.Add(n1);
                for (int i = 0; i < hits.Count && ns.Count < 3; i++)
                {
                    Vector3d nn = hits[i].NbrNormal;
                    bool dup = false;
                    // 法向几乎同向（例如整圈面的接缝处邻面就是自己）就当重复，别拿去解三平面 ——
                    // 两个近乎平行的平面求交会把点甩到很远的地方
                    for (int k = 0; k < ns.Count; k++) if (Math.Abs(Dot(ns[k], nn)) > 0.98) dup = true;
                    if (!dup) ns.Add(nn);
                }
                Point3d corner;
                if (ns.Count >= 3 && SolveOffsetPlanes(P, ns, hh, out corner) && corner.IsValid &&
                    corner.DistanceTo(P) <= Math.Abs(hh) * 2.2 + step)
                    return corner;
            }

            double cc = Dot(n1, best.NbrNormal);
            if (cc < -0.9) cc = -0.9;
            if (cc > 0.999) cc = 0.999;
            Vector3d m = (n1 + best.NbrNormal) / (1 + cc);
            Point3d v = P + m * hh;
            return v.IsValid ? v : P + n1 * hh;
        }

        /// <summary>解 (x − P)·n_i = hh（i = 0..2）—— 几个偏移平面的交点</summary>
        static bool SolveOffsetPlanes(Point3d P, List<Vector3d> ns, double hh, out Point3d x)
        {
            x = Point3d.Unset;
            if (ns.Count < 3) return false;
            Vector3d a = ns[0], b = ns[1], c = ns[2];
            double det = a.X * (b.Y * c.Z - b.Z * c.Y)
                       - a.Y * (b.X * c.Z - b.Z * c.X)
                       + a.Z * (b.X * c.Y - b.Y * c.X);
            if (Math.Abs(det) < 0.25) return false;   // 三面法向太接近：解会飞出去，交给两平面公式
            double r0 = P.X * a.X + P.Y * a.Y + P.Z * a.Z + hh;
            double r1 = P.X * b.X + P.Y * b.Y + P.Z * b.Z + hh;
            double r2 = P.X * c.X + P.Y * c.Y + P.Z * c.Z + hh;
            double dx = r0 * (b.Y * c.Z - b.Z * c.Y) - a.Y * (r1 * c.Z - b.Z * r2) + a.Z * (r1 * c.Y - b.Y * r2);
            double dy = a.X * (r1 * c.Z - b.Z * r2) - r0 * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * r2 - r1 * c.X);
            double dz = a.X * (b.Y * r2 - r1 * c.Y) - a.Y * (b.X * r2 - r1 * c.X) + r0 * (b.X * c.Y - b.Y * c.X);
            var v = new Point3d(dx / det, dy / det, dz / det);
            if (!v.IsValid) return false;
            x = v;
            return true;
        }

        // ---------------------------------------------------------------- 合并成「整体一张面」

        /// <summary>
        /// 把各面网格合并成一张连续网格：**只在接缝上顺接（共面/相切）的位置**焊接顶点 ——
        /// 这些点在两个面里算出来是同一个位置（公共采样点 + 斜接/混合），焊完就是整片连续面，
        /// 法向取平均，看不出接缝。输入本身是折痕的地方不焊（折痕仍是硬边、保持锐利）。
        /// </summary>
        public static Mesh MergeWeld(List<VoronoiFaceResult> results, double posTol)
        {
            var merged = new Mesh();
            if (results == null) return merged;
            for (int i = 0; i < results.Count; i++)
            {
                Mesh m = results[i] != null ? results[i].Mesh : null;
                if (m != null && m.Faces.Count > 0) merged.Append(m);
            }
            if (merged.Faces.Count == 0) return merged;

            // 可焊位置（量化成格子 key，避免浮点误差）
            if (!(posTol > 0)) posTol = 1e-6;
            var weldable = new HashSet<long>();
            for (int i = 0; i < results.Count; i++)
            {
                VoronoiFaceResult r = results[i];
                if (r == null || r.SeamWeldPos == null) continue;
                for (int k = 0; k < r.SeamWeldPos.Count; k++)
                {
                    Point3d p = r.SeamWeldPos[k];
                    if (!p.IsValid) continue;
                    weldable.Add(QuantKey(p, posTol));
                }
            }

            Mesh w = WeldCoincident(merged, posTol, weldable);
            try { w.Normals.ComputeNormals(); } catch { }
            try { w.Compact(); } catch { }
            return w;
        }

        // ============================================================ 一键平滑

        /// <summary>
        /// 一键平滑：把合并焊接后的整张网格丢给 Rhino 的 QuadRemesh（自适应四边面重构）——
        /// 「平滑度」映射到自适应大小（0~100 百分比），「细分程度」映射到目标四边面数量；结果默认转细分物件。
        /// 不勾选（Smooth = false）直接返回 Ok = false，不产生任何几何。
        /// </summary>
        /// <summary>
        /// 一键平滑：把「线框三角面细分出来的网格」直接转成**细分曲面（SubD）** ——
        /// 细分程度 / 平滑度 已经在建网格时用掉了（片内细分档位），这里只做「网格 → 细分曲面」，不再重构拓扑。
        /// ⚠ 不要用 QuadRemesh：它会重排拓扑（出四边面），线框结构全丢（用户实测口径：「拓扑结构完全不对」）。
        /// </summary>
        public static VoronoiSmoothResult ApplySmooth(Mesh merged, VoronoiSettings s)
        {
            var res = new VoronoiSmoothResult();
            if (s == null || !s.Smooth) { res.Note = "未勾选一键平滑"; return res; }
            if (merged == null || merged.Faces.Count == 0) { res.Note = "平滑：没有可转细分曲面的网格"; return res; }
            try
            {
                SubD sd = null;
                try { sd = SubD.CreateFromMesh(merged); } catch { sd = null; }
                res.Ok = true;
                res.Mesh = merged;
                res.SubD = sd;
                res.Faces = merged.Faces.Count;
                res.Note = sd != null
                    ? string.Format(CultureInfo.InvariantCulture, "一键平滑：{0} 个面的网格 → 细分曲面（SubD）", merged.Faces.Count)
                    : string.Format(CultureInfo.InvariantCulture, "一键平滑：细分曲面转换失败（按 {0} 个面的网格输出）", merged.Faces.Count);
            }
            catch (Exception ex) { res.Note = "细分曲面转换失败：" + ex.Message; }
            return res;
        }

        // ============================================================ 输出

        internal const string LayerWire = "泰森多边形纹-线框";
        internal const string LayerFace = "泰森多边形纹-面";
        internal const string LayerSmooth = "泰森多边形纹-平滑";

        internal static int EnsureLayer(RhinoDoc doc, string name, Color col)
        {
            foreach (Layer l in doc.Layers)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
            var nl = new Layer();
            nl.Name = name;
            nl.Color = col;
            return doc.Layers.Add(nl);
        }

        /// <summary>
        /// 把结果写进文档。**输出二选一，不混着给**：
        ///   线框 → 只写曲线（图层「泰森多边形纹-线框」）；
        ///   网格面 → 只写合并焊接后的那张连续网格（图层「泰森多边形纹-面」，**不带线框**）；
        ///   勾了「一键平滑」再多写一份平滑结果（图层「泰森多边形纹-平滑」）。
        /// </summary>
        internal static void AddToDocument(RhinoDoc doc, List<VoronoiFaceResult> results, Mesh merged,
            VoronoiSmoothResult smooth, int outputMode,
            out int curveCount, out int meshCount, out int smoothCount)
        {
            curveCount = 0; meshCount = 0; smoothCount = 0;
            if (outputMode == 0)
            {
                int lw = EnsureLayer(doc, LayerWire, Color.FromArgb(96, 108, 132));
                var aw = new ObjectAttributes();
                aw.LayerIndex = lw;
                for (int i = 0; i < results.Count; i++)
                {
                    List<Curve> wire = results[i] != null ? results[i].Wireframe : null;
                    if (wire != null)
                        for (int k = 0; k < wire.Count; k++)
                        {
                            if (wire[k] == null) continue;
                            if (doc.Objects.AddCurve(wire[k], aw) != Guid.Empty) curveCount++;
                        }
                    // 中心连线（峰顶 → 边界）也一起落盘
                    List<Curve> sp = results[i] != null ? results[i].Spokes : null;
                    if (sp != null)
                        for (int k = 0; k < sp.Count; k++)
                        {
                            if (sp[k] == null) continue;
                            if (doc.Objects.AddCurve(sp[k], aw) != Guid.Empty) curveCount++;
                        }
                    // 中心点也落盘（用户口径：中心点要能看见）
                    List<Point3d> cts = results[i] != null ? results[i].CellCenters : null;
                    if (cts != null)
                        for (int k = 0; k < cts.Count; k++)
                        {
                            if (!cts[k].IsValid) continue;
                            try { doc.Objects.AddPoint(cts[k], aw); } catch { }
                        }
                }
                return;
            }

            int lf = EnsureLayer(doc, LayerFace, Color.FromArgb(200, 160, 90));
            var af = new ObjectAttributes();
            af.LayerIndex = lf;
            // 勾了「一键平滑」→ 只写细分曲面（SubD）；不勾 → 只写网格（用户口径：要细分曲面，不是网格）
            if (merged != null && merged.Faces.Count > 0 && !(smooth != null && smooth.Ok))
            {
                if (doc.Objects.AddMesh(merged, af) != Guid.Empty) meshCount++;
            }
            if (smooth != null && smooth.Ok)
            {
                int ls = EnsureLayer(doc, LayerSmooth, Color.FromArgb(64, 148, 208));
                var asm = new ObjectAttributes();
                asm.LayerIndex = ls;
                if (smooth.SubD != null)
                {
                    if (doc.Objects.AddSubD(smooth.SubD, asm) != Guid.Empty) smoothCount++;
                }
                else if (smooth.Mesh != null && smooth.Mesh.Faces.Count > 0)
                {
                    if (doc.Objects.AddMesh(smooth.Mesh, asm) != Guid.Empty) smoothCount++;
                }
            }
        }

        static long Key3(int x, int y, int z)
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }

        static long QuantKey(Point3d p, double q)
        {
            return Key3((int)Math.Floor(p.X / q), (int)Math.Floor(p.Y / q), (int)Math.Floor(p.Z / q));
        }

        /// <summary>重合顶点焊接（返回新网格；未重合的顶点索引保持不变）</summary>
        static Mesh WeldCoincident(Mesh m, double posTol, HashSet<long> weldable)
        {
            int vc = m.Vertices.Count, fc = m.Faces.Count;
            if (vc == 0 || fc == 0) return m;
            if (weldable == null || weldable.Count == 0) return m;

            // 1) 量化位置 -> 计数：只有「多个顶点落在同一格」且该格可焊的才是候选（其余直接跳过，快）
            var cnt = new Dictionary<long, int>(vc);
            var keyOf = new long[vc];
            for (int i = 0; i < vc; i++)
            {
                Point3d p = m.Vertices[i];
                long k = QuantKey(p, posTol);
                keyOf[i] = k;
                int c;
                cnt.TryGetValue(k, out c);
                cnt[k] = c + 1;
            }
            var groups = new Dictionary<long, List<int>>();
            for (int i = 0; i < vc; i++)
            {
                if (cnt[keyOf[i]] < 2 || !weldable.Contains(keyOf[i])) continue;
                List<int> l;
                if (!groups.TryGetValue(keyOf[i], out l)) { l = new List<int>(4); groups[keyOf[i]] = l; }
                l.Add(i);
            }
            if (groups.Count == 0) return m;     // 没有要焊的点：原样返回

            // 2) 重建（重复项指到代表元；退化的面按三角形补，再退化就丢）
            var map = new int[vc];
            for (int i = 0; i < vc; i++) map[i] = i;
            foreach (KeyValuePair<long, List<int>> kv in groups)
            {
                List<int> g = kv.Value;
                int keep = g[0];
                for (int i = 1; i < g.Count; i++) map[g[i]] = keep;
            }

            var outMesh = new Mesh();
            for (int i = 0; i < vc; i++) outMesh.Vertices.Add(m.Vertices[i]);
            for (int f = 0; f < fc; f++)
            {
                MeshFace face = m.Faces[f];
                if (face.IsQuad)
                {
                    int a = map[face.A], b = map[face.B], c = map[face.C], d = map[face.D];
                    if (a == b) { if (a != c && a != d && c != d) outMesh.Faces.AddFace(a, c, d); }
                    else if (b == c) { if (a != b && b != d && a != d) outMesh.Faces.AddFace(a, b, d); }
                    else if (c == d) { if (a != b && a != c && b != c) outMesh.Faces.AddFace(a, b, c); }
                    else if (d == a) { if (a != b && b != c && a != c) outMesh.Faces.AddFace(a, b, c); }
                    else outMesh.Faces.AddFace(a, b, c, d);
                }
                else
                {
                    int a = map[face.A], b = map[face.B], c = map[face.C];
                    if (a == b || b == c || a == c) continue;
                    outMesh.Faces.AddFace(a, b, c);
                }
            }
            return outMesh;   // 未用到的重复顶点由调用方 Compact() 清掉
        }
    }
}
