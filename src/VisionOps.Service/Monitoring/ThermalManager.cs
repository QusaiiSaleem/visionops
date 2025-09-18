using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisionOps.Core.Models;
using VisionOps.Service.Events;

namespace VisionOps.Service.Monitoring;

/// <summary>
/// CRITICAL Phase 0 Component: Thermal management to prevent CPU throttling.
/// Intel CPUs throttle at 75°C, we proactively throttle at 70°C.
/// </summary>
public sealed class ThermalManager : BackgroundService, IDisposable
{
    private readonly ILogger<ThermalManager> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IEventBus _eventBus;
    private readonly Computer _computer;
    private readonly SemaphoreSlim _throttleSemaphore;

    // Critical temperature thresholds
    private const int THROTTLE_TEMP = 70;      // Start throttling
    private const int CRITICAL_TEMP = 75;      // Emergency shutdown
    private const int RESUME_TEMP = 65;        // Resume normal operation

    // Performance control
    private int _currentProcessingDelay = 0;
    private bool _isThrottled = false;
    private DateTime _lastThrottleTime = DateTime.MinValue;

    // CPU monitoring
    private readonly PerformanceCounter? _cpuCounter;
    private float _lastCpuUsage = 0f;
    private readonly Queue<float> _cpuHistory = new(60); // 5 minutes of history

    public ThermalManager(ILogger<ThermalManager> logger, IHostApplicationLifetime appLifetime, IEventBus eventBus)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _eventBus = eventBus;
        _throttleSemaphore = new SemaphoreSlim(1, 1);

        // Initialize hardware monitoring
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = false,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false
        };

        _computer.Open();
        _computer.Accept(new UpdateVisitor());

        // Initialize CPU performance counter
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // First call always returns 0
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize CPU performance counter");
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Thermal management started. Throttle: {Throttle}°C, Critical: {Critical}°C",
            THROTTLE_TEMP, CRITICAL_TEMP);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var temperature = await GetCpuTemperature();
                await ManageTemperature(temperature);

                // Check every 5 seconds
                await Task.Delay(5000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in thermal monitoring");
                await Task.Delay(10000, stoppingToken); // Back off on error
            }
        }
    }

    /// <summary>
    /// Get current CPU temperature in Celsius
    /// </summary>
    public async Task<int> GetCpuTemperature()
    {
        return await Task.Run(() =>
        {
            try
            {
                _computer.Accept(new UpdateVisitor());

                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        hardware.Update();

                        // Find temperature sensors
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature &&
                                sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            {
                                if (sensor.Value.HasValue)
                                {
                                    return (int)Math.Round(sensor.Value.Value);
                                }
                            }
                        }
                    }
                }

                // Fallback to WMI if LibreHardwareMonitor fails
                return GetCpuTemperatureWmi();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get CPU temperature, returning safe value");
                return 60; // Safe assumption to prevent emergency shutdown
            }
        });
    }

    /// <summary>
    /// Fallback WMI method for temperature
    /// </summary>
    private int GetCpuTemperatureWmi()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\WMI",
                "SELECT * FROM MSAcpi_ThermalZoneTemperature");

            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var temp = Convert.ToDouble(obj["CurrentTemperature"].ToString());
                // Convert from tenths of Kelvin to Celsius
                return (int)((temp - 2732) / 10.0);
            }
        }
        catch
        {
            // WMI might not be available or accessible
        }

        return 60; // Safe default
    }

    /// <summary>
    /// Manage system based on temperature
    /// </summary>
    private async Task ManageTemperature(int temperature)
    {
        await _throttleSemaphore.WaitAsync();
        try
        {
            if (temperature >= CRITICAL_TEMP)
            {
                _logger.LogCritical("CRITICAL TEMPERATURE: {Temp}°C - Emergency shutdown!", temperature);
                await EmergencyShutdown();
            }
            else if (temperature >= THROTTLE_TEMP && !_isThrottled)
            {
                _logger.LogWarning("High temperature: {Temp}°C - Throttling processing", temperature);
                await StartThrottling();
            }
            else if (temperature <= RESUME_TEMP && _isThrottled)
            {
                _logger.LogInformation("Temperature normalized: {Temp}°C - Resuming normal operation", temperature);
                await StopThrottling();
            }

            // Log temperature periodically
            if (DateTime.UtcNow - _lastThrottleTime > TimeSpan.FromMinutes(5))
            {
                _logger.LogInformation("CPU Temperature: {Temp}°C (Status: {Status})",
                    temperature, _isThrottled ? "Throttled" : "Normal");
                _lastThrottleTime = DateTime.UtcNow;
            }
        }
        finally
        {
            _throttleSemaphore.Release();
        }
    }

    /// <summary>
    /// Start throttling to reduce CPU load
    /// </summary>
    private async Task StartThrottling()
    {
        _isThrottled = true;
        _currentProcessingDelay = 5000; // Add 5 second delay between frames

        // Reduce process priority
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to adjust process priority");
        }

        // Notify all processing components to throttle
        await PublishThrottleEvent(true);
    }

    /// <summary>
    /// Stop throttling and resume normal operation
    /// </summary>
    private async Task StopThrottling()
    {
        _isThrottled = false;
        _currentProcessingDelay = 0;

        // Restore process priority
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore process priority");
        }

        // Notify all processing components to resume
        await PublishThrottleEvent(false);
    }

    /// <summary>
    /// Emergency shutdown to prevent hardware damage
    /// </summary>
    private async Task EmergencyShutdown()
    {
        _logger.LogCritical("Initiating emergency shutdown due to critical temperature");

        // Notify all components of emergency shutdown
        var temperature = await GetCpuTemperature();
        await _eventBus.PublishAsync(ThermalThrottleEvent.EmergencyShutdown(temperature));

        // Give time for logs to flush and components to save state
        await Task.Delay(2000);

        // Stop the application gracefully
        _appLifetime.StopApplication();
    }

    /// <summary>
    /// Publish throttle event to all components
    /// </summary>
    private async Task PublishThrottleEvent(bool throttle)
    {
        var temperature = await GetCpuTemperature();
        var eventData = throttle
            ? ThermalThrottleEvent.StartThrottle(temperature, _currentProcessingDelay)
            : ThermalThrottleEvent.StopThrottle(temperature);

        await _eventBus.PublishAsync(eventData);
        _logger.LogInformation("Thermal throttle event published: {Throttle}", throttle ? "ON" : "OFF");
    }

    /// <summary>
    /// Get current throttle delay in milliseconds
    /// </summary>
    public int GetThrottleDelay() => _currentProcessingDelay;

    /// <summary>
    /// Check if system is currently throttled
    /// </summary>
    public bool IsThrottled => _isThrottled;

    /// <summary>
    /// Get system metrics including temperature
    /// </summary>
    public async Task<SystemMetrics> GetSystemMetrics()
    {
        var process = Process.GetCurrentProcess();

        return new SystemMetrics
        {
            CpuTemperature = await GetCpuTemperature(),
            CpuUsage = await GetCpuUsage(),
            MemoryUsageMB = process.WorkingSet64 / (1024 * 1024),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get current CPU usage percentage
    /// </summary>
    private async Task<float> GetCpuUsage()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_cpuCounter != null)
                {
                    var usage = _cpuCounter.NextValue();

                    // Track history for averaging
                    _cpuHistory.Enqueue(usage);
                    if (_cpuHistory.Count > 60)
                        _cpuHistory.Dequeue();

                    _lastCpuUsage = usage;
                    return usage;
                }

                // Fallback to process CPU if counter unavailable
                return GetProcessCpuUsage();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get CPU usage");
                return _lastCpuUsage; // Return last known value
            }
        });
    }

    /// <summary>
    /// Get CPU usage for current process only
    /// </summary>
    private float GetProcessCpuUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(100); // Small sample period

            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return (float)(cpuUsageTotal * 100);
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// Get average CPU usage over the last minute
    /// </summary>
    public float GetAverageCpuUsage()
    {
        if (_cpuHistory.Count == 0)
            return _lastCpuUsage;

        return _cpuHistory.Average();
    }

    public override void Dispose()
    {
        _computer?.Close();
        _throttleSemaphore?.Dispose();
        _cpuCounter?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Visitor pattern for LibreHardwareMonitor
    /// </summary>
    private class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }

        public void VisitParameter(IParameter parameter) { }
        public void VisitSensor(ISensor sensor) { }
    }
}