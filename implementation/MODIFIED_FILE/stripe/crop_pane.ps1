Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("C:\zcode_build\stripe\now_view2.jpg")
# 消息窗格大致区域（1920x1076 原图坐标）
$rect = New-Object System.Drawing.Rectangle 0, 26, 900, 60
$crop = New-Object System.Drawing.Bitmap $rect.Width, $rect.Height
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,$rect.Width,$rect.Height), $rect, [System.Drawing.GraphicsUnit]::Pixel)
# 放大 2 倍
$big = New-Object System.Drawing.Bitmap ($rect.Width*2), ($rect.Height*2)
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($crop, 0, 0, $rect.Width*2, $rect.Height*2)
$big.Save("C:\zcode_build\stripe\pane_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png)
$src.Dispose()
Write-Output "cropped"
