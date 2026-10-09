Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.X, $b.Y, 0, 0, (New-Object System.Drawing.Size $b.Width, $b.Height))
# 裁到工具栏附近：屏幕 520..1000 x 250..420
$rect = New-Object System.Drawing.Rectangle 480, 240, 560, 200
$crop = New-Object System.Drawing.Bitmap ($rect.Width*2), ($rect.Height*2)
$g2 = [System.Drawing.Graphics]::FromImage($crop)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0,0,($rect.Width*2),($rect.Height*2)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$crop.Save("C:\zcode_build\stripe\toolbar_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "saved"
