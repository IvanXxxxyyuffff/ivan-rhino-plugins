$ErrorActionPreference = 'Stop'
$C = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE'
$P = Join-Path $C 'stripe\center\payload'
$D = 'C:\zcode_tools\dotnet\dotnet.exe'
$map = @(
  @{ Folder='voronoi'; Name='VoronoiTexture'; Rh7='VoronoiTexture.rhp' },
  @{ Folder='stripe'; Name='StripeOnSurface'; Rh7='StripeOnSurface.rhp' },
  @{ Folder='halftone'; Name='HalftoneDots'; Rh7='HalftoneDots.rhp' },
  @{ Folder='radialdots'; Name='RadialDots'; Rh7='RadialDots.rhp' },
  @{ Folder='meshfix'; Name='MeshFix'; Rh7='MeshFix.rhp' },
  @{ Folder='diamondfacet'; Name='DiamondFacet'; Rh7='DiamondFacet.rhp' }
)
foreach ($m in $map) {
  $base = Join-Path $C $m.Folder
  Copy-Item -LiteralPath (Join-Path $base "out\rhino7\$($m.Rh7)") -Destination (Join-Path $P "$($m.Name)-rh7.rhp") -Force
  Copy-Item -LiteralPath (Join-Path $base "out\rhino8\$($m.Name).rhp") -Destination (Join-Path $P "$($m.Name)-rh8.rhp") -Force
  foreach ($sfx in @('.dll', '.deps.json', '.runtimeconfig.json')) {
    $src = Join-Path $base "out\rhino8\$($m.Name)$sfx"
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $P "$($m.Name)$sfx") -Force }
  }
}
$vb = Join-Path $C 'src\VapeVolume\bin\Release'
Copy-Item -LiteralPath (Join-Path $vb 'net48\VapeVolume.rhp') -Destination (Join-Path $P 'VapeVolume-Rhino7.rhp') -Force
Copy-Item -LiteralPath (Join-Path $vb 'net7.0-windows\VapeVolume.rhp') -Destination (Join-Path $P 'VapeVolume-Rhino8.rhp') -Force
foreach ($sfx in @('.dll', '.deps.json', '.runtimeconfig.json')) {
  $src = Join-Path $vb "net7.0-windows\VapeVolume$sfx"
  if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $P "VapeVolume$sfx") -Force }
}
Write-Output ("payload refreshed (VoronoiTexture rh8={0})" -f (Get-Item (Join-Path $P 'VoronoiTexture-rh8.rhp')).Length)
$out = & $D build (Join-Path $C 'stripe\center\Center.csproj') -c Release -v minimal --nologo 2>&1
$out | Select-String -Pattern 'error|已成功|错误' | ForEach-Object { $_.Line }
$exe = Join-Path $C 'stripe\out\center\IVAN-CENTER.exe'
Write-Output ("center: {0} bytes  md5 {1}" -f (Get-Item $exe).Length, (Get-FileHash -LiteralPath $exe -Algorithm MD5).Hash.ToLower())
