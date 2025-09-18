namespace VisionOps.Core.Models;

/// <summary>
/// Represents a detected object from YOLOv8 or other detection models
/// </summary>
public class Detection
{
    /// <summary>
    /// Class ID of the detected object
    /// </summary>
    public int ClassId { get; set; }

    /// <summary>
    /// Human-readable label for the detected object (e.g., "person", "car")
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0-1)
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Bounding box X coordinate (normalized 0-1)
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Bounding box Y coordinate (normalized 0-1)
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// Bounding box width (normalized 0-1)
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Bounding box height (normalized 0-1)
    /// </summary>
    public float Height { get; set; }

    /// <summary>
    /// Optional tracking ID for object tracking across frames
    /// </summary>
    public int? TrackingId { get; set; }

    /// <summary>
    /// Frame number where this detection occurred
    /// </summary>
    public int FrameNumber { get; set; }

    /// <summary>
    /// Camera ID that captured this detection
    /// </summary>
    public string CameraId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the detection
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Convert normalized coordinates to pixel coordinates
    /// </summary>
    public (int x, int y, int width, int height) ToPixelCoordinates(int imageWidth, int imageHeight)
    {
        return (
            (int)(X * imageWidth),
            (int)(Y * imageHeight),
            (int)(Width * imageWidth),
            (int)(Height * imageHeight)
        );
    }

    /// <summary>
    /// Calculate IoU (Intersection over Union) with another detection
    /// </summary>
    public float CalculateIoU(Detection other)
    {
        float x1 = Math.Max(X, other.X);
        float y1 = Math.Max(Y, other.Y);
        float x2 = Math.Min(X + Width, other.X + other.Width);
        float y2 = Math.Min(Y + Height, other.Y + other.Height);

        if (x2 < x1 || y2 < y1)
            return 0;

        float intersection = (x2 - x1) * (y2 - y1);
        float area1 = Width * Height;
        float area2 = other.Width * other.Height;
        float union = area1 + area2 - intersection;

        return intersection / union;
    }
}

/// <summary>
/// Batch detection result from processing multiple frames
/// </summary>
public class BatchDetectionResult
{
    /// <summary>
    /// List of detections from the batch
    /// </summary>
    public List<Detection> Detections { get; set; } = new();

    /// <summary>
    /// Number of frames processed in this batch
    /// </summary>
    public int BatchSize { get; set; }

    /// <summary>
    /// Total inference time in milliseconds
    /// </summary>
    public double InferenceTimeMs { get; set; }

    /// <summary>
    /// Average inference time per frame
    /// </summary>
    public double PerFrameInferenceMs => BatchSize > 0 ? InferenceTimeMs / BatchSize : 0;

    /// <summary>
    /// Model name used for detection
    /// </summary>
    public string ModelName { get; set; } = "yolov8n";

    /// <summary>
    /// Whether INT8 quantization was used
    /// </summary>
    public bool IsQuantized { get; set; } = true;
}