# IVAN 外掛中心 · Rhino 7/8 外掛合集

[简体中文](README.md) | [English](README.en.md) | [日本語](README.ja.md) | **繁體中文**

一套在 **Rhino 7 / Rhino 8** 上執行的 .NET 外掛合集：**7 個參數化建模外掛** + 一個玻璃擬態風格的**外掛中心**（安裝器 / 啟動器）。
每個外掛都是「一條指令 + 一個參數面板 + 即時預覽 + 無介面自檢」，**不依賴任何第三方套件**（只用 RhinoCommon / WinForms）。

> 介面與互動遵循倉庫內的《設計一致性規範》：統一的玻璃卡片面板、語意控制項（滑桿 + 數字框連動）、
> 拾取按鈕「選到=綠 / 沒選或選錯=紅」、150/200/300ms 三檔動效、同一套線性圖示家族。

## 下載 / 安裝

| 版本 | 下載 | 說明 |
|---|---|---|
| | **v1.3.0** | [**IVAN-CENTER.exe**](https://github.com/IvanXxxxyyuffff/ivan-rhino-plugins/releases/download/v1.3.0/IVAN-CENTER.exe) | Windows x64 安裝器（3.3 MB，md5 `ddf66a708a37a431e7ba337c1f2dad9f`）
| v1.1.0 | [IVAN-CENTER.exe](https://github.com/IvanXxxxyyuffff/ivan-rhino-plugins/releases/download/v1.1.0/IVAN-CENTER.exe) | 上一版（8 外掛，2.9 MB，md5 `4a7932421e68dfc91d64618c63f9f648`） | |

1. **先關閉 Rhino**，再雙擊執行安裝器 → 自動安裝到 `%LOCALAPPDATA%\IVAN\plugins`（註冊 10 個外掛 + 寫入工具列）
2. 開啟 Rhino：工具列上出現 9 個按鈕，點按鈕開面板即可使用
3. 也可以從原始碼自行建置（見下）

> 安裝包不含 Rhino 本體，需已安裝 Rhino 7 或 Rhino 8。外掛介面目前為**簡體中文**。

## 外掛一覽

| # | 外掛 | 指令 | 功能 |
|---|------|------|------|
| 1 | 表面條紋 **StripeOnSurface** | `StripeOnSurface` | 在曲面/多重曲面上產生條紋；端部可選圓角 / 平齊 / 完全貼合邊界（依邊緣距離內縮、端部隨邊界裁剪）；硬邊可倒可調圓角；寬度、間距、角度、邊緣距離以真實 mm 即時調整 |
| 2 | 煙油容量計算器 **VapeVolume** | `VapeVolume`、`VapeVolumeWatch` | 計算瓶/倉體容量與注入量並即時顯示 |
| 3 | 參數化陣列紋理 **HalftoneDots** | `ParametricTexture` | 6 種陣列（網格/交錯/六邊/同心環/螺旋/抖動）× 4 種形狀（圓/三角/方/六邊）；同心環/螺旋/抖動可點選圓心向外擴散；曲面上圖形不變形、自動鋪滿整張面 |
| 4 | 泰森多邊形紋 **VoronoiTexture** | `VoronoiTexture` | 在平面邊界內產生泰森多邊形（Voronoi）胞元凹凸紋理：中心點、細胞壁壁厚、漸變物件控疏密；輸出「線框 / 網格面」二選一，勾選「一鍵平滑」可輸出**細分曲面（SubD）** |
| 5 | 徑向漸變圓點 **RadialDots** | `RadialDots` | 依半徑漸變的圓點圖案：4 種陣列 × 5 種圖形，尺寸依峰值位置與衰減漸變；重疊圖形自動布林合併 |
| 6 | 網格修復 **MeshFix** | `MeshFix` | 一鍵修復「著色/渲染模式下複雜修剪曲面只剩邊緣線、面體不顯示」：把物件渲染網格的「最大長寬比」由 0 改成 6 並重建；**無面板**，有選取就修選取的、沒選取自動掃描整份檔案 |
| 7 | 鑽石切面 **DiamondFacet** | `DiamondFacet` | 在平面/封閉曲線邊界內產生凹凸鑽石切面：隨機三角剖分 + 頂點隨機高低，可固定邊界；輸出「僅線框 / 面」二選一，面模式可**每個三角切面細分成一張網格片**並細分 |
| 8 | 水波紋 **WaterRipple** | `WaterRipple` | 在曲面 / 多重曲面（當成一整個面）/ 封閉平面曲線邊界上產生水波紋：**有機水波 / 定向條帶 / 同心漣漪**三種波形可切換，波長、波高、波數、主方向、方向散佈、波峰形狀可調；**固定邊界 + 邊界過渡**（寬度 / 平滑度）；輸出網格面，勾「一鍵平滑」轉成**細分曲面（SubD）**（邊界自動打 crease、角不收） |
| 9 | 單一曲面 **SurfaceUnify** | `SurfaceUnify` | 把複雜的多重曲面（或曲面 / 擠出體 / 網格）轉成**一張單一的開放式 NURBS 曲面**：邊界取原裸露邊界並完全逼近，內部按控制點網格**沿基面法向射線貼合**原曲面（控制點數 / 貼合強度 / 平滑度 / 最大貼合距離可調），內孔投影到結果面做修剪保留；面板即時預覽 + 最大 / 平均偏差與邊界偏差報告 |
| 10 | 多邊補面 **PatchFill** | `PatchFill` | 選一圈邊界（N≥2，含曲面邊）把洞補成**一張光滑 NURBS 曲面**：邊界取原裸露邊界並完全逼近，內部按控制點網格貼合；邊界連續性 **G0 / G1 / G2** 三檔、相鄰面導數取樣、內部曲線/點約束、**面積壓力能量項**、**殘差驅動的局部自適應插結**、逐邊縫隙 + 位元遮罩告警；即時預覽 |

每個外掛都附自檢指令（如 `VoronoiSelfTest`、`DiamondFacetSelfTest`），可在無介面下執行完整幾何斷言。

## 倉庫結構

```
implementation/
  MODIFIED_FILE/                  外掛原始碼
    stripe/                       表面條紋外掛 + 外掛中心（center：安裝器 / 啟動器）
    voronoi/  halftone/  radialdots/  meshfix/  diamondfacet/
                                  其餘 5 個外掛（各自 Rhino7 + Rhino8 雙 TFM 專案）
    src/VapeVolume/               煙油容量計算器
    shared/PanelTheme.cs          全部面板共用的自繪控制項 + 動效 + 圖示算繪（各外掛內為逐位元組一致的副本）
    iconmake/                     圖示算繪器（整套線性圖示由程式碼產生，不用點陣素材）
    PROJECT-STATE.md              專案狀態 / 逐輪變更 / 踩過的坑（真相來源）
    DESIGN-CONSISTENCY.md         設計一致性規範（新增外掛必讀）
    UI-REFACTOR-HANDOFF.md        介面重構交接說明
  *.ps1 / *.py                    建置、安裝、自檢、稽核的一鍵腳本
index.html / app.js / styles.css / icons/ …   UI 原型預覽（HTML/CSS/JS 玻璃擬態介面 + 截圖）
```

## 建置

需求：Windows + .NET SDK 7/8 + Rhino 7 或 8（RhinoCommon 走 NuGet，未安裝 Rhino 也能編譯）。

```bash
# 單一外掛（雙 TFM：Rhino 7 用 net48，Rhino 8 用 net7.0-windows）
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino8/VoronoiTexture.csproj -c Release
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino7/VoronoiTexture.csproj -c Release

# 全部外掛 + 外掛中心
python -X utf8 implementation/native-gate.py build-panels
```

產物落在各外掛的 `out/rhino7|rhino8/`（`.rhp` 可直接拖進 Rhino，或用外掛中心安裝）。

## 安裝 / 自檢

```bash
pwsh -File implementation/refresh-payload-and-center.ps1   # 彙整 payload + 編譯外掛中心 exe
pwsh -File implementation/install-and-verify.ps1           # 靜默安裝 + 斷言（登錄檔 / 工具列 / payload 逐位元組一致）
pwsh -File implementation/run-native-selftests.ps1         # 10 個外掛自檢一輪跑完（預設隱藏視窗，不彈窗）
pwsh -File implementation/run-native-selftests.ps1 -Only Voronoi   # 單一外掛
```

外掛安裝位置：`%LOCALAPPDATA%\IVAN\plugins\<外掛>\rh8\<外掛>.rhp`（Rhino 8）/ `rh7\`（Rhino 7）。

## 文件

- [`PROJECT-STATE.md`](implementation/MODIFIED_FILE/PROJECT-STATE.md) — 專案狀態、每個外掛的能力口徑、逐輪變更與**踩過的坑**（幾何 API、RhinoCommon 陷阱、驗證流程）
- [`DESIGN-CONSISTENCY.md`](implementation/MODIFIED_FILE/DESIGN-CONSISTENCY.md) — 像素級版面、語意控制項、拾取按鈕紅綠口徑、圖示家族、自檢格式
- [`UI-REFACTOR-HANDOFF.md`](implementation/MODIFIED_FILE/UI-REFACTOR-HANDOFF.md) — 介面重構交接

## 授權

MIT，見 [LICENSE](LICENSE)。
