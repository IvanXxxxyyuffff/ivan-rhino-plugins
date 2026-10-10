$dlls = @(
  'C:\Users\Administrator\.nuget\packages\rhinocommon\7.0.20314.3001\lib\net45\RhinoCommon.dll',
  'C:\Users\Administrator\.nuget\packages\rhinocommon\8.30.26103.11001\lib\net48\RhinoCommon.dll'
)
foreach ($d in $dlls) {
  Write-Output ('=== ' + (Split-Path $d -Parent) + ' ===')
  try {
    $asm = [Reflection.Assembly]::LoadFrom($d)
    $t = $asm.GetType('Rhino.Geometry.Brep')
    $t.GetMethods() | Where-Object { $_.Name -eq 'CreatePatch' } | ForEach-Object { $_.ToString() }
  } catch { Write-Output ('load fail: ' + $_.Exception.Message) }
}
