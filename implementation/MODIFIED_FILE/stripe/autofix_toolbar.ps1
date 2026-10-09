# 自动修复工具栏：等 Rhino 全部退出后，把工具条按 XNurbs 的同款格式写进 Rhino 工作区
$ErrorActionPreference = 'Continue'
$log = 'C:\zcode_build\stripe\_toolbar_fix.log'
"start $(Get-Date -Format s)" | Out-File -Encoding utf8 $log

function W($s) { $s | Out-File -Encoding utf8 -Append $log }

# 1) 等 Rhino 退出
$waited = 0
while ((Get-Process Rhino -ErrorAction SilentlyContinue) -and $waited -lt 3600) {
    Start-Sleep -Seconds 5
    $waited += 5
}
if (Get-Process Rhino -ErrorAction SilentlyContinue) { W "timeout: Rhino 一直在运行"; exit 2 }
W "Rhino 已关闭，开始修复（等待 $waited 秒）"

# 2) 取 rui 里的 guid
$rui = Join-Path $env:LOCALAPPDATA 'StripeOnSurface\StripeOnSurface.rui'
if (-not (Test-Path $rui)) { $rui = 'D:\UserData\Desktop\StripeOnSurface\StripeOnSurface.rui' }
W "rui = $rui"
$txt = Get-Content -Raw -Encoding UTF8 $rui
$grp = ([regex]::Match($txt, '<tool_bar_group guid="([^"]+)"')).Groups[1].Value
$tb  = ([regex]::Match($txt, '<tool_bar guid="([^"]+)"')).Groups[1].Value
$fileGuid = 'a7f3c1e2-4b58-4d6a-9c02-1e5b7d9a3f40'
$dockGuid = 'c4d2e8f1-6a37-4b95-8e10-2f7c9a4b6d13'
W "group=$grp toolbar=$tb"

if (-not $grp -or -not $tb) { W "无法从 rui 读取 guid"; exit 3 }

$block = '<dock_bar guid="' + $dockGuid + '" source_group_file="' + $fileGuid + '" source_group="' + $grp + '">' +
         '<placement dock_location="Floating" recent_dock_location="Left" float_point="180,180" float_size="90,52" />' +
         '<tabs name="表面条纹" selected_item="' + $tb + '" display_style="BitmapAndText">' +
         '<name><locale_2052>表面条纹</locale_2052></name>' +
         '<tool_bar guid="' + $tb + '" file="' + $fileGuid + '" side_bar_file="' + $fileGuid + '" />' +
         '</tabs></dock_bar>'

$fileEntry = '<file_name guid="' + $fileGuid + '" plug_in_guid="b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91" source="PlugInFolder">' + $rui + '</file_name>'

$patched = 0
foreach ($ver in @('8.0', '7.0')) {
    $setDir = Join-Path $env:APPDATA "McNeel\Rhinoceros\$ver\settings"
    if (-not (Test-Path $setDir)) { continue }
    Get-ChildItem -Path $setDir -Recurse -Filter *.xml -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'workspaces' -or $_.Name -eq 'containers.xml' } |
        ForEach-Object {
            $p = $_.FullName
            $s = Get-Content -Raw -Encoding UTF8 $p
            $orig = $s

            # 去掉旧的（可能格式不对的）dock_bar 块
            $s = [regex]::Replace($s, '<dock_bar [^>]*source_group_file="' + $fileGuid + '"[^>]*>.*?</dock_bar>', '', 'Singleline')

            if ($_.FullName -match 'workspaces') {
                # 文件登记
                if ($s -notmatch [regex]::Escape($fileGuid)) {
                    $s = [regex]::Replace($s, '(<files>)', ('$1' + $fileEntry))
                } else {
                    # 已登记但路径可能过期：统一替换为本地路径
                    $s = [regex]::Replace($s, '<file_name[^>]*' + $fileGuid + '[^>]*>[^<]*</file_name>', $fileEntry)
                }
                # 插入正确的 dock_bar
                $s = [regex]::Replace($s, '(<dock_bars[^>]*>)', ('$1' + $block))
            } else {
                if ($s -notmatch [regex]::Escape($fileGuid)) {
                    $s = [regex]::Replace($s, '(<plug_in_files>)', ('$1' + $fileEntry))
                }
            }

            if ($s -ne $orig) {
                Copy-Item $p "$p.bak2" -Force -ErrorAction SilentlyContinue
                [System.IO.File]::WriteAllText($p, $s, (New-Object System.Text.UTF8Encoding $true))
                W "已写入 $p"
                $patched++
            }
        }
}
W "patched=$patched"
"done $(Get-Date -Format s) patched=$patched" | Out-File -Encoding utf8 'C:\zcode_build\stripe\_toolbar_fix.done'
