using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureHostCore.Engine;
using SecureHostService.Services;
using SecureHostService.Api;

namespace SecureHostService;

/// <summary>
/// Main worker service for SecureHost Control Suite
/// Coordinates policy enforcement, driver communication, and API server
/// </summary>
public sealed class SecureHostWorker : BackgroundService
{
    private readonly ILogger<SecureHostWorker> _logger;
    private readonly PolicyEngine _policyEngine;
    private readonly AuditEngine _auditEngine;
    private readonly DriverCommunicationService _driverComm;
    private readonly PolicyManagementService _policyManagement;
    private readonly DeviceControlService _deviceControl;
    private readonly NetworkControlService _networkControl;
    private readonly ApiServer _apiServer;
    private readonly IHostApplicationLifetime _lifetime;

    public SecureHostWorker(
        ILogger<SecureHostWorker> logger,
        PolicyEngine policyEngine,
        AuditEngine auditEngine,
        DriverCommunicationService driverComm,
        PolicyManagementService policyManagement,
        DeviceControlService deviceControl,
        NetworkControlService networkControl,
        ApiServer apiServer,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
        _auditEngine = auditEngine ?? throw new ArgumentNullException(nameof(auditEngine));
        _driverComm = driverComm ?? throw new ArgumentNullException(nameof(driverComm));
        _policyManagement = policyManagement ?? throw new ArgumentNullException(nameof(policyManagement));
        _deviceControl = deviceControl ?? throw new ArgumentNullException(nameof(deviceControl));
        _networkControl = networkControl ?? throw new ArgumentNullException(nameof(networkControl));
        _apiServer = apiServer ?? throw new ArgumentNullException(nameof(apiServer));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("SecureHost Worker Service starting...");

            // Initialize drivers
            _logger.LogInformation("Initializing kernel drivers...");
            var driversInitialized = await _driverComm.InitializeAsync(stoppingToken);
            if (!driversInitialized)
            {
                _logger.LogWarning("Kernel drivers not available - running in user-mode only");
                await _auditEngine.LogPolicyChangeAsync(
                    "ServiceStart",
                    null,
                    "Service started in user-mode only (drivers not available)");
            }
            else
            {
                _logger.LogInformation("Kernel drivers initialized successfully");
                await _auditEngine.LogPolicyChangeAsync(
                    "ServiceStart",
                    null,
                    "Service started with kernel-mode enforcement");
            }

            // Start device control
            _logger.LogInformation("Starting device control service...");
            await _deviceControl.StartAsync(stoppingToken);

            // Link policy management to device control for enforcement
            _policyManagement.SetDeviceControlService(_deviceControl);

            // Link API server to device control for reset functionality
            _apiServer.SetDeviceControlService(_deviceControl);

            // Load policies (will trigger enforcement)
            _logger.LogInformation("Loading policy rules...");
            await _policyManagement.LoadPoliciesAsync(stoppingToken);
            var ruleCount = _policyEngine.GetAllRules().Count;
            _logger.LogInformation("Loaded {RuleCount} policy rules", ruleCount);

            // Start network control
            _logger.LogInformation("Starting network control service...");
            await _networkControl.StartAsync(stoppingToken);

            // Start API server
            _logger.LogInformation("Starting API server...");
            await _apiServer.StartAsync(stoppingToken);
            _logger.LogInformation("API server listening on: {ApiUrl}", _apiServer.ListeningUrl);

            _logger.LogInformation("SecureHost Worker Service is running");

            // Main service loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Health check and monitoring
                    await PerformHealthCheckAsync(stoppingToken);

                    // Wait 30 seconds before next check
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in service main loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("SecureHost Worker Service stopping...");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service shutdown requested");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in SecureHost Worker Service");
            _lifetime.StopApplication();
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SecureHost Worker Service shutdown initiated");

        try
        {
            // Stop API server
            await _apiServer.StopAsync(cancellationToken);

            // Stop network control
            await _networkControl.StopAsync(cancellationToken);

            // Stop device control
            await _deviceControl.StopAsync(cancellationToken);

            // Save policies
            await _policyManagement.SavePoliciesAsync(cancellationToken);

            // Shutdown drivers
            await _driverComm.ShutdownAsync(cancellationToken);

            // Log shutdown
            await _auditEngine.LogPolicyChangeAsync(
                "ServiceStop",
                null,
                "Service stopped gracefully");

            _logger.LogInformation("SecureHost Worker Service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service shutdown");
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Performs health checks and monitoring
    /// </summary>
    private async Task PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check driver health
            var driverHealth = await _driverComm.CheckHealthAsync(cancellationToken);
            // NOTE: Don't log tamper alerts for missing drivers - they may simply not be installed
            // Only log once during startup, not every health check

            // Check for tampering
            var tamperCheck = await DetectTamperingAsync(cancellationToken);
            if (tamperCheck)
            {
                _logger.LogCritical("TAMPERING DETECTED - System integrity compromised");
                await _auditEngine.LogTamperAttemptAsync(
                    (uint)Environment.ProcessId,
                    "SecureHostService",
                    "Service tampering detected during health check");
            }

            // Log statistics
            var stats = _policyEngine.GetAllRules();
            _logger.LogDebug("Health Check: {ActiveRules} active rules, Drivers: {DriverStatus}",
                stats.Count(r => r.Enabled),
                driverHealth ? "OK" : "Degraded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
        }
    }

    /// <summary>
    /// Detects tampering attempts
    /// </summary>
    private Task<bool> DetectTamperingAsync(CancellationToken cancellationToken)
    {
        // In production:
        // - Verify driver signatures
        // - Check service binary integrity
        // - Verify configuration file hashes
        // - Monitor for debugger attachment
        // - Check for known rootkit techniques

        return Task.FromResult(false);
    }
}
