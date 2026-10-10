$ErrorActionPreference = 'Stop'
$root = 'E:\IVAN-LiquidGlass-preview\implementation'
$iconmake = Join-Path $root 'MODIFIED_FILE\iconmake'
$D = 'C:\zcode_tools\dotnet\dotnet.exe'
$out = & $D build (Join-Path $iconmake 'iconmake.csproj') -c Release --nologo -v minimal 2>&1
$out | Select-String -Pattern 'error|已成功|错误' | ForEach-Object { $_.Line }
$exe = Join-Path $iconmake 'bin\Release\net48\iconmake.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到 $exe" }

$kinds = @('stripe','vape','halftone','voronoi','radialdots','meshfix','diamond','ripple','unify')
foreach ($k in $kinds) {
  & $exe --kind $k (Join-Path $root ("ICONS-$k.png")) | Out-Null
}

Add-Type -AssemblyName System.Drawing
$cell = 128; $label = 22
$strip = New-Object System.Drawing.Bitmap(($cell * $kinds.Count), ($cell + $label))
$g = [System.Drawing.Graphics]::FromImage($strip)
$g.Clear([System.Drawing.Color]::FromArgb(255, 246, 248, 250))
$g.SmoothingMode = 'AntiAlias'
$font = New-Object System.Drawing.Font('Segoe UI', 10)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 40, 50, 60))
for ($i = 0; $i -lt $kinds.Count; $i++) {
  $img = [System.Drawing.Image]::FromFile((Join-Path $root ("ICONS-$($kinds[$i]).png")))
  $g.DrawImage($img, ($i * $cell), 0, $cell, $cell)
  $img.Dispose()
  $g.DrawString($kinds[$i], $font, $brush, ($i * $cell + 8), ($cell + 2))
}
$g.Dispose()
$stripPath = Join-Path $root 'ICONS-strip.png'
$strip.Save($stripPath, [System.Drawing.Imaging.ImageFormat]::Png)
$strip.Dispose()
Write-Output ("对比图：{0} bytes → {1}" -f (Get-Item $stripPath).Length, $stripPath)
