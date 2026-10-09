$ErrorActionPreference = 'Stop'
$A = "$env:APPDATA\McNeel\Rhinoceros\8.0\settings\Scheme__Default"
$files = @(
  "$A\workspaces\d2451180-5ff5-4419-9abb-f982b0bf106a.xml",
  "$A\containers.xml"
)
$staleGroup = 'a7f3c1e2-4b58-4d6a-9c02-1e5b7d9a3f40'   # 旧独立安装器时代的 Stripe 工具条组
$staleDock  = 'c4d2e8f1-6a37-4b95-8e10-2f7c9a4b6d13'   # 对应 dock_bar

foreach ($f in $files) {
  if (-not (Test-Path $f)) { Write-Host ("skip missing " + $f); continue }
  $bak = "$f.ivanbak2"
  if (-not (Test-Path $bak)) { Copy-Item $f $bak -Force; Write-Host ("backup -> " + $bak) }
  $s = [System.IO.File]::ReadAllText($f)
  $orig = $s
  # 删除整块 <dock_bar ... source_group_file="a7f3c1e2..." ...>...</dock_bar>
  $s = [regex]::Replace($s, '<dock_bar [^>]*source_group_file="' + $staleGroup + '"[^>]*>.*?</dock_bar>', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
  # 删除残留的 <file_name ... 同组 ...>
  $s = [regex]::Replace($s, '<file_name [^>]*' + $staleGroup + '[^>]*>[^<]*</file_name>', '')
  if ($s -ne $orig) {
    [System.IO.File]::WriteAllText($f, $s, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ("cleaned " + $f)
  } else {
    Write-Host ("no change " + $f)
  }
}
Write-Host "--- verify ---"
foreach ($f in $files) {
  $c = [System.IO.File]::ReadAllText($f)
  Write-Host ((Split-Path $f -Leaf) + " : stale refs = " + ([regex]::Matches($c, $staleGroup)).Count)
}
