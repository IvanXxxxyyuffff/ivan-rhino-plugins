# Full install/uninstall/install cycle test for IVAN-CENTER + registry inspection.
$exe  = 'D:\UserData\Desktop\IVAN' + [char]0x63D2 + [char]0x4EF6 + [char]0x4E2D + [char]0x5FC3 + '\IVAN-CENTER.exe'
$ivan = 'C:\Users\Administrator\AppData\Local\IVAN\plugins'
$zero = '00000000-0000-0000-0000-000000000000'
$root = 'HKCU:\Software\MCNeel\Rhinoceros'

Write-Host ("exe exists: " + (Test-Path $exe))
if (-not (Test-Path $exe)) { exit 9 }

function Dump-IvanKeys($tag) {
  Write-Host ("--- registry IVAN keys [" + $tag + "] ---")
  foreach ($ver in (Get-ChildItem $root -ErrorAction SilentlyContinue).PSChildName) {
    $pi = "$root\$ver\Plug-Ins"
    if (-not (Test-Path $pi)) { continue }
    foreach ($k in (Get-ChildItem $pi -ErrorAction SilentlyContinue)) {
      $fn = (Get-ItemProperty "$($k.PSPath)\PlugIn" -ErrorAction SilentlyContinue).FileName
      if ([string]::IsNullOrEmpty($fn) -eq $false -and $fn.StartsWith($ivan, [System.StringComparison]::OrdinalIgnoreCase)) {
        $name = (Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue).Name
        $isZero = $k.PSChildName -ieq $zero
        Write-Host ("  [" + $ver + "] " + $k.PSChildName + "  zero=" + $isZero + "  name=" + $name)
        Write-Host ("        " + $fn)
      }
    }
  }
}

function Dump-Files($tag) {
  Write-Host ("--- deployed plugin files [" + $tag + "] ---")
  if (Test-Path $ivan) {
    Get-ChildItem $ivan -Recurse -File | ForEach-Object {
      Write-Host ("  " + $_.Length.ToString().PadLeft(7) + "  " + $_.LastWriteTime.ToString('HH:mm:ss') + "  " + $_.FullName)
    }
  } else { Write-Host "  (no IVAN plugins dir)" }
}

Dump-IvanKeys "BEFORE"
Dump-Files "BEFORE"

Write-Host ""
Write-Host "=== STEP 1: silent install ==="
$p = Start-Process -FilePath $exe -ArgumentList '/silent' -Wait -PassThru
Write-Host ("exit code: " + $p.ExitCode)
Get-Content "$env:TEMP\IVANCENTER.log" -Tail 60 -ErrorAction SilentlyContinue
Dump-IvanKeys "after install"
Dump-Files "after install"

Write-Host ""
Write-Host "=== STEP 2: silent uninstall ==="
$p = Start-Process -FilePath $exe -ArgumentList '/silent','/uninstall' -Wait -PassThru
Write-Host ("exit code: " + $p.ExitCode)
Get-Content "$env:TEMP\IVANCENTER.log" -Tail 40 -ErrorAction SilentlyContinue
Dump-IvanKeys "after uninstall"
Dump-Files "after uninstall"

Write-Host ""
Write-Host "=== STEP 3: silent install again (final state) ==="
$p = Start-Process -FilePath $exe -ArgumentList '/silent' -Wait -PassThru
Write-Host ("exit code: " + $p.ExitCode)
Get-Content "$env:TEMP\IVANCENTER.log" -Tail 60 -ErrorAction SilentlyContinue
Dump-IvanKeys "final"
Dump-Files "final"
