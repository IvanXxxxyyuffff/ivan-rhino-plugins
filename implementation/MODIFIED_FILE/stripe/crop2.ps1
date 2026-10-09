Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("C:\zcode_build\stripe\now_view2.jpg")
$rect = New-Object System.Drawing.Rectangle 620, 190, 130, 100
$crop = New-Object System.Drawing.Bitmap ($rect.Width*6), ($rect.Height*6)
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,($rect.Width*6),($rect.Height*6)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$crop.Save("C:\zcode_build\stripe\corner_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png)
$src.Dispose()
Write-Output "ok"
