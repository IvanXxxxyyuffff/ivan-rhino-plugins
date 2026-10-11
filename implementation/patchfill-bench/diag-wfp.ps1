# 诊断：面板活着时，逐个 PICKCURVE 采样点查 WFP 命中的窗口链（根顶层身份）
param([string]$Hole='bench3')
$ErrorActionPreference='Continue'
Add-Type -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct PT { public int X; public int Y; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct RECT { public int L; public int T; public int R; public int B; }
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PT p);
[DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
'@ -Name XDg -Namespace W6
function ClassOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W6.XDg]::GetClassName($h,$sb,256); $sb.ToString() }
function TextOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W6.XDg]::GetWindowText($h,$sb,256); $sb.ToString() }
function Kids([IntPtr]$h){ $r=@(); $c=[W6.XDg]::GetWindow($h,5); while($c -ne [IntPtr]::Zero){ $r+=$c; $c=[W6.XDg]::GetWindow($c,2) }; $r }
function Desc([IntPtr]$h){
  $all=New-Object 'System.Collections.Generic.List[IntPtr]'
  $q=New-Object 'System.Collections.Generic.Queue[IntPtr]'
  $q.Enqueue($h)
  while($q.Count){ $w=$q.Dequeue(); foreach($k in (Kids $w)){ if(-not $all.Contains($k)){ $all.Add($k); $q.Enqueue($k) } } }
  return $all
}
$rhino='D:\Rhino 8\System\Rhino.exe'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag=Join-Path $logs 'run-patchfill-probe.flag'
$report=Join-Path $logs 'PatchFillProbe.txt'
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item $flag,$report -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag,"xnrun=diag-$Hole`nxnpick=$Hole`n")
$p=Start-Process -FilePath $rhino -ArgumentList '/nosplash','"D:\UserData\Desktop\PatchFill-Bench-Holes.3dm"' -PassThru
$sw=[Diagnostics.Stopwatch]::StartNew()
while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 120){ Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 6
$dispatch=Get-Content $report -Raw -Encoding utf8
$curves=@()
foreach($ln in ($dispatch -split "`n")){
  if($ln -match '^PICKCURVE (\d+),(\d+) (\d+),(\d+) (\d+),(\d+)'){
    $curves += ,@([int]$Matches[1],[int]$Matches[2],[int]$Matches[3],[int]$Matches[4],[int]$Matches[5],[int]$Matches[6])
  }
}
Write-Output ("curves=" + $curves.Count)
$main=$p.MainWindowHandle
Write-Output ("MainWindowHandle=" + $main + " class=" + (ClassOf $main) + " title='" + (TextOf $main) + "'")
$mr=New-Object 'W6.XDg+RECT'; [void][W6.XDg]::GetWindowRect($main,[ref]$mr)
Write-Output ("main rect=($($mr.L),$($mr.T))-($($mr.R),$($mr.B))")
$mainSet=New-Object 'System.Collections.Generic.HashSet[IntPtr]'
foreach($d in (Desc $main)){ [void]$mainSet.Add($d) }
Write-Output ("mainSet count=" + $mainSet.Count)
$ci=0
foreach($c in $curves){
  for($k=0;$k -lt 3;$k++){
    $x=$c[$k*2]; $y=$c[$k*2+1]
    $pt=New-Object 'W6.XDg+PT'; $pt.X=$x; $pt.Y=$y
    $wfp=[W6.XDg]::WindowFromPoint($pt)
    $inMain = $mainSet.Contains($wfp)
    $root=[W6.XDg]::GetAncestor($wfp,2)
    $wpid=[uint32]0; [void][W6.XDg]::GetWindowThreadProcessId($root,[ref]$wpid)
    Write-Output ("c${ci}p$k ($x,$y) wfp=$wfp inMain=$inMain cls=" + (ClassOf $wfp) + " root=$root rootcls=" + (ClassOf $root) + " roottitle='" + (TextOf $root) + "' rootpid=$wpid")
  }
  $ci++
}
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Write-Output 'diag done'
