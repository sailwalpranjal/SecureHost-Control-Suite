using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SecureHostCore.Engine;
using SecureHostCore.Models;

namespace SecureHostTests;

public class PolicyEngineTests : IDisposable
{
    private readonly PolicyEngine _policyEngine;

    public PolicyEngineTests()
    {
        _policyEngine = new PolicyEngine(NullLogger<PolicyEngine>.Instance);
    }

    public void Dispose()
    {
        _policyEngine.Dispose();
    }

    [Fact]
    public void AddRule_ShouldAddRuleSuccessfully()
    {
        // Arrange
        var rule = new PolicyRule
        {
            Name = "Test Rule",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            Protocol = NetworkProtocol.TCP,
            LocalPort = 443
        };

        // Act
        var ruleId = _policyEngine.AddRule(rule);

        // Assert
        ruleId.Should().BeGreaterThan(0);
        var retrievedRule = _policyEngine.GetRule(ruleId);
        retrievedRule.Should().NotBeNull();
        retrievedRule!.Name.Should().Be("Test Rule");
    }

    [Fact]
    public void GetAllRules_ShouldReturnAllRules()
    {
        // Arrange
        _policyEngine.AddRule(new PolicyRule { Name = "Rule 1", Type = PolicyRuleType.Network, Action = PolicyAction.Allow });
        _policyEngine.AddRule(new PolicyRule { Name = "Rule 2", Type = PolicyRuleType.Device, Action = PolicyAction.Block });
        _policyEngine.AddRule(new PolicyRule { Name = "Rule 3", Type = PolicyRuleType.Application, Action = PolicyAction.Audit });

        // Act
        var rules = _policyEngine.GetAllRules();

        // Assert
        rules.Should().HaveCount(3);
    }

    [Fact]
    public void UpdateRule_ShouldUpdateExistingRule()
    {
        // Arrange
        var originalRule = new PolicyRule
        {
            Name = "Original",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Allow
        };
        var ruleId = _policyEngine.AddRule(originalRule);

        var updatedRule = new PolicyRule
        {
            Name = "Updated",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block
        };

        // Act
        var success = _policyEngine.UpdateRule(ruleId, updatedRule);

        // Assert
        success.Should().BeTrue();
        var retrievedRule = _policyEngine.GetRule(ruleId);
        retrievedRule!.Name.Should().Be("Updated");
        retrievedRule.Action.Should().Be(PolicyAction.Block);
    }

    [Fact]
    public void RemoveRule_ShouldRemoveExistingRule()
    {
        // Arrange
        var rule = new PolicyRule
        {
            Name = "To Delete",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block
        };
        var ruleId = _policyEngine.AddRule(rule);

        // Act
        var success = _policyEngine.RemoveRule(ruleId);

        // Assert
        success.Should().BeTrue();
        _policyEngine.GetRule(ruleId).Should().BeNull();
    }

    [Fact]
    public void EvaluateNetworkConnection_WithMatchingRule_ShouldReturnCorrectDecision()
    {
        // Arrange
        var rule = new PolicyRule
        {
            Name = "Block HTTPS",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            Protocol = NetworkProtocol.TCP,
            LocalPort = 443,
            Enabled = true
        };
        _policyEngine.AddRule(rule);

        // Act
        var decision = _policyEngine.EvaluateNetworkConnection(
            processId: 1234,
            processName: "chrome.exe",
            protocol: NetworkProtocol.TCP,
            localPort: 443,
            remotePort: 443,
            remoteAddress: "93.184.216.34",
            userSid: null
        );

        // Assert
        decision.Action.Should().Be(PolicyAction.Block);
        decision.RuleId.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EvaluateNetworkConnection_WithNoMatchingRule_ShouldReturnDefaultPolicy()
    {
        // Arrange - no rules

        // Act
        var decision = _policyEngine.EvaluateNetworkConnection(
            processId: 1234,
            processName: "notepad.exe",
            protocol: NetworkProtocol.TCP,
            localPort: 12345,
            remotePort: 80,
            remoteAddress: "8.8.8.8",
            userSid: null
        );

        // Assert
        decision.Action.Should().Be(PolicyAction.Allow); // Default policy
        decision.RuleId.Should().BeNull();
    }

    [Fact]
    public void EvaluateDeviceAccess_WithBlockRule_ShouldReturnBlock()
    {
        // Arrange
        var rule = new PolicyRule
        {
            Name = "Block Camera",
            Type = PolicyRuleType.Device,
            Action = PolicyAction.Block,
            DeviceType = DeviceType.Camera,
            Enabled = true
        };
        _policyEngine.AddRule(rule);

        // Act
        var decision = _policyEngine.EvaluateDeviceAccess(
            processId: 5678,
            processName: "chrome.exe",
            deviceType: DeviceType.Camera,
            deviceHardwareId: "USB\\VID_0000&PID_0001",
            userSid: null
        );

        // Assert
        decision.Action.Should().Be(PolicyAction.Block);
    }

    [Fact]
    public void EvaluateDeviceAccess_WithProcessFilter_ShouldMatchCorrectly()
    {
        // Arrange
        var rule = new PolicyRule
        {
            Name = "Allow Teams Camera",
            Type = PolicyRuleType.Device,
            Action = PolicyAction.Allow,
            DeviceType = DeviceType.Camera,
            ProcessName = "Teams.exe",
            Enabled = true,
            Priority = 200
        };
        _policyEngine.AddRule(rule);

        var blockRule = new PolicyRule
        {
            Name = "Block All Camera",
            Type = PolicyRuleType.Device,
            Action = PolicyAction.Block,
            DeviceType = DeviceType.Camera,
            Enabled = true,
            Priority = 100
        };
        _policyEngine.AddRule(blockRule);

        // Act - Teams should be allowed (higher priority)
        var teamsDecision = _policyEngine.EvaluateDeviceAccess(
            processId: 1111,
            processName: "Teams.exe",
            deviceType: DeviceType.Camera,
            deviceHardwareId: null,
            userSid: null
        );

        var chromeDecision = _policyEngine.EvaluateDeviceAccess(
            processId: 2222,
            processName: "chrome.exe",
            deviceType: DeviceType.Camera,
            deviceHardwareId: null,
            userSid: null
        );

        // Assert
        teamsDecision.Action.Should().Be(PolicyAction.Allow);
        chromeDecision.Action.Should().Be(PolicyAction.Block);
    }

    [Fact]
    public void PolicyRule_IsValid_ShouldRespectTemporalConstraints()
    {
        // Arrange
        var futureRule = new PolicyRule
        {
            Name = "Future Rule",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            Enabled = true,
            ValidFrom = DateTime.UtcNow.AddDays(1)
        };

        var expiredRule = new PolicyRule
        {
            Name = "Expired Rule",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            Enabled = true,
            ValidUntil = DateTime.UtcNow.AddDays(-1)
        };

        var activeRule = new PolicyRule
        {
            Name = "Active Rule",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            Enabled = true,
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        // Act & Assert
        futureRule.IsValid().Should().BeFalse();
        expiredRule.IsValid().Should().BeFalse();
        activeRule.IsValid().Should().BeTrue();
    }

    [Fact]
    public void PolicyRule_Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var original = new PolicyRule
        {
            Id = 123,
            Name = "Original",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            Metadata = new Dictionary<string, string> { { "key", "value" } }
        };

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original);
        clone.Id.Should().Be(original.Id);
        clone.Name.Should().Be(original.Name);
        clone.Metadata.Should().NotBeSameAs(original.Metadata);
        clone.Metadata.Should().ContainKey("key");
    }

    [Fact]
    public void ClearAllRules_ShouldRemoveAllRules()
    {
        // Arrange
        _policyEngine.AddRule(new PolicyRule { Name = "Rule 1", Type = PolicyRuleType.Network, Action = PolicyAction.Allow });
        _policyEngine.AddRule(new PolicyRule { Name = "Rule 2", Type = PolicyRuleType.Network, Action = PolicyAction.Block });

        // Act
        _policyEngine.ClearAllRules();

        // Assert
        _policyEngine.GetAllRules().Should().BeEmpty();
    }

    [Theory]
    [InlineData("chrome.exe", "chrome.exe", true)]
    [InlineData("chrome.exe", "*.exe", true)]
    [InlineData("chrome.exe", "chrom?.exe", true)]
    [InlineData("chrome.exe", "firefox.exe", false)]
    [InlineData("notepad.exe", "*", true)]
    public void ProcessNameFilter_ShouldMatchWildcardsCorrectly(string processName, string filter, bool shouldMatch)
    {
        // Arrange
        var rule = new PolicyRule
        {
            Name = "Wildcard Test",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            ProcessName = filter,
            Protocol = NetworkProtocol.TCP,
            Enabled = true
        };
        _policyEngine.AddRule(rule);

        // Act
        var decision = _policyEngine.EvaluateNetworkConnection(
            processId: 1234,
            processName: processName,
            protocol: NetworkProtocol.TCP,
            localPort: 443,
            remotePort: 443,
            remoteAddress: null,
            userSid: null
        );

        // Assert
        if (shouldMatch)
        {
            decision.Action.Should().Be(PolicyAction.Block);
        }
        else
        {
            decision.Action.Should().Be(PolicyAction.Allow); // Default policy
        }
    }
}
