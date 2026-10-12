$ErrorActionPreference = 'Continue'
$P = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\center\payload'
$I = Join-Path $env:LOCALAPPDATA 'IVAN\plugins'
$pairs = @(
  @('VoronoiTexture-rh8.rhp', 'VoronoiTexture\rh8\VoronoiTexture.rhp'),
  @('StripeOnSurface-rh8.rhp', 'StripeOnSurface\rh8\StripeOnSurface.rhp'),
  @('HalftoneDots-rh8.rhp', 'HalftoneDots\rh8\HalftoneDots.rhp'),
  @('RadialDots-rh8.rhp', 'RadialDots\rh8\RadialDots.rhp'),
  @('MeshFix-rh8.rhp', 'MeshFix\rh8\MeshFix.rhp'),
  @('DiamondFacet-rh8.rhp', 'DiamondFacet\rh8\DiamondFacet.rhp'),
  @('WaterRipple-rh8.rhp', 'WaterRipple\rh8\WaterRipple.rhp'),
  @('SurfaceUnify-rh8.rhp', 'SurfaceUnify\rh8\SurfaceUnify.rhp'),
  @('PatternEmboss-rh8.rhp', 'PatternEmboss\rh8\PatternEmboss.rhp'),
  @('VapeVolume-Rhino8.rhp', 'VapeVolume\rh8\VapeVolume.rhp')
)
$ok = 0; $bad = 0
foreach ($pair in $pairs) {
  $a = Join-Path $P $pair[0]
  $b = Join-Path $I $pair[1]
  if ((Test-Path $a) -and (Test-Path $b)) {
    $ha = (Get-FileHash -LiteralPath $a -Algorithm MD5).Hash
    $hb = (Get-FileHash -LiteralPath $b -Algorithm MD5).Hash
    if ($ha -eq $hb) { $ok++ } else { $bad++; Write-Output ('MISMATCH ' + $pair[1]) }
  } else { $bad++; Write-Output ('MISSING ' + $pair[0] + ' or ' + $pair[1]) }
}
Write-Output ('payload<->installed: ' + $ok + '/10 ok, bad=' + $bad)
$guids = @(
  'b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91',   # StripeOnSurface
  'A3F27B54-9C41-4E88-B0D6-7E5C1A93D842',   # VapeVolume
  '4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27',   # HalftoneDots
  '5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72',   # VoronoiTexture
  'F54E41C9-847C-4ED5-AE5C-FD905C55A1B6',   # RadialDots
  '8B1E47D2-6A35-4C09-9F82-3D6E15A7B0C4',   # MeshFix
  'D2F75B18-4E69-4A3C-8B51-7C0E29D6F3A8',   # DiamondFacet
  '6C615EE9-EADB-4346-A6F5-633CA3FD7D16',   # WaterRipple
  '7F3C8B2A-4D69-4E7F-9021-AB3C4D5E6F71',   # SurfaceUnify
  '719FCC1D-E8AE-4B5F-AE71-AE3DBF6699EA'    # PatternEmboss
)
$rok = 0
foreach ($g in $guids) { if (Test-Path ('HKCU:\Software\McNeel\Rhinoceros\8.0\Plug-Ins\' + $g)) { $rok++ } }
Write-Output ('registry 8.0: ' + $rok + '/10')
$rok7 = 0
$guids7 = @('b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91', 'C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01', '9C4E7A21-3D58-4B06-8E97-2F1B6A3C5D08', '7E4B1C85-9A37-4D62-8F10-2C6B5E8D4A93', '45403763-4C11-42E3-989C-430F82FEEB77', '3F8A2C64-1D75-4B93-A6E2-5C70491B8D3F', '6A1D4E27-9C83-4B15-A7F2-3D58E1B96C40', '901D517B-DBFD-4AD8-9A7B-82F465031C1F', '4E2B7A19-3C58-4D6E-8F01-9A2B3C4D5E60', 'FD17B96E-7D68-4E36-AF3D-D24837F3F8BF')
foreach ($g in $guids7) { if (Test-Path ('HKCU:\Software\McNeel\Rhinoceros\7.0\Plug-Ins\' + $g)) { $rok7++ } }
Write-Output ('registry 7.0 (本机未装 Rhino 7 时为 0，仅信息): ' + $rok7 + '/10')
