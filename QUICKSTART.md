# SecureHost Control Suite - QUICK START GUIDE

This guide contains the **actual, tested commands** to build and run the SecureHost Control Suite on your system.

---

## ✅ VERIFIED SYSTEM REQUIREMENTS

Your system must have:
- ✅ Windows 10/11 (x64)
- ✅ .NET SDK 8.0 or later (you have 10.0.101)
- ✅ Administrator privileges

**Optional** (for full build including drivers and installer):
- Visual Studio 2022 with C++ Desktop Development workload
- Windows Driver Kit (WDK) 11
- WiX Toolset v3.14 or later

---

## STEP-BY-STEP: BUILD AND RUN

### STEP 1: Build All .NET Components

Open PowerShell (any user, admin not required for building):

```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"

# Build Core Library
dotnet build src/core/SecureHostCore/SecureHostCore.csproj --configuration Release

# Build Service
dotnet build src/service/SecureHostService/SecureHostService.csproj --configuration Release

# Build CLI Client
dotnet build src/clients/SecureHostCLI/SecureHostCLI.csproj --configuration Release

# Build GUI Client
dotnet build src/clients/SecureHostGUI/SecureHostGUI.csproj --configuration Release
```

**Expected Result:**
```
Build succeeded in X.Xs
```

**If it fails:**
- Check error message for missing dependencies
- Ensure .NET SDK is installed: `dotnet --version`
- Close any running SecureHost processes before rebuilding

---

### STEP 2: Start the Service

**IMPORTANT:** Must run as Administrator!

Open PowerShell as Administrator:
```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe
```

**Expected Output:**
```
info: SecureHost Control Suite Service starting...
info: Version: 1.0.0 | Build: 2025-12-14
info: Service is running with elevated privileges
warn: Kernel drivers not available - running in user-mode only
info: Loaded 3 policy rules
info: API server listening on: http://localhost:5555
info: SecureHost Worker Service is running
```

**Keep this PowerShell window open** - the service is running.

**If it fails:**
- "Access Denied" → Run as Administrator
- "Port already in use" → Another instance is running. Kill it:
  ```powershell
  Get-Process SecureHostService | Stop-Process -Force
  ```
- "Driver health check failed" → This is NORMAL. Drivers aren't installed yet.

---

### STEP 3: Run the GUI Client

Open a **NEW** PowerShell window (can be regular user, not admin):

```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe
```

**Expected Result:**
- Window opens showing "SecureHost Control Suite" dashboard
- Green status indicator in top-right (shows "Connected")
- Tabs: Dashboard, Policy Rules, Network, Audit Logs
- Dashboard shows:
  - Service Status: Running
  - Total Rules: 3
  - Active Rules: 3

**If GUI doesn't open:**
- Check for error message box
- If nothing happens, check Windows Event Viewer:
  ```powershell
  Get-EventLog -LogName Application -Source "Application Error" -Newest 5 | Where-Object {$_.Message -like "*SecureHostGUI*"}
  ```
- Make sure service from STEP 2 is still running

---

### STEP 4: Test with CLI Client

In the same PowerShell window from STEP 3:

```powershell
# Show service status
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe status

# List all policy rules
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules list

# Show active network connections
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe network connections

# Export audit logs
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe audit export --output audit.json
```

**Expected Output:**
```
┌──────────────┬──────────────────┐
│ Property     │ Value            │
├──────────────┼──────────────────┤
│ Service      │ running          │
│ Version      │ 1.0.0            │
│ Machine      │ DESKTOP-XXXXXX   │
│ Uptime       │ 00:XX:XX         │
│ Total Rules  │ 3                │
│ Active Rules │ 3                │
└──────────────┴──────────────────┘
```

---

## CLI COMMAND REFERENCE

### General
```powershell
SecureHostCLI.exe --help              # Show all commands
SecureHostCLI.exe --version           # Show version
```

### Service Status
```powershell
SecureHostCLI.exe status              # Service status and stats
```

### Policy Rules
```powershell
SecureHostCLI.exe rules --help        # Show rules commands
SecureHostCLI.exe rules list          # List all rules
SecureHostCLI.exe rules view --id 1   # View specific rule
SecureHostCLI.exe rules add [options] # Add new rule
SecureHostCLI.exe rules delete --id 1 # Delete rule
```

### Network Monitoring
```powershell
SecureHostCLI.exe network --help          # Show network commands
SecureHostCLI.exe network connections     # Active connections
SecureHostCLI.exe network listeners       # Listening ports
```

### Audit Logs
```powershell
SecureHostCLI.exe audit --help                           # Show audit commands
SecureHostCLI.exe audit export --output logs.json        # Export all logs
SecureHostCLI.exe audit export --output logs.json --start "2025-12-01" --end "2025-12-14"
```

---

## TROUBLESHOOTING

### Problem: Build Fails with "WiX Toolset" Error

**Error:**
```
error The WiX Toolset v3.14 (or newer) build tools must be installed
```

**Solution:**
Don't use `dotnet build` or `dotnet clean` on the entire solution. Build only .NET projects individually (see STEP 1).

**Why:** The solution includes WiX installer and C++ driver projects that require Visual Studio and WDK. These are optional.

---

### Problem: Build Fails with "Microsoft.Cpp.Default.props" Error

**Error:**
```
error MSB4278: The imported file "$(VCTargetsPath)\Microsoft.Cpp.Default.props" does not exist
```

**Solution:**
Build only .NET projects individually (see STEP 1).

**Why:** Driver projects require Visual Studio with C++ workload installed.

---

### Problem: "File is being used by another process"

**Error:**
```
error MSB3021: Unable to copy file ... because it is being used by another process
```

**Solution:**
1. Stop the running service (Ctrl+C in service window)
2. Rebuild
3. Restart the service

---

### Problem: GUI Opens Then Closes Immediately

**Cause:** Material Design resource issue or service not running

**Solution:**
1. Rebuild GUI (this has been fixed):
   ```powershell
   dotnet build src/clients/SecureHostGUI/SecureHostGUI.csproj --configuration Release
   ```
2. Make sure service is running first (STEP 2)
3. Try running with dotnet to see errors:
   ```powershell
   dotnet src/clients/SecureHostGUI/bin/AnyCPU/Release/win-x64/SecureHostGUI.dll
   ```

---

### Problem: GUI Shows "Disconnected" Status

**Cause:** Service is not running or using different port

**Solution:**
1. Check service is running (STEP 2)
2. Verify API server port in service output (should show `http://localhost:5555`)
3. If port is different, update `src/clients/SecureHostGUI/Services/ApiClient.cs` line 16

---

## ADVANCED: Building Kernel Drivers (Optional)

**Prerequisites:**
- Visual Studio 2022 with C++ Desktop Development
- Windows Driver Kit (WDK) 11.0
- WDK Visual Studio Extension

**Commands:**
```powershell
# Build drivers using MSBuild (not dotnet)
cd "c:\Users\hp\Desktop\SecureHost Control Suite"

# Build WFP driver
msbuild src/drivers/SecureHostWFP/SecureHostWFP.vcxproj /p:Configuration=Release /p:Platform=x64

# Build Device driver
msbuild src/drivers/SecureHostDevice/SecureHostDevice.vcxproj /p:Configuration=Release /p:Platform=x64
```

**Installing Drivers (Test Mode Only):**
```powershell
# Enable test signing (REQUIRES REBOOT)
bcdedit /set testsigning on
Restart-Computer

# Install drivers
sc create SecureHostWFP type=kernel binPath="C:\path\to\SecureHostWFP.sys"
sc start SecureHostWFP
```

**WARNING:** Never use test signing in production!

---

## ADVANCED: Building MSI Installer (Optional)

**Prerequisites:**
- WiX Toolset v3.14 installed
- Drivers built (if including them)
- All .NET components built

**Commands:**
```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"

# Build installer
.\build\create-installer.ps1 -Configuration Release
```

**Output:**
- MSI file: `dist\Release\SecureHostSuite-1.0.0.msi`

**Install:**
```powershell
msiexec /i dist\Release\SecureHostSuite-1.0.0.msi
```

---

## FILE LOCATIONS

After building, executables are located at:

```
bin/AnyCPU/Release/win-x64/
├── SecureHostService.exe    (Service)
├── SecureHostCLI.exe        (CLI Client)
└── SecureHostGUI.exe        (GUI Client)
```

Full paths:
```
Service: src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe
CLI:     src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe
GUI:     src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe
```

Audit logs:
```
C:\ProgramData\SecureHost\Audit\audit.jsonl
```

Policy storage:
```
C:\ProgramData\SecureHost\Config\*.dat (encrypted)
```

---

## 🔒 CURRENT LIMITATIONS

**What's Working:**
- ✅ User-mode service with policy engine
- ✅ REST API on http://localhost:5555
- ✅ GUI client (WPF)
- ✅ CLI client
- ✅ Network connection monitoring (user-mode via netstat)
- ✅ Device enumeration (WMI)
- ✅ Audit logging (file-based)
- ✅ Encrypted policy storage

**What's NOT Working Yet:**
- ❌ Kernel drivers not installed (service runs in user-mode)
- ❌ No actual network blocking (would require WFP driver)
- ❌ No actual device blocking (would require filter driver)
- ❌ Audit logging is file-based, not ETW (requires signed service)

**To Enable Full Enforcement:**
1. Build and sign kernel drivers
2. Enable test signing (dev) or WHQL signing (prod)
3. Install drivers
4. Restart service

---

## NEXT STEPS

1. **Test User-Mode Functionality:**
   - Add/remove policy rules via GUI
   - View network connections
   - Export audit logs

2. **Build Drivers (Optional):**
   - Install Visual Studio 2022 + WDK
   - Build driver projects
   - Sign drivers (test or production)
   - Install and test

3. **Create Installer (Optional):**
   - Install WiX Toolset
   - Run create-installer.ps1
   - Test MSI installation

4. **Deploy to Production:**
   - Obtain EV code signing certificate
   - WHQL sign drivers
   - Create production MSI
   - Deploy via SCCM/Intune

