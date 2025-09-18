using System.ComponentModel.DataAnnotations;

namespace VisionOps.Data.Entities;

/// <summary>
/// Represents a key frame with Florence-2 description
/// </summary>
public class KeyFrameEntity
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
    /// Frame capture timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Compressed image data (WebP format, 3-5KB)
    /// </summary>
    public byte[] CompressedImage { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Image compression quality (0-100)
    /// </summary>
    public int CompressionQuality { get; set; } = 20;

    /// <summary>
    /// Compressed image size in bytes
    /// </summary>
    public int ImageSizeBytes { get; set; }

    /// <summary>
    /// Florence-2 generated description
    /// </summary>
    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Number of people detected
    /// </summary>
    public int PeopleCount { get; set; }

    /// <summary>
    /// Number of vehicles detected
    /// </summary>
    public int VehicleCount { get; set; }

    /// <summary>
    /// Other objects detected (JSON)
    /// </summary>
    public string? OtherObjectsJson { get; set; }

    /// <summary>
    /// Scene attributes (indoor/outdoor, lighting, etc.) as JSON
    /// </summary>
    public string? SceneAttributesJson { get; set; }

    /// <summary>
    /// Florence-2 processing time in milliseconds
    /// </summary>
    public int DescriptionProcessingTimeMs { get; set; }

    /// <summary>
    /// Whether this has been synced to cloud
    /// </summary>
    public bool IsSynced { get; set; }

    /// <summary>
    /// Cloud sync timestamp
    /// </summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>
    /// Frame number in sequence
    /// </summary>
    public long FrameNumber { get; set; }

    /// <summary>
    /// Original frame width
    /// </summary>
    public int OriginalWidth { get; set; } = 640;

    /// <summary>
    /// Original frame height
    /// </summary>
    public int OriginalHeight { get; set; } = 480;

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual CameraEntity? Camera { get; set; }
    public virtual ICollection<DetectionEntity> Detections { get; set; } = new List<DetectionEntity>();
}