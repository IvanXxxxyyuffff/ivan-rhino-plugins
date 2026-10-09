using System;
using System.Collections.Generic;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace VapeVolume.Core
{
    /// <summary>活标注的参数——标注自己记着这套参数，随后自己重算。</summary>
    public sealed class LiveSpec
    {
        public Guid SourceId;

        /// <summary>"cap" = 容量标注；"dim" = 长宽高标注。</summary>
        public string Kind = LiveAnnotations.KindCapacity;

        public bool CupMode = true;
        public double WallMm = 0.6;

        /// <summary>-1 表示「按最终容量自动推荐」。</summary>
        public int RatePercent = -1;

        public bool CoilOn = true;
        public double CoilDiameterMm = 5.0;
        public int CoilAxisIndex = 2;
        public int Decimals = 3;

        public LiveSpec Clone()
        {
            return new LiveSpec
            {
                SourceId = SourceId,
                Kind = Kind,
                CupMode = CupMode,
                WallMm = WallMm,
                RatePercent = RatePercent,
                CoilOn = CoilOn,
                CoilDiameterMm = CoilDiameterMm,
                CoilAxisIndex = CoilAxisIndex,
                Decimals = Decimals
            };
        }

        public bool SameAs(LiveSpec other)
        {
            if (other == null) return false;
            return SourceId == other.SourceId
                   && Kind == other.Kind
                   && CupMode == other.CupMode
                   && Math.Abs(WallMm - other.WallMm) < 1.0e-9
                   && RatePercent == other.RatePercent
                   && CoilOn == other.CoilOn
                   && Math.Abs(CoilDiameterMm - other.CoilDiameterMm) < 1.0e-9
                   && CoilAxisIndex == other.CoilAxisIndex
                   && Decimals == other.Decimals;
        }
    }

    /// <summary>
    /// 活标注：会自己更新的 TextDot。
    ///   · 容量标注：记着源物件 + 参数，物件一改尺寸就重算毫升数；
    ///   · 长宽高标注：跟着物件包围盒走，实时显示长×宽×高。
    /// 位置都跟着物件包围盒中心（长宽高那个往上偏一点，避免和容量标注重叠）。
    /// </summary>
    public static class LiveAnnotations
    {
        public const string KindCapacity = "cap";
        public const string KindDimensions = "dim";

        public const string KeyMark = "VapeVolume.Live";
        public const string KeyKind = "VapeVolume.Kind";
        public const string KeySource = "VapeVolume.Source";
        public const string KeyMode = "VapeVolume.Mode";
        public const string KeyWall = "VapeVolume.Wall";
        public const string KeyRate = "VapeVolume.Rate";
        public const string KeyCoil = "VapeVolume.Coil";
        public const string KeyDia = "VapeVolume.CoilDia";
        public const string KeyAxis = "VapeVolume.CoilAxis";
        public const string KeyDecimals = "VapeVolume.Decimals";

        const string DeadText = "（源物件已删除）";

        sealed class CacheEntry
        {
            public string Key = "";
            public string Text = "";
            public Point3d Point = Point3d.Unset;
        }

        static readonly Dictionary<Guid, CacheEntry> Cache = new Dictionary<Guid, CacheEntry>();

        // ─────────────────────────── 创建 / 更新 ───────────────────────────

        /// <summary>
        /// 同一个源物件同一类标注只保留一个：已有就更新（参数变了则重建），没有就新建。
        /// 返回标注的 Guid。
        /// </summary>
        public static Guid CreateOrUpdate(RhinoDoc doc, LiveSpec spec, Point3d point, string text)
        {
            if (doc == null || spec == null) return Guid.Empty;

            try
            {
                Guid existing = FindBySource(doc, spec.SourceId, spec.Kind);
                if (existing != Guid.Empty)
                {
                    RhinoObject ro = doc.Objects.FindId(existing);
                    if (ro != null)
                    {
                        LiveSpec old = SpecFrom(ro.Attributes);
                        if (old != null && old.SameAs(spec))
                        {
                            // 参数没变，只更新文字与位置
                            var dot = ro.Geometry as TextDot;
                            if (dot != null)
                            {
                                bool need = false;
                                if (!string.IsNullOrEmpty(text) && dot.Text != text) { dot.Text = text; need = true; }
                                if (point.IsValid && dot.Point.DistanceTo(point) > 1.0e-9) { dot.Point = point; need = true; }
                                if (need) ro.CommitChanges();
                                return existing;
                            }
                        }
                        else
                        {
                            // 参数变了：删掉重建，保证标注上的记录跟界面一致
                            try { doc.Objects.Delete(existing, true); } catch { }
                        }
                    }
                }

                return Create(doc, spec, point, text);
            }
            catch
            {
                return Guid.Empty;
            }
        }

        public static Guid Create(RhinoDoc doc, LiveSpec spec, Point3d point, string text)
        {
            if (doc == null || spec == null) return Guid.Empty;
            try
            {
                var attrs = new ObjectAttributes { Name = spec.Kind == KindDimensions ? "烟油尺寸标注" : "烟油容量标注" };
                attrs.SetUserString(KeyMark, "1");
                attrs.SetUserString(KeyKind, string.IsNullOrEmpty(spec.Kind) ? KindCapacity : spec.Kind);
                attrs.SetUserString(KeySource, spec.SourceId.ToString());
                attrs.SetUserString(KeyMode, spec.CupMode ? "cup" : "oil");
                attrs.SetUserString(KeyWall, spec.WallMm.ToString(CultureInfo.InvariantCulture));
                attrs.SetUserString(KeyRate, spec.RatePercent.ToString(CultureInfo.InvariantCulture));
                attrs.SetUserString(KeyCoil, spec.CoilOn ? "1" : "0");
                attrs.SetUserString(KeyDia, spec.CoilDiameterMm.ToString(CultureInfo.InvariantCulture));
                attrs.SetUserString(KeyAxis, spec.CoilAxisIndex.ToString(CultureInfo.InvariantCulture));
                attrs.SetUserString(KeyDecimals, spec.Decimals.ToString(CultureInfo.InvariantCulture));

                try
                {
                    attrs.LayerIndex = doc.Layers.CurrentLayerIndex;
                    if (spec.Kind == KindDimensions)
                    {
                        attrs.ObjectColor = System.Drawing.Color.FromArgb(70, 110, 200);
                    }
                    else
                    {
                        attrs.ObjectColor = System.Drawing.Color.FromArgb(0, 150, 90);
                    }
                    attrs.ColorSource = ObjectColorSource.ColorFromObject;
                }
                catch
                {
                    // 颜色/图层设置失败不影响标注
                }

                var dot = new TextDot(string.IsNullOrEmpty(text) ? "…" : text, point);
                return doc.Objects.AddTextDot(dot, attrs);
            }
            catch
            {
                return Guid.Empty;
            }
        }

        // ─────────────────────────── 查询 ───────────────────────────

        /// <summary>读来源物件 Guid；不是活标注返回 Guid.Empty。</summary>
        public static Guid ReadSourceId(ObjectAttributes attrs)
        {
            if (attrs == null || attrs.GetUserString(KeyMark) != "1") return Guid.Empty;
            string s = attrs.GetUserString(KeySource);
            Guid id;
            if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out id)) return Guid.Empty;
            return id;
        }

        public static string ReadKind(ObjectAttributes attrs)
        {
            if (attrs == null || attrs.GetUserString(KeyMark) != "1") return null;
            string k = attrs.GetUserString(KeyKind);
            return string.IsNullOrEmpty(k) ? KindCapacity : k;
        }

        /// <summary>把活标注上记录的参数读回来（双击标注重开界面时用）。</summary>
        public static LiveSpec SpecFrom(ObjectAttributes attrs)
        {
            Guid id = ReadSourceId(attrs);
            if (id == Guid.Empty) return null;

            string kind = ReadKind(attrs);
            return new LiveSpec
            {
                SourceId = id,
                Kind = string.IsNullOrEmpty(kind) ? KindCapacity : kind,
                CupMode = attrs.GetUserString(KeyMode) != "oil",
                WallMm = Parse(attrs.GetUserString(KeyWall), 0.6),
                RatePercent = (int)Parse(attrs.GetUserString(KeyRate), -1),
                CoilOn = attrs.GetUserString(KeyCoil) == "1",
                CoilDiameterMm = Parse(attrs.GetUserString(KeyDia), 5.0),
                CoilAxisIndex = (int)Parse(attrs.GetUserString(KeyAxis), 2),
                Decimals = (int)Parse(attrs.GetUserString(KeyDecimals), 3)
            };
        }

        public static Guid FindBySource(RhinoDoc doc, Guid sourceId, string kind)
        {
            if (doc == null || sourceId == Guid.Empty) return Guid.Empty;
            try
            {
                foreach (var obj in doc.Objects)
                {
                    if (obj == null || obj.IsDeleted) continue;
                    if (obj.ObjectType != ObjectType.TextDot) continue;

                    LiveSpec spec = SpecFrom(obj.Attributes);
                    if (spec == null) continue;
                    if (spec.SourceId == sourceId && (spec.Kind ?? KindCapacity) == (kind ?? KindCapacity))
                        return obj.Id;
                }
            }
            catch
            {
                // 查询失败按没有处理
            }
            return Guid.Empty;
        }

        // ─────────────────────────── 刷新 ───────────────────────────

        /// <summary>重算所有活标注；返回是否有内容变化。</summary>
        public static bool Refresh(RhinoDoc doc)
        {
            if (doc == null) return false;

            bool changed = false;
            try
            {
                List<RhinoObject> dots = null;
                foreach (var obj in doc.Objects)
                {
                    if (obj == null || obj.IsDeleted) continue;
                    if (obj.ObjectType != ObjectType.TextDot) continue;
                    if (obj.Attributes == null || obj.Attributes.GetUserString(KeyMark) != "1") continue;

                    if (dots == null) dots = new List<RhinoObject>();
                    dots.Add(obj);
                }

                if (dots == null) return false;

                foreach (var dotObj in dots)
                {
                    if (RefreshOne(doc, dotObj)) changed = true;
                }
            }
            catch
            {
                // 刷新异常不影响 Rhino
            }
            return changed;
        }

        static bool RefreshOne(RhinoDoc doc, RhinoObject dotObj)
        {
            try
            {
                var dot = dotObj.Geometry as TextDot;
                if (dot == null) return false;

                LiveSpec spec = SpecFrom(dotObj.Attributes);
                if (spec == null) return false;

                RhinoObject source = doc.Objects.FindId(spec.SourceId);
                if (source == null || source.IsDeleted)
                {
                    if (dot.Text == DeadText) return false;
                    dot.Text = DeadText;
                    dotObj.CommitChanges();
                    return true;
                }

                Point3d point;
                string text = spec.Kind == KindDimensions
                    ? DimensionsText(doc, source, out point)
                    : CapacityText(doc, source, spec, out point);

                if (text == null) return false;

                bool need = false;
                if (dot.Text != text)
                {
                    dot.Text = text;
                    need = true;
                }
                if (point.IsValid && dot.Point.DistanceTo(point) > 1.0e-9)
                {
                    dot.Point = point;
                    need = true;
                }

                if (!need) return false;
                dotObj.CommitChanges();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 容量标注的位置：物件包围盒 +X/+Y 方向的外侧（不压在物件身上）。
        /// </summary>
        public static Point3d CapacityLabelPoint(BoundingBox bb)
        {
            if (!bb.IsValid) return Point3d.Origin;
            double m = MarginOf(bb);
            return new Point3d(bb.Max.X + m, bb.Max.Y + m, bb.Center.Z);
        }

        /// <summary>
        /// 尺寸标注的位置：物件包围盒 -X/-Y 方向的外侧（与容量标注方向相反，任何视角都不会重叠）。
        /// </summary>
        public static Point3d DimensionsLabelPoint(BoundingBox bb)
        {
            if (!bb.IsValid) return Point3d.Origin;
            double m = MarginOf(bb);
            return new Point3d(bb.Min.X - m, bb.Min.Y - m, bb.Center.Z);
        }

        static double MarginOf(BoundingBox bb)
        {
            double diag = bb.Diagonal.Length;
            double m = diag * 0.10;
            double floor = VolumeEngine.SafeTolerance() * 20.0;
            return m > floor ? m : floor;
        }

        /// <summary>长宽高标注：包围盒的长(X) × 宽(Y) × 高(Z)，按文档单位显示。
        /// 位置放在包围盒之外的左下外侧，与容量标注分居两侧。</summary>
        public static string DimensionsText(RhinoDoc doc, RhinoObject source, out Point3d labelPoint)
        {
            labelPoint = Point3d.Unset;
            try
            {
                var geom = source.Geometry;
                if (geom == null) return null;

                BoundingBox bb = geom.GetBoundingBox(true);
                if (!bb.IsValid) return null;

                double dx = bb.Max.X - bb.Min.X;
                double dy = bb.Max.Y - bb.Min.Y;
                double dz = bb.Max.Z - bb.Min.Z;

                labelPoint = DimensionsLabelPoint(bb);

                UnitSystem unit = doc.ModelUnitSystem;
                if (unit == UnitSystem.None) unit = UnitSystem.Millimeters;

                return string.Format(CultureInfo.InvariantCulture, "长 {0} × 宽 {1} × 高 {2}",
                    Fmt.FormatLength(dx, unit, 2),
                    Fmt.FormatLength(dy, unit, 2),
                    Fmt.FormatLength(dz, unit, 2));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>容量标注：按标注记录的参数重算油量。</summary>
        static string CapacityText(RhinoDoc doc, RhinoObject source, LiveSpec spec, out Point3d labelPoint)
        {
            labelPoint = Point3d.Unset;
            try
            {
                var geom = source.Geometry;
                if (geom == null) return null;

                BoundingBox bb = geom.GetBoundingBox(true);
                if (bb.IsValid) labelPoint = CapacityLabelPoint(bb);

                string geomKey = GeometryKey(source, bb);
                CacheEntry cached;
                if (Cache.TryGetValue(spec.SourceId, out cached) && cached != null && cached.Key == geomKey
                    && cached.Text != null && !cached.Text.StartsWith("长 ", StringComparison.Ordinal))
                {
                    if (cached.Point.IsValid) labelPoint = cached.Point;
                    return cached.Text;
                }

                var st = new VolumeSettings
                {
                    CupMode = spec.CupMode,
                    WallMm = spec.WallMm,
                    CoilEnabled = spec.CoilOn,
                    CoilDiameterMm = spec.CoilDiameterMm,
                    CoilAxisIndex = spec.CoilAxisIndex,
                    Decimals = spec.Decimals
                };

                UnitContext units = st.CreateUnitContext(doc);
                MeshingParameters mp = st.Meshing;
                VolumeReport rep = VolumeEngine.Compute(source, units, mp);
                if (rep == null || !rep.Ok) return null;

                double baseMl;
                Brep liquidSolid;

                if (spec.CupMode)
                {
                    CupResult cup = VolumeEngine.ComputeCup(source, units, spec.WallMm, mp);
                    if (cup == null || !cup.Ok) return null;
                    baseMl = cup.BaseMl;
                    liquidSolid = cup.LiquidSolid;
                }
                else
                {
                    baseMl = rep.HasCavity ? rep.LiquidMl : rep.MaterialMl;
                    liquidSolid = OilSolid(doc, source, rep, mp);
                }

                double coilMl = 0.0;
                if (spec.CoilOn)
                {
                    CoilResult coil = VolumeEngine.ComputeCoil(liquidSolid, bb, spec.CoilDiameterMm,
                                                               st.CoilDirection, units, mp);
                    if (coil != null && coil.Ok) coilMl = coil.VolumeMl;
                }

                double net = Math.Max(0.0, baseMl - coilMl);

                // 转换率：-1 表示按「最终容量」反推
                int ratePercent = spec.RatePercent < 0
                    ? Fmt.RecommendRateForNet(net)
                    : Fmt.Clamp(spec.RatePercent, 50, 75);

                double rate = ratePercent / 100.0;
                string text = Fmt.Ml(net * rate, spec.Decimals);

                if (Cache.Count > 256) Cache.Clear();
                Cache[spec.SourceId] = new CacheEntry { Key = geomKey, Text = text, Point = labelPoint };
                return text;
            }
            catch
            {
                return null;
            }
        }

        static Brep OilSolid(RhinoDoc doc, RhinoObject source, VolumeReport rep, MeshingParameters mp)
        {
            try
            {
                Brep brep = VolumeEngine.ToBrepAny(source.Geometry);
                if (brep == null) return null;
                if (rep.HasCavity)
                {
                    Brep cavity = VolumeEngine.FindCavitySolid(brep, VolumeEngine.SafeTolerance(), mp);
                    if (cavity != null) return cavity;
                }
                return brep;
            }
            catch
            {
                return null;
            }
        }

        static string GeometryKey(RhinoObject obj, BoundingBox bb)
        {
            try
            {
                uint serial = 0;
                try { serial = obj.RuntimeSerialNumber; } catch { serial = 0; }
                if (!bb.IsValid) return serial.ToString(CultureInfo.InvariantCulture);
                Point3d c = bb.Center;
                return string.Format(CultureInfo.InvariantCulture, "{0};{1:F5},{2:F5},{3:F5};{4:F5}",
                    serial, c.X, c.Y, c.Z, bb.Diagonal.Length);
            }
            catch
            {
                return "?";
            }
        }

        static double Parse(string s, double fallback)
        {
            double v;
            if (!string.IsNullOrEmpty(s)
                && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }
    }

    /// <summary>
    /// 活标注刷新驱动：Rhino 空闲事件 + 文档增删改事件 + 双击标注。
    /// 文档一变就标脏，空闲时（节流）统一重算，只在内容变化时重绘。
    /// </summary>
    public static class LiveAnnotationWatch
    {
        static bool _installed;
        static bool _dirty = true;
        static int _lastTick;
        static DoubleClickWatcher _mouse;

        /// <summary>MouseCallback 是抽象类，必须继承并重写双击回调。</summary>
        sealed class DoubleClickWatcher : Rhino.UI.MouseCallback
        {
            protected override void OnMouseDoubleClick(Rhino.UI.MouseCallbackEventArgs e)
            {
                try
                {
                    HandleDoubleClick(e);
                }
                catch
                {
                    // 双击处理异常不影响 Rhino
                }
            }
        }

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                RhinoApp.Idle += OnIdle;
                RhinoDoc.AddRhinoObject += OnDocEvent;
                RhinoDoc.DeleteRhinoObject += OnDocEvent;
                RhinoDoc.ReplaceRhinoObject += OnReplaceEvent;
                InstallDoubleClick();
            }
            catch
            {
                _installed = false;
            }
        }

        static void InstallDoubleClick()
        {
            try
            {
                _mouse = new DoubleClickWatcher();
                _mouse.Enabled = true;
            }
            catch
            {
                _mouse = null;
            }
        }

        /// <summary>双击模型上的活标注 → 重新打开插件界面。</summary>
        static void HandleDoubleClick(Rhino.UI.MouseCallbackEventArgs e)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null || e == null || e.View == null) return;

            Rhino.Display.RhinoViewport vp = e.View.ActiveViewport;
            if (vp == null) return;

            Line ray;
            if (!vp.GetFrustumLine(e.ViewportPoint.X, e.ViewportPoint.Y, out ray)) return;

            Rhino.DocObjects.RhinoObject best = null;
            LiveSpec bestSpec = null;
            double bestDistance = double.MaxValue;

            foreach (var obj in doc.Objects)
            {
                if (obj == null || obj.IsDeleted) continue;
                if (obj.ObjectType != ObjectType.TextDot) continue;

                LiveSpec spec = LiveAnnotations.SpecFrom(obj.Attributes);
                if (spec == null) continue;

                var dot = obj.Geometry as TextDot;
                if (dot == null) continue;

                double t;
                var rayCurve = new LineCurve(ray);
                Point3d closest = rayCurve.ClosestPoint(dot.Point, out t)
                    ? rayCurve.PointAt(t)
                    : ray.From;
                double distance = closest.DistanceTo(dot.Point);

                // 容差按源物件包围盒的 2% 走（标注就画在那儿，双击基本落在点上）
                double tol = 0.05;
                try
                {
                    Rhino.DocObjects.RhinoObject src = doc.Objects.FindId(spec.SourceId);
                    if (src != null && src.Geometry != null)
                    {
                        BoundingBox bb = src.Geometry.GetBoundingBox(true);
                        if (bb.IsValid)
                            tol = Math.Max(bb.Diagonal.Length * 0.02, VolumeEngine.SafeTolerance() * 5.0);
                    }
                }
                catch
                {
                    // 容差取不到就用默认值
                }

                if (distance <= tol && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = obj;
                    bestSpec = spec;
                }
            }

            if (best == null || bestSpec == null) return;

            RhinoApp.WriteLine("打开活标注对应的容量窗口…");
            UI.VolumeDialog.OpenFor(doc, bestSpec);
        }

        static void OnDocEvent(object sender, RhinoObjectEventArgs e)
        {
            _dirty = true;
        }

        static void OnReplaceEvent(object sender, RhinoReplaceObjectEventArgs e)
        {
            _dirty = true;
        }

        static void OnIdle(object sender, EventArgs e)
        {
            int now = System.Environment.TickCount;
            if (!_dirty && now - _lastTick < 400) return;
            _lastTick = now;
            _dirty = false;

            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null) return;

                if (LiveAnnotations.Refresh(doc)) doc.Views.Redraw();

                // 容量窗口开着的话，也跟着重算（改尺寸时扣壁厚预览、数字要实时跟着变）
                UI.VolumeDialog openDialog = UI.VolumeDialog.Current;
                if (openDialog != null) openDialog.RefreshFromDocument();
            }
            catch
            {
                // 刷新异常不影响 Rhino
            }
        }
    }
}
