# 清理旧的 StripeOnSurface.rui 登记（它的 plug_in_guid 误用了插件 GUID，导致 Rhino 重复加载插件）
$ErrorActionPreference = 'Continue'
$log = 'C:\zcode_build\stripe\_cleanup.log'
"start $(Get-Date -Format s)" | Out-File -Encoding utf8 $log
function W($s) { $s | Out-File -Encoding utf8 -Append $log }

$waited = 0
while ((Get-Process Rhino -ErrorAction SilentlyContinue) -and $waited -lt 3600) { Start-Sleep -Seconds 5; $waited += 5 }
if (Get-Process Rhino -ErrorAction SilentlyContinue) { W "timeout"; exit 2 }
W "Rhino 已关闭（$waited 秒），开始清理"

$oldFileGuid = 'a7f3c1e2-4b58-4d6a-9c02-1e5b7d9a3f40'
$oldDockGuid = 'c4d2e8f1-6a37-4b95-8e10-2f7c9a4b6d13'

foreach ($ver in @('7.0', '8.0')) {
    # 1) 删除旧 rui
    $rui = Join-Path $env:APPDATA ("McNeel\Rhinoceros\$ver\UI\Plug-ins\StripeOnSurface.rui")
    if (Test-Path $rui) { Remove-Item $rui -Force; W "已删除 $rui" }

    # 2) 清掉工作区里的旧登记
    $base = Join-Path $env:APPDATA "McNeel\Rhinoceros\$ver\settings"
    if (-not (Test-Path $base)) { continue }
    Get-ChildItem -Path $base -Recurse -Filter *.xml -ErrorAction SilentlyContinue | ForEach-Object {
        $p = $_.FullName
        $s = Get-Content -Raw -Encoding UTF8 $p
        if ($s -notmatch $oldFileGuid -and $s -notmatch $oldDockGuid) { return }
        $n = [regex]::Replace($s, '<dock_bar [^>]*source_group_file="' + $oldDockGuid + '"[^>]*>.*?</dock_bar>', '', 'Singleline')
        $n = [regex]::Replace($n, '<file_name[^>]*' + $oldFileGuid + '[^>]*>[^<]*</file_name>', '')
        if ($n -ne $s) {
            $bak = "$p.cleanbak"
            if (-not (Test-Path $bak)) { Copy-Item $p $bak -Force }
            [System.IO.File]::WriteAllText($p, $n, (New-Object System.Text.UTF8Encoding $true))
            W "已清理 $p"
        }
    }
}

# 3) 重新跑一次 IVAN CENTER 静默安装，确保 IVAN CENTER.rui 登记正确
$exe = 'D:\UserData\Desktop\IVAN插件中心\IVAN-CENTER.exe'
if (-not (Test-Path $exe)) { $exe = 'C:\zcode_build\stripe\out\center\IVAN-CENTER.exe' }
$p = Start-Process -FilePath $exe -ArgumentList '/silent' -PassThru -Wait
W "IVAN-CENTER /silent exit=$($p.ExitCode)"
$t = Join-Path $env:TEMP 'IVANCENTER.log'
if (Test-Path $t) { Get-Content $t | Out-File -Encoding utf8 -Append $log }
"done $(Get-Date -Format s)" | Out-File -Encoding utf8 'C:\zcode_build\stripe\_cleanup.done'
