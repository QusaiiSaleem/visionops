using System.ComponentModel.DataAnnotations;

namespace VisionOps.Data.Entities;

/// <summary>
/// Represents a camera configuration in the database
/// </summary>
public class CameraEntity
{
    /// <summary>
    /// Unique identifier for the camera
    /// </summary>
    [Key]
    [MaxLength(100)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the camera
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// RTSP stream URL
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string RtspUrl { get; set; } = string.Empty;

    /// <summary>
    /// Sub-stream URL for lower quality preview
    /// </summary>
    [MaxLength(500)]
    public string? SubStreamUrl { get; set; }

    /// <summary>
    /// Camera location description
    /// </summary>
    [MaxLength(200)]
    public string? Location { get; set; }

    /// <summary>
    /// Whether the camera is enabled for processing
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether Florence-2 descriptions are enabled
    /// </summary>
    public bool EnableDescriptions { get; set; } = true;

    /// <summary>
    /// Frame processing interval in seconds
    /// </summary>
    public int FrameIntervalSeconds { get; set; } = 3;

    /// <summary>
    /// Key frame interval for Florence-2 (every N frames)
    /// </summary>
    public int KeyFrameInterval { get; set; } = 10;

    /// <summary>
    /// Detection zones as JSON
    /// </summary>
    public string? DetectionZonesJson { get; set; }

    /// <summary>
    /// Last successful connection timestamp
    /// </summary>
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>
    /// Last error message if any
    /// </summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>
    /// Connection retry count
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Record update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<DetectionEntity> Detections { get; set; } = new List<DetectionEntity>();
    public virtual ICollection<KeyFrameEntity> KeyFrames { get; set; } = new List<KeyFrameEntity>();
    public virtual ICollection<MetricEntity> Metrics { get; set; } = new List<MetricEntity>();
}