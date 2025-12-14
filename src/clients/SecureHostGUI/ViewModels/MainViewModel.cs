using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SecureHostCore.Models;
using SecureHostGUI.Services;

namespace SecureHostGUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ApiClient _apiClient;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Connecting...";

    [ObservableProperty]
    private string _serviceStatus = "Unknown";

    [ObservableProperty]
    private string _uptime = "N/A";

    [ObservableProperty]
    private int _totalRules;

    [ObservableProperty]
    private int _activeRules;

    [ObservableProperty]
    private int _activeConnections;

    [ObservableProperty]
    private string _statusBarText = "Ready";

    [ObservableProperty]
    private ObservableCollection<PolicyRule> _policyRules = new();

    [ObservableProperty]
    private ObservableCollection<NetworkConnection> _networkConnections = new();

    [ObservableProperty]
    private DateTime _auditStartDate = DateTime.Now.AddDays(-7);

    [ObservableProperty]
    private DateTime _auditEndDate = DateTime.Now;

    public MainViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task InitializeAsync()
    {
        await RefreshStatusAsync();
        await RefreshRulesAsync();
        await RefreshConnectionsAsync();

        // Start periodic refresh
        _ = StartPeriodicRefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            StatusBarText = "Refreshing status...";

            var status = await _apiClient.GetStatusAsync();
            if (status != null)
            {
                IsConnected = true;
                StatusText = "Connected";
                ServiceStatus = status.Service;
                Uptime = status.Uptime;
                TotalRules = status.RulesCount;
                ActiveRules = status.ActiveRules;
                StatusBarText = $"Last updated: {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                IsConnected = false;
                StatusText = "Disconnected";
                ServiceStatus = "Not running";
                StatusBarText = "Unable to connect to service";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = "Error";
            StatusBarText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshRulesAsync()
    {
        try
        {
            StatusBarText = "Loading policy rules...";

            var rules = await _apiClient.GetRulesAsync();
            PolicyRules.Clear();
            foreach (var rule in rules)
            {
                PolicyRules.Add(rule);
            }

            StatusBarText = $"Loaded {rules.Count} policy rules";
        }
        catch (Exception ex)
        {
            StatusBarText = $"Error loading rules: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshConnectionsAsync()
    {
        try
        {
            StatusBarText = "Loading network connections...";

            var connections = await _apiClient.GetConnectionsAsync();
            NetworkConnections.Clear();
            foreach (var conn in connections)
            {
                NetworkConnections.Add(conn);
            }

            ActiveConnections = connections.Count;
            StatusBarText = $"Loaded {connections.Count} active connections";
        }
        catch (Exception ex)
        {
            StatusBarText = $"Error loading connections: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddRuleAsync()
    {
        // In a production app, this would open a dialog to create a new rule
        var rule = new PolicyRule
        {
            Name = "New Rule",
            Type = PolicyRuleType.Network,
            Action = PolicyAction.Block,
            Enabled = true,
            Priority = 100
        };

        var success = await _apiClient.AddRuleAsync(rule);
        if (success)
        {
            await RefreshRulesAsync();
            StatusBarText = "Rule added successfully";
        }
        else
        {
            StatusBarText = "Failed to add rule";
        }
    }

    [RelayCommand]
    private async Task EditRuleAsync(PolicyRule? rule)
    {
        if (rule == null)
            return;

        // In a production app, this would open an edit dialog
        StatusBarText = $"Editing rule: {rule.Name}";
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(PolicyRule? rule)
    {
        if (rule == null)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the rule '{rule.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            var success = await _apiClient.DeleteRuleAsync(rule.Id);
            if (success)
            {
                await RefreshRulesAsync();
                StatusBarText = $"Rule '{rule.Name}' deleted";
            }
            else
            {
                StatusBarText = "Failed to delete rule";
            }
        }
    }

    [RelayCommand]
    private async Task ToggleRuleAsync(PolicyRule? rule)
    {
        if (rule == null)
            return;

        // Determine what action we're about to take
        var action = rule.Enabled ? "disable" : "enable";
        var deviceName = rule.Type == PolicyRuleType.Device ? rule.DeviceType.ToString() : "rule";

        // Show confirmation dialog
        var result = MessageBox.Show(
            $"Are you sure you want to {action} this rule?\n\n" +
            $"Rule: {rule.Name}\n" +
            $"Type: {rule.Type} ({deviceName})\n" +
            $"Action: {rule.Action}\n\n" +
            $"⚠️ WARNING: {(rule.Enabled ?
                $"Disabling this rule will IMMEDIATELY {(rule.Action == PolicyAction.Block ? "ENABLE" : "change")} {deviceName} access!" :
                $"Enabling this rule will IMMEDIATELY {rule.Action.ToString().ToUpper()} {deviceName} access!")}",
            "Confirm Permission Change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            StatusBarText = $"{(rule.Enabled ? "Disabling" : "Enabling")} rule: {rule.Name}...";

            var success = await _apiClient.ToggleRuleAsync(rule.Id);
            if (success)
            {
                // Refresh rules to get updated state
                await RefreshRulesAsync();
                await RefreshStatusAsync();
                StatusBarText = $"Rule '{rule.Name}' {(rule.Enabled ? "disabled" : "enabled")} successfully";

                MessageBox.Show(
                    $"Rule {(rule.Enabled ? "disabled" : "enabled")} successfully!\n\n" +
                    $"The change has been applied immediately.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                StatusBarText = $"Failed to toggle rule: {rule.Name}";
                MessageBox.Show(
                    $"Failed to toggle rule.\n\nPlease check the service connection and try again.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusBarText = $"Error toggling rule: {ex.Message}";
            MessageBox.Show(
                $"Error toggling rule:\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ResetAllDevicesAsync()
    {
        // Show strong warning dialog
        var result = MessageBox.Show(
            "⚠️ EMERGENCY DEVICE RESET ⚠️\n\n" +
            "This will IMMEDIATELY re-enable ALL devices on your computer, regardless of policy rules.\n\n" +
            "Use this if you've accidentally blocked critical devices or lost access.\n\n" +
            "⚠️ WARNING:\n" +
            "• All cameras, microphones, and other devices will be ENABLED\n" +
            "• Blocking rules will still exist and will re-apply on service restart\n" +
            "• This action cannot be undone\n\n" +
            "Are you ABSOLUTELY SURE you want to reset all devices?",
            "⚠️ EMERGENCY RESET - CONFIRM",
            MessageBoxButton.YesNo,
            MessageBoxImage.Exclamation);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            StatusBarText = "⚠️ RESETTING ALL DEVICES...";

            var success = await _apiClient.ResetAllDevicesAsync();
            if (success)
            {
                StatusBarText = "✓ All devices have been reset and re-enabled";

                MessageBox.Show(
                    "✓ RESET COMPLETE\n\n" +
                    "All devices have been re-enabled.\n\n" +
                    "IMPORTANT:\n" +
                    "• Your blocking rules are still active\n" +
                    "• To permanently enable devices, disable or delete the blocking rules\n" +
                    "• If you restart the service, blocked devices will be disabled again",
                    "Reset Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Refresh to show current state
                await RefreshStatusAsync();
                await RefreshRulesAsync();
            }
            else
            {
                StatusBarText = "Failed to reset devices";
                MessageBox.Show(
                    "Failed to reset devices.\n\n" +
                    "Please check:\n" +
                    "• Service is running\n" +
                    "• Service has administrator privileges\n" +
                    "• Try restarting the service",
                    "Reset Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusBarText = $"Error during reset: {ex.Message}";
            MessageBox.Show(
                $"Error during device reset:\n\n{ex.Message}",
                "Reset Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportAuditAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CEF Files (*.cef)|*.cef|All Files (*.*)|*.*",
                FileName = $"audit-export-{DateTime.Now:yyyyMMdd}.cef"
            };

            if (dialog.ShowDialog() == true)
            {
                StatusBarText = "Exporting audit logs...";

                var data = await _apiClient.ExportAuditAsync(AuditStartDate, AuditEndDate);
                if (data != null)
                {
                    await System.IO.File.WriteAllBytesAsync(dialog.FileName, data);
                    StatusBarText = $"Audit logs exported to: {dialog.FileName}";
                    MessageBox.Show($"Audit logs exported successfully!\n\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarText = "Failed to export audit logs";
                    MessageBox.Show("Failed to export audit logs. Please check the service connection.", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            StatusBarText = $"Export error: {ex.Message}";
            MessageBox.Show($"Error exporting audit logs:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartPeriodicRefreshAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await RefreshStatusAsync();
        }
    }
}
