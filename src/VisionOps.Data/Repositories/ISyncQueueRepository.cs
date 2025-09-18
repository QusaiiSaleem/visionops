using VisionOps.Data.Entities;

namespace VisionOps.Data.Repositories;

/// <summary>
/// Repository interface for sync queue operations
/// </summary>
public interface ISyncQueueRepository : IRepository<SyncQueueEntity>
{
    /// <summary>
    /// Get next batch of items to sync
    /// </summary>
    Task<IEnumerable<SyncQueueEntity>> GetPendingBatchAsync(int batchSize = 100);

    /// <summary>
    /// Add items to sync queue
    /// </summary>
    Task<Guid> EnqueueBatchAsync<T>(IEnumerable<T> entities, string entityType, string operation = "Create") where T : class;

    /// <summary>
    /// Mark items as successfully synced
    /// </summary>
    Task MarkAsCompletedAsync(IEnumerable<Guid> queueIds);

    /// <summary>
    /// Mark item as failed with error
    /// </summary>
    Task MarkAsFailedAsync(Guid queueId, string error);

    /// <summary>
    /// Reset failed items for retry
    /// </summary>
    Task<int> ResetFailedItemsAsync();

    /// <summary>
    /// Get sync queue statistics
    /// </summary>
    Task<SyncQueueStats> GetStatsAsync();

    /// <summary>
    /// Clean up completed and expired items
    /// </summary>
    Task<int> CleanupAsync();
}

/// <summary>
/// Sync queue statistics
/// </summary>
public class SyncQueueStats
{
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int FailedCount { get; set; }
    public int CompletedCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public Dictionary<string, int> EntityTypeCounts { get; set; } = new();
    public DateTime? OldestPendingItem { get; set; }
    public DateTime? LastSuccessfulSync { get; set; }
}