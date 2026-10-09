$files = @(
  "$env:APPDATA\McNeel\Rhinoceros\8.0\settings\settings-Scheme__Default.xml",
  "$env:APPDATA\McNeel\Rhinoceros\8.0\settings\Scheme__Default\containers.xml",
  "$env:APPDATA\McNeel\Rhinoceros\8.0\settings\Scheme__Default\workspaces\d2451180-5ff5-4419-9abb-f982b0bf106a.xml",
  "$env:APPDATA\McNeel\Rhinoceros\8.0\settings\Scheme__Default\StripeOnSurface_72dd056c-d640-4f6a-91fb-35632cc81cc5.xml"
)
foreach ($f in $files) {
    Write-Host ("########## " + $f)
    if (-not (Test-Path $f)) { Write-Host "  MISSING"; continue }
    $lines = [System.IO.File]::ReadAllLines($f)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l -match 'VapeVolume|StripeOnSurface|HalftoneDots|IVAN|\.rhp|d3e4f506|b1c2d3e4') {
            $s = [Math]::Max(0, $i - 1)
            $e = [Math]::Min($lines.Count - 1, $i + 1)
            for ($j = $s; $j -le $e; $j++) { Write-Host ("  " + ($j+1).ToString().PadLeft(4) + ": " + $lines[$j]) }
            Write-Host "  ----"
        }
    }
    Write-Host ""
}
