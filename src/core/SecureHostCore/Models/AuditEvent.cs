using System.Text.Json.Serialization;

namespace SecureHostCore.Models;

/// <summary>
/// Represents an audit event for network or device access
/// </summary>
public sealed class AuditEvent
{
    /// <summary>
    /// Unique event ID
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp (UTC)
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Event type
    /// </summary>
    [JsonPropertyName("eventType")]
    public AuditEventType EventType { get; set; }

    /// <summary>
    /// Event severity
    /// </summary>
    [JsonPropertyName("severity")]
    public EventSeverity Severity { get; set; }

    /// <summary>
    /// Process ID
    /// </summary>
    [JsonPropertyName("processId")]
    public uint ProcessId { get; set; }

    /// <summary>
    /// Process name
    /// </summary>
    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Process path
    /// </summary>
    [JsonPropertyName("processPath")]
    public string? ProcessPath { get; set; }

    /// <summary>
    /// User SID
    /// </summary>
    [JsonPropertyName("userSid")]
    public string? UserSid { get; set; }

    /// <summary>
    /// User name
    /// </summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    /// <summary>
    /// Policy rule ID that triggered this event
    /// </summary>
    [JsonPropertyName("ruleId")]
    public ulong? RuleId { get; set; }

    /// <summary>
    /// Action taken (Allowed, Blocked, Audited)
    /// </summary>
    [JsonPropertyName("action")]
    public PolicyAction Action { get; set; }

    /// <summary>
    /// Resource type (Network, Device, File, etc.)
    /// </summary>
    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Resource identifier (port, device ID, file path, etc.)
    /// </summary>
    [JsonPropertyName("resourceId")]
    public string? ResourceId { get; set; }

    /// <summary>
    /// Network-specific details
    /// </summary>
    [JsonPropertyName("networkDetails")]
    public NetworkEventDetails? NetworkDetails { get; set; }

    /// <summary>
    /// Device-specific details
    /// </summary>
    [JsonPropertyName("deviceDetails")]
    public DeviceEventDetails? DeviceDetails { get; set; }

    /// <summary>
    /// Event message/description
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Whether this event has been exported to SIEM
    /// </summary>
    [JsonPropertyName("exported")]
    public bool Exported { get; set; }

    /// <summary>
    /// Export timestamp
    /// </summary>
    [JsonPropertyName("exportedAt")]
    public DateTime? ExportedAt { get; set; }
}

/// <summary>
/// Audit event type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditEventType
{
    NetworkConnection = 1,
    DeviceAccess = 2,
    PolicyChange = 3,
    ServiceStart = 4,
    ServiceStop = 5,
    DriverLoad = 6,
    DriverUnload = 7,
    AuthenticationAttempt = 8,
    ConfigurationChange = 9,
    TamperAttempt = 10
}

/// <summary>
/// Event severity
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventSeverity
{
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

/// <summary>
/// Network event details
/// </summary>
public sealed class NetworkEventDetails
{
    [JsonPropertyName("protocol")]
    public NetworkProtocol Protocol { get; set; }

    [JsonPropertyName("localAddress")]
    public string? LocalAddress { get; set; }

    [JsonPropertyName("localPort")]
    public ushort LocalPort { get; set; }

    [JsonPropertyName("remoteAddress")]
    public string? RemoteAddress { get; set; }

    [JsonPropertyName("remotePort")]
    public ushort RemotePort { get; set; }

    [JsonPropertyName("direction")]
    public NetworkDirection Direction { get; set; }

    [JsonPropertyName("bytesTransferred")]
    public ulong BytesTransferred { get; set; }
}

/// <summary>
/// Device event details
/// </summary>
public sealed class DeviceEventDetails
{
    [JsonPropertyName("deviceType")]
    public DeviceType DeviceType { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("hardwareId")]
    public string? HardwareId { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("accessType")]
    public DeviceAccessType AccessType { get; set; }
}

/// <summary>
/// Network direction
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkDirection
{
    Inbound = 1,
    Outbound = 2
}

/// <summary>
/// Device access type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceAccessType
{
    Open = 1,
    Read = 2,
    Write = 3,
    Control = 4
}
