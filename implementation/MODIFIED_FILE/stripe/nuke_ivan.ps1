# NUCLEAR: completely wipe all IVAN-related registry keys and deployed files,
# then silent-install. This proves the bug is reproducible from a clean slate.
$ErrorActionPreference = 'Stop'
$root = 'HKCU:\Software\MCNeel\Rhinoceros'
$ivan = 'C:\Users\Administrator\AppData\Local\IVAN'
$uiRui = "$env:APPDATA\McNeel\Rhinoceros\8.0\UI\Plug-ins\IVAN-CENTER.rui"
$targetGuids = @('b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91','A3F27B54-9C41-4E88-B0D6-7E5C1A93D842','4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27','9C4E7A21-3D58-4B06-8E97-2F1B6A3C5D08','C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01','00000000-0000-0000-0000-000000000000')

foreach ($ver in (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
        $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
        $isZero = $k.PSChildName -ieq '00000000-0000-0000-0000-000000000000'
        $hit = $targetGuids -contains $k.PSChildName
        $pointsIvan = $fn -and $fn.StartsWith($ivan, [System.StringComparison]::OrdinalIgnoreCase)
        if ($hit -or $isZero -or $pointsIvan) {
            Write-Host ("DELETE key [" + $ver + "] " + $k.PSChildName)
            try { Remove-Item "$pi\$($k.PSChildName)" -Recurse -Force } catch { Write-Host ("  err: " + $_.Exception.Message) }
        }
    }
}

# Remove entire IVAN deployed dir
if (Test-Path $ivan) {
    Write-Host ("REMOVE dir " + $ivan)
    try { Remove-Item $ivan -Recurse -Force } catch { Write-Host ("  err: " + $_.Exception.Message) }
}

# Reset settings: remove any zero-GUID command entries
$sf = "$env:APPDATA\McNeel\Rhinoceros\8.0\settings\settings-Scheme__Default.xml"
if (Test-Path $sf) {
    $bak = "$sf.bak-nuclear"
    if (-not (Test-Path $bak)) { Copy-Item $sf $bak -Force; Write-Host ("backup $sf -> $bak") }
    $lines = [System.IO.File]::ReadAllLines($sf)
    $keep = @()
    $removed = 0
    foreach ($l in $lines) {
        if ($l -match '00000000-0000-0000-0000-000000000000\.(VapeVolume|HalftoneDots|StripeOnSurface)') {
            Write-Host ("settings STRIP: " + $l)
            $removed++
            continue
        }
        $keep += $l
    }
    [System.IO.File]::WriteAllLines($sf, $keep)
    Write-Host ("removed $removed zero-guid setting entries")
}

# Remove UI rui for our toolbar (so it doesn't try to dock a dead toolbar)
if (Test-Path $uiRui) {
    Write-Host ("REMOVE " + $uiRui)
    Remove-Item $uiRui -Force
}

Write-Host "=== after cleanup ==="
foreach ($ver in (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
        $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
        if ($fn -and $fn.StartsCaseInsensitive($ivan)) {
            Write-Host ("  STILL: [" + $ver + "] " + $k.PSChildName + " -> " + $fn)
        }
    }
}
if (Test-Path $ivan) { Write-Host ("  IVAN dir STILL exists") } else { Write-Host "  IVAN dir removed" }

Write-Host "DONE CLEANUP"