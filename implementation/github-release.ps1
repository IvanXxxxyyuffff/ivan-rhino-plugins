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
**本版移除第 10 个插件「多边补面 PatchFill」**（经实测评估后按用户决定下线），项目回到 **9 个插件**：表面条纹 / 烟油容量 / 参数化阵列 / 泰森多边形 / 径向渐变圆点 / 网格修复 / 钻石切面 / 水波纹 / 多重曲面转单一曲面。安装器会自动装好 9 个插件并写入工具条；此前装过 10 插件版的环境重装本版即可。

9 插件全量自检 ALL-PASS（Voronoi 150 / Stripe 55 / Halftone 23 / RadialDots 37+1 / MeshFix 14 / DiamondFacet 86 / WaterRipple 101 / SurfaceUnify 135 / VapeVolume 3）。

## English
**This release removes the 10th plugin "PatchFill"** (retired after hands-on evaluation), bringing the bundle back to **9 plugins**: Surface Stripes / Vape Volume / Parametric Array / Voronoi / Radial Dots / Mesh Fix / Diamond Facet / Water Ripple / SurfaceUnify. The installer sets up all 9 plugins and writes the toolbar; just re-run it over an older 10-plugin install.

Full self-test suite ALL-PASS for all 9 plugins.

## 日本語
**本リリースで 10 番目のプラグイン「PatchFill」を削除**しました（実機評価の結果、ユーザー判断で終了）。バンドルは **9 プラグイン**に戻ります：サーフェスストライプ / ベイプ容量 / パラメトリック配列 / ボロノイ / ラジアルドット / メッシュ修復 / ダイヤモンドファセット / 水面リップル / SurfaceUnify。インストーラーは 9 プラグインを導入しツールバーを書き込みます。

9 プラグイン全自検 ALL-PASS。

## 繁體中文
**本版移除第 10 個外掛「多邊補面 PatchFill」**（實測評估後依使用者決定下線），專案回到 **9 個外掛**：表面條紋 / 煙油容量 / 參數化陣列 / 泰森多邊形 / 徑向漸變圓點 / 網格修復 / 鑽石切面 / 水波紋 / 多重曲面轉單一曲面。安裝器會裝好 9 個外掛並寫入工具列；裝過 10 外掛版的環境重裝本版即可。

9 外掛全量自檢 ALL-PASS。
'@
$bodyObj = @{ tag_name = 'v1.3.2'; target_commitish = 'main'; name = 'v1.3.2 — 移除第 10 插件（多边补面），回到 9 插件版'; body = $notes; draft = $false; prerelease = $false }
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
