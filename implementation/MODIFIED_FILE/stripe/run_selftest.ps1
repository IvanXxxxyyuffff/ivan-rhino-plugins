$exe = "D:\Rhino 8\System\Rhino.exe"
$p = Start-Process -FilePath $exe -ArgumentList @('/nosplash', '/runscript=-StripeSelfTest') -PassThru
$p.Id | Out-File -Encoding ascii "C:\zcode_build\stripe\rhino_pid.txt"
Write-Output ("started pid " + $p.Id)
