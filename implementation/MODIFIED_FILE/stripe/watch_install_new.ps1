# 等 Rhino 关闭后自动执行 IVAN CENTER 静默安装（把新的多阵列方式版本装进去）
$log = 'C:\zcode_build\stripe\_watch_new.log'
"start $(Get-Date -Format s)" | Out-File -Encoding utf8 $log
$waited = 0
while ((Get-Process Rhino -ErrorAction SilentlyContinue) -and $waited -lt 7200) {
    Start-Sleep -Seconds 10
    $waited += 10
}
if (Get-Process Rhino -ErrorAction SilentlyContinue) { "timeout after $waited s" | Out-File -Encoding utf8 -Append $log; exit 2 }
"Rhino closed after $waited s, installing..." | Out-File -Encoding utf8 -Append $log
$exe = 'D:\UserData\Desktop\IVAN插件中心\IVAN-CENTER.exe'
$p = Start-Process -FilePath $exe -ArgumentList '/silent' -PassThru -Wait
"exit=$($p.ExitCode)" | Out-File -Encoding utf8 -Append $log
$t = Join-Path $env:TEMP 'IVANCENTER.log'
if (Test-Path $t) { Get-Content $t | Out-File -Encoding utf8 -Append $log }
"done $(Get-Date -Format s)" | Out-File -Encoding utf8 -Append $log
New-Item -ItemType File -Path 'C:\zcode_build\stripe\_watch_new.done' -Force | Out-Null
