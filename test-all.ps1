# SecureHost Control Suite - Complete Test Script
# This script tests all components to verify everything is working

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SecureHost Control Suite - System Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$allPassed = $true

# Test 1: Check .NET SDK
Write-Host "[1/6] Checking .NET SDK..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version 2>$null
if ($dotnetVersion) {
    Write-Host "✅ .NET SDK $dotnetVersion installed" -ForegroundColor Green
} else {
    Write-Host "❌ .NET SDK not found!" -ForegroundColor Red
    $allPassed = $false
}
Write-Host ""

# Test 2: Check if all components are built
Write-Host "[2/6] Checking built components..." -ForegroundColor Yellow

$components = @{
    "Core Library" = "src\core\SecureHostCore\bin\AnyCPU\Release\SecureHostCore.dll"
    "Service" = "src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe"
    "CLI Client" = "src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe"
    "GUI Client" = "src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe"
}

foreach ($name in $components.Keys) {
    $path = $components[$name]
    if (Test-Path $path) {
        $file = Get-Item $path
        Write-Host "  ✅ $name" -ForegroundColor Green
        Write-Host "     Size: $([math]::Round($file.Length / 1KB, 2)) KB | Modified: $($file.LastWriteTime.ToString('yyyy-MM-dd HH:mm'))" -ForegroundColor Gray
    } else {
        Write-Host "  ❌ $name not found at: $path" -ForegroundColor Red
        $allPassed = $false
    }
}
Write-Host ""

# Test 3: Check if service is running
Write-Host "[3/6] Checking if service is running..." -ForegroundColor Yellow
$serviceProcess = Get-Process SecureHostService -ErrorAction SilentlyContinue

if ($serviceProcess) {
    Write-Host "✅ Service is running (PID: $($serviceProcess.Id))" -ForegroundColor Green
    Write-Host "   Memory: $([math]::Round($serviceProcess.WorkingSet64 / 1MB, 2)) MB" -ForegroundColor Gray
} else {
    Write-Host "⚠️  Service is NOT running" -ForegroundColor Yellow
    Write-Host "   To start: Run as Administrator:" -ForegroundColor Gray
    Write-Host "   .\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe" -ForegroundColor White
}
Write-Host ""

# Test 4: Test API connectivity (if service is running)
Write-Host "[4/6] Testing API connectivity..." -ForegroundColor Yellow
if ($serviceProcess) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5555/api/status" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            Write-Host "✅ API is accessible on http://localhost:5555" -ForegroundColor Green
            $status = $response.Content | ConvertFrom-Json
            Write-Host "   Version: $($status.version)" -ForegroundColor Gray
            Write-Host "   Total Rules: $($status.totalRules)" -ForegroundColor Gray
        }
    } catch {
        Write-Host "❌ API not accessible: $($_.Exception.Message)" -ForegroundColor Red
        $allPassed = $false
    }
} else {
    Write-Host "⚠️  Skipped (service not running)" -ForegroundColor Yellow
}
Write-Host ""

# Test 5: Test CLI
Write-Host "[5/6] Testing CLI client..." -ForegroundColor Yellow
if (Test-Path "src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe") {
    if ($serviceProcess) {
        try {
            $cliOutput = & "src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe" status 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ CLI is working" -ForegroundColor Green
                Write-Host "   Output preview:" -ForegroundColor Gray
                $cliOutput | Select-Object -First 5 | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
            } else {
                Write-Host "❌ CLI exited with error code $LASTEXITCODE" -ForegroundColor Red
                $allPassed = $false
            }
        } catch {
            Write-Host "❌ CLI test failed: $($_.Exception.Message)" -ForegroundColor Red
            $allPassed = $false
        }
    } else {
        Write-Host "⚠️  CLI exists but service not running" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ CLI not built" -ForegroundColor Red
    $allPassed = $false
}
Write-Host ""

# Test 6: Test GUI (just check if it loads without crashing)
Write-Host "[6/6] Testing GUI client..." -ForegroundColor Yellow
if (Test-Path "src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe") {
    Write-Host "✅ GUI executable exists" -ForegroundColor Green
    Write-Host "   To launch manually:" -ForegroundColor Gray
    Write-Host "   .\src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe" -ForegroundColor White
    Write-Host ""
    Write-Host "   Testing GUI startup (will close automatically in 3 seconds)..." -ForegroundColor Gray

    # Launch GUI in background and kill after 3 seconds
    $guiJob = Start-Job -ScriptBlock {
        param($exePath)
        & $exePath
    } -ArgumentList (Resolve-Path "src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe").Path

    Start-Sleep -Seconds 3

    # Check if GUI process started
    $guiProcess = Get-Process SecureHostGUI -ErrorAction SilentlyContinue
    if ($guiProcess) {
        Write-Host "   ✅ GUI launched successfully" -ForegroundColor Green
        Stop-Process -Id $guiProcess.Id -Force
        Write-Host "   (Closed test window)" -ForegroundColor Gray
    } else {
        # Check if job failed
        $jobState = $guiJob.State
        if ($jobState -eq "Failed") {
            Write-Host "   ❌ GUI failed to start" -ForegroundColor Red
            Receive-Job -Job $guiJob -ErrorAction SilentlyContinue
            $allPassed = $false
        } else {
            Write-Host "   ⚠️  GUI status unclear (job state: $jobState)" -ForegroundColor Yellow
        }
    }

    Stop-Job -Job $guiJob -ErrorAction SilentlyContinue
    Remove-Job -Job $guiJob -ErrorAction SilentlyContinue
} else {
    Write-Host "❌ GUI not built" -ForegroundColor Red
    $allPassed = $false
}
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
if ($allPassed -and $serviceProcess) {
    Write-Host "✅ ALL TESTS PASSED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Your SecureHost Control Suite is fully operational!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Launch GUI:" -ForegroundColor White
    Write-Host "     .\src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  2. Use CLI commands:" -ForegroundColor White
    Write-Host "     .\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules list" -ForegroundColor Gray
} elseif (-not $serviceProcess) {
    Write-Host "⚠️  TESTS INCOMPLETE - Service Not Running" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To start the service (requires Administrator):" -ForegroundColor Yellow
    Write-Host "  .\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe" -ForegroundColor White
} else {
    Write-Host "❌ SOME TESTS FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please check the errors above and rebuild components if needed." -ForegroundColor Yellow
}
Write-Host "========================================" -ForegroundColor Cyan
