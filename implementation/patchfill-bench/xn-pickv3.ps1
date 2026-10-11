# =====================================================================
# XNURBS 对照 · v3 拾取器（PostMessage 路线，全部可观测）
# 与 v2（SendInput 盲点击）的区别：
#   1. 点击=PostMessage 直投视口子窗口（不依赖前台/焦点/真实光标）
#   2. 每次点击后读面板约束 ListBox(id=2001) 的 LB_GETCOUNT —— 点击是否被拾取循环
#      吃下当场可见（v2 的黑盒时序问题被消除）
#   3. 点击变体自动升级：PostMessage → SendMessage → 真鼠标(SendInput)
#   4. 创建按钮 = WM_COMMAND(id=2015) 直发对话框（UIA Invoke 失败也不怕）
# 用法：pwsh -File xn-pickv3.ps1 -Only bench3   （不填 -Only = 全部 5 洞）
# 产物：xn-metrics-v3.txt 汇总 + xn-metrics-v3-benchN.txt 各洞
# =====================================================================
param([string]$Only = '', [int]$TimeoutSec = 240)
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
'@ -Name XC -Namespace W3

$WM_MOUSEMOVE=0x200; $WM_LBUTTONDOWN=0x201; $WM_LBUTTONUP=0x202
$WM_RBUTTONDOWN=0x204; $WM_RBUTTONUP=0x205; $WM_COMMAND=0x111
$LB_GETCOUNT=0x018B; $LB_GETTEXT=0x0189; $LB_GETTEXTLEN=0x018A
$BM_CLICK=0xF5; $BM_GETCHECK=0xF0
$GW_CHILD=5; $GW_HWNDNEXT=2

function NP([int]$x,[int]$y){ $p=New-Object 'W3.XC+PT'; $p.X=$x; $p.Y=$y; $p }
function LParam([int]$cx,[int]$cy){ [IntPtr]::new((($cy -band 0xFFFF) -shl 16) -bor ($cx -band 0xFFFF)) }
function ClassOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W3.XC]::GetClassName($h,$sb,256); $sb.ToString() }
function TextOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W3.XC]::GetWindowText($h,$sb,256); $sb.ToString() }

function Kids([IntPtr]$h){ $r=@(); $c=[W3.XC]::GetWindow($h,$GW_CHILD); while($c -ne [IntPtr]::Zero){ $r+=$c; $c=[W3.XC]::GetWindow($c,$GW_HWNDNEXT) }; $r }
function Desc([IntPtr]$h){
  $all=New-Object System.Collections.Generic.List[IntPtr]
  $q=New-Object System.Collections.Generic.Queue[IntPtr]
  $q.Enqueue($h)
  while($q.Count){
    $w=$q.Dequeue()
    foreach($k in (Kids $w)){ if(-not $all.Contains($k)){ $all.Add($k); $q.Enqueue($k) } }
  }
  return $all
}
function LBCount([IntPtr]$h){ [W3.XC]::SendMessage($h,$LB_GETCOUNT,[IntPtr]::Zero,[IntPtr]::Zero).ToInt32() }
function RectHas([IntPtr]$h,[int]$x,[int]$y){ $r=New-Object 'W3.XC+RECT'; if(-not [W3.XC]::GetWindowRect($h,[ref]$r)){ return $false }; ($x -ge $r.L) -and ($x -lt $r.R) -and ($y -ge $r.T) -and ($y -lt $r.B) }

# 找目标进程的顶层窗口（按 pid + 标题）
function TopWinsOf([uint32]$procId){
  $r=@(); $w=[W3.XC]::GetTopWindow([IntPtr]::Zero)
  while($w -ne [IntPtr]::Zero){
    $wpid=0; [void][W3.XC]::GetWindowThreadProcessId($w,[ref]$wpid)
    if($wpid -eq $procId -and [W3.XC]::IsWindowVisible($w)){ $r+=$w }
    $w=[W3.XC]::GetWindow($w,$GW_HWNDNEXT)
  }; $r
}
# 视口窗口：主窗后代里包含点的最小可见窗口（排除面板子树——预计算哈希集，避免 O(N×M)）
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
# 面板：XNurbs 顶层窗标题实测='TabPanelDockBarFloatingForm()'（Eto 容器），
# 真 MFC 对话框=#32770 子窗（31 控件，含 ListBox id=2001）。以「子树里有 ListBox」为判据。
function FindPanel([uint32]$procId){
  foreach($w in (TopWinsOf $procId)){
    $t=TextOf $w
    if($t -notmatch 'TabPanelDockBarFloatingForm'){ continue }
    foreach($k in (Kids $w)){
      if((ClassOf $k) -ne '#32770'){ continue }
      $lb=[IntPtr]::Zero
      foreach($d in (Desc $k)){ if((ClassOf $d) -eq 'ListBox'){ $lb=$d; break } }
      if($lb -ne [IntPtr]::Zero){ return [pscustomobject]@{ Win=$w; Dlg=$k; ListBox=$lb; Title=$t } }
    }
  }
  return $null
}
function CtrlById([IntPtr]$root,[int]$id){
  foreach($d in (Desc $root)){ if([W3.XC]::GetDlgCtrlID($d) -eq $id){ return $d } }
  return [IntPtr]::Zero
}

$rhino='D:\Rhino 8\System\Rhino.exe'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag=Join-Path $logs 'run-patchfill-probe.flag'
$report=Join-Path $logs 'PatchFillProbe.txt'
$bench3dm='D:\UserData\Desktop\PatchFill-Bench-Holes.3dm'
$benchDir='E:\IVAN-LiquidGlass-preview\implementation\patchfill-bench'
$metrics=Join-Path $benchDir 'xn-metrics-v3.txt'

$holes=@(
  @{Hole='bench1'; Edge='bench1'; Ref='';       Cont='G0'},
  @{Hole='bench2'; Edge='bench2'; Ref='';       Cont='G0'},
  @{Hole='bench3'; Edge='bench3'; Ref='';       Cont='G0'},
  @{Hole='bench4'; Edge='bench4'; Ref='bench4'; Cont='G0'},
  @{Hole='bench5'; Edge='bench5'; Ref='bench5'; Cont='G1'}
)
if($Only -ne ''){ $holes=@($holes | Where-Object { $_.Hole -eq $Only }) }

$users=@(Get-Process -Name Rhino -EA SilentlyContinue | Where-Object { $_.MainWindowTitle -ne '' })
if ($users.Count -gt 0) { Write-Output "USER-RHINO-RUNNING - ABORT"; exit 3 }
$metricsTxt=@(); $manualHoles=@()

foreach ($hole in $holes) {
  $H=$hole.Hole; $done=$false
  for($attempt=1; $attempt -le 3 -and -not $done; $attempt++){
    Write-Output ("=== {0} attempt {1} ===" -f $H,$attempt)
    Remove-Item $flag,$report -Force -EA SilentlyContinue
    [IO.File]::WriteAllText($flag,"xnrun=v3-$H-a$attempt`nxnpick=$H`n")
    $p=Start-Process -FilePath $rhino -ArgumentList '/nosplash',('"' + $bench3dm + '"') -PassThru
    Start-Sleep -Seconds 3
    $sw=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 180){ Start-Sleep -Milliseconds 500; if($p.HasExited){break} }
    if(-not(Test-Path $report)){ Write-Output "dispatch timeout"; Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }
    Start-Sleep -Milliseconds 3500
    $dispatch=Get-Content $report -Raw -Encoding utf8
    if($dispatch -match 'XNurbs command returned'){ Write-Output "command exited before picking (empty pick)" }
    $curves=@()
    foreach($ln in ($dispatch -split "`n")){
      if($ln -match '^PICKCURVE (\d+),(\d+) (\d+),(\d+) (\d+),(\d+)'){
        $curves += ,@([int]$Matches[1],[int]$Matches[2],[int]$Matches[3],[int]$Matches[4],[int]$Matches[5],[int]$Matches[6])
      }
    }
    Write-Output ("curves: " + $curves.Count)
    if($curves.Count -eq 0){ Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }

    $main=$p.MainWindowHandle
    [void][W3.XC]::SetCursorPos(1,1)
    # 等面板出现（_XNurbs Idle 派发后弹出；ListBox 就位才可观测）
    $panel=$null; $sw2=[Diagnostics.Stopwatch]::StartNew()
    while($sw2.Elapsed.TotalSeconds -lt 30){
      $panel=FindPanel ([uint32]$p.Id)
      if($panel -and $panel.ListBox -ne [IntPtr]::Zero){ break }
      Start-Sleep -Milliseconds 500
    }
    if($panel){ Write-Output ("panel: '" + $panel.Title + "' dlgcls=" + (ClassOf $panel.Dlg) + " lb=" + $panel.ListBox) }
    else { Write-Output "PANEL NOT FOUND (LB observability off - abort attempt)"; Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }

    $baseCnt=LBCount $panel.ListBox
    Write-Output ("lb count base=" + $baseCnt)
    if($baseCnt -gt 0){ Write-Output "lb not empty at start (stale constraints?) continuing" }

    # ---- 逐条拾取（变体自动升级；面板/主窗子树一次性预计算）----
    $variant=''   # '' = 未定，'A'=PostMessage 'B'=SendMessage 'C'=真鼠标
    $picked=0
    $panelSet = New-Object 'System.Collections.Generic.HashSet[IntPtr]'
    foreach($d in (Desc $panel.Win)){ [void]$panelSet.Add($d) }
    foreach($c in $curves){
      # 选一个不在面板底下的采样点
      $pt=$null; $vw=[IntPtr]::Zero
      for($k=0;$k -lt 3;$k++){
        $ptArg = NP $c[$k*2] $c[$k*2+1]
        $wfp=[W3.XC]::WindowFromPoint($ptArg)
        if($panelSet.Contains($wfp)){ continue }
        $v=ViewHwndAt $main $panelSet $c[$k*2] $c[$k*2+1]
        if($v.H -ne [IntPtr]::Zero){ $pt=@($c[$k*2],$c[$k*2+1]); $vw=$v.H; break }
      }
      Write-Output ("curve#$picked hwnd=" + $vw + " cls=" + (ClassOf $vw))
      if($vw -eq [IntPtr]::Zero){ Write-Output ("c$picked : no view hwnd at any sample point"); continue }
      $pc=NP $pt[0] $pt[1]; [void][W3.XC]::ScreenToClient($vw,[ref]$pc)
      $lp=LParam $pc.X $pc.Y
      $cands=@('A','B','C')
      if($variant -ne ''){ $cands=@($variant) }
      $ok=$false
      foreach($v in $cands){
        $before=LBCount $panel.ListBox
        if($v -eq 'A'){
          [void][W3.XC]::PostMessage($vw,$WM_MOUSEMOVE,[IntPtr]::Zero,$lp); Start-Sleep -Milliseconds 120
          [void][W3.XC]::PostMessage($vw,$WM_LBUTTONDOWN,[IntPtr]::new(1),$lp); Start-Sleep -Milliseconds 90
          [void][W3.XC]::PostMessage($vw,$WM_LBUTTONUP,[IntPtr]::Zero,$lp)
        } elseif($v -eq 'B'){
          [void][W3.XC]::PostMessage($vw,$WM_MOUSEMOVE,[IntPtr]::Zero,$lp); Start-Sleep -Milliseconds 120
          [void][W3.XC]::SendMessage($vw,$WM_LBUTTONDOWN,[IntPtr]::new(1),$lp) | Out-Null; Start-Sleep -Milliseconds 90
          [void][W3.XC]::SendMessage($vw,$WM_LBUTTONUP,[IntPtr]::Zero,$lp) | Out-Null
        } else {
          [void][W3.XC]::SetCursorPos($pt[0],$pt[1]); Start-Sleep -Milliseconds 150
          [W3.XC]::mouse_event(2,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 90
          [W3.XC]::mouse_event(4,0,0,0,[IntPtr]::Zero)
        }
        $after=$before
        $w3=[Diagnostics.Stopwatch]::StartNew()
        while($w3.Elapsed.TotalMilliseconds -lt 1500){
          Start-Sleep -Milliseconds 150
          $after=LBCount $panel.ListBox
          if($after -gt $before){ break }
        }
        if($after -gt $before){ $variant=$v; $ok=$true; break }
      }
      if($ok){ $picked++ } else { Write-Output ("c$picked : ALL variants failed (A/B/C)") }
      Start-Sleep -Milliseconds 400
    }
    Write-Output ("picked=$picked/$($curves.Count) variant=$variant lb=" + (LBCount $panel.ListBox))
    if($picked -lt $curves.Count -or $variant -eq ''){ Write-Output "PICK FAILED"; Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; continue }

    # 结束拾取：视口内右键 + Enter 兜底
    $vr=New-Object 'W3.XC+RECT'; [void][W3.XC]::GetWindowRect($vw,[ref]$vr)
    $cx=[int](($vr.L+$vr.R)/2); $cy=[int](($vr.T+$vr.B)/2)
    $cp=NP $cx $cy; [void][W3.XC]::ScreenToClient($vw,[ref]$cp)
    $lpC=LParam $cp.X $cp.Y
    [void][W3.XC]::PostMessage($vw,$WM_RBUTTONDOWN,[IntPtr]::new(2),$lpC); Start-Sleep -Milliseconds 80
    [void][W3.XC]::PostMessage($vw,$WM_RBUTTONUP,[IntPtr]::Zero,$lpC)
    Start-Sleep -Milliseconds 700
    [W3.XC]::keybd_event(0x0D,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 60
    [W3.XC]::keybd_event(0x0D,0,2,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 1200

    # 面板操作：连续性 + 创建（WM_COMMAND 直发）
    if($hole.Cont -ne 'G0'){
      $rb=CtrlById $panel.Win $(if($hole.Cont -eq 'G1'){2003}else{2004})
      if($rb -ne [IntPtr]::Zero){ [void][W3.XC]::SendMessage($rb,$BM_CLICK,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Milliseconds 500
        Write-Output ("radio " + $hole.Cont + " check=" + [W3.XC]::SendMessage($rb,$BM_GETCHECK,[IntPtr]::Zero,[IntPtr]::Zero).ToInt32()) }
    }
    $btn=CtrlById $panel.Win 2015
    if($btn -ne [IntPtr]::Zero){
      $dlg=[W3.XC]::GetParent($btn)
      if($dlg -eq [IntPtr]::Zero){ $dlg=$panel.Win }
      $wparam=[IntPtr]::new((2015 -bor (0 -shl 16)))
      [void][W3.XC]::PostMessage($dlg,$WM_COMMAND,$wparam,$btn)
      Write-Output ("CREATE sent: WM_COMMAND 2015 -> dlg=" + $dlg + " class=" + (ClassOf $dlg))
    } else {
      Write-Output "button 2015 not found"
    }

    # 等 12s → 探针度量
    Start-Sleep -Seconds 12
    # 扫掉可能弹出的模态错误框（#32770）
    foreach($w in (TopWinsOf ([uint32]$p.Id))){ if((ClassOf $w) -eq '#32770'){ Write-Output ("modal dialog: '" + (TextOf $w) + "' -> close"); [void][W3.XC]::PostMessage($w,0x10,[IntPtr]::Zero,[IntPtr]::Zero) } }
    Remove-Item $flag -Force -EA SilentlyContinue
    $mout=Join-Path $benchDir ('xn-metrics-v3-' + $H + '.txt')
    Remove-Item $mout -Force -EA SilentlyContinue
    $refLine=''
    if($hole.Ref -ne ''){ $refLine="xnm_ref_layer=$($hole.Ref)`n" }
    [IO.File]::WriteAllText($flag,"xnmeasure=m-$H`nxnm_edge_layer=$($hole.Edge)`n$refLine" + "xnm_out=" + ($mout.Replace('\','/')) + "`n")
    $sw3=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path $mout) -and $sw3.Elapsed.TotalSeconds -lt 45){ Start-Sleep -Milliseconds 500; if($p.HasExited){break} }
    if(Test-Path $mout){
      $mt=Get-Content $mout -Raw -Encoding utf8
      Write-Output $mt.Trim()
      if($mt -match 'METRIC gap=([-\d\.eE]+) maxDev=([-\d\.eE]+)'){
        $metricsTxt += "[$H a$attempt] " + ($mt | Select-String 'METRIC[^\r\n]*').Matches[0].Value
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
Write-Output ("metrics: " + $metrics)
Write-Output ("manual fallback: " + $(if($manualHoles.Count){$manualHoles -join ','}else{'none'}))
Get-Process Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
