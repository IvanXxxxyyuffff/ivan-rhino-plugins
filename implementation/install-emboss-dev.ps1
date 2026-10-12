$ErrorActionPreference = 'Stop'
$C = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\patternemboss'
$inst = Join-Path $env:LOCALAPPDATA 'IVAN\plugins\PatternEmboss'
New-Item -ItemType Directory -Force -Path (Join-Path $inst 'rh8') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $inst 'rh7') | Out-Null
$path8 = Join-Path $inst 'rh8\PatternEmboss.rhp'
$path7 = Join-Path $inst 'rh7\PatternEmboss.rhp'
Copy-Item (Join-Path $C 'out\rhino8\PatternEmboss.rhp') $path8 -Force
Copy-Item (Join-Path $C 'out\rhino7\PatternEmboss.rhp') $path7 -Force
Write-Output ("installed rh8=" + (Get-Item $path8).Length + " rh7=" + (Get-Item $path7).Length)

function RegPlugin([string]$major, [string]$guid, [string]$file, [string]$enName) {
    $key = "Software\MCNeel\Rhinoceros\$major.0\Plug-Ins\$guid"
    $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($key)
    $k.SetValue('Name', $enName, 'String')
    $k.SetValue('EnglishName', $enName, 'String')
    $k.SetValue('Organization', 'IVAN', 'String')
    $k.SetValue('AddToHelpMenu', 0, 'DWord')
    $k.SetValue('LoadMode', 1, 'DWord')
    $k.SetValue('Type', 16, 'DWord')
    $k.SetValue('IsDotNETPlugIn', 1, 'DWord')
    $k.SetValue('DirectoryInstall', 0, 'DWord')
    $k.SetValue('RegPath', "\\HKEY_CURRENT_USER\$key", 'String')
    $pk = $k.CreateSubKey('PlugIn')
    $pk.SetValue('FileName', $file, 'String')
    $k.Close()
    Write-Output ("registered $major $guid -> $file")
}
RegPlugin '8' '719FCC1D-E8AE-4B5F-AE71-AE3DBF6699EA' $path8 'PatternEmboss'
RegPlugin '7' 'FD17B96E-7D68-4E36-AF3D-D24837F3F8BF' $path7 'PatternEmboss'
Write-Output 'REGISTRY OK'
