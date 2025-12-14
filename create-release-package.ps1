# SecureHost Control Suite - Release Package Creator
# Creates a distributable ZIP file ready for GitHub release

param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = ".\release-package"
)

Write-Host "Creating SecureHost Control Suite Release Package v$Version" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

# Clean and create output directory
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Create directory structure
$packageRoot = Join-Path $OutputDir "SecureHostSuite-$Version"
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path "$packageRoot\Service" -Force | Out-Null
New-Item -ItemType Directory -Path "$packageRoot\GUI" -Force | Out-Null
New-Item -ItemType Directory -Path "$packageRoot\CLI" -Force | Out-Null
New-Item -ItemType Directory -Path "$packageRoot\Docs" -Force | Out-Null

Write-Host "[1/5] Building all components..." -ForegroundColor Yellow

# Build all projects in Release mode
$buildProjects = @(
    "src\core\SecureHostCore\SecureHostCore.csproj",
    "src\service\SecureHostService\SecureHostService.csproj",
    "src\clients\SecureHostCLI\SecureHostCLI.csproj",
    "src\clients\SecureHostGUI\SecureHostGUI.csproj"
)

foreach ($project in $buildProjects) {
    Write-Host "  Building $project..." -ForegroundColor Gray
    dotnet build $project --configuration Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed for $project" -ForegroundColor Red
        exit 1
    }
}

Write-Host "  ✓ All builds successful" -ForegroundColor Green
Write-Host ""

Write-Host "[2/5] Copying Service files..." -ForegroundColor Yellow
$serviceSrc = "src\service\SecureHostService\bin\AnyCPU\Release\win-x64"
Copy-Item "$serviceSrc\*" -Destination "$packageRoot\Service" -Recurse -Force
Write-Host "  ✓ Service files copied" -ForegroundColor Green
Write-Host ""

Write-Host "[3/5] Copying GUI files..." -ForegroundColor Yellow
$guiSrc = "src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64"
Copy-Item "$guiSrc\*" -Destination "$packageRoot\GUI" -Recurse -Force
Write-Host "  ✓ GUI files copied" -ForegroundColor Green
Write-Host ""

Write-Host "[4/5] Copying CLI files..." -ForegroundColor Yellow
$cliSrc = "src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64"
Copy-Item "$cliSrc\*" -Destination "$packageRoot\CLI" -Recurse -Force
Write-Host "  ✓ CLI files copied" -ForegroundColor Green
Write-Host ""

Write-Host "[5/5] Copying documentation..." -ForegroundColor Yellow
# Copy essential documentation
Copy-Item "README.md" -Destination "$packageRoot\README.md"
Copy-Item "HOW-TO-RUN.md" -Destination "$packageRoot\Docs\HOW-TO-RUN.md"
Copy-Item "DEVICE-CONTROL-AND-RESET.md" -Destination "$packageRoot\Docs\DEVICE-CONTROL-AND-RESET.md"
Copy-Item "USING-THE-GUI.md" -Destination "$packageRoot\Docs\USING-THE-GUI.md"

# Create quick start launcher scripts
$startServiceScript = @"
@echo off
echo ========================================
echo SecureHost Control Suite - Service
echo ========================================
echo.
echo Starting service (requires Administrator)...
echo.
cd /d "%~dp0Service"
SecureHostService.exe
pause
"@

$startGUIScript = @"
@echo off
echo ========================================
echo SecureHost Control Suite - GUI
echo ========================================
echo.
echo Starting GUI...
echo.
cd /d "%~dp0GUI"
start SecureHostGUI.exe
"@

$startServiceScript | Out-File -FilePath "$packageRoot\START-SERVICE.bat" -Encoding ASCII
$startGUIScript | Out-File -FilePath "$packageRoot\START-GUI.bat" -Encoding ASCII

# Create installation guide
$installGuide = @"
# SecureHost Control Suite v$Version

## Quick Start

1. **Run as Administrator**: Right-click 'START-SERVICE.bat' → Run as Administrator
2. **Open GUI**: Double-click 'START-GUI.bat'
3. **Read Documentation**: See Docs folder for complete guides

## Important Files

- **START-SERVICE.bat** - Starts the service (REQUIRES ADMIN)
- **START-GUI.bat** - Starts the GUI
- **Service/** - Contains service executable and dependencies
- **GUI/** - Contains GUI executable and dependencies
- **CLI/** - Contains command-line tools
- **Docs/** - Complete documentation

## Emergency Reset

If you block something critical:

### Using GUI:
1. Open GUI (START-GUI.bat)
2. Go to "Policy Rules" tab
3. Click red "⚠️ RESET ALL DEVICES" button

### Using CLI:
```
cd CLI
SecureHostCLI.exe system reset --force
```

## Documentation

- **README.md** - Project overview
- **Docs/HOW-TO-RUN.md** - Complete setup instructions
- **Docs/DEVICE-CONTROL-AND-RESET.md** - Device control and emergency recovery
- **Docs/USING-THE-GUI.md** - GUI usage guide

## Requirements

- Windows 10/11 (64-bit)
- .NET Runtime (included in package)
- Administrator privileges for service

## Support

For issues or questions, visit: https://github.com/yourusername/SecureHostSuite

---
Version: $Version
Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm")
"@

$installGuide | Out-File -FilePath "$packageRoot\INSTALLATION.md" -Encoding UTF8
Write-Host "  ✓ Documentation copied" -ForegroundColor Green
Write-Host ""

Write-Host "[6/6] Creating ZIP archive..." -ForegroundColor Yellow
$zipPath = Join-Path $OutputDir "SecureHostSuite-v$Version-Windows-x64.zip"
Compress-Archive -Path $packageRoot -DestinationPath $zipPath -Force
Write-Host "  ✓ ZIP created: $zipPath" -ForegroundColor Green
Write-Host ""

# Calculate size
$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✓ Release package created successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Package Location: $zipPath" -ForegroundColor White
Write-Host "Package Size: $($zipSize.ToString('0.00')) MB" -ForegroundColor White
Write-Host ""
Write-Host "Ready to upload to GitHub Releases!" -ForegroundColor Green
