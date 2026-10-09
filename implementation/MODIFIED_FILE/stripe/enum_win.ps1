Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WE {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
  delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  static List<string> _out;
  static uint _pid;
  static void Add(IntPtr h) {
    uint pid; GetWindowThreadProcessId(h, out pid);
    if (pid != _pid) return;
    var t = new StringBuilder(256); GetWindowTextW(h, t, 256);
    var c = new StringBuilder(256); GetClassNameW(h, c, 256);
    RECT r; GetWindowRect(h, out r);
    _out.Add(string.Format("{0} cls={1} vis={2} rect={3},{4},{5},{6} title={7}",
      h.ToString(), c.ToString(), IsWindowVisible(h), r.L, r.T, r.R, r.B, t.ToString()));
  }
  public static List<string> Run(uint pid) {
    _pid = pid; _out = new List<string>();
    EnumWindows((h,p) => { Add(h); EnumChildWindows(h, (h2,p2) => { Add(h2); return true; }, IntPtr.Zero); return true; }, IntPtr.Zero);
    return _out;
  }
}
'@
$p = Get-Process -Name Rhino -ErrorAction SilentlyContinue | Select-Object -First 1
if ($p -eq $null) { Write-Output "no rhino"; exit }
[WE]::Run([uint32]$p.Id) | Where-Object { $_ -match "IVAN|ToolBar|Toolbar|vis=True" } | Select-Object -First 40
