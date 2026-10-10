param(
  [string]$Exe = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\out\center\IVAN-CENTER.exe',
  [int]$TimeoutSec = 300,
  [switch]$SkipInstall
)
# 一条命令做完「静默安装 + 交付核对」：
#   1) Rhino 在跑就直接说清楚（/silent 会 exit 2 中止，别再去查进程/注册表）
#   2) /silent 安装（Start-Process -Wait，计时；权威日志在 %TEMP%\IVANCENTER.log）
#   3) 断言：日志 7 个插件注册 + RUI 7 个按钮；payload↔已安装 7/7 逐字节；注册表 7/7；RUI macro_item 数
# 用法：pwsh -File install-and-verify.ps1            （完整跑）
#       pwsh -File install-and-verify.ps1 -SkipInstall （只核对当前已安装状态，约 2 秒）
$ErrorActionPreference = 'Stop'
$impl = Split-Path $PSScriptRoot -Parent
$mf = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE'
$pluginsRoot = Join-Path $env:LOCALAPPDATA 'IVAN\plugins'
$tempLog = Join-Path $env:TEMP 'IVANCENTER.log'
$fail = @()

$defs = @(
  @{ Key='StripeOnSurface'; Guid='b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91'; Res='StripeOnSurface-rh8.rhp'; Installed='StripeOnSurface.rhp'; Cmd='StripeOnSurface' },
  @{ Key='VapeVolume';      Guid='A3F27B54-9C41-4E88-B0D6-7E5C1A93D842'; Res='VapeVolume-Rhino8.rhp';      Installed='VapeVolume.rhp';      Cmd='VapeVolume' },
  @{ Key='HalftoneDots';    Guid='4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27'; Res='HalftoneDots-rh8.rhp';     Installed='HalftoneDots.rhp';    Cmd='ParametricTexture' },
  @{ Key='VoronoiTexture';  Guid='5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72'; Res='VoronoiTexture-rh8.rhp';   Installed='VoronoiTexture.rhp';  Cmd='VoronoiTexture' },
  @{ Key='RadialDots';      Guid='F54E41C9-847C-4ED5-AE5C-FD905C55A1B6'; Res='RadialDots-rh8.rhp';       Installed='RadialDots.rhp';      Cmd='RadialDots' },
  @{ Key='MeshFix';         Guid='8B1E47D2-6A35-4C09-9F82-3D6E15A7B0C4'; Res='MeshFix-rh8.rhp';          Installed='MeshFix.rhp';         Cmd='MeshFix' },
  @{ Key='DiamondFacet';    Guid='D2F75B18-4E69-4A3C-8B51-7C0E29D6F3A8'; Res='DiamondFacet-rh8.rhp';     Installed='DiamondFacet.rhp';    Cmd='DiamondFacet' },
  @{ Key='WaterRipple';     Guid='6C615EE9-EADB-4346-A6F5-633CA3FD7D16'; Res='WaterRipple-rh8.rhp';      Installed='WaterRipple.rhp';     Cmd='WaterRipple' },
  @{ Key='SurfaceUnify';    Guid='7F3C8B2A-4D69-4E7F-9021-AB3C4D5E6F71'; Res='SurfaceUnify-rh8.rhp';     Installed='SurfaceUnify.rhp';    Cmd='SurfaceUnify' }
)

$rhinos = @(Get-Process -Name Rhino -ErrorAction SilentlyContinue)
if ($rhinos.Count -gt 0) {
  Write-Output ("RHINO-RUNNING：检测到 {0} 个 Rhino 进程（pid {1}）。/silent 会直接 exit 2 中止，请先关闭 Rhino（不要强杀用户的 Rhino）。" -f $rhinos.Count, (($rhinos | ForEach-Object { $_.Id }) -join ','))
  if (-not $SkipInstall) { exit 2 }
}

if (-not $SkipInstall) {
  if (-not (Test-Path -LiteralPath $Exe)) { throw "找不到安装器：$Exe" }
  if (Test-Path -LiteralPath $tempLog) { Remove-Item -LiteralPath $tempLog -Force }
  $out = Join-Path $env:TEMP 'IVANCENTER-silent-stdout.txt'
  $err = Join-Path $env:TEMP 'IVANCENTER-silent-stderr.txt'
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process -FilePath $Exe -ArgumentList '/silent' -PassThru -Wait -RedirectStandardOutput $out -RedirectStandardError $err
  $sw.Stop()
  Write-Output ("INSTALL exit={0} elapsed={1:0.0}s exe={2}" -f $p.ExitCode, $sw.Elapsed.TotalSeconds, (Split-Path $Exe -Leaf))
  if ($p.ExitCode -ne 0) { $fail += "安装器退出码 $($p.ExitCode)" }
  if (-not (Test-Path -LiteralPath $tempLog)) { $fail += '没有 %TEMP%\IVANCENTER.log（安装没跑完？）' }
  else {
    $log = Get-Content -LiteralPath $tempLog -Raw -Encoding utf8
    Copy-Item -LiteralPath $tempLog -Destination 'E:\IVAN-LiquidGlass-preview\implementation\DIAMOND-silent-install.log' -Force
    $reg = ([regex]::Matches($log, '已注册到 Rhino 8')).Count
    $btn = [regex]::Match($log, '生成工具条[^\r\n]*?（(\d+) 个按钮）')
    Write-Output ("LOG 注册 {0}/{1}；工具条 {2} 个按钮；工具条登记 {3}" -f $reg, $defs.Count, $(if ($btn.Success) { $btn.Groups[1].Value } else { '?' }), $(if ($log -match '工具条已登记') { 'OK' } else { 'MISSING' }))
    if ($reg -ne $defs.Count) { $fail += "日志里注册数 $reg ≠ $($defs.Count)" }
    if (-not $btn.Success -or $btn.Groups[1].Value -ne "$($defs.Count)") { $fail += "工具条按钮数 ≠ $($defs.Count)" }
  }
}

# payload ↔ 已安装（rh8）
$payload = "$mf\stripe\center\payload"
$same = 0
foreach ($d in $defs) {
  $a = Join-Path $payload $d.Res
  $b = Join-Path $pluginsRoot "$($d.Key)\rh8\$($d.Installed)"
  if ((Test-Path -LiteralPath $a) -and (Test-Path -LiteralPath $b) -and
      ((Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $b -Algorithm SHA256).Hash)) { $same++ }
  else { $fail += "payload≠已安装：$($d.Key)" }
}
Write-Output ("PAYLOAD↔INSTALLED {0}/{1} 逐字节一致" -f $same, $defs.Count)

# 注册表 + RUI
$regOk = 0
foreach ($d in $defs) {
  $fn = (Get-ItemProperty -LiteralPath "HKCU:\Software\MCNeel\Rhinoceros\8.0\Plug-Ins\$($d.Guid)\PlugIn" -ErrorAction SilentlyContinue).FileName
  if ($fn -and $fn -like "*$($d.Key)*") { $regOk++ } else { $fail += "注册表缺：$($d.Key)" }
}
Write-Output ("REGISTRY {0}/{1}" -f $regOk, $defs.Count)

$rui = Join-Path $pluginsRoot 'IVAN-CENTER.rui'
$items = 0; $cmds = @()
if (Test-Path -LiteralPath $rui) {
  $txt = Get-Content -LiteralPath $rui -Raw -Encoding utf8
  $items = ([regex]::Matches($txt, '<macro_item ')).Count
  $cmds = [regex]::Matches($txt, '! _([A-Za-z]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
}
Write-Output ("RUI macro_item={0}；命令：{1}" -f $items, ($cmds -join ', '))
if ($items -ne $defs.Count) { $fail += "RUI 项数 $items ≠ $($defs.Count)" }
foreach ($d in $defs) { if ($cmds -notcontains $d.Cmd) { $fail += "RUI 缺命令：$($d.Cmd)" } }

if ($fail.Count -eq 0) { Write-Output 'INSTALL-VERIFY: PASS'; exit 0 }
Write-Output ('INSTALL-VERIFY: FAIL —— ' + ($fail -join '；'))
exit 1
