$ErrorActionPreference = 'Stop'
$repo = 'IvanXxxxyyuffff/ivan-rhino-plugins'
$proxy = 'http://127.0.0.1:7897'
$exe = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\out\center\IVAN-CENTER.exe'
$helper = '!"C:/Program Files/Git/mingw64/bin/git-credential-manager.exe"'
$tmp = Join-Path $env:TEMP 'ivan-rel'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$cred = ("protocol=https`nhost=github.com`n`n" | git -c credential.helper= -c "credential.helper=$helper" credential fill)
$token = (($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', '')
if (-not $token) { throw '拿不到 PAT' }
Write-Output ("PAT 长度 {0}" -f $token.Length)

$hdr = @('-H', "Authorization: Bearer $token", '-H', 'Accept: application/vnd.github+json',
         '-H', 'X-GitHub-Api-Version: 2022-11-28', '-H', 'User-Agent: IVAN-RELEASE', '-x', $proxy,
         '--ssl-no-revoke', '-sS')

$notes = @'
## 简体中文
第 9 个插件 **多重曲面转单一曲面（SurfaceUnify）**：把复杂的多重曲面 / 曲面 / 挤出体 / 网格转成**一张单一的开放式 NURBS 曲面** —— 边界取原裸露边界并完全逼近，内部沿基面法向**射线贴合**原曲面（凹袋 / 兜形壳体也能贴住，不会扣一个盖子），内孔投影到结果面做修剪保留；面板实时预览并给出最大 / 平均 / 边界偏差。命令：`SurfaceUnify`。

同时修复 / 改进：
- 水波纹「同心涟漪」由椭圆改回**正圆**（主方向 = 涟漪源偏移方向，0° = 源居中）
- 第 9 个插件图标重做（带 S 形曲边的整片曲面 + iso 肋线），与其余 8 个同族
- 9 插件全量自检 ALL-PASS（Voronoi 150 / Stripe 55 / Halftone 23 / RadialDots 37+1 / MeshFix 14 / DiamondFacet 86 / WaterRipple 101 / SurfaceUnify 114 / VapeVolume 3）

## English
New plugin **SurfaceUnify — polysurface to single surface**: converts a complex polysurface / surface / extrusion / mesh into **one single open NURBS surface**. The boundary follows the original naked edges exactly, and the interior is fitted by ray-casting along the base-surface normal (works for pouch / bowl shells — no "lid" artefacts). Inner holes are projected and trimmed. Live preview with max / RMS / boundary deviation. Command: `SurfaceUnify`.

Also fixed / improved: concentric ripples are now perfect circles (main direction = ripple source offset, 0° = centred); redesigned icon for the new plugin; full self-test suite ALL-PASS for all 9 plugins.

## 日本語
新プラグイン **SurfaceUnify（ポリサーフェス → 単一サーフェス）**：複雑なポリサーフェス / サーフェス / 押し出し / メッシュを**1 枚の開いた NURBS サーフェス**に変換します。境界は元の裸エッジに完全追従し、内部はベース面法線に沿った**レイキャストでフィット**（ポーチ / ボウル形状でも「蓋」になりません）。内側の穴は投影してトリム。最大 / 平均 / 境界偏差を表示。コマンド：`SurfaceUnify`。同心円リップルは正円に修正、アイコンも作り直しました。

## 繁體中文
第 9 個外掛 **多重曲面轉單一曲面（SurfaceUnify）**：把複雜的多重曲面 / 曲面 / 擠出體 / 網格轉成**一張單一的開放式 NURBS 曲面** —— 邊界取原裸露邊界並完全逼近，內部沿基面法向**射線貼合**原曲面（凹袋 / 兜形殼體也不會變成扣一個蓋子），內孔投影到結果面做修剪保留；面板即時預覽並給出最大 / 平均 / 邊界偏差。指令：`SurfaceUnify`。另修正水波紋「同心漣漪」為正圓、重做第 9 個外掛圖示。
'@
$bodyObj = @{ tag_name = 'v1.3.0'; target_commitish = 'main'; name = 'v1.3.0 — 第 10 个插件「多边补面 PatchFill」+ SurfaceUnify 交点精确 + G2 曲率连续'; body = $notes; draft = $false; prerelease = $false }
$bodyFile = Join-Path $tmp 'release.json'
[IO.File]::WriteAllText($bodyFile, ($bodyObj | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))

$resp = Join-Path $tmp 'resp.json'
curl.exe @hdr -X POST "https://api.github.com/repos/$repo/releases" --data-binary "@$bodyFile" -o $resp
$rel = [IO.File]::ReadAllText($resp, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
if (-not $rel.id) { Write-Output ([IO.File]::ReadAllText($resp)); throw '建 release 失败' }
Write-Output ("release id={0} tag={1} url={2}" -f $rel.id, $rel.tag_name, $rel.html_url)

$asset = Join-Path $tmp 'asset.json'
$upUrl = "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets"
curl.exe @hdr -X POST --url "$upUrl`?name=IVAN-CENTER.exe" -H 'Content-Type: application/octet-stream' --data-binary "@$exe" -o $asset
$a = [IO.File]::ReadAllText($asset, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
if (-not $a.browser_download_url) { Write-Output ([IO.File]::ReadAllText($asset)); throw '传附件失败' }
Write-Output ("asset {0} bytes state={1}" -f $a.size, $a.state)
Write-Output ("下载地址：{0}" -f $a.browser_download_url)
Write-Output ("本地 exe：{0} bytes  md5 {1}" -f (Get-Item $exe).Length, (Get-FileHash -LiteralPath $exe -Algorithm MD5).Hash.ToLower())
