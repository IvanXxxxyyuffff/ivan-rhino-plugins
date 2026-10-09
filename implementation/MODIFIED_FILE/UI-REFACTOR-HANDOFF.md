> ⚠ **源码权威位置已迁移（2026-09-30 18:26 起）**：UI/图标已由 CODEX 重构，**新版 UI 的权威完整源码在 `E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE`**（安装器项目 `E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\center\Center.csproj`）。
> 本目录（`C:\zcode_build`）保留的是**旧 UI** + **几何核心定稿**；几何核心与新树逐字节一致。
> **不要再从本目录直接重编并覆盖新版安装器**（会丢掉液态玻璃 UI）；改几何请改新树，或改完两处同步。
> 本次实施说明：`D:\UserData\Desktop\IVAN插件中心\UI重构实施交接说明-本次CODEX.md`

# UI / 图标重构交接书（给 CODEX）

> 目的：把**安装器 + 5 个插件面板 + 图标**的界面重构工作交给 CODEX，同时保证**不影响**插件几何/逻辑侧的修 BUG 工作。
> 读这份 + `DESIGN-CONSISTENCY.md`（设计规范）+ `PROJECT-STATE.md`（项目真相源）即可开工。
> 生成时间：2026-09-30。基线自检：泰森 74/0、条纹 55/0、阵列 25/0、圆点 37/1（残留 8 对 0.35mm 微重叠，已知）。

---

## 1. 允许重构的范围（UI 层，随你怎么改外观）

| 内容 | 路径 | 说明 |
|---|---|---|
| 共用主题/控件/动效 | `C:\zcode_build\shared\PanelTheme.cs` | **唯一母本**。改完必须同步到 5 份副本（见 §4 的同步脚本），6 份 md5 必须一致 |
| 泰森面板 | `C:\zcode_build\voronoi\src\VoronoiUi.cs` | 面板外观 + 控件布局 + 文案 |
| 条纹面板 | `C:\zcode_build\stripe\src\StripePanel.cs` | 同上 |
| 阵列面板 | `C:\zcode_build\halftone\src\HalftoneUi.cs` | 同上 |
| 圆点面板 | `C:\zcode_build\radialdots\src\RadialDotsUi.cs` | 同上 |
| 烟油面板（Eto） | `C:\zcode_build\src\VapeVolume\UI\VolumeDialog.cs` | Eto 对话框，风格要与四个 WinForms 面板一致 |
| 安装器窗口 | `C:\zcode_build\stripe\center\CenterForm.cs` | 行/进度条/按钮/动效 |
| 安装器图标工厂 | `C:\zcode_build\stripe\center\Installer.cs` 里的 `IconFactory` 类（约 150–320 行） | **只改这个类**，同文件其它部分（`Repo.Plugins`、注册表逻辑、payload 逻辑）**不许动** |
| EXE 应用图标 | `C:\zcode_build\iconmake\Program.cs`（生成多尺寸 `app.ico`） | 生成规则：<256 用 DIB 条目、256 用 PNG |

## 2. 冻结范围（几何/逻辑层，**绝对不要动**）

- `voronoi\src\`：`VoronoiCore.cs`、`CellNurbs.cs`、`VoronoiPlugin.cs`（含会话/命令）、`VoronoiSelfTest.cs`
- `stripe\src\`：`StripePattern.cs`、`StripeSession.cs`、`StripeCommand.cs`、`SelfTest.cs`
- `halftone\src\`：`HalftoneCore.cs`、`HalftoneGlobal.cs`、`HalftonePlugin.cs`、`HalftoneSelfTest.cs`
- `radialdots\src\`：`RadialDotsCore.cs`、`RadialDotsPlugin.cs`、`RadialDotsSelfTest.cs`
- `src\VapeVolume\`：除 `UI\VolumeDialog.cs` 以外的所有文件
- `stripe\center\`：除 `CenterForm.cs` 与 `Installer.cs` 的 `IconFactory` 以外的一切（`Repo.Plugins`、注册表、payload、`/silent`、`/uitest` 逻辑）

> 需要改这些文件里的**东西**（比如加一个面板控件的数据字段）时：**只加字段，不改语义**，并且必须跑 §5 的验证。

## 3. 必须保持的接口契约（面板 ↔ 会话）

面板类要保留这些**公开成员**（会话按这些名字调用；改名/改签名会编译不过）：

- 属性：`Settings`（可读）、`Committed`（可读）、`LivePreview`（可读）
- 事件：`ValueChanged`（参数变化 → 会话做防抖重算）
- 方法：`SetInfo(string)`、`Close()`、`Activate()`
- 各面板特有（**名字与语义都不要改**）：
  - 泰森：`SetTarget(string)`、`SetTargetState(bool)`、`SetGradient(string)`、`SetGradientState(bool)`、`RefreshScope()`
  - 条纹：`SetTarget(string)`、`SetTargetState(bool)`
  - 阵列：`SetTarget(string)`、`SetTargetState(bool)`、`SetGradientState(bool)`、`SetCenterInfo(string)`、`RefreshScope()`
  - 圆点：`SetPlace(string)`、`SetInfo(string)`

**必须保持的产品约定**（用户明确要求，别"顺手优化"掉）：
1. **面板第一张卡片是「选择」卡片**：参考物件按钮 `(12, 8, 140, 28)`、渐变物件按钮 `(160, 8, 150, 28)`；`FlatButton.PickStyle=true` + `Picked` → **未选红 `#CE4C4C` / 已选绿 `#389E5C`**、白字。
2. 命令名（`VoronoiTexture` / `StripeOnSurface` / `ParametricTexture` / `RadialDots` / `VapeVolume`）与隐藏拾取命令名（`*PickTarget` / `*PickGradient` / `*SelfTest`）**不许改**（安装器 `ExtraCommands` 与用户习惯都依赖它们）。
3. 图层名不许改：「泰森多边形纹」「表面条纹」「阵列纹理」「径向渐变圆点」。
4. 面板**永远先开、再选物件**（命令不看文档预选）；面板非模态、实时预览可关。
5. 参数单位一律 mm、数字框两位小数；动效只用 `Motion.Micro/Base/Slow/Exit` 四档。

## 4. 改主题的同步（每次改完 `shared\PanelTheme.cs` 都要做）

```bash
cd /c/zcode_build   # 用 Git Bash；注意本机 bash 下要用 "C:/..." 风格路径
for d in voronoi/src stripe/src halftone/src radialdots/src diamondfacet/src stripe/center; do cp -f shared/PanelTheme.cs "$d/PanelTheme.cs"; done
md5sum shared/PanelTheme.cs voronoi/src/PanelTheme.cs stripe/src/PanelTheme.cs halftone/src/PanelTheme.cs radialdots/src/PanelTheme.cs diamondfacet/src/PanelTheme.cs stripe/center/PanelTheme.cs   # 7 个必须一致（2026-10-09 起：第 7 个插件 diamondfacet 也编译这份）
```
> 改了主题/图标后**必须全量重建所有插件**（7 个插件里 6 个编译 PanelTheme：泰森/条纹/阵列/圆点/钻石切面 + 安装器），
> 并把 `iconmake\Program.cs` 里同名的渲染方法保持逐方法一致（`final-ui-audit.py` 的 `glass_renderers` 契约会查）。

## 5. 每次改完必须验证（回归网，一条都不能跳）

```bash
D="C:/zcode_tools/dotnet/dotnet.exe"
# ① 双 TFM 全编（0 error）
"$D" build "C:/zcode_build/voronoi/Rhino8/VoronoiTexture.csproj"   -c Release
"$D" build "C:/zcode_build/voronoi/Rhino7/VoronoiTexture.csproj"   -c Release
"$D" build "C:/zcode_build/stripe/Rhino8/StripeOnSurface.csproj"   -c Release
"$D" build "C:/zcode_build/stripe/Rhino7/StripeOnSurface.csproj"   -c Release
"$D" build "C:/zcode_build/halftone/Rhino8/HalftoneDots.csproj"    -c Release
"$D" build "C:/zcode_build/halftone/Rhino7/HalftoneDots.csproj"    -c Release
"$D" build "C:/zcode_build/radialdots/Rhino8/RadialDots.csproj"    -c Release
"$D" build "C:/zcode_build/radialdots/Rhino7/RadialDots.csproj"    -c Release
"$D" build "C:/zcode_build/src/VapeVolume/VapeVolume.csproj"       -c Release
"$D" build "C:/zcode_build/stripe/center/Center.csproj"            -c Release

# ② 刷安装器负载（**名字必须一致**：p_*7 → payload/<Name>-rh7.rhp、p_*8 → payload/<Name>-rh8.rhp、Res8Dll → payload/<Name>.dll）
P="C:/zcode_build/stripe/center/payload"
cp -f voronoi/out/rhino7/VoronoiTexture.rhp "$P/VoronoiTexture-rh7.rhp";  cp -f voronoi/out/rhino8/VoronoiTexture.rhp "$P/VoronoiTexture-rh8.rhp";  cp -f voronoi/out/rhino8/VoronoiTexture.dll "$P/VoronoiTexture.dll"
cp -f stripe/out/rhino7/StripeOnSurface.rhp "$P/StripeOnSurface-rh7.rhp"; cp -f stripe/out/rhino8/StripeOnSurface.rhp "$P/StripeOnSurface-rh8.rhp"; cp -f stripe/out/rhino8/StripeOnSurface.dll "$P/StripeOnSurface.dll"
cp -f halftone/out/rhino7/HalftoneDots.rhp "$P/HalftoneDots-rh7.rhp";     cp -f halftone/out/rhino8/HalftoneDots.rhp "$P/HalftoneDots-rh8.rhp";     cp -f halftone/out/rhino8/HalftoneDots.dll "$P/HalftoneDots.dll"
cp -f radialdots/out/rhino7/RadialDots.rhp "$P/RadialDots-rh7.rhp";       cp -f radialdots/out/rhino8/RadialDots.rhp "$P/RadialDots-rh8.rhp";       cp -f radialdots/out/rhino8/RadialDots.dll "$P/RadialDots.dll"
# 重编安装器 → 拷 dist + 桌面 → 静默安装：现在一条命令搞定（Rhino 必须先关）
#    pwsh -File install-and-verify.ps1        # /silent + 日志/注册表/payload/RUI 全自动核对（约 1~5 秒；-SkipInstall 只核对）
#    ⚠ Rhino 在跑时 /silent 会立刻 exit 2 中止（不是卡死）；bash 的 ls/stat 在 C: 上会给过期时间戳，别用它判断安装状态

# ③ 串行跑自检（16GB 内存，**不要并发**；flag 文件 + 报告在 %LOCALAPPDATA%\IVAN\logs\**）
#    一条命令跑完全部（推荐）：pwsh -File run-native-selftests.ps1   → 逐项断言 + 汇总表 + 写 MODIFIED-*.txt
#    · 期望值随「用户 3dm 是否存在」自动选；单项不符不中断整轮；迭代期用 -Only <名字>（15~60 秒）
#    · **默认隐藏窗口跑（不弹窗、不抢焦点）**；只有要验「真渲染显示」才加 -Visible（MeshFix 用例6 会弹一个窗口）
#    · 别改成「一次 Rhino /runscript 跑完」：面板冒烟用例在命令上下文里会死锁（见 PROJECT-STATE §15）
#    run-voronoi-selftest.flag / VoronoiSelfTest.txt        期望 74 通过 / 0 失败（曾 76：用例7 读桌面的 ceshi.3dm，文件已不在 → 跳过 2 项）
#    run-selftest.flag         / StripeSelfTest.txt         期望 55 通过 / 0 失败
#    run-halftone-selftest.flag/ HalftoneSelfTest.txt       期望 23 通过 / 0 失败（曾 25：用例6 读桌面的 anli.3dm，文件已不在 → 跳过 2 项）
#    run-radialdots-selftest.flag / RadialDotsSelfTest.txt  期望 37 通过 / 1 失败（已知残留，别当回归）
#    run-meshfix-selftest.flag / MeshFixSelfTest.txt        期望 14 通过 / 0 失败 / 1 跳过（第 6 个插件，见附录 B；
#      隐藏窗口下「显示可见性」用例 SKIP（报告写明原因），要这条证据用 -Visible -Only MeshFix → 15 通过 / 0 失败）
#    run-diamondfacet-selftest.flag / DiamondFacetSelfTest.txt 期望 79 通过 / 0 失败（第 7 个插件，见附录 C）
#    run-vape-selftest.flag    / VapeVolumeSelfTest.txt     末段面板冒烟 3 通过 / 0 失败

# ④ 安装器界面自检：IVAN-CENTER.exe /uitest（截图 + 日志写 %TEMP%）
```

**判定标准**：① 全 0 error；③ 四个自检数字**不低于上表**（面板冒烟用例会真正构造面板 + 重绘两遍 + 走关闭路径，UI 改坏了它一定红）。

## 6. 已知坑（照 `rhino-plugin-dev` 技能 §12–§14，最容易踩的六条）

1. **不要 `using (var f = Font)`** —— 那会把控件自己的字体 Dispose 掉，第二次重绘抛「参数无效」，WinForms 把控件画成**红叉且不抛异常**（表现为"一堆红色线框"）。正确写法 `var f = Font;`。
2. **动效补间的 done 回调必须真的被调用**（`Motion.To(...)` 的最后一个参数）——丢了会出现"面板关不掉、透明还吃点击、预览定时器继续跑 → Rhino 卡死"。
3. **`Control.DrawToBitmap` 对没显示过的窗体只画标题栏** —— 自检里要先 `Show()`。
4. 插件往 `%LOCALAPPDATA%\IVAN\logs\` 写图片会被 Rhino 改名成 `.IPGSD` → 图片写 `%TEMP%`。
5. **.ico 不能全用 PNG 条目**（Windows 读不出来）：<256 用 DIB/BMP、256 用 PNG；`<ApplicationIcon>` 只影响 EXE，窗体标题栏要显式 `Form.Icon`（面板用 `Theme.MakeFormIcon(kind,16)`）。
6. `Theme.DrawGlyph` / `IconFactory.Create` 的 `kind` 未命中会**落到默认分支**（画成条纹图标）→ 加新图标时两个文件都要加分支；每个插件的色相不与现有五个重复（青/蓝/靛/琥珀/紫）。

## 7. 交付与文档同步

- 交付目录固定 `D:\UserData\Desktop\IVAN插件中心\`（**不是 C 盘桌面**）：`IVAN-CENTER.exe` + `README.md` + `项目状态存档.md` + `设计一致性规范.md`。
- README 里的安装器 **字节数 + md5 每次都要更新**。
- UI/图标改完：更新 `PROJECT-STATE.md`（界面章节 + 交付 hash）+ 同步桌面 `项目状态存档.md`；如果改了设计规范本身，同时更新 `DESIGN-CONSISTENCY.md`（项目内与技能目录各一份）。

---

## 附录 A：`胞元输出` 控件（**语义冻结，重构时不许删项**）

泰森面板「参考面与输出」卡片里的 `IvanSegmented` 必须是 **4 项**，顺序与含义固定：

| 索引 | 文案 | 语义 | 设置项映射 |
|---|---|---|---|
| 0 | `整体一张面` | 各面网格合并焊接成一张连续面（保留输入折痕） | `PerCell=false, SmoothSeam=false, NurbOutput=false` |
| 1 | `跨面平滑` | 同上，但接缝法向混合、连折痕一起抹平 | `PerCell=false, SmoothSeam=true, NurbOutput=false` |
| 2 | `每胞元·网格` | 每胞元一个**网格**对象（边界是网格格子，放大有台阶） | `PerCell=true, NurbOutput=false` |
| 3 | `每胞元·NURBS` | 每胞元**一张平滑 NURBS**（边界=胞元多边形直边、G0 拼合、边缘贴合原曲面） | `NurbOutput=true` |

- 选中时写回：`Settings.NurbOutput = (idx == 3); Settings.PerCell = (idx == 2); Settings.SmoothSeam = (idx == 1);` 然后 `Raise()`。
- 初始/刷新（`RefreshScope`）：`SelectedIndex = NurbOutput ? 3 : (PerCell ? 2 : (SmoothSeam ? 1 : 0))`。
- 提示行（`CellHint()`）四句必须区分开，尤其：
  - 索引 2 → 「每胞元一张【网格】分开输出（边界是网格格子，放大看会有台阶；要光滑边缘请选右边那项）」
  - 索引 3 → 「每胞元一张平滑 NURBS（G0 拼合、边界就是胞元多边形、边缘贴合原曲面）；预览仍是网格，点「生成」才算」
- 控件宽度 ≥ 320（4 项中文标签），标签「胞元输出」宽 60。
- **不要**再单独放一个「输出多重曲面（NURBS）」勾选框（已经合并进来；并存会让用户选错 —— 这是用户实际踩过的坑）。

> 验收：`VoronoiSelfTest` 里 `用例12` 会检查「修剪后的胞元曲面外环边数 ≤ 12」（= 多边形直边，不是网格台阶），
> 以及「`WantCellSurfaces=false` 时 `CellSurfaces.Count == 0`」（预览不建 NURBS）。重构后这两条必须仍绿。

---

## 附录 B：第 6 个插件「网格修复 MeshFix」（2026-10-08 加入，**不属于本次 UI 重构**）

用户口径：「把这项修复能力集成到 IVAN CENTER，这个功能不需要面板，只需要一个图标，点一下即可修复」。

- 源码 `…\MODIFIED_FILE\meshfix\`（`src\MeshFixCore.cs` / `MeshFixPlugin.cs` / `MeshFixSelfTest.cs` + `Rhino7\` / `Rhino8\`）。
  **没有 PanelTheme.cs**（无面板工具不编译控件库），也没有会话/预览 conduit。
- 安装器侧共 4 处改动：`Repo.Plugins` 追加 `MeshFix`（**追加在末尾**，保住前 5 个 RUI item/bitmap 索引）、
  `Center.csproj` 加 5 条 `EmbeddedResource`、`payload\MeshFix*` 5 个文件、`CreateGlassIcon` 加 `meshfix` kind（绿 #208A60）。
- `CenterForm` 的行高/行数/卡片标题改成按 `Repo.Plugins.Count` 自动算（现在是 6 行、标题「插件套件 · 06」），
  `PluginRow` / `IconFactory` / `Motion` / 控件家族**一个都没改**；工具栏由 `InstallToolbar` 自动多一个按钮（宏 `! _MeshFix`）。
- 图标：`GlassDrawMeshFix`（三角网格补片 + 右下角对勾徽标）同时写进 6 份 `PanelTheme.cs` + `iconmake\Program.cs`，
  6 份 md5 仍一致（`74bf39fbd842af05cd5528a1bdde12ee`）、Theme↔IconMake 渲染器 parity 保持。
- 修复动作最终定稿为**三步**（用户报「点了还是不可见」后逐条定位）：写物件级参数（**读回复核**）→
  `_ClearAllMeshes` 清网格缓存 → 用写进去的参数重建渲染网格。少任何一步，网格数据看着好了（24066 面）但面体不显示。
- 验收：`MeshFixSelfTest` **15 通过 / 0 失败**，其中显示验收用真渲染截图（`_-ViewCaptureToFile`，
  判据 = 浅中性灰着色面体）：修复前 **0 像素** → 修复后 **24335 像素**。
- 交付 hash（历史值）：安装器 2093568 字节，md5 `379f99a77c229351799f8cedf658615a`（6 个插件，payload 里 6 份 .rhp 与安装目录逐字节一致）。

**给 CODEX 的两条提醒**：
1. `shared\PanelTheme.cs` 是被 5 个插件一起编译的 —— 任何图标/主题改动都必须**全量重建所有插件**再刷 payload，
   否则交付的二进制与源码不一致（本次实测每个插件 +1536 字节）。
2. `final-ui-audit.py` 现在报 23 条 violation（不是 0）。**逐条归属**（2026-10-08 实跑，报告 `MESHFIX-ui-audit.json`）：
   - **属于第 6 个插件（预期增量，7 条）**：`unexpected_candidate_source` ×5（`meshfix/Rhino7|Rhino8|src/*`）、
     `frozen_file_changed` ×1（`stripe/center/Center.csproj` 加 5 条 `EmbeddedResource`）、
     `installer_changed_outside_iconfactory` ×1（`Installer.cs` 加 `Repo.Plugins` 的 MeshFix 定义）——
     这三处正是 `DESIGN-CONSISTENCY.md §10` 规定的安装器集成点，加任何新插件都必然触发。
   - **属于烟油面板 UI 重写（2026-10-08，早于本次任务，16 条）**：`src/VapeVolume/UI/VolumeDialog.cs` 13 条
     （`public_member_missing` / `setting_or_range_missing` / `event_binding_missing` / `event_handler_body_changed` /
     `frozen_logic_method_changed` ×9）、`VapeVolume.csproj` + `VapeVolumePlugIn.cs` 各 1 条 `frozen_file_changed`、
     `src/VapeVolume/UI/VapePanelSmoke.cs` 1 条 `unexpected_candidate_source`。起因是用户报「烟油面板 UI 元素跟其他面板
     不一样」后把整页改成 WinForms + IvanUi（用户明确要求），功能侧自检 3/0、A–F 段与旧版逐行一致。
   - **仍然全绿的 UI 断言**：主题 6 份 md5 一致、Motion 6 份一致、`theme_to_iconmake_renderer_parity`、
     `centerform_operation_methods`、`scroll_hierarchy`（含泰森分段控件归属）、`volumedialog_type_signature`。
   `final-ui-audit.py` 与 `source-latest.json` **一字未改**；要让报告回到 0 violation 需要重新定义基线
   （把烟油重写与第 6 个插件纳入），属后续任务，本次没有擅自改审计口径。

## 附录 C：第 7 个插件「钻石切面 DiamondFacet」（2026-10-09 加入，**不属于本次 UI 重构**）

**它是什么**：面板插件（4 张卡：边界 / 切面参数 / 输出与边界 / 预览选项），在平面或闭合曲线边界内生成凹凸的钻石切面；
参数 = 面片大小、起伏高度、松弛次数、随机种子、固定边界；输出可选「仅线框」或「线框 + 每个三角面独立的 NURBS 平面」。

- 源码 `…\MODIFIED_FILE\diamondfacet\`（`src\DiamondFacetCore.cs` / `DiamondFacetUi.cs` / `DiamondFacetPlugin.cs` /
  `DiamondFacetSelfTest.cs` / `DiamondFacetProbe.cs` / `PanelTheme.cs` + `Rhino7\`(net48) / `Rhino8\`(net7.0-windows)）。
  面板控件全部复用 `IvanUi` 家族（CardPanel/IvanSlider/IvanCheck/IvanSegmented/FlatButton/InfoBar/HeaderBand），
  滚动卡片机制与圆点/阵列/泰森/条纹**同构**（`AddScrollCard` / `LayoutScrollCards` / `HookScrollFocus` / `EnsureScrollControlVisible`）。
- 安装器侧 4 处改动：`Repo.Plugins` 追加 `DiamondFacet`（**追加在末尾**，保住前 6 个 RUI item/bitmap 索引）、
  `Center.csproj` 加 5 条 `EmbeddedResource`（`p_dia7/p_dia8/p_dia8_dll/j_dia8_deps/j_dia8_rt`）、`payload\DiamondFacet*` 5 个文件、
  `CreateGlassIcon` 加 `diamond` kind（紫罗兰 #5C4CD0）。
- `CenterForm` 的**行高/行数/卡片标题本来就是按 `Repo.Plugins.Count` 自动算**（第 6 个插件时改好的），本次**一行没动**，
  自动变成 7 行 + 标题「插件套件 · 07」；`PluginRow` / `IconFactory` / `Motion` / 控件家族一个都没改。
- 图标：`GlassDrawDiamond`（5 个明暗错落的三角刻面 + 细边线）写进 **7 份 `PanelTheme.cs` + `iconmake\Program.cs`**，
  7 份 md5 仍一致（`52171c002446…`，74283 字节）、`theme_to_iconmake_renderer_parity` 保持（21 个渲染方法）。
- 验收：`DiamondFacetSelfTest` **79 通过 / 0 失败**（15 个用例，含顶点贴边界、一键平滑、面板交互）；`/uitest` 截图 7 行；工具条 RUI 7 个按钮（`! _DiamondFacet` 第 7 位）；
  payload↔安装目录 **7/7 逐字节一致**；全量回归 Voronoi 74/0 · Stripe 55/0 · Halftone 23/0 · RadialDots 37/1 · MeshFix 15/0 · DiamondFacet 79/0。
- 交付 hash：安装器 **2596352 字节，md5 `81aec142a22154190bffe96de8a3956f`**（7 个插件；2026-10-09：顶点贴边界 + 输出二选一 + 一键平滑）。

**给 CODEX 的三条提醒**：
1. `shared\PanelTheme.cs` 现在被 **6 个插件**一起编译（+ 安装器 = 7 份副本）—— 任何图标/主题改动都必须**全量重建 7 个插件**
   再刷 payload（本次实测每个插件 +2024 字节）。
2. 新增的几何结论（`Mesh.CreateFromTessellation` 的 `outlines` 参数不生效、自实现 Delaunay 要打乱插入顺序 + 相对容差、
   兜底结果必须面积复核）写在 `PROJECT-STATE.md §14`，别在没读之前动 `DiamondFacetCore.cs` 的剖分路径。
3. `final-ui-audit.py` 本次**只加了一行**（把 `diamondfacet/src/PanelTheme.cs` 纳入 `THEME_PATHS` 一致性检查，属**收紧**），
   其余口径与 `source-latest.json` 一字未改。现在报 **31 条 violation**，逐条归属（2026-10-09 实跑，报告 `DIAMOND-ui-audit.json`）：
   - **属于第 7 个插件（预期增量，9 条）**：`unexpected_candidate_source` ×8（`diamondfacet/Rhino7|Rhino8|src/*`）、
     `frozen_file_changed` ×1（`stripe/center/Center.csproj` 加 5 条 `EmbeddedResource`）；另加契约项
     `installer_outside_iconfactory = false`（`Installer.cs` 的 `Repo.Plugins` 追加了 DiamondFacet 定义）。
   - **属于第 6 个插件（仍不在基线里，5 条）**：`meshfix/Rhino7|Rhino8|src/*` 的 `unexpected_candidate_source`。
   - **属于烟油面板 UI 重写（2026-10-08，17 条）**：`src/VapeVolume/UI/VolumeDialog.cs` 14 条 + `VapeVolume.csproj`、
     `VapeVolumePlugIn.cs` 各 1 条 `frozen_file_changed` + `src/VapeVolume/UI/VapePanelSmoke.cs` 1 条 `unexpected_candidate_source`。
   - **仍然全绿的 UI 断言**：主题 7 份 md5 一致、`theme_to_iconmake_renderer_parity`、`centerform_operation_methods`、
     `scroll_hierarchy`、`settings_rows`、`panel_logic_methods`、`volumedialog_type_signature`。
   要让报告回到 0 violation 需要重新定义基线（把烟油重写、第 6/7 个插件纳入），属后续任务。
