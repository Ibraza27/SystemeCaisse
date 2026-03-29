$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=Ibraza27 SystemeCaisse" } | Select-Object -First 1
if ($cert) {
    Set-AuthenticodeSignature -Certificate $cert -FilePath ".\SystemeCaisse.UI\bin\Release\net8.0-windows\win-x64\publish\SystemeCaisse.UI.exe" -TimestampServer 'http://timestamp.digicert.com'
    Write-Host "L exécutable a bien été signé manuellement."
} else {
    Write-Warning "Certificat Ibraza27 SystemeCaisse non trouvé !"
}
