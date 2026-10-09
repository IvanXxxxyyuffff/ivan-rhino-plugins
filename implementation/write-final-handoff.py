from pathlib import Path
import json, hashlib, shutil, datetime

R=Path(__file__).resolve().parent
C=R/'MODIFIED_FILE'
DESK=Path(r'D:\UserData\Desktop\IVAN插件中心')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
audit=read(R/'final-ui-audit.json')
assert audit['summary']['passed'] and not audit['violations']
assert Path(audit['baseline_manifest']) == R/'source-latest.json'
assert Path(audit['candidate_root']) == C
assert audit['contracts']['volumedialog_type_signature']['passed']
smoke=read(R/'native-panel-smoke.json')
assert not smoke['failures'],smoke['failures']
assert len(smoke['plugins'])==5
assert all(p['status']=='passed' for p in smoke['plugins']),[(p['name'],p['status']) for p in smoke['plugins']]
assert read(R/'vape-core-comparison.json')['baseline_modified_literal_equal']
(R/'MODIFIED-VapeVolumeSelfTest.txt').write_text(smoke['vapeVolumeSelfTest']['reportLiteral'],encoding='utf-8')
assert read(R/'rollback-final-audit.json')['passed']
assert read(R/'installed-final-audit.json')['passed']
tx=read(R/'native-transaction.json')
assert tx['diff_reconstructed'] and not tx['original_source_changes']
exe=C/'stripe/out/center/IVAN-CENTER.exe'
md5=hashlib.md5(exe.read_bytes()).hexdigest()
size=exe.stat().st_size
desktop_exe=DESK/'IVAN-CENTER.exe'
if desktop_exe.exists() and sha(desktop_exe)!=sha(exe):
    assert sha(desktop_exe)==sha(R/'baseline-latest-IVAN-CENTER.exe'),'Desktop installer changed externally; do not overwrite'
    shutil.copy2(desktop_exe,R/'INSTALLED_BASELINE/Desktop/IVAN-CENTER-before-final.exe')
for p in [desktop_exe,Path(r'C:\zcode_build\stripe\dist\IVAN-CENTER.exe'),C/'stripe/dist/IVAN-CENTER.exe']:
    p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(exe,p);assert sha(p)==sha(exe);p.read_bytes()
time=datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
lines=[]
for p in smoke['plugins']:
    lines.append('| '+p['name']+' | '+p['status']+' |')
doc=f'''# IVAN CENTER · 本次液态玻璃 UI 实施交接说明

更新：{time}（本机时间）。本文件是本次实施结果，不替代原功能交接书；后续模型先读本文件，再读 `C:\\zcode_build\\UI-REFACTOR-HANDOFF.md`。

## 1. 当前交付和唯一正确的源码入口

- **已交付并静默安装**：`D:\\UserData\\Desktop\\IVAN插件中心\\IVAN-CENTER.exe`。
- 安装器 {size} 字节；MD5 `{md5}`；SHA256 `{sha(exe)}`。
- **新版 UI 的权威完整源码**：`E:\\IVAN-LiquidGlass-preview\\implementation\\MODIFIED_FILE`。安装器项目在此目录的 `stripe\\center\\Center.csproj`。
- `C:\\zcode_build` 保留原始 UI 源码，几何核心为另一任务最新定稿。**不要从 C 直接重编然后覆盖新版安装器**，否则会丢失液态玻璃 UI。C 的 `stripe\\dist\\IVAN-CENTER.exe` 只是此次最终二进制副本。
- 新版安装器另一个副本：`E:\\IVAN-LiquidGlass-preview\\implementation\\MODIFIED_FILE\\stripe\\dist\\IVAN-CENTER.exe`。
- 原安装器完整备份：`E:\\IVAN-LiquidGlass-preview\\implementation\\baseline-latest-IVAN-CENTER.exe`（1673728 字节，MD5 `b98b1f617b5f47875355e752c58ed003`）。旧预览仍在 `E:\\IVAN-LiquidGlass-preview\\gallery.html`，**预览不是 Rhino 功能实现**。

## 2. 本次改了什么

只改 15 个允许的 UI/图标文件，详见 `E:\\IVAN-LiquidGlass-preview\\implementation\\native-transaction.json` 的逐文件原始/修改 SHA256。

1. `shared\\PanelTheme.cs` 以及 `voronoi\\src`、`stripe\\src`、`halftone\\src`、`radialdots\\src`、`stripe\\center` 的 5 份副本：玻璃渐变、圆角、边缘高光、文字层级、按钮/滑块/分段/开关绘制、同族图标。6 份 MD5 一致：`{tx['theme_md5']}`。
2. `VoronoiUi.cs`、`StripePanel.cs`、`HalftoneUi.cs`、`RadialDotsUi.cs`：排版、滚动区和长度的两位小数显示；标题/第一张选择卡/底部操作区保持固定。长度显示不改参数存储、范围、步长与原回调。
3. `src\\VapeVolume\\UI\\VolumeDialog.cs`：**整页**统一到 434px 宽的同族玻璃面板，不是只改两个按钮。标题固定“烟油容量”、动态物件状态放副标题，36px 头图标与安装器烟油 PNG 同源；首选择卡内 140×28 红/绿按钮、紧凑分组、现代开关、4px 细滑轨及14px滑块，底部58px操作区含固定34px按钮行和10px底边距，而非巨大空卡片。`重新生成标注`/`关闭` 为圆角主次按钮。保留原 `Eto.Forms.Button`、`RadioButtonList`、`CheckBox`、`Slider`、`NumericStepper`、原 Click 处理及 Close 路径。`ApplyWpfButtonTemplate` 只赋视觉模板：9px 圆角、渐变、悬停/按下/焦点/禁用态。选择按钮模板绑定原 Background，保留红/绿状态。其他 Eto 后端仍保留原生控件及颜色回退。
4. `stripe\\center\\CenterForm.cs`：现代插件中心，1000×800 客户区、5 个插件及各自安装/更新和卸载操作。左侧品牌、标题栏和 exe 都是蓝色玻璃 IV 应用图标，显式绑定 `Theme.MakeFormIcon("app",32)`，不从烟测宿主 exe 提取错误图标。
5. `stripe\\center\\Installer.cs` **仅 `IconFactory` 类**：复用主题图标绘制。其余字节保持基线一致；注册表、资源名、`Repo.Plugins`、`/silent`、`/uitest` 不变。
6. `iconmake\\Program.cs`、`stripe\\center\\app.ico`：应用及 5 个插件统一图标家族。ICO 的 16/24/32/48/64/128 条目为 DIB，256 为 PNG。主题/iconmake 中 19 个图标绘制方法一致，不能只更新其中一处。

原生图标 PNG：`E:\\IVAN-LiquidGlass-preview\\implementation\\native-icons`；安装器和面板使用对应插件的同族图标，不混用品牌图标。最终真实截图在 `E:\\IVAN-LiquidGlass-preview\\implementation\\native-smoke-*.png`，安装器截图为 `E:\\IVAN-LiquidGlass-preview\\implementation\\MODIFIED-center-full.png`。

## 3. 功能边界和合并来源

- 所有几何/会话/命令/自检、项目文件、插件 GUID、命令和隐藏拾取命令、图层名称均未由本次 UI 工作修改。
- 公开契约（包括 SetTarget、SetTargetState、SetGradientState、SetInfo、RefreshScope、LivePreview、ValueChanged、Settings、Committed）及原点击处理均有静态审计。
- 第一张卡片仍为选择卡：根坐标 Left12/Top56；拾取按钮局部坐标不变；未选红 `#CE4C4C`、已选绿 `#389E5C`、白字。单面时隐藏参考面卡片的逻辑没有改。
- 原 Motion 类保持原样，四档 token 保留 Micro150/Base200/Slow300/Exit150；不得为了视觉去掉 done 回调或改关闭回调。
- 工作期间另一模型更新了泰森核心。经用户确认定稿后，从 C 按字节同步 `CellNurbs.cs`、`VoronoiCore.cs`、`VoronoiPlugin.cs`、`VoronoiSelfTest.cs`，并把最新 `VoronoiUi.cs` 的 **4 个输出档位** 接入新版 UI：整体一张面/跨面平滑/每胞元·网格/每胞元·NURBS。这些核心变更属于外部已完成工作，不是本次视觉重构。
- 最新可信基线为 `E:\\IVAN-LiquidGlass-preview\\implementation\\BASELINE_LATEST` 和 `source-latest.json`，不要用较早 BASELINE 把外部修复回退掉。
- 最终审计：62 个基线源文件中只有上述 15 个允许文件变化；冻结文件、Installer 非 IconFactory 字节、数值范围/步长/回调均通过。详见 `final-ui-audit.json`；`source-latest.json` 中的 C 源文件仍全部符合记录的原始哈希。

## 4. 实际执行的验证结果

### 构建和安装器

- 5 个插件分别 net48 / net7.0-windows 编译：全部 **0 error**。刷新 payload 后安装器编译 0 error。有原有未使用字段/变量 warning，不能写成“0 warning”。
- 最终 `/silent` 进程退出 0；本机安装目录的 10 个 Rhino7/8 `.rhp` 与最终 payload SHA256 全部一致。证据：`installed-final-audit.json`。
- 同输入 `/uitest` 实际结果：**BASELINE 9 个顶层控件、690×660；MODIFIED 11 个、1000×800；ROLLBACK 9 个、690×660**，均整窗重绘两次且 exit0。
- 加强检查 `NativeCenterAudit.exe`：三种状态均 5 个插件、5 个安装/更新、5 个卸载，无按钮越界，重绘两次和关闭通过。仅 `/uitest` 不足以发现 OnLoad 被系统异常框遮住的问题。

### Rhino8 串行自检（最终重新安装后复跑）

| 插件 | 实际通过/失败 | 备注 |
|---|---|---|
| 泰森 | 76 / 0 | 最新外部核心增加 2 条测试；旧交接 74/0 不是当前数量 |
| 条纹 | 55 / 0 | 无新增失败 |
| 参数化阵列 | 25 / 0 | 无新增失败 |
| 径向圆点 | 37 / 1 | 与基线同一项已知失败，不是 UI 回归 |

圆点保留失败原文：`[FAIL] 输出里没有互相交叠的图形（还有 8 对，最大侵入 0.3519mm）`。未改核心来消除它，也没有隐藏它。4 个测试进程均 exit0；验收比较了报告内容而非仅进程退出值。原自检中面板构造、分段切换、重绘两次、关闭路径也通过。

### 5 个真实面板和烟油补充检查

| 面板 | 实际烟测状态 |
|---|---|
{chr(10).join(lines)}

最终脚本真的打开窗口、检查控件树/边界、重绘两次并关闭；烟油补充 WPF 原生树/按钮模板与原按钮动作。真实点击“重新生成标注”得到原“还没有选择物件。”提示且未生成几何，真实点击“关闭”成功。**具体每一项以 `native-smoke-extended-audit.json` 为准**，不是只按截图判断。烟油算法自检报告为 `MODIFIED-VapeVolumeSelfTest.txt`。

烟油补充算法覆盖不是全通过：A/C/D 的实际/理论值通过、E 活标注自动更新成功、F 推荐规则全部正确；**B 原样输出“B 布尔差集失败，跳过”**，原核心自检结束还遗留3个临时对象。根代理用原版和修改版安装器在各自新建的空文档中同命令实跑，报告字面内容逐字相同、两边都 B 跳过/3件残留，证据 `vape-core-comparison.json`、`BASELINE-LATEST-VapeCore.json`、`MODIFIED-FINAL-VapeCore.json`。这是已有核心自检问题，不改冻结代码来让结果变绿。烟测的自有临时模型处理与核心自检自行清理分开记录，不触碰用户模型；不能把测试辅助清理包装成核心 cleanup 通过。

首轮 Halftone 烟测曾把“单面时按原逻辑隐藏参考面分段”误报为缺失，已修正审计器，没有为通过测试强行显示控件；首轮报告保留为 `native-panel-smoke-attempt1.json`。

### 回滚和补丁

- 用 `ROLLBACK.sh` 在最终修改版的 **单独副本** 上实际恢复；62 文件 SHA256 全匹配最新基线；恢复后的安装器重编、同 `/uitest` 和加强检查均通过。原新版候选保留修改状态，不在已安装目录试验回滚。
- `UI-PREVIEW.patch` 含预览+原生 UI 补丁；`NATIVE-UI.patch` 为原生部分。实际 Git apply 后，15 个原生文件以及预览 HTML 与最终修改版逐字节一致。

## 5. 后续模型的执行入口

工具路径：Python `C:\\Users\\Administrator\\AppData\\Local\\Programs\\Python\\Python312\\python.exe`；dotnet `C:\\zcode_tools\\dotnet\\dotnet.exe`；Rhino `D:\\Rhino 8\\System\\Rhino.exe`。

**每次源码改动后的顺序**：关闭用户 Rhino → 串行双 TFM 构建 → 刷 payload → 编安装器 → 静态契约审计 → /uitest+完整控件审计 → /silent → 串行 4 自检 → 5 面板烟测 → 哈希/补丁/回滚 → 再更新交接。不要同时运行多个 Rhino，也不要在 Rhino 自检同时编译，本机只有 16GB RAM。

```powershell
$r='E:\\IVAN-LiquidGlass-preview\\implementation'
$py='C:\\Users\\Administrator\\AppData\\Local\\Programs\\Python\\Python312\\python.exe'
& 'C:\\zcode_tools\\dotnet\\dotnet.exe' build "$r\\MODIFIED_FILE\\voronoi\\Rhino8\\VoronoiTexture.csproj" -c Release --nologo -v minimal
& $py -X utf8 "$r\\native-gate.py" build-panels
& $py -X utf8 "$r\\native-gate.py" build-vape-center
& $py -X utf8 "$r\\final-ui-audit.py" --manifest "$r\\source-latest.json" --output "$r\\final-ui-audit.json"
& $py -X utf8 "$r\\native-gate.py" uitest "$r\\MODIFIED_FILE\\stripe\\out\\center\\IVAN-CENTER.exe" MODIFIED-next-uitest
# 先确认没有用户 Rhino，再 /silent 和以下串行测试
& "$r\\MODIFIED_FILE\\stripe\\out\\center\\IVAN-CENTER.exe" /silent
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$r\\run-native-selftests.ps1"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$r\\run-native-smoke.ps1"
```

`build-panels` 包含泰森7及其他6个TFM；泰森8需要上面单独那条命令。脚本参数/退出值以本地文件为准，任何一项失败不要继续发布。禁止强制结束用户 Rhino；脚本只管理自己创建的 PID。

## 6. 四角色及恢复方法

1. MODIFIED_FILE：`E:\\IVAN-LiquidGlass-preview\\implementation\\MODIFIED_FILE`（补充原预览角色 `E:\\IVAN-LiquidGlass-preview\\index.html`）。
2. DIFF_FILE：`E:\\IVAN-LiquidGlass-preview\\UI-PREVIEW.patch`。
3. VERIFICATION：`E:\\IVAN-LiquidGlass-preview\\VERIFICATION.txt`，附字面输出、stderr、退出值、源码哈希和状态变化；命令事件另见 `implementation\\native-run.jsonl`。
4. ROLLBACK：`E:\\IVAN-LiquidGlass-preview\\ROLLBACK.sh`。原生目录模式使用同目录树下的 BASELINE_LATEST，携带整套目录，不要只复制 .sh。

已验证的恢复调用（目标必须是你准备好的独立副本，不能是用户原始目录）：

```powershell
& 'C:\\Program Files\\Git\\bin\\bash.exe' 'E:/IVAN-LiquidGlass-preview/ROLLBACK.sh' 'E:/IVAN-LiquidGlass-preview/implementation/ROLLBACK_TEST_FINAL'
```

Git Bash 子进程在本机接受 `E:/...`，此前 `/e/...` 调用真实失败127，修正后成功。源码恢复不等于自动改变已安装插件；要退回旧版安装，保存/关闭用户 Rhino 后运行备份安装器 `/silent`。旧版备份包含最新泰森核心，不要误用最早安装器。

## 7. 已解决坑与尚未验证边界

- 不要 `using(var f = Font)`，控件字体不是这段绘制代码拥有的资源。
- DrawToBitmap 之前必须 Show；保留 Motion done 和关闭动画完成回调。
- 不把图片写进 logs；本次图片在 implementation 或 TEMP，Rhino logs 保持文本/flag。
- WinForms PluginRow 不支持透明 BackColor，初次 OnLoad 真实异常已记录，已改主题 SurfaceAlt。
- 面板卡片必须仍可被冻结自检直接在 Form.Controls 找到，不能整体包进 ScrollPanel；泰森 scopeCard 保持根卡片最后一个，分段仍为卡片第一个相关子控件。第一次嵌套导致自检 70/1，修正后最终 76/0。
- Rhino.Geometry.Point 和 Drawing.Point 容易冲突，本次滚动辅助代码已显式用 System.Drawing.Point。
- mm 两位小数用纯显示 helper；不要改原 AddRow 函数的范围/步长/回调来“顺便优化”。
- 泰森胞元尺寸首行的 Label/Numeric y 偏移不同，旧的精确 `number.Top+6` 匹配漏掉18.0；已把纯显示helper改为同排8px内匹配，保留原AddRow函数/范围/步长，实机审计两位小数后再验收。
- Eto RadioButtonList 没有 Font 属性，尝试直接赋值曾导致两个 TFM 共4个 CS1061；已移除无效赋值，字体/胶囊只走可用的原生视觉路径。
- IronPython2/CLR 中文字符串的 JSON ASCII encoder 曾失败；审计报告现在先转纯 Python/Unicode、ensure_ascii=False 写 UTF-8。PowerShell 捕获字符串须去掉 ETS 元数据，进程必须保留 Handle 才能可靠读取 ExitCode，不能把 null 当0。所有失败源和旧报告已保留。
- 补丁重建需 Git `core.autocrlf=false`，否则 CRLF/LF 变化使看似应用成功但 HTML 字节不一致。
- 最后 installed hash 检查曾把目录 `rh7` 当成 rhino8，是审计路径识别错误；修正后 10/10 匹配，没有因此改或重装不同核心。
- **实际运行环境仅 Rhino8 / Windows WPF**；Rhino7 已编译但没有真实运行验证；其他 Eto 后端、低分辨率/不同缩放、不同 Windows DWM 材质效果没有完整实机覆盖。玻璃效果以原生渐变/边缘高光为基础，DWM 支持则增强，不保证所有系统获得同样背景模糊。
- 圆点 37/1 的已知几何残留可由下一模型另开功能修复，不属于本次 UI 工作；修复前需重新定义基线，不能把它归为新 UI 失败。
- 烟油原有 B 布尔差集跳过/3件自检残留也供后续模型另行处理；本次没有擅改几何计算或自检来消除它们。

此次没有新增云任务，也没有重传公网安装包。所有实际失败尝试、修正过程及最终通过证据保留在本机；后续不要只阅读旧 PROJECT-STATE 的体积/74条结果而忽略本文件。
'''
paths=[R/'UI-IMPLEMENTATION-HANDOFF.md',Path(r'C:\zcode_build\UI-IMPLEMENTATION-HANDOFF.md'),DESK/'UI重构实施交接说明-本次CODEX.md']
for p in paths:p.write_text(doc,encoding='utf-8');assert p.read_text(encoding='utf-8')==doc
pointer=f'> **UI 实施最新结果（{time}）**：请先读 `C:\\zcode_build\\UI-IMPLEMENTATION-HANDOFF.md`。新版 UI 源码在 `E:\\IVAN-LiquidGlass-preview\\implementation\\MODIFIED_FILE`，不是本目录旧 UI 源码。桌面新版安装器 {size} 字节，MD5 `{md5}`；泰森最新自检 76/0、条纹55/0、阵列25/0、圆点37/1（原已知残留）。\n\n'
for p in [Path(r'C:\zcode_build\PROJECT-STATE.md'),C/'PROJECT-STATE.md',DESK/'项目状态存档.md',DESK/'README.md']:
    original=p.read_text(encoding='utf-8-sig')
    if pointer not in original:p.write_text(pointer+original,encoding='utf-8')
    p.read_bytes()
result={'time':time,'installer_bytes':size,'md5':md5,'sha256':sha(exe),'handoff_paths':[str(p) for p in paths],'deployed_paths':[str(desktop_exe),r'C:\zcode_build\stripe\dist\IVAN-CENTER.exe',str(C/'stripe/dist/IVAN-CENTER.exe')],'all_readback_verified':True}
(R/'delivery-final.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
with (R.parent/'VERIFICATION.txt').open('a',encoding='utf-8') as f:f.write('\nFINAL_DELIVERY_AND_HANDOFF_READBACK\n'+json.dumps(result,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(result,ensure_ascii=False,indent=2))
