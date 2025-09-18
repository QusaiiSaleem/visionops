using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using VisionOps.Core.Models;
using VisionOps.Video.Memory;
using VisionOps.Video.Processing;

namespace VisionOps.Video.Scheduling;

/// <summary>
/// Manages sequential frame processing across multiple cameras.
/// CRITICAL: Cameras are processed sequentially to prevent CPU/memory overload.
/// </summary>
public class FrameScheduler : IDisposable
{
    private readonly ILogger<FrameScheduler> _logger;
    private readonly FrameBufferPool _bufferPool;
    private readonly Dictionary<string, CameraProcessor> _cameras = new();
    private readonly Channel<FrameData> _frameQueue;
    private readonly CircularFrameBuffer _frameBuffer;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private CancellationTokenSource? _schedulerCts;
    private Task? _schedulerTask;
    private bool _disposed;

    // Performance constraints
    private const int MaxCameras = 5; // Maximum concurrent cameras (Florence-2 constraint)
    private const int FrameQueueSize = 100; // Maximum frames in processing queue
    private const int ProcessingDelayMs = 100; // Delay between processing cycles

    public FrameScheduler(ILogger<FrameScheduler> logger, FrameBufferPool bufferPool)
    {
        _logger = logger;
        _bufferPool = bufferPool;

        // Create bounded channel to prevent memory overflow
        var options = new BoundedChannelOptions(FrameQueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _frameQueue = Channel.CreateBounded<FrameData>(options);

        // Initialize circular buffer for frame memory management
        _frameBuffer = new CircularFrameBuffer("scheduler", _bufferPool, _logger as ILogger, 30);
    }

    /// <summary>
    /// Add a camera to the processing schedule
    /// </summary>
    public async Task<bool> AddCameraAsync(CameraConfig camera)
    {
        if (_cameras.Count >= MaxCameras)
        {
            _logger.LogWarning("Maximum camera limit reached ({Max}). Cannot add {Camera}",
                MaxCameras, camera.Name);
            return false;
        }

        await _processingLock.WaitAsync();
        try
        {
            if (_cameras.ContainsKey(camera.CameraId))
            {
                _logger.LogWarning("Camera {Id} already exists", camera.CameraId);
                return false;
            }

            var processor = new CameraProcessor(camera, _logger, _bufferPool);
            _cameras[camera.CameraId] = processor;

            _logger.LogInformation("Added camera {Name} ({Id}) to scheduler",
                camera.Name, camera.CameraId);
            return true;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Remove a camera from the processing schedule
    /// </summary>
    public async Task RemoveCameraAsync(string cameraId)
    {
        await _processingLock.WaitAsync();
        try
        {
            if (_cameras.TryGetValue(cameraId, out var processor))
            {
                processor.Dispose();
                _cameras.Remove(cameraId);
                _logger.LogInformation("Removed camera {Id} from scheduler", cameraId);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Start the frame processing scheduler
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_schedulerTask != null)
        {
            _logger.LogWarning("Scheduler already running");
            return;
        }

        _schedulerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _schedulerTask = RunSchedulerAsync(_schedulerCts.Token);

        _logger.LogInformation("Frame scheduler started with {Count} cameras", _cameras.Count);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stop the frame processing scheduler
    /// </summary>
    public async Task StopAsync()
    {
        _schedulerCts?.Cancel();

        if (_schedulerTask != null)
        {
            await _schedulerTask;
            _schedulerTask = null;
        }

        // Stop all camera processors
        foreach (var processor in _cameras.Values)
        {
            processor.Dispose();
        }

        _logger.LogInformation("Frame scheduler stopped");
    }

    /// <summary>
    /// Main scheduler loop - processes cameras sequentially
    /// </summary>
    private async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting sequential camera processing loop");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Process each camera sequentially
                foreach (var processor in _cameras.Values.ToList())
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await ProcessCameraFrameAsync(processor, cancellationToken);

                    // Delay between cameras to prevent CPU spikes
                    await Task.Delay(ProcessingDelayMs, cancellationToken);

                    // Check memory pressure
                    await CheckMemoryPressureAsync();
                }

                // Complete cycle delay (ensures 1 frame per 3 seconds per camera)
                var cycleDelay = Math.Max(3000 - (_cameras.Count * ProcessingDelayMs), 1000);
                await Task.Delay(cycleDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler loop");
                await Task.Delay(1000, cancellationToken);
            }
        }

        _logger.LogInformation("Sequential camera processing loop ended");
    }

    /// <summary>
    /// Process a single frame from a camera
    /// </summary>
    private async Task ProcessCameraFrameAsync(
        CameraProcessor processor,
        CancellationToken cancellationToken)
    {
        try
        {
            var frame = await processor.CaptureFrameAsync(cancellationToken);
            if (frame == null)
                return;

            // Add to circular buffer (automatically removes oldest if full)
            _frameBuffer.AddFrame(frame);

            // Queue for further processing
            await _frameQueue.Writer.WriteAsync(frame, cancellationToken);

            _logger.LogDebug("Processed frame from camera {Camera}, buffer size: {Size}",
                processor.Camera.Name, _frameBuffer.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing frame from camera {Camera}",
                processor.Camera.Name);
        }
    }

    /// <summary>
    /// Check and handle memory pressure
    /// </summary>
    private async Task CheckMemoryPressureAsync()
    {
        var memoryMB = GC.GetTotalMemory(false) / (1024 * 1024);

        if (memoryMB > 4000) // 4GB threshold
        {
            _logger.LogWarning("High memory usage detected: {Memory}MB. Clearing buffers.",
                memoryMB);

            // Clear frame buffer
            _frameBuffer.Clear();

            // Force garbage collection
            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            await Task.Delay(500); // Give system time to recover
        }
    }

    /// <summary>
    /// Get the next frame for processing
    /// </summary>
    public async Task<FrameData?> GetNextFrameAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _frameQueue.Reader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Get current scheduler statistics
    /// </summary>
    public SchedulerStats GetStats()
    {
        return new SchedulerStats
        {
            ActiveCameras = _cameras.Count,
            QueuedFrames = _frameQueue.Reader.Count,
            BufferedFrames = _frameBuffer.Count,
            MemoryUsageMB = GC.GetTotalMemory(false) / (1024 * 1024)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().GetAwaiter().GetResult();
        _frameQueue.Writer.TryComplete();
        _frameBuffer.Dispose();
        _processingLock.Dispose();
        _schedulerCts?.Dispose();

        _disposed = true;
    }

    /// <summary>
    /// Internal camera processor wrapper
    /// </summary>
    private class CameraProcessor : IDisposable
    {
        public CameraConfig Camera { get; }
        private readonly FFmpegStreamProcessor _ffmpeg;
        private readonly ILogger _logger;
        private readonly Queue<Mat> _frameQueue = new();
        private readonly object _queueLock = new();
        private long _frameCounter;

        public CameraProcessor(CameraConfig camera, ILogger logger, FrameBufferPool bufferPool)
        {
            Camera = camera;
            _logger = logger;
            _ffmpeg = new FFmpegStreamProcessor(
                camera.Id,
                bufferPool,
                logger as ILogger<FFmpegStreamProcessor> ??
                throw new ArgumentException("Invalid logger type"));
        }

        public async Task<FrameData?> CaptureFrameAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Start FFmpeg if not running
                if (!_ffmpeg.IsRunning)
                {
                    await _ffmpeg.StartProcessAsync(
                        Camera.GetAuthenticatedUrl(),
                        OnFrameReceived,
                        cancellationToken);

                    // Wait for first frame
                    await Task.Delay(1000, cancellationToken);
                }

                // Get frame from queue
                Mat? mat = null;
                lock (_queueLock)
                {
                    if (_frameQueue.Count > 0)
                        mat = _frameQueue.Dequeue();
                }

                if (mat == null)
                    return null;

                using (mat)
                {
                    var frameNumber = Interlocked.Increment(ref _frameCounter);
                    var isKeyFrame = frameNumber % Camera.KeyFrameInterval == 0;

                    return new FrameData
                    {
                        CameraId = Camera.CameraId,
                        FrameNumber = frameNumber,
                        IsKeyFrame = isKeyFrame,
                        Dimensions = new FrameDimensions
                        {
                            Width = mat.Width,
                            Height = mat.Height,
                            Channels = mat.Channels()
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing frame from camera {Camera}",
                    Camera.Name);
                return null;
            }
        }

        private void OnFrameReceived(Mat frame)
        {
            lock (_queueLock)
            {
                // Keep only latest frame to prevent memory buildup
                while (_frameQueue.Count > 0)
                {
                    var old = _frameQueue.Dequeue();
                    old.Dispose();
                }

                _frameQueue.Enqueue(frame.Clone());
            }
        }

        public void Dispose()
        {
            _ffmpeg.StopProcess();
            _ffmpeg.Dispose();

            lock (_queueLock)
            {
                while (_frameQueue.Count > 0)
                {
                    var frame = _frameQueue.Dequeue();
                    frame.Dispose();
                }
            }
        }
    }
}

/// <summary>
/// Scheduler statistics
/// </summary>
public class SchedulerStats
{
    public int ActiveCameras { get; set; }
    public int QueuedFrames { get; set; }
    public int BufferedFrames { get; set; }
    public long MemoryUsageMB { get; set; }
}