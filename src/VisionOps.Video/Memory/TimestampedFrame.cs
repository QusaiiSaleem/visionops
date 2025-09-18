using OpenCvSharp;
using VisionOps.Video.Memory;

namespace VisionOps.Video.Memory;

/// <summary>
/// Represents a video frame with timing metadata
/// </summary>
public class TimestampedFrame : IDisposable
{
    private readonly byte[] _data;
    private readonly FrameBufferPool? _bufferPool;
    private bool _disposed;

    /// <summary>
    /// Frame width
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Frame height
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Frame type (OpenCV MatType)
    /// </summary>
    public MatType Type { get; }

    /// <summary>
    /// Timestamp when the frame was captured
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Camera ID that captured this frame (optional)
    /// </summary>
    public string? CameraId { get; init; }

    /// <summary>
    /// Frame sequence number (optional)
    /// </summary>
    public long? FrameNumber { get; init; }

    /// <summary>
    /// Whether this is a key frame for Florence-2 processing
    /// </summary>
    public bool IsKeyFrame => FrameNumber.HasValue && FrameNumber.Value % 10 == 0;

    /// <summary>
    /// Age of the frame
    /// </summary>
    public TimeSpan Age => DateTime.UtcNow - Timestamp;

    /// <summary>
    /// Get raw data reference (do not modify)
    /// </summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>
    /// Constructor used by CircularFrameBuffer
    /// </summary>
    internal TimestampedFrame(
        byte[] data,
        int width,
        int height,
        MatType type,
        DateTime timestamp,
        FrameBufferPool? bufferPool)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _bufferPool = bufferPool;
        Width = width;
        Height = height;
        Type = type;
        Timestamp = timestamp;
    }

    /// <summary>
    /// Constructor for general use with Mat and camera info
    /// </summary>
    public TimestampedFrame(
        string cameraId,
        Mat frame,
        DateTime timestamp,
        long frameNumber)
    {
        CameraId = cameraId ?? throw new ArgumentNullException(nameof(cameraId));
        if (frame == null) throw new ArgumentNullException(nameof(frame));

        Width = frame.Width;
        Height = frame.Height;
        Type = frame.Type();
        Timestamp = timestamp;
        FrameNumber = frameNumber;

        // Copy frame data to internal buffer
        _data = new byte[frame.Total() * frame.Channels()];
        System.Runtime.InteropServices.Marshal.Copy(
            frame.Data, _data, 0, _data.Length);
    }

    /// <summary>
    /// Convert to Mat for processing. Mat should be disposed after use.
    /// </summary>
    public Mat ToMat()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TimestampedFrame));

        return new Mat(Height, Width, Type, _data);
    }

    /// <summary>
    /// Clone this frame with a new Mat copy
    /// </summary>
    public TimestampedFrame Clone()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TimestampedFrame));

        var clonedData = new byte[_data.Length];
        Array.Copy(_data, clonedData, _data.Length);

        return new TimestampedFrame(
            clonedData,
            Width,
            Height,
            Type,
            Timestamp,
            null)
        {
            CameraId = this.CameraId,
            FrameNumber = this.FrameNumber
        };
    }

    /// <summary>
    /// Convert frame to byte array
    /// </summary>
    public byte[] ToByteArray()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TimestampedFrame));

        var result = new byte[_data.Length];
        Array.Copy(_data, result, _data.Length);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Return buffer to pool if it came from a pool
        _bufferPool?.ReturnBuffer(_data);
        _disposed = true;
    }
}