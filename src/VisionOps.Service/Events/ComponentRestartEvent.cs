namespace VisionOps.Service.Events;

/// <summary>
/// Event raised when components need to restart due to recovery
/// </summary>
public sealed class ComponentRestartEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Reason { get; init; } = "Watchdog recovery";
    public bool IsGraceful { get; init; } = true;
}