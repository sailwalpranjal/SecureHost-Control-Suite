using Microsoft.Extensions.Logging;
using SecureHostCore.Engine;
using SecureHostCore.Models;
using System.Net;
using System.Net.NetworkInformation;

namespace SecureHostService.Services;

/// <summary>
/// Monitors and controls network connections
/// Works with WFP driver for enforcement
/// </summary>
public sealed class NetworkControlService
{
    private readonly ILogger<NetworkControlService> _logger;
    private readonly PolicyEngine _policyEngine;
    private readonly AuditEngine _auditEngine;
    private Timer? _monitorTimer;

    public NetworkControlService(
        ILogger<NetworkControlService> logger,
        PolicyEngine policyEngine,
        AuditEngine auditEngine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
        _auditEngine = auditEngine ?? throw new ArgumentNullException(nameof(auditEngine));
    }

    /// <summary>
    /// Starts network monitoring
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting network control service...");

        // Start periodic monitoring (every 10 seconds)
        _monitorTimer = new Timer(
            MonitorNetworkConnections,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10));

        _logger.LogInformation("Network monitoring started");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops network monitoring
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping network control service...");

        if (_monitorTimer != null)
        {
            await _monitorTimer.DisposeAsync();
            _monitorTimer = null;
        }

        _logger.LogInformation("Network monitoring stopped");
    }

    /// <summary>
    /// Monitors active network connections
    /// </summary>
    private void MonitorNetworkConnections(object? state)
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();

            // Monitor TCP connections
            var tcpConnections = properties.GetActiveTcpConnections();
            foreach (var conn in tcpConnections)
            {
                if (conn.State == TcpState.Established)
                {
                    _ = EvaluateConnectionAsync(conn);
                }
            }

            // Monitor TCP listeners
            var tcpListeners = properties.GetActiveTcpListeners();
            foreach (var listener in tcpListeners)
            {
                _ = EvaluateListenerAsync(listener, NetworkProtocol.TCP);
            }

            // Monitor UDP listeners
            var udpListeners = properties.GetActiveUdpListeners();
            foreach (var listener in udpListeners)
            {
                _ = EvaluateListenerAsync(listener, NetworkProtocol.UDP);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring network connections");
        }
    }

    /// <summary>
    /// Evaluates a TCP connection
    /// </summary>
    private async Task EvaluateConnectionAsync(TcpConnectionInformation connection)
    {
        try
        {
            var localPort = (ushort)connection.LocalEndPoint.Port;
            var remotePort = (ushort)connection.RemoteEndPoint.Port;
            var remoteAddress = connection.RemoteEndPoint.Address.ToString();

            // Get process info (would need P/Invoke to get actual PID from connection)
            var processId = (uint)Environment.ProcessId;
            var processName = "Unknown";

            // Evaluate policy
            var decision = _policyEngine.EvaluateNetworkConnection(
                processId,
                processName,
                NetworkProtocol.TCP,
                localPort,
                remotePort,
                remoteAddress,
                null);

            // Log if blocked or high-priority
            if (decision.Action == PolicyAction.Block || decision.AuditLevel >= AuditLevel.High)
            {
                await _auditEngine.LogNetworkEventAsync(
                    processId,
                    processName,
                    decision.Action,
                    new NetworkEventDetails
                    {
                        Protocol = NetworkProtocol.TCP,
                        LocalAddress = connection.LocalEndPoint.Address.ToString(),
                        LocalPort = localPort,
                        RemoteAddress = remoteAddress,
                        RemotePort = remotePort,
                        Direction = NetworkDirection.Outbound
                    },
                    decision.RuleId,
                    $"TCP connection {decision.Action.ToString().ToLowerInvariant()}: {remoteAddress}:{remotePort}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating TCP connection");
        }
    }

    /// <summary>
    /// Evaluates a listening port
    /// </summary>
    private async Task EvaluateListenerAsync(IPEndPoint listener, NetworkProtocol protocol)
    {
        try
        {
            var localPort = (ushort)listener.Port;
            var processId = (uint)Environment.ProcessId;
            var processName = "Unknown";

            var decision = _policyEngine.EvaluateNetworkConnection(
                processId,
                processName,
                protocol,
                localPort,
                0,
                null,
                null);

            // Log only blocks or high-priority events for listeners
            if (decision.Action == PolicyAction.Block)
            {
                await _auditEngine.LogNetworkEventAsync(
                    processId,
                    processName,
                    decision.Action,
                    new NetworkEventDetails
                    {
                        Protocol = protocol,
                        LocalAddress = listener.Address.ToString(),
                        LocalPort = localPort,
                        Direction = NetworkDirection.Inbound
                    },
                    decision.RuleId,
                    $"{protocol} listener {decision.Action.ToString().ToLowerInvariant()}: port {localPort}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating listener");
        }
    }

    /// <summary>
    /// Gets active connections summary for API
    /// </summary>
    public async Task<List<ConnectionInfo>> GetActiveConnectionsAsync()
    {
        var connections = new List<ConnectionInfo>();

        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = properties.GetActiveTcpConnections();

            foreach (var conn in tcpConnections)
            {
                connections.Add(new ConnectionInfo
                {
                    Protocol = "TCP",
                    LocalAddress = conn.LocalEndPoint.Address.ToString(),
                    LocalPort = conn.LocalEndPoint.Port,
                    RemoteAddress = conn.RemoteEndPoint.Address.ToString(),
                    RemotePort = conn.RemoteEndPoint.Port,
                    State = conn.State.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active connections");
        }

        await Task.CompletedTask;
        return connections;
    }

    /// <summary>
    /// Gets listening ports summary for API
    /// </summary>
    public async Task<List<ListenerInfo>> GetActiveListenersAsync()
    {
        var listeners = new List<ListenerInfo>();

        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();

            var tcpListeners = properties.GetActiveTcpListeners();
            foreach (var listener in tcpListeners)
            {
                listeners.Add(new ListenerInfo
                {
                    Protocol = "TCP",
                    Address = listener.Address.ToString(),
                    Port = listener.Port
                });
            }

            var udpListeners = properties.GetActiveUdpListeners();
            foreach (var listener in udpListeners)
            {
                listeners.Add(new ListenerInfo
                {
                    Protocol = "UDP",
                    Address = listener.Address.ToString(),
                    Port = listener.Port
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active listeners");
        }

        await Task.CompletedTask;
        return listeners;
    }
}

public class ConnectionInfo
{
    public string Protocol { get; set; } = string.Empty;
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string State { get; set; } = string.Empty;
}

public class ListenerInfo
{
    public string Protocol { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
}
