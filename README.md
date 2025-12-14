# SecureHost Control Suite

A Windows security control system that lets you manage network connections, device access, and enforce policies on your machine.

---

## 🔴 EMERGENCY? Accidentally blocked something critical?

**→ See [DEVICE-CONTROL-AND-RESET.md](DEVICE-CONTROL-AND-RESET.md) for instant recovery!**

Quick reset command:
```powershell
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force
```

---

## Want to run this? Read [HOW-TO-RUN.md](HOW-TO-RUN.md)

The [HOW-TO-RUN.md](HOW-TO-RUN.md) file has step-by-step instructions with all the commands you need.

**New to device control?** Read [DEVICE-CONTROL-AND-RESET.md](DEVICE-CONTROL-AND-RESET.md) to understand how it works!

---

## What This Does

SecureHost lets you:
- **Control network access** - Block or allow connections by process, IP address, or port
- **Manage device access** - Control camera, microphone, USB, and Bluetooth
- **Set policies** - Create rules for what's allowed and what's blocked
- **Audit everything** - Every action gets logged for review

## How It Works

```
┌─────────────────────────────────────────┐
│           GUI or CLI Client             │  ← You interact with this
│  (Manage rules, view connections, etc)  │
└────────────────┬────────────────────────┘
                 │
                 ↓
┌─────────────────────────────────────────┐
│       SecureHost Service (Windows)      │  ← Main service (runs as SYSTEM)
│  • Policy Engine - decides allow/block  │
│  • REST API - http://localhost:5555     │
│  • Audit Logger - tracks everything     │
└────────────────┬────────────────────────┘
                 │
                 ↓
┌──────────────────────────────────────────┐
│    Kernel Drivers (optional, advanced)   │  ← For actual enforcement
│  • Network filter driver (WFP)           │
│  • Device filter driver (KMDF)           │
└──────────────────────────────────────────┘
```

## What's Included

### Working Right Now:
- ✅ **Windows Service** - Runs in background, manages policies
- ✅ **GUI Application** - WPF interface with dark theme
- ✅ **Command Line Tool** - Full CLI for scripting
- ✅ **REST API** - HTTP API for remote management
- ✅ **Network Monitoring** - See active connections
- ✅ **Device Enumeration** - List connected devices
- ✅ **Audit Logging** - JSON logs of all activities
- ✅ **Policy Storage** - Encrypted rule storage

### Not Installed (Advanced):
- ❌ **Kernel Drivers** - Need Windows Driver Kit and code signing
- ❌ **Actual Blocking** - Requires drivers to be installed
- ❌ **MSI Installer** - Can be built if you have WiX Toolset

The user-mode components work fully. The kernel drivers are for production deployment where you need tamper-proof enforcement.

## Quick Start

```powershell
# 1. Build everything
dotnet build src/core/SecureHostCore/SecureHostCore.csproj --configuration Release
dotnet build src/service/SecureHostService/SecureHostService.csproj --configuration Release
dotnet build src/clients/SecureHostCLI/SecureHostCLI.csproj --configuration Release
dotnet build src/clients/SecureHostGUI/SecureHostGUI.csproj --configuration Release

# 2. Start the service (PowerShell as Admin)
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe

# 3. Open the GUI (new PowerShell window)
.\src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe

# 4. Or use CLI
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe status
```

See [HOW-TO-RUN.md](HOW-TO-RUN.md) for detailed instructions and troubleshooting.

## Components

### 1. Core Library (`SecureHostCore`)
- Policy engine - evaluates rules and makes allow/deny decisions
- Audit engine - logs events to JSON files
- Storage - encrypted policy storage using Windows DPAPI

### 2. Service (`SecureHostService`)
- Windows service that runs as SYSTEM
- REST API server on http://localhost:5555
- Manages policy rules
- Monitors network and devices
- Talks to kernel drivers (when installed)

### 3. GUI Client (`SecureHostGUI`)
- WPF application with Material Design
- Dashboard showing service status
- Rule management interface
- Network connection viewer
- Audit log export

### 4. CLI Client (`SecureHostCLI`)
- Command-line tool for automation
- Commands: status, rules, network, audit
- Useful for scripts and remote management

### 5. Kernel Drivers (Optional)
- **SecureHostWFP.sys** - Windows Filtering Platform driver for network enforcement
- **SecureHostDevice.sys** - Filter driver for device control
- Need to be built separately with Visual Studio + WDK
- Require code signing (test mode or WHQL signature)

## Default Policy Rules

When you first run it, you get 3 default rules:

1. **Block Camera Access** - Prevents apps from using webcam
2. **Block Microphone Access** - Prevents apps from using mic
3. **Audit Non-Standard Outbound Connections** - Logs unusual network activity

You can add, edit, or delete these through the GUI or CLI.

## Files and Folders

```
Windows Suit/
├── src/
│   ├── core/SecureHostCore/          # Policy and audit engine
│   ├── service/SecureHostService/    # Windows service
│   ├── clients/
│   │   ├── SecureHostGUI/            # GUI app
│   │   └── SecureHostCLI/            # Command line
│   ├── drivers/                      # Kernel drivers (C++)
│   └── installer/                    # WiX MSI installer
├── docs/                             # Documentation
├── build/                            # Build scripts
├── HOW-TO-RUN.md                     # Setup instructions ← START HERE
├── README.md                         # This file
├── QUICKSTART.md                     # Original detailed guide
├── build-installer.ps1               # Create MSI installer
└── test-all.ps1                      # Test everything
```

## API Examples

The service exposes a REST API on http://localhost:5555:

```powershell
# Get service status
Invoke-WebRequest http://localhost:5555/api/status | ConvertFrom-Json

# List all rules
Invoke-WebRequest http://localhost:5555/api/rules | ConvertFrom-Json

# Get active connections
Invoke-WebRequest http://localhost:5555/api/network/connections | ConvertFrom-Json
```

## Requirements

**To run the basic system:**
- Windows 10/11 (64-bit)
- .NET SDK 8.0 or later
- PowerShell
- Administrator rights

**To build drivers (optional):**
- Visual Studio 2022 with C++ Desktop Development
- Windows Driver Kit (WDK) 11
- Code signing certificate

**To build installer (optional):**
- WiX Toolset v3.14

## Troubleshooting

**GUI won't start:**
```powershell
# See the actual error:
dotnet src/clients/SecureHostGUI/bin/AnyCPU/Release/win-x64/SecureHostGUI.dll
```

**Build fails with "WiX Toolset" error:**
- Don't build the entire solution
- Build only the .NET projects individually (see Quick Start)

**"File is being used by another process":**
- Stop the service (Ctrl+C) before rebuilding

**CLI says "Service unreachable":**
- Make sure the service is running
- Check http://localhost:5555 is accessible

See [HOW-TO-RUN.md](HOW-TO-RUN.md) for more troubleshooting tips.

## Security Notes

**Current Status:**
- Service runs as SYSTEM (full privileges)
- Policies stored encrypted with DPAPI
- Audit logs in JSON format
- No driver enforcement (user-mode only)

**Production Deployment Would Need:**
- Kernel drivers installed and running
- Drivers signed with EV code signing certificate
- Service running as Protected Process Light (PPL)
- Code integrity policies
- Secure Boot enabled

This is currently a development/demo system. The enforcement layer (drivers) is not active.

## Documentation

- [HOW-TO-RUN.md](HOW-TO-RUN.md) - **Start here** - Build and run instructions
- [QUICKSTART.md](QUICKSTART.md) - Original detailed guide
- [docs/architecture.md](docs/architecture.md) - System architecture
## License

This is a demonstration project. See LICENSE file for details.

## Support

If something isn't working:
1. Check [HOW-TO-RUN.md](HOW-TO-RUN.md) troubleshooting section
2. Run `.\test-all.ps1` to diagnose issues
3. Check audit logs at `C:\ProgramData\SecureHost\Audit\audit.jsonl`
