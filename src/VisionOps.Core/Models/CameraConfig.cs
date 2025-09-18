namespace VisionOps.Core.Models;

/// <summary>
/// Camera configuration for RTSP stream processing
/// </summary>
public class CameraConfig
{
    /// <summary>
    /// Unique identifier for the camera
    /// </summary>
    public string CameraId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for the camera
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// RTSP URL for the camera stream
    /// </summary>
    public string RtspUrl { get; set; } = string.Empty;

    /// <summary>
    /// Camera location/zone identifier
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Whether this camera is currently enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Connection retry attempts before marking as failed
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Timeout in seconds for connection attempts
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Last successful connection time
    /// </summary>
    public DateTime? LastConnected { get; set; }

    /// <summary>
    /// Current connection status
    /// </summary>
    public CameraStatus Status { get; set; } = CameraStatus.Disconnected;

    /// <summary>
    /// Optional username for RTSP authentication
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Optional password for RTSP authentication
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Frame processing interval in milliseconds (default: 3000ms = 3 seconds)
    /// </summary>
    public int FrameIntervalMs { get; set; } = 3000;

    /// <summary>
    /// Key frame extraction interval (every N frames)
    /// </summary>
    public int KeyFrameInterval { get; set; } = 10;

    /// <summary>
    /// Get the authenticated RTSP URL
    /// </summary>
    public string GetAuthenticatedUrl()
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            return RtspUrl;

        var uri = new UriBuilder(RtspUrl);
        uri.UserName = Username;
        uri.Password = Password;
        return uri.ToString();
    }
}

/// <summary>
/// Camera connection status
/// </summary>
public enum CameraStatus
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
    Reconnecting
}