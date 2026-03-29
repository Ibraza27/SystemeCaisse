$ErrorActionPreference = "Stop"

$subject = "CN=Ibraza27 SystemeCaisse"
$certName = "SystemeCaisseCert.cer"

# Check if the certificate already exists in My store
$existingCert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1

if (-not $existingCert) {
    Write-Host "Création d'un nouveau certificat auto-signé pour la signature de code..."
    $existingCert = New-SelfSignedCertificate -Subject $subject -Type CodeSigningCert -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(5)
} else {
    Write-Host "Le certificat existe déjà dans le magasin personnel."
}

# Export the certificate
Write-Host "Export du certificat vers $certName..."
Export-Certificate -Cert $existingCert -FilePath $certName -Force

# Check if the certificate is already in Trusted Root
$trustedCert = Get-ChildItem Cert:\CurrentUser\Root | Where-Object { $_.Thumbprint -eq $existingCert.Thumbprint }

if (-not $trustedCert) {
    Write-Host "Importation du certificat dans les Autorités de certification racines de confiance..."
    Import-Certificate -FilePath $certName -CertStoreLocation "Cert:\CurrentUser\Root"
    Write-Host "Certificat importé avec succès. (Vous avez peut-être dû accepter une alerte de sécurité Windows)."
} else {
    Write-Host "Le certificat est déjà approuvé."
}

Write-Host "Configuration terminée. Les binaires seront signés avec le fichier Directory.Build.targets lors de la compilation."
