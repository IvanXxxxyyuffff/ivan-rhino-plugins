$ErrorActionPreference = 'Stop'
$repo = 'IvanXxxxyyuffff/ivan-rhino-plugins'
$proxy = 'http://127.0.0.1:7897'
$exe = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\out\center\IVAN-CENTER.exe'
$tag = (Get-Content -LiteralPath 'E:\IVAN-LiquidGlass-preview\implementation\CURRENT-VERSION.txt' -Raw).Trim()
$helper = '!"C:/Program Files/Git/mingw64/bin/git-credential-manager.exe"'
$tmp = Join-Path $env:TEMP 'ivan-rel2'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$cred = ("protocol=https`nhost=github.com`n`n" | git -c credential.helper= -c "credential.helper=$helper" credential fill)
$token = (($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', '')
if (-not $token) { throw 'no PAT' }
$H = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'IVAN-RELEASE' }

$notes = @'
## 简体中文
v1.4.6 — **安装器内置更新检查**：检测到 GitHub 新版本自动弹出更新提醒
- 打开插件中心即后台检查 GitHub 最新 Release（约 6 秒超时；无网络/不可达时静默跳过，不打扰安装）
- 发现新版本：主界面右上角出现「**发现新版本 vX.Y.Z**」横幅（一键「前往下载」）+ 弹出提醒对话框
- 版本对比：当前版本随构建注入（`CURRENT-VERSION.txt` 为唯一版本源），语义化 vX.Y.Z 比较
- 静默安装（/silent）与 UI 冒烟（/uitest）模式不执行检查

## English
v1.4.6 — **built-in update check in the Plugin Center**: detects new GitHub releases and pops an update reminder
- On launch the center checks the latest GitHub release in the background (6 s timeout; silently skipped when offline or unreachable).
- When a new version exists: a "**vX.Y.Z available**" banner appears at the top-right (one-click "Download") plus a reminder dialog.
- Version comparison uses the build-injected current version (`CURRENT-VERSION.txt` is the single source of truth), semver vX.Y.Z.
- Skipped in /silent installs and /uitest smoke runs.

## 日本語
v1.4.6 — **プラグインセンターに更新チェックを内蔵**：GitHub の新バージョンを検出して更新通知を表示
- 起動時にバックグラウンドで最新 Release を確認（タイムアウト約 6 秒、オフライン時は静かにスキップ）
- 新バージョン検出時：右上に「**vX.Y.Z が見つかりました**」バナー（ワンクリックでダウンロード）+ 通知ダイアログ
- バージョン比較はビルド時に注入された現在バージョン（`CURRENT-VERSION.txt` が唯一のソース）
- /silent インストールと /uitest では実行しません

## 繁體中文
v1.4.6 — **安裝器內建更新檢查**：偵測到 GitHub 新版本自動彈出更新提醒
- 開啟外掛中心即在背景檢查 GitHub 最新 Release（約 6 秒逾時；無網路／不可達時靜默跳過）
- 發現新版本：主介面右上角出現「**發現新版本 vX.Y.Z**」橫幅（一鍵「前往下載」）+ 彈出提醒對話框
- 版本比對：目前版本隨建置注入（`CURRENT-VERSION.txt` 為唯一版本源），語意化 vX.Y.Z 比對
- 靜默安裝（/silent）與 UI 冒煙（/uitest）模式不執行檢查
'@

$body = @{ tag_name = $tag; target_commitish = 'main'; name = 'v1.4.6 — 安装器内置更新检查：GitHub 新版检测 + 弹出更新提醒'; body = $notes; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 5
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
