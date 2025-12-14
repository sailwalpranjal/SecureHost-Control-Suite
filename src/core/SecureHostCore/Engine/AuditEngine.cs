using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Text.Json;
using SecureHostCore.Models;
using Microsoft.Extensions.Logging;

namespace SecureHostCore.Engine;

/// <summary>
/// Audit and logging engine with ETW support
/// Provides tamper-resistant audit trail with SIEM export
/// </summary>
public sealed class AuditEngine : IDisposable
{
    private readonly ILogger<AuditEngine> _logger;
    private readonly ConcurrentQueue<AuditEvent> _eventQueue;
    private readonly string _auditLogPath;
    private readonly SecureHostEventSource _eventSource;
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _flushSemaphore;
    private bool _disposed;

    public AuditEngine(ILogger<AuditEngine> logger, string auditLogPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditLogPath = auditLogPath ?? throw new ArgumentNullException(nameof(auditLogPath));
        _eventQueue = new ConcurrentQueue<AuditEvent>();
        _eventSource = new SecureHostEventSource();
        _flushSemaphore = new SemaphoreSlim(1, 1);

        // Ensure audit directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(_auditLogPath)!);

        // Flush events to disk every 5 seconds
        _flushTimer = new Timer(FlushTimerCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _logger.LogInformation("Audit engine initialized. Log path: {AuditLogPath}", _auditLogPath);
    }

    /// <summary>
    /// Logs a network connection event
    /// </summary>
    public async Task LogNetworkEventAsync(
        uint processId,
        string processName,
        PolicyAction action,
        NetworkEventDetails details,
        ulong? ruleId = null,
        string? message = null)
    {
        var auditEvent = new AuditEvent
        {
            EventType = AuditEventType.NetworkConnection,
            Severity = action == PolicyAction.Block ? EventSeverity.Warning : EventSeverity.Info,
            ProcessId = processId,
            ProcessName = processName,
            Action = action,
            RuleId = ruleId,
            ResourceType = "Network",
            NetworkDetails = details,
            Message = message ?? $"Network connection {action.ToString().ToLowerInvariant()}"
        };

        await LogEventAsync(auditEvent);
    }

    /// <summary>
    /// Logs a device access event
    /// </summary>
    public async Task LogDeviceEventAsync(
        uint processId,
        string processName,
        PolicyAction action,
        DeviceEventDetails details,
        ulong? ruleId = null,
        string? message = null)
    {
        var auditEvent = new AuditEvent
        {
            EventType = AuditEventType.DeviceAccess,
            Severity = action == PolicyAction.Block ? EventSeverity.Warning : EventSeverity.Info,
            ProcessId = processId,
            ProcessName = processName,
            Action = action,
            RuleId = ruleId,
            ResourceType = "Device",
            DeviceDetails = details,
            Message = message ?? $"Device access {action.ToString().ToLowerInvariant()}"
        };

        await LogEventAsync(auditEvent);
    }

    /// <summary>
    /// Logs a policy change event
    /// </summary>
    public async Task LogPolicyChangeAsync(
        string changeType,
        ulong? ruleId,
        string details)
    {
        var auditEvent = new AuditEvent
        {
            EventType = AuditEventType.PolicyChange,
            Severity = EventSeverity.Warning,
            ProcessId = (uint)Environment.ProcessId,
            ProcessName = "SecureHostService",
            Action = PolicyAction.Audit,
            RuleId = ruleId,
            ResourceType = "Policy",
            Message = $"{changeType}: {details}"
        };

        await LogEventAsync(auditEvent);
    }

    /// <summary>
    /// Logs a tamper attempt
    /// </summary>
    public async Task LogTamperAttemptAsync(
        uint processId,
        string processName,
        string details)
    {
        var auditEvent = new AuditEvent
        {
            EventType = AuditEventType.TamperAttempt,
            Severity = EventSeverity.Critical,
            ProcessId = processId,
            ProcessName = processName,
            Action = PolicyAction.Block,
            ResourceType = "System",
            Message = $"TAMPER ATTEMPT: {details}"
        };

        await LogEventAsync(auditEvent);

        // Also write to Windows Event Log immediately
        _logger.LogCritical(
            "SECURITY ALERT: Tamper attempt detected from PID {ProcessId} ({ProcessName}): {Details}",
            processId, processName, details);
    }

    /// <summary>
    /// Core event logging method
    /// </summary>
    private async Task LogEventAsync(AuditEvent auditEvent)
    {
        // Enrich with current user info
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            auditEvent.UserSid = identity.User?.Value;
            auditEvent.UserName = identity.Name;
        }
        catch
        {
            // Ignore errors getting user info
        }

        // Queue for batch writing
        _eventQueue.Enqueue(auditEvent);

        // Write to ETW immediately for real-time monitoring
        _eventSource.WriteAuditEvent(
            auditEvent.Id.ToString(),
            auditEvent.EventType.ToString(),
            auditEvent.Severity.ToString(),
            auditEvent.ProcessName,
            auditEvent.Message);

        // Log to standard logger
        var logLevel = auditEvent.Severity switch
        {
            EventSeverity.Info => LogLevel.Information,
            EventSeverity.Warning => LogLevel.Warning,
            EventSeverity.Error => LogLevel.Error,
            EventSeverity.Critical => LogLevel.Critical,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel,
            "Audit Event: {EventType} | PID: {ProcessId} | Action: {Action} | Message: {Message}",
            auditEvent.EventType, auditEvent.ProcessId, auditEvent.Action, auditEvent.Message);

        // Flush if queue is large
        if (_eventQueue.Count >= 100)
        {
            await FlushEventsAsync();
        }
    }

    /// <summary>
    /// Timer callback for periodic flush
    /// </summary>
    private void FlushTimerCallback(object? state)
    {
        _ = FlushEventsAsync();
    }

    /// <summary>
    /// Flushes queued events to disk
    /// </summary>
    private async Task FlushEventsAsync()
    {
        if (!await _flushSemaphore.WaitAsync(0))
            return; // Another flush in progress

        try
        {
            if (_eventQueue.IsEmpty)
                return;

            var eventsToFlush = new List<AuditEvent>();
            while (_eventQueue.TryDequeue(out var evt) && eventsToFlush.Count < 1000)
            {
                eventsToFlush.Add(evt);
            }

            if (eventsToFlush.Count == 0)
                return;

            // Append to audit log file (JSON Lines format)
            var logFile = GetCurrentLogFilePath();
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false
            };

            await using var writer = new StreamWriter(logFile, append: true);
            foreach (var evt in eventsToFlush)
            {
                var json = JsonSerializer.Serialize(evt, jsonOptions);
                await writer.WriteLineAsync(json);
            }

            _logger.LogDebug("Flushed {Count} audit events to disk", eventsToFlush.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing audit events");
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    /// <summary>
    /// Gets the current log file path (rotates daily)
    /// </summary>
    private string GetCurrentLogFilePath()
    {
        var directory = Path.GetDirectoryName(_auditLogPath)!;
        var baseName = Path.GetFileNameWithoutExtension(_auditLogPath);
        var extension = Path.GetExtension(_auditLogPath);
        var dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");

        return Path.Combine(directory, $"{baseName}_{dateStamp}{extension}");
    }

    /// <summary>
    /// Exports events to SIEM format (CEF)
    /// </summary>
    public async Task<int> ExportToSiemAsync(DateTime startTime, DateTime endTime, string outputPath)
    {
        var events = await ReadEventsAsync(startTime, endTime);
        var cefLines = new List<string>();

        foreach (var evt in events)
        {
            var cef = ConvertToCef(evt);
            cefLines.Add(cef);
        }

        await File.WriteAllLinesAsync(outputPath, cefLines);
        _logger.LogInformation("Exported {Count} events to SIEM format: {OutputPath}", cefLines.Count, outputPath);

        return cefLines.Count;
    }

    /// <summary>
    /// Reads audit events from log files
    /// </summary>
    private async Task<List<AuditEvent>> ReadEventsAsync(DateTime startTime, DateTime endTime)
    {
        var events = new List<AuditEvent>();
        var directory = Path.GetDirectoryName(_auditLogPath)!;
        var pattern = Path.GetFileNameWithoutExtension(_auditLogPath) + "_*.jsonl";

        var files = Directory.GetFiles(directory, pattern);

        foreach (var file in files)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(file);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var evt = JsonSerializer.Deserialize<AuditEvent>(line);
                    if (evt != null &&
                        evt.Timestamp >= startTime &&
                        evt.Timestamp <= endTime)
                    {
                        events.Add(evt);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading audit log file: {File}", file);
            }
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// Converts audit event to CEF (Common Event Format) for SIEM
    /// </summary>
    private string ConvertToCef(AuditEvent evt)
    {
        // CEF format:
        // CEF:Version|Device Vendor|Device Product|Device Version|Signature ID|Name|Severity|Extension
        var severity = evt.Severity switch
        {
            EventSeverity.Info => 0,
            EventSeverity.Warning => 5,
            EventSeverity.Error => 7,
            EventSeverity.Critical => 10,
            _ => 0
        };

        var extension = $"dvchost={Environment.MachineName} " +
                       $"duser={evt.UserName} " +
                       $"suid={evt.UserSid} " +
                       $"sproc={evt.ProcessName} " +
                       $"spid={evt.ProcessId} " +
                       $"act={evt.Action} " +
                       $"msg={EscapeCef(evt.Message)}";

        return $"CEF:0|SecureHost|SecureHostSuite|1.0|{evt.EventType}|{evt.EventType}|{severity}|{extension}";
    }

    private static string EscapeCef(string value)
    {
        return value.Replace("\\", "\\\\")
                   .Replace("|", "\\|")
                   .Replace("=", "\\=")
                   .Replace("\n", "\\n")
                   .Replace("\r", "\\r");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _flushTimer.Dispose();
        _eventSource.Dispose();
        _flushSemaphore.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// ETW Event Source for SecureHost audit events
/// </summary>
[EventSource(Name = "SecureHost-Audit")]
public sealed class SecureHostEventSource : EventSource
{
    [Event(1, Level = EventLevel.Informational, Message = "Audit Event: {0} | Type: {1} | Severity: {2} | Process: {3} | Message: {4}")]
    public void WriteAuditEvent(string eventId, string eventType, string severity, string processName, string message)
    {
        WriteEvent(1, eventId, eventType, severity, processName, message);
    }

    [Event(2, Level = EventLevel.Critical, Message = "Tamper Attempt: PID {0} | Process: {1} | Details: {2}")]
    public void WriteTamperAttempt(uint processId, string processName, string details)
    {
        WriteEvent(2, processId, processName, details);
    }
}
