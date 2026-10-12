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
PatternEmboss（图案压花）两处改进：
- 「选择曲面」按钮改成**红/绿状态胶囊**（未选择 = 红，已选择 = 绿），与其它插件拾取按钮口径一致
- 输出新增「**SubD 平滑输出（细分曲面，一键平滑）**」勾选：生成的凹凸纹理由 Catmull-Clark 转换成 SubD 细分曲面对象（一键平滑）；不勾选仍输出网格

10 插件全量自检 ALL-PASS（PatternEmboss 32/0）。

## English
Two improvements to PatternEmboss:
- The "Select surface" button now uses the **red/green state pill** (red = nothing selected, green = selected), consistent with the pick buttons in the other plugins.
- New output option "**SubD smooth output (subdivision surface, one-click smoothing)**": the generated embossed mesh is converted to a Catmull-Clark SubD object; leave unchecked to output a mesh.

Full self-test suite ALL-PASS for all 10 plugins (PatternEmboss 32/0).

## 日本語
PatternEmboss の 2 点を改善：
- 「サーフェスを選択」ボタンを**赤/緑の状態ピル**に（未選択=赤、選択済み=緑、他プラグインと同口径）
- 出力に「**SubD スムース出力（サブディビジョン、ワンクリック平滑）**」を追加（Catmull-Clark で SubD に変換）

全文プラグイン全自検 ALL-PASS。

## 繁體中文
PatternEmboss（圖案壓花）兩處改進：
- 「選擇曲面」按鈕改為**紅/綠狀態膠囊**（未選 = 紅、已選 = 綠），與其它外掛拾取按鈕同口徑
- 輸出新增「**SubD 平滑輸出（細分曲面，一鍵平滑）**」勾選：凹凸網格轉 Catmull-Clark SubD 物件；不勾選仍輸出網格

10 外掛全量自檢 ALL-PASS（PatternEmboss 32/0）。
'@

$body = @{ tag_name = 'v1.4.1'; target_commitish = 'main'; name = 'v1.4.1 — PatternEmboss：拾取按钮红/绿双态 + SubD 平滑输出（一键平滑）'; body = $notes; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 5
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
