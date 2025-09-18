using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VisionOps.Data.Repositories;
using VisionOps.Data.Entities;
using VisionOps.Cloud.Models;
using System.Text.Json;

namespace VisionOps.Cloud;

/// <summary>
/// Background service that handles cloud synchronization
/// </summary>
public class SyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SyncService> _logger;
    private readonly SupabaseClient _supabaseClient;
    private readonly SupabaseConfiguration _configuration;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private DateTime _lastSuccessfulSync = DateTime.MinValue;
    private int _consecutiveFailures = 0;
    private const int MaxConsecutiveFailures = 5;

    public SyncService(
        IServiceProvider serviceProvider,
        ILogger<SyncService> logger,
        SupabaseClient supabaseClient,
        SupabaseConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _supabaseClient = supabaseClient;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sync service started");

        // Initial delay to let the main service start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Initialize Supabase connection
        if (!await _supabaseClient.InitializeAsync())
        {
            _logger.LogError("Failed to initialize Supabase client. Sync service will retry later.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_configuration.EnableSync)
                {
                    await PerformSyncAsync(stoppingToken);
                }

                // Wait for next sync interval
                await Task.Delay(TimeSpan.FromSeconds(_configuration.SyncIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in sync service");
                _consecutiveFailures++;

                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError("Too many consecutive failures. Pausing sync for 5 minutes.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    _consecutiveFailures = 0;
                }
            }
        }

        _logger.LogInformation("Sync service stopped");
    }

    private async Task PerformSyncAsync(CancellationToken cancellationToken)
    {
        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("Sync already in progress, skipping");
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("Starting sync cycle");

            using var scope = _serviceProvider.CreateScope();
            var syncQueueRepo = scope.ServiceProvider.GetRequiredService<ISyncQueueRepository>();
            var detectionRepo = scope.ServiceProvider.GetRequiredService<IDetectionRepository>();

            var syncStats = new SyncStatistics();

            // 1. Process sync queue first
            await ProcessSyncQueueAsync(syncQueueRepo, syncStats, cancellationToken);

            // 2. Sync new unsynced data
            await SyncDetectionsAsync(detectionRepo, syncQueueRepo, syncStats, cancellationToken);
            await SyncKeyFramesAsync(scope, syncQueueRepo, syncStats, cancellationToken);
            await SyncMetricsAsync(scope, syncQueueRepo, syncStats, cancellationToken);

            // 3. Cleanup old queue items
            await CleanupSyncQueueAsync(syncQueueRepo, cancellationToken);

            stopwatch.Stop();
            _lastSuccessfulSync = DateTime.UtcNow;
            _consecutiveFailures = 0;

            _logger.LogInformation(
                "Sync completed in {Duration}ms. Synced: {Detections} detections, {KeyFrames} key frames, {Metrics} metrics. " +
                "Failed: {Failed}, Queue size: {QueueSize}",
                stopwatch.ElapsedMilliseconds,
                syncStats.DetectionsSynced,
                syncStats.KeyFramesSynced,
                syncStats.MetricsSynced,
                syncStats.FailedItems,
                syncStats.RemainingQueueSize);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task ProcessSyncQueueAsync(
        ISyncQueueRepository syncQueueRepo,
        SyncStatistics stats,
        CancellationToken cancellationToken)
    {
        try
        {
            var pendingItems = await syncQueueRepo.GetPendingBatchAsync(_configuration.BatchSize);
            if (!pendingItems.Any()) return;

            _logger.LogDebug("Processing {Count} items from sync queue", pendingItems.Count());

            // Group by entity type for batch processing
            var groups = pendingItems.GroupBy(i => i.EntityType);

            foreach (var group in groups)
            {
                if (cancellationToken.IsCancellationRequested) break;

                await ProcessSyncGroupAsync(group.Key, group.ToList(), syncQueueRepo, stats);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing sync queue");
            stats.FailedItems += _configuration.BatchSize;
        }
    }

    private async Task ProcessSyncGroupAsync(
        string entityType,
        List<SyncQueueEntity> items,
        ISyncQueueRepository syncQueueRepo,
        SyncStatistics stats)
    {
        try
        {
            // Deserialize payloads
            var payloads = items.Select(i => JsonSerializer.Deserialize<object>(i.PayloadJson)).ToList();

            bool success = entityType switch
            {
                "Detection" => await _supabaseClient.InsertBatchAsync<CloudDetection>("detections",
                    payloads.Cast<CloudDetection>().ToList()),
                "KeyFrame" => await _supabaseClient.InsertBatchAsync<CloudKeyFrame>("key_frames",
                    payloads.Cast<CloudKeyFrame>().ToList()),
                "Metric" => await _supabaseClient.InsertBatchAsync<CloudMetric>("metrics",
                    payloads.Cast<CloudMetric>().ToList()),
                _ => false
            };

            if (success)
            {
                await syncQueueRepo.MarkAsCompletedAsync(items.Select(i => i.Id));
                UpdateStats(entityType, items.Count, stats);
            }
            else
            {
                // Mark as failed for retry
                foreach (var item in items)
                {
                    await syncQueueRepo.MarkAsFailedAsync(item.Id, "Sync failed");
                }
                stats.FailedItems += items.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing sync group for {EntityType}", entityType);
            stats.FailedItems += items.Count;

            // Mark items as failed
            foreach (var item in items)
            {
                await syncQueueRepo.MarkAsFailedAsync(item.Id, ex.Message);
            }
        }
    }

    private async Task SyncDetectionsAsync(
        IDetectionRepository detectionRepo,
        ISyncQueueRepository syncQueueRepo,
        SyncStatistics stats,
        CancellationToken cancellationToken)
    {
        try
        {
            var unsynced = await detectionRepo.GetUnsyncedAsync(_configuration.BatchSize);
            if (!unsynced.Any()) return;

            _logger.LogDebug("Syncing {Count} unsynced detections", unsynced.Count());

            // Convert to cloud models
            var cloudDetections = unsynced.Select(d => new CloudDetection
            {
                Id = d.Id,
                CameraId = d.CameraId,
                Timestamp = d.Timestamp,
                ClassName = d.ClassName,
                Confidence = d.Confidence,
                BboxX = d.BboxX,
                BboxY = d.BboxY,
                BboxWidth = d.BboxWidth,
                BboxHeight = d.BboxHeight,
                FrameNumber = d.FrameNumber,
                ZoneName = d.ZoneName,
                ProcessingTimeMs = d.ProcessingTimeMs,
                CreatedAt = d.CreatedAt
            }).ToList();

            // Try direct sync first
            if (await _supabaseClient.InsertBatchAsync("detections", cloudDetections))
            {
                await detectionRepo.MarkAsSyncedAsync(unsynced.Select(d => d.Id));
                stats.DetectionsSynced += cloudDetections.Count;
            }
            else
            {
                // Queue for retry
                await syncQueueRepo.EnqueueBatchAsync(cloudDetections, "Detection");
                stats.FailedItems += cloudDetections.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing detections");
        }
    }

    private async Task SyncKeyFramesAsync(
        IServiceScope scope,
        ISyncQueueRepository syncQueueRepo,
        SyncStatistics stats,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<VisionOps.Data.VisionOpsDbContext>();

            var unsynced = await context.KeyFrames
                .Where(k => !k.IsSynced)
                .OrderBy(k => k.CreatedAt)
                .Take(_configuration.BatchSize)
                .ToListAsync(cancellationToken);

            if (!unsynced.Any()) return;

            _logger.LogDebug("Syncing {Count} unsynced key frames", unsynced.Count);

            // Convert to cloud models
            var cloudKeyFrames = unsynced.Select(k => new CloudKeyFrame
            {
                Id = k.Id,
                CameraId = k.CameraId,
                Timestamp = k.Timestamp,
                CompressedImage = Convert.ToBase64String(k.CompressedImage),
                ImageSizeBytes = k.ImageSizeBytes,
                Description = k.Description,
                PeopleCount = k.PeopleCount,
                VehicleCount = k.VehicleCount,
                OtherObjectsJson = k.OtherObjectsJson,
                SceneAttributesJson = k.SceneAttributesJson,
                FrameNumber = k.FrameNumber,
                CreatedAt = k.CreatedAt
            }).ToList();

            // Try direct sync
            if (await _supabaseClient.InsertBatchAsync("key_frames", cloudKeyFrames))
            {
                foreach (var kf in unsynced)
                {
                    kf.IsSynced = true;
                    kf.SyncedAt = DateTime.UtcNow;
                }
                await context.SaveChangesAsync(cancellationToken);
                stats.KeyFramesSynced += cloudKeyFrames.Count;
            }
            else
            {
                // Queue for retry
                await syncQueueRepo.EnqueueBatchAsync(cloudKeyFrames, "KeyFrame");
                stats.FailedItems += cloudKeyFrames.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing key frames");
        }
    }

    private async Task SyncMetricsAsync(
        IServiceScope scope,
        ISyncQueueRepository syncQueueRepo,
        SyncStatistics stats,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<VisionOps.Data.VisionOpsDbContext>();

            var unsynced = await context.Metrics
                .Where(m => !m.IsSynced)
                .OrderBy(m => m.CreatedAt)
                .Take(_configuration.BatchSize)
                .ToListAsync(cancellationToken);

            if (!unsynced.Any()) return;

            _logger.LogDebug("Syncing {Count} unsynced metrics", unsynced.Count);

            // Convert to cloud models
            var cloudMetrics = unsynced.Select(m => new CloudMetric
            {
                Id = m.Id,
                CameraId = m.CameraId,
                WindowStart = m.WindowStart,
                WindowEnd = m.WindowEnd,
                WindowDurationSeconds = m.WindowDurationSeconds,
                SampleCount = m.SampleCount,
                AvgPeopleCount = m.AvgPeopleCount,
                MaxPeopleCount = m.MaxPeopleCount,
                AvgVehicleCount = m.AvgVehicleCount,
                MaxVehicleCount = m.MaxVehicleCount,
                TotalDetections = m.TotalDetections,
                AvgProcessingTimeMs = m.AvgProcessingTimeMs,
                P95ProcessingTimeMs = m.P95ProcessingTimeMs,
                FramesProcessed = m.FramesProcessed,
                KeyFramesProcessed = m.KeyFramesProcessed,
                ErrorCount = m.ErrorCount,
                AvgCpuUsage = m.AvgCpuUsage,
                MaxCpuTemperature = m.MaxCpuTemperature,
                AvgMemoryUsageMb = m.AvgMemoryUsageMb,
                ZoneStatsJson = m.ZoneStatsJson,
                CompressionRatio = m.CompressionRatio,
                CreatedAt = m.CreatedAt
            }).ToList();

            // Try direct sync
            if (await _supabaseClient.InsertBatchAsync("metrics", cloudMetrics))
            {
                foreach (var metric in unsynced)
                {
                    metric.IsSynced = true;
                    metric.SyncedAt = DateTime.UtcNow;
                }
                await context.SaveChangesAsync(cancellationToken);
                stats.MetricsSynced += cloudMetrics.Count;
            }
            else
            {
                // Queue for retry
                await syncQueueRepo.EnqueueBatchAsync(cloudMetrics, "Metric");
                stats.FailedItems += cloudMetrics.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing metrics");
        }
    }

    private async Task CleanupSyncQueueAsync(
        ISyncQueueRepository syncQueueRepo,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await syncQueueRepo.CleanupAsync();
            if (deleted > 0)
            {
                _logger.LogDebug("Cleaned up {Count} completed/expired sync queue items", deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up sync queue");
        }
    }

    private void UpdateStats(string entityType, int count, SyncStatistics stats)
    {
        switch (entityType)
        {
            case "Detection":
                stats.DetectionsSynced += count;
                break;
            case "KeyFrame":
                stats.KeyFramesSynced += count;
                break;
            case "Metric":
                stats.MetricsSynced += count;
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sync service stopping");
        _syncLock?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// Sync cycle statistics
/// </summary>
internal class SyncStatistics
{
    public int DetectionsSynced { get; set; }
    public int KeyFramesSynced { get; set; }
    public int MetricsSynced { get; set; }
    public int FailedItems { get; set; }
    public int RemainingQueueSize { get; set; }
}