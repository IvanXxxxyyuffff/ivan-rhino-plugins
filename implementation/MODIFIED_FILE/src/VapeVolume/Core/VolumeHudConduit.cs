using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace VapeVolume.Core
{
    /// <summary>
    /// 实时 HUD：把悬停（预选高亮）/ 选中物件的容量画在视口上。
    ///
    /// 分成两半，避免在绘制回调里做重活：
    ///   · Refresh()   在主线程（RhinoApp.Idle）里扫描物件并计算，结果存成待绘制的文本；
    ///   · DrawForeground() 只画已经算好的文本，不碰文档、不算几何。
    /// </summary>
    public sealed class VolumeHudConduit : DisplayConduit
    {
        sealed class Item
        {
            public string Text = "";
            public Color Color;
            public Point3d Point;
        }

        sealed class CacheEntry
        {
            public string Key = "";
            public VolumeReport Report;
        }

        readonly Dictionary<Guid, CacheEntry> _cache = new Dictionary<Guid, CacheEntry>();
        List<Item> _items = new List<Item>();

        public RhinoDoc Doc;
        public VolumeSettings Settings;

        /// <summary>是否显示悬停（预选高亮）物件的容量。</summary>
        public bool WatchHighlighted = true;

        /// <summary>是否显示已选中物件的容量。</summary>
        public bool WatchSelected = true;

        /// <summary>单次刷新最多标注的物件数量。</summary>
        public int MaxLabels = 24;

        static readonly Color HoverColor = Color.FromArgb(255, 214, 64);
        static readonly Color SelectedColor = Color.FromArgb(126, 232, 152);

        protected override void DrawForeground(DrawEventArgs e)
        {
            // 只画预计算好的内容：这里不访问文档、不计算几何
            try
            {
                var items = _items;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    e.Display.Draw2dText(it.Text, it.Color, it.Point, true);
                }
            }
            catch
            {
                // 绘制异常不得影响 Rhino 主循环
            }
        }

        /// <summary>
        /// 在主线程刷新待绘内容。返回 true 表示显示内容有变化（调用方据此决定要不要重绘）。
        /// </summary>
        public bool Refresh(RhinoDoc doc)
        {
            var list = new List<Item>();

            try
            {
                if (doc != null && Settings != null)
                {
                    UnitContext units = Settings.CreateUnitContext(doc);
                    MeshingParameters mp = Settings.Meshing;
                    string sig = Settings.Signature(doc);

                    foreach (RhinoObject obj in doc.Objects)
                    {
                        if (obj == null || obj.IsDeleted || !obj.Visible) continue;

                        bool hot = WatchHighlighted && IsHighlighted(obj);
                        bool selected = !hot && WatchSelected && IsSelected(obj);
                        if (!hot && !selected) continue;

                        VolumeReport rep = GetReport(obj, units, mp, sig);
                        if (rep == null || !rep.Ok) continue;

                        list.Add(new Item
                        {
                            Text = BuildLine(rep, Settings),
                            Color = hot ? HoverColor : SelectedColor,
                            Point = rep.LabelPoint
                        });

                        if (list.Count >= MaxLabels) break;
                    }
                }
            }
            catch
            {
                // 刷新失败时退化为空列表
            }

            bool changed = !SameAs(list, _items);
            if (changed) _items = list;
            return changed;
        }

        public void InvalidateCache()
        {
            _cache.Clear();
        }

        static bool SameAs(List<Item> a, List<Item> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Text != b[i].Text) return false;
                if (a[i].Point != b[i].Point) return false;
                if (a[i].Color != b[i].Color) return false;
            }
            return true;
        }

        VolumeReport GetReport(RhinoObject obj, UnitContext units, MeshingParameters mp, string settingsSig)
        {
            string key = settingsSig + "|" + GeometryKey(obj);

            CacheEntry entry;
            if (_cache.TryGetValue(obj.Id, out entry) && entry != null && entry.Key == key)
                return entry.Report;

            VolumeReport rep = VolumeEngine.Compute(obj, units, mp);

            if (_cache.Count > 512) _cache.Clear();
            _cache[obj.Id] = new CacheEntry { Key = key, Report = rep };
            return rep;
        }

        /// <summary>几何指纹：运行时序号 + 包围盒，用于判断是否需要重算。</summary>
        static string GeometryKey(RhinoObject obj)
        {
            try
            {
                uint serial = 0;
                try { serial = obj.RuntimeSerialNumber; } catch { serial = 0; }

                GeometryBase geom = obj.Geometry;
                if (geom == null) return serial.ToString(CultureInfo.InvariantCulture);

                BoundingBox bb = geom.GetBoundingBox(true);
                if (!bb.IsValid) return serial.ToString(CultureInfo.InvariantCulture);

                Point3d c = bb.Center;
                int extra = 0;
                var m = geom as Mesh;
                if (m != null)
                {
                    try { extra = m.Vertices.Count; } catch { extra = 0; }
                }

                return string.Format(CultureInfo.InvariantCulture,
                    "{0};{1:F5},{2:F5},{3:F5};{4:F5};{5}",
                    serial, c.X, c.Y, c.Z, bb.Diagonal.Length, extra);
            }
            catch
            {
                return "?";
            }
        }

        static bool IsSelected(RhinoObject obj)
        {
            try { return obj.IsSelected(false) != 0; } catch { return false; }
        }

        static bool IsHighlighted(RhinoObject obj)
        {
            try { return obj.IsHighlighted(false) != 0; } catch { return false; }
        }

        public static string BuildLine(VolumeReport rep, VolumeSettings st)
        {
            string name = rep.Name ?? "";
            if (name.Length > 16) name = name.Substring(0, 16) + "…";
            double value = rep.PrimaryMl(st.Mode);
            string prefix = rep.Approximate ? "≈" : "";
            return name + "  " + rep.PrimaryLabel(st.Mode) + " " + prefix + Fmt.Ml(value, st.Decimals);
        }
    }
}
