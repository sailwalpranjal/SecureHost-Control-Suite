using System.Collections.Concurrent;
using System.Security.Principal;
using SecureHostCore.Models;
using Microsoft.Extensions.Logging;

namespace SecureHostCore.Engine;

/// <summary>
/// Core policy evaluation and enforcement engine
/// Thread-safe, high-performance policy rule evaluation
/// </summary>
public sealed class PolicyEngine : IDisposable
{
    private readonly ILogger<PolicyEngine> _logger;
    private readonly ConcurrentDictionary<ulong, PolicyRule> _rules;
    private readonly ReaderWriterLockSlim _rulesLock;
    private ulong _nextRuleId;
    private bool _disposed;

    public PolicyEngine(ILogger<PolicyEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rules = new ConcurrentDictionary<ulong, PolicyRule>();
        _rulesLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        _nextRuleId = 1;
    }

    /// <summary>
    /// Adds a new policy rule
    /// </summary>
    public ulong AddRule(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        _rulesLock.EnterWriteLock();
        try
        {
            rule.Id = _nextRuleId++;
            rule.CreatedAt = DateTime.UtcNow;
            rule.ModifiedAt = DateTime.UtcNow;

            if (!_rules.TryAdd(rule.Id, rule))
            {
                _logger.LogError("Failed to add rule {RuleId}", rule.Id);
                throw new InvalidOperationException($"Failed to add rule {rule.Id}");
            }

            _logger.LogInformation("Added policy rule {RuleId}: {RuleName}", rule.Id, rule.Name);
            return rule.Id;
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Updates an existing policy rule
    /// </summary>
    public bool UpdateRule(ulong ruleId, PolicyRule updatedRule)
    {
        ArgumentNullException.ThrowIfNull(updatedRule);

        _rulesLock.EnterWriteLock();
        try
        {
            if (!_rules.TryGetValue(ruleId, out var existingRule))
            {
                _logger.LogWarning("Rule {RuleId} not found for update", ruleId);
                return false;
            }

            updatedRule.Id = ruleId;
            updatedRule.CreatedAt = existingRule.CreatedAt;
            updatedRule.ModifiedAt = DateTime.UtcNow;

            _rules[ruleId] = updatedRule;
            _logger.LogInformation("Updated policy rule {RuleId}: {RuleName}", ruleId, updatedRule.Name);
            return true;
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a policy rule
    /// </summary>
    public bool RemoveRule(ulong ruleId)
    {
        _rulesLock.EnterWriteLock();
        try
        {
            if (_rules.TryRemove(ruleId, out var rule))
            {
                _logger.LogInformation("Removed policy rule {RuleId}: {RuleName}", ruleId, rule.Name);
                return true;
            }

            _logger.LogWarning("Rule {RuleId} not found for removal", ruleId);
            return false;
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets a specific rule by ID
    /// </summary>
    public PolicyRule? GetRule(ulong ruleId)
    {
        _rulesLock.EnterReadLock();
        try
        {
            return _rules.TryGetValue(ruleId, out var rule) ? rule.Clone() : null;
        }
        finally
        {
            _rulesLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all rules
    /// </summary>
    public IReadOnlyList<PolicyRule> GetAllRules()
    {
        _rulesLock.EnterReadLock();
        try
        {
            return _rules.Values.Select(r => r.Clone()).ToList();
        }
        finally
        {
            _rulesLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Evaluates network connection request
    /// </summary>
    public PolicyDecision EvaluateNetworkConnection(
        uint processId,
        string processName,
        NetworkProtocol protocol,
        ushort localPort,
        ushort remotePort,
        string? remoteAddress,
        string? userSid)
    {
        _rulesLock.EnterReadLock();
        try
        {
            // Get applicable rules, sorted by priority (descending)
            var applicableRules = _rules.Values
                .Where(r => r.Type == PolicyRuleType.Network && r.IsValid())
                .OrderByDescending(r => r.Priority)
                .ToList();

            foreach (var rule in applicableRules)
            {
                if (MatchesNetworkRule(rule, processId, processName, protocol, localPort, remotePort, remoteAddress, userSid))
                {
                    _logger.LogDebug(
                        "Network connection matched rule {RuleId}: {RuleName} - Action: {Action}",
                        rule.Id, rule.Name, rule.Action);

                    return new PolicyDecision
                    {
                        Action = rule.Action,
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        Reason = $"Matched rule: {rule.Name}",
                        AuditLevel = rule.AuditLevel
                    };
                }
            }

            // Default: allow (can be configured)
            _logger.LogDebug("No matching network rule found, applying default policy (Allow)");
            return new PolicyDecision
            {
                Action = PolicyAction.Allow,
                Reason = "No matching rule (default policy)",
                AuditLevel = AuditLevel.Low
            };
        }
        finally
        {
            _rulesLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Evaluates device access request
    /// </summary>
    public PolicyDecision EvaluateDeviceAccess(
        uint processId,
        string processName,
        DeviceType deviceType,
        string? deviceHardwareId,
        string? userSid)
    {
        _rulesLock.EnterReadLock();
        try
        {
            var applicableRules = _rules.Values
                .Where(r => r.Type == PolicyRuleType.Device && r.IsValid())
                .OrderByDescending(r => r.Priority)
                .ToList();

            foreach (var rule in applicableRules)
            {
                if (MatchesDeviceRule(rule, processId, processName, deviceType, deviceHardwareId, userSid))
                {
                    _logger.LogDebug(
                        "Device access matched rule {RuleId}: {RuleName} - Action: {Action}",
                        rule.Id, rule.Name, rule.Action);

                    return new PolicyDecision
                    {
                        Action = rule.Action,
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        Reason = $"Matched rule: {rule.Name}",
                        AuditLevel = rule.AuditLevel
                    };
                }
            }

            // Default: block device access (secure default)
            _logger.LogDebug("No matching device rule found, applying default policy (Block)");
            return new PolicyDecision
            {
                Action = PolicyAction.Block,
                Reason = "No matching rule (default policy: deny)",
                AuditLevel = AuditLevel.High
            };
        }
        finally
        {
            _rulesLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Checks if a network rule matches the connection parameters
    /// </summary>
    private bool MatchesNetworkRule(
        PolicyRule rule,
        uint processId,
        string processName,
        NetworkProtocol protocol,
        ushort localPort,
        ushort remotePort,
        string? remoteAddress,
        string? userSid)
    {
        // Process ID filter (0 = match all)
        if (rule.ProcessId != 0 && rule.ProcessId != processId)
            return false;

        // Process name filter (with wildcard support)
        if (!string.IsNullOrEmpty(rule.ProcessName) &&
            !MatchesWildcard(processName, rule.ProcessName))
            return false;

        // Protocol filter
        if (rule.Protocol != NetworkProtocol.Any && rule.Protocol != protocol)
            return false;

        // Local port filter (0 = match all)
        if (rule.LocalPort != 0 && rule.LocalPort != localPort)
            return false;

        // Remote port filter (0 = match all)
        if (rule.RemotePort != 0 && rule.RemotePort != remotePort)
            return false;

        // Remote address filter (CIDR support)
        if (!string.IsNullOrEmpty(rule.RemoteAddress) &&
            !string.IsNullOrEmpty(remoteAddress) &&
            !MatchesAddress(remoteAddress, rule.RemoteAddress))
            return false;

        // User SID filter
        if (!string.IsNullOrEmpty(rule.UserSid) &&
            !string.Equals(rule.UserSid, userSid, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a device rule matches the access parameters
    /// </summary>
    private bool MatchesDeviceRule(
        PolicyRule rule,
        uint processId,
        string processName,
        DeviceType deviceType,
        string? deviceHardwareId,
        string? userSid)
    {
        if (rule.ProcessId != 0 && rule.ProcessId != processId)
            return false;

        if (!string.IsNullOrEmpty(rule.ProcessName) &&
            !MatchesWildcard(processName, rule.ProcessName))
            return false;

        if (rule.DeviceType != DeviceType.Unknown && rule.DeviceType != deviceType)
            return false;

        if (!string.IsNullOrEmpty(rule.DeviceHardwareId) &&
            !string.IsNullOrEmpty(deviceHardwareId) &&
            !MatchesWildcard(deviceHardwareId, rule.DeviceHardwareId))
            return false;

        if (!string.IsNullOrEmpty(rule.UserSid) &&
            !string.Equals(rule.UserSid, userSid, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Simple wildcard matching (* and ?)
    /// </summary>
    private static bool MatchesWildcard(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;

        if (pattern == "*")
            return true;

        // Convert wildcard pattern to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            input,
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Basic IP address/CIDR matching
    /// </summary>
    private static bool MatchesAddress(string address, string pattern)
    {
        // Simple implementation - production should use proper CIDR parsing
        return string.Equals(address, pattern, StringComparison.OrdinalIgnoreCase) ||
               pattern == "*" ||
               address.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clears all rules
    /// </summary>
    public void ClearAllRules()
    {
        _rulesLock.EnterWriteLock();
        try
        {
            _rules.Clear();
            _logger.LogWarning("All policy rules cleared");
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _rulesLock.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Policy evaluation decision
/// </summary>
public sealed class PolicyDecision
{
    public PolicyAction Action { get; set; }
    public ulong? RuleId { get; set; }
    public string? RuleName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public AuditLevel AuditLevel { get; set; }
}
