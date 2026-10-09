$ErrorActionPreference = 'Stop'
$impl = 'E:\IVAN-LiquidGlass-preview\implementation'
$mf = "$impl\MODIFIED_FILE"
Write-Output '===== 1) PanelTheme 7 份一致性 ====='
$themes = @(
  "$mf\shared\PanelTheme.cs", "$mf\halftone\src\PanelTheme.cs", "$mf\radialdots\src\PanelTheme.cs",
  "$mf\stripe\src\PanelTheme.cs", "$mf\stripe\center\PanelTheme.cs", "$mf\voronoi\src\PanelTheme.cs",
  "$mf\diamondfacet\src\PanelTheme.cs"
)
$hashes = $themes | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm MD5).Hash.ToLower() }
$uniq = ($hashes | Sort-Object -Unique).Count
Write-Output ("  copies={0}  unique_md5={1}  md5={2}  size={3}" -f $themes.Count, $uniq, $hashes[0].Substring(0,12), (Get-Item -LiteralPath $themes[0]).Length)

Write-Output '===== 2) payload <-> 已安装（rh8）逐字节 ====='
$payload = "$mf\stripe\center\payload"
$root = Join-Path $env:LOCALAPPDATA 'IVAN\plugins'
$map = [ordered]@{
  StripeOnSurface = 'StripeOnSurface-rh8.rhp'; VapeVolume = 'VapeVolume-Rhino8.rhp';
  HalftoneDots = 'HalftoneDots-rh8.rhp'; VoronoiTexture = 'VoronoiTexture-rh8.rhp';
  RadialDots = 'RadialDots-rh8.rhp'; MeshFix = 'MeshFix-rh8.rhp'; DiamondFacet = 'DiamondFacet-rh8.rhp'
}
$ok = 0
foreach ($k in $map.Keys) {
  $a = Join-Path $payload $map[$k]
  $installedName = ($map[$k] -replace '-rh8\.rhp$', '.rhp') -replace '-Rhino8\.rhp$', '.rhp'
  $b = Join-Path $root "$k\rh8\$installedName"
  $same = (Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $b -Algorithm SHA256).Hash
  if ($same) { $ok++ }
  Write-Output ("  {0,-16} {1}" -f $k, $(if ($same) { 'IDENTICAL' } else { 'DIFFER' }))
}
Write-Output ("  identical = {0}/7" -f $ok)

Write-Output '===== 3) 注册表（HKCU Rhino 8 Plug-Ins）====='
$defs = @(
  @{ K='StripeOnSurface'; G='b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91' }, @{ K='VapeVolume'; G='A3F27B54-9C41-4E88-B0D6-7E5C1A93D842' },
  @{ K='HalftoneDots'; G='4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27' }, @{ K='VoronoiTexture'; G='5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72' },
  @{ K='RadialDots'; G='F54E41C9-847C-4ED5-AE5C-FD905C55A1B6' }, @{ K='MeshFix'; G='8B1E47D2-6A35-4C09-9F82-3D6E15A7B0C4' },
  @{ K='DiamondFacet'; G='D2F75B18-4E69-4A3C-8B51-7C0E29D6F3A8' }
)
$regOk = 0
foreach ($d in $defs) {
  $key = "HKCU:\Software\MCNeel\Rhinoceros\8.0\Plug-Ins\$($d.G)"
  $fn = (Get-ItemProperty -LiteralPath "$key\PlugIn" -ErrorAction SilentlyContinue).FileName
  $hit = ($fn -ne $null) -and ($fn -like "*$($d.K)*")
  if ($hit) { $regOk++ }
  Write-Output ("  {0,-16} {1}  {2}" -f $d.K, $(if ($hit) { 'REGISTERED' } else { 'MISSING' }), $fn)
}
Write-Output ("  registered = {0}/7" -f $regOk)

Write-Output '===== 4) 工具条 RUI ====='
$rui = Join-Path $root 'IVAN-CENTER.rui'
$txt = Get-Content -LiteralPath $rui -Raw -Encoding utf8
$items = ([regex]::Matches($txt, '<macro_item ')).Count
$cmds = [regex]::Matches($txt, '! _([A-Za-z]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
Write-Output ("  rui={0} bytes  macro_items={1}" -f (Get-Item -LiteralPath $rui).Length, $items)
Write-Output ("  commands = " + ($cmds -join ', '))

Write-Output '===== 5) 交付目录 ====='
$dst = 'D:\UserData\Desktop\IVAN插件中心'
Get-ChildItem -LiteralPath $dst | ForEach-Object { Write-Output ("  {0,9}  {1}  {2}" -f $_.Length, $_.LastWriteTime.ToString('MM-dd HH:mm'), $_.Name) }
$exe = Join-Path $dst 'IVAN-CENTER.exe'
Write-Output ("  exe md5 = " + (Get-FileHash -LiteralPath $exe -Algorithm MD5).Hash.ToLower())

Write-Output '===== 6) 自检报告 RESULT ====='
$logs = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
foreach ($r in @('VoronoiSelfTest.txt','StripeSelfTest.txt','HalftoneSelfTest.txt','RadialDotsSelfTest.txt','MeshFixSelfTest.txt','DiamondFacetSelfTest.txt','VapeVolumeSelfTest.txt')) {
  $p = Join-Path $logs $r
  if (Test-Path -LiteralPath $p) {
    $line = (Get-Content -LiteralPath $p -Encoding utf8 | Where-Object { $_ -like 'RESULT:*' } | Select-Object -Last 1)
    Write-Output ("  {0,-26} {1}" -f $r, $line)
  } else { Write-Output ("  {0,-26} (no report)" -f $r) }
}
