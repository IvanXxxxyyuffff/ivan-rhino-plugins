$ErrorActionPreference = 'Continue'
$logs = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag = Join-Path $logs 'run-emboss-selftest.flag'
$report = Join-Path $logs 'PatternEmbossSelfTest.txt'
Remove-Item $report -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, 'run')
$p = Start-Process 'D:\Rhino 8\System\Rhino.exe' -ArgumentList '/nosplash' -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 150) { Start-Sleep -Milliseconds 500; if ($p.HasExited) { break } }
Start-Sleep -Seconds 2
if (Test-Path $report) {
    Get-Content $report -Encoding utf8 | Write-Output
} else {
    Write-Output ('NO REPORT (rhino exited=' + $p.HasExited + ')')
}
Get-Process -Id $p.Id -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
