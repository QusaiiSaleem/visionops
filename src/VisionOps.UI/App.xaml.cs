using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using VisionOps.UI.Services;

namespace VisionOps.UI;

/// <summary>
/// VisionOps UI Application with dependency injection and auto-updates.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services => _host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VisionOps", "UI", "Logs", "ui-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            Log.Information("Starting VisionOps UI v1.0.0");

            // Build host with dependency injection
            _host = Host.CreateDefaultBuilder(e.Args)
                .UseSerilog()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
                    config.AddEnvironmentVariables("VISIONOPS_");
                })
                .ConfigureServices((context, services) =>
                {
                    // Register services
                    services.AddSingleton<MainWindow>();
                    services.AddHostedService<AutoUpdateService>();
                    services.AddSingleton<AutoUpdateService>();

                    // Register configuration
                    services.AddSingleton<IConfiguration>(context.Configuration);
                })
                .Build();

            // Start the host
            _host.Start();

            // Show main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            MessageBox.Show(
                $"Failed to start VisionOps UI: {ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("Shutting down VisionOps UI");

            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }

            Log.CloseAndFlush();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error during shutdown");
        }

        base.OnExit(e);
    }
}