# Controlled Rhino startup experiment: capture every plugin-load dialog, its text and timing.
$ErrorActionPreference = 'Continue'

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
  public static List<string> DlgTexts(uint pid) {
    var res = new List<string>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) {
        var c = new StringBuilder(256); GetClassNameW(h, c, 256);
        if (c.ToString() == "#32770") {
          var sb = new StringBuilder();
          EnumChildWindows(h, (ch, l2) => {
            var cc = new StringBuilder(64); GetClassNameW(ch, cc, 64);
            var t = new StringBuilder(2048); GetWindowTextW(ch, t, 2048);
            if (t.Length > 0) sb.Append("[" + cc.ToString() + "]" + t.ToString() + " || ");
            return true;
          }, IntPtr.Zero);
          res.Add(sb.ToString());
        }
      }
      return true;
    }, IntPtr.Zero);
    return res;
  }
  public static int CloseDialogs(uint pid) {
    int n = 0;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) {
        var c = new StringBuilder(256); GetClassNameW(h, c, 256);
        if (c.ToString() == "#32770") { PostMessageW(h, 0x0010, IntPtr.Zero, IntPtr.Zero); n++; }
      }
      return true;
    }, IntPtr.Zero);
    return n;
  }
}
'@

function Dump-IvanKeys($tag) {
  Write-Host ("--- registry IVAN keys [" + $tag + "] ---")
  $root = 'HKCU:\Software\MCNeel\Rhinoceros'
  $ivan = 'C:\Users\Administrator\AppData\Local\IVAN\plugins'
  foreach ($ver in (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
      $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
      if ([string]::IsNullOrEmpty($fn) -eq $false -and ($fn.StartsWith($ivan, [System.StringComparison]::OrdinalIgnoreCase) -or $k.PSChildName -ieq '00000000-0000-0000-0000-000000000000')) {
        Write-Host ("  [" + $ver + "] " + $k.PSChildName + "  ->  " + $fn)
      }
    }
  }
}

# ---- 1) enable Rhino debug logging
$dl = 'HKCU:\Software\MCNeel\Rhinoceros\8.0\Global Options\Debug Logging'
Set-ItemProperty -Path $dl -Name 'Enabled'   -Value 1 -Type DWord
Set-ItemProperty -Path $dl -Name 'SaveTofile' -Value 1 -Type DWord
Write-Host "rhino debug logging enabled"

Dump-IvanKeys "BEFORE"

# ---- 2) launch Rhino and capture dialogs
$p = Start-Process -FilePath 'D:\Rhino 8\System\Rhino.exe' -ArgumentList '/nosplash' -PassThru
Write-Host ("rhino pid=" + $p.Id)
$t0 = Get-Date
$seen = @{}
$deadline = $t0.AddSeconds(150)
$mainSeen = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $p.Refresh()
    if ($p.HasExited) { Write-Host ("rhino exited at +" + [int]((Get-Date)-$t0).TotalSeconds + "s"); break }
    if (-not $mainSeen -and $p.MainWindowHandle -ne [IntPtr]::Zero) { $mainSeen = $true; Write-Host ("main window at +" + [int]((Get-Date)-$t0).TotalSeconds + "s") }
    $txts = [W]::DlgTexts([uint32]$p.Id)
    foreach ($t in $txts) {
        $el = [int]((Get-Date) - $t0).TotalSeconds
        $key = $t
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = $true
            Write-Host ("[+" + $el + "s] DIALOG: " + $t)
        }
    }
    if ($txts.Count -gt 0) { [void][W]::CloseDialogs([uint32]$p.Id) }
    if ($mainSeen -and ((Get-Date) - $t0).TotalSeconds -gt 75) { break }
}
Write-Host ("total dialogs captured: " + $seen.Count)

# ---- 3) snapshots
Start-Sleep -Seconds 3
Dump-IvanKeys "AFTER"

Write-Host "--- VapeVolume_load.log tail ---"
Get-Content "$env:TEMP\VapeVolume_load.log" -Tail 4 -ErrorAction SilentlyContinue

Write-Host "--- telemetry LoadLog (last entries) ---"
$sf = "$env:APPDATA\McNeel\Rhinoceros\8.0\settings\settings-Scheme__Default.xml"
if (Test-Path $sf) {
    $c = [System.IO.File]::ReadAllText($sf)
    $m = [regex]::Match($c, '(?s)<entry key="Telemetry\.Plugins\.LoadLog">(.*?)</entry>')
    if ($m.Success) { Write-Host $m.Groups[1].Value.Trim() }
}

Write-Host "--- rhino debug log candidates ---"
foreach ($cand in @("$env:TEMP\RhinoDebugLog.txt", "$env:USERPROFILE\Desktop\RhinoDebugLog.txt", "C:\Users\Administrator\Desktop\RhinoDebugLog.txt", "$env:LOCALAPPDATA\Temp\RhinoDebugLog.txt")) {
    if (Test-Path $cand) {
        $fi = Get-Item $cand
        Write-Host ("FOUND " + $cand + "  size=" + $fi.Length + "  mtime=" + $fi.LastWriteTime)
    }
}

# ---- 4) close rhino
try { $p.CloseMainWindow() | Out-Null } catch { }
$t2 = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $t2) { Start-Sleep -Seconds 2; $p.Refresh(); if ($p.HasExited) { break } }
$p.Refresh()
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; Write-Host "rhino killed" } else { Write-Host "rhino closed" }

# ---- 5) restore debug logging off
Set-ItemProperty -Path $dl -Name 'Enabled'   -Value 0 -Type DWord
Set-ItemProperty -Path $dl -Name 'SaveTofile' -Value 0 -Type DWord
Write-Host "debug logging restored to off"
Write-Host "DONE"
