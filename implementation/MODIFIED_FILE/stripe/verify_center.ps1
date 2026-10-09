$p = 'C:\zcode_build\stripe\out\center\IVAN-CENTER.exe'
$b = [System.IO.File]::ReadAllBytes($p)
$ascii = [System.Text.Encoding]::ASCII.GetString($b)
Write-Host ("ascii scan RhpContainsGuid : " + $ascii.Contains('RhpContainsGuid'))
Write-Host ("ascii scan CleanStaleZeroKey: " + $ascii.Contains('CleanStaleZeroKey'))

$a = [Reflection.Assembly]::LoadFile($p)
$t = $a.GetType('IvanCenter.Installer')
Write-Host ("type IvanCenter.Installer loaded: " + ($t -ne $null))
if ($t -ne $null) {
    $m1 = $t.GetMethod('RhpContainsGuid', [Reflection.BindingFlags]'Public,Static')
    $m2 = $t.GetMethod('CleanStaleZeroKey', [Reflection.BindingFlags]'NonPublic,Instance')
    $m3 = $t.GetMethod('RemoveAllRegistrationsFor', [Reflection.BindingFlags]'Public,Instance')
    Write-Host ("method RhpContainsGuid       : " + ($m1 -ne $null))
    Write-Host ("method CleanStaleZeroKey     : " + ($m2 -ne $null))
    Write-Host ("method RemoveAllRegistrationsFor: " + ($m3 -ne $null))
}
