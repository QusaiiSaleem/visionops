using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisionOps.Core.Models;
using VisionOps.Service.Events;
using VisionOps.Service.Monitoring;
using VisionOps.Service.Stability;

namespace VisionOps.Service.Health;

/// <summary>
/// Comprehensive health check service for monitoring system status
/// </summary>
public interface IHealthCheckService
{
    Task<HealthStatus> GetHealthStatusAsync();
    Task<SystemMetrics> GetSystemMetricsAsync();
    bool IsHealthy { get; }
}

/// <summary>
/// Health status model
/// </summary>
public sealed class HealthStatus
{
    public bool ServiceRunning { get; set; }
    public int CamerasConnected { get; set; }
    public long MemoryUsageMB { get; set; }
    public int CpuTemperature { get; set; }
    public float CpuUsage { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, ComponentHealth> ComponentStatus { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public bool IsHealthy =>
        ServiceRunning &&
        MemoryUsageMB < 6000 &&
        CpuTemperature < 75 &&
        CpuUsage < 80 &&
        Errors.Count == 0;
}

/// <summary>
/// Component health status
/// </summary>
public sealed class ComponentHealth
{
    public string Name { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public string Status { get; set; } = "Unknown";
    public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// Implementation of comprehensive health check service
/// </summary>
public sealed class HealthCheckService : BackgroundService, IHealthCheckService, IDisposable
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _eventBus;
    private readonly WatchdogService _watchdog;
    private readonly string _healthDirectory;

    private Timer? _healthCheckTimer;
    private HealthStatus _lastHealthStatus = new();
    private readonly Queue<HealthStatus> _healthHistory = new(60); // 1 hour of history
    private bool _disposed;

    // Health check settings
    private const int HEALTH_CHECK_INTERVAL_SECONDS = 60;
    private const int CRITICAL_ERROR_THRESHOLD = 5;
    private readonly List<string> _recentErrors = new();

    public bool IsHealthy => _lastHealthStatus.IsHealthy;

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        IServiceProvider serviceProvider,
        IEventBus eventBus,
        WatchdogService watchdog)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
        _watchdog = watchdog;

        // Setup health data directory
        _healthDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VisionOps",
            "Health");
        Directory.CreateDirectory(_healthDirectory);

        // Subscribe to events
        _eventBus.Subscribe<ThermalThrottleEvent>(OnThermalEvent);
        _eventBus.Subscribe<ComponentRestartEvent>(OnComponentRestart);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Health check service started");

        _healthCheckTimer = new Timer(
            async _ => await PerformHealthCheckAsync(),
            null,
            TimeSpan.FromSeconds(10), // Initial delay
            TimeSpan.FromSeconds(HEALTH_CHECK_INTERVAL_SECONDS));

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>
    /// Perform comprehensive health check
    /// </summary>
    private async Task PerformHealthCheckAsync()
    {
        try
        {
            var status = await GetHealthStatusAsync();

            // Track history
            _healthHistory.Enqueue(status);
            if (_healthHistory.Count > 60)
                _healthHistory.Dequeue();

            // Log health status
            LogHealthStatus(status);

            // Pulse watchdog if healthy
            if (status.IsHealthy)
            {
                _watchdog.Pulse("HealthCheck");
            }
            else
            {
                _logger.LogWarning("System unhealthy: Memory={Memory}MB, CPU={Cpu}%, Temp={Temp}°C, Errors={Errors}",
                    status.MemoryUsageMB, status.CpuUsage, status.CpuTemperature, status.Errors.Count);
            }

            // Save health report
            await SaveHealthReportAsync(status);

            // Check for critical conditions
            await CheckCriticalConditionsAsync(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
            RecordError($"Health check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Get comprehensive health status
    /// </summary>
    public async Task<HealthStatus> GetHealthStatusAsync()
    {
        var status = new HealthStatus
        {
            ServiceRunning = true,
            Timestamp = DateTime.UtcNow
        };

        // Get system metrics
        var metrics = await GetSystemMetricsAsync();
        status.MemoryUsageMB = metrics.MemoryUsageMB;
        status.CpuTemperature = metrics.CpuTemperature;
        status.CpuUsage = metrics.CpuUsage;

        // Check components
        await CheckComponentHealthAsync(status);

        // Check network connectivity
        status.ComponentStatus["Network"] = await CheckNetworkHealthAsync();

        // Check disk space
        status.ComponentStatus["Disk"] = CheckDiskHealth();

        // Get recent errors
        status.Errors = _recentErrors.ToList();

        // Get last sync time (would be retrieved from sync service)
        status.LastSyncTime = DateTime.UtcNow.AddMinutes(-5); // Placeholder

        _lastHealthStatus = status;
        return status;
    }

    /// <summary>
    /// Get system metrics
    /// </summary>
    public async Task<SystemMetrics> GetSystemMetricsAsync()
    {
        var metrics = new SystemMetrics();

        await Task.Run(() =>
        {
            var process = Process.GetCurrentProcess();

            // Memory usage
            metrics.MemoryUsageMB = process.WorkingSet64 / (1024 * 1024);

            // CPU usage (simplified - would use ThermalManager in production)
            using (var scope = _serviceProvider.CreateScope())
            {
                var thermalManager = scope.ServiceProvider.GetService<ThermalManager>();
                if (thermalManager != null)
                {
                    metrics.CpuUsage = thermalManager.GetAverageCpuUsage();
                    metrics.CpuTemperature = thermalManager.GetCpuTemperature().Result;
                }
            }

            // Process metrics
            metrics.ActiveCameras = GetActiveCameraCount();
            metrics.FramesProcessed = GetFramesProcessedCount();

            metrics.Timestamp = DateTime.UtcNow;
        });

        return metrics;
    }

    /// <summary>
    /// Check component health
    /// </summary>
    private async Task CheckComponentHealthAsync(HealthStatus status)
    {
        await Task.Run(() =>
        {
            var process = Process.GetCurrentProcess();

            // Process health
            status.ComponentStatus["Process"] = new ComponentHealth
            {
                Name = "VisionOps.Service",
                IsRunning = process.Responding,
                LastHeartbeat = DateTime.UtcNow,
                Status = process.Responding ? "Running" : "Not Responding",
                Metrics = new Dictionary<string, object>
                {
                    ["ThreadCount"] = process.Threads.Count,
                    ["HandleCount"] = process.HandleCount,
                    ["UptimeHours"] = (DateTime.UtcNow - process.StartTime).TotalHours
                }
            };

            // Memory health
            var memoryGB = process.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);
            status.ComponentStatus["Memory"] = new ComponentHealth
            {
                Name = "Memory",
                IsRunning = true,
                LastHeartbeat = DateTime.UtcNow,
                Status = memoryGB > 5 ? "Warning" : "Healthy",
                Metrics = new Dictionary<string, object>
                {
                    ["WorkingSetGB"] = memoryGB,
                    ["PrivateMemoryGB"] = process.PrivateMemorySize64 / (1024.0 * 1024.0 * 1024.0),
                    ["PagedMemoryGB"] = process.PagedMemorySize64 / (1024.0 * 1024.0 * 1024.0),
                    ["GCMemoryMB"] = GC.GetTotalMemory(false) / (1024 * 1024)
                }
            };
        });
    }

    /// <summary>
    /// Check network health
    /// </summary>
    private async Task<ComponentHealth> CheckNetworkHealthAsync()
    {
        var health = new ComponentHealth
        {
            Name = "Network",
            LastHeartbeat = DateTime.UtcNow
        };

        try
        {
            // Check network availability
            var isAvailable = NetworkInterface.GetIsNetworkAvailable();
            health.IsRunning = isAvailable;
            health.Status = isAvailable ? "Connected" : "Disconnected";

            if (isAvailable)
            {
                // Check internet connectivity with ping
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("8.8.8.8");
                    health.Metrics["PingStatus"] = reply.Status.ToString();
                    health.Metrics["RoundtripMs"] = reply.RoundtripTime;

                    if (reply.Status != IPStatus.Success)
                    {
                        health.Status = "Limited Connectivity";
                    }
                }

                // Get network interface stats
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var ni in interfaces.Take(1)) // Just first active interface
                {
                    var stats = ni.GetIPv4Statistics();
                    health.Metrics["BytesReceived"] = stats.BytesReceived;
                    health.Metrics["BytesSent"] = stats.BytesSent;
                    health.Metrics["Speed"] = ni.Speed;
                }
            }
        }
        catch (Exception ex)
        {
            health.Status = "Error";
            health.Metrics["Error"] = ex.Message;
        }

        return health;
    }

    /// <summary>
    /// Check disk health
    /// </summary>
    private ComponentHealth CheckDiskHealth()
    {
        var health = new ComponentHealth
        {
            Name = "Disk",
            IsRunning = true,
            LastHeartbeat = DateTime.UtcNow
        };

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\");
            var freeGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            var totalGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
            var usedPercent = ((totalGB - freeGB) / totalGB) * 100;

            health.Status = freeGB < 10 ? "Low Space" : "Healthy";
            health.Metrics = new Dictionary<string, object>
            {
                ["FreeSpaceGB"] = Math.Round(freeGB, 2),
                ["TotalSpaceGB"] = Math.Round(totalGB, 2),
                ["UsedPercent"] = Math.Round(usedPercent, 1)
            };
        }
        catch (Exception ex)
        {
            health.Status = "Error";
            health.Metrics["Error"] = ex.Message;
        }

        return health;
    }

    /// <summary>
    /// Check for critical conditions
    /// </summary>
    private async Task CheckCriticalConditionsAsync(HealthStatus status)
    {
        // Check memory pressure
        if (status.MemoryUsageMB > 5500)
        {
            _logger.LogWarning("High memory usage detected: {Memory}MB", status.MemoryUsageMB);
            await _eventBus.PublishAsync(new MemoryPressureEvent
            {
                MemoryUsageMB = status.MemoryUsageMB,
                Threshold = 6000
            });
        }

        // Check temperature
        if (status.CpuTemperature > 70)
        {
            _logger.LogWarning("High CPU temperature detected: {Temp}°C", status.CpuTemperature);
        }

        // Check error rate
        if (_recentErrors.Count > CRITICAL_ERROR_THRESHOLD)
        {
            _logger.LogError("Critical error threshold exceeded: {ErrorCount} errors",
                _recentErrors.Count);
        }
    }

    /// <summary>
    /// Save health report to disk
    /// </summary>
    private async Task SaveHealthReportAsync(HealthStatus status)
    {
        try
        {
            var reportFile = Path.Combine(_healthDirectory, $"health_{DateTime.UtcNow:yyyyMMdd}.json");
            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
            await File.AppendAllTextAsync(reportFile, json + Environment.NewLine);

            // Clean up old reports (keep 7 days)
            CleanupOldReports();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save health report");
        }
    }

    /// <summary>
    /// Clean up old health reports
    /// </summary>
    private void CleanupOldReports()
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            var files = Directory.GetFiles(_healthDirectory, "health_*.json")
                .Where(f =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(f);
                    if (fileName.Length > 7 && DateTime.TryParseExact(
                        fileName.Substring(7), "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var date))
                    {
                        return date < cutoffDate;
                    }
                    return false;
                });

            foreach (var file in files)
            {
                File.Delete(file);
                _logger.LogDebug("Deleted old health report: {File}", file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old health reports");
        }
    }

    /// <summary>
    /// Log health status
    /// </summary>
    private void LogHealthStatus(HealthStatus status)
    {
        if (status.IsHealthy)
        {
            _logger.LogDebug("System healthy: Memory={Memory}MB, CPU={Cpu}%, Temp={Temp}°C",
                status.MemoryUsageMB, status.CpuUsage, status.CpuTemperature);
        }
        else
        {
            var issues = new List<string>();
            if (status.MemoryUsageMB >= 6000) issues.Add($"Memory: {status.MemoryUsageMB}MB");
            if (status.CpuTemperature >= 75) issues.Add($"Temp: {status.CpuTemperature}°C");
            if (status.CpuUsage >= 80) issues.Add($"CPU: {status.CpuUsage}%");
            if (status.Errors.Any()) issues.Add($"Errors: {status.Errors.Count}");

            _logger.LogWarning("System unhealthy: {Issues}", string.Join(", ", issues));
        }
    }

    /// <summary>
    /// Record error for tracking
    /// </summary>
    private void RecordError(string error)
    {
        _recentErrors.Add($"{DateTime.UtcNow:HH:mm:ss} - {error}");

        // Keep only last 10 errors
        while (_recentErrors.Count > 10)
        {
            _recentErrors.RemoveAt(0);
        }
    }

    /// <summary>
    /// Handle thermal events
    /// </summary>
    private void OnThermalEvent(ThermalThrottleEvent thermalEvent)
    {
        if (thermalEvent.Temperature >= 75)
        {
            RecordError($"Critical temperature: {thermalEvent.Temperature}°C");
        }
    }

    /// <summary>
    /// Handle component restart events
    /// </summary>
    private void OnComponentRestart(ComponentRestartEvent restartEvent)
    {
        _logger.LogInformation("Component restart triggered: {Reason}", restartEvent.Reason);
        RecordError($"Component restart: {restartEvent.Reason}");
    }

    // Placeholder methods - would be implemented with actual camera/frame services
    private int GetActiveCameraCount() => 0;
    private int GetFramesProcessedCount() => 0;

    public override void Dispose()
    {
        if (_disposed) return;

        _healthCheckTimer?.Dispose();
        _eventBus.Unsubscribe<ThermalThrottleEvent>(OnThermalEvent);
        _eventBus.Unsubscribe<ComponentRestartEvent>(OnComponentRestart);

        _disposed = true;
        base.Dispose();
    }
}