> ⚠ **源码权威位置已迁移（2026-09-30 18:26 起）**：UI/图标已由 CODEX 重构，**新版 UI 的权威完整源码在 `E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE`**（安装器项目 `E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\center\Center.csproj`）。
> 本目录（`C:\zcode_build`）保留的是**旧 UI** + **几何核心定稿**；几何核心与新树逐字节一致。
> **不要再从本目录直接重编并覆盖新版安装器**（会丢掉液态玻璃 UI）；改几何请改新树，或改完两处同步。
> 本次实施说明：`D:\UserData\Desktop\IVAN插件中心\UI重构实施交接说明-本次CODEX.md`

# IVAN Rhino 插件 · 设计一致性规范（新插件照着做）

> 版本：2026-09-30。**新增任何插件都必须满足本文件**；改主题/控件只改 `C:\zcode_build\shared\PanelTheme.cs`，再同步到各 `src\PanelTheme.cs` 与 `stripe\center\PanelTheme.cs`（md5 必须一致）。
> 真相源：`C:\zcode_build\PROJECT-STATE.md`；技能：`C:\Users\Administrator\.zcode\skills\rhino-plugin-dev\SKILL.md`。

---

## 0. 一句话

> **控件家族铁律**：所有插件面板一律用 `PanelTheme` 的自绘控件家族（`CardPanel / IvanSlider / IvanCheck / IvanSegmented / FlatButton / InfoBar / HeaderBand`），**禁止**用 Eto/WinForms 原生控件（`Slider`/`CheckBox`/`RadioButtonList`/`NumericStepper`/裸 `Button`）做面板主体 ——
> 配色能对齐、控件本体对不齐，用户一眼就看出「这个面板跟别的不一样」（烟油面板踩过：65 次 vs 0 次）。Eto 面板必须把视图层改写成 WinForms + `PanelTheme`，逻辑层保留；跨框架的 WPF 模板 hack 一律不算。


**一个命令打开一个置顶浮动面板；面板最上面永远是「选择」卡片（未选红/已选绿），中间是参数卡片，底部是状态条 + 生成/取消；预览实时、可中断；生成写进自己的图层；同一套动效与图标家族。**

---

## 1. 目录与文件（照抄现有插件）

```
<plugin>/
  src/            PanelTheme.cs(从 shared 拷) + XxxCore.cs + XxxUi.cs + XxxPlugin.cs + XxxSelfTest.cs
  Rhino7/         net48  csproj（RhinoCommon 7.0.20314.3001 + RH7 常量 + Microsoft.NETFramework.ReferenceAssemblies）
  Rhino8/         net7.0-windows csproj（RhinoCommon 8.30.x）
  out/rhino7|rhino8/
```
- 两套 csproj 都用 `<Compile Include="..\src\**\*.cs" />` + `CopyRhp` 目标把 `$(TargetPath)` 复制成 `<Name>.rhp`。
- `[assembly: Guid]` 与类级 `[Guid]` **都要写**，Rhino 7/8 用**不同 GUID**（8-4-4-4-12 位，少一位 = CS0591）。

## 2. 命令 / 会话 / 面板三分工（照抄）

| 角色 | 要求 |
|---|---|
| `XxxCommand` | `EnglishName` = 面板要显示的名字；`RunCommand` **只做一件事**：建 `XxxSession` 并 `Start()`，打印一句「参数面板已打开…」，**立即返回**（绝不 `while+DoEvents` 阻塞） |
| 拾取命令 | 每个选择按钮一个隐藏命令：`XxxPickTarget`（参考物件）、`XxxPickGradient`（渐变物件）。命令里 `GetObject` → 取 `ObjRef` → `session.SetXxx(...)` |
| `XxxSession` | 持有 `RhinoDoc` / 目标 `Guid` / 面板 / 预览 conduit / 防抖 Timer / 跟随 Timer；订阅 `RhinoDoc.ReplaceRhinoObject`、`DeleteRhinoObject`；面板 `FormClosed` 时若 `Committed` 才写图层 |
| `XxxPanel` | WinForms `Form`，`TopMost`、`FormBorderStyle.FixedToolWindow`、`ShowInTaskbar=false`、`KeyPreview=true`、Esc=取消 |

**强制：面板优先**（用户口径）——命令**不看文档预选**，永远以空目标开面板；`EmptyTargetHint = "未选择物件 —— 点「选择物件」按钮选目标"`。

## 3. 面板布局（像素级，所有插件一致）

`const int W = 434;`（客户区宽）

| 区域 | 位置 | 说明 |
|---|---|---|
| 头部渐变带 | `(0, 0, W, 48)` | 自绘 `HeaderBand`：插件小图标 + 标题 + 副标题（当前目标）。**不吃点击**（`ControlStyles.UserPaint`） |
| **「选择」卡片** | `Top = 56, Left = 12, Width = W-24, Height = 44` | 见 §4。**所有插件第一张卡片必须是它** |
| 参数卡片们 | 从 `y = 108` 开始，每张 `Left=12, Width=W-24`，间距 8 | `CardPanel`，带 `Title`；行用 `AddRow(...)`（标签 84 + 滑杆 186 + 数字框 86，行高 32） |
| 输出/范围类卡片 | 参数之后 | 语义固定的分段控件，见 §5 |
| 实时预览 | 参数之后 | `IvanCheck`「实时预览」，默认勾选 |
| 状态条 | 预览卡之后 | `InfoBar`（≥44 高）：第 1 行数字（数量/面数/耗时），第 2 行参数摘要，第 3 行 `⚠ 失败原因`（有才显示） |
| 生成 / 取消 | 最底 | `FlatButton` 96×32：生成 `BtnStyle.Primary` 在 `Left = W-12-96-104`，取消 `BtnStyle.Ghost` 在 `Left = W-12-96`；`AcceptButton`/`CancelButton` 都设 |

收尾：`ClientSize = new Size(W, y + 32 + 12);`（**永远靠 `y` 累加，不写死高度**）。
窗口位置用 `PlaceNearRhino()`（Rhino 主窗口所在屏幕右侧，距右 420px）；`Icon = Theme.MakeFormIcon("<kind>", 16)`。

## 4. 「选择」按钮的统一口径（用户明确要求）

- **位置统一**：全部放在最上面那张「选择」卡片里。参考物件按钮固定 `(12, 8, 140, 28)`；需要第二个（渐变物件）固定 `(160, 8, 150, 28)`。
- **文字统一**：`选择物件`（参考目标）/ `选择渐变物件`（渐变参考）。**不要**叫「选择参考面」「拾取」等别名。
- **颜色统一**（`FlatButton.PickStyle = true` + `Picked`）：
  - 未选择 = **红** `#CE4C4C`（悬停 `#E26060`），白字；
  - 已选择 = **绿** `#389E5C`（悬停 `#4AB670`），白字。
- **红=「不可用」而不只是「没选」（2026-10-09 用户口径）**：**没选 / 选错（类型不支持、解析失败、算不出结果） / 目标被删除** 三种情况一律**红**，
  只有「选到且解析成功」才绿。要点：
  - 拾取命令里**任何拿到对象但不可用的分支**都要置红 + 给原因（不能只 `return Failure` 而把按钮留在绿）；
  - 「事后类型漂移」（对象还在但已不是可用类型，如 `brep == null`）在 `Rebuild` 里也要置红；
  - 删除事件走 **250ms 复查**（`doc.Objects.FindId(id)` 还在 = Move/Transform 的「先删后加」→ 只重算、不动颜色；真没了才置红）；
  - 用户**主动取消拾取（Esc）不置红**（已有有效目标时取消是正常操作）；
  - 自检要覆盖这四种情况（见 `diamondfacet\src\DiamondFacetSelfTest.cs` 用例14：`PickState` 未选红 / 有效绿 / 置 false 红）。
- **状态同步**：面板暴露 `SetTargetState(bool)` / `SetGradientState(bool)` / （有圆心的插件）`SetCenterState(bool)`；
  会话在 `SetTarget` / `SetGradient` / `PickCenter` 成功时置 `true`，在**解析失败、类型漂移、目标被真删除**时置 `false`。
- **移动/替换不是删除**：Rhino 的 Move/Transform 常是「先删旧对象、再加新对象」→ 删除事件要**延迟 ~250ms 复查** `doc.Objects.FindId(id)`，还在就只当移动（继续跟随、重算），真没了才提示删除。
- 渐变类参考**记物件 ID 不记坐标**：每次重算现取包围盒中心；`ReplaceRhinoObject` 事件 + 400ms 轮询兜底（等物件停下来再重算）。

## 5. 语义固定的控件（名字/选项不要自创）

| 控件 | 选项 | 语义 |
|---|---|---|
| **参考面** | `选中面` / `全部面` | 只处理点选的那一个面 / 整个多重曲面。**对象只有 1 个面时整张卡片隐藏** |
| **胞元输出**（4 项，语义冻结） | `整体一张面` / `跨面平滑` / `每胞元·网格` / `每胞元·NURBS` | 见 §7「整体生成」；后两项的区别要在提示行写清楚（网格边界=网格格子有台阶；NURBS=胞元多边形直边、G0、边缘贴原曲面）。**不许再并存一个独立的 NURBS 勾选框**（用户会选错） |
| **实时预览** | 勾选 | 关掉时不重算预览（但仍可生成） |
| 生成 / 取消 | 按钮 | 生成 = 写图层并关面板；取消 = 关面板不写 |

标签用词统一：`胞元尺寸/凹凸深度/过渡宽度`、`条纹宽度/条纹间距/倾斜角度/边缘距离/圆角半径`、`最大直径/最小直径/阵列间距/边缘间距/旋转角度/衰减幅度`、`外半径/内半径/间距/峰值位置/衰减`。
长度单位一律 **mm**（面板显示 mm，内部按 `MmToModel` 换算），数字框**两位小数**（step 0.01）。

## 6. 动效（finesse 口径，不要自创时长）

- token：`Motion.Micro=150ms`（hover/press/勾选）、`Motion.Base=200ms`（状态切换/分段）、`Motion.Slow=300ms`（面板进入）、`Motion.Exit=150ms`（退出）。
- 面板进入：`OnShown` 里淡入 + 上移 8px（`Motion.EaseOut`）；退出：150ms `EaseIn` 后 `Close()`——**`Motion.To` 的 done 回调必须真的被调用**（否则面板关不掉、透明还吃点击 → Rhino 卡死）。
- 拖滑块 1:1 跟手**不做动画**；高频改数值只做防抖重算（90–140ms），不做过渡动画。
- 尊重系统「减少动画」：`Motion.Enabled` 为 false 时瞬时到位，终态不变。

## 7. 几何行为约定（用户反复强调的「整体」）

- **多重曲面必须按一个整体生成**，不能各面各排一套：
  1. 首选**全局排版**：对整个对象做**最佳拟合平面** → 在平面里铺一套阵列 → 每点 `brep.ClosestPoint` 投到最近面成形 → 3D 去重；命中率 <15%（太弯）时退回第 2 条并在状态条说明。
  2. 退回路径：每个面按自己参数域铺，但**格点对齐到同一套全局相位**（一个全局锚点 = 参考面 2D 区域中心）。
  3. 多面时**所有面共用同一个 2D 工作平面**（不要各面用自己中心的切平面 —— 两张曲面片方向对不上、看着扭曲）；面几乎垂直于参考平面（|cos|<0.1）时该面退回自己的切平面并写 Note；2D→3D 必须回投到曲面上。
- **边距/收平只在外轮廓与折痕处生效**：顺接接缝（两侧都有面且夹角 < 35°，或接缝自己对自己）**不算边界**，图案要跨过去连成一片。
- 接缝处两边必须**共用同一批采样点**（公共采样点），否则顶点错开半格、看着就是两张面拼的。
- 相邻面网格合并成一张时，**按「输入面夹角」判顺接**（网格面片夹角会被浮雕坡度污染）并只焊接顺接处。
- 面用 `TryGetPlane` 判平面性；整圈环绕面（挤出筒面 / 圆柱侧面）要**明确提示**并给替代做法，不许静默返回空。

## 8. 图标（一套家族，程序化绘制，不用图片）

- 安装器：`IconFactory.Create(kind, size)` —— 中性圆角底板（白→浅灰渐变 + 边）+ 单一深色系强调色图形；已有 kind：`stripe`(青 #0E7490) / `vape`(蓝) / `halftone`(靛 #3E4C9A) / `voronoi`(琥珀) / `radialdots`(紫 #603EBA) / **`meshfix`(绿 #208A60，2026-10-08 加)**。新插件**必须新增一个 kind 和一种色相**，别复用别人的。
- **图标改动必须同时改 7 个文件**：6 份 `PanelTheme.cs`（shared + 4 个插件 src + stripe\center）+ `iconmake\Program.cs`（`GlassDraw*` 方法体要逐 token 一致，`final-ui-audit.py` 的 `theme_to_iconmake_renderer_parity` 会校验；6 份 PanelTheme 的 md5 也必须一致）。改完必须**全量重建所有插件** —— `shared\PanelTheme.cs` 是被 5 个插件一起编译的（实测加一个图标 = 每个插件二进制 +1536 字节）。
- 面板：`Theme.DrawGlyph(g, rect, kind, col)`（头部小图标）+ `Theme.MakeFormIcon(kind, 16)`（窗体标题栏图标）。**kind 未命中会落到「条纹」默认分支**，所以两个文件都要加分支。
- EXE 应用图标：`<ApplicationIcon>app.ico</ApplicationIcon>`（多尺寸 .ico；<256 用 DIB 条目，256 用 PNG），并由 `iconmake` 程序生成。

## 9. 自检（无人值守，改完必须跑）

- 标志文件 `%LOCALAPPDATA%\IVAN\logs\run-<name>-selftest.flag` → `RhinoApp.Idle` → `XxxSelfTestCommand.RunTo(doc, DefaultReportPath, true)` → 写 UTF-8 报告 `<Name>SelfTest.txt` → `RhinoApp.Exit()`。
- 报告格式：`=== <插件> 自检报告 ===` + 每个用例 `--- 用例N：... ---` + 每项 `[PASS]/[FAIL] 说明` + 末行 `RESULT: N 通过 / M 失败 -> PASS|FAIL`。
- **必测**：几何正确性（数值断言，不是「没抛异常」）、多重曲面跨面（相位/间隙/接缝）、面板冒烟（构造 + 分段切换 + `Show()` 后 `DrawToBitmap` **两遍** + 关闭路径真的 `IsDisposed`）、用户给的 3dm 实跑（有的话）、回归用例（每个修过的 BUG 都留一条）。
- 只读报告要用 `Get-Content -Encoding UTF8`；图片写 `%TEMP%`（写 logs 会被 Rhino 改名 `.IPGSD`）。

## 10. 安装器集成（4 处，缺一个就装不上/没图标）

1. `stripe\center\Installer.cs` 的 `Repo.Plugins` 加 `PluginDef`（Key/Name/Desc/Guid7/Guid8/Cmd/**ExtraCommands**/Res7/Res8/File7/File8/Res8Dll/Res8Deps/Res8Runtime/Deps8Name/Runtime8Name/**IconKind**）；
   `ExtraCommands` 必须把**所有隐藏命令**都列上（`XxxSelfTest;XxxPickTarget;XxxPickGradient`…），否则用户机器上命令不存在。
2. `stripe\center\Center.csproj` 加 5 条 `EmbeddedResource`（`payload\<Name>-rh7.rhp` → `p_xxx7`、`payload\<Name>-rh8.rhp` → `p_xxx8`、`payload\<Name>.dll` → `p_xxx8_dll`、`.deps.json`、`.runtimeconfig.json`）。
   **注意**：`payload\<Name>.rhp` 这个名字没有任何 csproj 引用（曾经拷到它 → 装回去是旧构建），别用。
3. `payload\` 放二进制（rh7 在 `out\rhino7\`，rh8 在 `out\rhino8\`；VapeVolume 在 `bin\Release\net48|net7.0-windows\`）。
4. `IconFactory` 加 kind（§8）。
工具条（RUI）由 `InstallToolbar(Repo.Plugins, …)` 自动按列表生成按钮，不用手工加。
**新插件必须追加在 `Repo.Plugins` 末尾**：RUI 的 item/bitmap guid 与位图索引是按序号生成的（`RuiItemGuid(i)`），插在中间会让老按钮的图标错位。
`CenterForm` 的行高/行数/卡片标题已改成按 `Repo.Plugins.Count` 自动算，不用手改数字。

## 11. 交付与验证流程（每次改完都走一遍）

```bash
D="C:/zcode_tools/dotnet/dotnet.exe"
# 1) 编所有插件（每个双 TFM）
# 2) 刷 payload（§10.3）
# 3) 编安装器 → 拷 dist + 桌面
# 4) taskkill //IM Rhino.exe //F → IVAN-CENTER.exe /silent → 跑各插件自检（串行！16GB 内存别并发）
# 5) 核对：安装目录里的 .rhp 与 payload 逐字节同尺寸；自检 RESULT 全 PASS
```
交付目录固定 `D:\UserData\Desktop\IVAN插件中心\`（**不是 C 盘桌面**）：`IVAN-CENTER.exe` + `README.md` + `项目状态存档.md`（= `C:\zcode_build\PROJECT-STATE.md` 副本）。
README 里的安装器 `字节数 + md5` 每次都要更新。

## 12. 收尾检查清单（新插件合并前逐条打勾）

- [ ] 两套 csproj 都 0 error；Rhino 7/8 GUID 不同且合法
- [ ] 命令不看预选、永远先开面板；面板上有「选择物件」（未选红/已选绿，位置 §4）
- [ ] 面板：头部带 + 选择卡片 + 参数卡 + 预览 + 状态条 + 生成/取消；`ClientSize` 用 y 累加
- [ ] 动效只用 token 时长；进入/退出都能真的关掉窗体
- [ ] 多重曲面按整体生成（§7）；顺接接缝不留边距；接缝共用采样点
- [ ] 图标：`DrawGlyph` + `IconFactory` 两个分支都加了新 kind，色相不与现有重复
- [ ] 自检：几何数值断言 + 面板冒烟（重绘两遍 + 关闭）+ 回归用例；跑出 PASS
- [ ] 安装器 4 处都改了；`ExtraCommands` 含全部隐藏命令
- [ ] 更新 `PROJECT-STATE.md`（插件表/功能/自检数字/交付 hash）+ 同步桌面副本 + README

## 13. 无面板「一键工具」类插件（2026-10-08 起，第二个类型）

不是所有插件都该有面板。用户口径：**「这个功能不需要面板，只需要一个图标，点一下即可修复」** —— 参照 `meshfix\`（网格修复 MeshFix）。

与面板插件的差别（**只列差异，其余照旧**）：

| 项 | 面板插件 | 无面板工具插件 |
|---|---|---|
| `src\` 内容 | `PanelTheme.cs` + Core + Ui + Plugin + SelfTest | **不要 PanelTheme.cs**（没有控件要画） |
| csproj | 同上 | 不需要 `UseWindowsForms` / `System.Windows.Forms` |
| 命令 | 建 Session + `Start()` 后立即返回 | **一次调用跑完**：不许弹面板、不许进 `Get*` 循环、不许 `while+DoEvents` |
| 作用范围 | 面板里选目标 | 有选中就作用于选中，**没有选中就扫全文档**（用户点一下就要有结果，不能反问） |
| 输出 | 写自己的图层 | 不改几何：只改物件属性 / 重建网格；**包一个 undo record**，跑完给一行汇总 |
| 自检 | 几何 + 面板冒烟 | 几何/行为断言 + **幂等断言**（跑第二遍必须 0 动作）+ 健康件不被误改 |

铁律：
1. **精度优先于覆盖面** —— 只在确实判定为「有问题」的物件上动手（例如 MeshFix 只在渲染网格真的退化时写参数），健康件一律不碰，并把这个性质写成自检用例。
2. **不许静默失败** —— 跳过/失败都要逐条写进命令行输出，最后一行给「扫描 N / 退化 M / 修复 X / 跳过 Y / 仍失败 Z」。
3. **诚实报告边界** —— 版本差异（如 Rhino 7 没有物件级网格参数）、复现不了的用例，都要写进报告，不许为了变绿而放宽成假断言。
4. **不碰用户文件** —— 需要读用户 3dm 时复制到 `%TEMP%` 再 `RhinoDoc.OpenHeadless`，并断言原文件字节数没变。

5. **改动「显示相关」的东西必须用真渲染截图验收**：`RhinoView.CaptureToBitmap` 在自动化/隐藏窗口下拿到的是
   不刷新的旧帧（实测四种刷新手段像素完全相同），要用命令 `_-ViewCaptureToFile`；像素判据别用「非背景像素」
   （线框也能到 2 万像素，看着像成功），要用**着色面体的颜色特征**（浅中性灰：`max-min<=8 && min>=190`）。
   见 `meshfix\src\MeshFixSelfTest.cs` 用例6 与 `PROJECT-STATE.md §13`。
6. **改物件属性/缓存类操作：写完必须读回复核**，失败要如实报「跳过」，不许当成功继续跑；
   涉及 Rhino 缓存（网格缓存等）时，RhinoCommon 没有 API 的，用命令（如 `_ClearAllMeshes`）并做选择保存/恢复。

## 14. 面板插件「钻石切面」的补充口径（2026-10-09，第 7 个插件）

它是**面板插件**（照 §3–§6 做），但有三处新口径值得写进规范，后面做同类插件照这个来：

1. **面板单位是 mm，文档单位用 `RhinoMath.UnitScale` 换算**：面板里所有长度参数（面片大小、起伏高度）都按 mm 显示与输入，
   `Settings.MmToModel` 存换算系数，生成时用 `FacetSizeModel = FacetSize * MmToModel`（条纹插件同款做法）。
2. **「边界」卡片与其它插件的「选择物件」卡片同构**：卡片里是「当前边界描述 + `选择边界` 按钮（Primary）+ 提示」，
   按钮通过 `PickRequested` 事件让会话去跑隐藏命令 `DiamondFacetPickTarget`（`GetObject.GetMultiple` 支持多选），
   会话再把新的 id 列表交给 `SetTargets` 并重算预览。删除目标时（`RhinoDoc.DeleteRhinoObject`）从列表里摘掉并如实提示。
3. **多边界 = 多套结果**：`Generate` 返回 `List<DiamondFacetResult>`，每个结果独立记「三角面/线/面/面积/耗时/备注」，
   面板状态条给合计 + 第一条备注；失败的边界逐条给原因（不吞掉、不整体失败）。

自检口径（`diamondfacet\src\DiamondFacetSelfTest.cs`，**14 个用例 68 项**）：
- **边界环必须含拐角 + 外轮廓顶点必须吸附到边界曲线**（用例12）：环含 4/4 拐角、环多边形面积 = 边界面积、
  网格包围盒偏差 0、所有裸边顶点落在曲线上。⚠ 取点排序别用 `Curve.LengthParameter`（它是弧长→参数）。
- **输出二选一、不混着给**（用例9）：仅线框 → 曲线数 > 0 且面数 = 0；面 → 面数 > 0 且**曲线数 = 0**（用户口径：
  「选面导出还带线框，我不需要」）；预览也要跟着一致（面模式不叠线框）。
- **一键平滑是勾选项**（用例13）：勾选 = 输出「QuadRemesh 平滑结果（默认转细分物件，图层 `钻石切面-平滑`）+ 原本的平面」，
  不勾 = 只输出原平面。⚠ `Mesh.QuadRemesh()` 只吃闭合网格（开放片一律 null）→ 必须先 `Brep.JoinBreps` 组合所有平面
  再用 `Mesh.QuadRemeshBrep`；⚠ `AdaptiveSize` 是 0~100 百分比（不是 0~1）。
- **圆角功能已取消**（Rhino 圆角对「开放壳体 + 折痕边终止于裸边界」极脆，用户实测「很多顶点破掉了」）。

- 几何不变量要**双向**断言：健康输入不被误改 + 退化输入必须给出明确原因（开放曲线/非平面曲线/弯曲面各自被拒）。
- **退化点集（圆周共圆）必须有底线断言**：兜底剖分要么算对（面积差 <5%）要么**明确拒绝**（报「不可信」），不许给错几何。
- **确定性**：同种子两次生成逐顶点一致；换种子几何必须不同。
- **性能护栏**：面片大小给到 0.0005 也必须不卡死（内部点数上限 8000，超了自动放大并如实写进备注）。

## 15. 「输出二选一 + 一键平滑」共用口径（2026-10-09，钻石切面 / 泰森多边形纹）

这两个插件走同一套输出约定，后续做同类「面片/图案」插件照这个来：

- **输出永远二选一，不混着给**（用户明确：「选面导出还带线框，我不需要」）：
  「**仅线框**」→ 只写曲线；「**面 / 网格面**」→ 只写面/网格。图层分开：`<插件名>-线框` / `<插件名>-面` / `<插件名>-平滑`。
- **一键平滑 = 勾选项 + 两个参数**（只在「面」模式可见，勾选后才可调）：
  - 「平滑度%」80–100（默认 95）→ `QuadRemeshParameters.AdaptiveSize`（**0~100 百分比**，不是 0~1）；
  - 「细分程度」（默认 5000，可手输）→ `TargetQuadCount`；
  - 实现：先组合成一个整体（钻石切面 `Brep.JoinBreps(所有平面面)`；泰森用已有合并网格 `Brep.CreateFromMesh`）→
    `Mesh.QuadRemeshBrep(joined, prm)`（⚠ **实例方法 `Mesh.QuadRemesh()` 对开放面片一律返回 null**）→
    补 `FaceNormals/Normals`（否则预览渲染成黑）→ `SubD.CreateFromMesh` 转细分物件；
  - 勾选 → 输出「平滑结果 + 原本的面」；不勾 → 只输出原本的面。平滑失败要如实说明并回退（不给假成功）。
- **预览要与输出一致**：面模式 = 半透明着色 + `DrawMeshWires`（看得见面片/四边面边界）；线框模式 = 只画线；勾平滑就画平滑后的网格。
