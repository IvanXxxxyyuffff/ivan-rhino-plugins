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
$guids = @('E0A7B5D4-2C19-4F83-A6B7-8D1E5F2C0A94', 'B7C2D8E5-3F91-4A26-8D5C-1E9F7B3A6C48', 'A1B2C3D4-5E6F-4789-9ABC-DEF012345678', 'C4D5E6F7-8A9B-4C1D-9E2F-3A4B5C6D7E8F', 'F3E2D1C0-B9A8-4765-8432-1FEDCBA98765', 'D2F75B18-4E69-4A3C-8B51-7C0E29D6F3A8', '6C615EE9-EADB-4346-A6F5-633CA3FD7D16', '7F3C8B2A-4D69-4E7F-9021-AB3C4D5E6F71', '719FCC1D-E8AE-4B5F-AE71-AE3DBF6699EA', 'A3F27B54-9C41-4E88-B0D6-7E5C1A93D842')
$rok = 0
foreach ($g in $guids) { if (Test-Path ('HKCU:\Software\McNeel\Rhinoceros\8.0\Plug-Ins\' + $g)) { $rok++ } }
Write-Output ('registry: ' + $rok + '/10')
