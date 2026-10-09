using System;
using System.Drawing;
using System.Globalization;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace VapeVolume.Core
{
    /// <summary>
    /// 视口实时预览：把「正在被扣掉的东西」直接画在模型上。
    ///   1) 扣壁厚后的**内部核心实体**（半透明蓝色）——一眼看出扣完剩多少；
    ///   2) **雾化芯圆柱**（半透明橙色 + 两端圆 + 轴线）——避免轴选错。
    /// 只负责画，不负责算；体积仍由 VolumeEngine 计算。
    /// </summary>
    public sealed class ViewportPreviewConduit : DisplayConduit
    {
        public RhinoDoc Doc;
        public Guid SourceId = Guid.Empty;

        // 壁厚扣减后的核心实体
        public bool ShowCore;
        public Brep CoreSolid;
        Mesh _coreMesh;
        string _coreKey = "";
        bool _coreMeshed;

        // 雾化芯
        public bool ShowCoil;
        public double DiameterMm = 5.0;
        public CoilAxis Axis = CoilAxis.Z;

        static readonly Color CoreWireColor = Color.FromArgb(200, 60, 150, 235);
        static readonly Color CoilFillColor = Color.FromArgb(70, 255, 140, 40);
        static readonly Color CoilEdgeColor = Color.FromArgb(230, 255, 120, 20);
        static readonly Color AxisLineColor = Color.FromArgb(200, 220, 60, 60);

        DisplayMaterial _coreMaterial;

        DisplayMaterial CoreMaterial()
        {
            if (_coreMaterial == null)
            {
                try { _coreMaterial = new DisplayMaterial(Color.FromArgb(60, 150, 235), 0.55); }
                catch { _coreMaterial = null; }
            }
            return _coreMaterial;
        }

        /// <summary>更新预览内容（在主线程调用）。</summary>
        public void UpdatePreview(RhinoDoc doc, Guid sourceId, bool showCore, Brep core,
                                  bool showCoil, double diameterMm, CoilAxis axis)
        {
            Doc = doc;
            SourceId = sourceId;
            ShowCore = showCore;
            CoreSolid = core;
            ShowCoil = showCoil;
            DiameterMm = diameterMm;
            Axis = axis;
        }

        protected override void DrawForeground(DrawEventArgs e)
        {
            try
            {
                if (!ShowCore && !ShowCoil) return;

                RhinoDoc doc = e.RhinoDoc ?? Doc ?? RhinoDoc.ActiveDoc;
                if (doc == null) return;

                if (ShowCore) DrawCore(e);
                if (ShowCoil) DrawCoil(e, doc);
            }
            catch
            {
                // 绘制异常不得影响 Rhino
            }
        }

        void DrawCore(DrawEventArgs e)
        {
            EnsureCoreMesh();
            if (_coreMesh == null) return;

            try
            {
                DisplayMaterial mat = CoreMaterial();
                if (mat != null) e.Display.DrawMeshShaded(_coreMesh, mat);
                e.Display.DrawMeshWires(_coreMesh, CoreWireColor, 1);
            }
            catch
            {
                // 画不出来就算了
            }
        }

        /// <summary>核心实体只在变化时网格化一次，之后每帧只画。</summary>
        void EnsureCoreMesh()
        {
            string key = CoreKey(CoreSolid);
            if (_coreMeshed && _coreKey == key) return;

            _coreKey = key;
            _coreMeshed = true;

            if (_coreMesh != null)
            {
                try { _coreMesh.Dispose(); } catch { }
                _coreMesh = null;
            }
            if (CoreSolid == null) return;

            try
            {
                Mesh[] parts = Mesh.CreateFromBrep(CoreSolid, MeshingParameters.FastRenderMesh);
                if (parts == null || parts.Length == 0) return;

                var m = new Mesh();
                foreach (var p in parts)
                {
                    if (p != null) m.Append(p);
                }
                if (m.Vertices.Count > 0) _coreMesh = m;
                else m.Dispose();
            }
            catch
            {
                _coreMesh = null;
            }
        }

        static string CoreKey(Brep b)
        {
            if (b == null) return "";
            try
            {
                BoundingBox bb = b.GetBoundingBox(true);
                return string.Format(CultureInfo.InvariantCulture, "{0}|{1:F6}|{2:F6},{3:F6},{4:F6}",
                    b.Faces.Count, bb.Diagonal.Length, bb.Center.X, bb.Center.Y, bb.Center.Z);
            }
            catch
            {
                return "?";
            }
        }

        void DrawCoil(DrawEventArgs e, RhinoDoc doc)
        {
            if (!(DiameterMm > 0.0) || SourceId == Guid.Empty) return;

            RhinoObject src = doc.Objects.FindId(SourceId);
            if (src == null || src.IsDeleted) return;

            BoundingBox box = src.Geometry != null ? src.Geometry.GetBoundingBox(true) : BoundingBox.Unset;
            if (!box.IsValid) return;

            double mmPerUnit = 1.0;
            try
            {
                var units = new UnitContext(doc.ModelUnitSystem, null);
                mmPerUnit = Math.Max(units.MillimetersPerUnit, 1.0e-12);
            }
            catch
            {
                mmPerUnit = 1.0;
            }

            double radius = (DiameterMm * 0.5) / mmPerUnit;
            Vector3d dir = Axis == CoilAxis.X ? Vector3d.XAxis
                         : Axis == CoilAxis.Y ? Vector3d.YAxis
                         : Vector3d.ZAxis;

            Point3d center = box.Center;
            double halfSpan;
            switch (Axis)
            {
                case CoilAxis.X: halfSpan = (box.Max.X - box.Min.X) * 0.5; break;
                case CoilAxis.Y: halfSpan = (box.Max.Y - box.Min.Y) * 0.5; break;
                default: halfSpan = (box.Max.Z - box.Min.Z) * 0.5; break;
            }
            if (!(halfSpan > 0.0)) halfSpan = Math.Max(box.Diagonal.Length * 0.4, radius * 2.0);

            Point3d p0 = center - dir * halfSpan;
            Point3d p1 = center + dir * halfSpan;

            var cylinder = new Cylinder(new Circle(new Plane(p0, dir), radius), halfSpan * 2.0);
            e.Display.DrawCylinder(cylinder, CoilFillColor);
            e.Display.DrawCircle(new Circle(new Plane(p0, dir), radius), CoilEdgeColor);
            e.Display.DrawCircle(new Circle(new Plane(p1, dir), radius), CoilEdgeColor);
            e.Display.DrawLine(p0, p1, AxisLineColor);
        }
    }

    /// <summary>视口预览的全局开关（容量窗口打开时挂上，关掉时卸下）。</summary>
    public static class ViewportPreview
    {
        static ViewportPreviewConduit _conduit;

        public static void Show(RhinoDoc doc, Guid sourceId,
                                bool showCore, Brep core,
                                bool showCoil, double diameterMm, CoilAxis axis)
        {
            try
            {
                if (_conduit == null)
                {
                    _conduit = new ViewportPreviewConduit();
                    _conduit.Enabled = true;
                }
                _conduit.UpdatePreview(doc, sourceId, showCore, core, showCoil, diameterMm, axis);

                if (doc != null && doc.Views != null) doc.Views.Redraw();
            }
            catch
            {
                // 预览失败不影响主流程
            }
        }

        public static void Hide(RhinoDoc doc)
        {
            try
            {
                if (_conduit != null)
                {
                    _conduit.ShowCoil = false;
                    _conduit.ShowCore = false;
                    _conduit.CoreSolid = null;
                    _conduit.SourceId = Guid.Empty;
                    _conduit.Enabled = false;
                    _conduit = null;
                }
                if (doc != null && doc.Views != null) doc.Views.Redraw();
            }
            catch
            {
                // 忽略
            }
        }
    }
}
