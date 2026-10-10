param(
  [string]$Only = 'all',              # all | 单个名字 | 逗号分隔多个（如 Voronoi,Halftone）
  [ValidateSet('session','perjob')]
  [string]$Mode = 'perjob',           # perjob = 每个自检单独启动 Rhino（默认）；session = 一次 Rhino 用 /runscript 跑完
  [switch]$Visible,                   # 默认隐藏窗口跑（不弹窗、不抢焦点）；只有要验「真渲染显示」时才加 -Visible
  [int]$TimeoutSec = 600
)
# 自检回归：一条命令跑完 7 个插件的无人值守自检，逐项断言 + 写证据文件。
# 运行方式：**默认隐藏窗口**（Rhino 8 没有真正的无头模式，插件命令必须在 GUI 进程里跑；
#   隐藏窗口启动 = 屏幕上完全看不到、不抢焦点）。唯一的例外是 MeshFix 用例6「显示可见性」——
#   它要用真渲染截图（`_-ViewCaptureToFile`），隐藏窗口下会如实 SKIP（14 通过 / 0 失败 / 1 跳过）；
#   需要这条证据时用：pwsh -File run-native-selftests.ps1 -Visible -Only MeshFix（会弹一个窗口，约 15 秒）。
# 优化点（2026-10-09，实测）：
#   ① 期望值随「用户 3dm 文件是否存在」自动选（文件被删时用例整段跳过，跳过不计分，不算回归）；
#   ② 单项不符**不再中断**整轮（旧版 throw 会让后面 6 项白等），跑完给汇总表 + 统一非零退出；
#   ③ 每项报告用时可见（挑慢项用）；单插件迭代用 -Only <名字>（约 15~60 秒）。
#   ⚠ session 模式（一次 Rhino 用 /runscript 跑完）**不可用**：面板冒烟用例会在命令上下文里构造面板 + DoEvents 死锁
#     （实测 2 分钟无任何报告）。保留该开关只为记录结论，正常都用默认 perjob。
$ErrorActionPreference = 'Stop'
$R = $PSScriptRoot
$logs = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
$rhino = 'D:\Rhino 8\System\Rhino.exe'

# PassWith / PassWithout：用户 3dm 存在 / 不存在 时的期望通过数（不存在时对应用例整段跳过，跳过不计分）
# HiddenDelta：隐藏窗口下会少掉的通过数（只有 MeshFix 的显示可见性用例；它 SKIP 时报告里会写明原因）
# Fail：已知残留（RadialDots 1 项），与 BASELINE-<Report> 的 [FAIL] 行逐字比对；PatchFill 的角点 G1 已知失败已由径向盘状参数化修复（2026-10-10）→ Fail=0
$jobs = @(
  @{Name='Voronoi';     Cmd='VoronoiSelfTest';     Flag='run-voronoi-selftest.flag';     Report='VoronoiSelfTest.txt';     PassWith=150; PassWithout=150; Fail=0; HiddenDelta=0; UserFile=''; Screenshot='VoronoiPanel-smoke.png'},
  @{Name='Stripe';      Cmd='StripeSelfTest';      Flag='run-selftest.flag';             Report='StripeSelfTest.txt';      PassWith=55; PassWithout=55; Fail=0; HiddenDelta=0; UserFile=''; Screenshot=''},
  @{Name='Halftone';    Cmd='HalftoneSelfTest';    Flag='run-halftone-selftest.flag';    Report='HalftoneSelfTest.txt';    PassWith=25; PassWithout=23; Fail=0; HiddenDelta=0; UserFile='D:\UserData\Desktop\anli.3dm'; Screenshot=''},
  @{Name='RadialDots';  Cmd='RadialDotsSelfTest';  Flag='run-radialdots-selftest.flag';  Report='RadialDotsSelfTest.txt';  PassWith=37; PassWithout=37; Fail=1; HiddenDelta=0; UserFile=''; Screenshot='RadialDotsPanel-smoke.png'},
  @{Name='MeshFix';     Cmd='MeshFixSelfTest';     Flag='run-meshfix-selftest.flag';     Report='MeshFixSelfTest.txt';     PassWith=15; PassWithout=15; Fail=0; HiddenDelta=1; UserFile='D:\UserData\Desktop\11.3dm'; Screenshot=''},
  @{Name='DiamondFacet';Cmd='DiamondFacetSelfTest';Flag='run-diamondfacet-selftest.flag';Report='DiamondFacetSelfTest.txt';PassWith=86; PassWithout=86; Fail=0; HiddenDelta=0; UserFile=''; Screenshot=''},
  @{Name='WaterRipple'; Cmd='WaterRippleSelfTest'; Flag='run-waterripple-selftest.flag'; Report='WaterRippleSelfTest.txt'; PassWith=101; PassWithout=101; Fail=0; HiddenDelta=0; UserFile=''; Screenshot=''},
  @{Name='SurfaceUnify';Cmd='SurfaceUnifySelfTest';Flag='run-surfaceunify-selftest.flag';Report='SurfaceUnifySelfTest.txt';PassWith=135; PassWithout=135; Fail=9; HiddenDelta=0; UserFile=''; Screenshot=''},
  @{Name='PatchFill';   Cmd='PatchFillSelfTest';   Flag='run-patchfill-selftest.flag';   Report='PatchFillSelfTest.txt';   PassWith=151; PassWithout=151; Fail=0; HiddenDelta=0; UserFile=''; Screenshot='PatchFillPanel-smoke.png'},
  @{Name='VapeVolume';  Cmd='VapeVolumeSelfTest';  Flag='run-vape-selftest.flag';        Report='VapeVolumeSelfTest.txt';  PassWith=3;  PassWithout=3;  Fail=0; HiddenDelta=0; UserFile=''; Screenshot=''}
)

$selected = @()
foreach ($j in $jobs) {
  if ($Only -eq 'all' -or ($Only -split ',') -contains $j.Name) { $selected += $j }
}
if ($selected.Count -eq 0) { throw "没有匹配的自检任务：$Only" }

if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
  throw 'Rhino 正在运行；自检会另开一个实例，但为避免与用户/其它测试重叠，请先关闭 Rhino 再跑'
}

Write-Output ("模式 {0}｜窗口 {1}｜任务 {2} 个：{3}" -f $Mode, $(if ($Visible) { '可见（会弹窗，仅验显示时用）' } else { '隐藏（不弹窗）' }), $selected.Count, (($selected | ForEach-Object { $_.Name }) -join ', '))
$preflight = @()
foreach ($j in $selected) {
  $has = ($j.UserFile -eq '') -or (Test-Path -LiteralPath $j.UserFile)
  $exp = if ($has) { $j.PassWith } else { $j.PassWithout }
  if (-not $Visible) { $exp -= $j.HiddenDelta }
  if (-not $has) { Write-Host ("  · {0}：用户文件 {1} 不存在 → 期望 {2}（该用例跳过）" -f $j.Name, $j.UserFile, $exp) }
  if (-not $Visible -and $j.HiddenDelta -gt 0) { Write-Host ("  · {0}：隐藏窗口 → 显示可见性用例跳过，期望 {1}（要这条证据加 -Visible）" -f $j.Name, $exp) }
  $preflight += @{ Job = $j; Expect = $exp; HasFile = $has }
}

function Invoke-Session {
  param($items, [int]$TimeoutSec)
  $script = ''
  foreach ($it in $items) { $script += "_-SelAll _-Delete _-$($it.Job.Cmd) _Enter " }
  $script += '_-Exit _No '
  $args = "/nosplash /runscript=`"$script`""
  Write-Output ("启动 Rhino（一次跑完）：{0}" -f $script.Trim())
  $p = Start-Process -FilePath $rhino -ArgumentList $args -PassThru
  $timedOut = $false
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while (-not $p.WaitForExit(1000)) {
    if ($sw.Elapsed.TotalSeconds -gt $TimeoutSec) { $timedOut = $true; try { Stop-Process -Id $p.Id -Force } catch { }; break }
  }
  $sw.Stop()
  $p.Refresh()
  $exit = if ($timedOut) { 124 } else { $p.ExitCode }
  Write-Output ("Rhino 退出码 {0}，用时 {1:0.0}s（超时={2}）" -f $exit, $sw.Elapsed.TotalSeconds, $timedOut)
  return @{ Pid = $p.Id; Exit = $exit; Seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1) }
}

function Invoke-PerJob {
  param($it, [int]$TimeoutSec, [bool]$Visible)
  $flag = Join-Path $logs $it.Job.Flag
  if (Test-Path -LiteralPath $flag) { throw "Unexpected existing test flag $flag" }
  [IO.File]::WriteAllText($flag, '')
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $p = if ($Visible) {
    Start-Process -FilePath $rhino -ArgumentList '/nosplash' -PassThru                       # 可见：MeshFix 用例6 要真渲染截图
  } else {
    Start-Process -FilePath $rhino -ArgumentList '/nosplash' -WindowStyle Hidden -PassThru   # 默认隐藏：屏幕上完全看不到
  }
  $timedOut = $false
  while (-not $p.WaitForExit(1000)) {
    if ($sw.Elapsed.TotalSeconds -gt $TimeoutSec) { $timedOut = $true; try { Stop-Process -Id $p.Id -Force } catch { }; break }
  }
  $sw.Stop(); $p.Refresh()
  return @{ Pid = $p.Id; Exit = $(if ($timedOut) { 124 } else { $p.ExitCode }); Seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1) }
}

$start = Get-Date
$session = $null
if ($Mode -eq 'session') { $session = Invoke-Session -items $preflight -TimeoutSec $TimeoutSec }

$results = @()
foreach ($it in $preflight) {
  $j = $it.Job
  $report = Join-Path $logs $j.Report
  $run = $null
  if ($Mode -eq 'perjob') { $run = Invoke-PerJob -it $it -TimeoutSec $TimeoutSec -Visible ([bool]$Visible) }

  $fresh = (Test-Path -LiteralPath $report) -and ((Get-Item -LiteralPath $report).LastWriteTime -ge $start)
  $verdict = 'MISSING'; $detail = ''; $actualPass = -1; $actualFail = -1; $secs = $null
  if ($fresh) {
    $text = Get-Content -LiteralPath $report -Raw -Encoding utf8
    Copy-Item -LiteralPath $report -Destination "$R\MODIFIED-$($j.Report)" -Force
    $m = [regex]::Match($text, 'RESULT:\s*(\d+) 通过 / (\d+) 失败')
    if ($m.Success) {
      $actualPass = [int]$m.Groups[1].Value; $actualFail = [int]$m.Groups[2].Value
      $verdict = if ($actualPass -eq $it.Expect -and $actualFail -eq $j.Fail) { 'PASS' } else { 'MISMATCH' }
      if ($verdict -eq 'MISMATCH') {
        $want = "期望 $($it.Expect)/$($j.Fail)，实际 $actualPass/$actualFail"
        $miss = @()
        if ($actualPass -lt $it.Expect) {
          $cur = [regex]::Matches($text, '(?m)^\[PASS\].*$') | ForEach-Object { $_.Value }
          $base = Join-Path $R "BASELINE-$($j.Report)"
          if (Test-Path -LiteralPath $base) {
            $old = [regex]::Matches((Get-Content -LiteralPath $base -Raw -Encoding utf8), '(?m)^\[PASS\].*$') | ForEach-Object { $_.Value }
            $miss = $old | Where-Object { $cur -notcontains $_ } | Select-Object -First 6
          }
        }
        $detail = $want + $(if ($miss.Count) { '；少的检查：' + (($miss | ForEach-Object { $_.Substring(0, [Math]::Min(90, $_.Length)) }) -join ' ｜ ') } else { '' })
      }
      if ($j.Fail -gt 0) {
        $base = Join-Path $R "BASELINE-$($j.Report)"
        if (Test-Path -LiteralPath $base) {
          $fail = [regex]::Matches($text, '(?m)^\[FAIL\].*$') | ForEach-Object { $_.Value }
          $oldFail = [regex]::Matches((Get-Content -LiteralPath $base -Raw -Encoding utf8), '(?m)^\[FAIL\].*$') | ForEach-Object { $_.Value }
          if (($fail -join "`n") -ne ($oldFail -join "`n")) { $verdict = 'REGRESSION'; $detail = '已知失败项变了，需人工复核' }
        }
      }
    } else { $verdict = 'NOPARSE'; $detail = '报告里没有 RESULT 行' }
    $secs = [Math]::Round(((Get-Item -LiteralPath $report).LastWriteTime - $start).TotalSeconds, 1)
  } else {
    $detail = if ($run) { "没有新报告（进程退出码 $($run.Exit)）" } elseif ($session) { "没有新报告（Rhino 退出码 $($session.Exit)）" } else { '没有新报告' }
  }

  $event = @{ label = "MODIFIED-$($j.Name)-SelfTest"; mode = $Mode; input = $(if ($Mode -eq 'perjob') { $j.Flag } else { $j.Cmd }); pid = $(if ($run) { $run.Pid } else { $session.Pid }); exit = $(if ($run) { $run.Exit } else { $session.Exit }); seconds = $secs; expect = "$($it.Expect)/$($j.Fail)"; actual = "$actualPass/$actualFail"; verdict = $verdict; time = (Get-Date).ToString('s') }
  $event | ConvertTo-Json -Depth 4 -Compress | Add-Content -LiteralPath "$R\native-run.jsonl" -Encoding utf8
  if ($fresh) {
    Add-Content -LiteralPath (Join-Path (Split-Path $R) 'VERIFICATION.txt') -Value ("`nNATIVE SELFTEST $($j.Name) MODE $Mode INPUT $($event.input) PID $($event.pid) EXIT $($event.exit)`nLITERAL REPORT:`n" + (Get-Content -LiteralPath $report -Raw -Encoding utf8)) -Encoding utf8
    if ($j.Screenshot) {
      $shot = Join-Path $env:TEMP $j.Screenshot
      if (Test-Path -LiteralPath $shot) { Copy-Item -LiteralPath $shot -Destination "$R\MODIFIED-$($j.Screenshot)" -Force }
    }
  }
  $results += @{ Name = $j.Name; Verdict = $verdict; Expect = $it.Expect; Fail = $j.Fail; Actual = $actualPass; ActualFail = $actualFail; Seconds = $secs; Detail = $detail }
}

Write-Output ''
Write-Output '===== 自检汇总 ====='
foreach ($r in $results) {
  $act = if ($r.Actual -lt 0) { '  --' } else { "{0,3}/{1}" -f $r.Actual, $r.ActualFail }
  $sec = if ($r.Seconds -eq $null) { '   -' } else { "{0,4:0.0}" -f $r.Seconds }
  Write-Output ("  {0,-13} {1,-9} 期望 {2,3}/{3}  实际 {4}  {5}s  {6}" -f $r.Name, $r.Verdict, $r.Expect, $r.Fail, $act, $sec, $r.Detail)
}
$bad = @($results | Where-Object { $_.Verdict -ne 'PASS' })
if ($bad.Count -eq 0) {
  Write-Output ("ALL-PASS：{0} 个自检全部符合预期（总用时 {1:0.0}s，模式 {2}）" -f $results.Count, ((Get-Date) - $start).TotalSeconds, $Mode)
  exit 0
}
Write-Output ("不通过：{0} 项 —— {1}" -f $bad.Count, (($bad | ForEach-Object { $_.Name + '(' + $_.Verdict + ')' }) -join ', '))
exit 1
