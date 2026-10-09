window.PanelData = {
  stripe: {
    name: "表面条纹",
    english: "Stripe on Surface",
    description: "在表面上生成条纹图案。",
    picks: ["target"],
    groups: [
      {
        id: "dimensions",
        title: "条纹尺寸",
        controls: [
          { type: "slider", key: "Width", label: "条纹宽度", unit: "mm", min: 0.05, max: 20, hardMax: 20, step: 0.05, value: 1.5 },
          { type: "slider", key: "Spacing", label: "条纹间距", unit: "mm", min: 0, max: 50, hardMax: 50, step: 0.05, value: 3 },
          { type: "slider", key: "AngleDeg", label: "倾斜角度", unit: "°", min: -90, max: 90, hardMax: 90, step: 1, value: 45 },
          { type: "slider", key: "Margin", label: "边缘距离", unit: "mm", min: 0, max: 50, hardMax: 50, step: 0.05, value: 2 },
          { type: "slider", key: "CornerRadius", label: "圆角半径", unit: "mm", min: 0, max: 20, hardMax: 20, step: 0.05, value: 0 }
        ]
      },
      {
        id: "edges",
        title: "边界与端部",
        controls: [
          { type: "toggle", key: "ConformToBoundary", label: "贴合边界", value: false },
          { type: "toggle", key: "RoundedEnds", label: "端部圆角", value: true, disabledIf: { key: "ConformToBoundary", equals: true } },
          { type: "toggle", key: "LivePreview", label: "实时预览", value: true },
          { type: "note", key: "info", text: "就绪" }
        ]
      }
    ],
    primaryLabel: "生成",
    secondaryLabel: "取消"
  },
  halftone: {
    name: "参数化阵列纹理",
    english: "Parametric Halftone Pattern",
    description: "以点阵阵列和渐变尺寸生成曲面纹理。",
    picks: ["target", "gradient"],
    groups: [
      {
        id: "dimensions",
        title: "尺寸与渐变",
        controls: [
          { type: "slider", key: "MaxDia", label: "最大直径", unit: "mm", min: 0.2, max: 10, hardMax: 500, step: 0.01, value: 8 },
          { type: "slider", key: "MinDia", label: "最小直径", unit: "mm", min: 0, max: 10, hardMax: 500, step: 0.01, value: 0.5 },
          { type: "slider", key: "Pitch", label: "阵列间距", unit: "mm", min: 0.2, max: 10, hardMax: 500, step: 0.01, value: 4 },
          { type: "slider", key: "Margin", label: "边缘间距", unit: "mm", min: 0, max: 10, hardMax: 500, step: 0.01, value: 1 },
          { type: "slider", key: "Rotation", label: "旋转角度", unit: "°", min: -180, max: 180, hardMax: 180, step: 1, value: 0 },
          { type: "slider", key: "Falloff", label: "衰减幅度", unit: "", min: 0.1, max: 5, hardMax: 20, step: 0.01, value: 1 }
        ]
      },
      {
        id: "scope",
        title: "参考面",
        showIf: { key: "targetFaces", gt: 1 },
        controls: [
          { type: "segments", key: "OnlyFace", label: "生成范围", options: ["选中面", "全部面"], value: 1, values: ["PickedFace", -1] },
          { type: "note", key: "faceHint", text: "当前对象只有 1 个面，两种设置效果相同" }
        ]
      },
      {
        id: "array",
        title: "阵列方式",
        controls: [
          { type: "tiles", key: "ArrayMode", label: "阵列方式", options: ["方形网格", "交错网格", "六边形", "同心环", "螺旋(黄金角)", "抖动网格"], value: 0 },
          { type: "note", key: "centerModeHint", text: "同心环、螺旋和抖动网格从指定圆心向外扩散", showIf: { key: "ArrayMode", in: [3, 4, 5] } }
        ]
      },
      {
        id: "shape",
        title: "图形形状",
        controls: [
          { type: "tiles", key: "Shape", label: "图形形状", options: ["圆形", "三角形", "方形", "六边形"], value: 0 }
        ]
      },
      {
        id: "center",
        title: "阵列圆心",
        controls: [
          { type: "action", key: "chooseCenter", label: "选择圆心" },
          { type: "action", key: "useSurfaceCenter", label: "用曲面中心" },
          { type: "note", key: "centerInfo", text: "圆心：曲面中心（同心环/螺旋/抖动 建议点选圆心）" },
          { type: "toggle", key: "UsePickedCenter", label: "从指定物件开始渐变（不勾 = 从曲面中心）", value: true, showIf: { key: "CenterPoints", gt: 0 } }
        ]
      },
      {
        id: "preview",
        title: "预览",
        controls: [
          { type: "toggle", key: "LivePreview", label: "实时预览", value: true },
          { type: "note", key: "info", text: "就绪" }
        ]
      }
    ],
    primaryLabel: "生成",
    secondaryLabel: "取消"
  },
  voronoi: {
    name: "泰森多边形纹",
    english: "Voronoi Texture",
    description: "在曲面上生成泰森胞元纹理，可调节胞元造型、边界和渐变。",
    picks: ["target", "gradient"],
    groups: [
      {
        id: "dimensions",
        title: "胞元参数",
        controls: [
          { type: "slider", key: "CellSize", label: "胞元尺寸", unit: "mm", min: 2, max: 60, hardMax: 500, step: 0.5, value: 18 },
          { type: "slider", key: "Depth", label: "凹凸深度", unit: "mm", min: -20, max: 20, hardMax: 200, step: 0.05, value: 2 },
          { type: "slider", key: "EdgeWidth", label: "过渡宽度", unit: "mm", min: 0.05, max: 20, hardMax: 200, step: 0.05, value: 2.5 },
          { type: "slider", key: "Shape", label: "凸起形状", unit: "", min: 0.2, max: 3, hardMax: 10, step: 0.05, value: 1 },
          { type: "slider", key: "Relax", label: "规整度", unit: "", min: 0, max: 20, hardMax: 30, step: 1, value: 2 },
          { type: "slider", key: "Seed", label: "随机种子", unit: "", min: 0, max: 200, hardMax: 9999, step: 1, value: 1 },
          { type: "slider", key: "EdgeFade", label: "边界收平", unit: "mm", min: 0, max: 20, hardMax: 200, step: 0.05, value: 0 },
          { type: "slider", key: "Step", label: "采样间距", unit: "mm", min: 0.05, max: 3, hardMax: 20, step: 0.05, value: 0.4 }
        ]
      },
      {
        id: "cellShape",
        title: "胞元造型",
        controls: [
          { type: "segments", key: "CellShape", label: "胞元形状", options: ["平顶", "穹顶"], value: 0 },
          { type: "note", key: "shapeHint", text: "平顶：内部是平顶，只到边界过渡宽度内平滑收边（老行为）" },
          { type: "slider", key: "DomePower", label: "穹顶圆度", unit: "", min: 0.3, max: 3, hardMax: 3, step: 0.05, value: 1, disabledIf: { key: "CellShape", equals: 0 } }
        ]
      },
      {
        id: "scopeOutput",
        title: "参考面与输出",
        controls: [
          { type: "segments", key: "OnlyFace", label: "参考面", options: ["选中面", "全部面"], value: 1, values: ["PickedFace", -1], disabledIf: { key: "targetFaces", equals: 1 } },
          { type: "note", key: "faceHint", text: "当前对象只有 1 个面，两种设置效果相同" },
          { type: "segments", key: "PerCell", label: "胞元输出", options: ["整体一张面", "跨面平滑", "每胞元一张面"], value: 1, bindings: { PerCell: [false, false, true], SmoothSeam: [false, true, false] } },
          { type: "note", key: "cellHint", text: "多重曲面合并焊接成一张连续面，接缝处平滑过渡（连折痕一起抹平）" }
        ]
      },
      {
        id: "gradient",
        title: "胞元渐变",
        controls: [
          { type: "action", key: "clearGradient", label: "取消渐变" },
          { type: "slider", key: "GradientAmount", label: "渐变幅度", unit: "", min: -2, max: 2, hardMax: 2, step: 0.05, value: 0.6 },
          { type: "note", key: "gradientHint", text: "未设置渐变参考物件（不设置时胞元均匀分布）" }
        ]
      },
      {
        id: "preview",
        title: "预览与输出",
        controls: [
          { type: "toggle", key: "LivePreview", label: "实时预览", value: true },
          { type: "toggle", key: "NurbOutput", label: "输出多重曲面（NURBS）而不是网格", value: false },
          { type: "note", key: "info", text: "就绪" }
        ]
      }
    ],
    primaryLabel: "生成",
    secondaryLabel: "取消"
  },
  radialdots: {
    name: "径向渐变圆点",
    english: "Radial Gradient Dots",
    description: "按径向尺寸渐变布置圆点、几何图形或网格阵列。",
    picks: [],
    groups: [
      {
        id: "dimensions",
        title: "尺寸与渐变",
        controls: [
          { type: "slider", key: "OuterR", label: "外半径", unit: "mm", min: 0.5, max: 10, hardMax: 2000, step: 0.01, value: 10 },
          { type: "slider", key: "InnerR", label: "内半径", unit: "mm", min: 0, max: 10, hardMax: 2000, step: 0.01, value: 4 },
          { type: "slider", key: "Pitch", label: "间距", unit: "mm", min: 0.05, max: 5, hardMax: 200, step: 0.01, value: 1 },
          { type: "slider", key: "MaxDia", label: "最大直径", unit: "mm", min: 0.05, max: 5, hardMax: 200, step: 0.01, value: 0.9 },
          { type: "slider", key: "MinDia", label: "最小直径", unit: "mm", min: 0, max: 5, hardMax: 200, step: 0.01, value: 0 },
          { type: "slider", key: "Peak", label: "峰值位置", unit: "", min: 0, max: 1, hardMax: 1, step: 0.01, value: 0.3 },
          { type: "slider", key: "Falloff", label: "衰减", unit: "", min: 0.1, max: 5, hardMax: 20, step: 0.01, value: 1 }
        ]
      },
      {
        id: "layout",
        title: "阵列方式",
        controls: [
          { type: "segments", key: "Layout", label: "阵列方式", options: ["同心环", "螺旋", "方形网格", "交错网格"], value: 0 },
          { type: "toggle", key: "Stagger", label: "相邻环错开半格（同心环）", value: true },
          { type: "note", key: "layoutHint", text: "同心环：每环按弧长取个数，环间距 = 间距" }
        ]
      },
      {
        id: "shape",
        title: "图形与细节",
        controls: [
          { type: "segments", key: "Shape", label: "图形形状", options: ["圆形", "方形", "三角形", "六边形", "圆方交替"], value: 0 },
          { type: "slider", key: "Rotation", label: "整体旋转", unit: "°", min: -180, max: 180, hardMax: 180, step: 0.01, value: 0 },
          { type: "slider", key: "Jitter", label: "位置抖动", unit: "", min: 0, max: 1, hardMax: 1, step: 0.01, value: 0 },
          { type: "toggle", key: "Merge", label: "重叠的图形自动布尔合并成一个整体", value: true },
          { type: "note", key: "shapeHint", text: "圆形：真圆弧输出" }
        ]
      },
      {
        id: "preview",
        title: "预览",
        controls: [
          { type: "toggle", key: "LivePreview", label: "实时预览", value: true },
          { type: "note", key: "info", text: "就绪" }
        ]
      }
    ],
    primaryLabel: "生成",
    secondaryLabel: "取消"
  },
  vape: {
    name: "烟油容量",
    english: "Vape Volume",
    description: "根据所选物件的体积和转换率计算可注入量，并同步标注。",
    picks: ["target"],
    groups: [
      {
        id: "result",
        title: "容量结果",
        controls: [
          { type: "result", key: "capacity", label: "可注入量", text: "未选择物件" },
          { type: "note", key: "formula", text: "" },
          { type: "note", key: "rateHint", text: "" },
          { type: "note", key: "targetStatus", text: "未选择物件 —— 点「选择物件」按钮选目标（也可以直接在视图里点选后再点按钮）" }
        ]
      },
      {
        id: "mode",
        title: "计算方式",
        controls: [
          { type: "segments", key: "CupMode", label: "计算方式", options: ["油杯体积（扣壁厚后计算）", "油的体积（直接测量）"], value: 0 },
          { type: "slider", key: "WallMm", label: "壁厚", unit: "mm", min: 0, max: 20, hardMax: 20, step: 0.05, value: 0.6, disabledIf: { key: "CupMode", equals: false } }
        ]
      },
      {
        id: "rate",
        title: "转换率",
        controls: [
          { type: "slider", key: "RatePercent", label: "转换率", unit: "%", min: 50, max: 75, hardMax: 75, step: 1, value: 65, disabledIf: { key: "RateAuto", equals: true } },
          { type: "toggle", key: "RateAuto", label: "自动推荐", value: true }
        ]
      },
      {
        id: "coil",
        title: "雾化芯",
        controls: [
          { type: "toggle", key: "CoilEnabled", label: "扣除雾化芯体积（视口里会实时预览那个圆柱）", value: true },
          { type: "slider", key: "CoilDiameterMm", label: "芯直径", unit: "mm", min: 0, max: 50, hardMax: 50, step: 0.1, value: 5 },
          { type: "segments", key: "CoilAxisIndex", label: "轴向", options: ["X", "Y", "Z 竖直"], value: 2 },
          { type: "note", key: "coilInfo", text: "" }
        ]
      },
      {
        id: "detail",
        title: "明细",
        controls: [
          { type: "note", key: "detail", text: "" },
          { type: "note", key: "note", text: "" },
          { type: "note", key: "status", text: "" }
        ]
      }
    ],
    primaryLabel: "重新生成标注",
    secondaryLabel: "关闭"
  }
};
