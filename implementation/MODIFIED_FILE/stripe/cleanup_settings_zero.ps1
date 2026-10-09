$a = $env:APPDATA
$f = "$a\McNeel\Rhinoceros\8.0\settings\settings-Scheme__Default.xml"
Write-Host "backup $f -> $f.bak"
Copy-Item $f "$f.bak" -Force
$lines = [System.IO.File]::ReadAllLines($f)
$keep = @()
$removed = 0
foreach ($l in $lines) {
    if ($l -match '00000000-0000-0000-0000-000000000000\.(VapeVolume|HalftoneDots|StripeOnSurface)') {
        Write-Host ("REMOVED: " + $l)
        $removed++
        continue
    }
    $keep += $l
}
[System.IO.File]::WriteAllLines($f, $keep)
Write-Host ("removed $removed zero-GUID command entries")
Write-Host ""
Write-Host "--- verify ---"
$lines2 = [System.IO.File]::ReadAllLines($f)
foreach ($l in $lines2) {
    if ($l -match '00000000-0000-0000-0000-000000000000') { Write-Host ("STILL THERE: " + $l) }
}
if (Test-Path "$f.bak") { Write-Host "backup at $f.bak" }