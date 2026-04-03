@echo off
echo Installation de l autorisation pour SystemeCaisse (Necessite les droits Administrateur)...
certutil -addstore -f "TrustedPublisher" "%~dp0HippocampeEnterprise.cer"
certutil -addstore -f "Root" "%~dp0HippocampeEnterprise.cer"
echo Termine ! Vous pouvez maintenant lancer Setup_SystemeCaisse.exe
pause
