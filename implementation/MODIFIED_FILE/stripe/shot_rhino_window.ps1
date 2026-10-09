Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@

$exe = 'D:\Rhino 8\System\Rhino.exe'
$p = Start-Process -FilePath $exe -ArgumentList '/nosplash' -PassThru
Write-Host ("pid=" + $p.Id)

$deadline = (Get-Date).AddSeconds(150)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $p.Refresh()
    if ($p.HasExited) { Write-Host "exited early"; break }
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
}
Start-Sleep -Seconds 20
$p.Refresh()
$h = $p.MainWindowHandle
Write-Host ("hwnd=" + $h)

[Win]::ShowWindow($h, 9) | Out-Null
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds 3

$r = New-Object 'Win+RECT'
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
Write-Host ("rect=" + $r.Left + "," + $r.Top + " " + $w + "x" + $ht)

Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))

$maxW = 1280
if ($w -gt $maxW) {
    $nh = [int]($ht * $maxW / $w)
    $small = New-Object System.Drawing.Bitmap $maxW, $nh
    $g2 = [System.Drawing.Graphics]::FromImage($small)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.DrawImage($bmp, 0, 0, $maxW, $nh)
    $small.Save('C:\zcode_build\stripe\rhino_window.png', [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("saved " + $maxW + "x" + $nh)
} else {
    $bmp.Save('C:\zcode_build\stripe\rhino_window.png', [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("saved " + $w + "x" + $ht)
}

try { $p.CloseMainWindow() | Out-Null } catch { }
$t2 = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $t2) { Start-Sleep -Seconds 2; $p.Refresh(); if ($p.HasExited) { break } }
$p.Refresh()
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; Write-Host "killed" } else { Write-Host "closed" }
Write-Host "DONE"
