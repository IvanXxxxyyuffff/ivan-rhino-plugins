param([int]$TargetPid = 0)
$src = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace W5 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L; public int T; public int R; public int B; }
  public static class U {
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  }
}
'@
Add-Type -TypeDefinition $src -Language CSharp
function ClassOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W5.U]::GetClassName($h,$sb,256); $sb.ToString() }
function TextOf([IntPtr]$h){ $sb=New-Object System.Text.StringBuilder 256; [void][W5.U]::GetWindowText($h,$sb,256); $sb.ToString() }
function Kids([IntPtr]$h){ $r=@(); $c=[W5.U]::GetWindow($h,5); while($c -ne [IntPtr]::Zero){ $r+=$c; $c=[W5.U]::GetWindow($c,2) }; $r }
if($TargetPid -eq 0){ $rp = Get-Process Rhino -EA SilentlyContinue | Select-Object -First 1; if($rp){ $TargetPid = $rp.Id } }
Write-Output ("target pid=" + $TargetPid)
if($TargetPid -eq 0){ Write-Output 'no rhino'; exit 1 }
$w=[W5.U]::GetTopWindow([IntPtr]::Zero)
$idx=0
while($w -ne [IntPtr]::Zero){
  $wpid=[uint32]0; [void][W5.U]::GetWindowThreadProcessId($w,[ref]$wpid)
  if($wpid -eq $TargetPid){
    $vis = [W5.U]::IsWindowVisible($w)
    $t=TextOf $w; $cl=ClassOf $w
    $r=New-Object W5.RECT; [void][W5.U]::GetWindowRect($w,[ref]$r)
    $idx++
    Write-Output ("TOP#$idx vis=$vis '$t' cls=$cl rect=($($r.L),$($r.T))-($($r.R),$($r.B)) kids=" + (Kids $w).Count)
    if($vis -and (Kids $w).Count -gt 0 -and $cl -notmatch 'IME|MSCTF'){
      foreach($k in (Kids $w)){
        $kc=ClassOf $k
        Write-Output ("  K1 '$(TextOf $k)' cls=$kc id=" + [W5.U]::GetDlgCtrlID($k) + " kids=" + (Kids $k).Count)
        if($kc -match 'Dock|XNurbs|Afx' -or $t -match 'XNurbs'){
          foreach($k2 in (Kids $k)){
            $k2c=ClassOf $k2
            $mark=''
            if($k2c -in @('ListBox','Button','ComboBox')){ $mark=' <<' }
            Write-Output ("    K2 '$(TextOf $k2)' cls=$k2c id=" + [W5.U]::GetDlgCtrlID($k2) + $mark)
          }
        }
      }
    }
  }
  $w=[W5.U]::GetWindow($w,2)
  if($idx -gt 300){ break }
}
Write-Output ("total tops matched: " + $idx)
