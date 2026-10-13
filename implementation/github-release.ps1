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
v1.4.5 — 两个实测问题修复：**Rhino 7 兼容** + **图案化拓扑重写**
- **Rhino 7 无法加载修复**：上一版把 Rhino7 版构建的 RhinoCommon 引用误升到 7.38，较旧的 Rhino 7 报「该插件与这个版本的 Rhino 不兼容」；现已回退到与其余 9 个插件一致的最老基线 SDK（7.0.20314），TargetEdgeLength 改由反射按需设置——**任何 Rhino 7.x 都可正常加载**
- **图案化拓扑重写（一键平滑更干净）**：网格改为「每个纹理单元 = 同心环四边面片、槽缝 = 共享边环」——拓扑沿图案、无锯齿；勾「一键平滑」**直接输出细分曲面（SubD）**（不再经 QuadRemesh 重拓扑；QuadRemesh 会打乱拓扑且对本插件图案网格存在长时间不返回风险，已从产品路径移除，仅保留内部诊断）
- 修复：图幅边界外的图案曾被误收进网格（表面积虚增）；边缘渐隐分辨率加倍；自检每用例刷盘（中途可见进度）
- 自检 47/0 PASS；10 插件全量回归 ALL-PASS

## English
v1.4.5 — two field fixes: **Rhino 7 compatibility** + **pattern-aligned topology rewrite**
- **Rhino 7 load failure fixed**: the previous build accidentally bumped the Rhino7 assembly's RhinoCommon reference to 7.38, so older Rhino 7 installs refused it as incompatible. Now back on the oldest baseline SDK (7.0.20314, same as the other 9 plugins) with TargetEdgeLength set via reflection — **loads on any Rhino 7.x**.
- **Pattern-aligned topology**: each embossed cell is now built as concentric quad rings with grooves shared as edge loops — the topology follows the pattern with no stair-stepping. "One-click smooth" outputs a **SubD directly** (no QuadRemesh pass; QuadRemesh reorders topology and can run indefinitely on this plugin's pattern meshes, so it was removed from the product path and kept only as an internal diagnostic).
- Also fixed: cells outside the surface boundary were wrongly included (inflated area); edge-fade grid resolution doubled; self-test now flushes per case.
- Self-test 47/0 PASS; full 10-plugin regression ALL-PASS.

## 日本語
v1.4.5 — 実測フィードバック 2 件の修正：**Rhino 7 互換** + **パターン整列トポロジーへの書き換え**
- **Rhino 7 読み込み不可の修正**：前版で Rhino7 版の RhinoCommon 参照が 7.38 に上がっており、古い Rhino 7 で「互換性がない」と拒否されていました。最古ベースライン SDK（7.0.20314、他 9 プラグインと同一）に戻し、TargetEdgeLength はリフレクションで設定 — **どの Rhino 7.x でも読み込めます**。
- **パターン整列トポロジー**：各セルを同心四角形リングで生成し、溝は共有エッジループに — トポロジーがパターンに沿い、ジャギーなし。「ワンクリックスムーズ」は **SubD を直接出力**（QuadRemesh はトポロジーを崩し、本プラグインのパターンメッシュでは長時間応答しないことがあるため製品経路から削除、内部診断のみ）。
- その他：パターンがサーフェス境界外に誤って含まれる問題（面積の水増し）を修正、減衰グリッドの解像度を倍化。
- 自検 47/0 PASS；全 10 プラグイン回帰 ALL-PASS。

## 繁體中文
v1.4.5 — 兩項實測問題修復：**Rhino 7 相容** + **圖案化拓撲重寫**
- **Rhino 7 無法載入修復**：上一版把 Rhino7 版建置的 RhinoCommon 引用誤升至 7.38，較舊的 Rhino 7 報「與這個版本的 Rhino 不相容」；現已回退到與其餘 9 個外掛一致的最老基線 SDK（7.0.20314），TargetEdgeLength 改以反射按需設定——**任何 Rhino 7.x 均可正常載入**
- **圖案化拓撲重寫**：每個紋理單元改為「同心環四邊面片、槽縫 = 共享邊環」——拓撲沿圖案、無鋸齒；勾「一鍵平滑」**直接輸出細分曲面（SubD）**（不再經 QuadRemesh 重構；其會打亂拓撲且對本外掛的圖案網格有長時間不返回風險，已從產品路徑移除，僅保留內部診斷）
- 另修：圖幅邊界外的圖案曾被誤收進網格（表面積虛增）；邊緣漸隱解析度加倍；自檢每用例刷盤
- 自檢 47/0 PASS；10 外掛全量回歸 ALL-PASS
'@

$body = @{ tag_name = 'v1.4.5'; target_commitish = 'main'; name = 'v1.4.5 — Rhino 7 兼容修复（回退基线 SDK）+ 图案化拓扑重写（一键平滑直出 SubD）'; body = $notes; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 5
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
