$ErrorActionPreference='Stop'
if(Get-Process -Name Rhino -ErrorAction SilentlyContinue){throw 'Existing Rhino; refusing overlap'}
$psi=New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName='D:\Rhino 8\System\Rhino.exe'
$psi.Arguments='/nosplash /runscript="_-New _None _Enter _-RunPythonScript E:\IVAN-LiquidGlass-preview\implementation\vape-core-smoke.py _Enter"'
$psi.UseShellExecute=$false
$psi.CreateNoWindow=$true
$psi.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$p=New-Object System.Diagnostics.Process
$p.StartInfo=$psi
if(-not $p.Start()){throw 'Failed to start own core-test Rhino'}
Write-Output "RHINO_CORE_STARTED PID=$($p.Id)"
if(-not $p.WaitForExit(120000)){$p.Kill();throw 'Own Rhino core command timed out'}
$code=$p.ExitCode
Write-Output "RHINO_CORE_EXIT=$code"
exit $code
