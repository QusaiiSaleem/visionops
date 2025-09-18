using System.Diagnostics;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisionOps.Service.Events;

namespace VisionOps.Service.Stability;

/// <summary>
/// CRITICAL Phase 0 Component: Service stability and lifecycle management.
/// Implements watchdog monitoring, daily restart at 3 AM, and auto-recovery.
/// </summary>
public sealed class ServiceLifecycleManager : BackgroundService
{
    private readonly ILogger<ServiceLifecycleManager> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly WatchdogService _watchdog;
    private readonly IServiceStateManager _stateManager;
    private readonly IEventBus? _eventBus;
    private Timer? _dailyRestartTimer;
    private Timer? _healthCheckTimer;
    private DateTime _serviceStartTime;
    private bool _disposed;
    private int _restartCount = 0;

    // Stability settings
    private const int DAILY_RESTART_HOUR = 3; // 3 AM
    private const int HEALTH_CHECK_INTERVAL_SECONDS = 30;
    private const int MAX_MEMORY_GB = 6;
    private const int MAX_CONSECUTIVE_FAILURES = 3;

    private int _consecutiveFailures = 0;

    public ServiceLifecycleManager(
        ILogger<ServiceLifecycleManager> logger,
        IHostApplicationLifetime appLifetime,
        WatchdogService watchdog,
        IServiceStateManager stateManager,
        IEventBus? eventBus = null)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _watchdog = watchdog;
        _stateManager = stateManager;
        _eventBus = eventBus;
        _serviceStartTime = DateTime.UtcNow;

        // Subscribe to events
        _eventBus?.Subscribe<MemoryPressureEvent>(OnMemoryPressure);
        _eventBus?.Subscribe<ThermalThrottleEvent>(OnThermalEvent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Service lifecycle manager started");

        // Load previous checkpoint
        await LoadPreviousCheckpoint();

        // Schedule daily restart
        ScheduleDailyRestart();

        // Start health monitoring
        StartHealthMonitoring();

        // Start watchdog
        await _watchdog.StartAsync(stoppingToken);

        // Monitor for lifetime events
        _appLifetime.ApplicationStarted.Register(() => OnApplicationStarted().Wait());
        _appLifetime.ApplicationStopping.Register(() => OnApplicationStopping().Wait());
        _appLifetime.ApplicationStopped.Register(() => OnApplicationStopped().Wait());

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>
    /// Schedule daily restart at 3 AM local time
    /// </summary>
    private void ScheduleDailyRestart()
    {
        var now = DateTime.Now;
        var nextRestart = now.Date.AddHours(DAILY_RESTART_HOUR);

        // If we've already passed 3 AM today, schedule for tomorrow
        if (now.Hour >= DAILY_RESTART_HOUR)
        {
            nextRestart = nextRestart.AddDays(1);
        }

        var delay = nextRestart - now;

        _logger.LogInformation("Scheduling daily restart at {RestartTime} ({Delay} from now)",
            nextRestart, delay);

        _dailyRestartTimer = new Timer(
            DailyRestartCallback,
            null,
            delay,
            TimeSpan.FromDays(1));
    }

    /// <summary>
    /// Perform daily restart
    /// </summary>
    private async void DailyRestartCallback(object? state)
    {
        _logger.LogInformation("Initiating scheduled daily restart at 3 AM");

        // Save current state
        await SaveServiceState();

        // Graceful shutdown
        await GracefulRestart("Scheduled daily restart");
    }

    /// <summary>
    /// Start health monitoring
    /// </summary>
    private void StartHealthMonitoring()
    {
        _healthCheckTimer = new Timer(
            HealthCheckCallback,
            null,
            TimeSpan.FromSeconds(HEALTH_CHECK_INTERVAL_SECONDS),
            TimeSpan.FromSeconds(HEALTH_CHECK_INTERVAL_SECONDS));
    }

    /// <summary>
    /// Perform health check
    /// </summary>
    private async void HealthCheckCallback(object? state)
    {
        try
        {
            var health = await CheckServiceHealth();

            if (!health.IsHealthy)
            {
                _consecutiveFailures++;
                _logger.LogWarning("Health check failed ({Failures}/{Max}): {Reason}",
                    _consecutiveFailures, MAX_CONSECUTIVE_FAILURES, health.Reason);

                if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    _logger.LogError("Maximum consecutive failures reached. Initiating auto-recovery");
                    await InitiateAutoRecovery(health.Reason);
                }
            }
            else
            {
                // Reset failure counter on successful health check
                if (_consecutiveFailures > 0)
                {
                    _logger.LogInformation("Service recovered after {Failures} failures",
                        _consecutiveFailures);
                    _consecutiveFailures = 0;
                }

                // Pulse the watchdog
                _watchdog.Pulse();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
            _consecutiveFailures++;
        }
    }

    /// <summary>
    /// Check service health
    /// </summary>
    private async Task<HealthCheckResult> CheckServiceHealth()
    {
        var result = new HealthCheckResult { IsHealthy = true };

        await Task.Run(() =>
        {
            // Check memory usage
            var process = Process.GetCurrentProcess();
            var memoryGB = process.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);

            if (memoryGB > MAX_MEMORY_GB)
            {
                result.IsHealthy = false;
                result.Reason = $"Memory usage too high: {memoryGB:F2}GB";
                return;
            }

            // Check if process is responding
            if (!process.Responding)
            {
                result.IsHealthy = false;
                result.Reason = "Process not responding";
                return;
            }

            // Check thread count (potential thread leak)
            if (process.Threads.Count > 200)
            {
                result.IsHealthy = false;
                result.Reason = $"Too many threads: {process.Threads.Count}";
                return;
            }

            // Check handle count (potential handle leak)
            if (process.HandleCount > 10000)
            {
                result.IsHealthy = false;
                result.Reason = $"Too many handles: {process.HandleCount}";
                return;
            }

            // Check uptime (restart if running for too long)
            var uptime = DateTime.UtcNow - _serviceStartTime;
            if (uptime.TotalHours > 48) // 2 days without restart
            {
                result.IsHealthy = false;
                result.Reason = $"Service running too long: {uptime.TotalHours:F1} hours";
                return;
            }
        });

        return result;
    }

    /// <summary>
    /// Initiate auto-recovery
    /// </summary>
    private async Task InitiateAutoRecovery(string reason)
    {
        _logger.LogWarning("Auto-recovery initiated: {Reason}", reason);

        // Try to save state
        try
        {
            await SaveServiceState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state during auto-recovery");
        }

        // Check if we should restart or just clean up
        var memoryGB = GC.GetTotalMemory(false) / (1024.0 * 1024.0 * 1024.0);
        if (memoryGB > MAX_MEMORY_GB * 0.8)
        {
            // Memory issue - try aggressive cleanup first
            _logger.LogInformation("Attempting memory recovery");
            await MemoryRecovery();

            // Re-check health
            var health = await CheckServiceHealth();
            if (health.IsHealthy)
            {
                _logger.LogInformation("Memory recovery successful");
                _consecutiveFailures = 0;
                return;
            }
        }

        // Recovery failed, restart service
        await GracefulRestart($"Auto-recovery: {reason}");
    }

    /// <summary>
    /// Attempt to recover memory
    /// </summary>
    private async Task MemoryRecovery()
    {
        _logger.LogInformation("Performing aggressive memory cleanup");

        // Force full GC
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        // Trim working set
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle,
                    (IntPtr)(-1), (IntPtr)(-1));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trim working set");
        }

        await Task.Delay(1000); // Give time for cleanup
    }

    /// <summary>
    /// Perform graceful restart
    /// </summary>
    private async Task GracefulRestart(string reason)
    {
        _logger.LogInformation("Graceful restart initiated: {Reason}", reason);

        // Stop accepting new work
        _appLifetime.StopApplication();

        // Wait for graceful shutdown
        await Task.Delay(5000);

        // If running as Windows Service, restart it
        if (OperatingSystem.IsWindows())
        {
            RestartWindowsService();
        }
        else
        {
            // For non-service scenarios, just exit and let supervisor restart
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Restart Windows Service
    /// </summary>
    private void RestartWindowsService()
    {
        try
        {
            using var sc = new ServiceController("VisionOps");
            if (sc.Status == ServiceControllerStatus.Running)
            {
                _logger.LogInformation("Restarting Windows Service");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                sc.Start();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart Windows Service, exiting process");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Save service state for recovery
    /// </summary>
    private async Task SaveServiceState()
    {
        try
        {
            var checkpoint = new ServiceCheckpoint
            {
                Status = new ServiceStatus
                {
                    IsRunning = true,
                    StartTime = _serviceStartTime,
                    Uptime = DateTime.UtcNow - _serviceStartTime,
                    RestartCount = _restartCount,
                    LastError = string.Empty
                },
                Processing = new ProcessingState
                {
                    TotalFramesProcessed = 0, // Would get from actual service
                    IsThrottled = false
                }
            };

            // Add memory and process info
            var process = Process.GetCurrentProcess();
            checkpoint.CustomData["MemoryMB"] = process.WorkingSet64 / (1024 * 1024);
            checkpoint.CustomData["ThreadCount"] = process.Threads.Count;
            checkpoint.CustomData["HandleCount"] = process.HandleCount;
            checkpoint.CustomData["ConsecutiveFailures"] = _consecutiveFailures;

            await _stateManager.SaveCheckpointAsync(checkpoint);
            _logger.LogInformation("Service checkpoint saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save service state");
        }
    }

    /// <summary>
    /// Load previous checkpoint on startup
    /// </summary>
    private async Task LoadPreviousCheckpoint()
    {
        try
        {
            var checkpoint = await _stateManager.LoadLastCheckpointAsync();
            if (checkpoint != null)
            {
                _restartCount = checkpoint.Status.RestartCount + 1;
                _logger.LogInformation("Loaded previous checkpoint. Restart count: {Count}, Previous uptime: {Uptime}",
                    _restartCount, checkpoint.Status.Uptime);

                // Check if this is a crash recovery
                if (checkpoint.Status.Uptime < TimeSpan.FromMinutes(5))
                {
                    _logger.LogWarning("Previous session ended prematurely after {Uptime}. Possible crash.",
                        checkpoint.Status.Uptime);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load previous checkpoint");
        }
    }

    private async Task OnApplicationStarted()
    {
        _logger.LogInformation("Application started successfully");
        _serviceStartTime = DateTime.UtcNow;

        // Save initial state
        await SaveServiceState();

        // Publish startup event
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new ServiceStartedEvent
            {
                RestartCount = _restartCount,
                PreviousUptime = TimeSpan.Zero
            });
        }
    }

    private async Task OnApplicationStopping()
    {
        _logger.LogInformation("Application stopping - saving final state");

        // Save final state
        await SaveServiceState();

        // Stop timers
        _dailyRestartTimer?.Change(Timeout.Infinite, 0);
        _healthCheckTimer?.Change(Timeout.Infinite, 0);
    }

    private async Task OnApplicationStopped()
    {
        _logger.LogInformation("Application stopped");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handle memory pressure events
    /// </summary>
    private void OnMemoryPressure(MemoryPressureEvent memoryEvent)
    {
        if (memoryEvent.Level == MemoryPressureLevel.Critical)
        {
            _logger.LogWarning("Critical memory pressure: {Memory}MB. Initiating memory recovery.",
                memoryEvent.MemoryUsageMB);
            Task.Run(() => MemoryRecovery());
        }
    }

    /// <summary>
    /// Handle thermal events
    /// </summary>
    private void OnThermalEvent(ThermalThrottleEvent thermalEvent)
    {
        if (thermalEvent.Temperature >= 75)
        {
            _logger.LogCritical("Critical temperature: {Temp}°C. Service may shutdown.",
                thermalEvent.Temperature);
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;

        // Unsubscribe from events
        if (_eventBus != null)
        {
            _eventBus.Unsubscribe<MemoryPressureEvent>(OnMemoryPressure);
            _eventBus.Unsubscribe<ThermalThrottleEvent>(OnThermalEvent);
        }

        _dailyRestartTimer?.Dispose();
        _healthCheckTimer?.Dispose();
        _watchdog?.Dispose();
        _stateManager?.Dispose();

        _disposed = true;
        base.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    private class HealthCheckResult
    {
        public bool IsHealthy { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}