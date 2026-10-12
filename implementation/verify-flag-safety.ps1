# 旗标防劫持 反面验证：无 /nosplash 的普通实例必须【不消费】残留旗标（不跑任务、不退出）
# 用法：pwsh -File implementation/verify-flag-safety.ps1
# 期望输出：flag still present = True / report created = False / instance exited = False / VERDICT: PASS
$ErrorActionPreference = 'Continue'
$logs = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag = Join-Path $logs 'run-emboss-probe.flag'
$report = Join-Path $env:TEMP 'ivan-flag-safety-denytest.txt'
Remove-Item $report -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, "report=$report`n")
$p = Start-Process 'D:\Rhino 8\System\Rhino.exe' -WindowStyle Hidden -PassThru
Write-Output ('no-nosplash instance pid=' + $p.Id + ' started; waiting 50s...')
Start-Sleep -Seconds 50
$flagStill = Test-Path $flag
$reportMade = Test-Path $report
$exited = $p.HasExited
Write-Output ('flag still present = ' + $flagStill)
Write-Output ('report created     = ' + $reportMade)
Write-Output ('instance exited    = ' + $exited)
Get-Process -Id $p.Id -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Remove-Item $flag -Force -EA SilentlyContinue
Remove-Item $report -Force -EA SilentlyContinue
$verdict = if ($flagStill -and -not $reportMade -and -not $exited) { 'PASS' } else { 'FAIL' }
Write-Output ('VERDICT: ' + $verdict + '（普通实例对残留旗标免疫）')
