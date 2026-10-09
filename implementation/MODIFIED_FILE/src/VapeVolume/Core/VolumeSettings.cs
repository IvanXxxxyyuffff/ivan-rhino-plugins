using System;
using Rhino;
using Rhino.Geometry;

namespace VapeVolume.Core
{
    /// <summary>命令选项与持久化设置的载体。</summary>
    public sealed class VolumeSettings
    {
        public static readonly string[] ModeNames = { "液体容量", "实体体积", "两者" };

        public static readonly string[] UnitNames = { "跟随文档", "毫米", "厘米", "米", "英寸", "英尺" };

        static readonly UnitSystem?[] UnitValues =
        {
            null,
            UnitSystem.Millimeters,
            UnitSystem.Centimeters,
            UnitSystem.Meters,
            UnitSystem.Inches,
            UnitSystem.Feet
        };

        /// <summary>HUD / 命令行显示口径：液体容量 / 实体体积。</summary>
        public VolumeMode Mode = VolumeMode.Liquid;

        /// <summary>单位覆盖索引，0 = 跟随文档单位。</summary>
        public int UnitIndex = 0;

        /// <summary>小数位。</summary>
        public int Decimals = 3;

        /// <summary>是否在模型上放置文字点标注。</summary>
        public bool Annotate = false;

        /// <summary>网格精度：0 = 快速，1 = 标准。</summary>
        public int Quality = 1;

        /// <summary>转换率（百分比），界面滑块范围 50–75。</summary>
        public int RatePercent = 65;

        /// <summary>转换率是否按容量自动推荐（&lt;10→75%、10–20→70%、20–30→65%、≥30→60%）。</summary>
        public bool RateAuto = true;

        /// <summary>true = 选中物件按「油杯」处理（扣壁厚）；false = 按「油的体积」直接测。</summary>
        public bool CupMode = true;

        /// <summary>壁厚，单位毫米。默认 0.6。</summary>
        public double WallMm = 0.6;

        /// <summary>是否从容积里扣除雾化芯体积。</summary>
        public bool CoilEnabled = true;

        /// <summary>雾化芯直径，单位毫米。默认 5。</summary>
        public double CoilDiameterMm = 5.0;

        /// <summary>雾化芯轴向：0 = X，1 = Y，2 = Z（默认竖直）。</summary>
        public int CoilAxisIndex = 2;

        public VapeVolume.Core.CoilAxis CoilDirection
        {
            get
            {
                switch (Fmt.Clamp(CoilAxisIndex, 0, 2))
                {
                    case 0: return VapeVolume.Core.CoilAxis.X;
                    case 1: return VapeVolume.Core.CoilAxis.Y;
                    default: return VapeVolume.Core.CoilAxis.Z;
                }
            }
        }

        public UnitSystem? UnitOverride
        {
            get
            {
                int i = Fmt.Clamp(UnitIndex, 0, UnitValues.Length - 1);
                return UnitValues[i];
            }
        }

        public UnitContext CreateUnitContext(RhinoDoc doc)
        {
            UnitSystem model = doc != null ? doc.ModelUnitSystem : UnitSystem.Millimeters;
            return new UnitContext(model, UnitOverride);
        }

        public MeshingParameters Meshing
        {
            get
            {
                try
                {
                    return Quality == 0 ? MeshingParameters.FastRenderMesh : MeshingParameters.Default;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>影响计算结果的设置指纹，用于 HUD 缓存校验。</summary>
        public string Signature(RhinoDoc doc)
        {
            return ((int)Mode).ToString() + "|" + UnitIndex + "|" + Decimals + "|" + Quality
                   + "|" + (doc != null ? ((int)doc.ModelUnitSystem).ToString() : "0");
        }

        public string Describe(RhinoDoc doc)
        {
            string mode = ModeNames[Fmt.Clamp((int)Mode, 0, ModeNames.Length - 1)];
            string unit = UnitNames[Fmt.Clamp(UnitIndex, 0, UnitNames.Length - 1)];
            return string.Format("模式={0}；单位={1}；小数位={2}；{3}",
                mode, unit, Decimals, CreateUnitContext(doc).Describe());
        }

        public static VolumeSettings Load()
        {
            var s = new VolumeSettings();
            s.LoadFromStore();
            return s;
        }

        public void LoadFromStore()
        {
            try
            {
                var store = VapeVolumePlugIn.Instance != null ? VapeVolumePlugIn.Instance.Settings : null;
                if (store == null) return;

                Mode = (VolumeMode)Fmt.Clamp(store.GetInteger("mode", (int)Mode), 0, ModeNames.Length - 1);
                UnitIndex = Fmt.Clamp(store.GetInteger("unit", UnitIndex), 0, UnitNames.Length - 1);
                Decimals = Fmt.Clamp(store.GetInteger("decimals", Decimals), 0, 6);
                Annotate = store.GetInteger("annotate", Annotate ? 1 : 0) != 0;
                Quality = Fmt.Clamp(store.GetInteger("quality", Quality), 0, 1);
                RatePercent = Fmt.Clamp(store.GetInteger("rate", RatePercent), 50, 75);
                RateAuto = store.GetInteger("rateauto", RateAuto ? 1 : 0) != 0;
                CupMode = store.GetInteger("cupmode", CupMode ? 1 : 0) != 0;
                WallMm = Fmt.ClampDouble(store.GetDouble("wall", WallMm), 0.0, 20.0);
                CoilEnabled = store.GetInteger("coilenabled", CoilEnabled ? 1 : 0) != 0;
                CoilDiameterMm = Fmt.ClampDouble(store.GetDouble("coildia", CoilDiameterMm), 0.0, 100.0);
                CoilAxisIndex = Fmt.Clamp(store.GetInteger("coilaxis", CoilAxisIndex), 0, 2);
            }
            catch
            {
                // 读取失败时保持默认值
            }
        }

        public void Save()
        {
            try
            {
                var store = VapeVolumePlugIn.Instance != null ? VapeVolumePlugIn.Instance.Settings : null;
                if (store == null) return;

                store.SetInteger("mode", (int)Mode);
                store.SetInteger("unit", UnitIndex);
                store.SetInteger("decimals", Decimals);
                store.SetInteger("annotate", Annotate ? 1 : 0);
                store.SetInteger("quality", Quality);
                store.SetInteger("rate", Fmt.Clamp(RatePercent, 50, 75));
                store.SetInteger("rateauto", RateAuto ? 1 : 0);
                store.SetInteger("cupmode", CupMode ? 1 : 0);
                store.SetDouble("wall", WallMm);
                store.SetInteger("coilenabled", CoilEnabled ? 1 : 0);
                store.SetDouble("coildia", CoilDiameterMm);
                store.SetInteger("coilaxis", Fmt.Clamp(CoilAxisIndex, 0, 2));
            }
            catch
            {
                // 写入失败不阻断命令
            }
        }
    }
}
