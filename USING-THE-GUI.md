# Using the GUI to Control Devices

This guide shows you how to use the GUI to actually block or allow your camera and microphone.

## 🔴 EMERGENCY RESET

**Accidentally blocked something?** Click the big red **"⚠️ RESET ALL DEVICES"** button in the Policy Rules tab!

Or use CLI:
```powershell
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force
```

**Full recovery guide:** [DEVICE-CONTROL-AND-RESET.md](DEVICE-CONTROL-AND-RESET.md)

---

## What You Need to Know

The service **actually disables devices** at the Windows level, not just monitoring. When a device is blocked, **no app can use it** until you unblock it.

**All changes are INSTANT and REVERSIBLE** - nothing is permanently broken!

---

## Step 1: Start Everything

1. Start the service (PowerShell as Admin):
```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe
```

2. Open the GUI (new PowerShell window):
```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostGUI\bin\AnyCPU\Release\win-x64\SecureHostGUI.exe
```

---

## Step 2: Understanding the Dashboard

When the GUI opens, you'll see:

**Dashboard Tab:**
- Service Status: Should show "Running"
- Total Rules: Shows 3 (2 device rules + 1 network rule)
- Active Rules: Shows how many are enabled

---

## Step 3: Viewing Current Rules

Click on the "**Policy Rules**" tab. You'll see 3 default rules:

| ID | Name | Type | Action | Enabled |
|----|------|------|--------|---------|
| 1 | Block Camera Access (Default) | Device | Block | Yes |
| 2 | Block Microphone Access (Default) | Device | Block | Yes |
| 3 | Audit Non-Standard Outbound Connections | Network | Audit | Yes |

---

## Step 4: Testing Camera Blocking

### Test that camera is actually blocked:

1. **Try to use your camera:**
   - Open Windows Camera app
   - Or open any video chat app (Zoom, Teams, etc.)
   - **Result:** Camera won't work - you'll see "Camera not found" or similar error

2. **Check Device Manager to confirm:**
   - Open Device Manager (Win+X, then "Device Manager")
   - Expand "Cameras" or "Imaging devices"
   - Your camera will have a **down arrow** icon (disabled state)

### Unblock the camera using the GUI:

**Option A: Click the green "Toggle" button (recommended)**
1. In the GUI, find rule "Block Camera Access (Default)"
2. Click the green **"Toggle"** button in the Actions column
3. **Read the warning dialog carefully!** It tells you exactly what will happen
4. Click **"Yes"** to confirm
5. **Wait 1-2 seconds** - you'll see a success message
6. ✅ Camera is now enabled!

**Option B: Delete the rule**
1. Click the "Delete" button next to the rule
2. Confirm deletion
3. Camera is immediately enabled

### Verify camera works:

1. Open Windows Camera app again
2. **Result:** Camera should work now!
3. Check Device Manager - camera should no longer have the down arrow

### Block it again:

1. Click the green **"Toggle"** button again
2. **Read the warning dialog carefully!**
3. Click **"Yes"** to confirm
4. **Wait 1-2 seconds**
5. Camera stops working again

---

## Step 5: Microphone Works the Same Way

Same process for microphone:

1. Default rule blocks it
2. Click the green **"Toggle"** button on "Block Microphone Access" rule to enable
3. Click **"Toggle"** again to disable

---

## Step 5.5: 🔴 Using the RESET Button

**When to use it:**
- You blocked something by mistake and need it RIGHT NOW
- You're not sure what you blocked
- You want everything back to normal Windows state

**How to use it:**

1. Click on the **"Policy Rules"** tab
2. Look at the top of the screen for the big red button: **"⚠️ RESET ALL DEVICES"**
3. Click it
4. **Read the warning carefully** - it explains exactly what will happen
5. Click "Yes" to confirm
6. ✅ All devices are now enabled!

**IMPORTANT:**
- Reset re-enables devices immediately ✅
- But your blocking rules still exist! ⚠️
- To permanently enable devices, toggle off or delete the blocking rules

---

## Step 6: Network Monitoring

Click on the "**Network**" tab to see:

- All active TCP connections on your machine
- Local address/port
- Remote address/port
- Connection state

This is read-only monitoring. Network blocking requires the kernel driver (advanced setup).

---

## Step 7: Audit Logs

Click on the "**Audit Logs**" tab:

1. Select a date range
2. Click "Export Audit Logs"
3. Save the JSON file
4. Open it to see all device access attempts and policy changes

---

## How It Actually Works

When you toggle a device rule in the GUI:

```
GUI (your click)
   ↓
REST API call to service (http://localhost:5555/api/rules/...)
   ↓
PolicyManagementService.UpdateRuleAsync()
   ↓
DeviceControlService.EnforcePoliciesAsync()
   ↓
Windows WMI: device.InvokeMethod("Disable" or "Enable")
   ↓
Device is disabled/enabled at Windows level
   ↓
Audit log written to C:\ProgramData\SecureHost\Audit\audit.jsonl
```

**This happens in 1-3 seconds.** The enforcement is immediate.

---

## Troubleshooting

### 🔴 "I blocked something and need it back NOW!"

**SOLUTION: Use the emergency reset!**

**GUI Method:**
1. Click "Policy Rules" tab
2. Click the big red **"⚠️ RESET ALL DEVICES"** button
3. Confirm when asked
4. ✅ Everything works again!

**CLI Method:**
```powershell
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\clients\SecureHostCLI\bin\AnyCPU\Release\win-x64\SecureHostCLI.exe system reset --force
```

**Remember:** Reset doesn't delete your rules! To permanently enable devices, toggle off the blocking rules.

### 🔴 Toggle button doesn't work or I get an error

**Possible causes:**
1. Service isn't running
2. Service doesn't have admin privileges
3. Network issue between GUI and service

**SOLUTION:**
```powershell
# Check if service is running (look for SecureHostService.exe process)

# If not running, start it (PowerShell as Administrator):
cd "c:\Users\hp\Desktop\SecureHost Control Suite"
.\src\service\SecureHostService\bin\AnyCPU\Release\win-x64\SecureHostService.exe
```

### Camera/Mic don't actually get disabled

**Check service logs:**
Look at the PowerShell window where the service is running. You should see:
```
info: Enforcing device policies on all devices...
info: Blocking Camera: [Your Camera Name]
info: Policy enforcement complete: 2 blocked, 0 allowed
```

**If you see "Error disabling device":**
- Make sure the service is running as Administrator
- Some built-in devices require special permissions
- Check Windows Event Viewer for access denied errors

### Changes don't apply immediately

- Give it 2-3 seconds for WMI to complete
- Refresh Device Manager to see the current state
- Check service logs for "Policy enforcement complete" message

---

## What Happens When Service Stops

When you stop the service (Ctrl+C):

- All blocked devices remain in their current state
- If camera was disabled, it stays disabled
- You need to manually enable devices in Device Manager, OR
- Restart the service and delete the blocking rules

---

## Advanced: Adding Custom Rules

To add a new device blocking rule:

1. Click "Add Rule" button
2. Fill in:
   - Name: "Block USB Storage"
   - Type: Device
   - Device Type: USB
   - Action: Block
   - Priority: 100
   - Enabled: Yes
3. Click Save
4. USB devices will be immediately disabled

---

## Summary

**What works:**
- ✅ Block/unblock camera via GUI
- ✅ Block/unblock microphone via GUI
- ✅ Real Windows device disable/enable
- ✅ Instant enforcement (1-3 seconds)
- ✅ Audit logging of all actions

**What doesn't work yet:**
- ❌ Network connection blocking (needs kernel driver)
- ❌ Per-process device control (needs kernel driver)
- ❌ Tamper protection (needs kernel driver + PPL)

**But device blocking DOES work** for camera and microphone right now, using Windows WMI.
