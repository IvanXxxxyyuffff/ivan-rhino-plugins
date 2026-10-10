# IVAN 插件中心 · Rhino 7/8 插件合集

**简体中文** | [English](README.en.md) | [日本語](README.ja.md) | [繁體中文](README.zh-TW.md)

一套运行在 **Rhino 7 / Rhino 8** 上的 .NET 插件合集：**7 个参数化建模插件** + 一个玻璃拟态风格的**插件中心**（安装器 / 启动器）。
每个插件都是「一条命令 + 一个参数面板 + 实时预览 + 无头自检」，**不依赖任何第三方包**（只用 RhinoCommon / WinForms）。

> 界面与交互遵循仓库内的《设计一致性规范》：统一的玻璃卡片面板、语义控件（滑块 + 数字框联动）、
> 拾取按钮「选到=绿 / 没选或选错=红」、150/200/300ms 三档动效、同一套线性图标家族。

## 下载 / 安装

| 版本 | 下载 | 说明 |
|---|---|---|
| **v1.1.0** | [**IVAN-CENTER.exe**](https://github.com/IvanXxxxyyuffff/ivan-rhino-plugins/releases/download/v1.1.0/IVAN-CENTER.exe) | Windows x64 安装器（2.9 MB，md5 `4a7932421e68dfc91d64618c63f9f648`） |

1. **先关闭 Rhino**，双击运行安装器 → 自动装到 `%LOCALAPPDATA%\IVAN\plugins`（注册 7 个插件 + 写入工具条）
2. 打开 Rhino：工具条上出现 7 个按钮，点按钮开面板即可用
3. 也可以从源码自行构建（见下）

> 安装包不含 Rhino 本体，需要已安装 Rhino 7 或 Rhino 8。插件界面目前为**简体中文**。

## 插件一览

| # | 插件 | 命令 | 做什么 |
|---|------|------|--------|
| 1 | 表面条纹 **StripeOnSurface** | `StripeOnSurface` | 曲面/多重曲面上生成条纹；端部可选圆角 / 平齐 / 完全贴合边界（按边缘距离内缩、端部随边界裁剪）；硬边可倒可调圆角；宽度、间距、角度、边缘距离按真实 mm 实时调 |
| 2 | 烟油容量计算器 **VapeVolume** | `VapeVolume`、`VapeVolumeWatch` | 计算瓶/仓体容量与注入量并实时显示 |
| 3 | 参数化阵列纹理 **HalftoneDots** | `ParametricTexture` | 6 种阵列（网格/交错/六边/同心环/螺旋/抖动）× 4 种形状（圆/三角/方/六边）；同心环/螺旋/抖动可点选圆心向外扩散；曲面上图形不变形、自动铺满整张面 |
| 4 | 泰森多边形纹 **VoronoiTexture** | `VoronoiTexture` | 平面边界内的泰森多边形（Voronoi）胞元凹凸纹理：中心点、细胞壁壁厚、渐变物件控疏密；输出「线框 / 网格面」二选一，勾「一键平滑」出**细分曲面（SubD）** |
| 5 | 径向渐变圆点 **RadialDots** | `RadialDots` | 按半径渐变的圆点图案：4 种阵列 × 5 种图形，尺寸按峰值位置与衰减渐变；重叠图形自动布尔合并 |
| 6 | 网格修复 **MeshFix** | `MeshFix` | 一键修复「着色/渲染模式下复杂修剪曲面只剩边缘线、面体不显示」：把物件渲染网格的「最大长宽比」由 0 改成 6 并重建；**无面板**，选中物件修选中的、没选中自动扫描整份文件 |
| 7 | 钻石切面 **DiamondFacet** | `DiamondFacet` | 平面/闭合曲线边界内生成凹凸钻石切面：随机三角剖分 + 顶点随机高低，可固定边界；输出「仅线框 / 面」二选一，面模式可**每个三角切面细分出一张网格片**并细分 |
| 8 | 水波纹 **WaterRipple** | `WaterRipple` | 曲面 / 多重曲面（当成一整个面）/ 闭合平面曲线边界上生成水波纹：**有机水波 / 定向条带 / 同心涟漪**三种波形可切换，波长、波高、波数、主方向、方向散布、波峰形状可调；**固定边界 + 边界过渡**（宽度 / 平滑度）；输出网格面，勾「一键平滑」转成**细分曲面（SubD）**（边界自动打 crease，角不收） |

每个插件都带自检命令（如 `VoronoiSelfTest`、`DiamondFacetSelfTest`），可在无界面下跑完整几何断言。

## 仓库结构

```
implementation/
  MODIFIED_FILE/                  插件源码
    stripe/                       表面条纹插件 + 插件中心（center：安装器 / 启动器）
    voronoi/  halftone/  radialdots/  meshfix/  diamondfacet/
                                  其余 5 个插件（各自 Rhino7 + Rhino8 双 TFM 工程）
    src/VapeVolume/               烟油容量计算器
    shared/PanelTheme.cs          全部面板共用的自绘控件 + 动效 + 图标渲染（各插件内是逐字节一致的副本）
    iconmake/                     图标渲染器（整套线性图标由代码生成，不用位图素材）
    PROJECT-STATE.md              项目状态 / 逐轮改动 / 踩过的坑（真相源）
    DESIGN-CONSISTENCY.md         设计一致性规范（新增插件必读）
    UI-REFACTOR-HANDOFF.md        界面重构交接说明
  *.ps1 / *.py                    构建、安装、自检、审计的一键脚本
index.html / app.js / styles.css / icons/ …   UI 原型预览（HTML/CSS/JS 玻璃拟态界面 + 截图）
```

## 构建

要求：Windows + .NET SDK 7/8 + Rhino 7 或 8（RhinoCommon 走 NuGet，无需安装 Rhino 也能编译）。

```bash
# 单个插件（双 TFM：Rhino 7 用 net48，Rhino 8 用 net7.0-windows）
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino8/VoronoiTexture.csproj -c Release
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino7/VoronoiTexture.csproj -c Release

# 全部插件 + 插件中心
python -X utf8 implementation/native-gate.py build-panels
```

产物落在各插件的 `out/rhino7|rhino8/`（`.rhp` 可直接拖进 Rhino，或用插件中心安装）。

## 安装 / 自检

```bash
pwsh -File implementation/refresh-payload-and-center.ps1   # 汇总 payload + 编插件中心 exe
pwsh -File implementation/install-and-verify.ps1           # 静默安装 + 断言（注册表 / 工具条 / payload 逐字节一致）
pwsh -File implementation/run-native-selftests.ps1         # 7 个插件自检一轮跑全（默认隐藏窗口，不弹窗）
pwsh -File implementation/run-native-selftests.ps1 -Only Voronoi   # 单插件
```

插件安装位置：`%LOCALAPPDATA%\IVAN\plugins\<插件>\rh8\<插件>.rhp`（Rhino 8）/ `rh7\`（Rhino 7）。

## 文档

- [`implementation/MODIFIED_FILE/PROJECT-STATE.md`](implementation/MODIFIED_FILE/PROJECT-STATE.md)：项目状态、每个插件的能力口径、逐轮改动与**踩过的坑**（几何 API、RhinoCommon 陷阱、验证流程）
- [`implementation/MODIFIED_FILE/DESIGN-CONSISTENCY.md`](implementation/MODIFIED_FILE/DESIGN-CONSISTENCY.md)：像素级布局 / 语义控件 / 拾取按钮红绿口径 / 图标家族 / 自检格式
- [`implementation/MODIFIED_FILE/UI-REFACTOR-HANDOFF.md`](implementation/MODIFIED_FILE/UI-REFACTOR-HANDOFF.md)：界面重构交接

## 许可

MIT，见 [LICENSE](LICENSE)。
