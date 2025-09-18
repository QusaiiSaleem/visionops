using System.ComponentModel.DataAnnotations;

namespace VisionOps.Data.Entities;

/// <summary>
/// Represents aggregated metrics for a time window (5-minute aggregation)
/// </summary>
public class MetricEntity
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Associated camera ID
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string CameraId { get; set; } = string.Empty;

    /// <summary>
    /// Start of the aggregation window
    /// </summary>
    public DateTime WindowStart { get; set; }

    /// <summary>
    /// End of the aggregation window
    /// </summary>
    public DateTime WindowEnd { get; set; }

    /// <summary>
    /// Window duration in seconds (typically 300 for 5 minutes)
    /// </summary>
    public int WindowDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Number of samples in this window
    /// </summary>
    public int SampleCount { get; set; }

    /// <summary>
    /// Average number of people detected
    /// </summary>
    public float AvgPeopleCount { get; set; }

    /// <summary>
    /// Maximum number of people detected
    /// </summary>
    public int MaxPeopleCount { get; set; }

    /// <summary>
    /// Minimum number of people detected
    /// </summary>
    public int MinPeopleCount { get; set; }

    /// <summary>
    /// Average number of vehicles detected
    /// </summary>
    public float AvgVehicleCount { get; set; }

    /// <summary>
    /// Maximum number of vehicles detected
    /// </summary>
    public int MaxVehicleCount { get; set; }

    /// <summary>
    /// Total number of detections
    /// </summary>
    public int TotalDetections { get; set; }

    /// <summary>
    /// Average processing time in milliseconds
    /// </summary>
    public float AvgProcessingTimeMs { get; set; }

    /// <summary>
    /// P95 processing time in milliseconds
    /// </summary>
    public float P95ProcessingTimeMs { get; set; }

    /// <summary>
    /// Maximum processing time in milliseconds
    /// </summary>
    public float MaxProcessingTimeMs { get; set; }

    /// <summary>
    /// Number of frames processed
    /// </summary>
    public int FramesProcessed { get; set; }

    /// <summary>
    /// Number of key frames processed
    /// </summary>
    public int KeyFramesProcessed { get; set; }

    /// <summary>
    /// Number of errors in this window
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Average CPU usage percentage
    /// </summary>
    public float AvgCpuUsage { get; set; }

    /// <summary>
    /// Maximum CPU temperature in Celsius
    /// </summary>
    public float MaxCpuTemperature { get; set; }

    /// <summary>
    /// Average memory usage in MB
    /// </summary>
    public float AvgMemoryUsageMb { get; set; }

    /// <summary>
    /// Compressed raw data (Brotli compressed JSON)
    /// </summary>
    public byte[]? CompressedRawData { get; set; }

    /// <summary>
    /// Size of compressed data in bytes
    /// </summary>
    public int CompressedSizeBytes { get; set; }

    /// <summary>
    /// Compression ratio (original/compressed)
    /// </summary>
    public float CompressionRatio { get; set; }

    /// <summary>
    /// Zone-based statistics (JSON)
    /// </summary>
    public string? ZoneStatsJson { get; set; }

    /// <summary>
    /// Whether this has been synced to cloud
    /// </summary>
    public bool IsSynced { get; set; }

    /// <summary>
    /// Cloud sync timestamp
    /// </summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual CameraEntity? Camera { get; set; }
}