$ErrorActionPreference = 'Stop'
$D = 'C:\zcode_tools\dotnet\dotnet.exe'
$C = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\patternemboss'
foreach ($tfm in @('Rhino8', 'Rhino7')) {
    $proj = Join-Path $C ($tfm + '\PatternEmboss.csproj')
    $o = & $D build $proj -c Release --nologo -v minimal 2>&1
    $e = $o | Select-String ' error '
    if ($e) { $e | Select-Object -First 8 | ForEach-Object { Write-Output $_.Line }; exit 1 }
    Write-Output ('BUILT ' + $tfm)
}
# 装到安装目录（dev 链路）
$inst = Join-Path $env:LOCALAPPDATA 'IVAN\plugins\PatternEmboss'
Copy-Item (Join-Path $C 'out\rhino8\PatternEmboss.rhp') (Join-Path $inst 'rh8\PatternEmboss.rhp') -Force
Copy-Item (Join-Path $C 'out\rhino7\PatternEmboss.rhp') (Join-Path $inst 'rh7\PatternEmboss-rh7.rhp') -Force
Write-Output 'INSTALLED'
