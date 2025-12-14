using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SecureHostGUI.Services;
using SecureHostGUI.ViewModels;

namespace SecureHostGUI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start application:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "SecureHost GUI - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ConfigureServices(ServiceCollection services)
    {
        // Register services
        services.AddSingleton<ApiClient>();

        // Register view models
        services.AddSingleton<MainViewModel>();

        // Register windows
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
