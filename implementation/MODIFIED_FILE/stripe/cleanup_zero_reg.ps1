# Remove all-zero GUID legacy Plug-Ins keys whose PlugIn\FileName points at the IVAN plugin dir.
# Plain string paths only (Join-Path mangles registry paths in this environment).
$root = "HKCU:\Software\MCNeel\Rhinoceros"
$zero = "00000000-0000-0000-0000-000000000000"
$ivan = "C:\Users\Administrator\AppData\Local\IVAN\plugins"

Write-Host "root exists: $(Test-Path $root)"
$vers = (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName
Write-Host ("versions: " + ($vers -join ", "))

$deleted = 0
foreach ($ver in $vers) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { Write-Host "[$ver] no Plug-Ins"; continue }
    $keys = Get-ChildItem $pi -ErrorAction SilentlyContinue
    Write-Host "[$ver] Plug-Ins key count: $($keys.Count)"
    foreach ($k in $keys) {
        $name = $k.PSChildName
        $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
        $isZero = [bool]($name -ieq $zero)
        $pointsIvan = ([string]$fn).StartsWith($ivan, [System.StringComparison]::OrdinalIgnoreCase)
        if ($isZero -or $pointsIvan) {
            Write-Host ("  MATCH [" + $ver + "] " + $name + " zero=" + $isZero + " ivan=" + $pointsIvan + " fn=" + $fn)
            if ($isZero -and $pointsIvan) {
                Remove-Item "$pi\$name" -Recurse -Force
                Write-Host "  >>> DELETED $pi\$name"
                $deleted++
            }
        }
    }
}
Write-Host ("deleted=" + $deleted)

Write-Host "--- verify: remaining IVAN-pointing keys ---"
foreach ($ver in $vers) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
        $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
        if ([string]::IsNullOrEmpty($fn) -eq $false -and $fn.StartsWith($ivan, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Host ("  [" + $ver + "] " + $k.PSChildName + "  ->  " + $fn)
        }
    }
}
