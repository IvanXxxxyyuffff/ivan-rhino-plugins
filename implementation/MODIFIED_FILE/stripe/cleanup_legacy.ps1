# Clean up OLD plugin copies from before the IVAN CENTER era and any stale registry keys they own.
# Targets:
#   AppData\Local\StripeOnSurface\            (legacy Stripe installer)
#   AppData\Local\VapeVolume\                 (legacy Vape installer)
# Only deletes registry keys whose PlugIn\FileName points inside those legacy dirs.
$oldRoots = @(
  'C:\Users\Administrator\AppData\Local\StripeOnSurface',
  'C:\Users\Administrator\AppData\Local\VapeVolume'
)
$root = 'HKCU:\Software\MCNeel\Rhinoceros'

foreach ($dir in $oldRoots) {
    Write-Host ("=== " + $dir + " ===")
    if (Test-Path $dir) {
        Get-ChildItem $dir -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ("   - " + $_.FullName) }
        try { Remove-Item $dir -Recurse -Force -ErrorAction Stop; Write-Host "   (removed)" } catch { Write-Host ("   ERR remove: " + $_.Exception.Message) }
    } else { Write-Host "   (absent)" }
}

Write-Host ""
Write-Host "=== legacy-dir registry keys ==="
foreach ($ver in (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
        $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
        if ([string]::IsNullOrEmpty($fn) -eq $false) {
            foreach ($oldDir in $oldRoots) {
                if ($fn.StartsWith($oldDir, [System.StringComparison]::OrdinalIgnoreCase)) {
                    Write-Host ("   DELETE [" + $ver + "] " + $k.PSChildName + " -> " + $fn)
                    Remove-Item "$pi\$($k.PSChildName)" -Recurse -Force
                }
            }
        }
    }
}

Write-Host ""
Write-Host "=== verify: any key still pointing at legacy dirs ==="
foreach ($ver in (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
        $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
        if ([string]::IsNullOrEmpty($fn) -eq $false) {
            foreach ($oldDir in $oldRoots) {
                if ($fn.StartsWith($oldDir, [System.StringComparison]::OrdinalIgnoreCase)) {
                    Write-Host ("   STILL: [" + $ver + "] " + $k.PSChildName + " -> " + $fn)
                }
            }
        }
    }
}
Write-Host "DONE"