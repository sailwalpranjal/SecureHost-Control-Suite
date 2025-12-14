using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using SecureHostCore.Models;

namespace SecureHostCLI;

class Program
{
    private static readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://localhost:5555")
    };

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SecureHost Control Suite - Command Line Interface");

        // Status command
        var statusCommand = new Command("status", "Display service status");
        statusCommand.SetHandler(HandleStatusAsync);
        rootCommand.AddCommand(statusCommand);

        // Rules commands
        var rulesCommand = new Command("rules", "Manage policy rules");

        var listRulesCommand = new Command("list", "List all policy rules");
        listRulesCommand.SetHandler(HandleListRulesAsync);
        rulesCommand.AddCommand(listRulesCommand);

        var addRuleCommand = new Command("add", "Add a new policy rule");
        var nameOption = new Option<string>("--name", "Rule name") { IsRequired = true };
        var typeOption = new Option<string>("--type", "Rule type (Network, Device, Application)") { IsRequired = true };
        var actionOption = new Option<string>("--action", "Action (Allow, Block, Audit)") { IsRequired = true };
        var targetOption = new Option<string?>("--target", "Target (process name, device ID, etc.)");

        addRuleCommand.AddOption(nameOption);
        addRuleCommand.AddOption(typeOption);
        addRuleCommand.AddOption(actionOption);
        addRuleCommand.AddOption(targetOption);
        addRuleCommand.SetHandler(HandleAddRuleAsync, nameOption, typeOption, actionOption, targetOption);
        rulesCommand.AddCommand(addRuleCommand);

        var deleteRuleCommand = new Command("delete", "Delete a policy rule");
        var ruleIdOption = new Option<ulong>("--id", "Rule ID") { IsRequired = true };
        deleteRuleCommand.AddOption(ruleIdOption);
        deleteRuleCommand.SetHandler(HandleDeleteRuleAsync, ruleIdOption);
        rulesCommand.AddCommand(deleteRuleCommand);

        var toggleRuleCommand = new Command("toggle", "Toggle a policy rule (enable/disable)");
        var toggleRuleIdOption = new Option<ulong>("--id", "Rule ID") { IsRequired = true };
        toggleRuleCommand.AddOption(toggleRuleIdOption);
        toggleRuleCommand.SetHandler(HandleToggleRuleAsync, toggleRuleIdOption);
        rulesCommand.AddCommand(toggleRuleCommand);

        rootCommand.AddCommand(rulesCommand);

        // Network commands
        var networkCommand = new Command("network", "Network monitoring");

        var connectionsCommand = new Command("connections", "Show active network connections");
        connectionsCommand.SetHandler(HandleConnectionsAsync);
        networkCommand.AddCommand(connectionsCommand);

        var listenersCommand = new Command("listeners", "Show active listeners");
        listenersCommand.SetHandler(HandleListenersAsync);
        networkCommand.AddCommand(listenersCommand);

        rootCommand.AddCommand(networkCommand);

        // Audit commands
        var auditCommand = new Command("audit", "Audit log management");

        var exportCommand = new Command("export", "Export audit logs");
        var outputOption = new Option<string>("--output", "Output file path") { IsRequired = true };
        var startTimeOption = new Option<DateTime?>("--start", "Start time (UTC)");
        var endTimeOption = new Option<DateTime?>("--end", "End time (UTC)");

        exportCommand.AddOption(outputOption);
        exportCommand.AddOption(startTimeOption);
        exportCommand.AddOption(endTimeOption);
        exportCommand.SetHandler(HandleExportAuditAsync, outputOption, startTimeOption, endTimeOption);
        auditCommand.AddCommand(exportCommand);

        rootCommand.AddCommand(auditCommand);

        // System commands
        var systemCommand = new Command("system", "System management");

        var resetCommand = new Command("reset", "⚠️ EMERGENCY: Reset all devices (re-enable everything)");
        var forceOption = new Option<bool>("--force", "Skip confirmation prompt");
        resetCommand.AddOption(forceOption);
        resetCommand.SetHandler(HandleResetAsync, forceOption);
        systemCommand.AddCommand(resetCommand);

        rootCommand.AddCommand(systemCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task HandleStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/status");
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to get service status[/]");
                return;
            }

            var status = await response.Content.ReadFromJsonAsync<JsonElement>();

            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");

            table.AddRow("Service", status.GetProperty("service").GetString() ?? "Unknown");
            table.AddRow("Version", status.GetProperty("version").GetString() ?? "Unknown");
            table.AddRow("Machine", status.GetProperty("machineId").GetString() ?? "Unknown");
            table.AddRow("Uptime", status.GetProperty("uptime").GetString() ?? "Unknown");
            table.AddRow("Total Rules", status.GetProperty("rulesCount").GetInt32().ToString());
            table.AddRow("Active Rules", status.GetProperty("activeRules").GetInt32().ToString());

            AnsiConsole.Write(table);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Unable to connect to service - {ex.Message}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleListRulesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/rules");
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to get rules[/]");
                return;
            }

            var rules = await response.Content.ReadFromJsonAsync<List<PolicyRule>>();
            if (rules == null || rules.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No policy rules found[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("ID");
            table.AddColumn("Name");
            table.AddColumn("Type");
            table.AddColumn("Action");
            table.AddColumn("Enabled");
            table.AddColumn("Priority");

            foreach (var rule in rules)
            {
                table.AddRow(
                    rule.Id.ToString(),
                    rule.Name,
                    rule.Type.ToString(),
                    rule.Action.ToString(),
                    rule.Enabled ? "[green]Yes[/]" : "[red]No[/]",
                    rule.Priority.ToString()
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[green]Total: {rules.Count} rules[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleAddRuleAsync(string name, string type, string action, string? target)
    {
        try
        {
            if (!Enum.TryParse<PolicyRuleType>(type, ignoreCase: true, out var ruleType))
            {
                AnsiConsole.MarkupLine("[red]Error: Invalid rule type. Use: Network, Device, or Application[/]");
                return;
            }

            if (!Enum.TryParse<PolicyAction>(action, ignoreCase: true, out var policyAction))
            {
                AnsiConsole.MarkupLine("[red]Error: Invalid action. Use: Allow, Block, or Audit[/]");
                return;
            }

            var rule = new PolicyRule
            {
                Name = name,
                Type = ruleType,
                Action = policyAction,
                Target = target ?? string.Empty,
                Enabled = true,
                Priority = 100
            };

            var response = await _httpClient.PostAsJsonAsync("/api/rules", rule);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to add rule[/]");
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var ruleId = result.GetProperty("id").GetUInt64();

            AnsiConsole.MarkupLine($"[green]Rule added successfully! ID: {ruleId}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleDeleteRuleAsync(ulong ruleId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/rules/{ruleId}");
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    AnsiConsole.MarkupLine($"[red]Error: Rule {ruleId} not found[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Error: Failed to delete rule[/]");
                }
                return;
            }

            AnsiConsole.MarkupLine($"[green]Rule {ruleId} deleted successfully![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleConnectionsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/network/connections");
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to get connections[/]");
                return;
            }

            var connections = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
            if (connections == null || connections.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No active connections[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("Protocol");
            table.AddColumn("Local Address");
            table.AddColumn("Remote Address");
            table.AddColumn("State");

            foreach (var conn in connections)
            {
                table.AddRow(
                    conn.GetProperty("protocol").GetString() ?? "",
                    $"{conn.GetProperty("localAddress").GetString()}:{conn.GetProperty("localPort").GetInt32()}",
                    $"{conn.GetProperty("remoteAddress").GetString()}:{conn.GetProperty("remotePort").GetInt32()}",
                    conn.GetProperty("state").GetString() ?? ""
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[green]Total: {connections.Count} connections[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleListenersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/network/listeners");
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to get listeners[/]");
                return;
            }

            var listeners = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
            if (listeners == null || listeners.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No active listeners[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("Protocol");
            table.AddColumn("Address");
            table.AddColumn("Port");

            foreach (var listener in listeners)
            {
                table.AddRow(
                    listener.GetProperty("protocol").GetString() ?? "",
                    listener.GetProperty("address").GetString() ?? "",
                    listener.GetProperty("port").GetInt32().ToString()
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[green]Total: {listeners.Count} listeners[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleExportAuditAsync(string output, DateTime? startTime, DateTime? endTime)
    {
        try
        {
            var start = startTime ?? DateTime.UtcNow.AddDays(-7);
            var end = endTime ?? DateTime.UtcNow;

            var url = $"/api/audit/export?startTime={start:O}&endTime={end:O}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to export audit logs[/]");
                return;
            }

            var content = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(output);
            await content.CopyToAsync(fileStream);

            AnsiConsole.MarkupLine($"[green]Audit logs exported to: {output}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleToggleRuleAsync(ulong ruleId)
    {
        try
        {
            // First get the rule to show current state
            var getResponse = await _httpClient.GetAsync($"/api/rules/{ruleId}");
            if (!getResponse.IsSuccessStatusCode)
            {
                if (getResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    AnsiConsole.MarkupLine($"[red]Error: Rule {ruleId} not found[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Error: Failed to get rule information[/]");
                }
                return;
            }

            var rule = await getResponse.Content.ReadFromJsonAsync<PolicyRule>();
            if (rule == null)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to parse rule data[/]");
                return;
            }

            var action = rule.Enabled ? "disable" : "enable";

            AnsiConsole.MarkupLine($"[yellow]⚠️  WARNING: This will {action} the rule '{rule.Name}'[/]");
            AnsiConsole.MarkupLine($"[yellow]Type: {rule.Type} | Action: {rule.Action} | Currently: {(rule.Enabled ? "Enabled" : "Disabled")}[/]");
            AnsiConsole.MarkupLine($"[yellow]This change will apply IMMEDIATELY![/]");

            if (!AnsiConsole.Confirm($"\nAre you sure you want to {action} this rule?", false))
            {
                AnsiConsole.MarkupLine("[yellow]Operation cancelled[/]");
                return;
            }

            var response = await _httpClient.PostAsync($"/api/rules/{ruleId}/toggle", null);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to toggle rule[/]");
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var newState = result.GetProperty("enabled").GetBoolean();

            AnsiConsole.MarkupLine($"[green]✓ Rule {ruleId} {(newState ? "enabled" : "disabled")} successfully![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    static async Task HandleResetAsync(bool force)
    {
        try
        {
            if (!force)
            {
                AnsiConsole.MarkupLine("[red bold]⚠️  EMERGENCY DEVICE RESET ⚠️[/]");
                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("[yellow]This will IMMEDIATELY re-enable ALL devices on your computer![/]");
                AnsiConsole.MarkupLine("[yellow]Use this if you've accidentally blocked critical devices.[/]");
                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("[red]WARNING:[/]");
                AnsiConsole.MarkupLine("  • All cameras, microphones, and other devices will be ENABLED");
                AnsiConsole.MarkupLine("  • Blocking rules will still exist and will re-apply on service restart");
                AnsiConsole.MarkupLine("  • This action cannot be undone");
                AnsiConsole.MarkupLine("");

                if (!AnsiConsole.Confirm("[red bold]Are you ABSOLUTELY SURE you want to reset all devices?[/]", false))
                {
                    AnsiConsole.MarkupLine("[yellow]Reset cancelled - No changes made[/]");
                    return;
                }

                AnsiConsole.MarkupLine("");
                if (!AnsiConsole.Confirm("[red]Final confirmation - Proceed with reset?[/]", false))
                {
                    AnsiConsole.MarkupLine("[yellow]Reset cancelled - No changes made[/]");
                    return;
                }
            }

            AnsiConsole.MarkupLine("");
            AnsiConsole.Status()
                .Start("Resetting all devices...", ctx =>
                {
                    ctx.Spinner(Spinner.Known.Dots);
                    ctx.SpinnerStyle(Style.Parse("yellow"));
                    Thread.Sleep(500); // Brief delay for effect
                });

            var response = await _httpClient.PostAsync("/api/system/reset", null);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to reset devices[/]");
                AnsiConsole.MarkupLine("[yellow]Please check:[/]");
                AnsiConsole.MarkupLine("  • Service is running");
                AnsiConsole.MarkupLine("  • Service has administrator privileges");
                AnsiConsole.MarkupLine("  • Try restarting the service");
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var message = result.GetProperty("message").GetString();

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[green bold]✓ RESET COMPLETE[/]");
            AnsiConsole.MarkupLine($"[green]{message}[/]");
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[yellow]IMPORTANT:[/]");
            AnsiConsole.MarkupLine("  • Your blocking rules are still active");
            AnsiConsole.MarkupLine("  • To permanently enable devices, disable or delete the blocking rules");
            AnsiConsole.MarkupLine("  • If you restart the service, blocked devices will be disabled again");
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[cyan]To disable all blocking rules:[/]");
            AnsiConsole.MarkupLine("  SecureHostCLI rules list          [dim]# List all rules and their IDs[/]");
            AnsiConsole.MarkupLine("  SecureHostCLI rules toggle --id X [dim]# Toggle each blocking rule[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }
}
