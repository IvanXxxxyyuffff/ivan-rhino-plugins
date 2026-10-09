$a = $env:APPDATA
$paths = @(
  "$a\McNeel\Rhinoceros\8.0\settings\settings-Scheme__Default.xml",
  "$a\McNeel\Rhinoceros\8.0\settings\Scheme__Default\containers.xml",
  "$a\McNeel\Rhinoceros\8.0\settings\Scheme__Default\workspaces\d2451180-5ff5-4419-9abb-f982b0bf106a.xml"
)
foreach ($p in $paths) {
  Write-Host ("### " + $p)
  if (-not (Test-Path $p)) { Write-Host "  MISSING"; continue }
  $lines = [System.IO.File]::ReadAllLines($p)
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match '00000000|\.rhp|VapeVolume|HalftoneDots|StripeOnSurface|IVAN|plugin_group') {
      $s = [Math]::Max(0, $i - 2)
      $e = [Math]::Min($lines.Count - 1, $i + 1)
      for ($j = $s; $j -le $e; $j++) { Write-Host (($j + 1).ToString().PadLeft(4) + ": " + $lines[$j]) }
      Write-Host "---"
    }
  }
  Write-Host ""
}