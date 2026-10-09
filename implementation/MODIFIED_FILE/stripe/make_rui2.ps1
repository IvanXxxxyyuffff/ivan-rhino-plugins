Add-Type -AssemblyName System.Drawing

function New-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(255, 250, 250, 250))
    $panel = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 248, 214, 216))
    $g.FillRectangle($panel, 1, 2, $size - 2, $size - 4)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 120, 110)), ([float]($size / 9.0))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    for ($i = 0; $i -lt 3; $i++) {
        $off = $size * (0.18 + 0.26 * $i)
        $g.DrawLine($pen, [float]($off - $size * 0.35), [float]($size * 0.95), [float]($off + $size * 0.35), [float]($size * 0.05))
    }
    $g.Dispose()
    return $bmp
}
function Get-B64($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b = [Convert]::ToBase64String($ms.ToArray()); $ms.Dispose(); return $b
}

$i16 = New-Icon 16; $i32 = New-Icon 32
$b64_16 = Get-B64 $i16; $b64_32 = Get-B64 $i32
$i16.Dispose(); $i32.Dispose()

$src = Join-Path $env:APPDATA 'McNeel\Rhinoceros\7.0\UI\Plug-ins\XNurbsRhino.rui'
if (-not (Test-Path $src)) { $src = 'C:\Program Files\Rhino 7\Plug-ins\XNurbsRhino 5\XNurbsRhino.rui' }
if (-not (Test-Path $src)) { Write-Output "找不到模板 rui"; exit 1 }

[xml]$doc = Get-Content -Raw -Encoding UTF8 $src
$myGroup = [guid]::NewGuid().ToString()
$myTb    = [guid]::NewGuid().ToString()
$myItem  = [guid]::NewGuid().ToString()
$myMacro = [guid]::NewGuid().ToString()
$myBmp   = [guid]::NewGuid().ToString()

$doc.DocumentElement.guid = [guid]::NewGuid().ToString()

$g = $doc.SelectSingleNode('/RhinoUI/tool_bar_groups/tool_bar_group')
$g.guid = $myGroup
$g.point_floating = '180,180'
$g.SelectSingleNode('text/locale_1033').InnerText = 'StripeOnSurface'
$gi = $g.SelectSingleNode('tool_bar_group_item')
$gi.guid = $myItem
$gi.SelectSingleNode('text/locale_1033').InnerText = 'StripeOnSurface'
$gi.SelectSingleNode('tool_bar_id').InnerText = $myTb

$tb = $doc.SelectSingleNode('/RhinoUI/tool_bars/tool_bar')
$tb.guid = $myTb
$tb.bitmap_id = $myBmp
$tb.SelectSingleNode('text/locale_1033').InnerText = 'StripeOnSurface'
$items = @($tb.SelectNodes('tool_bar_item'))
for ($i = 1; $i -lt $items.Count; $i++) { $tb.RemoveChild($items[$i]) | Out-Null }
$items[0].guid = [guid]::NewGuid().ToString()
$items[0].SelectSingleNode('left_macro_id').InnerText = $myMacro

$macros = $doc.SelectSingleNode('/RhinoUI/macros')
$mItems = @($macros.SelectNodes('macro_item'))
$keep = $mItems[0]
for ($i = 1; $i -lt $mItems.Count; $i++) { $macros.RemoveChild($mItems[$i]) | Out-Null }
$keep.guid = $myMacro
$keep.bitmap_id = $myBmp
$keep.SelectSingleNode('text/locale_1033').InnerText = 'StripeOnSurface'
$keep.SelectSingleNode('tooltip/locale_1033').InnerText = '表面条纹：选择曲面生成条纹（宽度/间距/角度/边距，单位 mm）'
$keep.SelectSingleNode('button_text/locale_1033').InnerText = '表面条纹'
$keep.SelectSingleNode('script').InnerText = '! _StripeOnSurface'

$bmps = $doc.SelectSingleNode('/RhinoUI/bitmaps')
$bmps.RemoveAll() | Out-Null
$small = $doc.CreateElement('small_bitmap'); $small.SetAttribute('item_width', '16'); $small.SetAttribute('item_height', '16')
$bi = $doc.CreateElement('bitmap_item'); $bi.SetAttribute('guid', $myBmp); $bi.SetAttribute('index', '0')
$bd = $doc.CreateElement('bitmap'); $bd.InnerText = $b64_16
$small.AppendChild($bi) | Out-Null; $small.AppendChild($bd) | Out-Null; $bmps.AppendChild($small) | Out-Null

$large = $doc.CreateElement('large_bitmap'); $large.SetAttribute('item_width', '32'); $large.SetAttribute('item_height', '32')
$bi2 = $doc.CreateElement('bitmap_item'); $bi2.SetAttribute('guid', $myBmp); $bi2.SetAttribute('index', '0')
$bd2 = $doc.CreateElement('bitmap'); $bd2.InnerText = $b64_32
$large.AppendChild($bi2) | Out-Null; $large.AppendChild($bd2) | Out-Null; $bmps.AppendChild($large) | Out-Null

$out = 'C:\zcode_build\stripe\dist\StripeOnSurface.rui'
$doc.Save($out)
$targets = @(
  'D:\UserData\Desktop\StripeOnSurface\StripeOnSurface.rui',
  (Join-Path $env:LOCALAPPDATA 'StripeOnSurface\StripeOnSurface.rui'),
  (Join-Path $env:APPDATA 'McNeel\Rhinoceros\8.0\UI\Plug-ins\StripeOnSurface.rui'),
  (Join-Path $env:APPDATA 'McNeel\Rhinoceros\7.0\UI\Plug-ins\StripeOnSurface.rui')
)
foreach ($t in $targets) {
    try { $d = Split-Path $t; if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
          Copy-Item $out $t -Force; Write-Output "ok $t" } catch { Write-Output "fail $t : $_" }
}
Write-Output "group=$myGroup toolbar=$myTb size=$((Get-Item $out).Length)"
