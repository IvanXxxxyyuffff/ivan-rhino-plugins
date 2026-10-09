$ErrorActionPreference = 'Stop'
$src = 'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE'
$pairs = @(
  @{ S = "$src\PROJECT-STATE.md";       D = 'C:\zcode_build\PROJECT-STATE.md' },
  @{ S = "$src\PROJECT-STATE.md";       D = 'D:\UserData\Desktop\IVAN插件中心\项目状态存档.md' },
  @{ S = "$src\DESIGN-CONSISTENCY.md";  D = 'C:\zcode_build\DESIGN-CONSISTENCY.md' },
  @{ S = "$src\DESIGN-CONSISTENCY.md";  D = 'D:\UserData\Desktop\IVAN插件中心\设计一致性规范.md' },
  @{ S = "$src\UI-REFACTOR-HANDOFF.md"; D = 'C:\zcode_build\UI-REFACTOR-HANDOFF.md' },
  @{ S = "$src\UI-REFACTOR-HANDOFF.md"; D = 'D:\UserData\Desktop\IVAN插件中心\UI重构实施交接说明-本次CODEX.md' }
)
foreach ($p in $pairs) {
  Copy-Item -LiteralPath $p.S -Destination $p.D -Force
  $h1 = (Get-FileHash -LiteralPath $p.S -Algorithm MD5).Hash.ToLower()
  $h2 = (Get-FileHash -LiteralPath $p.D -Algorithm MD5).Hash.ToLower()
  $tag = if ($h1 -eq $h2) { 'OK  ' } else { 'DIFF' }
  Write-Output ("{0} {1,8} bytes  md5 {2}  {3}" -f $tag, (Get-Item -LiteralPath $p.D).Length, $h1.Substring(0, 12), $p.D)
}
