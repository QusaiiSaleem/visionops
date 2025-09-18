using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionOps.Core.Models;

namespace VisionOps.AI.Inference;

/// <summary>
/// Manages the key frame generation and description pipeline
/// Ensures Florence-2 runs max once every 10 seconds per camera
/// </summary>
public class KeyFramePipeline : IDisposable
{
    private readonly ILogger<KeyFramePipeline> _logger;
    private readonly BatchProcessor _batchProcessor;
    private readonly ConcurrentDictionary<string, CameraKeyFrameState> _cameraStates;
    private readonly ConcurrentQueue<KeyFrame> _keyFrameQueue;
    private readonly SemaphoreSlim _processingLock;
    private readonly Timer _cleanupTimer;

    // Configuration
    private const int KeyFrameIntervalSeconds = 10;
    private const int MaxKeyFramesInMemory = 100;
    private const int CleanupIntervalMinutes = 5;

    private bool _disposed;

    public KeyFramePipeline(
        ILogger<KeyFramePipeline> logger,
        BatchProcessor batchProcessor)
    {
        _logger = logger;
        _batchProcessor = batchProcessor;
        _cameraStates = new ConcurrentDictionary<string, CameraKeyFrameState>();
        _keyFrameQueue = new ConcurrentQueue<KeyFrame>();
        _processingLock = new SemaphoreSlim(1, 1);

        // Cleanup timer to remove old key frames from memory
        _cleanupTimer = new Timer(
            CleanupOldKeyFrames,
            null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    /// <summary>
    /// State tracking for key frame generation per camera
    /// </summary>
    private class CameraKeyFrameState
    {
        public DateTime LastKeyFrameTime { get; set; }
        public int KeyFrameCount { get; set; }
        public Queue<KeyFrame> RecentKeyFrames { get; } = new(10);
        public double AverageProcessingTime { get; set; }
        public string LastDescription { get; set; } = string.Empty;
    }

    /// <summary>
    /// Process a frame and determine if it should become a key frame
    /// </summary>
    public async Task<KeyFrameResult> ProcessFrameAsync(
        byte[] imageData,
        string cameraId,
        int frameNumber,
        List<Detection>? detections = null)
    {
        // Get or create camera state
        var state = _cameraStates.GetOrAdd(cameraId, _ => new CameraKeyFrameState());

        // Check if enough time has passed for a new key frame
        var now = DateTime.UtcNow;
        var timeSinceLastKeyFrame = now - state.LastKeyFrameTime;

        if (timeSinceLastKeyFrame.TotalSeconds < KeyFrameIntervalSeconds)
        {
            return new KeyFrameResult
            {
                IsKeyFrame = false,
                NextKeyFrameIn = TimeSpan.FromSeconds(KeyFrameIntervalSeconds) - timeSinceLastKeyFrame
            };
        }

        // Process as key frame
        await _processingLock.WaitAsync();
        try
        {
            var startTime = DateTime.UtcNow;

            // Queue frame for processing
            var frameData = new BatchProcessor.FrameData
            {
                CameraId = cameraId,
                FrameNumber = frameNumber,
                ImageData = imageData,
                CaptureTime = now,
                IsKeyFrameCandidate = true
            };

            var result = await _batchProcessor.ProcessSingleFrameAsync(frameData);

            if (result.KeyFrame != null)
            {
                // Update state
                state.LastKeyFrameTime = now;
                state.KeyFrameCount++;
                state.LastDescription = result.KeyFrame.Description;

                // Update average processing time
                var alpha = 0.1;
                state.AverageProcessingTime = state.AverageProcessingTime * (1 - alpha) +
                                             result.KeyFrame.ProcessingTimeMs * alpha;

                // Store in recent queue
                state.RecentKeyFrames.Enqueue(result.KeyFrame);
                if (state.RecentKeyFrames.Count > 10)
                {
                    state.RecentKeyFrames.Dequeue();
                }

                // Add to global queue
                _keyFrameQueue.Enqueue(result.KeyFrame);

                // Limit memory usage
                while (_keyFrameQueue.Count > MaxKeyFramesInMemory)
                {
                    _keyFrameQueue.TryDequeue(out _);
                }

                _logger.LogInformation(
                    "Generated key frame for camera {Camera}: \"{Description}\" in {Time}ms",
                    cameraId,
                    result.KeyFrame.Description.Length > 50
                        ? result.KeyFrame.Description.Substring(0, 47) + "..."
                        : result.KeyFrame.Description,
                    result.KeyFrame.ProcessingTimeMs);

                return new KeyFrameResult
                {
                    IsKeyFrame = true,
                    KeyFrame = result.KeyFrame,
                    ProcessingTimeMs = result.KeyFrame.ProcessingTimeMs,
                    NextKeyFrameIn = TimeSpan.FromSeconds(KeyFrameIntervalSeconds)
                };
            }

            return new KeyFrameResult
            {
                IsKeyFrame = false,
                Error = result.Error
            };
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Get recent key frames for a camera
    /// </summary>
    public List<KeyFrame> GetRecentKeyFrames(string cameraId, int count = 10)
    {
        if (_cameraStates.TryGetValue(cameraId, out var state))
        {
            return state.RecentKeyFrames.Take(count).ToList();
        }
        return new List<KeyFrame>();
    }

    /// <summary>
    /// Get all pending key frames for sync
    /// </summary>
    public List<KeyFrame> GetPendingKeyFrames(int maxCount = 50)
    {
        var pending = new List<KeyFrame>();

        while (pending.Count < maxCount && _keyFrameQueue.TryDequeue(out var keyFrame))
        {
            if (!keyFrame.IsSynced)
            {
                pending.Add(keyFrame);
            }
        }

        return pending;
    }

    /// <summary>
    /// Mark key frames as synced
    /// </summary>
    public void MarkAsSynced(IEnumerable<Guid> keyFrameIds)
    {
        var idSet = new HashSet<Guid>(keyFrameIds);

        foreach (var state in _cameraStates.Values)
        {
            foreach (var keyFrame in state.RecentKeyFrames)
            {
                if (idSet.Contains(keyFrame.Id))
                {
                    keyFrame.IsSynced = true;
                    keyFrame.LastSyncAttempt = DateTime.UtcNow;
                }
            }
        }
    }

    /// <summary>
    /// Get statistics for the pipeline
    /// </summary>
    public KeyFramePipelineStats GetStats()
    {
        var stats = new KeyFramePipelineStats
        {
            TotalCameras = _cameraStates.Count,
            TotalKeyFrames = _cameraStates.Values.Sum(s => s.KeyFrameCount),
            PendingKeyFrames = _keyFrameQueue.Count,
            AverageProcessingTimeMs = _cameraStates.Values
                .Where(s => s.AverageProcessingTime > 0)
                .Select(s => s.AverageProcessingTime)
                .DefaultIfEmpty(0)
                .Average()
        };

        // Calculate memory usage
        long totalMemory = 0;
        foreach (var keyFrame in _keyFrameQueue)
        {
            totalMemory += keyFrame.GetStorageSize();
        }
        stats.MemoryUsageMB = totalMemory / (1024.0 * 1024.0);

        // Per-camera stats
        foreach (var kvp in _cameraStates)
        {
            stats.CameraStats[kvp.Key] = new CameraKeyFrameStats
            {
                KeyFrameCount = kvp.Value.KeyFrameCount,
                LastKeyFrameTime = kvp.Value.LastKeyFrameTime,
                LastDescription = kvp.Value.LastDescription,
                AverageProcessingTimeMs = kvp.Value.AverageProcessingTime
            };
        }

        return stats;
    }

    /// <summary>
    /// Cleanup old key frames from memory
    /// </summary>
    private void CleanupOldKeyFrames(object? state)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-30);
            var removed = 0;

            foreach (var cameraState in _cameraStates.Values)
            {
                while (cameraState.RecentKeyFrames.Count > 0)
                {
                    if (cameraState.RecentKeyFrames.Peek().Timestamp < cutoffTime)
                    {
                        cameraState.RecentKeyFrames.Dequeue();
                        removed++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (removed > 0)
            {
                _logger.LogDebug("Cleaned up {Count} old key frames from memory", removed);
            }

            // Force GC if memory usage is high
            if (GC.GetTotalMemory(false) > 4_000_000_000)
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during key frame cleanup");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();
        _processingLock?.Dispose();
        _cameraStates.Clear();

        _disposed = true;
    }
}

/// <summary>
/// Result of key frame processing
/// </summary>
public class KeyFrameResult
{
    public bool IsKeyFrame { get; set; }
    public KeyFrame? KeyFrame { get; set; }
    public double ProcessingTimeMs { get; set; }
    public TimeSpan NextKeyFrameIn { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Statistics for the key frame pipeline
/// </summary>
public class KeyFramePipelineStats
{
    public int TotalCameras { get; set; }
    public int TotalKeyFrames { get; set; }
    public int PendingKeyFrames { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public double MemoryUsageMB { get; set; }
    public Dictionary<string, CameraKeyFrameStats> CameraStats { get; set; } = new();
}

/// <summary>
/// Per-camera key frame statistics
/// </summary>
public class CameraKeyFrameStats
{
    public int KeyFrameCount { get; set; }
    public DateTime LastKeyFrameTime { get; set; }
    public string LastDescription { get; set; } = string.Empty;
    public double AverageProcessingTimeMs { get; set; }
}