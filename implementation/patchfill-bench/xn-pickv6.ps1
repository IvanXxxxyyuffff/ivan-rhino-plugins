# =====================================================================
# XNURBS 对照 · v4 拾取器（观测点修正版）
# 反编译时序：GetObjects(拾取循环) → 右键/Enter 结束 → AddOneConstraint 喂面板 → 命令返回
# → 约束 ListBox 只在「拾取结束后」才有行数！协议：
#   逐条点击全部边界（变体按尝试轮换 A=PostMessage / B=SendMessage / C=真鼠标）
#   → 视口右键 + Enter → 轮询 LB_GETCOUNT（>0 = 拾取闭环）→ 连续性 → WM_COMMAND(2015) 创建
#   → 探针 xnmeasure 量六项
# 用法：pwsh -File xn-pickv6.ps1 -Only bench3   （不填 -Only = 全部 5 洞）
# =====================================================================
param([string]$Only = '')
$ErrorActionPreference = 'Continue'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct PT { public int X; public int Y; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct RECT { public int L; public int T; public int R; public int B; }
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PT p);
[DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref PT p);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr e);
[DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
'@ -Name XC -Namespace W3

$WM_MOUSEMOVE=0x200; $WM_LBUTTONDOWN=0x201; $WM_LBUTTONUP=0x202
$WM_RBUTTONDOWN=0x204; $WM_RBUTTONUP=0x205; $WM_COMMAND=0x111; $WM_CLOSE=0x10
$LB_GETCOUNT=0x018B; $BM_CLICK=0xF5; $BM_GETCHECK=0xF0
$GW_CHILD=5; $GW_HWNDNEXT=2

function NP([int]$x,[int]$y){ $p=New-Object 'W3.XC+PT'; $p.X=$x; $p.Y=$y; $p }
function LParam([int]$cx,[int]$cy){ [IntPtr]::new((($cy -band 0xFFFF) -shl 16) -bor ($cx -band 0xFFFF)) }
function ClassOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W3.XC]::GetClassName($h,$sb,256); $sb.ToString() }
function TextOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W3.XC]::GetWindowText($h,$sb,256); $sb.ToString() }
function Kids([IntPtr]$h){ $r=@(); $c=[W3.XC]::GetWindow($h,$GW_CHILD); while($c -ne [IntPtr]::Zero){ $r+=$c; $c=[W3.XC]::GetWindow($c,$GW_HWNDNEXT) }; $r }
function Desc([IntPtr]$h){
  $all=New-Object 'System.Collections.Generic.List[IntPtr]'
  $q=New-Object 'System.Collections.Generic.Queue[IntPtr]'
  $q.Enqueue($h)
  while($q.Count){
    $w=$q.Dequeue()
    foreach($k in (Kids $w)){ if(-not $all.Contains($k)){ $all.Add($k); $q.Enqueue($k) } }
  }
  return $all
}
function LBCount([IntPtr]$h){ [W3.XC]::SendMessage($h,$LB_GETCOUNT,[IntPtr]::Zero,[IntPtr]::Zero).ToInt32() }
function RectHas([IntPtr]$h,[int]$x,[int]$y){ $r=New-Object 'W3.XC+RECT'; if(-not [W3.XC]::GetWindowRect($h,[ref]$r)){ return $false }; ($x -ge $r.L) -and ($x -lt $r.R) -and ($y -ge $r.T) -and ($y -lt $r.B) }
function TopWinsOf([uint32]$procId){
  $r=@(); $w=[W3.XC]::GetTopWindow([IntPtr]::Zero)
  while($w -ne [IntPtr]::Zero){
    $wpid=[uint32]0; [void][W3.XC]::GetWindowThreadProcessId($w,[ref]$wpid)
    if($wpid -eq $procId -and [W3.XC]::IsWindowVisible($w)){ $r+=$w }
    $w=[W3.XC]::GetWindow($w,$GW_HWNDNEXT)
  }; $r
}
function CtrlById([IntPtr]$root,[int]$id){
  foreach($d in (Desc $root)){ if([W3.XC]::GetDlgCtrlID($d) -eq $id){ return $d } }
  return [IntPtr]::Zero
}
function ViewHwndAt([IntPtr]$main,[System.Collections.Generic.HashSet[IntPtr]]$panelSet,[int]$x,[int]$y){
  $best=[IntPtr]::Zero; $bestA=[int]::MaxValue; $cls=''
  foreach($w in (Desc $main)){
    if($w -eq $main -or -not [W3.XC]::IsWindowVisible($w)){ continue }
    if($panelSet.Contains($w)){ continue }
    if(-not (RectHas $w $x $y)){ continue }
    $r=New-Object 'W3.XC+RECT'; [void][W3.XC]::GetWindowRect($w,[ref]$r)
    $a=($r.R-$r.L)*($r.B-$r.T)
    if($a -gt 0 -and $a -lt $bestA){ $bestA=$a; $best=$w; $cls=ClassOf $w }
  }
  [pscustomobject]@{ H=$best; Cls=$cls }
}
function FindPanel([uint32]$procId){
  foreach($w in (TopWinsOf $procId)){
    if((TextOf $w) -notmatch 'TabPanelDockBarFloatingForm'){ continue }
    foreach($k in (Kids $w)){
      if((ClassOf $k) -ne '#32770'){ continue }
      $lb=[IntPtr]::Zero
      foreach($d in (Desc $k)){ if((ClassOf $d) -eq 'ListBox'){ $lb=$d; break } }
      if($lb -ne [IntPtr]::Zero){ return [pscustomobject]@{ Win=$w; Dlg=$k; ListBox=$lb; Title=(TextOf $w) } }
    }
  }
  return $null
}

$rhino='D:\Rhino 8\System\Rhino.exe'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag=Join-Path $logs 'run-patchfill-probe.flag'
$report=Join-Path $logs 'PatchFillProbe.txt'
$bench3dm='D:\UserData\Desktop\PatchFill-Bench-Holes.3dm'
$benchDir='E:\IVAN-LiquidGlass-preview\implementation\patchfill-bench'
$metrics=Join-Path $benchDir 'xn-metrics-v6.txt'

$holes=@(
  @{Hole='bench1'; Edge='bench1'; Ref='';       Cont='G0'},
  @{Hole='bench2'; Edge='bench2'; Ref='';       Cont='G0'},
  @{Hole='bench3'; Edge='bench3'; Ref='';       Cont='G0'},
  @{Hole='bench4'; Edge='bench4'; Ref='bench4'; Cont='G0'},
  @{Hole='bench5'; Edge='bench5'; Ref='bench5'; Cont='G1'}
)
if($Only -ne ''){ $holes=@($holes | Where-Object { $_.Hole -eq $Only }) }

# 用户不在电脑前：本脚本只清理自动化遗留 Rhino；若用户回来在使用（有输入 5 秒内），不抢
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
$metricsTxt=@(); $manualHoles=@()
$variantByAttempt=@('A','B','C')

foreach ($hole in $holes) {
  $H=$hole.Hole; $done=$false
  for($attempt=1; $attempt -le 3 -and -not $done; $attempt++){
    $variant=$variantByAttempt[$attempt-1]
    Write-Output ("=== {0} attempt {1} (variant {2}) ===" -f $H,$attempt,$variant)
    Remove-Item $flag,$report -Force -EA SilentlyContinue
    [IO.File]::WriteAllText($flag,"xnrun=v6-$H-a$attempt`nxnpick=$H`n")
    $p=Start-Process -FilePath $rhino -ArgumentList '/nosplash',('"' + $bench3dm + '"') -PassThru
    Start-Sleep -Seconds 3
    $sw=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 180){ Start-Sleep -Milliseconds 500; if($p.HasExited){break} }
    if(-not(Test-Path $report)){ Write-Output "dispatch timeout"; Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }
    Start-Sleep -Milliseconds 3500
    $dispatch=Get-Content $report -Raw -Encoding utf8
    $curves=@()
    foreach($ln in ($dispatch -split "`n")){
      if($ln -match '^PICKCURVE (\d+),(\d+) (\d+),(\d+) (\d+),(\d+)'){
        $curves += ,@([int]$Matches[1],[int]$Matches[2],[int]$Matches[3],[int]$Matches[4],[int]$Matches[5],[int]$Matches[6])
      }
    }
    Write-Output ("curves: " + $curves.Count)
    if($curves.Count -eq 0){ Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }

    $main=$p.MainWindowHandle
    $panel=$null; $sw2=[Diagnostics.Stopwatch]::StartNew()
    while($sw2.Elapsed.TotalSeconds -lt 30){
      $panel=FindPanel ([uint32]$p.Id)
      if($panel){ break }
      Start-Sleep -Milliseconds 500
    }
    if(-not $panel){ Write-Output "PANEL NOT FOUND"; Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }
    Write-Output ("panel ok dlg=" + $panel.Dlg + " lb=" + $panel.ListBox)

    # v6 教训：挪面板会触发布局抖动，窗口集合与真实 HWND 失配（v4/v5 失败根因）。
    # 布局已被早前几轮持久化（视口无遮挡）→ 一律不再挪窗；集合在面板就绪后延迟 1.5s 再建。
    Start-Sleep -Milliseconds 1500
    $panelSet = New-Object 'System.Collections.Generic.HashSet[IntPtr]'
    foreach($d in (Desc $panel.Win)){ [void]$panelSet.Add($d) }
    $mainSet   = New-Object 'System.Collections.Generic.HashSet[IntPtr]'
    foreach($d in (Desc $main)){ [void]$mainSet.Add($d) }
    Write-Output ("sets: panel=" + $panelSet.Count + " main=" + $mainSet.Count)

    # ---- 点击全部边界（变体固定；每击后轮询 lb —— 观测 AddOneConstraint 到底何时喂行）----
    function ClickOne([IntPtr]$vw,[IntPtr]$lp,[string]$variant,[int[]]$pt){
      if($variant -eq 'A'){
        [void][W3.XC]::PostMessage($vw,$WM_MOUSEMOVE,[IntPtr]::Zero,$lp); Start-Sleep -Milliseconds 120
        [void][W3.XC]::PostMessage($vw,$WM_LBUTTONDOWN,[IntPtr]::new(1),$lp); Start-Sleep -Milliseconds 90
        [void][W3.XC]::PostMessage($vw,$WM_LBUTTONUP,[IntPtr]::Zero,$lp)
      } elseif($variant -eq 'B'){
        [void][W3.XC]::PostMessage($vw,$WM_MOUSEMOVE,[IntPtr]::Zero,$lp); Start-Sleep -Milliseconds 120
        [void][W3.XC]::SendMessage($vw,$WM_LBUTTONDOWN,[IntPtr]::new(1),$lp) | Out-Null; Start-Sleep -Milliseconds 90
        [void][W3.XC]::SendMessage($vw,$WM_LBUTTONUP,[IntPtr]::Zero,$lp) | Out-Null
      } else {
        [void][W3.XC]::SetCursorPos($pt[0],$pt[1]); Start-Sleep -Milliseconds 150
        [W3.XC]::mouse_event(2,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 90
        [W3.XC]::mouse_event(4,0,0,0,[IntPtr]::Zero)
      }
    }
    $lbSeq=@()
    $clicked=0; $lastView=[IntPtr]::Zero; $wfpLog=@()
    foreach($c in $curves){
      $pt=$null; $vw=[IntPtr]::Zero
      for($k=0;$k -lt 3;$k++){
        $ptArg = NP $c[$k*2] $c[$k*2+1]
        $wfp=[W3.XC]::WindowFromPoint($ptArg)
        if($panelSet.Contains($wfp)){ $wfpLog += ("p" + $k + "=panel") ; continue }
        if($mainSet.Contains($wfp)){ $vw=$wfp; $pt=@($c[$k*2],$c[$k*2+1]); $wfpLog += ("p" + $k + "=wfp:" + (ClassOf $wfp)); break }
        $vv=ViewHwndAt $main $panelSet $c[$k*2] $c[$k*2+1]
        if($vv.H -ne [IntPtr]::Zero){ $vw=$vv.H; $pt=@($c[$k*2],$c[$k*2+1]); $wfpLog += ("p" + $k + "=behind:" + (ClassOf $vv.H)); break }
        $wfpLog += ("p" + $k + "=outside")
      }
      if($vw -eq [IntPtr]::Zero){ Write-Output ("curve#$clicked no target"); continue }
      $pc=NP $pt[0] $pt[1]; [void][W3.XC]::ScreenToClient($vw,[ref]$pc)
      $lp=LParam $pc.X $pc.Y
      ClickOne $vw $lp $variant $pt
      $clicked++; $lastView=$vw
      Start-Sleep -Milliseconds 350
      $lbSeq += (LBCount $panel.ListBox)
    }
    Write-Output ("clicked=$clicked variant=$variant lbPerClick=" + ($lbSeq -join ','))
    Write-Output ("targets=" + ($wfpLog -join ' | '))

    # ---- 结束拾取：右键（视口中心）+ Enter 兜底 → 然后 LB 才会有行 ----
    if($lastView -ne [IntPtr]::Zero){
      $vr=New-Object 'W3.XC+RECT'; [void][W3.XC]::GetWindowRect($lastView,[ref]$vr)
      $cx=[int](($vr.L+$vr.R)/2); $cy=[int](($vr.T+$vr.B)/2)
      $cp=NP $cx $cy; [void][W3.XC]::ScreenToClient($lastView,[ref]$cp)
      $lpC=LParam $cp.X $cp.Y
      [void][W3.XC]::PostMessage($lastView,$WM_RBUTTONDOWN,[IntPtr]::new(2),$lpC); Start-Sleep -Milliseconds 80
      [void][W3.XC]::PostMessage($lastView,$WM_RBUTTONUP,[IntPtr]::Zero,$lpC)
    }
    Start-Sleep -Milliseconds 800
    [W3.XC]::keybd_event(0x0D,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 60
    [W3.XC]::keybd_event(0x0D,0,2,[IntPtr]::Zero)
    $lbNow=-1
    $w3=[Diagnostics.Stopwatch]::StartNew()
    while($w3.Elapsed.TotalSeconds -lt 6){
      Start-Sleep -Milliseconds 400
      $lbNow=LBCount $panel.ListBox
      if($lbNow -gt 0){ break }
    }
    Write-Output ("lb after enter = " + $lbNow)
    if($lbNow -le 0){ Write-Output "PICK DID NOT REGISTER"; Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }

    # ---- 连续性 + 创建 ----
    if($hole.Cont -ne 'G0'){
      $rb=CtrlById $panel.Win $(if($hole.Cont -eq 'G1'){2003}else{2004})
      if($rb -ne [IntPtr]::Zero){
        [void][W3.XC]::SendMessage($rb,$BM_CLICK,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Milliseconds 500
        Write-Output ("radio " + $hole.Cont + " check=" + [W3.XC]::SendMessage($rb,$BM_GETCHECK,[IntPtr]::Zero,[IntPtr]::Zero).ToInt32())
      }
    }
    $btn=CtrlById $panel.Win 2015
    if($btn -ne [IntPtr]::Zero){
      $dlg=[W3.XC]::GetParent($btn); if($dlg -eq [IntPtr]::Zero){ $dlg=$panel.Win }
      [void][W3.XC]::PostMessage($dlg,$WM_COMMAND,[IntPtr]::new(2015),$btn)
      Write-Output "CREATE sent (WM_COMMAND 2015)"
    } else { Write-Output "button 2015 not found" }

    Start-Sleep -Seconds 12
    foreach($w in (TopWinsOf ([uint32]$p.Id))){ if((ClassOf $w) -eq '#32770'){ Write-Output ("modal: '" + (TextOf $w) + "' -> close"); [void][W3.XC]::PostMessage($w,$WM_CLOSE,[IntPtr]::Zero,[IntPtr]::Zero) } }
    Remove-Item $flag -Force -EA SilentlyContinue
    $mout=Join-Path $benchDir ('xn-metrics-v6-' + $H + '.txt')
    Remove-Item $mout -Force -EA SilentlyContinue
    $refLine=''
    if($hole.Ref -ne ''){ $refLine="xnm_ref_layer=$($hole.Ref)`n" }
    [IO.File]::WriteAllText($flag,"xnmeasure=m-$H`nxnm_edge_layer=$($hole.Edge)`n$refLine" + "xnm_out=" + ($mout.Replace('\','/')) + "`n")
    $sw4=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path $mout) -and $sw4.Elapsed.TotalSeconds -lt 45){ Start-Sleep -Milliseconds 500; if($p.HasExited){break} }
    if(Test-Path $mout){
      $mt=Get-Content $mout -Raw -Encoding utf8
      Write-Output $mt.Trim()
      if($mt -match 'METRIC gap=([-\d\.eE]+) maxDev=([-\d\.eE]+)'){
        $metricsTxt += "[$H a$attempt $variant] " + ($mt | Select-String 'METRIC[^\r\n]*').Matches[0].Value
        $done=$true
      }
    } else { Write-Output "measure timeout" }
    if(-not $done){ Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; Start-Sleep -Seconds 2 }
  }
  if(-not $done){ $manualHoles += $H; Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; Start-Sleep -Seconds 2 }
}

$metricsTxt += ''
$metricsTxt += 'manual-fallback: ' + $(if($manualHoles.Count){$manualHoles -join ','}else{'none'})
$metricsTxt | Set-Content $metrics -Encoding utf8
Write-Output '=== DONE ==='
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
