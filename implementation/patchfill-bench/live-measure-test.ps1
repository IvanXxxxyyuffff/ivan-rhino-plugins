$ErrorActionPreference='Continue'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag=Join-Path $logs 'run-patchfill-probe.flag'
$report=Join-Path $logs 'PatchFillProbe.txt'
$mout='E:/IVAN-LiquidGlass-preview/implementation/patchfill-bench/manual-mout.txt'
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item $flag,$report,$mout -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, "xnrun=man-live`nxnpick=bench1`n")
$p=Start-Process 'D:\Rhino 8\System\Rhino.exe' -ArgumentList '/nosplash','"D:\UserData\Desktop\PatchFill-Bench-Holes.3dm"' -PassThru
$sw=[Diagnostics.Stopwatch]::StartNew()
while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 120){ Start-Sleep -Milliseconds 500 }
Write-Output ("report at " + [int]$sw.Elapsed.TotalSeconds + "s")
Start-Sleep -Seconds 6
# 手动结束 XNurbs 命令：真右键视口中心 ×2 + Enter
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr e);
'@ -Name XM -Namespace W7
[W7.XM]::SetCursorPos(1366,700) | Out-Null; Start-Sleep -Milliseconds 300
foreach($i in 1..2){
  [W7.XM]::mouse_event(8,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
  [W7.XM]::mouse_event(16,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 700
}
[W7.XM]::keybd_event(0x0D,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 60
[W7.XM]::keybd_event(0x0D,0,2,[IntPtr]::Zero)
Start-Sleep -Seconds 4
$rp = Get-Content $report -Raw -Encoding utf8
Write-Output ("cmd returned: " + ($rp -match 'XNurbs command returned'))
# 手写 C2 = xnmeasure
Remove-Item $flag -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, "xnmeasure=t2`nxnm_edge_layer=bench1`nxnm_out=$mout`n")
Write-Output "C2 written"
for($i=0;$i -lt 15;$i++){
  Start-Sleep -Seconds 2
  $fx = Test-Path $flag
  $mx = Test-Path $mout
  Write-Output ("t+" + (($i+1)*2) + "s flagExists=$fx moutExists=$mx")
  if($mx){ break }
}
if(Test-Path $mout){ Get-Content $mout | Select-Object -First 8 }
$rp2 = Get-Content $report -Raw -Encoding utf8
$tail = ($rp2 -split "`n") | Select-Object -Last 6
Write-Output ("report tail: " + ($tail -join ' / '))
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
