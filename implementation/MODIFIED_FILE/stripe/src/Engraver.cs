using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace StripeOnSurface
{
    /// <summary>把条纹曲线变成凹槽：沿面法向做棱柱，与原实体布尔差集</summary>
    public static class Engraver
    {
        public const double Tol = 0.005;

        public static Brep Engrave(Brep target, List<StripeFaceResult> results, double depth, out string error)
        {
            error = null;
            if (target == null) { error = "目标为空"; return null; }
            if (!(depth > 1e-6)) { error = "深度无效"; return null; }
            if (!target.IsSolid) { error = "目标不是封闭实体"; return null; }

            var cutters = new List<Brep>();
            int skippedFaces = 0;
            double clearance = Math.Max(depth, 0.5) + 1.0;

            foreach (StripeFaceResult r in results)
            {
                if (!r.Planar) { if (r.Stripes.Count > 0) skippedFaces++; continue; }
                Vector3d n = r.Plane.ZAxis;
                foreach (Curve c in r.Stripes)
                {
                    try
                    {
                        Extrusion ex = Extrusion.Create(c, depth + clearance, true);
                        if (ex == null) continue;
                        Brep bp = ex.ToBrep();
                        if (bp == null || !bp.IsValid) continue;

                        BoundingBox bb = bp.GetBoundingBox(true);
                        Point3d[] corners = bb.GetCorners();
                        double minProj = double.MaxValue;
                        for (int i = 0; i < corners.Length; i++)
                        {
                            double t = (corners[i] - r.Plane.Origin) * n;
                            if (t < minProj) minProj = t;
                        }
                        if (minProj == double.MaxValue) continue;
                        bp.Transform(Transform.Translation(n * (-depth - minProj)));
                        cutters.Add(bp);
                    }
                    catch { }
                }
            }

            if (cutters.Count == 0)
            {
                error = "没有可用于开槽的条纹（仅平面支持开槽）";
                return null;
            }

            Brep[] res = Brep.CreateBooleanDifference(new Brep[] { target }, cutters, Tol);
            if (res == null || res.Length == 0)
            {
                error = "布尔差集失败（实体或条纹过于复杂）";
                return null;
            }
            if (skippedFaces > 0)
                error = string.Format("注意：{0} 个非平面面未开槽", skippedFaces);

            if (res.Length == 1) return res[0];
            Brep[] joined = Brep.JoinBreps(res, Tol);
            if (joined != null && joined.Length > 0) return joined[0];
            return res[0];
        }
    }
}
