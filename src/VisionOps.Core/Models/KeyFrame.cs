namespace VisionOps.Core.Models;

/// <summary>
/// Represents a key frame with Florence-2 description for long-term storage
/// </summary>
public class KeyFrame
{
    /// <summary>
    /// Unique identifier for the key frame
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Camera ID that captured this frame
    /// </summary>
    public string CameraId { get; set; } = string.Empty;

    /// <summary>
    /// Frame number in the video stream
    /// </summary>
    public int FrameNumber { get; set; }

    /// <summary>
    /// Timestamp when the frame was captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// WebP compressed image data (Q=20, 320x240, ~3-5KB)
    /// </summary>
    public byte[] CompressedImage { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Florence-2 generated natural language description (~200 bytes)
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional embeddings from Florence-2 for semantic search (pgvector)
    /// </summary>
    public float[]? Embeddings { get; set; }

    /// <summary>
    /// Number of people detected in the frame
    /// </summary>
    public int PeopleCount { get; set; }

    /// <summary>
    /// List of detected object classes in the frame
    /// </summary>
    public List<string> DetectedObjects { get; set; } = new();

    /// <summary>
    /// Processing time in milliseconds for description generation
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Whether this frame has been synced to cloud
    /// </summary>
    public bool IsSynced { get; set; }

    /// <summary>
    /// Last sync attempt timestamp
    /// </summary>
    public DateTime? LastSyncAttempt { get; set; }

    /// <summary>
    /// Location identifier for multi-site deployments
    /// </summary>
    public string LocationId { get; set; } = string.Empty;

    /// <summary>
    /// Calculate storage size in bytes
    /// </summary>
    public int GetStorageSize()
    {
        int size = CompressedImage.Length;
        size += System.Text.Encoding.UTF8.GetByteCount(Description);
        size += System.Text.Encoding.UTF8.GetByteCount(CameraId);
        size += System.Text.Encoding.UTF8.GetByteCount(LocationId);
        if (Embeddings != null)
            size += Embeddings.Length * sizeof(float);
        return size;
    }
}

/// <summary>
/// Configuration for Florence-2 processing
/// </summary>
public class Florence2Config
{
    /// <summary>
    /// Model path for ONNX runtime
    /// </summary>
    public string ModelPath { get; set; } = "models/florence2-base.onnx";

    /// <summary>
    /// Tokenizer vocabulary path
    /// </summary>
    public string TokenizerPath { get; set; } = "models/florence2-vocab.json";

    /// <summary>
    /// Maximum description length in tokens
    /// </summary>
    public int MaxTokens { get; set; } = 64;

    /// <summary>
    /// Image size for Florence-2 input (must be 384x384)
    /// </summary>
    public int ImageSize { get; set; } = 384;

    /// <summary>
    /// Key frame interval in seconds (default: every 10 seconds)
    /// </summary>
    public int KeyFrameIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// WebP compression quality (1-100, lower = smaller file)
    /// </summary>
    public int CompressionQuality { get; set; } = 20;

    /// <summary>
    /// Thumbnail size for compressed storage
    /// </summary>
    public (int Width, int Height) ThumbnailSize { get; set; } = (320, 240);

    /// <summary>
    /// Generate embeddings for semantic search
    /// </summary>
    public bool GenerateEmbeddings { get; set; } = true;

    /// <summary>
    /// Batch size for Florence-2 processing (1-4 recommended)
    /// </summary>
    public int BatchSize { get; set; } = 1;
}