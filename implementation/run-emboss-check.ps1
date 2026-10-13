$ErrorActionPreference = 'Continue'
$logs = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag = Join-Path $logs 'run-emboss-selftest.flag'
$report = Join-Path $logs 'PatternEmbossSelfTest.txt'
Remove-Item $report -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, 'run')
$p = Start-Process 'D:\Rhino 8\System\Rhino.exe' -ArgumentList '/nosplash' -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
$done = $false
# 自检报告每用例刷盘 → 必须以 RESULT: 完成标记判断结束，不能只看文件是否存在
while (-not $done -and $sw.Elapsed.TotalSeconds -lt 240) {
  Start-Sleep -Milliseconds 500
  if (Test-Path $report) {
    try { if ([IO.File]::ReadAllText($report).Contains('RESULT:')) { $done = $true } } catch { }
  }
  if ($p.HasExited) { break }
}
Start-Sleep -Seconds 1
if (Test-Path $report) { Get-Content $report -Encoding utf8 | Select-Object -Last 26 | Write-Output }
else { Write-Output ('NO REPORT (exited=' + $p.HasExited + ')') }
Get-Process -Id $p.Id -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
