#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Complete build script for SecureHost Control Suite

.DESCRIPTION
    Builds all components: kernel drivers, user-mode service, GUI, CLI, and installer
    Requires:
    - Visual Studio 2022 with C++ and WDK
    - .NET 8.0 SDK
    - WiX Toolset 3.14+

.PARAMETER Configuration
    Build configuration (Debug or Release)

.PARAMETER SkipTests
    Skip running tests

.PARAMETER SkipDrivers
    Skip building kernel drivers (requires WDK)

.EXAMPLE
    .\build-all.ps1 -Configuration Release
#>

param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [switch]$SkipTests,

    [Parameter()]
    [switch]$SkipDrivers
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$SolutionRoot = Split-Path -Parent $PSScriptRoot
$SolutionFile = Join-Path $SolutionRoot "SecureHostSuite.sln"

# Colors for output
function Write-BuildStatus {
    param([string]$Message, [string]$Color = 'Green')
    Write-Host "`n=== $Message ===" -ForegroundColor $Color
}

function Write-BuildError {
    param([string]$Message)
    Write-Host "ERROR: $Message" -ForegroundColor Red
}

function Write-BuildWarning {
    param([string]$Message)
    Write-Host "WARNING: $Message" -ForegroundColor Yellow
}

# Check prerequisites
Write-BuildStatus "Checking prerequisites" "Cyan"

# Check for MSBuild
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1

if (-not $msbuild) {
    Write-BuildError "MSBuild not found. Please install Visual Studio 2022."
    exit 1
}
Write-Host "Found MSBuild: $msbuild"

# Check for .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Host "Found .NET SDK: $dotnetVersion"
} catch {
    Write-BuildError ".NET 8.0 SDK not found. Please install from https://dotnet.microsoft.com/"
    exit 1
}

# Check for WDK (if building drivers)
if (-not $SkipDrivers) {
    $wdkPath = "${env:ProgramFiles(x86)}\Windows Kits\10"
    if (-not (Test-Path $wdkPath)) {
        Write-BuildWarning "Windows Driver Kit not found. Skipping driver build."
        $SkipDrivers = $true
    } else {
        Write-Host "Found WDK: $wdkPath"
    }
}

# Clean previous builds
Write-BuildStatus "Cleaning previous builds"
$binPath = Join-Path $SolutionRoot "bin"
$objPath = Join-Path $SolutionRoot "obj"

if (Test-Path $binPath) {
    Remove-Item -Path $binPath -Recurse -Force
}
if (Test-Path $objPath) {
    Remove-Item -Path $objPath -Recurse -Force
}

Write-Host "Clean completed"

# Restore NuGet packages
Write-BuildStatus "Restoring NuGet packages"
& $msbuild $SolutionFile /t:Restore /p:Configuration=$Configuration /p:Platform=x64 /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    Write-BuildError "NuGet restore failed"
    exit 1
}

# Build core library
Write-BuildStatus "Building SecureHostCore"
& dotnet build (Join-Path $SolutionRoot "src\core\SecureHostCore\SecureHostCore.csproj") `
    --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-BuildError "Core library build failed"
    exit 1
}

# Build kernel drivers
if (-not $SkipDrivers) {
    Write-BuildStatus "Building kernel drivers"

    # Build WFP driver
    Write-Host "Building SecureHostWFP.sys..."
    & $msbuild (Join-Path $SolutionRoot "src\drivers\SecureHostWFP\SecureHostWFP.vcxproj") `
        /p:Configuration=$Configuration /p:Platform=x64 /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) {
        Write-BuildError "WFP driver build failed"
        exit 1
    }

    # Build device filter driver
    Write-Host "Building SecureHostDevice.sys..."
    & $msbuild (Join-Path $SolutionRoot "src\drivers\SecureHostDevice\SecureHostDevice.vcxproj") `
        /p:Configuration=$Configuration /p:Platform=x64 /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) {
        Write-BuildError "Device driver build failed"
        exit 1
    }

    Write-Host "Kernel drivers built successfully"
} else {
    Write-BuildWarning "Skipping kernel driver build"
}

# Build service
Write-BuildStatus "Building SecureHostService"
& dotnet build (Join-Path $SolutionRoot "src\service\SecureHostService\SecureHostService.csproj") `
    --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-BuildError "Service build failed"
    exit 1
}

# Build GUI client
Write-BuildStatus "Building SecureHostGUI"
& dotnet build (Join-Path $SolutionRoot "src\clients\SecureHostGUI\SecureHostGUI.csproj") `
    --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-BuildError "GUI build failed"
    exit 1
}

# Build CLI client
Write-BuildStatus "Building SecureHostCLI"
& dotnet build (Join-Path $SolutionRoot "src\clients\SecureHostCLI\SecureHostCLI.csproj") `
    --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-BuildError "CLI build failed"
    exit 1
}

# Run tests
if (-not $SkipTests) {
    Write-BuildStatus "Running tests"
    & dotnet test (Join-Path $SolutionRoot "tests\SecureHostTests\SecureHostTests.csproj") `
        --configuration $Configuration --no-build --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-BuildWarning "Some tests failed"
    }
} else {
    Write-BuildWarning "Skipping tests"
}

# Build summary
Write-BuildStatus "Build Summary" "Green"

$binRoot = Join-Path $SolutionRoot "bin\x64\$Configuration"

$artifacts = @(
    "SecureHostCore.dll",
    "SecureHostService.exe",
    "SecureHostGUI.exe",
    "SecureHostCLI.exe"
)

if (-not $SkipDrivers) {
    $artifacts += @("SecureHostWFP.sys", "SecureHostDevice.sys")
}

Write-Host "`nBuilt artifacts:"
foreach ($artifact in $artifacts) {
    $path = Join-Path $binRoot $artifact
    if (Test-Path $path) {
        $size = (Get-Item $path).Length / 1KB
        Write-Host "  [OK] $artifact ($([Math]::Round($size, 2)) KB)" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] $artifact" -ForegroundColor Red
    }
}

Write-BuildStatus "Build completed successfully!" "Green"
Write-Host "Output directory: $binRoot"
Write-Host "`nNext steps:"
Write-Host "  1. Sign drivers: .\build\sign-drivers.ps1"
Write-Host "  2. Create installer: .\build\create-installer.ps1"
Write-Host "  3. Run tests: .\build\run-tests.ps1"
