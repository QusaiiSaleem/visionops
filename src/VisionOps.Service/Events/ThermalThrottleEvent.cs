namespace VisionOps.Service.Events;

/// <summary>
/// Event raised when thermal throttling state changes
/// </summary>
public sealed class ThermalThrottleEvent
{
    public bool IsThrottled { get; init; }
    public int Temperature { get; init; }
    public int ThrottleDelayMs { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Reason { get; init; } = string.Empty;

    public static ThermalThrottleEvent StartThrottle(int temperature, int delayMs) => new()
    {
        IsThrottled = true,
        Temperature = temperature,
        ThrottleDelayMs = delayMs,
        Reason = $"CPU temperature {temperature}°C exceeds threshold"
    };

    public static ThermalThrottleEvent StopThrottle(int temperature) => new()
    {
        IsThrottled = false,
        Temperature = temperature,
        ThrottleDelayMs = 0,
        Reason = $"CPU temperature {temperature}°C returned to normal"
    };

    public static ThermalThrottleEvent EmergencyShutdown(int temperature) => new()
    {
        IsThrottled = true,
        Temperature = temperature,
        ThrottleDelayMs = int.MaxValue,
        Reason = $"CRITICAL: CPU temperature {temperature}°C - Emergency shutdown"
    };
}