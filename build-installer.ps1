# SecureHost Control Suite - Installer Build Script
# This script builds the MSI installer using WiX Toolset

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Write-Host "SecureHost Installer Build Script" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Check if WiX is installed
Write-Host "[1/5] Checking WiX Toolset installation..." -ForegroundColor Yellow

$wixPaths = @(
    "${env:WIX}bin\candle.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin\candle.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin\candle.exe"
)

$candleExe = $null
foreach ($path in $wixPaths) {
    if (Test-Path $path) {
        $candleExe = $path
        break
    }
}

if (-not $candleExe) {
    Write-Host "❌ WiX Toolset not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "WiX Toolset is required to build the MSI installer." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Download and install WiX from:" -ForegroundColor Yellow
    Write-Host "https://wixtoolset.org/releases/" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Recommended: WiX Toolset v3.14" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host "✅ WiX found at: $candleExe" -ForegroundColor Green
$lightExe = $candleExe -replace "candle.exe", "light.exe"
Write-Host ""

# Check if all .NET components are built
Write-Host "[2/5] Checking if .NET components are built..." -ForegroundColor Yellow

$requiredBinaries = @(
    "src\core\SecureHostCore\bin\AnyCPU\$Configuration\SecureHostCore.dll",
    "src\service\SecureHostService\bin\AnyCPU\$Configuration\win-x64\SecureHostService.exe",
    "src\clients\SecureHostCLI\bin\AnyCPU\$Configuration\win-x64\SecureHostCLI.exe",
    "src\clients\SecureHostGUI\bin\AnyCPU\$Configuration\win-x64\SecureHostGUI.exe"
)

$allBuilt = $true
foreach ($binary in $requiredBinaries) {
    if (-not (Test-Path $binary)) {
        Write-Host "❌ Missing: $binary" -ForegroundColor Red
        $allBuilt = $false
    }
}

if (-not $allBuilt) {
    Write-Host ""
    Write-Host "Not all components are built. Building now..." -ForegroundColor Yellow
    Write-Host ""

    dotnet build src/core/SecureHostCore/SecureHostCore.csproj --configuration $Configuration
    dotnet build src/service/SecureHostService/SecureHostService.csproj --configuration $Configuration
    dotnet build src/clients/SecureHostCLI/SecureHostCLI.csproj --configuration $Configuration
    dotnet build src/clients/SecureHostGUI/SecureHostGUI.csproj --configuration $Configuration

    Write-Host ""
}

Write-Host "✅ All .NET components are built" -ForegroundColor Green
Write-Host ""

# Build the installer using WiX tools directly
Write-Host "[3/5] Building MSI installer with WiX..." -ForegroundColor Yellow

# Try to find MSBuild first
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null

if (Test-Path $vsWhere) {
    $msbuild = & $vsWhere -latest -requires Microsoft.Component.MSBuild `
        -find MSBuild\**\Bin\MSBuild.exe -prerelease 2>$null | Select-Object -First 1
}

if ($msbuild -and (Test-Path $msbuild)) {
    # Use MSBuild if available
    Write-Host "Using Visual Studio MSBuild: $msbuild" -ForegroundColor Gray

    $projectFile = "src\installer\SecureHostInstaller.wixproj"
    $buildArgs = @(
        $projectFile,
        "/p:Configuration=$Configuration",
        "/p:Platform=x64",
        "/v:minimal"
    )

    Write-Host "Command: $msbuild $buildArgs" -ForegroundColor Gray
    Write-Host ""

    & $msbuild @buildArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "❌ MSBuild failed, falling back to WiX tools..." -ForegroundColor Yellow
        $msbuild = $null
    }
}

if (-not $msbuild) {
    # Use WiX tools directly
    Write-Host "Using WiX tools directly (candle + light)" -ForegroundColor Yellow
    Write-Host ""

    # Setup paths
    $wixDir = Split-Path $candleExe
    $sourceDir = (Get-Location).Path
    $binDir = "$sourceDir\bin\AnyCPU\$Configuration"
    $objDir = "$sourceDir\obj\installer\$Configuration"
    $outDir = "$sourceDir\dist\$Configuration"

    # Create output directories
    New-Item -ItemType Directory -Force -Path $objDir | Out-Null
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    # Compile WiX source
    Write-Host "Step 1: Compiling WiX sources (candle.exe)..." -ForegroundColor Gray
    $candleArgs = @(
        '-nologo'
        "-dConfiguration=$Configuration"
        "-dBinDir=$binDir\win-x64"
        "-dSourceDir=$sourceDir"
        '-out'
        "$objDir\"
        '-arch'
        'x64'
        '-ext'
        "$wixDir\WixUtilExtension.dll"
        '-ext'
        "$wixDir\WixFirewallExtension.dll"
        '-ext'
        "$wixDir\WixUIExtension.dll"
        'src\installer\Product.wxs'
    )

    & $candleExe @candleArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "❌ Candle (WiX compiler) failed!" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    # Link to create MSI
    Write-Host "Step 2: Linking MSI (light.exe)..." -ForegroundColor Gray
    $lightArgs = @(
        '-nologo'
        '-out'
        "$outDir\SecureHostSuite-1.0.0.msi"
        '-ext'
        "$wixDir\WixUtilExtension.dll"
        '-ext'
        "$wixDir\WixFirewallExtension.dll"
        '-ext'
        "$wixDir\WixUIExtension.dll"
        '-sice:ICE61'
        '-sw1076'
        "$objDir\Product.wixobj"
    )

    & $lightExe @lightArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "❌ Light (WiX linker) failed!" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host ""
Write-Host "✅ Installer built successfully" -ForegroundColor Green
Write-Host ""

# Verify output
Write-Host "[4/5] Verifying installer output..." -ForegroundColor Yellow

$msiPath = "dist\$Configuration\SecureHostSuite-1.0.0.msi"

if (Test-Path $msiPath) {
    $msiFile = Get-Item $msiPath
    Write-Host "✅ MSI created: $msiPath" -ForegroundColor Green
    Write-Host "   Size: $([math]::Round($msiFile.Length / 1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "   Modified: $($msiFile.LastWriteTime)" -ForegroundColor Gray
    Write-Host ""
} else {
    Write-Host "❌ MSI file not found at expected location: $msiPath" -ForegroundColor Red
    exit 1
}

# Summary
Write-Host "[5/5] Build Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "MSI INSTALLER READY" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Location: $msiPath" -ForegroundColor White
Write-Host ""
Write-Host "To install (requires administrator):" -ForegroundColor Yellow
Write-Host "  msiexec /i $msiPath" -ForegroundColor White
Write-Host ""
Write-Host "Silent install:" -ForegroundColor Yellow
Write-Host "  msiexec /i $msiPath /qn" -ForegroundColor White
Write-Host ""
Write-Host "Install with logging:" -ForegroundColor Yellow
Write-Host "  msiexec /i $msiPath /l*v install.log" -ForegroundColor White
Write-Host ""
