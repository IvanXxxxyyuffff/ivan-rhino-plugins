# 启动 Rhino 等待工具条加载，整屏截图（缩放到 1280 宽）后关闭 Rhino
$exe = 'D:\Rhino 8\System\Rhino.exe'
$p = Start-Process -FilePath $exe -ArgumentList '/nosplash' -PassThru
Write-Host ("pid=" + $p.Id)

$deadline = (Get-Date).AddSeconds(150)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $p.Refresh()
    if ($p.HasExited) { Write-Host "rhino exited early"; break }
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
}
Write-Host "main window up, waiting for toolbars..."
Start-Sleep -Seconds 18

Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)

$maxW = 1280
if ($b.Width -gt $maxW) {
    $nh = [int]($b.Height * $maxW / $b.Width)
    $small = New-Object System.Drawing.Bitmap $maxW, $nh
    $g2 = [System.Drawing.Graphics]::FromImage($small)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.DrawImage($bmp, 0, 0, $maxW, $nh)
    $small.Save('C:\zcode_build\stripe\rhino_toolbar_check.png', [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("saved scaled " + $maxW + "x" + $nh)
} else {
    $bmp.Save('C:\zcode_build\stripe\rhino_toolbar_check.png', [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("saved " + $b.Width + "x" + $b.Height)
}

# 关闭 Rhino
try { $p.CloseMainWindow() | Out-Null } catch { }
$t2 = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $t2) { Start-Sleep -Seconds 2; $p.Refresh(); if ($p.HasExited) { break } }
$p.Refresh()
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; Write-Host "rhino killed" } else { Write-Host "rhino closed" }
Write-Host "DONE"
