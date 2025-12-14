using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SecureHostCore.Engine;
using SecureHostCore.Models;
using SecureHostService.Services;

namespace SecureHostService.Api;

/// <summary>
/// REST API server for SecureHost management
/// Listens on localhost:5555 for local management
/// </summary>
public sealed class ApiServer
{
    private readonly ILogger<ApiServer> _logger;
    private readonly PolicyEngine _policyEngine;
    private readonly AuditEngine _auditEngine;
    private readonly PolicyManagementService _policyManagement;
    private readonly NetworkControlService _networkControl;
    private DeviceControlService? _deviceControl;
    private IWebHost? _webHost;

    public string ListeningUrl { get; private set; } = "http://localhost:5555";

    public ApiServer(
        ILogger<ApiServer> logger,
        PolicyEngine policyEngine,
        AuditEngine auditEngine,
        PolicyManagementService policyManagement,
        NetworkControlService networkControl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
        _auditEngine = auditEngine ?? throw new ArgumentNullException(nameof(auditEngine));
        _policyManagement = policyManagement ?? throw new ArgumentNullException(nameof(policyManagement));
        _networkControl = networkControl ?? throw new ArgumentNullException(nameof(networkControl));
    }

    /// <summary>
    /// Sets the device control service for reset functionality
    /// </summary>
    public void SetDeviceControlService(DeviceControlService deviceControl)
    {
        _deviceControl = deviceControl;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting API server on {Url}", ListeningUrl);

        _webHost = new WebHostBuilder()
            .UseKestrel(options =>
            {
                options.ListenLocalhost(5555);
            })
            .ConfigureServices(services =>
            {
                services.AddControllers();
                services.AddSingleton(_policyEngine);
                services.AddSingleton(_auditEngine);
                services.AddSingleton(_policyManagement);
                services.AddSingleton(_networkControl);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    ConfigureEndpoints(endpoints);
                });
            })
            .Build();

        await _webHost.StartAsync(cancellationToken);
        _logger.LogInformation("API server started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping API server");

        if (_webHost != null)
        {
            await _webHost.StopAsync(cancellationToken);
            _webHost.Dispose();
            _webHost = null;
        }
    }

    private void ConfigureEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
    {
        // Health check
        endpoints.MapGet("/api/health", async (HttpContext context) =>
        {
            await context.Response.WriteAsJsonAsync(new { status = "healthy", version = "1.0.0" });
        });

        // Get all rules
        endpoints.MapGet("/api/rules", async (HttpContext context, PolicyEngine engine) =>
        {
            var rules = engine.GetAllRules();
            await context.Response.WriteAsJsonAsync(rules);
        });

        // Get specific rule
        endpoints.MapGet("/api/rules/{id}", async (HttpContext context, PolicyEngine engine) =>
        {
            if (!ulong.TryParse(context.Request.RouteValues["id"]?.ToString(), out var ruleId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid rule ID" });
                return;
            }

            var rule = engine.GetRule(ruleId);
            if (rule == null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "Rule not found" });
                return;
            }

            await context.Response.WriteAsJsonAsync(rule);
        });

        // Add rule
        endpoints.MapPost("/api/rules", async (HttpContext context, PolicyManagementService policyMgmt) =>
        {
            var rule = await context.Request.ReadFromJsonAsync<PolicyRule>();
            if (rule == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid rule data" });
                return;
            }

            var ruleId = await policyMgmt.AddRuleAsync(rule, CancellationToken.None);
            await context.Response.WriteAsJsonAsync(new { id = ruleId, message = "Rule added successfully" });
        });

        // Update rule
        endpoints.MapPut("/api/rules/{id}", async (HttpContext context, PolicyManagementService policyMgmt) =>
        {
            if (!ulong.TryParse(context.Request.RouteValues["id"]?.ToString(), out var ruleId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid rule ID" });
                return;
            }

            var rule = await context.Request.ReadFromJsonAsync<PolicyRule>();
            if (rule == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid rule data" });
                return;
            }

            var success = await policyMgmt.UpdateRuleAsync(ruleId, rule, CancellationToken.None);
            if (!success)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "Rule not found" });
                return;
            }

            await context.Response.WriteAsJsonAsync(new { message = "Rule updated successfully" });
        });

        // Delete rule
        endpoints.MapDelete("/api/rules/{id}", async (HttpContext context, PolicyManagementService policyMgmt) =>
        {
            if (!ulong.TryParse(context.Request.RouteValues["id"]?.ToString(), out var ruleId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid rule ID" });
                return;
            }

            var success = await policyMgmt.RemoveRuleAsync(ruleId, CancellationToken.None);
            if (!success)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "Rule not found" });
                return;
            }

            await context.Response.WriteAsJsonAsync(new { message = "Rule deleted successfully" });
        });

        // Toggle rule enabled/disabled
        endpoints.MapPost("/api/rules/{id}/toggle", async (HttpContext context, PolicyEngine engine, PolicyManagementService policyMgmt) =>
        {
            if (!ulong.TryParse(context.Request.RouteValues["id"]?.ToString(), out var ruleId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid rule ID" });
                return;
            }

            var rule = engine.GetRule(ruleId);
            if (rule == null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "Rule not found" });
                return;
            }

            // Toggle the enabled state
            rule.Enabled = !rule.Enabled;
            var success = await policyMgmt.UpdateRuleAsync(ruleId, rule, CancellationToken.None);

            if (success)
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    message = $"Rule {(rule.Enabled ? "enabled" : "disabled")} successfully",
                    enabled = rule.Enabled
                });
            }
            else
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = "Failed to update rule" });
            }
        });

        // Get active connections
        endpoints.MapGet("/api/network/connections", async (HttpContext context, NetworkControlService networkCtrl) =>
        {
            var connections = await networkCtrl.GetActiveConnectionsAsync();
            await context.Response.WriteAsJsonAsync(connections);
        });

        // Get active listeners
        endpoints.MapGet("/api/network/listeners", async (HttpContext context, NetworkControlService networkCtrl) =>
        {
            var listeners = await networkCtrl.GetActiveListenersAsync();
            await context.Response.WriteAsJsonAsync(listeners);
        });

        // Export audit events
        endpoints.MapGet("/api/audit/export", async (HttpContext context, AuditEngine auditEngine) =>
        {
            var startTimeStr = context.Request.Query["startTime"].ToString();
            var endTimeStr = context.Request.Query["endTime"].ToString();

            if (!DateTime.TryParse(startTimeStr, out var startTime))
                startTime = DateTime.UtcNow.AddDays(-7);

            if (!DateTime.TryParse(endTimeStr, out var endTime))
                endTime = DateTime.UtcNow;

            var tempFile = Path.GetTempFileName();
            var count = await auditEngine.ExportToSiemAsync(startTime, endTime, tempFile);

            context.Response.ContentType = "text/plain";
            context.Response.Headers.Add("Content-Disposition", "attachment; filename=audit-export.cef");

            await using var fileStream = File.OpenRead(tempFile);
            await fileStream.CopyToAsync(context.Response.Body);

            File.Delete(tempFile);
        });

        // System status
        endpoints.MapGet("/api/status", async (HttpContext context, PolicyEngine engine) =>
        {
            var rules = engine.GetAllRules();
            var status = new
            {
                service = "running",
                version = "1.0.0",
                rulesCount = rules.Count,
                activeRules = rules.Count(r => r.Enabled),
                machineId = Environment.MachineName,
                uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString()
            };

            await context.Response.WriteAsJsonAsync(status);
        });

        // EMERGENCY RESET: Re-enable all devices
        endpoints.MapPost("/api/system/reset", async (HttpContext context) =>
        {
            if (_deviceControl == null)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new { error = "Device control service not available" });
                return;
            }

            _logger.LogWarning("EMERGENCY RESET triggered via API");

            try
            {
                await _deviceControl.ResetAllDevicesAsync();
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "All devices have been reset and re-enabled",
                    warning = "This is an emergency reset. All blocking rules are still active and will re-apply on service restart."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during device reset");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = $"Reset failed: {ex.Message}" });
            }
        });
    }
}
