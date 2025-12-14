using System.Text.Json.Serialization;

namespace SecureHostCore.Models;

/// <summary>
/// Represents a policy rule for network or device access control
/// </summary>
public sealed class PolicyRule
{
    /// <summary>
    /// Unique identifier for the rule
    /// </summary>
    [JsonPropertyName("id")]
    public ulong Id { get; set; }

    /// <summary>
    /// Rule name/description
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Rule type (Network, Device, Application)
    /// </summary>
    [JsonPropertyName("type")]
    public PolicyRuleType Type { get; set; }

    /// <summary>
    /// Target (process name, device ID, etc.)
    /// </summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Action to take (Allow, Block, Audit)
    /// </summary>
    [JsonPropertyName("action")]
    public PolicyAction Action { get; set; }

    /// <summary>
    /// Process ID (0 = all processes)
    /// </summary>
    [JsonPropertyName("processId")]
    public uint ProcessId { get; set; }

    /// <summary>
    /// Process name filter (wildcards supported)
    /// </summary>
    [JsonPropertyName("processName")]
    public string? ProcessName { get; set; }

    /// <summary>
    /// Network protocol (TCP, UDP, ICMP, Any)
    /// </summary>
    [JsonPropertyName("protocol")]
    public NetworkProtocol Protocol { get; set; }

    /// <summary>
    /// Local port (0 = any)
    /// </summary>
    [JsonPropertyName("localPort")]
    public ushort LocalPort { get; set; }

    /// <summary>
    /// Remote port (0 = any)
    /// </summary>
    [JsonPropertyName("remotePort")]
    public ushort RemotePort { get; set; }

    /// <summary>
    /// Remote IP address or CIDR range
    /// </summary>
    [JsonPropertyName("remoteAddress")]
    public string? RemoteAddress { get; set; }

    /// <summary>
    /// Device type (Camera, Microphone, USB, Bluetooth)
    /// </summary>
    [JsonPropertyName("deviceType")]
    public DeviceType DeviceType { get; set; }

    /// <summary>
    /// Device hardware ID filter
    /// </summary>
    [JsonPropertyName("deviceHardwareId")]
    public string? DeviceHardwareId { get; set; }

    /// <summary>
    /// Whether the rule is enabled
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Rule priority (higher = evaluated first)
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;

    /// <summary>
    /// User SID filter (null = all users)
    /// </summary>
    [JsonPropertyName("userSid")]
    public string? UserSid { get; set; }

    /// <summary>
    /// Temporal constraint - start time (UTC)
    /// </summary>
    [JsonPropertyName("validFrom")]
    public DateTime? ValidFrom { get; set; }

    /// <summary>
    /// Temporal constraint - end time (UTC)
    /// </summary>
    [JsonPropertyName("validUntil")]
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last modification timestamp
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Audit log level for this rule
    /// </summary>
    [JsonPropertyName("auditLevel")]
    public AuditLevel AuditLevel { get; set; } = AuditLevel.Normal;

    /// <summary>
    /// Custom metadata (JSON object)
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Checks if this rule is currently valid based on temporal constraints
    /// </summary>
    public bool IsValid()
    {
        if (!Enabled)
            return false;

        var now = DateTime.UtcNow;

        if (ValidFrom.HasValue && now < ValidFrom.Value)
            return false;

        if (ValidUntil.HasValue && now > ValidUntil.Value)
            return false;

        return true;
    }

    /// <summary>
    /// Creates a deep copy of this rule
    /// </summary>
    public PolicyRule Clone()
    {
        return new PolicyRule
        {
            Id = Id,
            Name = Name,
            Type = Type,
            Target = Target,
            Action = Action,
            ProcessId = ProcessId,
            ProcessName = ProcessName,
            Protocol = Protocol,
            LocalPort = LocalPort,
            RemotePort = RemotePort,
            RemoteAddress = RemoteAddress,
            DeviceType = DeviceType,
            DeviceHardwareId = DeviceHardwareId,
            Enabled = Enabled,
            Priority = Priority,
            UserSid = UserSid,
            ValidFrom = ValidFrom,
            ValidUntil = ValidUntil,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            AuditLevel = AuditLevel,
            Metadata = Metadata != null ? new Dictionary<string, string>(Metadata) : null
        };
    }
}

/// <summary>
/// Policy rule type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PolicyRuleType
{
    Network = 1,
    Device = 2,
    Application = 3
}

/// <summary>
/// Policy action
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PolicyAction
{
    Allow = 1,
    Block = 2,
    Audit = 3
}

/// <summary>
/// Network protocol
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkProtocol
{
    Any = 0,
    TCP = 6,
    UDP = 17,
    ICMP = 1,
    ICMPv6 = 58
}

/// <summary>
/// Device type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceType
{
    Unknown = 0,
    Camera = 1,
    Microphone = 2,
    USB = 3,
    Bluetooth = 4
}

/// <summary>
/// Audit log level
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditLevel
{
    None = 0,
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}
