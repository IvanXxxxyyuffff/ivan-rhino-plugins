Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
Add-Type -AssemblyName System.Windows.Forms

$exe = "D:\Rhino 8\System\Rhino.exe"
$p = Start-Process -FilePath $exe -ArgumentList @('/nosplash') -PassThru
Write-Output ("launched pid " + $p.Id)
Start-Sleep -Seconds 55
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output "no main window"; exit 1 }
[Win]::ShowWindow($h, 9) | Out-Null
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1500
[System.Windows.Forms.SendKeys]::SendWait("-StripeSelfTest")
Start-Sleep -Milliseconds 800
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Write-Output "keys sent"
Start-Sleep -Seconds 40
Write-Output ("rhino alive: " + (-not $p.HasExited))
