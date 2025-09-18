using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VisionOps.Data.Entities;

namespace VisionOps.Data.Repositories;

/// <summary>
/// Repository implementation for sync queue operations
/// </summary>
public class SyncQueueRepository : Repository<SyncQueueEntity>, ISyncQueueRepository
{
    public SyncQueueRepository(VisionOpsDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<SyncQueueEntity>> GetPendingBatchAsync(int batchSize = 100)
    {
        var now = DateTime.UtcNow;

        return await _dbSet
            .Where(q => q.Status == "Pending" &&
                       (q.NextRetryAt == null || q.NextRetryAt <= now))
            .OrderBy(q => q.Priority)
            .ThenBy(q => q.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task<Guid> EnqueueBatchAsync<T>(IEnumerable<T> entities, string entityType, string operation = "Create") where T : class
    {
        var batchId = Guid.NewGuid();
        var queueItems = new List<SyncQueueEntity>();
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        foreach (var entity in entities)
        {
            var json = JsonSerializer.Serialize(entity, jsonOptions);
            var entityId = GetEntityId(entity);
            var cameraId = GetCameraId(entity);

            var queueItem = new SyncQueueEntity
            {
                EntityType = entityType,
                EntityId = entityId,
                Operation = operation,
                PayloadJson = json,
                PayloadSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json),
                Status = "Pending",
                BatchId = batchId,
                CameraId = cameraId,
                Priority = GetPriority(entityType),
                MaxRetries = 5,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            queueItems.Add(queueItem);
        }

        await _dbSet.AddRangeAsync(queueItems);
        await _context.SaveChangesAsync();

        return batchId;
    }

    public async Task MarkAsCompletedAsync(IEnumerable<Guid> queueIds)
    {
        await _dbSet
            .Where(q => queueIds.Contains(q.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.Status, "Completed")
                .SetProperty(q => q.UpdatedAt, DateTime.UtcNow));
    }

    public async Task MarkAsFailedAsync(Guid queueId, string error)
    {
        var item = await _dbSet.FindAsync(queueId);
        if (item != null)
        {
            item.Status = "Failed";
            item.LastError = error;
            item.LastAttemptAt = DateTime.UtcNow;
            item.RetryCount++;
            item.UpdatedAt = DateTime.UtcNow;

            // Calculate next retry time with exponential backoff
            if (item.RetryCount < item.MaxRetries)
            {
                var delaySeconds = Math.Pow(2, item.RetryCount) * 30; // 30s, 60s, 120s, 240s, 480s
                item.NextRetryAt = DateTime.UtcNow.AddSeconds(delaySeconds);
                item.Status = "Pending"; // Reset to pending for retry
            }

            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> ResetFailedItemsAsync()
    {
        return await _dbSet
            .Where(q => q.Status == "Failed" && q.RetryCount < q.MaxRetries)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.Status, "Pending")
                .SetProperty(q => q.NextRetryAt, DateTime.UtcNow)
                .SetProperty(q => q.UpdatedAt, DateTime.UtcNow));
    }

    public async Task<SyncQueueStats> GetStatsAsync()
    {
        var stats = new SyncQueueStats
        {
            PendingCount = await _dbSet.CountAsync(q => q.Status == "Pending"),
            ProcessingCount = await _dbSet.CountAsync(q => q.Status == "Processing"),
            FailedCount = await _dbSet.CountAsync(q => q.Status == "Failed"),
            CompletedCount = await _dbSet.CountAsync(q => q.Status == "Completed"),
            TotalSizeBytes = await _dbSet.SumAsync(q => (long)q.PayloadSizeBytes)
        };

        // Get entity type counts
        var typeCounts = await _dbSet
            .GroupBy(q => q.EntityType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Type, g => g.Count);
        stats.EntityTypeCounts = typeCounts;

        // Get oldest pending item
        var oldestPending = await _dbSet
            .Where(q => q.Status == "Pending")
            .OrderBy(q => q.CreatedAt)
            .FirstOrDefaultAsync();
        stats.OldestPendingItem = oldestPending?.CreatedAt;

        // Get last successful sync
        var lastSuccess = await _dbSet
            .Where(q => q.Status == "Completed")
            .OrderByDescending(q => q.UpdatedAt)
            .FirstOrDefaultAsync();
        stats.LastSuccessfulSync = lastSuccess?.UpdatedAt;

        return stats;
    }

    public async Task<int> CleanupAsync()
    {
        var deletedCount = 0;

        // Delete completed items older than 1 day
        deletedCount += await _dbSet
            .Where(q => q.Status == "Completed" && q.UpdatedAt < DateTime.UtcNow.AddDays(-1))
            .ExecuteDeleteAsync();

        // Delete expired items
        deletedCount += await _dbSet
            .Where(q => q.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync();

        return deletedCount;
    }

    private string GetEntityId(object entity)
    {
        var idProperty = entity.GetType().GetProperty("Id");
        return idProperty?.GetValue(entity)?.ToString() ?? Guid.NewGuid().ToString();
    }

    private string? GetCameraId(object entity)
    {
        var cameraIdProperty = entity.GetType().GetProperty("CameraId");
        return cameraIdProperty?.GetValue(entity)?.ToString();
    }

    private int GetPriority(string entityType)
    {
        return entityType switch
        {
            "KeyFrame" => 50,     // Higher priority
            "Detection" => 100,   // Normal priority
            "Metric" => 150,      // Lower priority
            _ => 100
        };
    }
}