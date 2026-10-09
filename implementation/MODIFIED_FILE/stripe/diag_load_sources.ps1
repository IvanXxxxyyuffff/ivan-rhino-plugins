# Look for non-registry plugin load sources: per-user Plug-ins folder, settings folder, WOW6432Node keys, search folders.
$out = @()

Write-Host "=== 1) per-user Rhino plugin folder ==="
$p1 = "$env:APPDATA\McNeel\Rhinoceros\8.0\Plug-ins"
Write-Host ("path: " + $p1 + "  exists: " + (Test-Path $p1))
if (Test-Path $p1) { Get-ChildItem $p1 -Recurse -File | ForEach-Object { Write-Host ("   " + $_.Length + "  " + $_.LastWriteTime.ToString('MM-dd HH:mm') + "  " + $_.FullName) } }

Write-Host ""
Write-Host "=== 2) settings folder (recent files) ==="
$p2 = "$env:APPDATA\McNeel\Rhinoceros\8.0\settings"
if (Test-Path $p2) {
    Get-ChildItem $p2 -Recurse -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 25 | ForEach-Object {
        Write-Host ("   " + $_.LastWriteTime.ToString('MM-dd HH:mm') + "  " + $_.Length.ToString().PadLeft(8) + "  " + $_.FullName)
    }
} else { Write-Host "  (no settings dir)" }

Write-Host ""
Write-Host "=== 3) any settings file mentioning VapeVolume/StripeOnSurface/HalftoneDots/IVAN ==="
if (Test-Path $p2) {
    Get-ChildItem $p2 -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $c = [System.IO.File]::ReadAllText($_.FullName)
            foreach ($s in @('VapeVolume', 'StripeOnSurface', 'HalftoneDots', 'IVAN')) {
                if ($c.Contains($s)) { Write-Host ("   HIT " + $s + " in " + $_.FullName); break }
            }
        } catch { }
    }
}

Write-Host ""
Write-Host "=== 4) WOW6432Node registry views ==="
foreach ($h in @('HKCU:\Software\WOW6432Node\McNeel\Rhinoceros', 'HKLM:\SOFTWARE\WOW6432Node\McNeel\Rhinoceros')) {
    if (-not (Test-Path $h)) { Write-Host ("  " + $h + " : absent"); continue }
    foreach ($ver in (Get-ChildItem $h).PSChildName) {
        $pi = "$h\$ver\Plug-Ins"
        if (-not (Test-Path $pi)) { continue }
        foreach ($k in (Get-ChildItem $pi)) {
            $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
            if ($fn -and ($fn -like '*IVAN*' -or $fn -like '*Vape*' -or $fn -like '*Stripe*' -or $fn -like '*Halftone*')) {
                Write-Host ("  " + $pi + "  " + $k.PSChildName + " -> " + $fn)
            }
        }
    }
    Write-Host ("  (scanned " + $h + ")")
}

Write-Host ""
Write-Host "=== 5) HKCU 8.0 top-level values ==="
$t = 'HKCU:\Software\MCNeel\Rhinoceros\8.0'
$p = Get-ItemProperty $t -ErrorAction SilentlyContinue
if ($p) { foreach ($prop in $p.PSObject.Properties) { if ($prop.Name -notlike 'PS*') { Write-Host ("   " + $prop.Name + " = " + $prop.Value) } } }
Write-Host "  subkeys:"
foreach ($k in (Get-ChildItem $t -ErrorAction SilentlyContinue)) { Write-Host ("   " + $k.PSChildName) }

Write-Host ""
Write-Host "=== 6) Rhino install Plug-ins folder: IVAN-ish files ==="
Get-ChildItem 'D:\Rhino 8\Plug-ins' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*IVAN*' -or $_.Name -like '*Vape*' -or $_.Name -like '*Stripe*' -or $_.Name -like '*Halftone*' } | ForEach-Object { Write-Host ("   " + $_.FullName) }
Write-Host "  (done)"

Write-Host ""
Write-Host "=== 7) deployed rhp GUID strings re-check ==="
$ivan = 'C:\Users\Administrator\AppData\Local\IVAN\plugins'
$checks = @(
  @{f="$ivan\HalftoneDots\HalftoneDots-rh8.rhp"; g="4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27"},
  @{f="$ivan\StripeOnSurface\StripeOnSurface-rh8.rhp"; g="b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91"},
  @{f="$ivan\VapeVolume\VapeVolume-Rhino8.rhp"; g="A3F27B54-9C41-4E88-B0D6-7E5C1A93D842"}
)
foreach ($c in $checks) {
    if (Test-Path $c.f) {
        $b = [System.IO.File]::ReadAllBytes($c.f)
        $t2 = [System.Text.Encoding]::ASCII.GetString($b)
        $fi = Get-Item $c.f
        Write-Host ("   " + $fi.Name + "  size=" + $fi.Length + "  mtime=" + $fi.LastWriteTime.ToString('HH:mm:ss') + "  guidFound=" + $t2.ToLower().Contains($c.g.ToLower()))
    } else { Write-Host ("   MISSING " + $c.f) }
}
