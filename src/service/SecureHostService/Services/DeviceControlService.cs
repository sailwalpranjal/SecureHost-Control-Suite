using Microsoft.Extensions.Logging;
using SecureHostCore.Engine;
using SecureHostCore.Models;
using System.Management;

namespace SecureHostService.Services;

/// <summary>
/// Controls device access (camera, microphone, USB, Bluetooth)
/// Uses WMI and Windows APIs for device management
/// </summary>
public sealed class DeviceControlService
{
    private readonly ILogger<DeviceControlService> _logger;
    private readonly PolicyEngine _policyEngine;
    private readonly AuditEngine _auditEngine;
    private ManagementEventWatcher? _deviceWatcher;

    public DeviceControlService(
        ILogger<DeviceControlService> logger,
        PolicyEngine policyEngine,
        AuditEngine auditEngine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
        _auditEngine = auditEngine ?? throw new ArgumentNullException(nameof(auditEngine));
    }

    /// <summary>
    /// Starts device monitoring
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting device control service...");

        try
        {
            // Monitor device arrival/removal events
            var query = new WqlEventQuery("SELECT * FROM __InstanceOperationEvent WITHIN 2 " +
                                         "WHERE TargetInstance ISA 'Win32_PnPEntity'");

            _deviceWatcher = new ManagementEventWatcher(query);
            _deviceWatcher.EventArrived += OnDeviceEventArrived;
            _deviceWatcher.Start();

            _logger.LogInformation("Device monitoring started");

            // Enumerate existing devices
            await EnumerateDevicesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting device control service");
        }
    }

    /// <summary>
    /// Stops device monitoring
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping device control service...");

        if (_deviceWatcher != null)
        {
            _deviceWatcher.Stop();
            _deviceWatcher.Dispose();
            _deviceWatcher = null;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles device plug/unplug events
    /// </summary>
    private void OnDeviceEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = e.NewEvent["TargetInstance"] as ManagementBaseObject;
            if (targetInstance == null)
                return;

            var deviceId = targetInstance["DeviceID"]?.ToString();
            var description = targetInstance["Description"]?.ToString();
            var classGuid = targetInstance["ClassGuid"]?.ToString();

            var eventType = e.NewEvent.ClassPath.ClassName;
            var isArrival = eventType.Contains("Creation");

            _logger.LogInformation(
                "Device event: {EventType} | Device: {Description} | ID: {DeviceId}",
                isArrival ? "Arrival" : "Removal",
                description,
                deviceId);

            // Identify device type
            var deviceType = IdentifyDeviceType(classGuid, description);

            if (deviceType != DeviceType.Unknown)
            {
                _ = HandleDeviceEventAsync(deviceId!, description!, deviceType, isArrival);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling device event");
        }
    }

    /// <summary>
    /// Handles device arrival/removal
    /// </summary>
    private async Task HandleDeviceEventAsync(
        string deviceId,
        string description,
        DeviceType deviceType,
        bool isArrival)
    {
        try
        {
            // Evaluate policy
            var decision = _policyEngine.EvaluateDeviceAccess(
                (uint)Environment.ProcessId,
                "System",
                deviceType,
                deviceId,
                null);

            // Log audit event
            await _auditEngine.LogDeviceEventAsync(
                (uint)Environment.ProcessId,
                "System",
                decision.Action,
                new DeviceEventDetails
                {
                    DeviceType = deviceType,
                    DeviceId = deviceId,
                    DeviceName = description,
                    AccessType = isArrival ? DeviceAccessType.Open : DeviceAccessType.Control
                },
                decision.RuleId,
                $"Device {(isArrival ? "connected" : "disconnected")}: {description}");

            // If blocked, attempt to disable device
            if (decision.Action == PolicyAction.Block && isArrival)
            {
                await DisableDeviceAsync(deviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling device event");
        }
    }

    /// <summary>
    /// Enumerates all existing devices
    /// </summary>
    private async Task EnumerateDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Status='OK'");

            var devices = searcher.Get();
            var count = 0;

            foreach (ManagementObject device in devices)
            {
                var deviceId = device["DeviceID"]?.ToString();
                var description = device["Description"]?.ToString();
                var classGuid = device["ClassGuid"]?.ToString();

                if (string.IsNullOrEmpty(deviceId))
                    continue;

                var deviceType = IdentifyDeviceType(classGuid, description);

                if (deviceType != DeviceType.Unknown)
                {
                    _logger.LogDebug("Found device: {Type} - {Description}", deviceType, description);
                    count++;
                }
            }

            _logger.LogInformation("Enumerated {Count} monitored devices", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating devices");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Identifies device type from class GUID and description
    /// </summary>
    private DeviceType IdentifyDeviceType(string? classGuid, string? description)
    {
        if (string.IsNullOrEmpty(classGuid) && string.IsNullOrEmpty(description))
            return DeviceType.Unknown;

        // Camera class: {ca3e7ab9-b4c3-4ae6-8251-579ef933890f}
        if (classGuid?.Contains("ca3e7ab9", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("camera", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("webcam", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DeviceType.Camera;
        }

        // Audio class: {4d36e96c-e325-11ce-bfc1-08002be10318}
        if (classGuid?.Contains("4d36e96c", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("microphone", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("audio capture", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DeviceType.Microphone;
        }

        // USB class: {36fc9e60-c465-11cf-8056-444553540000}
        if (classGuid?.Contains("36fc9e60", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DeviceType.USB;
        }

        // Bluetooth class: {e0cbf06c-cd8b-4647-bb8a-263b43f0f974}
        if (classGuid?.Contains("e0cbf06c", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("bluetooth", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DeviceType.Bluetooth;
        }

        return DeviceType.Unknown;
    }

    /// <summary>
    /// Disables a device using WMI
    /// </summary>
    private async Task<bool> DisableDeviceAsync(string deviceId)
    {
        try
        {
            var query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'";
            var searcher = new ManagementObjectSearcher(query);
            var devices = searcher.Get();

            foreach (ManagementObject device in devices)
            {
                var result = device.InvokeMethod("Disable", null);
                var returnValue = Convert.ToInt32(result);

                if (returnValue == 0)
                {
                    _logger.LogWarning("Disabled device: {DeviceId}", deviceId);
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to disable device: {DeviceId}, Error: {Error}", deviceId, returnValue);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling device: {DeviceId}", deviceId);
        }

        await Task.CompletedTask;
        return false;
    }

    /// <summary>
    /// Enables a device using WMI
    /// </summary>
    private async Task<bool> EnableDeviceAsync(string deviceId)
    {
        try
        {
            var query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'";
            var searcher = new ManagementObjectSearcher(query);
            var devices = searcher.Get();

            foreach (ManagementObject device in devices)
            {
                var result = device.InvokeMethod("Enable", null);
                var returnValue = Convert.ToInt32(result);

                if (returnValue == 0)
                {
                    _logger.LogInformation("Enabled device: {DeviceId}", deviceId);
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to enable device: {DeviceId}, Error: {Error}", deviceId, returnValue);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling device: {DeviceId}", deviceId);
        }

        await Task.CompletedTask;
        return false;
    }

    /// <summary>
    /// Enforces current policy rules on all devices
    /// Call this when rules are added/updated/deleted
    /// </summary>
    public async Task EnforcePoliciesAsync()
    {
        _logger.LogInformation("Enforcing device policies on all devices...");

        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode=0");

            var devices = searcher.Get();
            int blocked = 0;
            int allowed = 0;

            foreach (ManagementObject device in devices)
            {
                var deviceId = device["DeviceID"]?.ToString();
                var description = device["Description"]?.ToString();
                var classGuid = device["ClassGuid"]?.ToString();

                if (string.IsNullOrEmpty(deviceId))
                    continue;

                var deviceType = IdentifyDeviceType(classGuid, description);

                // Only enforce on camera, microphone, USB, Bluetooth
                if (deviceType == DeviceType.Unknown)
                    continue;

                // Evaluate policy
                var decision = _policyEngine.EvaluateDeviceAccess(
                    (uint)Environment.ProcessId,
                    "System",
                    deviceType,
                    deviceId,
                    null);

                // Apply enforcement
                if (decision.Action == PolicyAction.Block)
                {
                    _logger.LogInformation("Blocking {DeviceType}: {Description}", deviceType, description);
                    await DisableDeviceAsync(deviceId);
                    blocked++;

                    // Log audit event
                    await _auditEngine.LogDeviceEventAsync(
                        (uint)Environment.ProcessId,
                        "System",
                        PolicyAction.Block,
                        new DeviceEventDetails
                        {
                            DeviceType = deviceType,
                            DeviceId = deviceId,
                            DeviceName = description ?? "Unknown",
                            AccessType = DeviceAccessType.Control
                        },
                        decision.RuleId,
                        $"Device blocked by policy: {description}");
                }
                else
                {
                    // Make sure device is enabled if policy allows it
                    _logger.LogInformation("Allowing {DeviceType}: {Description}", deviceType, description);
                    await EnableDeviceAsync(deviceId);
                    allowed++;
                }
            }

            _logger.LogInformation("Policy enforcement complete: {Blocked} blocked, {Allowed} allowed", blocked, allowed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enforcing device policies");
        }
    }

    /// <summary>
    /// EMERGENCY RESET: Re-enables ALL devices regardless of policy
    /// Use this if you accidentally blocked critical devices
    /// </summary>
    public async Task ResetAllDevicesAsync()
    {
        _logger.LogWarning("RESETTING ALL DEVICES - Re-enabling everything!");

        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity");

            var devices = searcher.Get();
            int enabled = 0;

            foreach (ManagementObject device in devices)
            {
                var deviceId = device["DeviceID"]?.ToString();
                var description = device["Description"]?.ToString();
                var status = device["Status"]?.ToString();

                if (string.IsNullOrEmpty(deviceId))
                    continue;

                // Only try to enable devices that are currently disabled
                if (status != "OK")
                {
                    _logger.LogInformation("Re-enabling: {Description}", description);
                    await EnableDeviceAsync(deviceId);
                    enabled++;
                }
            }

            _logger.LogWarning("RESET COMPLETE: Re-enabled {Count} devices", enabled);

            await _auditEngine.LogPolicyChangeAsync(
                "DeviceReset",
                null,
                $"EMERGENCY RESET: Re-enabled all {enabled} devices");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during device reset");
        }
    }
}
