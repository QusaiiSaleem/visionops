using OpenCvSharp;

namespace VisionOps.Video.Memory;

/// <summary>
/// Represents a video frame with timing metadata
/// </summary>
public class TimestampedFrame : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Camera ID that captured this frame
    /// </summary>
    public string CameraId { get; }

    /// <summary>
    /// The actual frame data (OpenCV Mat)
    /// </summary>
    public Mat Frame { get; }

    /// <summary>
    /// Timestamp when the frame was captured
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Frame sequence number
    /// </summary>
    public long FrameNumber { get; }

    /// <summary>
    /// Whether this is a key frame for Florence-2 processing
    /// </summary>
    public bool IsKeyFrame => FrameNumber % 10 == 0;

    public TimestampedFrame(
        string cameraId,
        Mat frame,
        DateTime timestamp,
        long frameNumber)
    {
        CameraId = cameraId ?? throw new ArgumentNullException(nameof(cameraId));
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        Timestamp = timestamp;
        FrameNumber = frameNumber;
    }

    /// <summary>
    /// Clone this frame with a new Mat copy
    /// </summary>
    public TimestampedFrame Clone()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TimestampedFrame));

        return new TimestampedFrame(
            CameraId,
            Frame.Clone(),
            Timestamp,
            FrameNumber);
    }

    /// <summary>
    /// Convert frame to byte array
    /// </summary>
    public byte[] ToByteArray()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TimestampedFrame));

        var bytes = new byte[Frame.Total() * Frame.Channels()];
        System.Runtime.InteropServices.Marshal.Copy(
            Frame.Data, bytes, 0, bytes.Length);
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed) return;

        Frame?.Dispose();
        _disposed = true;
    }
}
