$sdkDir = 'C:\zcode_tools\dotnet\sdk\8.0.425\Sdks\Microsoft.NET.Sdk\tools\net472'
Add-Type -Path "$sdkDir\System.Collections.Immutable.dll"
Add-Type -Path "$sdkDir\System.Reflection.Metadata.dll"

$files = @(
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\HalftoneDots\HalftoneDots-rh8.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\StripeOnSurface\StripeOnSurface-rh8.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\VapeVolume\VapeVolume-Rhino8.rhp',
  'C:\Users\Administrator\AppData\Local\IVAN\plugins\HalftoneDots\HalftoneDots-rh7.rhp'
)

$HK = [System.Reflection.Metadata.HandleKind]

foreach ($f in $files) {
    Write-Host ("########## " + $f)
    if (-not (Test-Path $f)) { Write-Host "  MISSING"; continue }
    $fs = [System.IO.File]::OpenRead($f)
    try {
        $pe = New-Object System.Reflection.PortableExecutable.PEReader($fs)
        $mr = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $ad = $mr.GetAssemblyDefinition()
        Write-Host ("  ASSEMBLY: " + $mr.GetString($ad.Name) + "  v" + $ad.Version)
        foreach ($h in $mr.TypeDefinitions) {
            $td = $mr.GetTypeDefinition($h)
            $ns = $mr.GetString($td.Namespace)
            $nm = $mr.GetString($td.Name)
            $bt = $td.BaseType
            $btName = ''
            if (-not $bt.IsNil) {
                if ($bt.Kind -eq $HK::TypeReference) {
                    $tr = $mr.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$bt)
                    $btName = ($mr.GetString($tr.Namespace) + '.' + $mr.GetString($tr.Name)).Trim('.')
                } elseif ($bt.Kind -eq $HK::TypeDefinition) {
                    $td2 = $mr.GetTypeDefinition([System.Reflection.Metadata.TypeDefinitionHandle]$bt)
                    $btName = ($mr.GetString($td2.Namespace) + '.' + $mr.GetString($td2.Name)).Trim('.')
                } else { $btName = [string]$bt.Kind }
            }
            $full = ($ns + '.' + $nm).Trim('.')
            $interesting = ($btName -like '*PlugIn*') -or ($full -like '*Plugin*') -or ($full -like '*PlugIn*')
            if ($interesting) { Write-Host ("  TYPE " + $full + "   base=" + $btName) }
            foreach ($cah in $td.GetCustomAttributes()) {
                $ca = $mr.GetCustomAttribute($cah)
                $ctor = $ca.Constructor
                $an = '?'
                if ($ctor.Kind -eq $HK::MemberReference) {
                    $mref = $mr.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$ctor)
                    $par = $mref.Parent
                    if ($par.Kind -eq $HK::TypeReference) {
                        $tr = $mr.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$par)
                        $an = ($mr.GetString($tr.Namespace) + '.' + $mr.GetString($tr.Name)).Trim('.')
                    }
                } elseif ($ctor.Kind -eq $HK::MethodDefinition) {
                    $md = $mr.GetMethodDefinition([System.Reflection.Metadata.MethodDefinitionHandle]$ctor)
                    $an = 'MethodDef:' + $mr.GetString($md.Name)
                }
                $blob = $mr.GetBlobBytes($ca.Value)
                $sb = New-Object System.Text.StringBuilder
                foreach ($b in $blob) { if ($b -ge 32 -and $b -lt 127) { [void]$sb.Append([char]$b) } else { [void]$sb.Append('.') } }
                if ($interesting -or $an -like '*Guid*') {
                    Write-Host ("       ATTR " + $an + "   blob=[" + $sb.ToString() + "]")
                }
            }
        }
        $pe.Dispose()
    } catch {
        Write-Host ("  ERROR: " + $_.Exception.Message)
    } finally { $fs.Dispose() }
    Write-Host ""
}
