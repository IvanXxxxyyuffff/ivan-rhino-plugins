Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WinEnum {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  public static List<string> List() {
    var r = new List<string>();
    EnumWindows((h, l) => {
      if (IsWindowVisible(h)) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        uint pid; GetWindowThreadProcessId(h, out pid);
        if (sb.Length > 0) r.Add(pid + "|" + sb);
      }
      return true;
    }, IntPtr.Zero);
    return r;
  }
}
"@
$rhinoPid = (Get-Process Rhino | Select-Object -First 1).Id
$pwshPid = 35952
foreach ($line in [WinEnum]::List()) {
  $pid0 = [uint32]($line.Split('|')[0])
  $title = $line.Split('|')[1]
  if ($pid0 -eq $rhinoPid -or $pid0 -eq $pwshPid) { Write-Output ("WIN: $pid0 | $title") }
}
'done'