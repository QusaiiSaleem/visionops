namespace VisionOps.Service.Events;

/// <summary>
/// Event raised when the service starts
/// </summary>
public sealed class ServiceStartedEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int RestartCount { get; init; }
    public TimeSpan PreviousUptime { get; init; }
    public bool IsRecovery => RestartCount > 0;
}