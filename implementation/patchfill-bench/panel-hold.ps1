# 保持 Rhino+XNurbs 面板存活 90 秒，供并行窗口树诊断
param([string]$Hole='bench3')
$rhino='D:\Rhino 8\System\Rhino.exe'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag=Join-Path $logs 'run-patchfill-probe.flag'
$report=Join-Path $logs 'PatchFillProbe.txt'
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item $flag,$report -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag,"xnrun=hold-$Hole`nxnpick=$Hole`n")
$p=Start-Process -FilePath $rhino -ArgumentList '/nosplash','"D:\UserData\Desktop\PatchFill-Bench-Holes.3dm"' -PassThru
Write-Output ("pid=" + $p.Id)
$sw=[Diagnostics.Stopwatch]::StartNew()
while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 120){ Start-Sleep -Milliseconds 500 }
if(-not(Test-Path $report)){ Write-Output 'dispatch timeout'; exit 4 }
Start-Sleep -Seconds 10
Write-Output 'panel should be up now - hold 90s'
Start-Sleep -Seconds 90
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Write-Output 'killed'
