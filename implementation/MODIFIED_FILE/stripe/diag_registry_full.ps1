# Comprehensive plugin-registry forensics: find every key related to IVAN plugins in any hive/version,
# and detect duplicate registrations (same FileName under two keys).
$hives = @('HKCU:\Software\MCNeel\Rhinoceros', 'HKLM:\SOFTWARE\MCNeel\Rhinoceros')
$all = @()

foreach ($hive in $hives) {
    if (-not (Test-Path $hive)) { continue }
    foreach ($ver in (Get-ChildItem $hive -ErrorAction SilentlyContinue).PSChildName) {
        $pi = "$hive\$ver\Plug-Ins"
        if (-not (Test-Path $pi)) { continue }
        foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
            $p  = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue
            $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
            $name = [string]$p.Name
            $en   = [string]$p.EnglishName
            $all += [pscustomobject]@{
                Hive = $hive; Ver = $ver; Key = $k.PSChildName
                Name = $name; En = $en; FileName = [string]$fn
            }
        }
    }
}

Write-Host ("total plugin keys scanned: " + $all.Count)
Write-Host ""

Write-Host "=== A) keys matching IVAN plugin markers (any hive/version) ==="
foreach ($r in $all) {
    $hit = $false
    foreach ($s in @('IVAN', 'Vape', 'Stripe', 'Halftone')) {
        if ($r.FileName -like "*$s*" -or $r.Name -like "*$s*" -or $r.En -like "*$s*") { $hit = $true }
    }
    if ($hit) {
        Write-Host ("  [" + ($r.Hive -replace '.*Rhinoceros', '') + " " + $r.Ver + "] " + $r.Key)
        Write-Host ("        Name=" + $r.Name + "  En=" + $r.En)
        Write-Host ("        File=" + $r.FileName)
    }
}

Write-Host ""
Write-Host "=== B) duplicate FileName groups (same file registered more than once) ==="
$all | Where-Object { $_.FileName -ne '' } | Group-Object { $_.FileName.ToLower() } | Where-Object { $_.Count -gt 1 } | ForEach-Object {
    Write-Host ("  x" + $_.Count + "  " + $_.Name)
    $_.Group | ForEach-Object { Write-Host ("        [" + ($_.Hive -replace '.*Rhinoceros', '') + " " + $_.Ver + "] " + $_.Key) }
}

Write-Host ""
Write-Host "=== C) keys with zero GUID name (any hive/version) ==="
foreach ($r in $all) {
    if ($r.Key -ieq '00000000-0000-0000-0000-000000000000') {
        Write-Host ("  [" + ($r.Hive -replace '.*Rhinoceros', '') + " " + $r.Ver + "] zero key -> " + $r.FileName + "  Name=" + $r.Name)
    }
}

Write-Host ""
Write-Host "=== D) our 3 keys: full values ==="
foreach ($r in $all) {
    if ($r.FileName -like '*IVAN*') {
        $full = "$($r.Hive)\$($r.Ver)\Plug-Ins\$($r.Key)"
        Write-Host ("  KEY " + $full)
        $p = Get-ItemProperty $full -ErrorAction SilentlyContinue
        foreach ($prop in $p.PSObject.Properties) {
            if ($prop.Name -notlike 'PS*') { Write-Host ("      " + $prop.Name + " = " + $prop.Value) }
        }
        $ck = Get-Item "$full\CommandList" -ErrorAction SilentlyContinue
        if ($ck) { foreach ($v in $ck.GetValueNames()) { Write-Host ("      CommandList." + $v + " = " + $ck.GetValue($v)) } }
    }
}
