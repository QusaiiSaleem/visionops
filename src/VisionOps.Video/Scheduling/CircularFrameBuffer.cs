using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionOps.Core.Models;

namespace VisionOps.Video.Scheduling;

/// <summary>
/// Circular buffer for managing frame memory with automatic eviction.
/// CRITICAL: Maximum 30 frames in memory at any time.
/// </summary>
public class CircularFrameBuffer : IDisposable
{
    private readonly object _lock = new();
    private readonly LinkedList<FrameData> _frames = new();
    private readonly Dictionary<string, LinkedListNode<FrameData>> _frameIndex = new();
    private readonly int _maxFrames;
    private bool _disposed;

    public CircularFrameBuffer(int maxFrames = 30)
    {
        if (maxFrames <= 0 || maxFrames > 30)
            throw new ArgumentException("Max frames must be between 1 and 30", nameof(maxFrames));

        _maxFrames = maxFrames;
    }

    /// <summary>
    /// Add a frame to the buffer, automatically removing oldest if at capacity
    /// </summary>
    public void AddFrame(FrameData frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));

        lock (_lock)
        {
            // If at capacity, remove oldest frame
            if (_frames.Count >= _maxFrames)
            {
                RemoveOldestFrame();
            }

            // Add new frame
            var node = _frames.AddLast(frame);
            _frameIndex[frame.FrameId] = node;
        }
    }

    /// <summary>
    /// Get a frame by ID
    /// </summary>
    public FrameData? GetFrame(string frameId)
    {
        lock (_lock)
        {
            if (_frameIndex.TryGetValue(frameId, out var node))
            {
                // Move to end (most recently accessed)
                _frames.Remove(node);
                _frames.AddLast(node);
                return node.Value;
            }
            return null;
        }
    }

    /// <summary>
    /// Get all frames for a specific camera
    /// </summary>
    public List<FrameData> GetCameraFrames(string cameraId)
    {
        lock (_lock)
        {
            return _frames
                .Where(f => f.CameraId == cameraId)
                .ToList();
        }
    }

    /// <summary>
    /// Get the most recent key frame for a camera
    /// </summary>
    public FrameData? GetLatestKeyFrame(string cameraId)
    {
        lock (_lock)
        {
            return _frames
                .Where(f => f.CameraId == cameraId && f.IsKeyFrame)
                .LastOrDefault();
        }
    }

    /// <summary>
    /// Remove a specific frame from the buffer
    /// </summary>
    public bool RemoveFrame(string frameId)
    {
        lock (_lock)
        {
            if (_frameIndex.TryGetValue(frameId, out var node))
            {
                _frames.Remove(node);
                _frameIndex.Remove(frameId);

                // Dispose of compressed data if present
                if (node.Value.CompressedData != null)
                {
                    node.Value.CompressedData = null;
                }

                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Remove the oldest frame from the buffer
    /// </summary>
    private void RemoveOldestFrame()
    {
        var oldest = _frames.First;
        if (oldest != null)
        {
            _frameIndex.Remove(oldest.Value.FrameId);
            _frames.RemoveFirst();

            // Dispose of compressed data if present
            if (oldest.Value.CompressedData != null)
            {
                oldest.Value.CompressedData = null;
            }
        }
    }

    /// <summary>
    /// Clear all frames from the buffer
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            // Dispose of all compressed data
            foreach (var frame in _frames)
            {
                if (frame.CompressedData != null)
                {
                    frame.CompressedData = null;
                }
            }

            _frames.Clear();
            _frameIndex.Clear();
        }
    }

    /// <summary>
    /// Get the current number of frames in the buffer
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _frames.Count;
            }
        }
    }

    /// <summary>
    /// Get total memory usage of buffered frames in bytes
    /// </summary>
    public long GetMemoryUsage()
    {
        lock (_lock)
        {
            return _frames.Sum(f => f.CompressedSize);
        }
    }

    /// <summary>
    /// Get buffer statistics
    /// </summary>
    public BufferStats GetStats()
    {
        lock (_lock)
        {
            var cameraFrameCounts = _frames
                .GroupBy(f => f.CameraId)
                .ToDictionary(g => g.Key, g => g.Count());

            return new BufferStats
            {
                TotalFrames = _frames.Count,
                KeyFrames = _frames.Count(f => f.IsKeyFrame),
                TotalMemoryBytes = GetMemoryUsage(),
                CameraFrameCounts = cameraFrameCounts,
                OldestFrameTime = _frames.FirstOrDefault()?.Timestamp,
                NewestFrameTime = _frames.LastOrDefault()?.Timestamp
            };
        }
    }

    /// <summary>
    /// Prune old frames beyond a time threshold
    /// </summary>
    public int PruneOldFrames(TimeSpan maxAge)
    {
        lock (_lock)
        {
            var cutoffTime = DateTime.UtcNow - maxAge;
            var toRemove = _frames
                .Where(f => f.Timestamp < cutoffTime)
                .Select(f => f.FrameId)
                .ToList();

            foreach (var frameId in toRemove)
            {
                RemoveFrame(frameId);
            }

            return toRemove.Count;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Clear();
        _disposed = true;
    }
}

/// <summary>
/// Buffer statistics
/// </summary>
public class BufferStats
{
    public int TotalFrames { get; set; }
    public int KeyFrames { get; set; }
    public long TotalMemoryBytes { get; set; }
    public Dictionary<string, int> CameraFrameCounts { get; set; } = new();
    public DateTime? OldestFrameTime { get; set; }
    public DateTime? NewestFrameTime { get; set; }

    public double MemoryMB => TotalMemoryBytes / (1024.0 * 1024.0);
}