using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace VisionOps.Video.Memory;

/// <summary>
/// CRITICAL Phase 0 Component: Lock-free circular buffer for frame management.
/// Ensures we never exceed 30 frames in memory per camera (production constraint).
/// Automatic disposal of old frames prevents memory accumulation.
/// </summary>
public sealed class CircularFrameBuffer : IDisposable
{
    private readonly string _cameraId;
    private readonly FrameBufferPool _bufferPool;
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<TimestampedFrame> _frames;
    private readonly SemaphoreSlim _frameSemaphore;
    private readonly Timer _cleanupTimer;
    private readonly int _maxFrames;

    // Tracking
    private long _totalFramesProcessed;
    private long _totalFramesDropped;
    private long _currentMemoryUsage;
    private DateTime _lastCleanupTime;
    private readonly object _statsLock = new();
    private bool _disposed;

    // Constants
    private const int CleanupIntervalMs = 30000; // 30 seconds
    private const int FrameAgeThresholdMs = 10000; // Drop frames older than 10 seconds

    public CircularFrameBuffer(
        string cameraId,
        FrameBufferPool bufferPool,
        ILogger logger,
        int maxFrames = 30)
    {
        _cameraId = cameraId ?? throw new ArgumentNullException(nameof(cameraId));
        _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxFrames = Math.Min(maxFrames, 30); // Hard limit of 30 frames

        _frames = new ConcurrentQueue<TimestampedFrame>();
        _frameSemaphore = new SemaphoreSlim(0, _maxFrames);
        _lastCleanupTime = DateTime.UtcNow;

        // Start periodic cleanup timer
        _cleanupTimer = new Timer(
            CleanupOldFrames,
            null,
            CleanupIntervalMs,
            CleanupIntervalMs);

        _logger.LogInformation(
            "CircularFrameBuffer created for camera {CameraId} with max {MaxFrames} frames",
            _cameraId, _maxFrames);
    }

    /// <summary>
    /// Add a frame to the buffer. Oldest frame is automatically dropped if full.
    /// Uses memory pool for efficient allocation.
    /// </summary>
    public async Task<bool> AddFrameAsync(Mat frame, CancellationToken cancellationToken = default)
    {
        if (_disposed || frame == null || frame.Empty())
            return false;

        try
        {
            // Check if buffer is full
            while (_frames.Count >= _maxFrames)
            {
                if (_frames.TryDequeue(out var oldFrame))
                {
                    oldFrame.Dispose();
                    lock (_statsLock)
                    {
                        _totalFramesDropped++;
                    }

                    if (_totalFramesDropped % 100 == 0)
                    {
                        _logger.LogDebug(
                            "Dropped {Count} frames for camera {CameraId}",
                            _totalFramesDropped, _cameraId);
                    }
                }
            }

            // Use buffer pool for frame data
            var frameSize = frame.Width * frame.Height * frame.Channels();
            var buffer = _bufferPool.RentBuffer(frameSize);

            try
            {
                // Copy frame data to pooled buffer
                unsafe
                {
                    var srcPtr = (byte*)frame.Data.ToPointer();
                    fixed (byte* dstPtr = buffer)
                    {
                        Buffer.MemoryCopy(srcPtr, dstPtr, frameSize, frameSize);
                    }
                }

                var timestampedFrame = new TimestampedFrame(
                    buffer,
                    frame.Width,
                    frame.Height,
                    frame.Type(),
                    DateTime.UtcNow,
                    _bufferPool);

                _frames.Enqueue(timestampedFrame);
                _frameSemaphore.Release();

                lock (_statsLock)
                {
                    _totalFramesProcessed++;
                    _currentMemoryUsage = _frames.Count * frameSize;
                }

                return true;
            }
            catch
            {
                // Return buffer on error
                _bufferPool.ReturnBuffer(buffer);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding frame to buffer for camera {CameraId}", _cameraId);
            return false;
        }
    }

    /// <summary>
    /// Get the next frame from the buffer with age checking
    /// </summary>
    public async Task<TimestampedFrame?> GetFrameAsync(
        int timeoutMs = 1000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Wait for frame availability
            if (!await _frameSemaphore.WaitAsync(timeoutMs, cancellationToken))
            {
                return null; // Timeout
            }

            if (_frames.TryDequeue(out var frame))
            {
                // Check frame age
                var age = DateTime.UtcNow - frame.Timestamp;
                if (age.TotalMilliseconds > FrameAgeThresholdMs)
                {
                    _logger.LogDebug(
                        "Skipping stale frame for camera {CameraId}, age: {Age}ms",
                        _cameraId, age.TotalMilliseconds);
                    frame.Dispose();
                    return null;
                }

                return frame;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting frame from buffer for camera {CameraId}", _cameraId);
        }

        return null;
    }

    /// <summary>
    /// Clean up old frames to prevent memory accumulation
    /// </summary>
    private void CleanupOldFrames(object state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var cleanedCount = 0;
            var freshFrames = new List<TimestampedFrame>();

            // Process all frames
            while (_frames.TryDequeue(out var frame))
            {
                var age = now - frame.Timestamp;
                if (age.TotalMilliseconds > FrameAgeThresholdMs)
                {
                    frame.Dispose();
                    cleanedCount++;

                    lock (_statsLock)
                    {
                        _totalFramesDropped++;
                    }
                }
                else
                {
                    freshFrames.Add(frame);
                }
            }

            // Re-add fresh frames
            foreach (var frame in freshFrames)
            {
                _frames.Enqueue(frame);
            }

            if (cleanedCount > 0)
            {
                _logger.LogInformation(
                    "Cleaned {Count} old frames from buffer for camera {CameraId}",
                    cleanedCount, _cameraId);
            }

            _lastCleanupTime = now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during frame cleanup for camera {CameraId}", _cameraId);
        }
    }

    /// <summary>
    /// Get buffer statistics
    /// </summary>
    public CircularBufferStats GetStatistics()
    {
        lock (_statsLock)
        {
            return new CircularBufferStats
            {
                CameraId = _cameraId,
                CurrentFrameCount = _frames.Count,
                MaxFrames = _maxFrames,
                TotalFramesProcessed = _totalFramesProcessed,
                TotalFramesDropped = _totalFramesDropped,
                CurrentMemoryUsageMB = _currentMemoryUsage / (1024.0 * 1024.0),
                LastCleanupTime = _lastCleanupTime,
                BufferUtilization = (_frames.Count / (double)_maxFrames) * 100,
                DropRate = _totalFramesProcessed > 0
                    ? (_totalFramesDropped / (double)_totalFramesProcessed) * 100
                    : 0
            };
        }
    }

    /// <summary>
    /// Clear all frames from the buffer
    /// </summary>
    public void Clear()
    {
        while (_frames.TryDequeue(out var frame))
        {
            frame.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();
        Clear();
        _frameSemaphore?.Dispose();
        _disposed = true;

        _logger.LogInformation(
            "CircularFrameBuffer disposed for camera {CameraId}. Processed: {Processed}, Dropped: {Dropped}",
            _cameraId, _totalFramesProcessed, _totalFramesDropped);
    }
}

/// <summary>
/// Circular buffer statistics
/// </summary>
public class CircularBufferStats
{
    public string CameraId { get; init; } = string.Empty;
    public int CurrentFrameCount { get; init; }
    public int MaxFrames { get; init; }
    public long TotalFramesProcessed { get; init; }
    public long TotalFramesDropped { get; init; }
    public double CurrentMemoryUsageMB { get; init; }
    public DateTime LastCleanupTime { get; init; }
    public double BufferUtilization { get; init; }
    public double DropRate { get; init; }
}

// Legacy compatibility
public class BufferStatistics : CircularBufferStats
{
    // Maps to CircularBufferStats for compatibility
    public long TotalFramesAdded => TotalFramesProcessed;
}
