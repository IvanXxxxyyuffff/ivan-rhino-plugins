$guids = @(
  'A3F27B54-9C41-4E88-B0D6-7E5C1A93D842',
  'b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91',
  '4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27',
  '5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72'
)
foreach ($g in $guids) {
  Write-Output "=== $g ==="
  $p = 'HKCU:\Software\MCNeel\Rhinoceros\8.0\Plug-Ins\' + $g
  if (Test-Path $p) {
    Get-Item $p | Select-Object -ExpandProperty Property | ForEach-Object { $n=$_; $v=(Get-ItemProperty -Path $p -Name $n).$n; Write-Output ("  {0} = {1}" -f $n, $v) }
    if (Test-Path ($p + '\PlugIn')) {
      $q = $p + '\PlugIn'
      Get-Item $q | Select-Object -ExpandProperty Property | ForEach-Object { $n=$_; $v=(Get-ItemProperty -Path $q -Name $n).$n; Write-Output ("  PlugIn\{0} = {1}" -f $n, $v) }
    } else { Write-Output "  (no PlugIn subkey)" }
  } else { Write-Output "  NOT PRESENT" }
}
Write-Output "=== Rhino7 root ==="
$r7 = 'HKCU:\Software\MCNeel\Rhinoceros\7.0\Plug-Ins'
if (Test-Path $r7) { Get-ChildItem $r7 | Select-Object -ExpandProperty Name } else { Write-Output "  (7.0 Plug-Ins missing)" }
