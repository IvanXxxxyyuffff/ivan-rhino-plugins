# Launch Rhino 8, wait for startup, enumerate its top-level windows (look for modal dialogs),
# screenshot the screen (scaled), dump IVAN registry keys + VapeVolume load log, then close Rhino.
$ErrorActionPreference = 'Continue'
$exe  = 'D:\Rhino 8\System\Rhino.exe'
$ivan = 'C:\Users\Administrator\AppData\Local\IVAN\plugins'
$root = 'HKCU:\Software\MCNeel\Rhinoceros'

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinEnum {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
  public static List<string> WindowsOf(uint targetPid) {
    var list = new List<string>();
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        var t = new StringBuilder(512); GetWindowTextW(h, t, 512);
        var c = new StringBuilder(256); GetClassNameW(h, c, 256);
        list.Add(c.ToString() + " | " + t.ToString());
      }
      return true;
    }, IntPtr.Zero);
    return list;
  }
}
'@

Write-Host "=== launching Rhino 8 ==="
$p = Start-Process -FilePath $exe -ArgumentList '/nosplash' -PassThru
Write-Host ("pid=" + $p.Id)

$deadline = (Get-Date).AddSeconds(150)
$mainHwnd = [IntPtr]::Zero
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $p.Refresh()
    if ($p.HasExited) { Write-Host "rhino exited early!"; break }
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $mainHwnd = $p.MainWindowHandle; break }
}
Write-Host ("main window handle: " + $mainHwnd)

# give plugins a few more seconds to load
Start-Sleep -Seconds 20

Write-Host ""
Write-Host "=== Rhino top-level visible windows (class | title) ==="
try {
    $wins = [WinEnum]::WindowsOf([uint32]$p.Id)
    foreach ($w in $wins) { Write-Host ("  " + $w) }
    $dlg = $wins | Where-Object { $_ -like '#32770*' }
    Write-Host ("DIALOG-WINDOWS: " + ($dlg | Measure-Object).Count)
} catch { Write-Host ("window enum failed: " + $_.Exception.Message) }

Write-Host ""
Write-Host "=== screenshot ==="
try {
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
        $small.Save('C:\zcode_build\stripe\rhino_verify.png', [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host ("saved scaled " + $maxW + "x" + $nh)
    } else {
        $bmp.Save('C:\zcode_build\stripe\rhino_verify.png', [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host ("saved " + $b.Width + "x" + $b.Height)
    }
} catch { Write-Host ("screenshot failed: " + $_.Exception.Message) }

Write-Host ""
Write-Host "=== registry IVAN keys after Rhino start ==="
foreach ($ver in (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
        $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
        if ([string]::IsNullOrEmpty($fn) -eq $false -and $fn.StartsWith($ivan, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Host ("  [" + $ver + "] " + $k.PSChildName + "  ->  " + $fn)
        }
    }
}

Write-Host ""
Write-Host "=== VapeVolume_load.log tail ==="
Get-Content "$env:TEMP\VapeVolume_load.log" -Tail 6 -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== closing Rhino ==="
try { $p.CloseMainWindow() | Out-Null } catch { }
$t2 = (Get-Date).AddSeconds(25)
while ((Get-Date) -lt $t2) {
    Start-Sleep -Seconds 2
    $p.Refresh()
    if ($p.HasExited) { break }
}
$p.Refresh()
if (-not $p.HasExited) { Write-Host "still running, Stop-Process"; Stop-Process -Id $p.Id -Force }
else { Write-Host "closed gracefully" }
Write-Host "DONE"
