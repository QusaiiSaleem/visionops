using System.Diagnostics;
using System.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisionOps.Core.Models;
using VisionOps.Video.Memory;

namespace VisionOps.Service.Monitoring;

/// <summary>
/// CRITICAL Phase 0 Component: Memory monitoring and leak detection service.
/// Tracks memory growth, enforces limits, and triggers restarts when necessary.
/// Production requirement: <50MB growth per 24 hours, <6GB total usage.
/// </summary>
public sealed class MemoryMonitor : BackgroundService, IDisposable
{
    private readonly ILogger<MemoryMonitor> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly FrameBufferPool _bufferPool;
    private readonly List<CircularFrameBuffer> _frameBuffers;
    private readonly Process _currentProcess;
    private readonly Timer _gcTimer;

    // Memory thresholds (in MB)
    private const long WarningMemoryMB = 4000;      // 4GB warning
    private const long CriticalMemoryMB = 5500;     // 5.5GB critical
    private const long MaxMemoryMB = 6000;          // 6GB hard limit
    private const double WarningGrowthRateMBPerHour = 10;   // 10MB/hour warning
    private const double CriticalGrowthRateMBPerHour = 50;  // 50MB/hour restart

    // Tracking
    private readonly Queue<MemorySnapshot> _memoryHistory;
    private readonly object _historyLock = new();
    private DateTime _startTime;
    private long _initialMemoryMB;
    private long _peakMemoryMB;
    private int _gcGen2Count;
    private int _consecutiveHighMemoryReadings;

    // GC settings
    private const int GCIntervalMinutes = 30;
    private const int HistoryWindowHours = 24;
    private const int SnapshotIntervalSeconds = 60;

    public MemoryMonitor(
        ILogger<MemoryMonitor> logger,
        IHostApplicationLifetime appLifetime,
        FrameBufferPool bufferPool)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _bufferPool = bufferPool;
        _frameBuffers = new List<CircularFrameBuffer>();
        _currentProcess = Process.GetCurrentProcess();
        _memoryHistory = new Queue<MemorySnapshot>();

        // Configure GC for server mode (better for long-running services)
        ConfigureGarbageCollection();

        // Start periodic GC timer
        _gcTimer = new Timer(
            ForceGarbageCollection,
            null,
            TimeSpan.FromMinutes(GCIntervalMinutes),
            TimeSpan.FromMinutes(GCIntervalMinutes));

        _logger.LogInformation(
            "MemoryMonitor initialized. Limits: Warning={Warning}MB, Critical={Critical}MB, Max={Max}MB",
            WarningMemoryMB, CriticalMemoryMB, MaxMemoryMB);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _startTime = DateTime.UtcNow;
        _initialMemoryMB = GetCurrentMemoryMB();
        _peakMemoryMB = _initialMemoryMB;
        _gcGen2Count = GC.CollectionCount(2);

        _logger.LogInformation("Memory monitoring started. Initial memory: {Initial}MB", _initialMemoryMB);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorMemory(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(SnapshotIntervalSeconds), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in memory monitoring");
                await Task.Delay(TimeSpan.FromSeconds(SnapshotIntervalSeconds * 2), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Configure GC for optimal server performance
    /// </summary>
    private void ConfigureGarbageCollection()
    {
        try
        {
            // Enable server GC for better throughput
            GCSettings.IsServerGC = true;

            // Set LOH threshold to 85KB (default)
            GCSettings.LargeObjectHeapCompactionMode =
                GCLargeObjectHeapCompactionMode.Default;

            // Set latency mode for balanced performance
            GCSettings.LatencyMode = GCLatencyMode.Interactive;

            _logger.LogInformation(
                "GC configured: ServerGC={IsServer}, LatencyMode={Mode}",
                GCSettings.IsServerGC, GCSettings.LatencyMode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure GC settings");
        }
    }

    /// <summary>
    /// Main memory monitoring loop
    /// </summary>
    private async Task MonitorMemory(CancellationToken cancellationToken)
    {
        var currentMemoryMB = GetCurrentMemoryMB();
        var snapshot = new MemorySnapshot
        {
            Timestamp = DateTime.UtcNow,
            WorkingSetMB = currentMemoryMB,
            PrivateBytesMB = GetPrivateBytesMB(),
            ManagedMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };

        // Track peak memory
        if (currentMemoryMB > _peakMemoryMB)
        {
            _peakMemoryMB = currentMemoryMB;
        }

        // Add to history
        lock (_historyLock)
        {
            _memoryHistory.Enqueue(snapshot);

            // Remove old entries
            var cutoff = DateTime.UtcNow.AddHours(-HistoryWindowHours);
            while (_memoryHistory.Count > 0 && _memoryHistory.Peek().Timestamp < cutoff)
            {
                _memoryHistory.Dequeue();
            }
        }

        // Check memory conditions
        await CheckMemoryConditions(snapshot, cancellationToken);

        // Check for memory leaks
        await CheckForMemoryLeaks(cancellationToken);

        // Log periodic status
        if (snapshot.Timestamp.Minute % 5 == 0 && snapshot.Timestamp.Second < SnapshotIntervalSeconds)
        {
            LogMemoryStatus(snapshot);
        }
    }

    /// <summary>
    /// Check memory conditions and take action if necessary
    /// </summary>
    private async Task CheckMemoryConditions(MemorySnapshot snapshot, CancellationToken cancellationToken)
    {
        // Check absolute memory limits
        if (snapshot.WorkingSetMB >= MaxMemoryMB)
        {
            _logger.LogCritical(
                "MEMORY LIMIT EXCEEDED: {Current}MB >= {Max}MB. Initiating emergency restart!",
                snapshot.WorkingSetMB, MaxMemoryMB);

            await TriggerEmergencyRestart("Memory limit exceeded");
        }
        else if (snapshot.WorkingSetMB >= CriticalMemoryMB)
        {
            _consecutiveHighMemoryReadings++;

            _logger.LogError(
                "Critical memory usage: {Current}MB (consecutive readings: {Count})",
                snapshot.WorkingSetMB, _consecutiveHighMemoryReadings);

            // Trigger restart after 3 consecutive high readings
            if (_consecutiveHighMemoryReadings >= 3)
            {
                await TriggerEmergencyRestart("Sustained critical memory usage");
            }

            // Force aggressive GC
            await ForceAggressiveCleanup();
        }
        else if (snapshot.WorkingSetMB >= WarningMemoryMB)
        {
            _logger.LogWarning(
                "High memory usage: {Current}MB. Peak: {Peak}MB",
                snapshot.WorkingSetMB, _peakMemoryMB);

            // Reset consecutive counter if below critical
            _consecutiveHighMemoryReadings = 0;

            // Trigger standard GC
            GC.Collect(2, GCCollectionMode.Forced, true);
        }
        else
        {
            _consecutiveHighMemoryReadings = 0;
        }

        // Check growth rate
        var growthRate = CalculateGrowthRate();
        if (growthRate >= CriticalGrowthRateMBPerHour)
        {
            _logger.LogError(
                "Critical memory growth rate: {Rate:F2}MB/hour. Scheduling restart.",
                growthRate);

            await ScheduleRestart(TimeSpan.FromMinutes(30), "High memory growth rate");
        }
        else if (growthRate >= WarningGrowthRateMBPerHour)
        {
            _logger.LogWarning(
                "Warning: Memory growth rate {Rate:F2}MB/hour exceeds threshold",
                growthRate);
        }
    }

    /// <summary>
    /// Check for memory leaks in pools and buffers
    /// </summary>
    private async Task CheckForMemoryLeaks(CancellationToken cancellationToken)
    {
        // Check buffer pool for leaks
        if (_bufferPool != null && _bufferPool.HasMemoryLeak())
        {
            var stats = _bufferPool.GetStatistics();
            _logger.LogWarning(
                "Memory leak detected in buffer pool. Leaked buffers: {Leaked}, Peak usage: {Peak}",
                stats.LeakedBuffers, stats.PeakUsage);

            // Force cleanup
            _bufferPool.ForceCleanup();
        }

        // Check frame buffers
        foreach (var buffer in _frameBuffers)
        {
            var stats = buffer.GetStatistics();
            if (stats.DropRate > 10) // More than 10% drop rate
            {
                _logger.LogWarning(
                    "High frame drop rate for camera {Camera}: {Rate:F2}%",
                    stats.CameraId, stats.DropRate);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Calculate memory growth rate in MB per hour
    /// </summary>
    private double CalculateGrowthRate()
    {
        lock (_historyLock)
        {
            if (_memoryHistory.Count < 2)
                return 0;

            var oldest = _memoryHistory.First();
            var newest = _memoryHistory.Last();

            var timeDiffHours = (newest.Timestamp - oldest.Timestamp).TotalHours;
            if (timeDiffHours == 0)
                return 0;

            var memoryDiffMB = newest.WorkingSetMB - oldest.WorkingSetMB;
            return memoryDiffMB / timeDiffHours;
        }
    }

    /// <summary>
    /// Force aggressive memory cleanup
    /// </summary>
    private async Task ForceAggressiveCleanup()
    {
        _logger.LogWarning("Forcing aggressive memory cleanup");

        // Clear frame buffers
        foreach (var buffer in _frameBuffers)
        {
            buffer.Clear();
        }

        // Force buffer pool cleanup
        _bufferPool?.ForceCleanup();

        // Multiple GC passes
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            await Task.Delay(100);
        }

        // Compact LOH
        GCSettings.LargeObjectHeapCompactionMode =
            GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();

        _logger.LogInformation(
            "Aggressive cleanup completed. Memory after: {Memory}MB",
            GetCurrentMemoryMB());
    }

    /// <summary>
    /// Schedule a service restart
    /// </summary>
    private async Task ScheduleRestart(TimeSpan delay, string reason)
    {
        _logger.LogWarning(
            "Service restart scheduled in {Delay} minutes. Reason: {Reason}",
            delay.TotalMinutes, reason);

        await Task.Delay(delay);
        await TriggerEmergencyRestart(reason);
    }

    /// <summary>
    /// Trigger emergency restart
    /// </summary>
    private async Task TriggerEmergencyRestart(string reason)
    {
        _logger.LogCritical("EMERGENCY RESTART initiated. Reason: {Reason}", reason);

        // Log final statistics
        LogFinalStatistics();

        // Allow time for logs to flush
        await Task.Delay(2000);

        // Stop the application
        _appLifetime.StopApplication();
    }

    /// <summary>
    /// Periodic GC callback
    /// </summary>
    private void ForceGarbageCollection(object state)
    {
        try
        {
            var beforeMB = GetCurrentMemoryMB();

            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true);

            var afterMB = GetCurrentMemoryMB();
            var freedMB = beforeMB - afterMB;

            if (freedMB > 0)
            {
                _logger.LogInformation(
                    "Periodic GC completed. Freed: {Freed}MB (Before: {Before}MB, After: {After}MB)",
                    freedMB, beforeMB, afterMB);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during periodic GC");
        }
    }

    /// <summary>
    /// Get current working set in MB
    /// </summary>
    private long GetCurrentMemoryMB()
    {
        _currentProcess.Refresh();
        return _currentProcess.WorkingSet64 / (1024 * 1024);
    }

    /// <summary>
    /// Get private bytes in MB
    /// </summary>
    private long GetPrivateBytesMB()
    {
        _currentProcess.Refresh();
        return _currentProcess.PrivateMemorySize64 / (1024 * 1024);
    }

    /// <summary>
    /// Log current memory status
    /// </summary>
    private void LogMemoryStatus(MemorySnapshot snapshot)
    {
        var uptime = DateTime.UtcNow - _startTime;
        var growthRate = CalculateGrowthRate();

        _logger.LogInformation(
            "Memory Status - Working: {Working}MB, Private: {Private}MB, Managed: {Managed}MB, " +
            "Peak: {Peak}MB, Growth: {Growth:F2}MB/hr, Uptime: {Uptime:F1}hrs, " +
            "GC Gen0: {Gen0}, Gen1: {Gen1}, Gen2: {Gen2}",
            snapshot.WorkingSetMB,
            snapshot.PrivateBytesMB,
            snapshot.ManagedMemoryMB,
            _peakMemoryMB,
            growthRate,
            uptime.TotalHours,
            snapshot.Gen0Collections,
            snapshot.Gen1Collections,
            snapshot.Gen2Collections);

        // Log buffer pool statistics
        if (_bufferPool != null)
        {
            var poolStats = _bufferPool.GetStatistics();
            _logger.LogDebug(
                "Buffer Pool - In Use: {InUse}, Peak: {Peak}, Leaked: {Leaked}",
                poolStats.CurrentlyInUse,
                poolStats.PeakUsage,
                poolStats.LeakedBuffers);
        }
    }

    /// <summary>
    /// Log final statistics before shutdown
    /// </summary>
    private void LogFinalStatistics()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var totalGrowth = _peakMemoryMB - _initialMemoryMB;
        var growthRate = CalculateGrowthRate();

        _logger.LogInformation(
            "FINAL MEMORY STATISTICS - " +
            "Initial: {Initial}MB, Peak: {Peak}MB, Current: {Current}MB, " +
            "Total Growth: {Growth}MB, Growth Rate: {Rate:F2}MB/hr, " +
            "Uptime: {Uptime:F1}hrs, Gen2 Collections: {Gen2}",
            _initialMemoryMB,
            _peakMemoryMB,
            GetCurrentMemoryMB(),
            totalGrowth,
            growthRate,
            uptime.TotalHours,
            GC.CollectionCount(2) - _gcGen2Count);

        // Log pool statistics
        if (_bufferPool != null)
        {
            var poolStats = _bufferPool.GetStatistics();
            _logger.LogInformation(
                "FINAL BUFFER POOL STATISTICS - " +
                "Total Allocated: {Allocated}, Total Returned: {Returned}, " +
                "Leaked: {Leaked}, Peak Usage: {Peak}",
                poolStats.TotalAllocated,
                poolStats.TotalReturned,
                poolStats.LeakedBuffers,
                poolStats.PeakUsage);
        }
    }

    /// <summary>
    /// Register a frame buffer for monitoring
    /// </summary>
    public void RegisterFrameBuffer(CircularFrameBuffer buffer)
    {
        if (buffer != null && !_frameBuffers.Contains(buffer))
        {
            _frameBuffers.Add(buffer);
            _logger.LogDebug("Registered frame buffer for monitoring. Total: {Count}", _frameBuffers.Count);
        }
    }

    /// <summary>
    /// Get comprehensive memory metrics
    /// </summary>
    public MemoryMetrics GetMemoryMetrics()
    {
        lock (_historyLock)
        {
            var snapshot = _memoryHistory.LastOrDefault() ?? new MemorySnapshot
            {
                WorkingSetMB = GetCurrentMemoryMB(),
                PrivateBytesMB = GetPrivateBytesMB(),
                ManagedMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024)
            };

            return new MemoryMetrics
            {
                CurrentWorkingSetMB = snapshot.WorkingSetMB,
                CurrentPrivateBytesMB = snapshot.PrivateBytesMB,
                CurrentManagedMemoryMB = snapshot.ManagedMemoryMB,
                PeakMemoryMB = _peakMemoryMB,
                InitialMemoryMB = _initialMemoryMB,
                GrowthRateMBPerHour = CalculateGrowthRate(),
                UptimeHours = (DateTime.UtcNow - _startTime).TotalHours,
                Gen0Collections = snapshot.Gen0Collections,
                Gen1Collections = snapshot.Gen1Collections,
                Gen2Collections = snapshot.Gen2Collections,
                IsHealthy = snapshot.WorkingSetMB < WarningMemoryMB &&
                           CalculateGrowthRate() < WarningGrowthRateMBPerHour
            };
        }
    }

    public override void Dispose()
    {
        LogFinalStatistics();
        _gcTimer?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Memory snapshot for tracking
/// </summary>
internal class MemorySnapshot
{
    public DateTime Timestamp { get; init; }
    public long WorkingSetMB { get; init; }
    public long PrivateBytesMB { get; init; }
    public long ManagedMemoryMB { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
}

/// <summary>
/// Comprehensive memory metrics
/// </summary>
public class MemoryMetrics
{
    public long CurrentWorkingSetMB { get; init; }
    public long CurrentPrivateBytesMB { get; init; }
    public long CurrentManagedMemoryMB { get; init; }
    public long PeakMemoryMB { get; init; }
    public long InitialMemoryMB { get; init; }
    public double GrowthRateMBPerHour { get; init; }
    public double UptimeHours { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public bool IsHealthy { get; init; }
}