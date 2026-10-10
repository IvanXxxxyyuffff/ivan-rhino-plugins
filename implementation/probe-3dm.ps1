param([int]$Grid = 12, [switch]$Capture, [switch]$Save)
$ErrorActionPreference = 'Stop'
$logs = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$rhino = 'D:\Rhino 8\System\Rhino.exe'
$flag = Join-Path $logs 'run-surfaceunify-probe.flag'
$report = Join-Path $logs ("SurfaceUnifyProbe-g{0}.txt" -f $Grid)
$capPath = Join-Path $env:TEMP ("surfunify-probe-g{0}.png" -f $Grid)
if (Test-Path -LiteralPath $flag) { Remove-Item -LiteralPath $flag -Force }
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }

$lines = @('open=D:\UserData\Desktop\2234.3dm', ('grid=' + $Grid), 'fit=1', 'smooth=0.15', 'lock=1', 'holes=1')
if ($Capture) { $lines += ('capture=' + $capPath) }
$lines += ('report=' + $report)
if ($Save) { $lines += 'save=D:\UserData\Desktop\2234-单一曲面.3dm' }
[IO.File]::WriteAllText($flag, ($lines -join "`r`n"), [Text.UTF8Encoding]::new($false))

if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) { throw 'Rhino 正在运行，先关闭再跑' }
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $rhino -ArgumentList '/nosplash' -WindowStyle Hidden -PassThru
$timedOut = $false
while (-not $p.WaitForExit(1000)) {
  if ($sw.Elapsed.TotalSeconds -gt 400) { $timedOut = $true; try { Stop-Process -Id $p.Id -Force } catch { }; break }
}
$sw.Stop()
Write-Output ("grid={0}：Rhino 退出码 {1}，用时 {2:0.0}s" -f $Grid, $(if ($timedOut) { 124 } else { $p.ExitCode }), $sw.Elapsed.TotalSeconds)
if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Encoding utf8 | Select-String -Pattern '目标[12]：|拟合：|结果：|写入文档' | ForEach-Object { $_.Line } }
else { Write-Output '没有生成诊断报告' }
