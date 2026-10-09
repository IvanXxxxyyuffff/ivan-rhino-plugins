$ErrorActionPreference = 'Stop'
$guid = '5D2A9F41-7C63-4E18-B095-8A4F1D6C3E72'
$root = Join-Path $env:LOCALAPPDATA 'IVAN\plugins\VoronoiTexture'
$rh8  = Join-Path $root 'rh8'
$rh7  = Join-Path $root 'rh7'
New-Item -ItemType Directory -Force -Path $rh8 | Out-Null
New-Item -ItemType Directory -Force -Path $rh7 | Out-Null
Copy-Item 'C:\zcode_build\voronoi\out\rhino8\VoronoiTexture.rhp' (Join-Path $rh8 'VoronoiTexture.rhp') -Force
Copy-Item 'C:\zcode_build\voronoi\out\rhino7\VoronoiTexture.rhp' (Join-Path $rh7 'VoronoiTexture-rh7.rhp') -Force

$key = "Software\MCNeel\Rhinoceros\8.0\Plug-Ins\$guid"
$k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($key)
$k.SetValue('Name', '泰森多边形纹 (VoronoiTexture)')
$k.SetValue('EnglishName', 'VoronoiTexture')
$k.SetValue('Organization', 'IVAN')
$k.SetValue('AddToHelpMenu', 0, 'DWord')
$k.SetValue('LoadMode', 1, 'DWord')
$k.SetValue('Type', 16, 'DWord')
$k.SetValue('IsDotNETPlugIn', 1, 'DWord')
$k.SetValue('DirectoryInstall', 0, 'DWord')
$k.SetValue('RegPath', '\\HKEY_CURRENT_USER\' + $key)
$pk = $k.CreateSubKey('PlugIn')
$pk.SetValue('FileName', (Join-Path $rh8 'VoronoiTexture.rhp'))
$ck = $k.CreateSubKey('CommandList')
$ck.SetValue('VoronoiTexture', '2;VoronoiTexture')
$ck.SetValue('VoronoiSelfTest', '2;VoronoiSelfTest')
$ck.Close(); $pk.Close(); $k.Close()

$logDir = Join-Path $env:LOCALAPPDATA 'IVAN\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$report = Join-Path $logDir 'VoronoiSelfTest.txt'
if (Test-Path $report) { Remove-Item $report -Force }
Set-Content -Path (Join-Path $logDir 'run-voronoi-selftest.flag') -Value 'go' -Encoding ASCII

Write-Output "registered: $key"
Write-Output "rhp: $(Join-Path $rh8 'VoronoiTexture.rhp') exists=$([IO.File]::Exists((Join-Path $rh8 'VoronoiTexture.rhp')))"
Write-Output "flag: $([IO.File]::Exists((Join-Path $logDir 'run-voronoi-selftest.flag')))"
Write-Output "report exists(before): $([IO.File]::Exists($report))"
