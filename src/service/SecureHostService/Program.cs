using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using SecureHostCore.Engine;
using SecureHostCore.Storage;
using SecureHostService.Services;
using SecureHostService.Api;

namespace SecureHostService;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();

            // Log startup
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("SecureHost Control Suite Service starting...");
            logger.LogInformation("Version: 1.0.0 | Build: {BuildDate}", DateTime.UtcNow.ToString("yyyy-MM-dd"));

            // Verify running as SYSTEM or Administrator
            if (!IsRunningAsElevated())
            {
                logger.LogCritical("Service must run with elevated privileges (SYSTEM or Administrator)");
                return 1;
            }

            logger.LogInformation("Service is running with elevated privileges");

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex}");
            return 1;
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "SecureHostService";
            })
            .ConfigureServices((hostContext, services) =>
            {
                // Configure Windows Event Log
                services.Configure<EventLogSettings>(config =>
                {
                    config.SourceName = "SecureHostService";
                    config.LogName = "Application";
                });

                // Register core services
                services.AddSingleton<PolicyEngine>();
                services.AddSingleton(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<AuditEngine>>();
                    var auditPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "SecureHost",
                        "Audit",
                        "audit.jsonl");
                    return new AuditEngine(logger, auditPath);
                });
                services.AddSingleton(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<SecureStorage>>();
                    var storagePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "SecureHost",
                        "Config",
                        "*.dat");
                    return new SecureStorage(logger, storagePath);
                });

                // Register application services
                services.AddSingleton<DriverCommunicationService>();
                services.AddSingleton<PolicyManagementService>();
                services.AddSingleton<DeviceControlService>();
                services.AddSingleton<NetworkControlService>();

                // Register API services
                services.AddSingleton<ApiServer>();

                // Register hosted service (main worker)
                services.AddHostedService<SecureHostWorker>();

                // Add logging
                services.AddLogging(builder =>
                {
                    builder.AddEventLog();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
            });

    private static bool IsRunningAsElevated()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) ||
                   identity.IsSystem;
        }
        catch
        {
            return false;
        }
    }
}
