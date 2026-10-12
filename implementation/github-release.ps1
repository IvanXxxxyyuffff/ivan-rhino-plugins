$ErrorActionPreference = 'Stop'
$repo = 'IvanXxxxyyuffff/ivan-rhino-plugins'
$proxy = 'http://127.0.0.1:7897'
$exe = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\out\center\IVAN-CENTER.exe'
$helper = '!"C:/Program Files/Git/mingw64/bin/git-credential-manager.exe"'
$tmp = Join-Path $env:TEMP 'ivan-rel2'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$cred = ("protocol=https`nhost=github.com`n`n" | git -c credential.helper= -c "credential.helper=$helper" credential fill)
$token = (($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', '')
if (-not $token) { throw 'no PAT' }
$H = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'IVAN-RELEASE' }

$notes = @'
## 简体中文
新增第 10 个插件 **图案压花（PatternEmboss）**：在曲面 / 多重曲面上生成**六边形（蜂窝）/ 四边形 / 菱形凹凸纹理** —— 单元尺寸、高度（正凸负凹）、槽宽、圆润度、边缘留边、采样密度、**加厚成壳**全部可调；沿面法向偏移建网格，实时预览。命令：`PatternEmboss`。

10 插件全量自检 ALL-PASS（Voronoi 150 / Stripe 55 / Halftone 23 / RadialDots 37 / MeshFix 14 / DiamondFacet 86 / WaterRipple 101 / SurfaceUnify 130+9* / PatternEmboss 26 / VapeVolume 3；* 其中一项为「用户实测文件」用例，文件缺失时按设计跳过）。

## English
New 10th plugin **PatternEmboss**: emboss **hexagon (honeycomb) / square / diamond patterns** on surfaces and polysurfaces. Cell size, height (positive = raised, negative = sunken), groove width, roundness, edge margin, sampling density and shell thickness are all adjustable; the mesh is offset along surface normals with live preview. Command: `PatternEmboss`.

Full self-test suite ALL-PASS for all 10 plugins.

## 日本語
新規 10 番目のプラグイン **PatternEmboss**：サーフェス/ポリサーフェスに**六角形（ハニカム）/ 四角形 / 菱形の凹凸テクスチャ**を生成。セルサイズ、高さ（正=凸・負=凹）、溝幅、丸み、エッジ余白、サンプリング密度、シェル厚さを調整可能。コマンド：`PatternEmboss`。

10 プラグイン全自検 ALL-PASS。

## 繁體中文
新增第 10 個外掛 **圖案壓花（PatternEmboss）**：在曲面 / 多重曲面上生成**六邊形（蜂窩）/ 四邊形 / 菱形凹凸紋理** —— 單元尺寸、高度（正凸負凹）、槽寬、圓潤度、邊緣留邊、採樣密度、**加厚成殼**全部可調；沿面法向偏移建網格，即時預覽。指令：`PatternEmboss`。

10 外掛全量自檢 ALL-PASS。
'@

$body = @{ tag_name = 'v1.4.0'; target_commitish = 'main'; name = 'v1.4.0 — 第 10 插件「图案压花 PatternEmboss」（曲面六边形/四边形/菱形凹凸纹理）'; body = $notes; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 5
$rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" -Headers $H -Proxy $proxy -TimeoutSec 60 -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType 'application/json'
Write-Output ("release id={0} tag={1}" -f $rel.id, $rel.tag_name)
if (-not $rel.id) { throw 'create failed' }

$up = "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=IVAN-CENTER.exe"
$a = Invoke-RestMethod -Method Post -Uri $up -Headers $H -Proxy $proxy -TimeoutSec 600 -InFile $exe -ContentType 'application/octet-stream'
Write-Output ("asset {0} bytes state={1}" -f $a.size, $a.state)
Write-Output ("url {0}" -f $a.browser_download_url)

# 回下载核对 md5
$dl = Join-Path $tmp 'IVAN-CENTER.exe'
Invoke-WebRequest -Uri $a.browser_download_url -OutFile $dl -Proxy $proxy -TimeoutSec 600
$md5 = (Get-FileHash -LiteralPath $dl -Algorithm MD5).Hash.ToLower()
$local = (Get-FileHash -LiteralPath $exe -Algorithm MD5).Hash.ToLower()
Write-Output ("local md5 {0}" -f $local)
Write-Output ("downloaded md5 {0}  match={1}" -f $md5, ($md5 -eq $local))
