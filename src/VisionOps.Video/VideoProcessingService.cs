using Microsoft.Extensions.Logging;
using OpenCvSharp;
using VisionOps.Core.Models;
using VisionOps.Video.Compression;
using VisionOps.Video.Discovery;
using VisionOps.Video.Scheduling;

namespace VisionOps.Video;

/// <summary>
/// Main video processing service that coordinates all video-related operations.
/// Ensures sequential processing, memory management, and compression.
/// </summary>
public class VideoProcessingService : IDisposable
{
    private readonly ILogger<VideoProcessingService> _logger;
    private readonly OnvifDiscoveryService _discovery;
    private readonly FrameScheduler _scheduler;
    private readonly WebPCompressor _compressor;
    private readonly List<CameraConfig> _cameras = new();
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private bool _disposed;

    // Events for external consumers
    public event Action<FrameData>? OnFrameProcessed;
    public event Action<FrameData>? OnKeyFrameReady;
    public event Action<string, Exception>? OnCameraError;

    public VideoProcessingService(ILogger<VideoProcessingService> logger)
    {
        _logger = logger;
        _discovery = new OnvifDiscoveryService(logger as ILogger<OnvifDiscoveryService> ??
            throw new ArgumentException("Invalid logger type"));
        _scheduler = new FrameScheduler(logger as ILogger<FrameScheduler> ??
            throw new ArgumentException("Invalid logger type"));
        _compressor = new WebPCompressor(logger as ILogger<WebPCompressor> ??
            throw new ArgumentException("Invalid logger type"));
    }

    /// <summary>
    /// Initialize and discover cameras on the network
    /// </summary>
    public async Task<List<CameraConfig>> InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing video processing service...");

        try
        {
            // Discover ONVIF cameras
            var discoveredCameras = await _discovery.DiscoverCamerasAsync(cancellationToken);
            _logger.LogInformation("Discovered {Count} cameras via ONVIF", discoveredCameras.Count);

            // Test connections and add valid cameras
            foreach (var camera in discoveredCameras)
            {
                if (await _discovery.TestCameraConnectionAsync(camera, cancellationToken))
                {
                    await AddCameraAsync(camera);
                }
            }

            return _cameras;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize video processing service");
            throw;
        }
    }

    /// <summary>
    /// Add a camera to the processing pipeline
    /// </summary>
    public async Task<bool> AddCameraAsync(CameraConfig camera)
    {
        await _processingLock.WaitAsync();
        try
        {
            if (_cameras.Count >= 5)
            {
                _logger.LogWarning("Maximum camera limit (5) reached. Cannot add {Camera}",
                    camera.Name);
                return false;
            }

            if (_cameras.Any(c => c.CameraId == camera.CameraId))
            {
                _logger.LogWarning("Camera {Id} already exists", camera.CameraId);
                return false;
            }

            // Add to scheduler
            if (await _scheduler.AddCameraAsync(camera))
            {
                _cameras.Add(camera);
                _logger.LogInformation("Added camera {Name} to processing pipeline",
                    camera.Name);
                return true;
            }

            return false;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Add camera manually by RTSP URL
    /// </summary>
    public async Task<CameraConfig?> AddCameraManuallyAsync(
        string rtspUrl,
        string? username = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var camera = await _discovery.AddCameraManuallyAsync(
            rtspUrl, username, password, cancellationToken);

        if (camera != null && await AddCameraAsync(camera))
        {
            return camera;
        }

        return null;
    }

    /// <summary>
    /// Remove a camera from processing
    /// </summary>
    public async Task RemoveCameraAsync(string cameraId)
    {
        await _processingLock.WaitAsync();
        try
        {
            var camera = _cameras.FirstOrDefault(c => c.CameraId == cameraId);
            if (camera != null)
            {
                await _scheduler.RemoveCameraAsync(cameraId);
                _cameras.Remove(camera);
                _logger.LogInformation("Removed camera {Name} from pipeline",
                    camera.Name);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Start video processing
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask != null)
        {
            _logger.LogWarning("Video processing already running");
            return;
        }

        _logger.LogInformation("Starting video processing with {Count} cameras",
            _cameras.Count);

        _processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start frame scheduler
        await _scheduler.StartAsync(_processingCts.Token);

        // Start processing loop
        _processingTask = ProcessFramesAsync(_processingCts.Token);
    }

    /// <summary>
    /// Stop video processing
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping video processing...");

        _processingCts?.Cancel();

        if (_processingTask != null)
        {
            await _processingTask;
            _processingTask = null;
        }

        await _scheduler.StopAsync();

        _logger.LogInformation("Video processing stopped");
    }

    /// <summary>
    /// Main frame processing loop
    /// </summary>
    private async Task ProcessFramesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Frame processing loop started");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Get next frame from scheduler
                var frame = await _scheduler.GetNextFrameAsync(cancellationToken);
                if (frame == null)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                // Process the frame
                await ProcessSingleFrameAsync(frame, cancellationToken);

                // Monitor system health
                await MonitorSystemHealthAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in frame processing loop");
                await Task.Delay(1000, cancellationToken);
            }
        }

        _logger.LogInformation("Frame processing loop ended");
    }

    /// <summary>
    /// Process a single frame
    /// </summary>
    private async Task ProcessSingleFrameAsync(
        FrameData frame,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // Only compress key frames
            if (frame.IsKeyFrame)
            {
                // Create a dummy Mat for compression (in production, this would come from FFmpeg)
                using var mat = new Mat(frame.Dimensions.Height,
                    frame.Dimensions.Width,
                    MatType.CV_8UC3);

                // Compress frame
                var compressed = await _compressor.CompressFrameAsync(mat, true, cancellationToken);
                if (compressed != null)
                {
                    frame.CompressedData = compressed;
                    _logger.LogDebug("Compressed key frame to {Size} bytes", compressed.Length);

                    // Notify consumers
                    OnKeyFrameReady?.Invoke(frame);
                }
            }

            frame.ProcessingTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Notify frame processed
            OnFrameProcessed?.Invoke(frame);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing frame from camera {Camera}",
                frame.CameraId);

            var camera = _cameras.FirstOrDefault(c => c.CameraId == frame.CameraId);
            if (camera != null)
            {
                OnCameraError?.Invoke(camera.Name, ex);
            }
        }
    }

    /// <summary>
    /// Monitor system health and apply throttling if needed
    /// </summary>
    private async Task MonitorSystemHealthAsync()
    {
        var stats = GetSystemStats();

        // Check memory pressure
        if (stats.MemoryUsageMB > 5000)
        {
            _logger.LogWarning("High memory usage: {Memory}MB. Triggering cleanup.",
                stats.MemoryUsageMB);

            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            await Task.Delay(1000);
        }

        // Check CPU temperature (placeholder - implement actual reading)
        if (stats.CpuTemperature > 70)
        {
            _logger.LogWarning("High CPU temperature: {Temp}°C. Throttling processing.",
                stats.CpuTemperature);

            // Add delay to reduce processing rate
            await Task.Delay(2000);
        }
    }

    /// <summary>
    /// Get current system statistics
    /// </summary>
    public SystemMetrics GetSystemStats()
    {
        var schedulerStats = _scheduler.GetStats();

        return new SystemMetrics
        {
            ActiveCameras = schedulerStats.ActiveCameras,
            MemoryUsageMB = schedulerStats.MemoryUsageMB,
            FramesProcessed = schedulerStats.BufferedFrames,
            SyncQueueSize = schedulerStats.QueuedFrames,
            CpuTemperature = GetCpuTemperature(),
            CpuUsage = GetCpuUsage(),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get CPU temperature (platform-specific implementation needed)
    /// </summary>
    private int GetCpuTemperature()
    {
        // TODO: Implement actual CPU temperature reading
        // This is platform-specific (WMI on Windows)
        return 65; // Placeholder
    }

    /// <summary>
    /// Get CPU usage percentage
    /// </summary>
    private float GetCpuUsage()
    {
        // TODO: Implement actual CPU usage monitoring
        return 45.0f; // Placeholder
    }

    /// <summary>
    /// Get list of active cameras
    /// </summary>
    public List<CameraConfig> GetCameras() => _cameras.ToList();

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().GetAwaiter().GetResult();
        _scheduler.Dispose();
        _compressor.Dispose();
        _processingLock.Dispose();
        _processingCts?.Dispose();

        _disposed = true;
    }
}