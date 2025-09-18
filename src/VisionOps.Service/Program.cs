using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using VisionOps.AI.Inference;
using VisionOps.Service.Events;
using VisionOps.Service.Health;
using VisionOps.Service.Monitoring;
using VisionOps.Service.Stability;
using VisionOps.Video.Processing;

namespace VisionOps.Service;

/// <summary>
/// VisionOps Windows Service entry point.
/// Phase 0 production hardening is fully integrated.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // Configure Serilog for production logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "VisionOps", "Logs", "service-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 50 * 1024 * 1024) // 50MB per file
            .WriteTo.EventLog("VisionOps", manageEventSource: true)
            .CreateLogger();

        try
        {
            Log.Information("Starting VisionOps Service v2.0 (Phase 0 Hardened)");
            Log.Information("System: {OS}, Processors: {Processors}, Memory: {Memory}GB",
                Environment.OSVersion,
                Environment.ProcessorCount,
                Environment.WorkingSet / (1024 * 1024 * 1024));

            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.Information("VisionOps Service stopped");
            await Log.CloseAndFlushAsync();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "VisionOps";
            })
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                // Phase 0 Critical Components

                // 1. Event Bus for component communication (MANDATORY)
                services.AddSingleton<IEventBus, EventBus>();

                // 2. State Management (MANDATORY for recovery)
                services.AddSingleton<IServiceStateManager, ServiceStateManager>();

                // 3. Thermal Management (MANDATORY)
                services.AddSingleton<ThermalManager>();
                services.AddHostedService(provider => provider.GetRequiredService<ThermalManager>());

                // 4. Service Stability & Watchdog (MANDATORY)
                services.AddSingleton<WatchdogService>();
                services.AddSingleton<ServiceLifecycleManager>();
                services.AddHostedService(provider => provider.GetRequiredService<ServiceLifecycleManager>());

                // 5. Health Monitoring (MANDATORY)
                services.AddSingleton<HealthCheckService>();
                services.AddSingleton<IHealthCheckService>(provider =>
                    provider.GetRequiredService<HealthCheckService>());
                services.AddHostedService(provider =>
                    provider.GetRequiredService<HealthCheckService>());

                // 6. Shared ONNX Inference (MANDATORY - prevents crashes)
                services.AddSingleton(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<SharedInferenceEngine>>();
                    return SharedInferenceEngine.GetInstance(logger);
                });

                // 7. FFmpeg Process Isolation (MANDATORY - prevents memory leaks)
                services.AddTransient<FFmpegStreamProcessor>();

                // Configuration
                services.Configure<ServiceConfiguration>(
                    hostContext.Configuration.GetSection("VisionOps"));

                // Main service worker
                services.AddHostedService<VisionOpsWorker>();

                // Health checks
                services.AddHealthChecks()
                    .AddCheck<ServiceHealthCheck>("service_health");
            });
}

/// <summary>
/// Main service worker that orchestrates all processing
/// </summary>
public class VisionOpsWorker : BackgroundService
{
    private readonly ILogger<VisionOpsWorker> _logger;
    private readonly ThermalManager _thermalManager;
    private readonly WatchdogService _watchdog;
    private readonly IEventBus _eventBus;
    private readonly IServiceStateManager _stateManager;

    public VisionOpsWorker(
        ILogger<VisionOpsWorker> logger,
        ThermalManager thermalManager,
        WatchdogService watchdog,
        IEventBus eventBus,
        IServiceStateManager stateManager)
    {
        _logger = logger;
        _thermalManager = thermalManager;
        _watchdog = watchdog;
        _eventBus = eventBus;
        _stateManager = stateManager;

        // Subscribe to critical events
        _eventBus.Subscribe<ThermalThrottleEvent>(OnThermalEvent);
        _eventBus.Subscribe<ComponentRestartEvent>(OnComponentRestart);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VisionOps Worker started");

        // Load previous state
        var checkpoint = await _stateManager.LoadLastCheckpointAsync();
        if (checkpoint != null)
        {
            _logger.LogInformation("Resuming from checkpoint: {Frames} frames processed",
                checkpoint.Processing.TotalFramesProcessed);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Pulse watchdog to indicate we're alive
                _watchdog.Pulse("VisionOpsWorker");

                // Get thermal throttle delay
                var throttleDelay = _thermalManager.GetThrottleDelay();
                if (throttleDelay > 0)
                {
                    _logger.LogDebug("Thermal throttling active: {Delay}ms delay", throttleDelay);
                    await Task.Delay(throttleDelay, stoppingToken);
                }

                // Main processing loop would go here
                // For now, just demonstrate the Phase 0 infrastructure
                await Task.Delay(1000, stoppingToken);

                // Periodically save checkpoint
                if (DateTime.UtcNow.Second % 30 == 0)
                {
                    await SaveCheckpoint();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main processing loop");
                await Task.Delay(5000, stoppingToken); // Back off on error
            }
        }

        // Save final state before stopping
        await SaveCheckpoint();
        _logger.LogInformation("VisionOps Worker stopped");
    }

    private async Task SaveCheckpoint()
    {
        try
        {
            var checkpoint = new ServiceCheckpoint
            {
                Status = new ServiceStatus
                {
                    IsRunning = true,
                    StartTime = DateTime.UtcNow.AddMinutes(-10), // Example
                    Uptime = TimeSpan.FromMinutes(10)
                },
                Processing = new ProcessingState
                {
                    IsThrottled = _thermalManager.IsThrottled,
                    ThrottleDelayMs = _thermalManager.GetThrottleDelay()
                }
            };

            await _stateManager.SaveCheckpointAsync(checkpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save checkpoint");
        }
    }

    private void OnThermalEvent(ThermalThrottleEvent e)
    {
        _logger.LogWarning("Thermal event: {Temperature}°C, Throttled: {Throttled}",
            e.Temperature, e.IsThrottled);
    }

    private void OnComponentRestart(ComponentRestartEvent e)
    {
        _logger.LogInformation("Component restart requested: {Reason}", e.Reason);
    }
}

/// <summary>
/// Health check for the service
/// </summary>
public class ServiceHealthCheck : IHealthCheck
{
    private readonly ThermalManager _thermalManager;

    public ServiceHealthCheck(ThermalManager thermalManager)
    {
        _thermalManager = thermalManager;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var metrics = await _thermalManager.GetSystemMetrics();

        if (metrics.CpuTemperature > 70)
        {
            return HealthCheckResult.Degraded(
                $"High CPU temperature: {metrics.CpuTemperature}°C");
        }

        if (metrics.MemoryUsageMB > 6000)
        {
            return HealthCheckResult.Unhealthy(
                $"Memory usage too high: {metrics.MemoryUsageMB}MB");
        }

        return HealthCheckResult.Healthy(
            $"CPU: {metrics.CpuTemperature}°C, Memory: {metrics.MemoryUsageMB}MB");
    }
}

/// <summary>
/// Service configuration
/// </summary>
public class ServiceConfiguration
{
    public int MaxCameras { get; set; } = 5;
    public int FrameIntervalSeconds { get; set; } = 3;
    public int KeyFrameIntervalSeconds { get; set; } = 10;
    public int MaxMemoryGB { get; set; } = 6;
    public int MaxCpuPercent { get; set; } = 60;
    public int ThermalThrottleTemp { get; set; } = 70;
    public string DataPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VisionOps");
}