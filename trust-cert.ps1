# trust-cert.ps1  -  OPTIONAL, run once, with your consent.
#
# Removes the "Unknown publisher" prompt that Windows shows for the launcher's
# .rdp file. It creates a self-signed CODE-SIGNING certificate and adds it to
# YOUR OWN (current-user) Trusted Publishers store so mstsc trusts the signed
# .rdp file. Nothing is installed for other users and no admin rights are needed.
#
# This is a security-relevant action (it lets content signed by this one cert run
# without a warning), so it is a separate, explicit opt-in - the launcher never
# does it for you. To undo it, run this script with:  -Remove
#
# Usage:
#   Right-click > Run with PowerShell        (installs & trusts the cert)
#   powershell -File trust-cert.ps1 -Remove   (removes the cert again)

param([switch]$Remove)

$subject = 'CN=UbuntuDesktop Launcher'

function Get-MyCert {
    Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
}

if ($Remove) {
    $found = $false
    foreach ($loc in 'My', 'TrustedPublisher', 'Root') {
        Get-ChildItem "Cert:\CurrentUser\$loc" -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $subject } |
            ForEach-Object { Remove-Item $_.PSPath -Force; $found = $true }
    }
    if ($found) { Write-Host "Removed the UbuntuDesktop signing certificate." -ForegroundColor Green }
    else { Write-Host "No certificate to remove." -ForegroundColor Yellow }
    return
}

$cert = Get-MyCert
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Subject $subject -Type CodeSigningCert `
        -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(10)
    Write-Host "Created signing certificate." -ForegroundColor Green
} else {
    Write-Host "Signing certificate already exists." -ForegroundColor Green
}

# Trust it for the current user only (self-signed cert must be its own root).
foreach ($storeName in 'TrustedPublisher', 'Root') {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
    $store.Open('ReadWrite')
    if (-not ($store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) {
        $store.Add($cert)
    }
    $store.Close()
}

Write-Host ""
Write-Host "Done. The launcher will now sign its .rdp file and connect with no prompt." -ForegroundColor Cyan
Write-Host "Thumbprint: $($cert.Thumbprint)"
