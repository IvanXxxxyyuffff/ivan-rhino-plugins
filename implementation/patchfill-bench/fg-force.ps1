Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class FG {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  public static void ForceForeground(IntPtr h) {
    uint fgPid; IntPtr fg = GetForegroundWindow();
    GetWindowThreadProcessId(fg, out fgPid);
    uint cur = GetCurrentThreadId();
    uint fgThread = GetWindowThreadProcessId(fg, out fgPid);
    AttachThreadInput(cur, fgThread, true);
    SetForegroundWindow(h);
    AttachThreadInput(cur, fgThread, false);
  }
}
"@
$p = Get-Process Rhino | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[FG]::ShowWindow($p.MainWindowHandle, 9) | Out-Null   # SW_RESTORE
Start-Sleep -Milliseconds 300
[FG]::ForceForeground($p.MainWindowHandle)
Start-Sleep -Milliseconds 400
'foreground forced'