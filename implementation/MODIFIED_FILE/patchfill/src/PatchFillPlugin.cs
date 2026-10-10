using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;   // [assembly: Guid] 需要（否则 Guid 解析成 System.Guid）
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using Rhino.PlugIns;

// Rhino 按「程序集级」[assembly: Guid] 识别插件 ID；只写类级 [Guid] 会被登记成全零 GUID。
#if RH7
[assembly: Guid("FEAB732C-606E-41CD-BB13-894894D73F08")]
#else
[assembly: Guid("016FBAD8-C5C9-49E7-B56C-9CCEB364DE06")]
#endif

namespace PatchFillPattern
{
    /// <summary>插件入口：多边补面（Rhino 7 / 8 用不同 GUID，避免同机冲突）</summary>
#if RH7
    [Guid("FEAB732C-606E-41CD-BB13-894894D73F08")]
#else
    [Guid("016FBAD8-C5C9-49E7-B56C-9CCEB364DE06")]
#endif
    public class PatchFillPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime { get { return PlugInLoadTime.AtStartup; } }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            MaybeRunSelfTest();
            MaybeRunProbe();
            RhinoApp.WriteLine("多边补面插件已加载。命令：PatchFill");
            return LoadReturnCode.Success;
        }

        /// <summary>无人值守诊断：run-patchfill-probe.flag（内容见 PatchFillProbeCommand 注释）</summary>
        static void MaybeRunProbe()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                if (!File.Exists(Path.Combine(dir, "run-patchfill-probe.flag"))) return;
                RhinoApp.Idle += ProbeOnIdle;
            }
            catch { }
        }

        static void ProbeOnIdle(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= ProbeOnIdle;
                PatchFillProbeCommand.RunFromFlag();
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("多边补面诊断失败：" + ex.Message); } catch { }
            }
        }

        /// <summary>
        /// 无人值守自检：在 %LOCALAPPDATA%\IVAN\logs\ 放 run-patchfill-selftest.flag，
        /// 下次启动 Rhino 自动跑几何自检并写报告到同目录 PatchFillSelfTest.txt，跑完退出。
        /// </summary>
        static void MaybeRunSelfTest()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs");
                string flag = Path.Combine(dir, "run-patchfill-selftest.flag");
                if (!File.Exists(flag)) return;
                File.Delete(flag);
                RhinoApp.Idle += SelfTestOnIdle;
            }
            catch { }
        }

        static void SelfTestOnIdle(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= SelfTestOnIdle;
                PatchFillSelfTestCommand.RunTo(RhinoDoc.ActiveDoc, PatchFillSelfTestCommand.DefaultReportPath, true);
            }
            catch (Exception ex)
            {
                try { RhinoApp.WriteLine("多边补面自检失败：" + ex.Message); } catch { }
            }
        }
    }

    /// <summary>一条边界引用：物件 id + 子物件组件（曲面边要带组件索引，否则丢了相邻面就没了 G1）</summary>
    internal struct BoundaryRef
    {
        public Guid Id;
        public ComponentIndexType Type;
        public int Index;

        public BoundaryRef(Guid id, ComponentIndexType type, int index)
        {
            Id = id; Type = type; Index = index;
        }

        public static BoundaryRef Whole(Guid id)
        {
            return new BoundaryRef(id, ComponentIndexType.InvalidType, -1);
        }
    }

    public class PatchFillCommand : Command
    {
        public override string EnglishName { get { return "PatchFill"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var settings = new PatchFillSettings();
            try { settings.MmToModel = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem); }
            catch { settings.MmToModel = 1.0; }

            // 预选的物件直接当边界（含子物件选中的边）；没预选就用空目标打开面板
            var refs = new List<BoundaryRef>();
            try
            {
                foreach (RhinoObject o in doc.Objects.GetSelectedObjects(false, false))
                {
                    if (o == null) continue;
                    ComponentIndex[] subs = null;
                    try { subs = o.GetSelectedSubObjects(); } catch { subs = null; }
                    bool any = false;
                    if (subs != null)
                    {
                        for (int i = 0; i < subs.Length; i++)
                        {
                            if (subs[i].ComponentIndexType == ComponentIndexType.BrepEdge)
                            {
                                refs.Add(new BoundaryRef(o.Id, ComponentIndexType.BrepEdge, subs[i].Index));
                                any = true;
                            }
                        }
                    }
                    if (!any) refs.Add(BoundaryRef.Whole(o.Id));
                }
            }
            catch { }

            string desc = PatchFillSession.DescribeBoundary(doc, refs);
            var session = new PatchFillSession(doc, refs, settings, desc);
            session.Start();
            RhinoApp.WriteLine(refs.Count > 0
                ? "参数面板已打开（已用选中的 " + refs.Count + " 条边作为边界）。"
                : "参数面板已打开。点面板上的「选择边界」选一圈首尾相连的曲线 / 曲面边。");
            return Result.Success;
        }
    }

    /// <summary>
    /// 面板上「选择边界」/「选择内部约束」按钮调用的隐藏命令（面板是非模态的，GetObject 只能在命令上下文里跑）。
    /// 用静态 NextMode 区分两次拾取（同一个命令，命令集不变）。
    /// </summary>
    public class PatchFillPickTargetCommand : Command
    {
        public override string EnglishName { get { return "PatchFillPickTarget"; } }

        internal enum PickMode { Boundary, Inner }
        internal static PickMode NextMode = PickMode.Boundary;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            PatchFillSession session = PatchFillSession.Active;
            if (session == null)
            {
                RhinoApp.WriteLine("请先运行 PatchFill 打开参数面板。");
                return Result.Nothing;
            }
            PickMode pickMode = NextMode;
            NextMode = PickMode.Boundary;

            var go = new GetObject();
            go.SubObjectSelect = true;
            if (pickMode == PickMode.Boundary)
            {
                go.SetCommandPrompt("选择边界：一圈曲线 / 曲面边（可子物件选边；多选后回车结束）");
                go.GeometryFilter = ObjectType.Curve | ObjectType.Brep | ObjectType.Surface | ObjectType.Extrusion;
            }
            else
            {
                go.SetCommandPrompt("选择内部约束：曲线 / 点（可多选；直接回车 = 清空内部约束）");
                go.GeometryFilter = ObjectType.Curve | ObjectType.Point | ObjectType.PointSet;
            }
            go.GetMultiple(pickMode == PickMode.Boundary ? 1 : 0, 0);
            Result r = go.CommandResult();
            if (r == Result.Cancel)
            {
                // 用户主动取消：不改变已有状态
                RhinoApp.WriteLine("已取消拾取。");
                return Result.Cancel;
            }
            if (r != Result.Success) return r;

            var ids = new List<Guid>();
            var picks = new List<BoundaryRef>();
            for (int i = 0; i < go.ObjectCount; i++)
            {
                ObjRef objRef = go.Object(i);
                if (objRef == null || objRef.ObjectId == Guid.Empty) continue;
                ids.Add(objRef.ObjectId);
                ComponentIndex ci = ComponentIndex.Unset;
                try { ci = objRef.GeometryComponentIndex; } catch { ci = ComponentIndex.Unset; }
                if (ci.ComponentIndexType != ComponentIndexType.InvalidType)
                    picks.Add(new BoundaryRef(objRef.ObjectId, ci.ComponentIndexType, ci.Index));
                else picks.Add(BoundaryRef.Whole(objRef.ObjectId));
            }

            if (pickMode == PickMode.Boundary)
            {
                if (picks.Count == 0) { RhinoApp.WriteLine("没有选到边界。"); return Result.Failure; }
                session.SetBoundary(doc, picks);
                RhinoApp.WriteLine("边界已更新：" + PatchFillSession.DescribeBoundary(doc, picks));
            }
            else
            {
                session.SetInner(doc, ids);
                RhinoApp.WriteLine(ids.Count == 0 ? "内部约束已清空。" : ("内部约束已更新：" + ids.Count + " 个物件。"));
            }
            return Result.Success;
        }
    }

    /// <summary>
    /// 诊断命令（实测/排查用）：把文档里每个 Brep 的裸露边界环结构（几条环、周长、面积、4 角方式、拐角数、
    /// 相邻面）与补面偏差打成报告；同时把补出来的面写进当前文档（图层「多边补面」）。
    /// 支持无人值守：%LOCALAPPDATA%\IVAN\logs\run-patchfill-probe.flag，内容为 key=value 多行：
    ///   open=D:\...\x.3dm   save=D:\...\out.3dm   capture=%TEMP%\x.png   report=...
    ///   grid=12  fit=1  smooth=0.15  g1=1  g1d=1.0  inner=0
    /// </summary>
        /// <summary>安装器 PluginDef.ExtraCommands 要把这三个隐藏命令都列上：PatchFillSelfTest;PatchFillPickTarget;PatchFillProbe</summary>
        public class PatchFillProbeCommand : Command
    {
        public override string EnglishName { get { return "PatchFillProbe"; } }

        public static string DefaultReportPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    @"IVAN\logs\PatchFillProbe.txt");
            }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RunProbe(doc, new PatchFillSettings(), null, null, DefaultReportPath);
            RhinoApp.WriteLine("诊断报告：" + DefaultReportPath);
            return Result.Success;
        }

        internal static void RunFromFlag()
        {
            string dir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
            string flag = Path.Combine(dir, "run-patchfill-probe.flag");
            var kv = new Dictionary<string, string>();
            try
            {
                if (File.Exists(flag))
                    foreach (string line in File.ReadAllLines(flag))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) kv[line.Substring(0, i).Trim().ToLowerInvariant()] = line.Substring(i + 1).Trim();
                    }
            }
            catch { }
            try { File.Delete(flag); } catch { }

            var s = new PatchFillSettings();
            int n; double d; string v;
            if (kv.TryGetValue("grid", out v) && int.TryParse(v, out n)) s.ControlCount = n;
            if (kv.TryGetValue("fit", out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.FitStrength = d;
            if (kv.TryGetValue("smooth", out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.Smooth = d;
            if (kv.TryGetValue("g1d", out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.G1Distance = d;
            if (kv.TryGetValue("g1", out v)) s.Continuity = (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)) ? 0 : (v == "2" ? 2 : 1);
            if (kv.TryGetValue("inner", out v)) s.KeepHoles = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            string open = Get(kv, "open");
            if (!string.IsNullOrEmpty(open) && File.Exists(open))
            {
                RhinoDoc opened = null;
                try
                {
                    bool already;
                    opened = RhinoDoc.Open(open, out already);
                }
                catch { }
                if (opened != null) doc = opened;
            }

            // xnrun=<label>：机器化 XNURBS 对照。流程：
            //   1) LoadPlugIn 加载 XNurbsRhino.rhp（ Rhino7 目录里的插件，Rhino 8 兼容——用户实测可跑）
            //   2) 构造/打开 bench 洞（xnbench=D:/xxx.3dm 或复用 bench= 通道）
            //   3) 预选边界 → RunScript("_XNurbs")：反编译结论 = 命令是「拾取循环+喂约束+返回」，
            //      EnablePreSelect 已开 → 预选集直接满足拾取，不卡
            //   4) 面板（CRhinoTabbedDockBarDialog，非模态）弹出后由外部 UIA/WM_COMMAND 驱动点「创建」
            //   5) 本命令立即返回（脚本继续跑后面的宏，如保存文档）
            string xnrun = Get(kv, "xnrun");
            if (!string.IsNullOrEmpty(xnrun))
            {
                RunXnurbsProbe(kv, xnrun);
                return;
            }

            // xnmeasure=<label>：对文档里「新增的结果面」量 BENCH 六项。
            //   xnm_edge_layer=benchN   输入边界所在层（Brep 的裸边 = 边界）
            //   xnm_ref_layer=…         可选：参考几何层（bench4/5 的圆柱/球）；缺省只用边界
            //   xnm_out=D:/…txt         度量输出（驱动器读它填 BENCH 表）
            // 结果面识别 = 文档中不属于任何 bench* 层的 Brep（XNurbs 创建后落在当前层）。
            string xnmeasure = Get(kv, "xnmeasure");
            if (!string.IsNullOrEmpty(xnmeasure))
            {
                RunXnMeasure(kv, xnmeasure);
                return;
            }

            // bench=1：程序化构造 5 个测试洞（BENCH 口径同款几何）写进文档并另存 3dm，
            // 不做补面诊断（只当「同一批输入」的生成器；几何与 PatchFillSelfTest 的 Case2-6 一致）
            string bench = Get(kv, "bench");
            if (!string.IsNullOrEmpty(bench))
            {
                var bl = new System.Text.StringBuilder("bench 开始 · path=" + bench
                    + " · activeDoc=" + (RhinoDoc.ActiveDoc != null));
                try
                {
                    doc = BuildBenchDoc(bench);
                    bl.Append(" · 构造完成 objects=" + (doc != null ? doc.Objects.Count.ToString() : "n/a"));
                    bl.Append(" · 另存 " + (File.Exists(bench) ? "成功" : "失败"));
                }
                catch (Exception ex) { bl.Append(" · 异常 " + ex.GetType().Name + ": " + ex.Message); }
                try
                {
                    string bdir = Path.GetDirectoryName(DefaultReportPath);
                    if (!string.IsNullOrEmpty(bdir)) Directory.CreateDirectory(bdir);
                    File.WriteAllText(DefaultReportPath, "=== bench ===\n" + bl.ToString(), new System.Text.UTF8Encoding(false));
                }
                catch { }
                try { RhinoApp.Exit(); } catch { }
                return;
            }

            string report = Get(kv, "report");
            RunProbe(doc, s, Get(kv, "save"), Get(kv, "capture"),
                string.IsNullOrEmpty(report) ? DefaultReportPath : reportPathFallback(report));
            try { RhinoApp.Exit(); } catch { }
        }

        static string reportPathFallback(string report)
        {
            return string.IsNullOrEmpty(report) ? DefaultReportPath : report;
        }

        /// <summary>
        /// BENCH 测试洞文档：① 4 边平面矩形洞 ② 5 边洞 ③ 3 边洞（40×40 平板 z=0）
        /// ④ 圆柱面 60°×6 矩形洞 ⑤ 球带（R=10，0°~30°N）。与自检 Case2/3/4/5/6 同参数。
        /// 每个洞一个图层，名字 = bench1..bench5，跑 XNURBS 时一次开一个。
        /// </summary>
        internal static RhinoDoc BuildBenchDoc(string savePath)
        {
            // runscript 启动的 Rhino 一定有活动文档
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            var mkLayer = new System.Func<string, int>((name) =>
            {
                int idx = doc.Layers.Find(name, true);
                if (idx < 0)
                {
                    var l = new Layer { Name = name };
                    idx = doc.Layers.Add(l);
                }
                return idx;
            });

            // ① 4 边平面洞（10,10)-(30,30)
            {
                int li = mkLayer("bench1");
                var hole = new Point3d[] { new Point3d(10, 10, 0), new Point3d(30, 10, 0), new Point3d(30, 30, 0), new Point3d(10, 30, 0) };
                var outer = new PolylineCurve(new Point3d[] { new Point3d(0,0,0), new Point3d(40,0,0), new Point3d(40,40,0), new Point3d(0,40,0), new Point3d(0,0,0) });
                var hp = new List<Point3d>(hole); hp.Add(hole[0]);
                Brep[] b = Brep.CreatePlanarBreps(new Curve[] { outer, new PolylineCurve(hp) }, 1e-6);
                if (b != null && b.Length > 0)
                {
                    var a = new ObjectAttributes { LayerIndex = li };
                    doc.Objects.AddBrep(b[0], a);
                }
            }
            // ② 5 边洞
            {
                int li = mkLayer("bench2");
                var pent = new List<Point3d>();
                for (int i = 0; i < 5; i++)
                {
                    double a = Math.PI / 2 + 2 * Math.PI * i / 5.0;
                    pent.Add(new Point3d(20 + 11 * Math.Cos(a), 20 + 11 * Math.Sin(a), 0));
                }
                var outer = new PolylineCurve(new Point3d[] { new Point3d(0,0,0), new Point3d(40,0,0), new Point3d(40,40,0), new Point3d(0,40,0), new Point3d(0,0,0) });
                pent.Add(pent[0]);
                Brep[] b = Brep.CreatePlanarBreps(new Curve[] { outer, new PolylineCurve(pent) }, 1e-6);
                if (b != null && b.Length > 0)
                {
                    var a2 = new ObjectAttributes { LayerIndex = li };
                    doc.Objects.AddBrep(b[0], a2);
                }
            }
            // ③ 3 边洞
            {
                int li = mkLayer("bench3");
                var tri = new Point3d[] { new Point3d(12, 10, 0), new Point3d(30, 12, 0), new Point3d(20, 31, 0) };
                var outer = new PolylineCurve(new Point3d[] { new Point3d(0,0,0), new Point3d(40,0,0), new Point3d(40,40,0), new Point3d(0,40,0), new Point3d(0,0,0) });
                var tp = new List<Point3d>(tri); tp.Add(tri[0]);
                Brep[] b = Brep.CreatePlanarBreps(new Curve[] { outer, new PolylineCurve(tp) }, 1e-6);
                if (b != null && b.Length > 0)
                {
                    var a3 = new ObjectAttributes { LayerIndex = li };
                    doc.Objects.AddBrep(b[0], a3);
                }
            }
            // ④ 圆柱面洞（R=10，z 0..6，θ ±30°）—— 只放洞边界 4 条曲线 + 未修剪圆柱参考
            {
                int li = mkLayer("bench4");
                var cyl = new Cylinder(new Circle(new Plane(Point3d.Origin, Vector3d.ZAxis), 10.0), 6.0);
                Brep cylB = Brep.CreateFromSurface(NurbsSurface.CreateFromCylinder(cyl));
                if (cylB != null) doc.Objects.AddBrep(cylB, new ObjectAttributes { LayerIndex = li });
                double a0 = -Math.PI / 6, a1 = Math.PI / 6;
                Point3d p00 = new Point3d(10 * Math.Cos(a0), 10 * Math.Sin(a0), 0);
                Point3d p10 = new Point3d(10 * Math.Cos(a1), 10 * Math.Sin(a1), 0);
                Point3d p01 = new Point3d(p00.X, p00.Y, 6);
                Point3d p11 = new Point3d(p10.X, p10.Y, 6);
                doc.Objects.AddCurve(new Arc(new Circle(new Plane(new Point3d(0, 0, 0), Vector3d.ZAxis), 10.0), new Interval(a0, a1)).ToNurbsCurve(), new ObjectAttributes { LayerIndex = li });
                doc.Objects.AddCurve(new Arc(new Circle(new Plane(new Point3d(0, 0, 6), Vector3d.ZAxis), 10.0), new Interval(a0, a1)).ToNurbsCurve(), new ObjectAttributes { LayerIndex = li });
                doc.Objects.AddCurve(new LineCurve(p00, p01), new ObjectAttributes { LayerIndex = li });
                doc.Objects.AddCurve(new LineCurve(p10, p11), new ObjectAttributes { LayerIndex = li });
            }
            // ⑤ 球带（R=10，纬度 0°~30°N）—— 完整球参考 + 球带（洞 = 顶圈）
            {
                int li = mkLayer("bench5");
                Brep sphere = Brep.CreateFromSurface(NurbsSurface.CreateFromSphere(new Sphere(Point3d.Origin, 10.0)));
                if (sphere != null) doc.Objects.AddBrep(sphere, new ObjectAttributes { LayerIndex = li });
                double a0 = 0, a1 = 30 * Math.PI / 180.0;
                Point3d p0 = new Point3d(10 * Math.Cos(a0), 0, 10 * Math.Sin(a0));
                Point3d p1 = new Point3d(10 * Math.Cos(a1), 0, 10 * Math.Sin(a1));
                Point3d pm = new Point3d(10 * Math.Cos((a0 + a1) / 2), 0, 10 * Math.Sin((a0 + a1) / 2));
                var meridian = new Arc(p0, pm, p1).ToNurbsCurve();
                RevSurface rev = RevSurface.Create(meridian, new Line(Point3d.Origin, new Point3d(0, 0, 1)), 0, 2 * Math.PI);
                if (rev != null)
                {
                    Brep zone = Brep.CreateFromSurface(rev);
                    if (zone != null) doc.Objects.AddBrep(zone, new ObjectAttributes { LayerIndex = li });
                }
            }

            if (!string.IsNullOrEmpty(savePath))
            {
                try { doc.WriteFile(savePath, new Rhino.FileIO.FileWriteOptions()); } catch { }
            }
            return doc;
        }

        static string Get(Dictionary<string, string> kv, string key)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>
        /// 机器化 XNURBS 探针：加载插件 → 造洞 → 预选 → 起面板 → 写状态文件退出脚本上下文。
        /// 状态文件给外部驱动器（PowerShell UIA）读：xnhwnd=面板窗口句柄（若已找到）。
        /// 不驱动面板本身——那一步由外部 UIA 做（本命令在 Rhino 主线程，RunScript 里做模态等待会卡死）。
        /// </summary>
        static void RunXnurbsProbe(Dictionary<string, string> kv, string label)
        {
            var sb = new System.Text.StringBuilder("=== xnurbs run " + label + " ===\n");
            sb.AppendLine("time: " + DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            string report = DefaultReportPath;
            try
            {
                // 1) XNurbs 已注册在 Rhino 8（HKCU ...\8.0\Plug-Ins\80be33b0-...，用户实测可跑）
                //    → 随 Rhino 启动自动加载，无需 LoadPlugIn（实测 LoadPlugIn 对这台机器的注册返回 Guid.Zero）
                //    这里只确认命令存在（打一条版本信息，真实验证靠 RunScript）
                sb.AppendLine("XNurbs pre-registered: expecting auto-load (guid 80be33b0-13b2-4ac4-9c77-03829214f9e9)");

                // 2) 文档：不用进程内 RhinoDoc.Open（Idle 回调里拿到的句柄 objects=0，实测踩到）——
                //    open= 只做提示：3dm 由启动命令行打开（Rhino.exe file.3dm），这里直接用 ActiveDoc
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                string open = Get(kv, "open");
                if (!string.IsNullOrEmpty(open)) sb.AppendLine("open(expected via cmdline): " + open);
                if (doc != null)
                {
                    for (int i = 0; i < 40 && doc.Objects.Count == 0; i++) System.Threading.Thread.Sleep(250);
                    sb.AppendLine("activeDoc objects=" + doc.Objects.Count + " layers=" + doc.Layers.Count);
                }
                if (doc == null) { WriteReport(report, sb); return; }

                // 3) 预选 + **屏幕坐标表**：XNurbs 的 GetObjects 是 EnablePreSelect(false,true)（反汇编 0x149e：
                //    edx=0 → 显式关闭预选，强制用户手点）→ 预选无效。改为输出每条边界曲线上 3 个采样点的
                //    **屏幕像素坐标**（ActiveViewport 世界→屏幕），驱动器用 SendInput 逐条点击（拾取循环吃点击）。
                string pick = Get(kv, "xnpick");
                if (!string.IsNullOrEmpty(pick))
                {
                    int li = doc.Layers.Find(pick, true);
                    sb.AppendLine("layerFind '" + pick + "' = " + li + " · docObjects=" + doc.Objects.Count);
                    if (li >= 0)
                    {
                        doc.Objects.UnselectAll();
                        var view = doc.Views.ActiveView;
                        var vp = view != null ? view.ActiveViewport : null;
                        sb.AppendLine("view: " + (vp != null ? vp.Name : "(none)"));
                        foreach (RhinoObject o in doc.Objects)
                        {
                            if (o == null || o.Attributes == null || o.Attributes.LayerIndex != li) continue;
                            Brep bp = o.Geometry as Brep;
                            if (bp == null) continue;
                            Curve[] naked = null;
                            try { naked = bp.DuplicateNakedEdgeCurves(true, true); } catch { naked = null; }
                            if (naked == null) continue;
                            foreach (Curve nc in naked)
                            {
                                // 曲线 1/4、中点、3/4 → viewport client → 屏幕（点中点附近最容易拾取）
                                double len = 0;
                                try { len = nc.GetLength(); } catch { }
                                if (len < 1e-9) continue;
                                var pts = new string[3];
                                double[] fr = { 0.25, 0.5, 0.75 };
                                for (int k = 0; k < 3; k++)
                                {
                                    Point3d wp = nc.PointAtLength(len * fr[k]);
                                    string s = "x";
                                    if (vp != null)
                                    {
                                        try
                                        {
                                            // 世界 → viewport 客户区像素 → 屏幕绝对像素（ClientToScreen 吃 client 坐标，实测踩过：直接喂世界坐标会点到 z=0 平面别处）
                                            Point2d client = vp.WorldToClient(wp);
                                            System.Drawing.Point sp = vp.ClientToScreen(client);
                                            s = sp.X + "," + sp.Y;
                                        }
                                        catch { }
                                    }
                                    pts[k] = s;
                                }
                                sb.AppendLine("PICKCURVE " + string.Join(" ", pts));
                            }
                        }
                        sb.AppendLine("pick curves listed");
                    }
                }

                // 4) 落盘坐标表，起 XNurbs（同步阻塞）。⚠ 报告写在 RunScript 前 = 驱动器点击时命令可能
                //    还没进拾取状态（实测点击全部落空）→ 驱动器必须先等「拾取就绪」信号再点。
                //    信号 = 命令行提示词变化，这里用简单时序：先把 RunScript 派到 Idle 队列之后再返回，
                //    让本 Idle 回调结束、Rhino 进入命令拾取循环 → 驱动器 sleep 3s 后开始点击。
                WriteReport(report, sb);
                RhinoApp.Idle += XnurbsDeferredRun;
                sb.AppendLine("deferred _XNurbs via Idle");

                // 5) 记录当前顶层窗口（找 XNurbs 面板给外部 UIA 用）
                sb.AppendLine("hwnds: see external enumerator");

                // 6) 保存（save=）
                string save = Get(kv, "save");
                if (!string.IsNullOrEmpty(save))
                {
                    bool ok = doc.WriteFile(save, new Rhino.FileIO.FileWriteOptions());
                    sb.AppendLine("save: " + save + " → " + (ok ? "OK" : "FAIL"));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("EXC: " + ex.GetType().Name + ": " + ex.Message);
            }
            WriteReport(report, sb);
        }

        static bool RhinoPlugInPlugIn(string rhpPath, out Guid id)
        {
            id = Guid.Empty;
            try
            {
                var r = Rhino.PlugIns.PlugIn.LoadPlugIn(rhpPath, out id);
                return r == Rhino.PlugIns.LoadPlugInResult.Success || id != Guid.Empty;
            }
            catch { return false; }
        }

        /// <summary>把 _XNurbs 派到下一次 Idle（本 Idle 回调退出后 Rhino 才进入命令拾取循环，外部点击才有效）</summary>
        static void XnurbsDeferredRun(object sender, EventArgs e)
        {
            try
            {
                RhinoApp.Idle -= XnurbsDeferredRun;
                RhinoApp.RunScript("_XNurbs", false);
                try
                {
                    string dir = Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), @"IVAN\logs");
                    File.AppendAllText(Path.Combine(dir, "PatchFillProbe.txt"),
                        "\nXNurbs command returned\n", new System.Text.UTF8Encoding(false));
                }
                catch { }
            }
            catch { }
        }

        /// <summary>
        /// XNURBS 结果面度量（BENCH 六项，与 PatchFillSelfTest 同一套口径）：
        /// 缝隙 = 结果面参数边界采样 → 输入边界折线；偏差 = 结果面采样 → 参考几何最近点；
        /// 控制网/面数直读；耗时由驱动器记（面板生成时间无法从插件外拿）。
        /// </summary>
        static void RunXnMeasure(Dictionary<string, string> kv, string label)
        {
            var sb = new System.Text.StringBuilder("=== xnmeasure " + label + " ===\n");
            string outPath = Get(kv, "xnm_out");
            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null) { sb.AppendLine("no doc"); return; }
                string edgeLayer = Get(kv, "xnm_edge_layer") ?? "bench1";
                string refLayer = Get(kv, "xnm_ref_layer");
                int liEdge = doc.Layers.Find(edgeLayer, true);
                int liRef = string.IsNullOrEmpty(refLayer) ? -1 : doc.Layers.Find(refLayer, true);
                sb.AppendLine("edgeLayer=" + edgeLayer + "(" + liEdge + ") refLayer=" + refLayer + "(" + liRef + ")");

                // 输入边界 = edge 层 Brep 的裸边
                var edgeList = new List<PatchFillEdge>();
                foreach (RhinoObject o in doc.Objects)
                {
                    if (o == null || o.Attributes == null || o.Attributes.LayerIndex != liEdge) continue;
                    Brep bp = o.Geometry as Brep;
                    if (bp == null) continue;
                    Curve[] naked = null;
                    try { naked = bp.DuplicateNakedEdgeCurves(true, true); } catch { naked = null; }
                    if (naked == null) continue;
                    for (int k = 0; k < naked.Length; k++)
                        edgeList.Add(new PatchFillEdge(naked[k], bp, -1, null, "bench-edge" + (k + 1)));
                }
                // 参考几何 = ref 层 Brep（若有）
                Brep refBrep = null;
                if (liRef >= 0)
                {
                    foreach (RhinoObject o in doc.Objects)
                    {
                        if (o == null || o.Attributes == null || o.Attributes.LayerIndex != liRef) continue;
                        Brep bp = o.Geometry as Brep;
                        if (bp != null && bp.Faces.Count > 0) { refBrep = bp; break; }
                    }
                }
                // 结果面 = 不属于任何 bench* 层的 Brep
                Brep result = null;
                foreach (RhinoObject o in doc.Objects)
                {
                    if (o == null || o.Geometry as Brep == null || o.Attributes == null) continue;
                    string ln = doc.Layers[o.Attributes.LayerIndex].Name;
                    if (ln.StartsWith("bench", StringComparison.Ordinal)) continue;
                    if (ln == PatchFillCore.LayerPatch) continue;
                    result = o.Geometry as Brep;
                }
                if (result == null || result.Faces.Count == 0) { sb.AppendLine("result surface NOT found"); return; }
                if (edgeList.Count == 0) { sb.AppendLine("edge curves NOT found"); return; }

                double diag = 1;
                {
                    var bb = result.GetBoundingBox(true);
                    diag = bb.Diagonal.Length;
                    if (!(diag > 1e-9)) diag = 1;
                }
                var refr = new PatchFillCore.Reference();
                if (refBrep != null) refr.AddExplicit(refBrep);

                // ① 边界缝隙：结果面外环采样 → 边界折线
                var loopList = new List<PatchFillCore.LoopData>(); var lcnt = new List<int>(); string why;
                double gap = -1;
                if (PatchFillCore.ChainLoops(edgeList, diag, out loopList, out lcnt, out why))
                {
                    gap = 0;
                    Curve outer = null;
                    try { outer = result.Faces[0].OuterLoop.To3dCurve(); } catch { }
                    if (outer != null)
                    {
                        double len = outer.GetLength();
                        for (int k = 0; k < 121; k++)
                        {
                            Point3d p = outer.PointAtLength(len * k / 120.0);
                            double d = PatchFillCore.DistToLoop(loopList[0], p);
                            if (d > gap) gap = d;
                        }
                    }
                }
                else sb.AppendLine("chain fail: " + why);

                // ② 偏差（结果面 21×21 → 参考几何）
                double maxDev = -1, rms = -1;
                if (refr.Any)
                {
                    maxDev = 0; double sum = 0; int cnt = 0;
                    var srf = result.Faces[0];
                    Interval du = srf.Domain(0), dv = srf.Domain(1);
                    double cap = diag * 0.75;
                    for (int i = 0; i < 21; i++)
                        for (int j = 0; j < 21; j++)
                        {
                            Point3d p;
                            try { p = srf.PointAt(du.ParameterAt(i / 20.0), dv.ParameterAt(j / 20.0)); } catch { continue; }
                            Point3d q; Vector3d nn; double d; bool atEdge;
                            if (!refr.Closest(p, cap, out q, out nn, out d, out atEdge)) continue;
                            if (d > maxDev) maxDev = d;
                            sum += d * d; cnt++;
                        }
                    rms = cnt > 0 ? Math.Sqrt(sum / cnt) : -1;
                }
                else sb.AppendLine("no reference geometry — deviation skipped");

                // ④ 面数/控制网
                int nu = 0, nv = 0;
                try { var us = result.Faces[0].UnderlyingSurface() as NurbsSurface; if (us != null) { nu = us.Points.CountU; nv = us.Points.CountV; } } catch { }

                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "METRIC gap={0:0.########} maxDev={1:0.########} rms={2:0.########} faces={3} cp={4}x{5}",
                    gap, maxDev, rms, result.Faces.Count, nu, nv));
                sb.AppendLine("note: G1 角度与耗时由驱动器阶段补（G1 需相邻面法向；耗时=创建按钮到生成的秒表）");
            }
            catch (Exception ex)
            {
                sb.AppendLine("EXC: " + ex.GetType().Name + ": " + ex.Message);
            }
            try
            {
                if (!string.IsNullOrEmpty(outPath)) File.AppendAllText(outPath, sb.ToString() + "\n", new System.Text.UTF8Encoding(false));
            }
            catch { }
            WriteReport(DefaultReportPath, sb);
        }

        internal static void RunProbe(RhinoDoc doc, PatchFillSettings s, string savePath, string capturePath, string reportPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 多边补面 诊断报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            if (doc == null) { WriteReport(reportPath, sb); return; }
            sb.AppendLine("文档：" + (string.IsNullOrEmpty(doc.Path) ? "(未保存)" : doc.Path));

            var targets = new List<RhinoObject>();
            try
            {
                foreach (RhinoObject o in doc.Objects)
                {
                    if (o == null) continue;
                    GeometryBase g = o.Geometry;
                    if (g is Brep bp && bp.Faces.Count > 0) targets.Add(o);
                }
            }
            catch { }
            sb.AppendLine("候选目标（Brep）：" + targets.Count);
            if (targets.Count == 0) { WriteReport(reportPath, sb); return; }

            int layer = PatchFillCore.EnsureLayer(doc, PatchFillCore.LayerPatch, System.Drawing.Color.FromArgb(194, 96, 58));
            var attrs = new ObjectAttributes();
            attrs.LayerIndex = layer;
            int written = 0, filled = 0, tried = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                Brep bp = targets[i].Geometry as Brep;
                if (bp == null) continue;
                Curve[] naked = null;
                try { naked = bp.DuplicateNakedEdgeCurves(true, true); } catch { naked = null; }
                if (naked == null || naked.Length == 0) continue;
                tried++;
                sb.AppendLine();
                sb.AppendLine("---- 目标 " + (i + 1) + "：" + PatchFillCore.Describe(targets[i])
                    + " · 裸露边 " + naked.Length + " 条 ----");

                var edges = new List<PatchFillEdge>();
                for (int k = 0; k < naked.Length; k++)
                {
                    var e = new PatchFillEdge();
                    e.Curve = naked[k];
                    e.Source = "裸边 " + (k + 1);
                    // 找到这条裸边在 Brep 里的边序号（拿相邻面做 G1）
                    int ei = FindEdgeIndex(bp, naked[k]);
                    if (ei >= 0)
                    {
                        e.Host = bp;
                        e.HostEdgeIndex = ei;
                        try { e.AdjacentFaces = bp.Edges[ei].AdjacentFaces(); } catch { e.AdjacentFaces = null; }
                    }
                    edges.Add(e);
                }

                try { sb.Append(PatchFillCore.Diagnose(edges, null, s)); }
                catch (Exception ex) { sb.AppendLine("诊断异常：" + ex.Message); }

                string rep;
                PatchFillResult r = PatchFillCore.Generate(edges, null, null, s, out rep);
                sb.Append(rep);
                if (r.Ok)
                {
                    filled++;
                    attrs.Name = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "多边补面 {0}（偏差 {1:0.####}）", i + 1, r.MaxDeviation);
                    if (doc.Objects.AddBrep(r.Result, attrs) != Guid.Empty) written++;
                }
            }
            sb.AppendLine();
            sb.AppendLine("扫描 " + targets.Count + " / 有裸边 " + tried + " / 补面成功 " + filled
                + " / 写入文档 " + written + "（图层 " + PatchFillCore.LayerPatch + "）");
            WriteReport(reportPath, sb);

            if (!string.IsNullOrEmpty(capturePath))
            {
                try
                {
                    RhinoApp.RunScript("_-SelAll _-Zoom _Selected _-SelNone", false);
                    RhinoApp.RunScript(string.Format("_-ViewCaptureToFile \"{0}\" _Enter", capturePath), false);
                    sb.AppendLine("截图：" + capturePath);
                }
                catch (Exception ex) { sb.AppendLine("截图失败：" + ex.Message); }
                WriteReport(reportPath, sb);
            }

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    bool ok = doc.WriteFile(savePath, new Rhino.FileIO.FileWriteOptions());
                    sb.AppendLine("另存：" + savePath + " → " + (ok ? "成功" : "失败"));
                }
                catch (Exception ex) { sb.AppendLine("另存异常：" + ex.Message); }
                WriteReport(reportPath, sb);
            }
        }

        static int FindEdgeIndex(Brep bp, Curve c)
        {
            if (bp == null || c == null) return -1;
            try
            {
                Point3d mid = c.PointAt(c.Domain.Mid);
                for (int i = 0; i < bp.Edges.Count; i++)
                {
                    Curve e = bp.Edges[i].DuplicateCurve();
                    if (e == null) continue;
                    double t;
                    if (!e.ClosestPoint(mid, out t)) continue;
                    if (e.PointAt(t).DistanceTo(mid) < 1e-6) return i;
                }
            }
            catch { }
            return -1;
        }

        static void WriteReport(string reportPath, System.Text.StringBuilder sb)
        {
            if (string.IsNullOrEmpty(reportPath)) return;
            try
            {
                string d = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                File.WriteAllText(reportPath, sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch { }
            try { RhinoApp.WriteLine(sb.ToString()); } catch { }
        }
    }

    /// <summary>一次「多边补面」会话：浮动面板 + 实时预览 + 关闭时写图层（不阻塞 Rhino）</summary>
    internal class PatchFillSession
    {
        static readonly List<PatchFillSession> _live = new List<PatchFillSession>();

        readonly RhinoDoc _doc;
        readonly List<BoundaryRef> _edges;
        readonly List<Guid> _inner;
        readonly PatchFillPanel _panel;
        readonly PatchFillPreviewConduit _conduit = new PatchFillPreviewConduit();
        readonly Timer _timer = new Timer();
        List<PatchFillResult> _results = new List<PatchFillResult>();
        bool _closed;

        public PatchFillSession(RhinoDoc doc, List<BoundaryRef> edges, PatchFillSettings settings, string desc)
        {
            _doc = doc;
            _edges = edges != null ? new List<BoundaryRef>(edges) : new List<BoundaryRef>();
            _inner = new List<Guid>();
            _panel = new PatchFillPanel(settings, desc);
            _panel.PickRequested += delegate { RunPick(false); };
            _panel.InnerPickRequested += delegate { RunPick(true); };
        }

        internal static PatchFillSession Active
        {
            get { return _live.Count > 0 ? _live[_live.Count - 1] : null; }
        }

        /// <summary>边界摘要（1 条就报名字，多条报数量 + 第一条）</summary>
        internal static string DescribeBoundary(RhinoDoc doc, List<BoundaryRef> refs)
        {
            if (refs == null || refs.Count == 0) return PatchFillCore.EmptyTargetHint;
            RhinoObject first = null;
            try { first = doc.Objects.FindId(refs[0].Id); } catch { }
            if (refs.Count == 1)
                return "边界：" + PatchFillCore.Describe(first) + (refs[0].Index >= 0 ? (" · 边" + (refs[0].Index + 1)) : "");
            return string.Format("{0} 条边界（{1} 等）", refs.Count, PatchFillCore.Describe(first));
        }

        /// <summary>把边界引用解析成边界边（曲面边带相邻面 → 支持 G1）</summary>
        List<PatchFillEdge> ResolveEdges(out int valid, out string firstWhy)
        {
            valid = 0; firstWhy = null;
            var list = new List<PatchFillEdge>();
            for (int i = 0; i < _edges.Count; i++)
            {
                RhinoObject o = null;
                try { o = _doc.Objects.FindId(_edges[i].Id); } catch { }
                if (o == null) { if (firstWhy == null) firstWhy = "对象已删除"; continue; }
                ComponentIndex ci = ComponentIndex.Unset;
                if (_edges[i].Index >= 0)
                    ci = new ComponentIndex(_edges[i].Type, _edges[i].Index);
                PatchFillEdge e; string why;
                if (PatchFillCore.ResolveEdgeFromComponent(o, ci, out e, out why))
                {
                    list.Add(e); valid++;
                }
                else if (firstWhy == null) firstWhy = why;
            }
            return list;
        }

        void RunPick(bool inner)
        {
            try
            {
                PatchFillPickTargetCommand.NextMode = inner
                    ? PatchFillPickTargetCommand.PickMode.Inner
                    : PatchFillPickTargetCommand.PickMode.Boundary;
                RhinoApp.RunScript("_-PatchFillPickTarget", false);
            }
            catch (Exception ex) { _panel.SetInfo("选择失败：" + ex.Message); }
        }

        public void SetBoundary(RhinoDoc doc, List<BoundaryRef> refs)
        {
            if (_closed) return;
            _edges.Clear();
            if (refs != null) _edges.AddRange(refs);
            _panel.SetTarget(DescribeBoundary(doc, refs));
            _results = new List<PatchFillResult>();
            _conduit.Shaded = null;
            _conduit.SourceBoundary = null;
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        public void SetInner(RhinoDoc doc, List<Guid> ids)
        {
            if (_closed) return;
            _inner.Clear();
            if (ids != null) _inner.AddRange(ids);
            _panel.SetInnerState(_inner.Count > 0);
            Rebuild();
            try { _doc.Views.Redraw(); } catch { }
        }

        public void Start()
        {
            foreach (PatchFillSession s in _live.ToArray()) s.ClosePanel();

            _timer.Interval = 120;
            _timer.Tick += delegate { _timer.Stop(); Rebuild(); };
            _panel.ValueChanged += delegate
            {
                if (!_panel.LivePreview) return;
                _timer.Stop(); _timer.Start();
            };
            _panel.FormClosed += delegate { OnClosed(); };

            RhinoDoc.ReplaceRhinoObject += OnReplaceObject;
            RhinoDoc.DeleteRhinoObject += OnDeleteObject;

            _conduit.Enabled = true;
            _panel.Show();
            Rebuild();
            _panel.Activate();
            _live.Add(this);
        }

        void OnReplaceObject(object sender, RhinoReplaceObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (e.Document != null && e.Document.RuntimeSerialNumber != _doc.RuntimeSerialNumber) return;
                if (HasBoundary(e.ObjectId) || _inner.Contains(e.ObjectId)) Rebuild();
            }
            catch { }
        }

        bool HasBoundary(Guid id)
        {
            for (int i = 0; i < _edges.Count; i++) if (_edges[i].Id == id) return true;
            return false;
        }

        void OnDeleteObject(object sender, RhinoObjectEventArgs e)
        {
            if (_closed) return;
            try
            {
                bool hit = HasBoundary(e.ObjectId) || _inner.Contains(e.ObjectId);
                if (!hit) return;
                for (int i = _edges.Count - 1; i >= 0; i--)
                    if (_edges[i].Id == e.ObjectId) _edges.RemoveAt(i);
                _inner.Remove(e.ObjectId);
                _conduit.Shaded = null;
                _conduit.SourceBoundary = null;
                _panel.SetTarget(DescribeBoundary(_doc, _edges));
                _panel.SetInnerState(_inner.Count > 0);
                _panel.SetInfo(_edges.Count > 0 ? "有边界被删除，已用剩下的边界重算。" : "边界已被删除，请点「选择边界」重选。");
                Rebuild();
                _doc.Views.Redraw();
            }
            catch { }
        }

        void Rebuild()
        {
            if (_closed) return;
            try
            {
                if (_edges.Count == 0)
                {
                    _results = new List<PatchFillResult>();
                    _conduit.Shaded = null;
                    _conduit.SourceBoundary = null;
                    _conduit.InnerPoints = null;
                    _panel.SetEdgeState(false);
                    _panel.SetInfo(PatchFillCore.EmptyTargetHint);
                    try { _doc.Views.Redraw(); } catch { }
                    return;
                }

                PatchFillSettings cur = _panel.Settings;
                int valid; string validWhy;
                List<PatchFillEdge> list = ResolveEdges(out valid, out validWhy);

                var innerGeo = new List<GeometryBase>();
                for (int i = 0; i < _inner.Count; i++)
                {
                    RhinoObject o = null;
                    try { o = _doc.Objects.FindId(_inner[i]); } catch { }
                    if (o != null && o.Geometry != null) innerGeo.Add(o.Geometry);
                }

                _results = new List<PatchFillResult>();
                var breps = new List<Brep>();
                var loops = new List<Polyline>();
                var innerPts = new List<Point3d>();
                int failed = 0, faces = 0, holes = 0, rounds = 0, warns = 0;
                double secs = 0, worstDev = 0, worstBoundary = 0, worstAngle = -1;
                string firstNote = "";

                if (list.Count > 0)
                {
                    string rep;
                    PatchFillResult r = PatchFillCore.Generate(list, innerGeo, null, cur, out rep);
                    _results.Add(r);
                    secs += r.Seconds;
                    if (!string.IsNullOrEmpty(r.Error))
                    {
                        failed++;
                        firstNote = r.Error;
                    }
                    else
                    {
                        firstNote = r.Note;
                        if (r.Ok) { breps.Add(r.Result); faces += r.Result.Faces.Count; }
                        holes += r.Holes;
                        rounds = r.Rounds;
                        warns = r.WarnFlags;
                        if (r.MaxDeviation > worstDev) worstDev = r.MaxDeviation;
                        if (r.BoundaryDeviation > worstBoundary) worstBoundary = r.BoundaryDeviation;
                        if (r.MaxNormalAngleDeg > worstAngle) worstAngle = r.MaxNormalAngleDeg;
                        if (r.SourceBoundary != null) loops.AddRange(r.SourceBoundary);
                    }
                }
                else
                {
                    failed = 1;
                    firstNote = validWhy ?? "边界解析失败";
                }

                for (int i = 0; i < innerGeo.Count; i++)
                {
                    Curve c = innerGeo[i] as Curve;
                    if (c == null) continue;
                    double len = 0;
                    try { len = c.GetLength(); } catch { }
                    for (int k = 0; k <= 8; k++)
                    {
                        try { innerPts.Add(c.PointAtLength(len * k / 8.0)); } catch { }
                    }
                }

                if (_panel.LivePreview)
                {
                    _conduit.Shaded = breps.Count > 0 ? breps : null;
                    _conduit.SourceBoundary = loops.Count > 0 ? loops : null;
                    _conduit.InnerPoints = innerPts.Count > 0 ? innerPts : null;
                    _conduit.ShowSourceBoundary = cur.ShowSourceBoundary;
                    _doc.Views.Redraw();
                }

                _panel.SetEdgeState(list.Count > 0 && failed == 0);
                _panel.SetInnerState(_inner.Count > 0);

                string line = string.Format(
                    "{0} 条边 → {1} 张面（{2} 面 · 内孔 {3}）· 最大偏差 {4:0.####} · 逐边缝隙 {5:0.######} · {6:0.00}s · {7} 轮",
                    list.Count, breps.Count, faces, holes, worstDev, worstBoundary, secs, rounds);
                if (worstAngle >= 0) line += string.Format(" · G1 {0:0.####}°", worstAngle);
                if (failed > 0) line += " · 失败";
                if (warns != 0) line += " · ⚠ " + PatchFillCore.WarnText(warns);
                _panel.SetInfo(line + "\r\n" + FirstNote(firstNote));
            }
            catch (Exception ex)
            {
                _panel.SetInfo("计算失败：" + ex.Message);
                _panel.SetEdgeState(false);
            }
        }

        static string FirstNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            string[] lines = note.Split('\n');
            if (lines.Length == 0) return "";
            string s = lines[0].Trim();
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        public void ClosePanel()
        {
            try { _panel.Close(); } catch { }
        }

        void OnClosed()
        {
            if (_closed) return;

            if (_panel.Committed && _edges.Count > 0)
            {
                try
                {
                    Rebuild();
                    int count;
                    uint undo = _doc.BeginUndoRecord("生成多边补面");
                    try
                    {
                        PatchFillCore.AddToDocument(_doc, _results, out count);
                    }
                    finally { _doc.EndUndoRecord(undo); }
                    RhinoApp.WriteLine(string.Format("已生成 {0} 张补面（图层：{1}）。", count, PatchFillCore.LayerPatch));
                    try { _doc.Views.Redraw(); } catch { }
                }
                catch (Exception ex) { RhinoApp.WriteLine("写入失败：" + ex.Message); }
            }
            else if (_panel.Committed)
            {
                RhinoApp.WriteLine("没有选择边界，未生成任何几何。");
            }
            else
            {
                RhinoApp.WriteLine("已取消，未生成任何几何。");
            }

            _closed = true;
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            try { _conduit.Enabled = false; } catch { }
            try { RhinoDoc.ReplaceRhinoObject -= OnReplaceObject; } catch { }
            try { RhinoDoc.DeleteRhinoObject -= OnDeleteObject; } catch { }
            _live.Remove(this);
            try { _doc.Views.Redraw(); } catch { }
        }
    }
}
