using System.ComponentModel.DataAnnotations;

namespace VisionOps.Data.Entities;

/// <summary>
/// Represents an object detection result in the database
/// </summary>
public class DetectionEntity
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
    /// Detection timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Object class (person, vehicle, etc.)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// Detection confidence (0-1)
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Bounding box X coordinate
    /// </summary>
    public int BboxX { get; set; }

    /// <summary>
    /// Bounding box Y coordinate
    /// </summary>
    public int BboxY { get; set; }

    /// <summary>
    /// Bounding box width
    /// </summary>
    public int BboxWidth { get; set; }

    /// <summary>
    /// Bounding box height
    /// </summary>
    public int BboxHeight { get; set; }

    /// <summary>
    /// Frame number in sequence
    /// </summary>
    public long FrameNumber { get; set; }

    /// <summary>
    /// Zone name where detection occurred
    /// </summary>
    [MaxLength(100)]
    public string? ZoneName { get; set; }

    /// <summary>
    /// Whether this has been synced to cloud
    /// </summary>
    public bool IsSynced { get; set; }

    /// <summary>
    /// Cloud sync timestamp
    /// </summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public int ProcessingTimeMs { get; set; }

    /// <summary>
    /// Associated key frame ID if this is a key frame detection
    /// </summary>
    public Guid? KeyFrameId { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual CameraEntity? Camera { get; set; }
    public virtual KeyFrameEntity? KeyFrame { get; set; }
}