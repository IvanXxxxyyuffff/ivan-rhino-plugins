param([string]$Hole='bench1', [string]$Continuity='G0', [int]$TimeoutSec=150)
$ErrorActionPreference='Continue'
$rhino='D:\Rhino 8\System\Rhino.exe'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$report=Join-Path $logs 'PatchFillProbe.txt'
$flag=Join-Path $logs 'run-patchfill-probe.flag'
$save3dm='E:\IVAN-LiquidGlass-preview\implementation\patchfill-bench\xn-result-'+$Hole+'.3dm'
Remove-Item $save3dm -Force -EA SilentlyContinue

Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] input, int size);
[StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }
[StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
public static void Enter() {
  INPUT[] inp = new INPUT[2];
  inp[0].type = 1; inp[0].ki.wVk = 0x0D;
  inp[1].type = 1; inp[1].ki.wVk = 0x0D; inp[1].ki.dwFlags = 2;
  SendInput(2, inp, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
}
[StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
public static void Click(int x, int y) {
  var a = new INPUT[3];
  a[0].type = 0; a[0].mi = NewMi(); a[0].mi.dx = x; a[0].mi.dy = y; a[0].mi.dwFlags = 0x8000 - 0x8000 + 0x4000 + 1; // MOVE|ABSOLUTE = 0x8001|... use 0x8001? correct: MOUSEEVENTF_MOVE=1, ABSOLUTE=0x8000
  a[0].mi.dwFlags = 0x8001;
  a[1].type = 0; a[1].mi.dx = x; a[1].mi.dy = y; a[1].mi.dwFlags = 0x8002;   // LEFTDOWN=2 (+ABS? no—button flags standalone)
  a[2].type = 0; a[2].mi.dx = x; a[2].mi.dy = y; a[2].mi.dwFlags = 0x8004;   // LEFTUP=4
  SendInput(3, a, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
}
static MOUSEINPUT NewMi() { return new MOUSEINPUT(); }
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
'@ -Name XC -Namespace W3

function ClickAt([int]$x,[int]$y){
  [W3.XC]::SetCursorPos($x,$y) | Out-Null
  Start-Sleep -Milliseconds 120
  [W3.XC]::mouse_event(2,0,0,0,[IntPtr]::Zero)   # LEFTDOWN
  Start-Sleep -Milliseconds 80
  [W3.XC]::mouse_event(4,0,0,0,[IntPtr]::Zero)   # LEFTUP
}

$users=@(Get-Process -Name Rhino -EA SilentlyContinue | Where-Object { $_.MainWindowTitle -ne '' })
if ($users.Count -gt 0) { Write-Output "USER-RHINO-RUNNING — ABORT"; exit 3 }
Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item $flag,$report -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, "xnrun=click-$Hole`nxnpick=$Hole`n")
$p=Start-Process -FilePath $rhino -ArgumentList '/nosplash','"D:\UserData\Desktop\PatchFill-Bench-Holes.3dm"' -PassThru
Write-Output "rhino pid=$($p.Id)"
$sw=[Diagnostics.Stopwatch]::StartNew()
while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 90){ Start-Sleep -Milliseconds 500 }
if(-not(Test-Path $report)){ Write-Output "TIMEOUT dispatch"; Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force; exit 4 }
Start-Sleep -Milliseconds 800
$dispatch=Get-Content $report -Raw -Encoding utf8
Write-Output "--- dispatch ---"; Write-Output $dispatch
$curves=@()
foreach($ln in ($dispatch -split "`n")){
  if($ln -match '^PICKCURVE ((\d+,\d+) (\d+,\d+) (\d+,\d+))'){
    $curves += ,@([int[]]($Matches[2] -split ','),[int[]]($Matches[3] -split ','),[int[]]($Matches[4] -split ','))
  }
}
Write-Output ("clickable curves: "+$curves.Count)
if($curves.Count -eq 0){ Write-Output "NO COORDS"; Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force; exit 5 }

$h=$p.MainWindowHandle
[W3.XC]::ShowWindow($h,9)|Out-Null; [W3.XC]::SetForegroundWindow($h)|Out-Null
Start-Sleep -Milliseconds 800

# 逐条点击（命令已在拾取状态）——每条点中点，两次点击间给 Rhino 反应时间
foreach($c in $curves){
  $mid=$c[1]
  ClickAt $mid[0] $mid[1]
  Start-Sleep -Milliseconds 500
}
Write-Output "clicked $($curves.Count) curves"
Start-Sleep -Milliseconds 500
[W3.XC]::Enter(); Start-Sleep -Milliseconds 1200     # 第一段结束
[W3.XC]::Enter(); Start-Sleep -Milliseconds 1200     # 第二段（内部约束）跳过
Write-Output "enters sent; waiting solve"

# 等 XNurbs 面板上有结果（创建按钮按之前需要约束已在列表）→ 先枚举面板窗口（类名任意、标题 XNurbs 或含创建按钮）
Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::RootElement
$pidCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)
$panel=$null
$sw2=[Diagnostics.Stopwatch]::StartNew()
while($sw2.Elapsed.TotalSeconds -lt 25 -and -not $panel){
  $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,$pidCond)
  foreach($w in $wins){
    $btnCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'创建')
    $b=$w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$btnCond)
    if($b -and $w.Current.Name -match 'XNurbs'){ $panel=$w; break }
    if($b -and $w -ne $main){ $panel=$w; break }
  }
  if(-not $panel){ Start-Sleep -Milliseconds 500 }
}
if($panel){
  Write-Output ("panel found: '"+$panel.Current.Name+"'")
  if($Continuity -ne 'G0'){
    $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$Continuity)
    $rb=$panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
    if($rb){ try{ ($rb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select(); Write-Output "continuity=$Continuity selected" }catch{ Write-Output "G1 select failed" } }
    Start-Sleep -Milliseconds 500
  }
  $c2=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'创建')
  $btn=$panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c2)
  if($btn){ try{ ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); Write-Output "创建 clicked" }catch{ Write-Output "创建 invoke failed" } }
}else{
  Write-Output "panel not found via UIA — continue anyway (maybe constraints still feeding)"
}
# 等生成
Start-Sleep -Seconds 15
Add-Type -AssemblyName System.Drawing; Add-Type -AssemblyName System.Windows.Forms
$b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height
$g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size)
$shot='E:\IVAN-LiquidGlass-preview\implementation\patchfill-bench\xn-click-'+$Hole+'.png'
$bmp.Save($shot,[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
Write-Output "shot=$shot"
Write-Output "rhino alive pid=$($p.Id)"
