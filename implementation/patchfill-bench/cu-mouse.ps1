param([string]$Action, [int]$X = 0, [int]$Y = 0, [int]$RhinoPid = 0)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Mouse2 {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  public const uint LEFTDOWN = 0x0002; public const uint LEFTUP = 0x0004;
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(120);
    mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60);
    mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
  }
}
"@
if ($Action -eq 'activate') {
  $p = Get-Process -Id $RhinoPid
  [Mouse2]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 400
  'activated'
} elseif ($Action -eq 'click') {
  [Mouse2]::Click($X, $Y)
  'clicked'
}
