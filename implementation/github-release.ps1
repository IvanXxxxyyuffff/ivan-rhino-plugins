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
v1.4.3 — 修复「Rhino 一启动就被关」隐患，旗标协议全面加固：
- **根因**：无人值守自检/探针旗标残留时，任何新启动的 Rhino 加载插件后会执行旗标任务并自动退出（RhinoApp.Exit）——表现为「一启动就被关」
- **修复**：①旗标改为**原子领取 + 消费即删**（即使任务没跑完，残留也不再影响后续启动）②只有以 `/nosplash` 启动的无人值守实例才消费旗标——**普通启动的 Rhino 完全免疫**
- **实测**：正常实例面对残留旗标无任何动作（旗标保留、无报告、不退出）；无人值守链路正常（PatternEmboss 自检 42/0 PASS）
- 另含 v1.4.2 内容：PatternEmboss「一键平滑」= QuadRemesh 重构 → SubD，参数面板可调

## English
v1.4.3 — fixes the "Rhino closes right after launch" hazard and hardens the flag protocol:
- **Root cause**: a stale unattended self-test/probe flag made any newly launched Rhino run the flag task on plugin load and then quit itself (RhinoApp.Exit).
- **Fix**: (1) flags are now **atomically claimed and deleted on consumption** (leftovers can no longer affect future launches); (2) only unattended instances started with `/nosplash` consume flags — **normally launched Rhino is fully immune**.
- **Verified**: a normal instance ignores leftover flags (flag stays, no action, no exit); the unattended path still works (PatternEmboss self-test 42/0 PASS).

## 日本語
v1.4.3 — 「起動直後に Rhino が閉じる」問題の修正とフラグプロトコル強化：
- **原因**：無人自検/プローブフラグが残っていると、新しく起動した Rhino がプラグイン読み込み時にフラグ処理を実行し自ら終了（RhinoApp.Exit）
- **修正**：①フラグは**アトミック取得 + 消費後即削除** ②`/nosplash` 起動の無人インスタンスのみ消費 —— **通常起動の Rhino は完全に無影響**
- **実測**：通常インスタンスは残存フラグを無視（フラグ保持・無動作・終了しない）／無人パスは正常（自検 42/0 PASS）

## 繁體中文
v1.4.3 — 修復「Rhino 一啟動就被關」隱患，旗標協議全面加固：
- **根因**：無人值守自檢/探針旗標殘留時，任何新啟動的 Rhino 載入外掛後會執行旗標任務並自動退出（RhinoApp.Exit）
- **修復**：①旗標改為**原子領取 + 消費即刪**②僅以 `/nosplash` 啟動的無人值守實例才消費旗標——**普通啟動的 Rhino 完全免疫**
- **實測**：正常實例面對殘留旗標無任何動作；無人值守鏈路正常（自檢 42/0 PASS）
'@

$body = @{ tag_name = 'v1.4.3'; target_commitish = 'main'; name = 'v1.4.3 — 修复「启动即被关」：旗标原子领取+即删，普通实例完全免疫（含 v1.4.2 QuadRemesh 一键平滑）'; body = $notes; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 5
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
