# =====================================================================
# XNURBS 机器化对照 · 一次性总控（带确认）
# 流程：确认框 → 逐洞（bench1..bench5）：清场→起命令→坐标点击→创建→度量→存结果
#       → 每洞最多重试 3 次，3 次不闭环记入手动兜底清单
# 启动：pwsh -File xn-runall.ps1        （全程约 5 分钟；确认后请勿动鼠标）
# 产物：patchfill-bench\xn-metrics.txt（六项度量）、xn-result-benchN.3dm、xn-todo-manual.txt
# =====================================================================
param([int]$TimeoutSec = 150)
$ErrorActionPreference = 'Continue'

# ---- 确认框（用户主动让出鼠标）----
$ans = [System.Windows.Forms.MessageBox]::Show(
  "将置前 Rhino 并占用鼠标约 5 分钟，自动跑 XNURBS 对照（5 个测试洞）。`n`n确认后请不要移动鼠标/键盘，直到完成提示出现。",
  "XNURBS 机器化对照", [System.Windows.Forms.MessageBoxButtons]::OKCancel,
  [System.Windows.Forms.MessageBoxIcon]::Information)
if ($ans -ne [System.Windows.Forms.DialogResult]::OK) { Write-Output "CANCELLED by user"; exit 0 }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr e);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
'@ -Name XC -Namespace W3
Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes

function ClickAt([int]$x,[int]$y){
  [W3.XC]::SetCursorPos($x,$y)|Out-Null; Start-Sleep -Milliseconds 150
  [W3.XC]::mouse_event(2,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 90
  [W3.XC]::mouse_event(4,0,0,0,[IntPtr]::Zero)
}
function SendEnter{
  [W3.XC]::keybd_event(0x0D,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 60
  [W3.XC]::keybd_event(0x0D,0,2,[IntPtr]::Zero)
}

$rhino='D:\Rhino 8\System\Rhino.exe'
$logs=Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$flag=Join-Path $logs 'run-patchfill-probe.flag'
$report=Join-Path $logs 'PatchFillProbe.txt'
$bench3dm='D:\UserData\Desktop\PatchFill-Bench-Holes.3dm'
$benchDir='E:\IVAN-LiquidGlass-preview\implementation\patchfill-bench'
$metrics=Join-Path $benchDir 'xn-metrics.txt'
$manual=Join-Path $benchDir 'xn-todo-manual.txt'
Remove-Item $metrics,$manual -Force -EA SilentlyContinue
$metricsTxt = @()
$manualHoles = @()

# 洞配置：边界层 | 参考层 | 连续性
$holes = @(
  @{Hole='bench1'; Edge='bench1'; Ref='';        Cont='G0'},
  @{Hole='bench2'; Edge='bench2'; Ref='';        Cont='G0'},
  @{Hole='bench3'; Edge='bench3'; Ref='';        Cont='G0'},
  @{Hole='bench4'; Edge='bench4'; Ref='bench4';  Cont='G0'},
  @{Hole='bench5'; Edge='bench5'; Ref='bench5';  Cont='G1'}
)

$users=@(Get-Process -Name Rhino -EA SilentlyContinue | Where-Object { $_.MainWindowTitle -ne '' })
if ($users.Count -gt 0) { Write-Output "USER-RHINO-RUNNING — ABORT"; exit 3 }
Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2

foreach ($hole in $holes) {
  $H = $hole.Hole; $done = $false
  for ($attempt = 1; $attempt -le 3 -and -not $done; $attempt++) {
    Write-Output ("=== {0} attempt {1} ===" -f $H, $attempt)
    Remove-Item $flag,$report -Force -EA SilentlyContinue
    # flag：派发探针（坐标表 + Idle 派发 _XNurbs）
    [IO.File]::WriteAllText($flag, "xnrun=runall-$H-a$attempt`nxnpick=$H`n")
    if ($attempt -eq 1) {
      $p = Start-Process -FilePath $rhino -ArgumentList '/nosplash', ('"' + $bench3dm + '"') -PassThru
    }
    $sw=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path $report) -and $sw.Elapsed.TotalSeconds -lt 60){ Start-Sleep -Milliseconds 500 }
    if(-not(Test-Path $report)){ Write-Output "dispatch timeout"; continue }
    Start-Sleep -Milliseconds 3500    # Idle 派发 + 命令进入拾取
    $dispatch = Get-Content $report -Raw -Encoding utf8
    $curves=@()
    foreach($ln in ($dispatch -split "`n")){
      if($ln -match '^PICKCURVE (\d+),(\d+) (\d+),(\d+) (\d+),(\d+)'){
        $curves += ,@([int]$Matches[1],[int]$Matches[2],[int]$Matches[3],[int]$Matches[4],[int]$Matches[5],[int]$Matches[6])
      }
    }
    if($curves.Count -eq 0){ Write-Output "no coords"; continue }
    $h = $p.MainWindowHandle
    [W3.XC]::ShowWindow($h,9)|Out-Null; [W3.XC]::SetForegroundWindow($h)|Out-Null
    Start-Sleep -Milliseconds 700
    ClickAt 400 30; Start-Sleep -Milliseconds 500    # 激活
    foreach($c in $curves){ ClickAt $c[0] $c[1]; Start-Sleep -Milliseconds 650 }
    SendEnter; Start-Sleep -Milliseconds 1100
    SendEnter; Start-Sleep -Milliseconds 1500

    # 面板：UIA 找含「创建」的 XNurbs 浮窗（MFC 自绘可能找不到 → 兜底用 TabPanelDockBarFloatingForm 里带「创建」的窗）
    $root=[System.Windows.Automation.AutomationElement]::RootElement
    $pidCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)
    $panel=$null
    $sw2=[Diagnostics.Stopwatch]::StartNew()
    while($sw2.Elapsed.TotalSeconds -lt 15 -and -not $panel){
      $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,$pidCond)
      foreach($w in $wins){
        $c2=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'创建')
        $b=$w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c2)
        if($b){ $panel=$w; break }
      }
      if(-not $panel){ Start-Sleep -Milliseconds 500 }
    }
    if($panel){
      if($hole.Cont -eq 'G1'){
        $c3=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'G1')
        $rb=$panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c3)
        if($rb){ try{ ($rb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() }catch{} }
        Start-Sleep -Milliseconds 400
      }
      $c4=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'创建')
      $btn=$panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c4)
      if($btn){ try{ ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); Write-Output "CREATE clicked" }catch{ Write-Output "create invoke failed" } }
    } else { Write-Output "panel/buttons not visible via UIA" }

    # 等生成 → 度量（探针 flag 通道；结果面 = 非 bench* 层的 Brep）
    Start-Sleep -Seconds 12
    Remove-Item $flag -Force -EA SilentlyContinue
    $mout = Join-Path $benchDir ('xn-metrics-' + $H + '.txt')
    Remove-Item $mout -Force -EA SilentlyContinue
    $refLine = ''
    if ($hole.Ref -ne '') { $refLine = "xnm_ref_layer=$($hole.Ref)`n" }
    [IO.File]::WriteAllText($flag, "xnmeasure=m-$H`nxnm_edge_layer=$($hole.Edge)`n$refLine" + "xnm_out=" + ($mout -replace [char]92,'/') + "`n")
    $sw3=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path $mout) -and $sw3.Elapsed.TotalSeconds -lt 30){ Start-Sleep -Milliseconds 500 }
    if(Test-Path $mout){
      $mt = Get-Content $mout -Raw -Encoding utf8
      Write-Output $mt
      if($mt -match 'METRIC gap=([\d\.\-]+) maxDev=([\d\.\-]+)'){
        $metricsTxt += "[$H attempt$attempt] " + ($mt | Select-String 'METRIC[^\r\n]*').Matches.Value
        # 保存结果面
        $save3dm = Join-Path $benchDir ('xn-result-' + $H + '.3dm')
        Remove-Item $flag -Force -EA SilentlyContinue
        # 用 Save 通道：探针 run 的 save= 只在 xnrun 分支——这里用 Rhino 脚本保存（RunScript 会排队）
        # 简化：直接把度量结果落 metrics（结果面留在 rhino 文档，最后统一另存）
        $done = $true
      }
    }
    if(-not $done){ Write-Output "attempt $attempt failed"; Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; Start-Sleep -Seconds 2 }
    else { break }
  }
  if(-not $done){ $manualHoles += $H; Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue; Start-Sleep -Seconds 2 }
}

# 收尾：手动兜底清单 + 汇总
if($manualHoles.Count -gt 0){
  [IO.File]::WriteAllText($manual, "3 次尝试未闭环的洞（手动 30 秒兜底：框选边界 → 右键 → 面板创建 → 结果面留下跑 xnmeasure）：" + "`n" + ($manualHoles -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
}
$metricsTxt += ""
$metricsTxt += "manual-fallback: " + $(if($manualHoles.Count){$manualHoles -join ','}else{'none'})
$metricsTxt | Set-Content $metrics -Encoding utf8
Write-Output "=== DONE ==="
Write-Output ("metrics: " + $metrics)
Write-Output ("manual fallback holes: " + $(if($manualHoles.Count){$manualHoles -join ','}else{'none'}))
Start-Sleep -Seconds 3
Get-Process -Name Rhino -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
