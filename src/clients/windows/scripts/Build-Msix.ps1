<#
.SYNOPSIS
    Builds the HoloRepository MSIX package and optionally signs it.

.EXAMPLE
    # Unsigned package
    ./scripts/Build-Msix.ps1

.EXAMPLE
    # Signed with a development certificate
    ./scripts/New-TestSigningCertificate.ps1 -OutDir artifacts/signing -Password dev
    ./scripts/Build-Msix.ps1 -CertificatePath artifacts/signing/HoloRepository-test.pfx -CertificatePassword dev
#>
param(
    [ValidateSet("x64", "ARM64")] [string] $Platform = "x64",
    [string] $Configuration = "Release",
    [string] $CertificatePath,
    [string] $CertificatePassword,
    [string] $OutDir = "artifacts/msix"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/HoloRepository.App/HoloRepository.App.csproj"
$packages = Join-Path $root "src/HoloRepository.App/AppPackages"

if (Test-Path $packages) { Remove-Item -Recurse -Force $packages }

dotnet build $project -c $Configuration -p:Platform=$Platform -p:GenerateAppxPackageOnBuild=true -nologo
if ($LASTEXITCODE -ne 0) { throw "MSIX build failed." }

$msix = Get-ChildItem $packages -Recurse -Filter *.msix | Select-Object -First 1
if (-not $msix) { throw "No .msix was produced." }

if ($CertificatePath) {
    $signtool = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget/packages/microsoft.windows.sdk.buildtools") -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found in the Windows SDK build tools package." }
    & $signtool.FullName sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $msix.FullName
    if ($LASTEXITCODE -ne 0) { throw "Signing failed." }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$target = Join-Path $OutDir $msix.Name
Copy-Item $msix.FullName $target -Force
Write-Output "Package: $target"
