@echo off
echo Installation de l autorisation pour SystemeCaisse (Necessite les droits Administrateur)...
certutil -addstore -f "TrustedPublisher" "%~dp0SystemeCaisseCert.cer"
certutil -addstore -f "Root" "%~dp0SystemeCaisseCert.cer"
echo Termine ! Vous pouvez maintenant lancer Setup_SystemeCaisse.exe
pause
