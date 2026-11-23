<#
.SYNOPSIS
    Creates a self-signed code-signing certificate for sideloading the MSIX during development.

.DESCRIPTION
    Writes a .pfx (for signing) and a .cer (for trusting on a test machine) to -OutDir.
    Nothing is added to any certificate store. The subject must match the Publisher in
    src/HoloRepository.App/Package.appxmanifest (CN=HoloRepository).

.EXAMPLE
    ./scripts/New-TestSigningCertificate.ps1 -OutDir artifacts/signing -Password "dev-only"
#>
param(
    [string] $OutDir = "artifacts/signing",
    [string] $Password = "holorepository-test",
    [string] $Subject = "CN=HoloRepository"
)

$ErrorActionPreference = 'Stop'
$crypto = [System.Security.Cryptography.X509Certificates.X509Certificate2].Assembly

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
    $Subject, $rsa,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

$request.CertificateExtensions.Add(
    [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
$request.CertificateExtensions.Add(
    [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
$usages = [System.Security.Cryptography.OidCollection]::new()
[void] $usages.Add([System.Security.Cryptography.Oid]::new("1.3.6.1.5.5.7.3.3", "Code Signing"))
$request.CertificateExtensions.Add(
    [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages, $false))

$certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(1))

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$pfx = Join-Path $OutDir "HoloRepository-test.pfx"
$cer = Join-Path $OutDir "HoloRepository-test.cer"
[System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path $OutDir) "HoloRepository-test.pfx"),
    $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $Password))
[System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path $OutDir) "HoloRepository-test.cer"),
    $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

Write-Output "Signing certificate: $pfx"
Write-Output "Public certificate:  $cer"
Write-Output "Thumbprint:          $($certificate.Thumbprint)"
