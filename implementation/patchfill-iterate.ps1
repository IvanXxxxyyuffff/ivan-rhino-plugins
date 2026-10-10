# 迭代期快速回路：清掉隐藏的遗留 Rhino → 编 patchfill 双 TFM → 直接替换已安装的 .rhp → 跑自检
# （收尾仍必须走 native-gate build-vape-center + install-and-verify.ps1 做逐字节核对）
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$D = 'C:\zcode_tools\dotnet\dotnet.exe'
$C = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE'
$inst = Join-Path $env:LOCALAPPDATA 'IVAN\plugins\PatchFill'

# 清掉「没有主窗口」的遗留 Rhino（= 自检隐藏实例；用户自己的 Rhino 有窗口标题，不动）
foreach ($p in @(Get-Process -Name Rhino -ErrorAction SilentlyContinue)) {
  if ([string]::IsNullOrEmpty($p.MainWindowTitle)) {
    Write-Output ("KILL stray hidden Rhino pid {0}" -f $p.Id)
    try { Stop-Process -Id $p.Id -Force } catch { }
  } else {
    Write-Output ("RHINO-RUNNING (有窗口，可能是用户自己的)：pid {0} {1}" -f $p.Id, $p.MainWindowTitle); exit 2
  }
}
Start-Sleep -Milliseconds 400

if (-not $NoBuild) {
  foreach ($p in @("$C\patchfill\Rhino8\PatchFill.csproj", "$C\patchfill\Rhino7\PatchFill.csproj")) {
    $out = & $D build $p -c Release --nologo -v minimal 2>&1
    $err = $out | Select-String -Pattern ' error ' | ForEach-Object { $_.Line }
    if ($err) { $err | ForEach-Object { Write-Output $_ }; exit 1 }
    Write-Output ("BUILT {0}" -f (Split-Path $p -Parent | Split-Path -Leaf))
  }
}
Copy-Item -LiteralPath "$C\patchfill\out\rhino8\PatchFill.rhp" -Destination (Join-Path $inst 'rh8\PatchFill.rhp') -Force
Copy-Item -LiteralPath "$C\patchfill\out\rhino7\PatchFill.rhp" -Destination (Join-Path $inst 'rh7\PatchFill.rhp') -Force
Write-Output ("INSTALLED-COPY rh8 {0} bytes" -f (Get-Item (Join-Path $inst 'rh8\PatchFill.rhp')).Length)
& pwsh -File (Join-Path $PSScriptRoot 'run-native-selftests.ps1') -Only PatchFill
$code = $LASTEXITCODE

# 等自检 Rhino 退干净（不退就按「隐藏实例」清掉，免得锁住 .rhp 让下一轮拷不进去）
for ($i = 0; $i -lt 60; $i++) {
  $alive = @(Get-Process -Name Rhino -ErrorAction SilentlyContinue | Where-Object { [string]::IsNullOrEmpty($_.MainWindowTitle) })
  if ($alive.Count -eq 0) { break }
  Start-Sleep -Milliseconds 1000
}
foreach ($p in @(Get-Process -Name Rhino -ErrorAction SilentlyContinue | Where-Object { [string]::IsNullOrEmpty($_.MainWindowTitle) })) {
  Write-Output ("KILL lingering hidden Rhino pid {0}" -f $p.Id)
  try { Stop-Process -Id $p.Id -Force } catch { }
}
exit $code
