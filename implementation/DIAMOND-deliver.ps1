$ErrorActionPreference = 'Stop'
$src = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\out\center\IVAN-CENTER.exe'
$targets = @(
  'D:\UserData\Desktop\IVAN插件中心\IVAN-CENTER.exe',
  'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\stripe\dist\IVAN-CENTER.exe',
  'C:\zcode_build\stripe\dist\IVAN-CENTER.exe'
)
$h = (Get-FileHash -LiteralPath $src -Algorithm MD5).Hash.ToLower()
$len = (Get-Item -LiteralPath $src).Length
Write-Output ("SOURCE {0} bytes  md5 {1}" -f $len, $h)
foreach ($t in $targets) {
  Copy-Item -LiteralPath $src -Destination $t -Force
  $h2 = (Get-FileHash -LiteralPath $t -Algorithm MD5).Hash.ToLower()
  Write-Output ("{0}  {1} bytes  md5 {2}  {3}" -f $(if ($h2 -eq $h) { 'OK  ' } else { 'DIFF' }), (Get-Item -LiteralPath $t).Length, $h2, $t)
}
