namespace VisionOps.Core.Models;

/// <summary>
/// System performance metrics for monitoring and health checks
/// </summary>
public class SystemMetrics
{
    /// <summary>
    /// CPU usage percentage. Target: <60% average
    /// </summary>
    public float CpuUsage { get; set; }

    /// <summary>
    /// Memory usage in megabytes. Target: <6000MB
    /// </summary>
    public long MemoryUsageMB { get; set; }

    /// <summary>
    /// CPU temperature in Celsius. Target: <70°C
    /// </summary>
    public int CpuTemperature { get; set; }

    /// <summary>
    /// Total frames processed across all cameras
    /// </summary>
    public int FramesProcessed { get; set; }

    /// <summary>
    /// AI inference latency in milliseconds. Target: <200ms
    /// </summary>
    public int InferenceLatencyMs { get; set; }

    /// <summary>
    /// Size of the synchronization queue. Warning: >1000
    /// </summary>
    public int SyncQueueSize { get; set; }

    /// <summary>
    /// Number of active cameras. Max: 5
    /// </summary>
    public int ActiveCameras { get; set; }

    /// <summary>
    /// Timestamp of the metrics collection
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}