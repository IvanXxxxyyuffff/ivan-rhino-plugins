Add-Type -AssemblyName System.Drawing

function New-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(255, 250, 250, 250))
    # 面板底色
    $panel = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 248, 214, 216))
    $g.FillRectangle($panel, 1, 2, $size - 2, $size - 4)
    # 45° 条纹
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 120, 110)), ([float]($size / 9.0))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    for ($i = 0; $i -lt 3; $i++) {
        $off = $size * (0.18 + 0.26 * $i)
        $g.DrawLine($pen, [float]($off - $size * 0.35), [float]($size * 0.95), [float]($off + $size * 0.35), [float]($size * 0.05))
    }
    $g.Dispose()
    return $bmp
}

function Get-Base64Png($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b64 = [Convert]::ToBase64String($ms.ToArray())
    $ms.Dispose()
    return $b64
}

$icon16 = New-Icon 16
$icon32 = New-Icon 32
$b64_16 = Get-Base64Png $icon16
$b64_32 = Get-Base64Png $icon32
$icon16.Dispose(); $icon32.Dispose()

# GUID
$g0 = [guid]::NewGuid().ToString()
$g1 = [guid]::NewGuid().ToString()
$g64 = [guid]::NewGuid().ToString()
$g2 = [guid]::NewGuid().ToString()
$tb = [guid]::NewGuid().ToString()
$gi = [guid]::NewGuid().ToString()
$m1 = [guid]::NewGuid().ToString()
$bm1 = [guid]::NewGuid().ToString()
$bmtb = [guid]::NewGuid().ToString()

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RhinoUI major_ver="3" minor_ver="0" guid="$g0" localize="False" default_language_id="1033" dpi_scale="100">
  <menus />
  <tool_bar_groups>
    <tool_bar_group guid="$g1" dock_bar_guid32="00000000-0000-0000-0000-000000000000" dock_bar_guid64="$g64" single_file="False" hide_single_tab="False" point_floating="120,120">
      <text>
        <locale_1033>表面条纹</locale_1033>
      </text>
      <tool_bar_group_item guid="$g2" major_version="1" minor_version="1">
        <text>
          <locale_1033>表面条纹</locale_1033>
        </text>
        <tool_bar_id>$tb</tool_bar_id>
      </tool_bar_group_item>
      <dock_bar_info dpi_scale="100" dock_bar="False" docking="False" horz="False" visible="True" floating="True" />
    </tool_bar_group>
  </tool_bar_groups>
  <tool_bars>
    <tool_bar guid="$tb" bitmap_id="$bm1">
      <text>
        <locale_1033>表面条纹</locale_1033>
      </text>
      <tool_bar_item guid="$gi" display_style_from_parent="False">
        <left_macro_id>$m1</left_macro_id>
      </tool_bar_item>
    </tool_bar>
  </tool_bars>
  <macros>
    <macro_item guid="$m1" bitmap_id="$bm1">
      <text>
        <locale_1033>表面条纹</locale_1033>
      </text>
      <tooltip>
        <locale_1033>表面条纹：选择曲面生成条纹（宽度/间距/角度/边缘距离）</locale_1033>
      </tooltip>
      <button_text>
        <locale_1033>表面条纹</locale_1033>
      </button_text>
      <script>! _StripeOnSurface</script>
    </macro_item>
  </macros>
  <bitmaps>
    <small_bitmap item_width="16" item_height="16">
      <bitmap_item guid="$bm1" index="0" />
      <bitmap>$b64_16</bitmap>
    </small_bitmap>
    <large_bitmap item_width="32" item_height="32">
      <bitmap_item guid="$bm1" index="0" />
      <bitmap>$b64_32</bitmap>
    </large_bitmap>
  </bitmaps>
</RhinoUI>
"@

$out = "C:\zcode_build\stripe\dist\StripeOnSurface.rui"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
[System.IO.File]::WriteAllText($out, $xml, (New-Object System.Text.UTF8Encoding $true))
Write-Output ("written " + $out + " size=" + (Get-Item $out).Length)
