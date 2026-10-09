$guids = @{
  'Grasshopper'  = 'b45a29b1-4343-4035-989e-044e8580d9cf'
  'IronPython'   = '814d908a-e25c-493d-97e9-ee3861957f49'
  'RhinoScript'  = '1c7a3523-9a8f-4cec-a8e0-310f580536a7'
  'SolidTools'   = '01eb3d37-d856-4e8a-8d41-c2deb7c8b4bb'
  'HalftoneDots' = '4B8D2E63-7A19-4C5F-91B2-6E3A8D0C4F27'
  'VapeVolume'   = 'A3F27B54-9C41-4E88-B0D6-7E5C1A93D842'
  'StripeOnSurface' = 'b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91'
}
$base = 'HKCU:\Software\MCNeel\Rhinoceros\8.0\Plug-Ins'
foreach ($name in $guids.Keys) {
  $k = "$base\$($guids[$name])"
  Write-Host ("=== " + $name)
  if (-not (Test-Path $k)) { Write-Host "   (absent)"; continue }
  $p = Get-ItemProperty $k -ErrorAction SilentlyContinue
  Write-Host ("   Type=" + $p.Type + "  IsDotNET=" + $p.IsDotNETPlugIn + "  LoadMode=" + $p.LoadMode + "  DirInstall=" + $p.DirectoryInstall)
  $fn = (Get-ItemProperty "$k\PlugIn" -ErrorAction SilentlyContinue).FileName
  Write-Host ("   FileName=" + $fn)
  $cl = Get-Item "$k\CommandList" -ErrorAction SilentlyContinue
  if ($cl) {
    $names = $cl.GetValueNames()
    $sample = @()
    foreach ($n in $names) { if ($sample.Count -lt 3) { $sample += ($n + '=' + $cl.GetValue($n)) } }
    Write-Host ("   CommandList count=" + $names.Count + "  sample: " + ($sample -join ' | '))
  }
  Write-Host ""
}
