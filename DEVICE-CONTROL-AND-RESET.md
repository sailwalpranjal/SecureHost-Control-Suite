# 🔴 Device Control & Emergency Reset Guide

**⚠️ CRITICAL: READ THIS BEFORE USING THE APPLICATION**

This guide explains how to control your devices (camera, microphone, etc.) and **how to recover if you accidentally block something critical**.

---

## Table of Contents
1. [How Device Control Works](#how-device-control-works)
2. [🔴 EMERGENCY RESET - IF YOU MESSED UP](#-emergency-reset---if-you-messed-up)
3. [Using the GUI to Control Devices](#using-the-gui-to-control-devices)
4. [Using the CLI to Control Devices](#using-the-cli-to-control-devices)
5. [Common Mistakes and How to Fix Them](#common-mistakes-and-how-to-fix-them)
6. [Understanding What Happens When You Toggle](#understanding-what-happens-when-you-toggle)

---

## How Device Control Works

SecureHost **ACTUALLY disables devices** at the Windows level. This is NOT just monitoring:

- When a device is blocked: **NO APP CAN USE IT** - Windows sees it as disabled
- When a device is unblocked: **IT WORKS NORMALLY** - Windows sees it as enabled
- Changes are **INSTANT** - they apply in 1-3 seconds
- All changes are **REVERSIBLE** - nothing is permanently broken

### What Gets Controlled:

- **Camera** (webcam, built-in camera, external cameras)
- **Microphone** (all audio input devices)
- **USB Devices** (USB storage, peripherals)
- **Bluetooth Devices**

---

## 🔴 EMERGENCY RESET - IF YOU MESSED UP

### When to Use the Reset

Use the emergency reset if:
- ❌ You blocked your microphone and need it for a meeting RIGHT NOW
- ❌ You blocked your camera and can't join a video call
- ❌ You blocked a USB device you need immediately
- ❌ You're not sure what you blocked but something isn't working
- ❌ You just want everything back to normal Windows state

### 🔴 METHOD 1: GUI Reset Button (EASIEST)

1. **Open the GUI**
   ```powershell
   cd "c:\Users\hp\Desktop\SecureHost Control Suite"
   .\src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe
   ```

2. **Click the "Policy Rules" tab**

3. **Look for the BIG RED BUTTON at the top:**
   ```
   ⚠️ RESET ALL DEVICES
   ```

4. **Click it** and confirm when asked

5. **DONE!** All devices are now enabled.

### 🔴 METHOD 2: CLI Reset Command

```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset
```

You'll see warnings - confirm by typing "yes" twice.

### 🔴 METHOD 3: Force Reset (Skip Confirmations)

```powershell
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force
```

**WARNING:** This skips all confirmations and immediately resets everything.

---

## ⚠️ IMPORTANT: What Reset Does and Doesn't Do

### ✅ Reset DOES:
- ✅ Re-enable ALL devices immediately (camera, mic, USB, etc.)
- ✅ Make everything work like normal Windows
- ✅ Fix any "device not found" errors caused by blocking

### ❌ Reset DOES NOT:
- ❌ Delete your blocking rules (they still exist!)
- ❌ Stop the service
- ❌ Make permanent changes

### 🔴 CRITICAL: Rules Still Exist After Reset!

**After using reset:**
- Your devices work immediately ✅
- BUT the blocking rules are still active ⚠️
- If you restart the service, devices will be blocked again! ⚠️

**To PERMANENTLY enable devices:**
1. Use the reset to get your devices working NOW
2. Then disable or delete the blocking rules (see below)

---

## Using the GUI to Control Devices

### Step 1: Start Everything

```powershell
# Terminal 1 (PowerShell as Administrator)
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe

# Terminal 2 (Normal PowerShell)
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe
```

### Step 2: View Current Rules

Click on **"Policy Rules"** tab. You'll see 3 default rules:

| ID | Name | Type | Action | Enabled |
|----|------|------|--------|---------|
| 1 | Block Camera Access (Default) | Device | Block | ✅ Yes |
| 2 | Block Microphone Access (Default) | Device | Block | ✅ Yes |
| 3 | Audit Non-Standard Outbound Connections | Network | Audit | ✅ Yes |

### Step 3: Enable Your Camera (Allow It to Work)

**Option A: Click the GREEN "Toggle" Button**

1. Find the rule "Block Camera Access (Default)"
2. Click the green **"Toggle"** button
3. **Read the warning carefully!**
4. Click "Yes" to confirm
5. ✅ Camera is now enabled!

**To verify:**
- Open Windows Camera app
- Camera should work now!
- Check Device Manager - no down arrow on camera

**Option B: Delete the Rule Completely**

1. Find the rule "Block Camera Access (Default)"
2. Click the **"Delete"** button
3. Confirm deletion
4. ✅ Camera is now permanently enabled (rule is gone)

### Step 4: Disable Your Camera (Block It)

1. Find the rule (or create a new one if you deleted it)
2. Click the green **"Toggle"** button
3. **Read the warning carefully!**
4. Click "Yes" to confirm
5. ✅ Camera is now disabled!

**To verify:**
- Try to open Windows Camera app
- It should show "Camera not found" or similar
- Check Device Manager - camera has down arrow (disabled)

---

## Using the CLI to Control Devices

### Step 1: List All Rules

```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules list
```

Output:
```
ID | Name                                    | Type    | Action | Enabled | Priority
1  | Block Camera Access (Default)           | Device  | Block  | Yes     | 100
2  | Block Microphone Access (Default)        | Device  | Block  | Yes     | 100
3  | Audit Non-Standard Outbound Connections  | Network | Audit  | Yes     | 50
```

### Step 2: Toggle a Rule (Enable/Disable)

```powershell
# Toggle camera rule (if currently blocking, this will allow it)
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules toggle --id 1
```

**What happens:**
1. CLI shows you what the rule currently does
2. Asks for confirmation
3. Toggles the rule
4. Shows success message

### Step 3: Delete a Rule

```powershell
# Permanently delete the camera blocking rule
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules delete --id 1
```

---

## Common Mistakes and How to Fix Them

### ❌ Mistake 1: "I blocked my camera and can't get it back!"

**🔴 SOLUTION:**

```powershell
# Method 1: Emergency reset (fastest)
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force

# Method 2: Toggle the rule off
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules toggle --id 1

# Method 3: Delete the rule
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules delete --id 1
```

### ❌ Mistake 2: "I used reset but devices are blocked again after restart!"

**Why this happens:**
- Reset only re-enables devices temporarily
- Your blocking rules still exist
- When service restarts, rules re-apply

**🔴 SOLUTION:**
```powershell
# Step 1: List rules to see which ones are blocking
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules list

# Step 2: Toggle off each blocking rule
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules toggle --id 1
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules toggle --id 2

# Now devices will stay enabled even after restart!
```

### ❌ Mistake 3: "Service won't start or errors out"

**🔴 SOLUTION:**
```powershell
# Make sure you're running PowerShell as Administrator!
# Right-click PowerShell → "Run as Administrator"

# Then start the service:
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe
```

### ❌ Mistake 4: "GUI shows 'Service unreachable'"

**🔴 SOLUTION:**
```powershell
# Check if service is running:
# You should see SecureHostService.exe process

# If not running, start it:
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe
```

### ❌ Mistake 5: "I deleted a rule by mistake"

**🔴 SOLUTION:**
```powershell
# You can recreate it using the CLI:
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules add \
  --name "Block Camera Access" \
  --type Device \
  --action Block

# Or just restart the service - if no rules exist, defaults are loaded
```

### ❌ Mistake 6: "Camera works in Device Manager but not in apps"

**Why this happens:**
- This is a Windows app permissions issue, not SecureHost
- Windows 10/11 have privacy settings for camera/mic

**🔴 SOLUTION:**
```
Settings → Privacy → Camera → Allow apps to access camera → ON
Settings → Privacy → Microphone → Allow apps to access microphone → ON
```

---

## Understanding What Happens When You Toggle

### When You DISABLE a Blocking Rule (Enable the Device):

**Rule: "Block Camera Access" is currently ENABLED**

You click Toggle → Rule becomes DISABLED

**What Happens:**
1. GUI/CLI sends API call to service **INSTANT**
2. Service calls `EnforcePoliciesAsync()` **1-2 seconds**
3. Service runs `EnableDeviceAsync()` on all cameras **1-2 seconds**
4. Windows WMI enables the device **INSTANT**
5. Camera works! ✅

**Total time:** 2-3 seconds

**You can verify:**
- Windows Camera app works immediately
- Device Manager shows no down arrow
- Any app can use the camera

---

### When You ENABLE a Blocking Rule (Block the Device):

**Rule: "Block Camera Access" is currently DISABLED**

You click Toggle → Rule becomes ENABLED

**What Happens:**
1. GUI/CLI sends API call to service **INSTANT**
2. Service calls `EnforcePoliciesAsync()` **1-2 seconds**
3. Service runs `DisableDeviceAsync()` on all cameras **1-2 seconds**
4. Windows WMI disables the device **INSTANT**
5. Camera stops working! ❌

**Total time:** 2-3 seconds

**You can verify:**
- Windows Camera app shows "Camera not found"
- Device Manager shows down arrow on camera
- No app can use the camera

---

## Full Example: Complete Workflow

### Scenario: Block camera, then realize you need it for a Zoom call

```powershell
# 1. Start service (PowerShell as Admin)
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe

# Service loads default rules → Camera is BLOCKED by default

# 2. Try to use camera in Zoom
# ❌ Zoom says "No camera found"

# 3. OH NO! I need my camera NOW!

# 4. EMERGENCY RESET (fastest way)
# Open new PowerShell:
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force

# Output: "✓ RESET COMPLETE - All devices have been reset and re-enabled"

# 5. Try Zoom again
# ✅ Camera works!

# 6. After the meeting, permanently enable camera:
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules list
# Note the ID of "Block Camera Access" (e.g., ID = 1)

.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules toggle --id 1
# Confirm when asked

# ✅ Camera is now permanently allowed (rule is disabled)
```

---

## Quick Reference Card

### 🔴 Emergency Commands (Copy-Paste These)

```powershell
# Reset everything instantly (skip confirmations)
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force

# Enable camera (toggle rule 1)
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules toggle --id 1

# Enable microphone (toggle rule 2)
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules toggle --id 2

# List all rules to see what's blocking
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe rules list
```

---

## Remember

✅ **Everything is reversible** - you can't permanently break anything

✅ **Reset is your friend** - use it if you're stuck

✅ **Rules persist** - reset doesn't delete rules, just enables devices

✅ **Changes are instant** - you see results in 1-3 seconds

✅ **Read the warnings** - they tell you exactly what will happen

---

## Still Need Help?

If you're completely stuck:

1. **🔴 Reset everything:**
   ```powershell
   .\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force
   ```

2. **Stop the service:**
   - Go to the PowerShell window running the service
   - Press `Ctrl+C`

3. **Manually enable devices in Device Manager:**
   - Win+X → Device Manager
   - Find the device (Cameras, Audio inputs, etc.)
   - Right-click → Enable device

4. **Check Windows privacy settings:**
   - Settings → Privacy → Camera/Microphone
   - Make sure apps are allowed to use them

---

**Last Updated:** After messing a lot and making emergency reset functionality.