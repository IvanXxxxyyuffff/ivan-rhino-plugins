Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class W2 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
$p = Get-Process -Id 22052 -ErrorAction SilentlyContinue
if ($p -eq $null) { Write-Output "no process"; exit }
$h = $p.MainWindowHandle
Write-Output ("hwnd=" + $h + " title=" + $p.MainWindowTitle)
[W2]::ShowWindow($h, 9) | Out-Null
[W2]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds 2
$r = New-Object W2+RECT
[W2]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
Write-Output ("rect=" + $w + "x" + $hh)
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
$sc = [Math]::Min(1280.0 / $w, 1.0)
$nw = [int]($w * $sc); $nh = [int]($hh * $sc)
$small = New-Object System.Drawing.Bitmap $nw, $nh
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($bmp, 0, 0, $nw, $nh)
$small.Save("C:\zcode_build\stripe\now_view.jpg", [System.Drawing.Imaging.ImageFormat]::Jpeg)
Write-Output "saved"
