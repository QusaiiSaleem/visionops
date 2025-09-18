using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using VisionOps.AI.Models;
using VisionOps.Core.Models;

namespace VisionOps.AI.Inference;

/// <summary>
/// Manages batch processing of frames for efficient AI inference
/// Optimized for 8-16 frame batches with <200ms total latency
/// </summary>
public class BatchProcessor : IDisposable
{
    private readonly ILogger<BatchProcessor> _logger;
    private readonly YoloV8Detector _yoloDetector;
    private readonly Florence2Processor _florence2Processor;
    private readonly RecyclableMemoryStreamManager _memoryManager;

    // Batch configuration
    private const int OptimalBatchSize = 8;
    private const int MaxBatchSize = 16;
    private const int BatchTimeoutMs = 500; // Max wait time to fill batch

    // Processing channels for each camera
    private readonly ConcurrentDictionary<string, FrameChannel> _cameraChannels;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly SemaphoreSlim _processingLock;

    // Metrics
    private long _totalFramesProcessed;
    private long _totalBatchesProcessed;
    private double _averageInferenceTime;

    private bool _disposed;

    public BatchProcessor(
        ILogger<BatchProcessor> logger,
        YoloV8Detector yoloDetector,
        Florence2Processor florence2Processor)
    {
        _logger = logger;
        _yoloDetector = yoloDetector;
        _florence2Processor = florence2Processor;
        _memoryManager = new RecyclableMemoryStreamManager();
        _cameraChannels = new ConcurrentDictionary<string, FrameChannel>();
        _cancellationTokenSource = new CancellationTokenSource();
        _processingLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Frame data with metadata for batch processing
    /// </summary>
    public class FrameData
    {
        public string CameraId { get; set; } = string.Empty;
        public int FrameNumber { get; set; }
        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        public DateTime CaptureTime { get; set; }
        public bool IsKeyFrameCandidate { get; set; }
        public TaskCompletionSource<ProcessingResult>? CompletionSource { get; set; }
    }

    /// <summary>
    /// Processing result for a frame
    /// </summary>
    public class ProcessingResult
    {
        public List<Detection> Detections { get; set; } = new();
        public KeyFrame? KeyFrame { get; set; }
        public double InferenceTimeMs { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Channel for a camera's frames
    /// </summary>
    private class FrameChannel
    {
        public Channel<FrameData> Channel { get; }
        public Task ProcessingTask { get; }
        public DateTime LastProcessTime { get; set; }
        public int FrameCount { get; set; }

        public FrameChannel(Channel<FrameData> channel, Task processingTask)
        {
            Channel = channel;
            ProcessingTask = processingTask;
            LastProcessTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Initialize batch processor
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing batch processor for 8-16 frame batches");

        // Initialize AI models
        await _yoloDetector.InitializeAsync();
        await _florence2Processor.InitializeAsync();

        _logger.LogInformation("Batch processor initialized");
    }

    /// <summary>
    /// Queue frame for batch processing
    /// </summary>
    public async Task<ProcessingResult> QueueFrameAsync(
        FrameData frame,
        CancellationToken cancellationToken = default)
    {
        // Get or create channel for camera
        var channel = _cameraChannels.GetOrAdd(frame.CameraId, cameraId =>
        {
            var ch = Channel.CreateUnbounded<FrameData>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            // Start processing task for this camera
            var task = ProcessCameraChannelAsync(cameraId, ch, _cancellationTokenSource.Token);

            return new FrameChannel(ch, task);
        });

        // Create completion source for async result
        frame.CompletionSource = new TaskCompletionSource<ProcessingResult>();

        // Queue frame
        await channel.Channel.Writer.WriteAsync(frame, cancellationToken);
        channel.FrameCount++;

        // Wait for processing
        return await frame.CompletionSource.Task;
    }

    /// <summary>
    /// Process frames for a specific camera
    /// </summary>
    private async Task ProcessCameraChannelAsync(
        string cameraId,
        Channel<FrameData> channel,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting batch processor for camera {Camera}", cameraId);

        var batch = new List<FrameData>(MaxBatchSize);
        var reader = channel.Reader;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Collect frames for batch
                batch.Clear();
                var batchTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    new CancellationTokenSource(BatchTimeoutMs).Token);

                // Fill batch up to optimal size
                while (batch.Count < OptimalBatchSize && !batchTimeout.Token.IsCancellationRequested)
                {
                    if (await reader.WaitToReadAsync(batchTimeout.Token))
                    {
                        if (reader.TryRead(out var frame))
                        {
                            batch.Add(frame);
                        }
                    }
                }

                // Continue filling up to max size if more frames available
                while (batch.Count < MaxBatchSize && reader.TryRead(out var frame))
                {
                    batch.Add(frame);
                }

                if (batch.Count > 0)
                {
                    await ProcessBatchAsync(batch, cameraId);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch for camera {Camera}", cameraId);

                // Complete frames with error
                foreach (var frame in batch)
                {
                    frame.CompletionSource?.TrySetResult(new ProcessingResult
                    {
                        Error = ex.Message
                    });
                }
            }
        }

        _logger.LogInformation("Stopped batch processor for camera {Camera}", cameraId);
    }

    /// <summary>
    /// Process a batch of frames
    /// </summary>
    private async Task ProcessBatchAsync(List<FrameData> batch, string cameraId)
    {
        if (batch.Count == 0) return;

        var startTime = DateTime.UtcNow;

        _logger.LogDebug("Processing batch of {Count} frames for camera {Camera}",
            batch.Count, cameraId);

        await _processingLock.WaitAsync();
        try
        {
            // Extract image data for batch
            var images = batch.Select(f => f.ImageData).ToList();

            // Run YOLOv8 batch detection
            var batchResult = await _yoloDetector.DetectBatchAsync(
                images,
                cameraId,
                batch[0].FrameNumber);

            // Process each frame's results
            for (int i = 0; i < batch.Count; i++)
            {
                var frame = batch[i];
                var frameDetections = batchResult.Detections
                    .Where(d => d.FrameNumber == frame.FrameNumber)
                    .ToList();

                var result = new ProcessingResult
                {
                    Detections = frameDetections,
                    InferenceTimeMs = batchResult.PerFrameInferenceMs
                };

                // Check if this should be a key frame (every 10 seconds)
                if (frame.IsKeyFrameCandidate || _florence2Processor.ShouldGenerateKeyFrame(cameraId))
                {
                    try
                    {
                        result.KeyFrame = await _florence2Processor.GenerateKeyFrameAsync(
                            frame.ImageData,
                            cameraId,
                            frame.FrameNumber,
                            frameDetections);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to generate key frame for camera {Camera}", cameraId);
                    }
                }

                // Complete the frame processing
                frame.CompletionSource?.TrySetResult(result);
            }

            // Update metrics
            var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            UpdateMetrics(batch.Count, totalTime);

            _logger.LogDebug("Batch processed in {Time}ms ({PerFrame}ms per frame)",
                totalTime, totalTime / batch.Count);
        }
        finally
        {
            _processingLock.Release();

            // Clean up memory
            if (GC.GetTotalMemory(false) > 4_000_000_000) // 4GB threshold
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
            }
        }
    }

    /// <summary>
    /// Update processing metrics
    /// </summary>
    private void UpdateMetrics(int batchSize, double processingTimeMs)
    {
        Interlocked.Add(ref _totalFramesProcessed, batchSize);
        Interlocked.Increment(ref _totalBatchesProcessed);

        // Update moving average
        var alpha = 0.1; // Smoothing factor
        _averageInferenceTime = _averageInferenceTime * (1 - alpha) + (processingTimeMs / batchSize) * alpha;
    }

    /// <summary>
    /// Get processing statistics
    /// </summary>
    public BatchProcessingStats GetStats()
    {
        return new BatchProcessingStats
        {
            TotalFramesProcessed = _totalFramesProcessed,
            TotalBatchesProcessed = _totalBatchesProcessed,
            AverageInferenceTimeMs = _averageInferenceTime,
            ActiveCameras = _cameraChannels.Count,
            AverageBatchSize = _totalBatchesProcessed > 0
                ? (double)_totalFramesProcessed / _totalBatchesProcessed
                : 0
        };
    }

    /// <summary>
    /// Clear frames for a specific camera
    /// </summary>
    public void ClearCameraQueue(string cameraId)
    {
        if (_cameraChannels.TryRemove(cameraId, out var channel))
        {
            channel.Channel.Writer.TryComplete();
            _logger.LogInformation("Cleared queue for camera {Camera}", cameraId);
        }
    }

    /// <summary>
    /// Process a single frame immediately (bypass batching)
    /// </summary>
    public async Task<ProcessingResult> ProcessSingleFrameAsync(
        FrameData frame,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // Run single-frame detection
            var detections = await _yoloDetector.DetectAsync(
                frame.ImageData,
                frame.CameraId,
                frame.FrameNumber);

            var result = new ProcessingResult
            {
                Detections = detections,
                InferenceTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            };

            // Generate key frame if needed
            if (frame.IsKeyFrameCandidate)
            {
                result.KeyFrame = await _florence2Processor.GenerateKeyFrameAsync(
                    frame.ImageData,
                    frame.CameraId,
                    frame.FrameNumber,
                    detections);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process single frame");
            return new ProcessingResult { Error = ex.Message };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cancellationTokenSource.Cancel();

        // Complete all channels
        foreach (var channel in _cameraChannels.Values)
        {
            channel.Channel.Writer.TryComplete();
        }

        // Wait for processing tasks
        var tasks = _cameraChannels.Values.Select(c => c.ProcessingTask).ToArray();
        Task.WaitAll(tasks, TimeSpan.FromSeconds(5));

        _cameraChannels.Clear();
        _cancellationTokenSource.Dispose();
        _processingLock.Dispose();
        _yoloDetector?.Dispose();
        _florence2Processor?.Dispose();

        _disposed = true;
    }
}

/// <summary>
/// Statistics for batch processing
/// </summary>
public class BatchProcessingStats
{
    public long TotalFramesProcessed { get; set; }
    public long TotalBatchesProcessed { get; set; }
    public double AverageInferenceTimeMs { get; set; }
    public double AverageBatchSize { get; set; }
    public int ActiveCameras { get; set; }
}