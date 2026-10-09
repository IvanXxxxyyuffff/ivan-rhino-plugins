# 等 Rhino 关闭后自动执行 IVAN CENTER 静默安装（修复烟油插件注册 GUID）
$log = 'C:\zcode_build\stripe\_ivan_install.log'
"start $(Get-Date -Format s)" | Out-File -Encoding utf8 $log
$waited = 0
while ((Get-Process Rhino -ErrorAction SilentlyContinue) -and $waited -lt 3600) {
    Start-Sleep -Seconds 5
    $waited += 5
}
if (Get-Process Rhino -ErrorAction SilentlyContinue) { "timeout" | Out-File -Encoding utf8 -Append $log; exit 2 }
"Rhino 已关闭（等待 $waited 秒），开始安装" | Out-File -Encoding utf8 -Append $log

$exe = 'D:\UserData\Desktop\IVAN插件中心\IVAN-CENTER.exe'
if (-not (Test-Path $exe)) { $exe = 'C:\zcode_build\stripe\out\center\IVAN-CENTER.exe' }
$p = Start-Process -FilePath $exe -ArgumentList '/silent' -PassThru -Wait
"exit=$($p.ExitCode)" | Out-File -Encoding utf8 -Append $log
$t = Join-Path $env:TEMP 'IVANCENTER.log'
if (Test-Path $t) { Get-Content $t | Out-File -Encoding utf8 -Append $log }
"done $(Get-Date -Format s)" | Out-File -Encoding utf8 'C:\zcode_build\stripe\_ivan_install.done'
