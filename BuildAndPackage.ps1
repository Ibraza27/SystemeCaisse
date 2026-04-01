$ErrorActionPreference = "Stop"

Write-Host "=========================================="
Write-Host "🚀 Compilation Autonome de SystemeCaisse"
Write-Host "=========================================="

$projectPath = "SystemeCaisse.UI\SystemeCaisse.UI.csproj"
$publishDir = "SystemeCaisse.UI\bin\Release\net8.0-windows10.0.19041\win-x64\publish"
$distDir = "Distribution"
$signBat = Join-Path (Get-Location) "sign.bat"

# 1. Nettoyer les versions précédentes
Write-Host "1. Nettoyage..."
if (Test-Path $publishDir) { Remove-Item -Path $publishDir -Recurse -Force }
if (Test-Path $distDir) { Remove-Item -Path $distDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# 2. Compiler en mode Release Autonome (Inclus .NET 8)
Write-Host "2. Compilation via dotnet publish (Ceci peut prendre quelques instants)..."
& "C:\Program Files\dotnet\dotnet.exe" publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "La compilation a échoué."
    exit 1
}

# 3. Signature des binaires SystemeCaisse via PowerShell avant l'empaquetage
Write-Host "3. Signature des binaires (DLLs et EXEs)..."
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=Ibraza27 SystemeCaisse" } | Select-Object -First 1

if ($cert) {
    Get-ChildItem -Path $publishDir -Include *.exe,*.dll -Recurse | ForEach-Object {
        if ($_.Name -like "SystemeCaisse*") {
            Set-AuthenticodeSignature -Certificate $cert -FilePath $_.FullName -TimestampServer "http://timestamp.digicert.com" -ErrorAction SilentlyContinue | Out-Null
        }
    }
    Write-Host "Binaires signés !"
} else {
    Write-Warning "Certificat Ibraza27 SystemeCaisse non trouvé. Les binaires ne seront pas signés formellement."
}

# 4. Création de l'installateur avec Inno Setup (avec SignTool intégré)
Write-Host "4. Génération de Setup.exe avec Inno Setup (signature intégrée)..."
$isccPath = "$env:ProgramFiles (x86)\Inno Setup 6\ISCC.exe"

if (-not (Test-Path $isccPath)) {
    Write-Error "Inno Setup n'est pas installé dans le dossier par défaut ($isccPath)."
    exit 1
}

# Passer le SignTool via /S pour que ISCC signe le setup.exe, le setup.tmp ET le uninstaller
& $isccPath "systemecaisse.iss" /Smysigntool="$signBat `$f"

if ($LASTEXITCODE -ne 0) {
    Write-Warning "ISCC a échoué avec le SignTool intégré. Tentative sans SignTool..."
    # Fallback: compiler sans SignTool, puis signer manuellement
    & $isccPath "systemecaisse.iss" /O"$distDir" /DNoSignTool
    
    $setupExe = (Get-ChildItem -Path $distDir -Filter "Setup_SystemeCaisse*.exe" | Select-Object -First 1).FullName
    if ($cert -and $setupExe) {
        Write-Host "Signature manuelle du fichier d'installation : $setupExe"
        Set-AuthenticodeSignature -Certificate $cert -FilePath $setupExe -TimestampServer "http://timestamp.digicert.com" -ErrorAction SilentlyContinue | Out-Null
    }
}

# 5. Copie du certificat public pour la distribution
Write-Host "5. Préparation du certificat de confiance..."
$certFile = "SystemeCaisseCert.cer"
if ($cert) {
    Export-Certificate -Cert $cert -FilePath "$distDir\$certFile" -Force | Out-Null
}

# Script batch pour installer le certif sur d'autres PC (TrustedPublisher + Root)
$installCertScript = "$distDir\InstallerAutorisationSecurite.bat"
Set-Content -Path $installCertScript -Value "@echo off"
Add-Content -Path $installCertScript -Value "echo Installation de l autorisation pour SystemeCaisse (Necessite les droits Administrateur)..."
Add-Content -Path $installCertScript -Value "certutil -addstore -f ""TrustedPublisher"" ""%~dp0$certFile"""
Add-Content -Path $installCertScript -Value "certutil -addstore -f ""Root"" ""%~dp0$certFile"""
Add-Content -Path $installCertScript -Value "echo Termine ! Vous pouvez maintenant lancer Setup_SystemeCaisse.exe"
Add-Content -Path $installCertScript -Value "pause"

Write-Host "=========================================="
Write-Host "✅ Terminé ! Le Setup complet se trouve dans le dossier Distribution."
Write-Host "=========================================="
