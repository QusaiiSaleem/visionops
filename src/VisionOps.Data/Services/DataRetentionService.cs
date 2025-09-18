using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VisionOps.Data.Entities;
using VisionOps.Data.Repositories;

namespace VisionOps.Data.Services;

/// <summary>
/// Background service that manages data retention and cleanup
/// </summary>
public class DataRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataRetentionService> _logger;
    private readonly DataRetentionConfiguration _configuration;
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private DateTime _lastCleanup = DateTime.MinValue;

    public DataRetentionService(
        IServiceProvider serviceProvider,
        ILogger<DataRetentionService> logger,
        DataRetentionConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;

        // Set up timer for periodic cleanup
        var interval = TimeSpan.FromHours(_configuration.CleanupIntervalHours);
        _cleanupTimer = new Timer(_ => Task.Run(PerformCleanupAsync), null, interval, interval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data retention service started");

        // Initial delay to let the main service start
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        // Perform initial cleanup
        await PerformCleanupAsync();

        // Keep service alive
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }

        _logger.LogInformation("Data retention service stopped");
    }

    private async Task PerformCleanupAsync()
    {
        if (!await _cleanupLock.WaitAsync(0))
        {
            _logger.LogDebug("Cleanup already in progress, skipping");
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Starting data retention cleanup");

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<VisionOpsDbContext>();

            var stats = new CleanupStatistics();

            // 1. Clean up old detections
            stats.DeletedDetections = await CleanupDetectionsAsync(context);

            // 2. Clean up old key frames
            stats.DeletedKeyFrames = await CleanupKeyFramesAsync(context);

            // 3. Clean up old metrics
            stats.DeletedMetrics = await CleanupMetricsAsync(context);

            // 4. Clean up sync queue
            stats.DeletedSyncQueueItems = await CleanupSyncQueueAsync(context);

            // 5. Vacuum database if significant cleanup occurred
            if (stats.TotalDeleted > 1000)
            {
                await VacuumDatabaseAsync(context);
                stats.DatabaseVacuumed = true;
            }

            // 6. Get database statistics
            stats.DatabaseStats = await context.GetDatabaseStatsAsync();

            stopwatch.Stop();
            _lastCleanup = DateTime.UtcNow;

            _logger.LogInformation(
                "Data retention cleanup completed in {Duration}ms. " +
                "Deleted: {Detections} detections, {KeyFrames} key frames, {Metrics} metrics, {SyncQueue} sync queue items. " +
                "Database size: {DbSize:F2}MB",
                stopwatch.ElapsedMilliseconds,
                stats.DeletedDetections,
                stats.DeletedKeyFrames,
                stats.DeletedMetrics,
                stats.DeletedSyncQueueItems,
                stats.DatabaseStats.DatabaseSizeMb);

            // Alert if database is getting large
            if (stats.DatabaseStats.DatabaseSizeMb > _configuration.MaxDatabaseSizeMb)
            {
                _logger.LogWarning(
                    "Database size {Size:F2}MB exceeds maximum {Max}MB. Consider adjusting retention settings.",
                    stats.DatabaseStats.DatabaseSizeMb,
                    _configuration.MaxDatabaseSizeMb);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data retention cleanup");
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    private async Task<int> CleanupDetectionsAsync(VisionOpsDbContext context)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_configuration.LocalRetentionDays);

            // Only delete synced detections
            var deletedCount = await context.Detections
                .Where(d => d.CreatedAt < cutoffDate && d.IsSynced)
                .ExecuteDeleteAsync();

            if (deletedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} old detections", deletedCount);
            }

            // Also delete orphaned detections (no camera)
            var orphanedCount = await context.Detections
                .Where(d => !context.Cameras.Any(c => c.Id == d.CameraId))
                .ExecuteDeleteAsync();

            if (orphanedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} orphaned detections", orphanedCount);
                deletedCount += orphanedCount;
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up detections");
            return 0;
        }
    }

    private async Task<int> CleanupKeyFramesAsync(VisionOpsDbContext context)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_configuration.KeyFrameRetentionDays);

            // Only delete synced key frames
            var deletedCount = await context.KeyFrames
                .Where(k => k.CreatedAt < cutoffDate && k.IsSynced)
                .ExecuteDeleteAsync();

            if (deletedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} old key frames", deletedCount);
            }

            // Delete orphaned key frames
            var orphanedCount = await context.KeyFrames
                .Where(k => !context.Cameras.Any(c => c.Id == k.CameraId))
                .ExecuteDeleteAsync();

            if (orphanedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} orphaned key frames", orphanedCount);
                deletedCount += orphanedCount;
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up key frames");
            return 0;
        }
    }

    private async Task<int> CleanupMetricsAsync(VisionOpsDbContext context)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_configuration.MetricsRetentionDays);

            // Only delete synced metrics
            var deletedCount = await context.Metrics
                .Where(m => m.CreatedAt < cutoffDate && m.IsSynced)
                .ExecuteDeleteAsync();

            if (deletedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} old metrics", deletedCount);
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up metrics");
            return 0;
        }
    }

    private async Task<int> CleanupSyncQueueAsync(VisionOpsDbContext context)
    {
        try
        {
            var deletedCount = 0;

            // Delete completed items older than 1 day
            deletedCount += await context.SyncQueue
                .Where(s => s.Status == "Completed" && s.UpdatedAt < DateTime.UtcNow.AddDays(-1))
                .ExecuteDeleteAsync();

            // Delete expired items
            deletedCount += await context.SyncQueue
                .Where(s => s.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync();

            // Delete permanently failed items (exceeded max retries)
            deletedCount += await context.SyncQueue
                .Where(s => s.Status == "Failed" &&
                           s.RetryCount >= s.MaxRetries &&
                           s.UpdatedAt < DateTime.UtcNow.AddDays(-7))
                .ExecuteDeleteAsync();

            if (deletedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} sync queue items", deletedCount);
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up sync queue");
            return 0;
        }
    }

    private async Task VacuumDatabaseAsync(VisionOpsDbContext context)
    {
        try
        {
            _logger.LogDebug("Vacuuming database to reclaim space");
            await context.Database.ExecuteSqlRawAsync("VACUUM");

            // Also analyze tables for better query performance
            await context.Database.ExecuteSqlRawAsync("ANALYZE");

            _logger.LogInformation("Database vacuum and analyze completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error vacuuming database");
        }
    }

    /// <summary>
    /// Get current retention statistics
    /// </summary>
    public async Task<RetentionStatistics> GetStatisticsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VisionOpsDbContext>();

        var stats = new RetentionStatistics
        {
            LastCleanup = _lastCleanup,
            NextCleanup = _lastCleanup.AddHours(_configuration.CleanupIntervalHours)
        };

        // Get age distribution
        var now = DateTime.UtcNow;

        stats.DetectionsByAge = new Dictionary<string, int>
        {
            ["<1day"] = await context.Detections.CountAsync(d => d.CreatedAt > now.AddDays(-1)),
            ["1-3days"] = await context.Detections.CountAsync(d => d.CreatedAt > now.AddDays(-3) && d.CreatedAt <= now.AddDays(-1)),
            ["3-7days"] = await context.Detections.CountAsync(d => d.CreatedAt > now.AddDays(-7) && d.CreatedAt <= now.AddDays(-3)),
            [">7days"] = await context.Detections.CountAsync(d => d.CreatedAt <= now.AddDays(-7))
        };

        stats.KeyFramesByAge = new Dictionary<string, int>
        {
            ["<1day"] = await context.KeyFrames.CountAsync(k => k.CreatedAt > now.AddDays(-1)),
            ["1-3days"] = await context.KeyFrames.CountAsync(k => k.CreatedAt > now.AddDays(-3) && k.CreatedAt <= now.AddDays(-1)),
            ["3-7days"] = await context.KeyFrames.CountAsync(k => k.CreatedAt > now.AddDays(-7) && k.CreatedAt <= now.AddDays(-3)),
            [">7days"] = await context.KeyFrames.CountAsync(k => k.CreatedAt <= now.AddDays(-7))
        };

        // Get database stats
        stats.DatabaseStats = await context.GetDatabaseStatsAsync();

        return stats;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Data retention service stopping");

        _cleanupTimer?.Change(Timeout.Infinite, 0);
        _cleanupTimer?.Dispose();
        _cleanupLock?.Dispose();

        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// Data retention configuration
/// </summary>
public class DataRetentionConfiguration
{
    public int LocalRetentionDays { get; set; } = 7;
    public int KeyFrameRetentionDays { get; set; } = 7;
    public int MetricsRetentionDays { get; set; } = 30;
    public int CleanupIntervalHours { get; set; } = 6;
    public double MaxDatabaseSizeMb { get; set; } = 5000; // 5GB max
}

/// <summary>
/// Cleanup operation statistics
/// </summary>
internal class CleanupStatistics
{
    public int DeletedDetections { get; set; }
    public int DeletedKeyFrames { get; set; }
    public int DeletedMetrics { get; set; }
    public int DeletedSyncQueueItems { get; set; }
    public bool DatabaseVacuumed { get; set; }
    public DatabaseStats DatabaseStats { get; set; } = new();

    public int TotalDeleted => DeletedDetections + DeletedKeyFrames + DeletedMetrics + DeletedSyncQueueItems;
}

/// <summary>
/// Retention statistics for monitoring
/// </summary>
public class RetentionStatistics
{
    public DateTime LastCleanup { get; set; }
    public DateTime NextCleanup { get; set; }
    public Dictionary<string, int> DetectionsByAge { get; set; } = new();
    public Dictionary<string, int> KeyFramesByAge { get; set; } = new();
    public DatabaseStats DatabaseStats { get; set; } = new();
}