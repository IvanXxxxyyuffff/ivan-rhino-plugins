param([string]$cmd = "-StripeSelfTest")
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class W4 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$p = Get-Process -Name Rhino -ErrorAction SilentlyContinue | Select-Object -First 1
if ($p -eq $null) { Write-Output "no rhino"; exit 1 }
$h = $p.MainWindowHandle
[W4]::ShowWindow($h, 3) | Out-Null      # maximize
Start-Sleep -Milliseconds 800
[W4]::ShowWindow($h, 9) | Out-Null      # restore
[Microsoft.VisualBasic.Interaction]::AppActivate($p.Id) | Out-Null
Start-Sleep -Seconds 1
[W4]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1200
Write-Output ("fg=" + [W4]::GetForegroundWindow() + "  hwnd=" + $h)
[System.Windows.Forms.SendKeys]::SendWait($cmd)
Start-Sleep -Milliseconds 900
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Write-Output "sent"
