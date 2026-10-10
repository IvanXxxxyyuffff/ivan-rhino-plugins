param([int]$Grid = 16, [switch]$Capture, [switch]$Save)
$ErrorActionPreference = 'Stop'
$logs = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$rhino = 'D:\Rhino 8\System\Rhino.exe'
$flag = Join-Path $logs 'run-surfaceunify-probe.flag'
$report = Join-Path $logs 'SurfaceUnifyProbe-test.txt'
$src = 'D:\UserData\Desktop\测试.3dm'
$capPath = Join-Path $env:TEMP 'surfunify-test.png'
if (Test-Path -LiteralPath $flag) { Remove-Item -LiteralPath $flag -Force }
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
$lines = @(('open=' + $src), ('grid=' + $Grid), 'fit=1', 'smooth=0.15', 'lock=1', 'holes=1', ('report=' + $report))
if ($Capture) { $lines += ('capture=' + $capPath) }
if ($Save) { $lines += 'save=D:\UserData\Desktop\测试-单一曲面.3dm' }
[IO.File]::WriteAllText($flag, ($lines -join "`r`n"), [Text.UTF8Encoding]::new($false))
Write-Output ('flag 已写（open=' + $src + ' grid=' + $Grid + '）')
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) { throw 'Rhino 正在运行，先关闭再跑' }
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $rhino -ArgumentList '/nosplash' -WindowStyle Hidden -PassThru
$to = $false
while (-not $p.WaitForExit(1000)) {
  if ($sw.Elapsed.TotalSeconds -gt 400) { $to = $true; try { Stop-Process -Id $p.Id -Force } catch { }; break }
}
Write-Output ("Rhino exit={0} 用时 {1:0.0}s" -f $(if ($to) { 124 } else { $p.ExitCode }), $sw.Elapsed.TotalSeconds)
if (Test-Path -LiteralPath $report) {
  Get-Content -LiteralPath $report -Encoding utf8 | Select-String -Pattern '目标|拟合：|结果：|钉合|爬行|Patch|修剪|另存|截图' | ForEach-Object { $_.Line }
} else { Write-Output '没有生成诊断报告' }
