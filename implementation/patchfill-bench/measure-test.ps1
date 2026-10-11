$ErrorActionPreference='Continue'
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
$flag=Join-Path $env:LOCALAPPDATA 'IVAN\logs\run-patchfill-probe.flag'
$mout='E:/IVAN-LiquidGlass-preview/implementation/patchfill-bench/manual-mout.txt'
Remove-Item $flag,$mout -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, "xnmeasure=t1`nxnm_edge_layer=bench1`nxnm_out=$mout`n")
$p=Start-Process 'D:\Rhino 8\System\Rhino.exe' -ArgumentList '/nosplash','"D:\UserData\Desktop\PatchFill-Bench-Holes.3dm"' -PassThru
$sw=[Diagnostics.Stopwatch]::StartNew()
while(-not(Test-Path $mout) -and $sw.Elapsed.TotalSeconds -lt 40){ Start-Sleep -Milliseconds 500 }
if(Test-Path $mout){ Write-Output 'MOUT OK:'; Get-Content $mout | Select-Object -First 10 } else { Write-Output 'MOUT TIMEOUT - channel dead' }
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
