using System.ComponentModel.DataAnnotations;

namespace VisionOps.Data.Entities;

/// <summary>
/// Represents a pending sync operation in the queue
/// </summary>
public class SyncQueueEntity
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Type of entity to sync (Detection, KeyFrame, Metric)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Entity ID to sync
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Operation type (Create, Update, Delete)
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Operation { get; set; } = "Create";

    /// <summary>
    /// Serialized entity data (JSON)
    /// </summary>
    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// Payload size in bytes
    /// </summary>
    public int PayloadSizeBytes { get; set; }

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum retry attempts allowed
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Last error message if failed
    /// </summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>
    /// Last attempt timestamp
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// Next retry timestamp (exponential backoff)
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Sync status
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Priority (lower number = higher priority)
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Associated camera ID
    /// </summary>
    [MaxLength(100)]
    public string? CameraId { get; set; }

    /// <summary>
    /// Batch ID for grouping related operations
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Record update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Expiry timestamp (for cleanup)
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
}