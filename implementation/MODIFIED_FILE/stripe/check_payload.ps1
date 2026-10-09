$f = 'C:\zcode_build\stripe\center\payload\HalftoneDots-rh8.rhp'
$b = [IO.File]::ReadAllBytes($f)
Write-Host ("file size = " + $b.Length)

function Find-Bytes($hay, $pat) {
    $limit = $hay.Length - $pat.Length
    for ($i = 0; $i -le $limit; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $pat.Length; $j++) { if ($hay[$i + $j] -ne $pat[$j]) { $ok = $false; break } }
        if ($ok) { return $i }
    }
    return -1
}

# UTF-16LE 字节模式
$patFalloff  = [byte[]](0x70,0x88,0xCF,0x51,0x45,0x5E,0xA6,0x5E)   # 衰减幅度
$patRotation = [byte[]](0xCB,0x65,0x6C,0x8F,0xD2,0x89,0xA6,0x5E)   # 旋转角度
$patLayer    = [byte[]](0x35,0x96,0x17,0x52,0xB9,0x7E,0x06,0x74)   # 阵列纹理
$patAscii    = [Text.Encoding]::ASCII.GetBytes('Falloff')

Write-Host ("Falloff label(UTF16) at " + (Find-Bytes $b $patFalloff))
Write-Host ("Rotation label(UTF16) at " + (Find-Bytes $b $patRotation))
Write-Host ("Layer name(UTF16) at " + (Find-Bytes $b $patLayer))
Write-Host ("'Falloff' name(ASCII) at " + (Find-Bytes $b $patAscii))
