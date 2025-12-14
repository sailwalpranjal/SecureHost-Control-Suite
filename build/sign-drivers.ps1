#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Signs kernel drivers with EV code signing certificate

.DESCRIPTION
    Signs driver binaries (.sys), catalog files (.cat), and INF files
    Requires EV code signing certificate in certificate store or HSM

.PARAMETER CertificateThumbprint
    Thumbprint of the EV code signing certificate

.PARAMETER TimestampServer
    RFC 3161 timestamp server URL

.PARAMETER Configuration
    Build configuration (Debug or Release)

.EXAMPLE
    .\sign-drivers.ps1 -CertificateThumbprint "ABC123..." -Configuration Release
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$CertificateThumbprint,

    [Parameter()]
    [string]$TimestampServer = "http://timestamp.digicert.com",

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$SolutionRoot = Split-Path -Parent $PSScriptRoot
$BinPath = Join-Path $SolutionRoot "bin\x64\$Configuration"

Write-Host "SecureHost Driver Signing Tool" -ForegroundColor Cyan
Write-Host "==============================`n"

# Check for signtool
$signtool = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\*\x64\signtool.exe" |
    Get-ChildItem | Sort-Object -Descending | Select-Object -First 1

if (-not $signtool) {
    Write-Host "ERROR: signtool.exe not found. Install Windows SDK." -ForegroundColor Red
    exit 1
}

Write-Host "Using signtool: $($signtool.FullName)`n"

# Get certificate
if (-not $CertificateThumbprint) {
    Write-Host "Searching for EV code signing certificates..."
    $certs = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
        $_.EnhancedKeyUsageList -match "Code Signing" -and
        $_.Subject -match "CN="
    }

    if ($certs.Count -eq 0) {
        Write-Host "ERROR: No code signing certificate found." -ForegroundColor Red
        Write-Host "`nTo sign drivers, you need an EV code signing certificate."
        Write-Host "Instructions for obtaining one:"
        Write-Host "  1. Purchase EV code signing certificate from DigiCert, Sectigo, or GlobalSign"
        Write-Host "  2. Install certificate on this machine or USB token"
        Write-Host "  3. Re-run this script with -CertificateThumbprint parameter`n"
        exit 1
    }

    Write-Host "Found certificates:"
    $certs | ForEach-Object -Begin { $i = 1 } -Process {
        Write-Host "  $i. $($_.Subject) (Thumbprint: $($_.Thumbprint))"
        $i++
    }

    $selection = Read-Host "`nSelect certificate (1-$($certs.Count))"
    $cert = $certs[[int]$selection - 1]
    $CertificateThumbprint = $cert.Thumbprint
}

Write-Host "Using certificate: $CertificateThumbprint`n"

# Files to sign
$driverFiles = @(
    "SecureHostWFP.sys",
    "SecureHostDevice.sys"
)

$catalogFiles = @(
    "SecureHostWFP.cat",
    "SecureHostDevice.cat"
)

# Sign drivers
Write-Host "Signing driver binaries..." -ForegroundColor Yellow

foreach ($driver in $driverFiles) {
    $driverPath = Join-Path $BinPath $driver

    if (-not (Test-Path $driverPath)) {
        Write-Host "  WARNING: $driver not found, skipping" -ForegroundColor Yellow
        continue
    }

    Write-Host "  Signing $driver..."

    # Sign with SHA256 (required for kernel-mode drivers)
    & $signtool sign /v /fd sha256 /sha1 $CertificateThumbprint `
        /t $TimestampServer `
        /ph `  # Page hashing
        /ac "C:\Windows\System32\DriverStore\FileRepository\*\MSCV-VSClass3.cer" `  # Cross-cert (if available)
        $driverPath

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Failed to sign $driver" -ForegroundColor Red
        continue
    }

    # Verify signature
    & $signtool verify /v /kp $driverPath | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [OK] $driver signed and verified" -ForegroundColor Green
    } else {
        Write-Host "  [FAILED] Signature verification failed for $driver" -ForegroundColor Red
    }
}

# Generate catalog files
Write-Host "`nGenerating catalog files..." -ForegroundColor Yellow

$inf2cat = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\*\x86\inf2cat.exe" |
    Get-ChildItem | Sort-Object -Descending | Select-Object -First 1

if ($inf2cat) {
    $drivers = @(
        @{ Name = "SecureHostWFP"; Path = "src\drivers\SecureHostWFP" },
        @{ Name = "SecureHostDevice"; Path = "src\drivers\SecureHostDevice" }
    )

    foreach ($driver in $drivers) {
        $infPath = Join-Path $SolutionRoot $driver.Path
        Write-Host "  Generating catalog for $($driver.Name)..."

        & $inf2cat /driver:$infPath /os:10_X64,Server10_X64 /verbose

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  [OK] Catalog generated" -ForegroundColor Green

            # Copy catalog to bin
            $catSource = Join-Path $infPath "$($driver.Name).cat"
            $catDest = Join-Path $BinPath "$($driver.Name).cat"
            if (Test-Path $catSource) {
                Copy-Item $catSource $catDest -Force
            }
        }
    }
}

# Sign catalog files
Write-Host "`nSigning catalog files..." -ForegroundColor Yellow

foreach ($catalog in $catalogFiles) {
    $catPath = Join-Path $BinPath $catalog

    if (-not (Test-Path $catPath)) {
        Write-Host "  WARNING: $catalog not found, skipping" -ForegroundColor Yellow
        continue
    }

    Write-Host "  Signing $catalog..."

    & $signtool sign /v /fd sha256 /sha1 $CertificateThumbprint `
        /t $TimestampServer `
        $catPath

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [OK] $catalog signed" -ForegroundColor Green
    }
}

# Summary
Write-Host "`n==============================" -ForegroundColor Cyan
Write-Host "Signing Summary" -ForegroundColor Cyan
Write-Host "==============================`n"

Write-Host "Driver signing completed." -ForegroundColor Green
Write-Host "`nNext steps:"
Write-Host "  1. Submit drivers to Microsoft for WHQL certification"
Write-Host "  2. Use HLK (Hardware Lab Kit) to test drivers"
Write-Host "  3. Submit to Windows Dev Center Dashboard"
Write-Host "  4. Download signed drivers from dashboard`n"

Write-Host "For testing unsigned drivers:"
Write-Host "  - Enable test signing: bcdedit /set testsigning on"
Write-Host "  - Reboot the system"
Write-Host "  - Load drivers using sc.exe or devcon.exe`n"

Write-Host "IMPORTANT: Production deployment requires WHQL-signed drivers!" -ForegroundColor Yellow
