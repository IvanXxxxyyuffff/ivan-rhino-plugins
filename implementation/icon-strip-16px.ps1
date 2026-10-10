$ErrorActionPreference = 'Stop'
$root = 'E:\IVAN-LiquidGlass-preview\implementation'
$kinds = @('stripe','vape','halftone','voronoi','radialdots','meshfix','diamond','ripple','unify','patchfill')
Add-Type -AssemblyName System.Drawing
$cell = 128; $label = 22; $small = 16; $zoom = 5
$strip = New-Object System.Drawing.Bitmap(($cell * $kinds.Count), ($cell + $label + $small * $zoom + $label))
$g = [System.Drawing.Graphics]::FromImage($strip)
$g.Clear([System.Drawing.Color]::FromArgb(255, 246, 248, 250))
$g.SmoothingMode = 'AntiAlias'
$font = New-Object System.Drawing.Font('Segoe UI', 10)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 40, 50, 60))
$ySmall = $cell + $label
for ($i = 0; $i -lt $kinds.Count; $i++) {
  $img = [System.Drawing.Image]::FromFile((Join-Path $root ("ICONS-$($kinds[$i]).png")))
  $g.DrawImage($img, ($i * $cell), 0, $cell, $cell)
  $g.DrawString($kinds[$i], $font, $brush, ($i * $cell + 8), ($cell + 2))
  $img.Dispose()
}
# 第二行：16px 真实工具条尺寸（放大 5 倍最近邻看清像素）
for ($i = 0; $i -lt $kinds.Count; $i++) {
  $img = [System.Drawing.Image]::FromFile((Join-Path $root ("ICONS-$($kinds[$i]).png")))
  $tiny = New-Object System.Drawing.Bitmap($small, $small)
  $gt = [System.Drawing.Graphics]::FromImage($tiny)
  $gt.InterpolationMode = 'HighQualityBicubic'
  $gt.DrawImage($img, 0, 0, $small, $small)
  $gt.Dispose(); $img.Dispose()
  $g.InterpolationMode = 'NearestNeighbor'
  $g.PixelOffsetMode = 'Half'
  $g.DrawImage($tiny, ($i * $cell + 8), $ySmall, ($small * $zoom), ($small * $zoom))
  $g.InterpolationMode = 'HighQualityBicubic'
  $tiny.Dispose()
}
$g.DrawString('16px 工具条尺寸（放大 5 倍）', $font, $brush, 8, ($ySmall + $small * $zoom + 2))
$g.Dispose()
$out = Join-Path $root 'ICONS-strip-16.png'
$strip.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$strip.Dispose()
Write-Output ("16px 对比图：{0} bytes → {1}" -f (Get-Item $out).Length, $out)
