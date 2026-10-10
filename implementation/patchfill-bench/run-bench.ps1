$rhino = 'D:\Rhino 8\System\Rhino.exe'
$users = @(Get-Process -Name Rhino -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -ne '' })
if ($users.Count -gt 0) { Write-Output "USER-RHINO-RUNNING — ABORT"; exit 3 }
Get-Process -Name Rhino -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq '' } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$p = Start-Process -FilePath $rhino -ArgumentList '/nosplash' -PassThru -WindowStyle Hidden
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt 90) { Start-Sleep -Milliseconds 700 }
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; Write-Output "TIMEOUT 90s" } else { Write-Output "exited code=$($p.ExitCode) at $([int]$sw.Elapsed.TotalSeconds)s" }
