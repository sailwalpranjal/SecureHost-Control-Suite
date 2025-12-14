# Creating GitHub Release - Complete Step-by-Step Guide

This guide shows you **exactly** how to create a GitHub release with downloadable executables that users can run directly on Windows.

---

## Prerequisites

1. ✅ GitHub account
2. ✅ Git installed on your computer
3. ✅ GitHub repository created (we'll create this in Step 1 if you don't have one)

---

## Step 1: Create GitHub Repository

### Option A: Using GitHub Website

1. Go to https://github.com
2. Click the **"+"** button (top right) → **"New repository"**
3. Fill in:
   - **Repository name**: `SecureHostSuite` (or your choice)
   - **Description**: "Windows device and network control suite with instant toggle and emergency reset"
   - **Public** or **Private**: Choose based on your needs
   - **Initialize**: Leave unchecked (we'll push existing code)
4. Click **"Create repository"**
5. **Copy the repository URL** (e.g., `https://github.com/yourusername/SecureHostSuite.git`)

### Option B: Using GitHub CLI

```powershell
# Install GitHub CLI if you don't have it
winget install GitHub.cli

# Login
gh auth login

# Create repository
gh repo create SecureHostSuite --public --description "Windows device and network control suite"
```

---

## Step 2: Initialize Git in Your Project

Open PowerShell in your project directory:

```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"

# Initialize git (if not already done)
git init

# Create .gitignore to exclude build artifacts
@"
# Build outputs
bin/
obj/
dist/
release-package/
*.exe
*.dll
*.pdb
*.cache

# User-specific files
.vs/
.vscode/
*.user
*.suo

# NuGet
packages/
*.nupkg

# Logs and temp files
*.log
*.tmp
"@ | Out-File -FilePath .gitignore -Encoding UTF8

# Add all files
git add .

# Create initial commit
git commit -m "SecureHost Control Suite v1.0.0

Features:
- Instant device toggle (camera, microphone, USB, Bluetooth)
- Emergency reset button with confirmation dialogs
- GUI and CLI interfaces
- REST API for automation
- Comprehensive documentation with emergency recovery
- All changes are reversible and instant (1-2 seconds)
"

# Add remote (replace with your repository URL)
git remote add origin https://github.com/sailwalpranjal/SecureHost-Control-Suite.git

# Push to GitHub
git push -u origin master
```

---

## Step 3: Create Release Package

Run the packaging script:

```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"

# Create release package
.\create-release-package.ps1 -Version "1.0.0"
```

**What this does:**
1. Builds all components in Release mode
2. Copies executables and dependencies
3. Includes documentation
4. Creates launcher scripts (.bat files)
5. Packages everything into a ZIP file

**Output:**
- `release-package\SecureHostSuite-v1.0.0-Windows-x64.zip`

---

## Step 4: Create Release on GitHub

### Method 1: Using GitHub Website (Recommended)

1. **Go to your repository** on GitHub
   - `https://github.com/yourusername/SecureHostSuite`

2. **Click "Releases"** (right sidebar)

3. **Click "Create a new release"** or **"Draft a new release"**

4. **Fill in release details:**

   **Tag version:** `v1.0.0`
   - Click "Create new tag: v1.0.0 on publish"

   **Release title:** `SecureHost Control Suite v1.0.0 - Instant Device Control with Emergency Reset`

   **Description:** (Copy this template)
   ```markdown
   # SecureHost Control Suite v1.0.0

   ## 🎉 Features

   ### ✅ Instant Device Control
   - **Toggle devices instantly** - Camera, microphone, USB, Bluetooth (1-2 second response)
   - **GUI and CLI interfaces** - Easy-to-use graphical interface and powerful command-line tools
   - **Confirmation dialogs** - Clear warnings before every action
   - **Real Windows enforcement** - Actually disables devices at OS level, not just monitoring

   ### 🔴 Emergency Reset
   - **Big red reset button** in GUI - One click to re-enable all devices
   - **CLI reset command** - `SecureHostCLI.exe system reset --force`
   - **Always available** - Can't lock yourself out
   - **Fully reversible** - Nothing is permanently broken

   ### 📖 Comprehensive Documentation
   - Emergency recovery guide with RED warnings
   - Step-by-step usage instructions
   - Common mistakes and solutions
   - Quick reference commands

   ---

   ## 📥 Download

   **For Windows 10/11 (64-bit)**

   Download: **SecureHostSuite-v1.0.0-Windows-x64.zip** (below)

   ---

   ## 🚀 Quick Start

   1. **Download** the ZIP file (see below)
   2. **Extract** to a folder (e.g., `C:\SecureHostSuite`)
   3. **Run as Administrator**: Right-click `START-SERVICE.bat` → "Run as Administrator"
   4. **Open GUI**: Double-click `START-GUI.bat`

   ### 🔴 Emergency Recovery

   If you accidentally block something critical:

   **GUI Method:**
   1. Open GUI
   2. Go to "Policy Rules" tab
   3. Click red **"⚠️ RESET ALL DEVICES"** button

   **CLI Method:**
   ```powershell
   cd CLI
   SecureHostCLI.exe system reset --force
   ```

   ---

   ## 📋 What's Inside

   - **Service** - Background service for enforcement (runs as SYSTEM)
   - **GUI** - Modern WPF interface with Material Design
   - **CLI** - Command-line tools for automation
   - **Documentation** - Complete guides with emergency recovery

   ---

   ## 📖 Documentation

   - **README.md** - Project overview
   - **INSTALLATION.md** - Quick start guide (included in ZIP)
   - **Docs/HOW-TO-RUN.md** - Complete setup instructions
   - **Docs/DEVICE-CONTROL-AND-RESET.md** - Device control & emergency recovery
   - **Docs/USING-THE-GUI.md** - GUI usage guide

   ---

   ## ⚙️ Requirements

   - Windows 10/11 (64-bit)
   - Administrator privileges
   - .NET Runtime (included in package - no separate installation needed)

   ---

   ## ⚠️ Important Notes

   - **All changes are reversible** - Nothing is permanently deleted
   - **Emergency reset always available** - Can't lock yourself out
   - **Instant response** - Device changes apply in 1-2 seconds
   - **Read warnings carefully** - Dialogs explain exactly what will happen

   ---

   ## 🆘 Support

   - **Documentation**: See `Docs` folder in the ZIP
   - **Issues**: [Report issues](https://github.com/yourusername/SecureHostSuite/issues)
   - **Emergency**: Use the reset button or CLI reset command

   ---

   ## 🔒 Security

   - Uses Windows WMI for device control (official Windows API)
   - All changes logged to audit files
   - Service runs as SYSTEM for proper device access
   - Confirmation required for all actions

   ---

   ## Changelog

   ### New Features
   - ✅ Instant toggle for camera, microphone, USB, Bluetooth
   - ✅ Emergency reset button (GUI and CLI)
   - ✅ Confirmation dialogs with clear warnings
   - ✅ 1-2 second response time for device changes
   - ✅ Comprehensive documentation with recovery guide

   ### What Works
   - ✅ Camera blocking/unblocking
   - ✅ Microphone blocking/unblocking
   - ✅ Network monitoring
   - ✅ Audit logging
   - ✅ GUI toggle buttons
   - ✅ CLI toggle commands
   - ✅ Emergency reset (GUI and CLI)

   ---

   **Download the ZIP file below to get started!**
   ```

5. **Attach the ZIP file:**
   - Scroll down to **"Attach binaries by dropping them here or selecting them"**
   - Click and select: `release-package\SecureHostSuite-v1.0.0-Windows-x64.zip`
   - Wait for upload to complete

6. **Publish release:**
   - ✅ Check **"Set as the latest release"**
   - Click **"Publish release"**

### Method 2: Using GitHub CLI

```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"

# Create release with ZIP file
gh release create v1.0.0 `
  release-package\SecureHostSuite-v1.0.0-Windows-x64.zip `
  --title "SecureHost Control Suite v1.0.0 - Instant Device Control" `
  --notes-file release-notes.txt

# The release-notes.txt should contain the description from above
```

---

## Step 5: Verify Release

1. **Go to your repository** → **Releases**
2. **Click on the release** (v1.0.0)
3. **Verify:**
   - ✅ ZIP file is attached
   - ✅ Description is correct
   - ✅ Download link works

---

## Step 6: Test Download

**Test as if you're a user:**

1. **Click the ZIP download link**
2. **Extract the ZIP**
3. **Run `START-SERVICE.bat` as Administrator**
4. **Run `START-GUI.bat`**
5. **Verify everything works**

---

## Updating to Version 1.1.0 (Future Releases)

When you want to release a new version:

```powershell
# 1. Make your code changes and commit
git add .
git commit -m "Version 1.1.0: Added new features"
git push

# 2. Create new release package
.\create-release-package.ps1 -Version "1.1.0"

# 3. Create new release on GitHub
# - Use tag: v1.1.0
# - Upload new ZIP: SecureHostSuite-v1.1.0-Windows-x64.zip
# - Update release notes
```

---

## What Users Will See

When users visit your GitHub release page, they'll see:

1. **Release title** - Clear version and feature highlights
2. **Description** - Features, quick start, requirements
3. **Download button** - Big green "Assets" dropdown with ZIP file
4. **File size** - Shows ZIP size (~50-100 MB typically)

**User downloads the ZIP and:**
- Extracts to any folder
- Runs START-SERVICE.bat as admin
- Runs START-GUI.bat
- Everything works - no compilation needed!

---

## Advanced: Creating Auto-Update Functionality (Optional)

If you want users to get notified of new versions, add this to your code:

```csharp
// In your GUI or CLI, check GitHub API for latest release
var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd("SecureHostSuite/1.0");
var response = await client.GetStringAsync(
    "https://api.github.com/repos/yourusername/SecureHostSuite/releases/latest"
);
var release = JsonSerializer.Deserialize<GitHubRelease>(response);
if (release.TagName != "v1.0.0") {
    // Show update notification
}
```

---

## Troubleshooting

### Problem: ZIP file too large for GitHub (>2GB)

**Solution:**
- GitHub allows files up to 2GB
- Your package should be ~50-100MB
- If larger, you can use GitHub Large File Storage (LFS)

### Problem: Users report "Windows protected your PC"

**Solution:**
This is Windows SmartScreen. Users can:
1. Click "More info"
2. Click "Run anyway"

**To avoid this (advanced):**
- Sign your EXE with a code signing certificate
- Costs $100-300/year from certificate authorities

### Problem: Users get ".NET not found" error

**Solution:**
Your package should include the .NET runtime (self-contained deployment):

```powershell
# Build with runtime included
dotnet publish src/service/SecureHostService/SecureHostService.csproj `
  --configuration Release `
  --self-contained true `
  --runtime win-x64 `
  -p:PublishSingleFile=true
```

Update your `create-release-package.ps1` to use `publish` instead of `build`.

---

## Summary Checklist

- [ ] Created GitHub repository
- [ ] Initialized Git in project folder
- [ ] Created .gitignore file
- [ ] Committed and pushed code to GitHub
- [ ] Ran create-release-package.ps1
- [ ] Created release on GitHub
- [ ] Uploaded ZIP file
- [ ] Published release
- [ ] Tested download and installation
- [ ] Shared release link with users

**Your release URL will be:**
`https://github.com/yourusername/SecureHostSuite/releases/tag/v1.0.0`

---

## Example Release Links

Other popular projects for reference:
- https://github.com/microsoft/PowerToys/releases
- https://github.com/notepad-plus-plus/notepad-plus-plus/releases
- https://github.com/ShareX/ShareX/releases

Notice how they all have:
- Clear version tags (v1.0.0)
- Detailed release notes
- Attached ZIP/EXE files
- Installation instructions

**Follow this same pattern for professional releases!**
