# Scan .rhp payload builds for their embedded [Guid] attribute strings (ASCII form) + list all GUID-like strings.
$targets = @(
  @{f="C:\zcode_build\stripe\center\payload\VapeVolume-Rhino7.rhp"; g="C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01"},
  @{f="C:\zcode_build\stripe\center\payload\VapeVolume-Rhino8.rhp"; g="A3F27B54-9C41-4E88-B0D6-7E5C1A93D842"},
  @{f="C:\zcode_build\out-r7\VapeVolume.rhp";                        g="C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01"},
  @{f="C:\zcode_build\out-r8\VapeVolume.rhp";                        g="A3F27B54-9C41-4E88-B0D6-7E5C1A93D842"},
  @{f="C:\zcode_build\src\VapeVolume\obj\Release\net48\VapeVolume.rhp";          g="C4E17D63-8B52-4A19-9F3E-6D2B8A5C7E01"},
  @{f="C:\zcode_build\src\VapeVolume\obj\Release\net7.0-windows\VapeVolume.rhp"; g="A3F27B54-9C41-4E88-B0D6-7E5C1A93D842"},
  @{f="C:\zcode_build\stripe\center\payload\HalftoneDots-rh7.rhp";   g="9C4E7A21-3D58-4B06-8E97-2F1B6A3C5D08"},
  @{f="C:\zcode_build\stripe\center\payload\HalftoneDots-rh8.rhp";   g="4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27"},
  @{f="C:\zcode_build\stripe\center\payload\StripeOnSurface-rh7.rhp"; g="b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91"},
  @{f="C:\zcode_build\stripe\center\payload\StripeOnSurface-rh8.rhp"; g="b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91"},
  @{f="C:\Users\Administrator\AppData\Local\IVAN\plugins\VapeVolume\VapeVolume-Rhino8.rhp"; g="A3F27B54-9C41-4E88-B0D6-7E5C1A93D842"},
  @{f="C:\Users\Administrator\AppData\Local\IVAN\plugins\HalftoneDots\HalftoneDots-rh8.rhp"; g="4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27"}
)
$rx = [regex]'[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
foreach ($t in $targets) {
  if (-not (Test-Path $t.f)) { Write-Host ("MISSING : " + $t.f); continue }
  $bytes = [System.IO.File]::ReadAllBytes($t.f)
  $txt   = [System.Text.Encoding]::ASCII.GetString($bytes)
  $has   = $txt.ToLower().Contains($t.g.ToLower())
  $uniq  = $rx.Matches($txt) | ForEach-Object { $_.Value.ToLower() } | Sort-Object -Unique
  $list  = ($uniq | Select-Object -First 12) -join "  "
  $fi = Get-Item $t.f
  Write-Host ("FILE   : " + $t.f)
  Write-Host ("         size=" + $fi.Length + "  mtime=" + $fi.LastWriteTime)
  Write-Host ("         expect=" + $t.g.ToLower() + "  FOUND=" + $has)
  Write-Host ("         guids-in-file: " + $list)
  Write-Host ""
}
Write-Host "=== Rhino installs (HKCU/HKLM) ==="
foreach ($hive in @("HKCU:\Software\MCNeel\Rhinoceros","HKLM:\SOFTWARE\MCNeel\Rhinoceros")) {
  if (Test-Path $hive) {
    foreach ($v in (Get-ChildItem $hive).PSChildName) {
      $ip = "$hive\$v\Install"
      if (Test-Path $ip) {
        $p = Get-ItemProperty $ip -ErrorAction SilentlyContinue
        Write-Host ("  " + $hive + "\" + $v + "  ExePath=" + $p.ExePath + "  Path=" + $p.Path)
      }
    }
  }
}
