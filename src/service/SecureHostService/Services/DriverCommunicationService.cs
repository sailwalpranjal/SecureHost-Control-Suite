using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace SecureHostService.Services;

/// <summary>
/// Handles communication with kernel-mode drivers
/// Provides IOCTL interface and driver lifecycle management
/// </summary>
public sealed class DriverCommunicationService : IDisposable
{
    private readonly ILogger<DriverCommunicationService> _logger;
    private SafeFileHandle? _wfpDriverHandle;
    private SafeFileHandle? _deviceDriverHandle;
    private bool _disposed;

    private const string WFP_DRIVER_NAME = @"\\.\SecureHostWFP";
    private const string DEVICE_DRIVER_NAME = @"\\.\SecureHostDevice";

    // IOCTL codes
    private const uint IOCTL_ADD_NETWORK_RULE = 0x222000;
    private const uint IOCTL_REMOVE_NETWORK_RULE = 0x222004;
    private const uint IOCTL_ADD_DEVICE_RULE = 0x222008;
    private const uint IOCTL_REMOVE_DEVICE_RULE = 0x22200C;
    private const uint IOCTL_GET_STATISTICS = 0x222010;

    public DriverCommunicationService(ILogger<DriverCommunicationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes communication with kernel drivers
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing driver communication...");

        var success = true;

        // Try to open WFP driver
        try
        {
            _wfpDriverHandle = CreateFileW(
                WFP_DRIVER_NAME,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                0,
                IntPtr.Zero);

            if (_wfpDriverHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning("Could not open WFP driver: Error {Error}", error);
                _wfpDriverHandle = null;
                success = false;
            }
            else
            {
                _logger.LogInformation("WFP driver connection established");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception opening WFP driver");
            _wfpDriverHandle = null;
            success = false;
        }

        // Try to open device filter driver
        try
        {
            _deviceDriverHandle = CreateFileW(
                DEVICE_DRIVER_NAME,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                0,
                IntPtr.Zero);

            if (_deviceDriverHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning("Could not open device driver: Error {Error}", error);
                _deviceDriverHandle = null;
                success = false;
            }
            else
            {
                _logger.LogInformation("Device driver connection established");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception opening device driver");
            _deviceDriverHandle = null;
            success = false;
        }

        await Task.CompletedTask;
        return success;
    }

    /// <summary>
    /// Sends network rule to WFP driver
    /// </summary>
    public async Task<bool> SendNetworkRuleAsync(
        ulong ruleId,
        uint processId,
        ushort protocol,
        ushort localPort,
        ushort remotePort,
        uint action,
        CancellationToken cancellationToken)
    {
        if (_wfpDriverHandle == null || _wfpDriverHandle.IsInvalid)
        {
            _logger.LogWarning("WFP driver not available");
            return false;
        }

        try
        {
            // Create rule structure
            var rule = new NetworkRule
            {
                RuleId = ruleId,
                ProcessId = processId,
                Protocol = protocol,
                LocalPort = localPort,
                RemotePort = remotePort,
                Action = action,
                Enabled = 1
            };

            var ruleBytes = StructToBytes(rule);

            var result = DeviceIoControl(
                _wfpDriverHandle,
                IOCTL_ADD_NETWORK_RULE,
                ruleBytes,
                (uint)ruleBytes.Length,
                null,
                0,
                out _,
                IntPtr.Zero);

            if (!result)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to send network rule to driver: Error {Error}", error);
                return false;
            }

            _logger.LogDebug("Network rule {RuleId} sent to driver", ruleId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending network rule to driver");
            return false;
        }
    }

    /// <summary>
    /// Sends device rule to device filter driver
    /// </summary>
    public async Task<bool> SendDeviceRuleAsync(
        ulong ruleId,
        uint processId,
        uint deviceType,
        uint action,
        CancellationToken cancellationToken)
    {
        if (_deviceDriverHandle == null || _deviceDriverHandle.IsInvalid)
        {
            _logger.LogWarning("Device driver not available");
            return false;
        }

        try
        {
            var rule = new DeviceRule
            {
                RuleId = ruleId,
                ProcessId = processId,
                DeviceType = deviceType,
                Action = action,
                Enabled = 1
            };

            var ruleBytes = StructToBytes(rule);

            var result = DeviceIoControl(
                _deviceDriverHandle,
                IOCTL_ADD_DEVICE_RULE,
                ruleBytes,
                (uint)ruleBytes.Length,
                null,
                0,
                out _,
                IntPtr.Zero);

            if (!result)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to send device rule to driver: Error {Error}", error);
                return false;
            }

            _logger.LogDebug("Device rule {RuleId} sent to driver", ruleId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending device rule to driver");
            return false;
        }
    }

    /// <summary>
    /// Checks driver health
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        // Check if handles are still valid
        var wfpOk = _wfpDriverHandle != null && !_wfpDriverHandle.IsInvalid;
        var deviceOk = _deviceDriverHandle != null && !_deviceDriverHandle.IsInvalid;

        _logger.LogDebug("Driver health: WFP={WfpStatus}, Device={DeviceStatus}",
            wfpOk ? "OK" : "Unavailable",
            deviceOk ? "OK" : "Unavailable");

        await Task.CompletedTask;
        return wfpOk || deviceOk; // At least one driver should be available
    }

    /// <summary>
    /// Shuts down driver communication
    /// </summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down driver communication...");

        _wfpDriverHandle?.Dispose();
        _wfpDriverHandle = null;

        _deviceDriverHandle?.Dispose();
        _deviceDriverHandle = null;

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _wfpDriverHandle?.Dispose();
        _deviceDriverHandle?.Dispose();
        _disposed = true;
    }

    // Helper methods
    private static byte[] StructToBytes<T>(T structure) where T : struct
    {
        var size = Marshal.SizeOf(structure);
        var bytes = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structure, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return bytes;
    }

    // Native structures matching kernel driver structures
    [StructLayout(LayoutKind.Sequential)]
    private struct NetworkRule
    {
        public ulong RuleId;
        public uint ProcessId;
        public ushort Protocol;
        public ushort LocalPort;
        public ushort RemotePort;
        public uint Action;
        public byte Enabled;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceRule
    {
        public ulong RuleId;
        public uint ProcessId;
        public uint DeviceType;
        public uint Action;
        public byte Enabled;
    }

    // P/Invoke declarations
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
        [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
        [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
        [MarshalAs(UnmanagedType.U4)] uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[]? lpInBuffer,
        uint nInBufferSize,
        byte[]? lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
