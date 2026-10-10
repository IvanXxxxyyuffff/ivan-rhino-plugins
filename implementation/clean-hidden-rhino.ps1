# 只清「没有主窗口」的隐藏 Rhino 实例（授权范围内）；有主窗口的一律不动。
# 先打印 Id / StartTime / 是否有窗口 / 内存，写进回报。
$ErrorActionPreference = 'Continue'
$all = @(Get-Process -Name Rhino -ErrorAction SilentlyContinue)
Write-Output ("Rhino 进程数：{0}" -f $all.Count)
foreach ($p in $all) {
  $hasWin = $p.MainWindowHandle -ne [IntPtr]::Zero
  Write-Output ("  pid={0} start={1} 有窗口={2} 标题='{3}' 内存={4}MB" -f `
    $p.Id, $p.StartTime.ToString('HH:mm:ss'), $hasWin, $p.MainWindowTitle, [int]($p.WorkingSet64 / 1MB))
}
$killed = 0
foreach ($p in $all) {
  if ($p.MainWindowHandle -eq [IntPtr]::Zero) {
    Write-Output ("  → 清掉隐藏实例 pid={0}（start {1}）" -f $p.Id, $p.StartTime.ToString('HH:mm:ss'))
    try { Stop-Process -Id $p.Id -Force; $killed++ } catch { Write-Output ("     清理失败：" + $_.Exception.Message) }
  } else {
    Write-Output ("  → 保留（有主窗口，可能是用户的）pid={0}" -f $p.Id)
  }
}
Start-Sleep -Milliseconds 800
Write-Output ("已清理 {0} 个；剩余 Rhino 进程 {1} 个" -f $killed, (@(Get-Process -Name Rhino -ErrorAction SilentlyContinue)).Count)
