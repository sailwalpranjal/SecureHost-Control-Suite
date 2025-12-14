#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates MSI installer for SecureHost Control Suite

.DESCRIPTION
    Builds WiX-based MSI installer package with all components

.PARAMETER Configuration
    Build configuration (Debug or Release)

.PARAMETER SignInstaller
    Sign the MSI with code signing certificate

.EXAMPLE
    .\create-installer.ps1 -Configuration Release -SignInstaller
#>

param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [switch]$SignInstaller
)

$ErrorActionPreference = 'Stop'

$SolutionRoot = Split-Path -Parent $PSScriptRoot
$InstallerProject = Join-Path $SolutionRoot "src\installer\SecureHostInstaller.wixproj"
$DistPath = Join-Path $SolutionRoot "dist\$Configuration"

Write-Host "`nSecureHost Installer Builder" -ForegroundColor Cyan
Write-Host "============================`n"

# Check for WiX Toolset
Write-Host "Checking for WiX Toolset..." -ForegroundColor Yellow

$wixPath = "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin"
if (-not (Test-Path $wixPath)) {
    $wixPath = "${env:ProgramFiles}\WiX Toolset v3.14\bin"
}

if (-not (Test-Path $wixPath)) {
    Write-Host "ERROR: WiX Toolset v3.14 not found." -ForegroundColor Red
    Write-Host "Download from: https://wixtoolset.org/releases/`n" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found WiX Toolset: $wixPath`n" -ForegroundColor Green

# Check for MSBuild
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1

if (-not $msbuild) {
    Write-Host "ERROR: MSBuild not found." -ForegroundColor Red
    exit 1
}

Write-Host "Using MSBuild: $msbuild`n"

# Verify binaries exist
Write-Host "Verifying binaries..." -ForegroundColor Yellow

$binPath = Join-Path $SolutionRoot "bin\x64\$Configuration"
$requiredFiles = @(
    "SecureHostCore.dll",
    "SecureHostService.exe",
    "SecureHostGUI.exe",
    "SecureHostCLI.exe",
    "SecureHostWFP.sys",
    "SecureHostDevice.sys"
)

$missingFiles = @()
foreach ($file in $requiredFiles) {
    $filePath = Join-Path $binPath $file
    if (-not (Test-Path $filePath)) {
        $missingFiles += $file
        Write-Host "  [MISSING] $file" -ForegroundColor Red
    } else {
        Write-Host "  [OK] $file" -ForegroundColor Green
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host "`nERROR: Missing required files. Please build the solution first:" -ForegroundColor Red
    Write-Host "  .\build\build-all.ps1 -Configuration $Configuration`n"
    exit 1
}

Write-Host "`nAll required files found." -ForegroundColor Green

# Create resources directory
Write-Host "`nCreating installer resources..." -ForegroundColor Yellow

$resourcesPath = Join-Path $SolutionRoot "src\installer\Resources"
if (-not (Test-Path $resourcesPath)) {
    New-Item -ItemType Directory -Path $resourcesPath -Force | Out-Null
}

# Create placeholder license RTF (should be replaced with actual license)
$licenseRtf = Join-Path $resourcesPath "License.rtf"
if (-not (Test-Path $licenseRtf)) {
    @"
{\rtf1\ansi\deff0
{\fonttbl{\f0 Arial;}}
\f0\fs20
SecureHost Control Suite - End User License Agreement

Copyright (c) 2025 SecureHost Control Suite

THIS SOFTWARE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND.

See LICENSE.md for complete terms.
}
"@ | Out-File -FilePath $licenseRtf -Encoding ascii
}

# Create placeholder images (should be replaced with actual branding)
# Banner: 493x58 pixels
# Dialog: 493x312 pixels
Write-Host "Note: Using placeholder images. Replace with branding in src\installer\Resources\`n" -ForegroundColor Yellow

# Build installer
Write-Host "Building MSI installer..." -ForegroundColor Yellow
Write-Host "Configuration: $Configuration`n"

try {
    & $msbuild $InstallerProject `
        /p:Configuration=$Configuration `
        /p:Platform=x64 `
        /v:minimal `
        /nologo

    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE"
    }

    Write-Host "`nInstaller built successfully!" -ForegroundColor Green
} catch {
    Write-Host "`nERROR: Failed to build installer: $_" -ForegroundColor Red
    exit 1
}

# Find MSI file
$msiFile = Get-ChildItem -Path $DistPath -Filter "*.msi" | Select-Object -First 1

if (-not $msiFile) {
    Write-Host "ERROR: MSI file not found in $DistPath" -ForegroundColor Red
    exit 1
}

Write-Host "MSI created: $($msiFile.FullName)" -ForegroundColor Green
Write-Host "Size: $([Math]::Round($msiFile.Length / 1MB, 2)) MB`n"

# Sign installer if requested
if ($SignInstaller) {
    Write-Host "Signing installer..." -ForegroundColor Yellow

    $signtool = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\*\x64\signtool.exe" |
        Get-ChildItem | Sort-Object -Descending | Select-Object -First 1

    if (-not $signtool) {
        Write-Host "WARNING: signtool.exe not found. Skipping signature." -ForegroundColor Yellow
    } else {
        # Find code signing certificate
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
            $_.EnhancedKeyUsageList -match "Code Signing"
        } | Select-Object -First 1

        if ($cert) {
            Write-Host "Using certificate: $($cert.Subject)`n"

            & $signtool sign /v /fd SHA256 /sha1 $cert.Thumbprint `
                /t http://timestamp.digicert.com `
                /d "SecureHost Control Suite" `
                /du "https://www.securehost.example.com" `
                $msiFile.FullName

            if ($LASTEXITCODE -eq 0) {
                Write-Host "`nInstaller signed successfully!" -ForegroundColor Green
            } else {
                Write-Host "WARNING: Failed to sign installer." -ForegroundColor Yellow
            }
        } else {
            Write-Host "WARNING: No code signing certificate found." -ForegroundColor Yellow
        }
    }
}

# Generate checksums
Write-Host "`nGenerating checksums..." -ForegroundColor Yellow

$sha256 = (Get-FileHash -Path $msiFile.FullName -Algorithm SHA256).Hash
$checksumFile = Join-Path $DistPath "checksums.txt"

@"
SecureHost Control Suite v1.0.0 Checksums
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')

File: $($msiFile.Name)
SHA256: $sha256
"@ | Out-File -FilePath $checksumFile -Encoding utf8

Write-Host "SHA256: $sha256"
Write-Host "Checksums saved to: $checksumFile`n"

# Installation instructions
Write-Host "============================`n" -ForegroundColor Cyan
Write-Host "Installation Instructions:" -ForegroundColor Green
Write-Host "`n1. Interactive install:`n   msiexec /i `"$($msiFile.FullName)`"`n"
Write-Host "2. Silent install:`n   msiexec /i `"$($msiFile.FullName)`" /qn /l*v install.log`n"
Write-Host "3. Install without drivers (user-mode only):`n   msiexec /i `"$($msiFile.FullName)`" INSTALL_DRIVERS=0 /qn`n"
Write-Host "4. Uninstall:`n   msiexec /x `"$($msiFile.FullName)`" /qn`n"

Write-Host "Note: Drivers require WHQL signature or test signing for installation." -ForegroundColor Yellow
Write-Host "See docs\deployment.md for detailed instructions.`n"

Write-Host "============================`n" -ForegroundColor Cyan
Write-Host "Installer creation completed successfully!" -ForegroundColor Green
Write-Host "Output: $($msiFile.FullName)`n"
