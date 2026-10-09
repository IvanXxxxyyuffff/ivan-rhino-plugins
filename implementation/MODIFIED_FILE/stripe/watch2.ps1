$log = 'C:\zcode_build\stripe\watch2.log'
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Encoding utf8 $log
$waited = 0
while ((Get-Process Rhino -ErrorAction SilentlyContinue) -and $waited -lt 10800) {
    Start-Sleep -Seconds 10
    $waited += 10
}
if (Get-Process Rhino -ErrorAction SilentlyContinue) { "timeout after $waited s" | Out-File -Encoding utf8 -Append $log; exit 2 }
"Rhino closed after $waited s; installing..." | Out-File -Encoding utf8 -Append $log
$exe = 'D:\UserData\Desktop\IVAN插件中心\IVAN-CENTER.exe'
$p = Start-Process -FilePath $exe -ArgumentList '/silent' -PassThru -Wait
"install exit=$($p.ExitCode)" | Out-File -Encoding utf8 -Append $log
$t = Join-Path $env:TEMP 'IVANCENTER.log'
if (Test-Path $t) { Get-Content $t | Out-File -Encoding utf8 -Append $log }
"done $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Encoding utf8 -Append $log
