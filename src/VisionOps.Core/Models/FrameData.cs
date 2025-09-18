namespace VisionOps.Core.Models;

/// <summary>
/// Represents a processed video frame with metadata
/// </summary>
public class FrameData
{
    /// <summary>
    /// Unique identifier for this frame
    /// </summary>
    public string FrameId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Camera that captured this frame
    /// </summary>
    public string CameraId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the frame was captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Frame number in the sequence
    /// </summary>
    public long FrameNumber { get; set; }

    /// <summary>
    /// Whether this is a key frame for Florence-2 processing
    /// </summary>
    public bool IsKeyFrame { get; set; }

    /// <summary>
    /// Compressed frame data (WebP format)
    /// </summary>
    public byte[]? CompressedData { get; set; }

    /// <summary>
    /// Size of compressed data in bytes
    /// </summary>
    public int CompressedSize => CompressedData?.Length ?? 0;

    /// <summary>
    /// Original frame dimensions
    /// </summary>
    public FrameDimensions Dimensions { get; set; } = new();

    /// <summary>
    /// Florence-2 description (for key frames only)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public int ProcessingTimeMs { get; set; }

    /// <summary>
    /// Object detections from YOLOv8 (if processed)
    /// </summary>
    public List<Detection> Detections { get; set; } = new();
}

/// <summary>
/// Frame dimension information
/// </summary>
public class FrameDimensions
{
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;
    public int Channels { get; set; } = 3;
}

