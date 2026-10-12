$ErrorActionPreference = 'Stop'
$D = 'C:\zcode_tools\dotnet\dotnet.exe'
$C = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE'
$names = @('voronoi', 'stripe', 'halftone', 'radialdots', 'meshfix', 'diamondfacet', 'waterripple', 'surfaceunify', 'patternemboss')
$fail = 0
foreach ($n in $names) {
    foreach ($tfm in @('Rhino7', 'Rhino8')) {
        $projs = Get-ChildItem -Path (Join-Path $C (Join-Path $n $tfm)) -Filter '*.csproj' -EA SilentlyContinue
        foreach ($pr in $projs) {
            $o = & $D build $pr.FullName -c Release --nologo -v minimal 2>&1
            $e = $o | Select-String ' error '
            if ($e) { Write-Output ('FAIL ' + $n + '/' + $tfm); $e | Select-Object -First 3 | ForEach-Object { Write-Output $_.Line }; $fail = 1 }
            else { Write-Output ('OK   ' + $n + '/' + $tfm) }
        }
    }
}
# VapeVolume（双 TFM 单工程）
$pr = Get-ChildItem -Path (Join-Path $C 'src\VapeVolume') -Filter '*.csproj' | Select-Object -First 1
$o = & $D build $pr.FullName -c Release --nologo -v minimal 2>&1
if ($o | Select-String ' error ') { Write-Output 'FAIL VapeVolume'; $o | Select-String ' error ' | Select-Object -First 3 | ForEach-Object { Write-Output $_.Line }; $fail = 1 }
else { Write-Output 'OK   VapeVolume' }
if ($fail -ne 0) { exit 1 }
# payload + 安装器
& pwsh -NoProfile -File 'E:\IVAN-LiquidGlass-preview\implementation\refresh-payload-and-center.ps1' 2>&1 | Select-Object -Last 2 | ForEach-Object { Write-Output $_ }
Write-Output 'BUILD-ALL DONE'
