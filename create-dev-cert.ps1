# create-dev-cert.ps1
# Creates a self-signed code-signing certificate for LOCAL sideloading / testing only.
# You do NOT need this for Microsoft Store submission — Partner Center signs the package for you.
#
# Run once from an ADMINISTRATOR PowerShell prompt:
#   powershell -ExecutionPolicy Bypass -File .\create-dev-cert.ps1

$certSubject = "CN=StreamCommand"
$certPath    = "$PSScriptRoot\StreamCommandPackage\StreamCommandPackage_TemporaryKey.pfx"
$certPassword = ConvertTo-SecureString -String "TempPassword123!" -Force -AsPlainText

# Check if already exists
if (Test-Path $certPath) {
    Write-Host "Certificate already exists at: $certPath"
    Write-Host "Delete it first if you want to regenerate."
    exit 0
}

Write-Host "Creating self-signed code-signing certificate..."

# Create the cert in the current user's store
$cert = New-SelfSignedCertificate `
    -Subject $certSubject `
    -Type CodeSigningCert `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears(2)

Write-Host "Certificate created: $($cert.Thumbprint)"

# Export to PFX
Export-PfxCertificate -Cert $cert -FilePath $certPath -Password $certPassword | Out-Null

Write-Host "Exported to: $certPath"
Write-Host "Password:    TempPassword123!"
Write-Host ""
Write-Host "IMPORTANT:"
Write-Host "  - This certificate is for local testing ONLY."
Write-Host "  - For Microsoft Store submission, Partner Center signs the package."
Write-Host "  - In Visual Studio: right-click StreamCommandPackage > Properties"
Write-Host "    and set the PFX path + password under 'Package Signing'."
Write-Host ""
Write-Host "Thumbprint: $($cert.Thumbprint)"
