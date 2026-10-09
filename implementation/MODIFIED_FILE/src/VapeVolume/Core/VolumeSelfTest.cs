using System;
using System.Collections.Generic;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace VapeVolume.Core
{
    /// <summary>
    /// 自检：在当前文档里临时造几个形体，把核心算法跑一遍并记录结果。
    /// 只用到 Rhino 7 / 8 都有的 API，所以两个版本都能跑。
    /// </summary>
    public static class VolumeSelfTest
    {
        public static void Run(RhinoDoc doc, List<string> log, List<Guid> created)
        {
            MeshingParameters mp = MeshingParameters.Default;
            var units = new UnitContext(doc.ModelUnitSystem, null);
            double tol = doc.ModelAbsoluteTolerance;

            log.Add(string.Format(CultureInfo.InvariantCulture,
                "文档单位 {0}；公差 {1}", UnitContext.Name(units.EffectiveUnits), tol));

            // ── A. 实心长方体 20×10×5 = 1000 mm³ = 1 mL ──
            Brep boxA = new Box(Plane.WorldXY, new Interval(0, 20), new Interval(0, 10), new Interval(0, 5)).ToBrep();
            Guid idA = doc.Objects.AddBrep(boxA);
            created.Add(idA);
            VolumeReport ra = VolumeEngine.Compute(doc.Objects.FindId(idA), units, mp);
            log.Add("");
            log.Add("A 实心长方体 20×10×5");
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "   Ok={0} 材料={1:F6} mL 内腔={2:F6} 外形={3:F6} 壳数={4} 闭合={5}",
                ra.Ok, ra.MaterialMl, ra.LiquidMl, ra.EnvelopeMl, ra.ShellCount, ra.Closed));
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "   期望 材料 1.000000 / 内腔 0.000000 / 外形 1.000000　→ {0}",
                Math.Abs(ra.MaterialMl - 1.0) < 0.001 && ra.LiquidMl < 0.001 ? "通过" : "不对"));

            // ── B. 空心盒：外 20×10×10 减 内 16×6×6 ──
            Brep outer = new Box(Plane.WorldXY, new Interval(0, 20), new Interval(0, 10), new Interval(0, 10)).ToBrep();
            Brep inner = new Box(Plane.WorldXY, new Interval(2, 18), new Interval(2, 8), new Interval(2, 8)).ToBrep();
            log.Add("");
            try
            {
                Brep[] diff = Brep.CreateBooleanDifference(new[] { outer }, new[] { inner }, tol);
                if (diff != null && diff.Length > 0)
                {
                    Guid idB = doc.Objects.AddBrep(diff[0]);
                    created.Add(idB);
                    VolumeReport rb = VolumeEngine.Compute(doc.Objects.FindId(idB), units, mp);
                    log.Add("B 空心盒（外 20×10×10 减 内 16×6×6）");
                    log.Add(string.Format(CultureInfo.InvariantCulture,
                        "   Ok={0} 材料={1:F6} mL 内腔={2:F6} 外形={3:F6} 壳数={4} 有内腔={5}",
                        rb.Ok, rb.MaterialMl, rb.LiquidMl, rb.EnvelopeMl, rb.ShellCount, rb.HasCavity));
                    log.Add(string.Format(CultureInfo.InvariantCulture,
                        "   期望 内腔 0.576000 / 材料 1.424000 / 外形 2.000000　→ {0}",
                        Math.Abs(rb.LiquidMl - 0.576) < 0.005 && Math.Abs(rb.MaterialMl - 1.424) < 0.005 ? "通过" : "不对"));
                }
                else
                {
                    log.Add("B 布尔差集失败，跳过");
                }
            }
            catch (Exception ex)
            {
                log.Add("B 异常：" + ex.Message);
            }

            // ── C. 油杯：实心圆柱 D10×H20，扣 0.6mm 壁厚 ──
            Brep cyl = new Cylinder(new Circle(Plane.WorldXY, 5.0), 20.0).ToBrep(true, true);
            Guid idC = doc.Objects.AddBrep(cyl);
            created.Add(idC);
            CupResult cup = VolumeEngine.ComputeCup(doc.Objects.FindId(idC), units, 0.6, mp);
            double expectCore = Math.PI * Math.Pow(5.0 - 0.6, 2.0) * (20.0 - 1.2) / 1000.0;
            log.Add("");
            log.Add("C 油杯：圆柱 D10×H20，壁厚 0.6mm");
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "   Ok={0} 基准容积={1:F6} mL 近似={2} 用已有内腔={3}",
                cup.Ok, cup.BaseMl, cup.Approximate, cup.UsedExistingCavity));
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "   理论值 {0:F6} mL　→ {1}",
                expectCore,
                cup.Ok && Math.Abs(cup.BaseMl - expectCore) / expectCore < 0.05 ? "通过（误差<5%）" : "偏差较大"));
            if (!cup.Ok) log.Add("   错误：" + cup.Error);
            log.Add("   说明：" + cup.Note);

            // ── D. 雾化芯 ⌀5mm ──
            log.Add("");
            if (cup.Ok)
            {
                BoundingBox bb = cyl.GetBoundingBox(true);
                CoilResult coil = VolumeEngine.ComputeCoil(cup.LiquidSolid, bb, 5.0, CoilAxis.Z, units, mp);
                double expectCoil = Math.PI * 2.5 * 2.5 * 18.8 / 1000.0;
                log.Add("D 雾化芯 ⌀5mm（轴向 Z）");
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "   Ok={0} 雾化芯体积={1:F6} mL 长度={2:F2} mm 近似={3}",
                    coil.Ok, coil.VolumeMl, coil.LengthMm, coil.Approximate));
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "   理论值 {0:F6} mL　→ {1}",
                    expectCoil,
                    coil.Ok && Math.Abs(coil.VolumeMl - expectCoil) / expectCoil < 0.05 ? "通过（误差<5%）" : "偏差较大"));
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "   净容积 = {0:F6} mL", Math.Max(0.0, cup.BaseMl - coil.VolumeMl)));
                log.Add("   说明：" + coil.Note);
            }
            else
            {
                log.Add("D 油杯模式没算出来，跳过雾化芯测试");
            }

            // ── E. 活标注：改尺寸后是否自动更新 ──
            log.Add("");
            try
            {
                var spec = new LiveSpec
                {
                    SourceId = idC,
                    CupMode = true,
                    WallMm = 0.6,
                    RatePercent = -1,          // 自动推荐
                    CoilOn = true,
                    CoilDiameterMm = 5.0,
                    CoilAxisIndex = 2,
                    Decimals = 3
                };

                RhinoObject src = doc.Objects.FindId(idC);
                Point3d pt = src.Geometry.GetBoundingBox(true).Center;
                Guid dotId = LiveAnnotations.Create(doc, spec, pt, "自检");
                LiveAnnotations.Refresh(doc);
                string before = TextOf(doc, dotId);

                // 把对象放大一倍（几何直接变换 + 提交），模拟用户改尺寸
                GeometryBase g = src.Geometry;
                g.Transform(Transform.Scale(Point3d.Origin, 2.0));
                src.CommitChanges();

                LiveAnnotations.Refresh(doc);
                string after = TextOf(doc, dotId);

                log.Add("E 活标注（物件放大一倍后是否自动更新）");
                log.Add("   缩放前：" + before);
                log.Add("   缩放后：" + after);
                log.Add("   结果：" + (!string.IsNullOrEmpty(before) && before != after ? "自动更新成功" : "未更新（不对）"));
                if (!string.IsNullOrEmpty(dotId.ToString())) created.Add(dotId);
            }
            catch (Exception ex)
            {
                log.Add("E 活标注异常：" + ex.Message);
            }

            // ── F. 转换率推荐规则 ──
            log.Add("");
            log.Add("F 转换率推荐规则");
            double[] vals = { 1.0, 9.99, 10.0, 19.99, 20.0, 29.99, 30.0, 120.0 };
            int[] expect = { 75, 75, 70, 70, 65, 65, 60, 60 };
            bool allOk = true;
            for (int i = 0; i < vals.Length; i++)
            {
                int got = Fmt.RecommendRate(vals[i]);
                bool ok = got == expect[i];
                allOk = allOk && ok;
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "   {0,7} mL → {1}%（期望 {2}%）{3}", vals[i], got, expect[i], ok ? "" : "  ← 不对"));
            }
            log.Add("   规则全部正确：" + (allOk ? "是" : "否"));

            log.Add("");
            log.Add("DONE");
        }

        static string TextOf(RhinoDoc doc, Guid dotId)
        {
            try
            {
                RhinoObject ro = doc.Objects.FindId(dotId);
                if (ro == null) return "";
                var dot = ro.Geometry as TextDot;
                return dot != null ? dot.Text : "";
            }
            catch
            {
                return "";
            }
        }
    }
}
