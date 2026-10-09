using System;
using System.Globalization;
using Rhino;

namespace VapeVolume.Core
{
    /// <summary>
    /// 单位换算上下文：把「模型单位的立方」换算成毫升。
    /// 1 mL = 1 cm³ = 1000 mm³，因此只需要知道 1 个模型单位等于多少毫米。
    /// </summary>
    public sealed class UnitContext
    {
        /// <summary>文档的模型单位。</summary>
        public UnitSystem ModelUnits { get; }

        /// <summary>实际参与换算的单位（命令选项可覆盖文档单位）。</summary>
        public UnitSystem EffectiveUnits { get; }

        /// <summary>文档没有设定单位时按毫米处理。</summary>
        public bool UnitsUnspecified { get; }

        /// <summary>命令选项是否覆盖了文档单位。</summary>
        public bool Overridden { get; }

        /// <summary>1 个模型单位³ 等于多少毫升。</summary>
        public double MillilitersPerCubicUnit { get; }

        /// <summary>1 个模型单位等于多少毫米。</summary>
        public double MillimetersPerUnit { get; }

        public UnitContext(UnitSystem modelUnits, UnitSystem? unitOverride)
        {
            ModelUnits = modelUnits;
            Overridden = unitOverride.HasValue;

            UnitSystem src = unitOverride ?? modelUnits;
            UnitsUnspecified = src == UnitSystem.None;
            if (UnitsUnspecified)
                src = UnitSystem.Millimeters;
            EffectiveUnits = src;

            // RhinoMath.UnitScale(from, to) = 1 个 from 单位等于多少个 to 单位
            double mmPerUnit = 1.0;
            try
            {
                double scale = RhinoMath.UnitScale(UnitSystem.Millimeters, src);
                if (scale > 0.0 && !double.IsNaN(scale) && !double.IsInfinity(scale))
                    mmPerUnit = 1.0 / scale;
            }
            catch
            {
                mmPerUnit = 1.0;
            }

            MillimetersPerUnit = mmPerUnit;
            MillilitersPerCubicUnit = Math.Pow(mmPerUnit, 3.0) / 1000.0;
        }

        public double ToMilliliters(double cubicModelUnits)
        {
            return cubicModelUnits * MillilitersPerCubicUnit;
        }

        public string Describe()
        {
            string unit = UnitsUnspecified
                ? "毫米（文档未设定单位，按毫米处理）"
                : Name(EffectiveUnits) + (Overridden ? "（命令选项覆盖）" : "（文档单位）");

            return string.Format(CultureInfo.InvariantCulture,
                "单位：{0}；换算：1 单位³ = {1:G6} mL",
                unit, MillilitersPerCubicUnit);
        }

        public static string Name(UnitSystem u)
        {
            switch (u)
            {
                case UnitSystem.Millimeters: return "毫米 mm";
                case UnitSystem.Centimeters: return "厘米 cm";
                case UnitSystem.Meters: return "米 m";
                case UnitSystem.Kilometers: return "千米 km";
                case UnitSystem.Microns: return "微米 µm";
                case UnitSystem.Nanometers: return "纳米 nm";
                case UnitSystem.Decimeters: return "分米 dm";
                case UnitSystem.Inches: return "英寸 in";
                case UnitSystem.Feet: return "英尺 ft";
                case UnitSystem.Yards: return "码 yd";
                case UnitSystem.Miles: return "英里 mi";
                default: return u.ToString();
            }
        }
    }

    /// <summary>数值格式化。</summary>
    public static class Fmt
    {
        public static string Ml(double ml, int decimals)
        {
            decimals = Clamp(decimals, 0, 6);
            string s = ml.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + " mL";
            if (Math.Abs(ml) >= 1000.0)
            {
                int ld = Clamp(decimals - 1, 1, 6);
                s += "（" + (ml / 1000.0).ToString("N" + ld.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + " L）";
            }
            return s;
        }

        /// <summary>立方厘米数值（与毫升数值相同，单独格式化便于阅读）。</summary>
        public static string Cm3(double ml, int decimals)
        {
            decimals = Clamp(decimals, 0, 6);
            return ml.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + " cm³";
        }

        public static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static double ClampDouble(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>
        /// 按目标容量推荐转换率：
        ///   &lt; 10 mL → 75%；10–20 mL → 70%；20–30 mL → 65%；≥ 30 mL → 60%
        /// </summary>
        public static int RecommendRate(double baseMl)
        {
            if (!(baseMl > 0.0)) return 70;
            if (baseMl < 10.0) return 75;
            if (baseMl < 20.0) return 70;
            if (baseMl < 30.0) return 65;
            return 60;
        }

        /// <summary>推荐值对应的容量区间说明。</summary>
        public static string RecommendReason(double baseMl)
        {
            if (!(baseMl > 0.0)) return "容量未知";
            if (baseMl < 10.0) return "小于 10 mL";
            if (baseMl < 20.0) return "10–20 mL";
            if (baseMl < 30.0) return "20–30 mL";
            return "30 mL 以上";
        }

        /// <summary>单个数值落在哪个档位（&lt;10→75、10–20→70、20–30→65、≥30→60）。</summary>
        public static int BandRate(double ml)
        {
            if (!(ml > 0.0)) return 70;
            if (ml < 10.0) return 75;
            if (ml < 20.0) return 70;
            if (ml < 30.0) return 65;
            return 60;
        }

        /// <summary>
        /// 按「最终算出来的容量」反推转换率。
        ///
        /// 这里有个循环依赖：最终容量 = 净容积 × 转换率，而档位又由最终容量决定。
        /// 做法：从高到低试每个候选档位，取「该档位算出来的最终容量，其所在区间不低于该档位」
        /// 的最大档位——这样结果确定、不会来回跳，并且偏向保守（不会用比结果所能支撑的更高的转换率）。
        ///
        /// 例：净容积 44.927 mL
        ///   75% → 33.70（≥30 区间只给 60%）不成立
        ///   70% → 31.45（同上）不成立
        ///   65% → 29.20（落在 20–30 区间，正是 65%）✓ 取 65%
        /// </summary>
        public static int RecommendRateForNet(double netMl)
        {
            if (!(netMl > 0.0)) return 70;

            int[] candidates = { 75, 70, 65, 60 };
            for (int i = 0; i < candidates.Length; i++)
            {
                int r = candidates[i];
                double finalMl = netMl * r / 100.0;
                if (BandRate(finalMl) >= r) return r;
            }
            return 60;
        }

        /// <summary>换算成毫米后的长度文字，用于标注尺寸（按文档单位显示）。</summary>
        public static string FormatLength(double value, UnitSystem unit, int decimals)
        {
            int d = Clamp(decimals, 0, 4);
            return value.ToString("N" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                   + " " + ShortName(unit);
        }

        public static string ShortName(UnitSystem u)
        {
            switch (u)
            {
                case UnitSystem.Millimeters: return "mm";
                case UnitSystem.Centimeters: return "cm";
                case UnitSystem.Meters: return "m";
                case UnitSystem.Kilometers: return "km";
                case UnitSystem.Microns: return "µm";
                case UnitSystem.Decimeters: return "dm";
                case UnitSystem.Inches: return "in";
                case UnitSystem.Feet: return "ft";
                case UnitSystem.Yards: return "yd";
                default: return u.ToString();
            }
        }
    }
}
