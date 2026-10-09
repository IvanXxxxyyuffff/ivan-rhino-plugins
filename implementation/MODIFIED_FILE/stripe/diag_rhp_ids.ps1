# Inspect the deployed .rhp assemblies: find PlugIn-derived classes and their [Guid] attributes.
# net48 files load directly; net7 files use reflection-only.
$files = @(
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\HalftoneDots\HalftoneDots-rh7.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\HalftoneDots\HalftoneDots-rh8.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\StripeOnSurface\StripeOnSurface-rh7.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\StripeOnSurface\StripeOnSurface-rh8.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\VapeVolume\VapeVolume-Rhino7.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\VapeVolume\VapeVolume-Rhino8.rhp'
)

foreach ($f in $files) {
    Write-Host ("=== " + $f)
    if (-not (Test-Path $f)) { Write-Host "  MISSING"; continue }
    $asm = $null
    try { $asm = [Reflection.Assembly]::ReflectionOnlyLoadFrom($f) }
    catch {
        try { $asm = [Reflection.Assembly]::LoadFile($f) } catch { Write-Host ("  LOAD FAILED: " + $_.Exception.Message); continue }
    }
    $types = $null
    try { $types = $asm.GetTypes() }
    catch [Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }
    catch { Write-Host ("  GetTypes FAILED: " + $_.Exception.Message); continue }

    Write-Host ("  runtime: " + $asm.ImageRuntimeVersion + "  types: " + $types.Count)
    foreach ($t in $types) {
        $baseName = ''
        try { if ($t.BaseType) { $baseName = $t.BaseType.FullName } } catch { $baseName = '(base unreadable)' }
        $isPlugin = $baseName -like '*Rhino.PlugIns.PlugIn*' -or $baseName -like '*PlugIn*'
        $attrs = @()
        try { $attrs = [System.Reflection.CustomAttributeData]::GetCustomAttributes($t) } catch { }
        $guidAttr = $attrs | Where-Object { $_.AttributeType.FullName -like '*GuidAttribute*' }
        if ($isPlugin -or $guidAttr) {
            Write-Host ("    TYPE " + $t.FullName + "  base=" + $baseName)
            if ($guidAttr) {
                foreach ($a in $guidAttr) {
                    $argTxt = ($a.ConstructorArguments | ForEach-Object { [string]$_.Value }) -join ', '
                    Write-Host ("         [Guid] = " + $argTxt)
                }
            } else {
                Write-Host "         [Guid] = (none)"
            }
        }
    }
}

Write-Host ""
Write-Host "=== VapeVolume_load.log tail ==="
Get-Content "$env:TEMP\VapeVolume_load.log" -Tail 8 -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== stock plugin key for comparison: solidtools (HKCU 8.0) ==="
$sk = 'HKCU:\Software\MCNeel\Rhinoceros\8.0\Plug-Ins\01eb3d37-d856-4e8a-8d41-c2deb7c8b4bb'
if (Test-Path $sk) {
    $p = Get-ItemProperty $sk
    foreach ($prop in $p.PSObject.Properties) { if ($prop.Name -notlike 'PS*') { Write-Host ("    " + $prop.Name + " = " + $prop.Value) } }
    $ck = Get-Item "$sk\CommandList" -ErrorAction SilentlyContinue
    if ($ck) { foreach ($v in $ck.GetValueNames()) { Write-Host ("    CommandList." + $v + " = " + $ck.GetValue($v)) } }
}
