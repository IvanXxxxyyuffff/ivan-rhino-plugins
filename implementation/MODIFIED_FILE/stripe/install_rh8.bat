@echo off
set K=HKCU\Software\MCNeel\Rhinoceros\8.0\Plug-Ins\b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91
reg add "%K%" /v Name /t REG_SZ /d "StripeOnSurface (Surface Stripe)" /f
reg add "%K%" /v EnglishName /t REG_SZ /d "StripeOnSurface" /f
reg add "%K%" /v Organization /t REG_SZ /d "" /f
reg add "%K%" /v AddToHelpMenu /t REG_DWORD /d 0 /f
reg add "%K%" /v LoadMode /t REG_DWORD /d 1 /f
reg add "%K%" /v Type /t REG_DWORD /d 16 /f
reg add "%K%" /v IsDotNETPlugIn /t REG_DWORD /d 1 /f
reg add "%K%" /v DirectoryInstall /t REG_DWORD /d 0 /f
reg add "%K%" /v RegPath /t REG_SZ /d "\\HKEY_CURRENT_USER\Software\MCNeel\Rhinoceros\8.0\Plug-Ins\b7a1c3d2-5e64-4f7a-9b21-3c0d5e7f8a91" /f
reg add "%K%\PlugIn" /v FileName /t REG_SZ /d "D:\UserData\Desktop\StripeOnSurface\StripeOnSurface-rh8.rhp" /f
reg add "%K%\CommandList" /v StripeOnSurface /t REG_SZ /d "2;StripeOnSurface" /f
reg add "%K%\CommandList" /v StripeSelfTest /t REG_SZ /d "2;StripeSelfTest" /f
echo ---
reg query "%K%" /s
