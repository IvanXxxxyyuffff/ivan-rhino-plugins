param([string]$Hole='bench1', [string]$Continuity='G0', [int]$TimeoutSec=150)
$ErrorActionPreference='Continue'
$rhino='D:\Rhino 8\System\Rhino.exe'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$report=Join-Path $logs 'PatchFillProbe.txt'
$flag=Join-Path $logs 'run-patchfill-probe.flag'

Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr e);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
'@ -Name XC -Namespace W3

function ClickAt([int]$x,[int]$y){
  [W3.XC]::SetCursorPos($x,$y) | Out-Null
  Start-Sleep -Milliseconds 150
  [W3.XC]::mouse_event(2,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 90
  [W3.XC]::mouse_event(4,0,0,0,[IntPtr]::Zero)
}
function SendEnter{
  [W3.XC]::keybd_event(0x0D,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 60
  [W3.XC]::keybd_event(0x0D,0,2,[IntPtr]::Zero)
}

$users=@(Get-Process -Name Rhino -EA SilentlyContinue | Where-Object { $_.MainWindowTitle -ne '' })
if ($users.Count -gt 0) { Write-Output "USER-RHINO-RUNNING — ABORT"; exit 3 }
Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item $flag,$report -Force -EA SilentlyContinue
[IO.File]::WriteAllText($flag, "xnrun=click2-$Hole`nxnpick=$Hole`n")
$p=Start-Process -FilePath $rhino -ArgumentList '/nosplash','"D:\UserData\Desktop\PatchFill-Bench-Holes.3dm"' -PassThru
Write-Output "rhino pid=$($p.Id)"
$sw=[Diagnostics.Stopwatch]::StartNew()
while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 90){ Start-Sleep -Milliseconds 500 }
if(-not(Test-Path $report)){ Write-Output "TIMEOUT dispatch"; Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force; exit 4 }
Start-Sleep -Milliseconds 4000
$dispatch=Get-Content $report -Raw -Encoding utf8
$curves=@()
foreach($ln in ($dispatch -split "`n")){
  if($ln -match '^PICKCURVE (\d+),(\d+) (\d+),(\d+) (\d+),(\d+)'){
    $curves += ,@([int]$Matches[1],[int]$Matches[2],[int]$Matches[3],[int]$Matches[4],[int]$Matches[5],[int]$Matches[6])
  }
}
Write-Output ("clickable curves: "+$curves.Count)
if($curves.Count -eq 0){ Write-Output "NO COORDS"; Get-Content $report -Raw -Encoding utf8; Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force; exit 5 }
$h=$p.MainWindowHandle
[W3.XC]::ShowWindow($h,9)|Out-Null; [W3.XC]::SetForegroundWindow($h)|Out-Null
Start-Sleep -Milliseconds 800
# 先点标题栏激活窗口（防 click-to-activate 吞掉第一次点击），再逐条点曲线
$titleMid = 400
ClickAt $titleMid 30
Start-Sleep -Milliseconds 600
foreach($c in $curves){
  ClickAt $c[0] $c[1]    # 1/4 点
  Start-Sleep -Milliseconds 700
}
Write-Output "clicked $($curves.Count) curves"
Start-Sleep -Milliseconds 400
SendEnter; Start-Sleep -Milliseconds 1200
SendEnter; Start-Sleep -Milliseconds 1200
Write-Output "enters sent"

Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::RootElement
$pidCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)
$panel=$null
$sw2=[Diagnostics.Stopwatch]::StartNew()
while($sw2.Elapsed.TotalSeconds -lt 25 -and -not $panel){
  $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,$pidCond)
  foreach($w in $wins){
    if($w.Current.Name -match 'XNurbs'){
      $c2=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'创建')
      $b=$w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c2)
      if($b){ $panel=$w; break }
    }
  }
  if(-not $panel){ Start-Sleep -Milliseconds 500 }
}
if($panel){
  Write-Output ("panel: '"+$panel.Current.Name+"'")
  if($Continuity -ne 'G0'){
    $c3=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$Continuity)
    $rb=$panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c3)
    if($rb){ try{ ($rb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select(); Write-Output "$Continuity selected" }catch{ Write-Output "continuity select failed" } }
    Start-Sleep -Milliseconds 400
  }
  $c4=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'创建')
  $btn=$panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c4)
  if($btn){ try{ ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); Write-Output "CREATE clicked" }catch{ Write-Output "create invoke failed" } }
}else{ Write-Output "XNurbs panel with 创建 button NOT found via UIA" }
Start-Sleep -Seconds 15
Add-Type -AssemblyName System.Drawing; Add-Type -AssemblyName System.Windows.Forms
$b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height
$g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size)
$shot='E:\IVAN-LiquidGlass-preview\implementation\patchfill-bench\xn-click2-'+$Hole+'.png'
$bmp.Save($shot,[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
Write-Output "shot=$shot"
Write-Output "rhino alive pid=$($p.Id)"
