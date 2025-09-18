namespace VisionOps.Service.Events;

/// <summary>
/// Event raised when memory usage approaches critical thresholds
/// </summary>
public sealed class MemoryPressureEvent
{
    public long MemoryUsageMB { get; init; }
    public long Threshold { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public MemoryPressureLevel Level => MemoryUsageMB switch
    {
        >= 5500 => MemoryPressureLevel.Critical,
        >= 5000 => MemoryPressureLevel.High,
        >= 4000 => MemoryPressureLevel.Medium,
        _ => MemoryPressureLevel.Low
    };
}

public enum MemoryPressureLevel
{
    Low,
    Medium,
    High,
    Critical
}