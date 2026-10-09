Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
}
'@
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$p = Get-Process -Name Rhino -ErrorAction SilentlyContinue | Select-Object -First 1
if ($p -eq $null) { Write-Output "no rhino"; return }
$h = $p.MainWindowHandle
[Win]::ShowWindow($h, 9) | Out-Null
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1500
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save("C:\zcode_build\stripe\rhino_shot.png", [System.Drawing.Imaging.ImageFormat]::Png)
$pt = $p.MainWindowHandle
$rect = New-Object System.Drawing.Rectangle 700, 1150, 1800, 220
$crop = $bmp.Clone($rect, $bmp.PixelFormat)
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$ep = New-Object System.Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, 88L)
$crop.Save("C:\zcode_build\stripe\rhino_cli.jpg", $codec, $ep)
Write-Output ("ok " + $b.Width + "x" + $b.Height)
