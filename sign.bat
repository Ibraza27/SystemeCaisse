@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Set-AuthenticodeSignature -FilePath '%~1' -Certificate (Get-ChildItem Cert:\CurrentUser\My | Where-Object {$_.Subject -eq 'CN=Ibraza27 SystemeCaisse'} | Select-Object -First 1) -TimestampServer 'http://timestamp.digicert.com'" >nul 2>&1
