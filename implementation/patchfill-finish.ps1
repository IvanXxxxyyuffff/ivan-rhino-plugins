# 收尾脚本：抽自检数字（BENCH/§19 用）+ 桌面同步 + 图标对比图交付
# 不启动 Rhino；跑之前请先完成：build-vape-center → install-and-verify → run-native-selftests -Only PatchFill
$ErrorActionPreference = 'Stop'
$impl = 'E:\IVAN-LiquidGlass-preview\implementation'
$rep = Join-Path $env:LOCALAPPDATA 'IVAN\logs\PatchFillSelfTest.txt'
$desk = 'D:\UserData\Desktop\IVAN插件中心'

Write-Output '===== 1) 自检报告：用例标题 + 全部 [info]/[rep] + 失败 + RESULT ====='
if (-not (Test-Path -LiteralPath $rep)) { throw "找不到自检报告：$rep（先跑 run-native-selftests.ps1 -Only PatchFill）" }
Get-Content -LiteralPath $rep -Encoding UTF8 |
  Select-String -Pattern '^--- |^\[FAIL\]|^RESULT|\[info\]|\[rep\]|\[dbg\]' |
  ForEach-Object { $_.Line }

Write-Output ''
Write-Output '===== 2) 文档同步（PROJECT-STATE / DESIGN-CONSISTENCY / UI-REFACTOR-HANDOFF → 桌面 + C:\zcode_build） ====='
& pwsh -File (Join-Path $impl 'sync-docs.ps1')

Write-Output ''
Write-Output '===== 3) 图标对比图 → 桌面 ====='
foreach ($f in @('ICONS-strip.png', 'ICONS-strip-16.png', 'ICONS-patchfill.png')) {
  $src = Join-Path $impl $f
  if (Test-Path -LiteralPath $src) {
    Copy-Item -LiteralPath $src -Destination (Join-Path $desk $f) -Force
    Write-Output ("  {0} → {1} ({2} bytes)" -f $f, $desk, (Get-Item -LiteralPath $src).Length)
  }
}
Write-Output 'DONE'
