using Microsoft.Extensions.Logging;
using SecureHostCore.Engine;
using SecureHostCore.Models;
using SecureHostCore.Storage;

namespace SecureHostService.Services;

/// <summary>
/// Manages policy rules: loading, saving, and persistence
/// </summary>
public sealed class PolicyManagementService
{
    private readonly ILogger<PolicyManagementService> _logger;
    private readonly PolicyEngine _policyEngine;
    private readonly AuditEngine _auditEngine;
    private readonly SecureStorage _storage;
    private readonly DriverCommunicationService _driverComm;
    private DeviceControlService? _deviceControl;

    private const string POLICY_STORAGE_KEY = "policies";

    public PolicyManagementService(
        ILogger<PolicyManagementService> logger,
        PolicyEngine policyEngine,
        AuditEngine auditEngine,
        SecureStorage storage,
        DriverCommunicationService driverComm)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
        _auditEngine = auditEngine ?? throw new ArgumentNullException(nameof(auditEngine));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _driverComm = driverComm ?? throw new ArgumentNullException(nameof(driverComm));
    }

    /// <summary>
    /// Sets the device control service for enforcement
    /// </summary>
    public void SetDeviceControlService(DeviceControlService deviceControl)
    {
        _deviceControl = deviceControl;
    }

    /// <summary>
    /// Loads policies from secure storage
    /// </summary>
    public async Task LoadPoliciesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Loading policies from secure storage...");

            var policies = await _storage.LoadAsync<List<PolicyRule>>(POLICY_STORAGE_KEY);

            if (policies == null || policies.Count == 0)
            {
                _logger.LogInformation("No policies found in storage, loading defaults");
                await LoadDefaultPoliciesAsync(cancellationToken);
                return;
            }

            foreach (var policy in policies)
            {
                var ruleId = _policyEngine.AddRule(policy);
                await SyncRuleToDriverAsync(policy, cancellationToken);
            }

            _logger.LogInformation("Loaded {Count} policies from storage", policies.Count);
            await _auditEngine.LogPolicyChangeAsync(
                "PoliciesLoaded",
                null,
                $"Loaded {policies.Count} policy rules from storage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading policies");
            await LoadDefaultPoliciesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Saves policies to secure storage
    /// </summary>
    public async Task SavePoliciesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Saving policies to secure storage...");

            var policies = _policyEngine.GetAllRules();
            await _storage.SaveAsync(POLICY_STORAGE_KEY, policies.ToList());

            _logger.LogInformation("Saved {Count} policies to storage", policies.Count);
            await _auditEngine.LogPolicyChangeAsync(
                "PoliciesSaved",
                null,
                $"Saved {policies.Count} policy rules to storage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving policies");
        }
    }

    /// <summary>
    /// Adds a new policy rule
    /// </summary>
    public async Task<ulong> AddRuleAsync(PolicyRule rule, CancellationToken cancellationToken)
    {
        var ruleId = _policyEngine.AddRule(rule);

        await SyncRuleToDriverAsync(rule, cancellationToken);
        await SavePoliciesAsync(cancellationToken);

        // Enforce device policies if this is a device rule
        if (rule.Type == PolicyRuleType.Device && _deviceControl != null)
        {
            await _deviceControl.EnforcePoliciesAsync();
        }

        await _auditEngine.LogPolicyChangeAsync(
            "RuleAdded",
            ruleId,
            $"Added policy rule: {rule.Name}");

        return ruleId;
    }

    /// <summary>
    /// Updates an existing policy rule
    /// </summary>
    public async Task<bool> UpdateRuleAsync(ulong ruleId, PolicyRule rule, CancellationToken cancellationToken)
    {
        var success = _policyEngine.UpdateRule(ruleId, rule);
        if (!success)
            return false;

        await SyncRuleToDriverAsync(rule, cancellationToken);
        await SavePoliciesAsync(cancellationToken);

        // Enforce device policies if this is a device rule
        if (rule.Type == PolicyRuleType.Device && _deviceControl != null)
        {
            await _deviceControl.EnforcePoliciesAsync();
        }

        await _auditEngine.LogPolicyChangeAsync(
            "RuleUpdated",
            ruleId,
            $"Updated policy rule: {rule.Name}");

        return true;
    }

    /// <summary>
    /// Removes a policy rule
    /// </summary>
    public async Task<bool> RemoveRuleAsync(ulong ruleId, CancellationToken cancellationToken)
    {
        var rule = _policyEngine.GetRule(ruleId);
        if (rule == null)
            return false;

        var isDeviceRule = rule.Type == PolicyRuleType.Device;

        var success = _policyEngine.RemoveRule(ruleId);
        if (!success)
            return false;

        await SavePoliciesAsync(cancellationToken);

        // Enforce device policies if this was a device rule
        if (isDeviceRule && _deviceControl != null)
        {
            await _deviceControl.EnforcePoliciesAsync();
        }

        await _auditEngine.LogPolicyChangeAsync(
            "RuleRemoved",
            ruleId,
            $"Removed policy rule: {rule.Name}");

        return true;
    }

    /// <summary>
    /// Syncs a rule to the kernel driver
    /// </summary>
    private async Task SyncRuleToDriverAsync(PolicyRule rule, CancellationToken cancellationToken)
    {
        if (!rule.Enabled)
            return;

        try
        {
            if (rule.Type == PolicyRuleType.Network)
            {
                await _driverComm.SendNetworkRuleAsync(
                    rule.Id,
                    rule.ProcessId,
                    (ushort)rule.Protocol,
                    rule.LocalPort,
                    rule.RemotePort,
                    (uint)rule.Action,
                    cancellationToken);
            }
            else if (rule.Type == PolicyRuleType.Device)
            {
                await _driverComm.SendDeviceRuleAsync(
                    rule.Id,
                    rule.ProcessId,
                    (uint)rule.DeviceType,
                    (uint)rule.Action,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing rule {RuleId} to driver", rule.Id);
        }
    }

    /// <summary>
    /// Loads default policies on first run
    /// </summary>
    private async Task LoadDefaultPoliciesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading default policies...");

        var defaultRules = new List<PolicyRule>
        {
            // Block all camera access by default
            new PolicyRule
            {
                Name = "Block Camera Access (Default)",
                Type = PolicyRuleType.Device,
                DeviceType = DeviceType.Camera,
                Action = PolicyAction.Block,
                Priority = 100,
                Enabled = true,
                AuditLevel = AuditLevel.High
            },

            // Block all microphone access by default
            new PolicyRule
            {
                Name = "Block Microphone Access (Default)",
                Type = PolicyRuleType.Device,
                DeviceType = DeviceType.Microphone,
                Action = PolicyAction.Block,
                Priority = 100,
                Enabled = true,
                AuditLevel = AuditLevel.High
            },

            // Audit all outbound connections on non-standard ports
            new PolicyRule
            {
                Name = "Audit Non-Standard Outbound Connections",
                Type = PolicyRuleType.Network,
                Protocol = NetworkProtocol.Any,
                Action = PolicyAction.Audit,
                Priority = 50,
                Enabled = true,
                AuditLevel = AuditLevel.Normal
            }
        };

        foreach (var rule in defaultRules)
        {
            var ruleId = _policyEngine.AddRule(rule);
            await SyncRuleToDriverAsync(rule, cancellationToken);
        }

        await SavePoliciesAsync(cancellationToken);

        // Enforce device policies after loading defaults
        if (_deviceControl != null)
        {
            await _deviceControl.EnforcePoliciesAsync();
        }

        _logger.LogInformation("Loaded {Count} default policies", defaultRules.Count);
    }
}
