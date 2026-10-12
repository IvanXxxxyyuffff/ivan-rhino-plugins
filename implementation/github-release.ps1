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
PatternEmboss（图案压花）「一键平滑」升级为 **QuadRemesh 重构**：
- 勾选「启用一键平滑」后：凹凸网格先经 **QuadRemesh** 四边形重构，再转 **SubD 细分曲面**输出
- 新增「一键平滑输出（QuadRemesh → SubD）」参数卡：**目标四边形数 / 目标边长 / 自适应尺寸 / 对称轴 / 检测硬边**全部可调，预览与生成共用（改参数即时重算）
- 修复：QuadRemesh 需要输入网格已计算顶点法向（否则静默失败），已自动补齐；失败时按「QuadRemesh → 直接 SubD → 网格」逐级回退

PatternEmboss 自检 42/0 PASS（新增用例9：重构成功 / 面数随目标接线 / 对称轴 / 自适应 / 重构结果转 SubD）。

## English
PatternEmboss "one-click smooth" now runs a **QuadRemesh rebuild**:
- When "Enable one-click smooth" is checked, the embossed mesh is first rebuilt as quads with **QuadRemesh**, then converted to a **SubD** subdivision surface.
- New "One-click smooth output (QuadRemesh → SubD)" card: **target quad count / target edge length / adaptive size / symmetry axis / detect hard edges**, all adjustable; live preview and output share the same parameters.
- Fix: QuadRemesh requires computed vertex normals on the input mesh (it fails silently otherwise); this is now handled automatically, with staged fallback (QuadRemesh → plain SubD → mesh).

PatternEmboss self-test 42/0 PASS.

## 日本語
PatternEmboss の「ワンクリックスムーズ」が **QuadRemesh 再構築**に：
- 「ワンクリックスムーズを有効化」で、凹凸メッシュを **QuadRemesh** で四角形に再構築してから **SubD** に変換
- 新カード「ワンクリックスムーズ出力（QuadRemesh → SubD）」：**ターゲット四角形数 / 目標エッジ長 / 適応サイズ / 対称軸 / ハードエッジ検出**を調整可能
- 修正：QuadRemesh は入力メッシュの頂点法線が必要（ないと無言で失敗）。自動補正 + 段階フォールバック（QuadRemesh → SubD → メッシュ）

PatternEmboss 自検 42/0 PASS。

## 繁體中文
PatternEmboss（圖案壓花）「一鍵平滑」升級為 **QuadRemesh 重構**：
- 勾選「啟用一鍵平滑」後：凹凸網格先經 **QuadRemesh** 四邊形重構，再轉 **SubD 細分曲面**輸出
- 新增「一鍵平滑輸出（QuadRemesh → SubD）」參數卡：**目標四邊形數 / 目標邊長 / 自適應尺寸 / 對稱軸 / 檢測硬邊**全部可調
- 修復：QuadRemesh 需要輸入網格已計算頂點法向（否則靜默失敗），已自動補齊；失敗時逐級回退（QuadRemesh → 直接 SubD → 網格）

PatternEmboss 自檢 42/0 PASS。
'@

$body = @{ tag_name = 'v1.4.2'; target_commitish = 'main'; name = 'v1.4.2 — PatternEmboss：一键平滑 = QuadRemesh 重构 → SubD（参数面板可调）'; body = $notes; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 5
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
