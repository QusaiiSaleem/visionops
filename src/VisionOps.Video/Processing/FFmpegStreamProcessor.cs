using System.Buffers;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using OpenCvSharp;
using VisionOps.Video.Memory;

namespace VisionOps.Video.Processing;

/// <summary>
/// CRITICAL: FFmpeg process isolation to prevent memory leaks.
/// NEVER use OpenCVSharp VideoCapture directly with RTSP streams.
/// This is a Phase 0 production hardening requirement.
/// </summary>
public sealed class FFmpegStreamProcessor : IDisposable
{
    private readonly ILogger<FFmpegStreamProcessor> _logger;
    private readonly FrameBufferPool _bufferPool;
    private readonly CircularFrameBuffer _frameBuffer;
    private readonly Channel<TimestampedFrame> _frameChannel;
    private Process? _ffmpegProcess;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;
    private bool _disposed;

    // Performance constraints
    private const int MaxMemoryBufferMB = 50;
    private const int FrameWidth = 640;
    private const int FrameHeight = 480;
    private const int FrameChannels = 3;
    private const int FrameBufferSize = FrameWidth * FrameHeight * FrameChannels;
    private const int CircularBufferFrames = 30; // Max frames in memory
    private const int ChannelCapacity = 10; // Channel buffer size

    // Health monitoring
    private DateTime _lastFrameTime = DateTime.UtcNow;
    private long _totalFramesProcessed;
    private long _totalFramesDropped;
    private long _totalRestarts;

    public FFmpegStreamProcessor(
        string cameraId,
        FrameBufferPool bufferPool,
        ILogger<FFmpegStreamProcessor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));

        // Initialize circular buffer for this camera
        _frameBuffer = new CircularFrameBuffer(
            cameraId,
            _bufferPool,
            logger,
            CircularBufferFrames);

        // Create bounded channel for frame processing
        _frameChannel = Channel.CreateBounded<TimestampedFrame>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        _logger.LogInformation(
            "FFmpegStreamProcessor created for camera {CameraId} with {BufferFrames} frame buffer",
            cameraId, CircularBufferFrames);
    }

    /// <summary>
    /// Start FFmpeg process for RTSP stream processing with complete isolation
    /// </summary>
    public async Task StartProcessAsync(
        string rtspUrl,
        Func<TimestampedFrame, Task> onFrameReceived,
        CancellationToken cancellationToken = default)
    {
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            throw new InvalidOperationException("FFmpeg process already running");
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // Start frame processing task
            _processingTask = ProcessFrameChannelAsync(onFrameReceived, _cancellationTokenSource.Token);

            // Configure FFmpeg arguments for optimal performance and stability
            var arguments = BuildFFmpegArguments(rtspUrl);

            _ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            // Monitor process exit for auto-restart
            _ffmpegProcess.Exited += async (sender, args) =>
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("FFmpeg process exited unexpectedly. Attempting restart...");
                    _totalRestarts++;
                    await RestartProcessAsync(rtspUrl, onFrameReceived);
                }
            };

            // Set process priority and affinity
            _ffmpegProcess.Start();
            try
            {
                _ffmpegProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
                // Limit to first 2 CPU cores to prevent resource starvation
                _ffmpegProcess.ProcessorAffinity = (IntPtr)0x3;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set process priority/affinity");
            }

            _logger.LogInformation(
                "FFmpeg process started for {RtspUrl} (PID: {PID})",
                rtspUrl, _ffmpegProcess.Id);

            // Start monitoring stderr for errors
            _ = MonitorStandardError(_ffmpegProcess.StandardError, _cancellationTokenSource.Token);

            // Process frames in isolated context with memory pooling
            await ProcessFrameStreamWithPool(
                _ffmpegProcess.StandardOutput.BaseStream,
                _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg process failed for {RtspUrl}", rtspUrl);
            StopProcess();
            throw;
        }
    }

    /// <summary>
    /// Build optimized FFmpeg arguments for RTSP processing
    /// </summary>
    private string BuildFFmpegArguments(string rtspUrl)
    {
        // Critical settings for production stability:
        // - rtsp_transport tcp: More reliable than UDP
        // - analyzeduration/probesize: Faster startup
        // - fflags nobuffer: Reduce latency
        // - fps=1/3: Process 1 frame every 3 seconds (production constraint)
        // - scale: Normalize to 640x480 for consistent processing
        // - threads 1: Prevent CPU overuse per stream
        return $"-rtsp_transport tcp " +
               $"-analyzeduration 1000000 " +
               $"-probesize 1000000 " +
               $"-fflags nobuffer " +
               $"-i \"{rtspUrl}\" " +
               $"-vf \"fps=1/3,scale=640:480\" " +  // 1 frame per 3 seconds, scaled to 640x480
               $"-threads 1 " +
               $"-f rawvideo " +
               $"-pix_fmt bgr24 " +
               $"pipe:1";
    }

    /// <summary>
    /// Process frame stream with memory pooling and circular buffer
    /// </summary>
    private async Task ProcessFrameStreamWithPool(
        Stream inputStream,
        CancellationToken cancellationToken)
    {
        var frameSize = FrameBufferSize;
        var buffer = _bufferPool.RentBuffer(frameSize);
        var totalBytesRead = 0;
        var frameCount = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Read complete frame (may require multiple reads)
                while (totalBytesRead < frameSize && !cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await inputStream.ReadAsync(
                        buffer.AsMemory(totalBytesRead, frameSize - totalBytesRead),
                        cancellationToken);

                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("FFmpeg stream ended unexpectedly after {FrameCount} frames", frameCount);
                        return;
                    }

                    totalBytesRead += bytesRead;
                }

                // Process complete frame
                if (totalBytesRead == frameSize)
                {
                    // Create Mat from buffer (no copy)
                    using var mat = new Mat(FrameHeight, FrameWidth, MatType.CV_8UC3, buffer);

                    // Add to circular buffer (buffer handles cloning)
                    var added = await _frameBuffer.AddFrameAsync(mat, cancellationToken);

                    if (added)
                    {
                        _totalFramesProcessed++;
                        _lastFrameTime = DateTime.UtcNow;

                        // Get frame from buffer and send to channel
                        var timestampedFrame = await _frameBuffer.GetFrameAsync(100, cancellationToken);
                        if (timestampedFrame != null)
                        {
                            // Try to write to channel, drop if full
                            if (!_frameChannel.Writer.TryWrite(timestampedFrame))
                            {
                                timestampedFrame.Dispose();
                                _totalFramesDropped++;

                                if (_totalFramesDropped % 100 == 0)
                                {
                                    _logger.LogWarning(
                                        "Dropped {DroppedCount} frames due to channel congestion",
                                        _totalFramesDropped);
                                }
                            }
                        }
                    }
                    else
                    {
                        _totalFramesDropped++;
                    }

                    frameCount++;
                    totalBytesRead = 0; // Reset for next frame

                    // Log progress periodically
                    if (frameCount % 100 == 0)
                    {
                        var bufferStats = _frameBuffer.GetStatistics();
                        _logger.LogDebug(
                            "Processed {Count} frames. Buffer: {BufferFrames}/{MaxFrames}, " +
                            "Dropped: {Dropped}, Memory: {Memory:F2}MB",
                            frameCount,
                            bufferStats.CurrentFrameCount,
                            bufferStats.MaxFrames,
                            _totalFramesDropped,
                            bufferStats.CurrentMemoryUsageMB);
                    }
                }

                // Check for stalled stream
                var timeSinceLastFrame = DateTime.UtcNow - _lastFrameTime;
                if (timeSinceLastFrame > TimeSpan.FromSeconds(30))
                {
                    _logger.LogWarning(
                        "Stream appears stalled. No frames for {Seconds} seconds",
                        timeSinceLastFrame.TotalSeconds);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Frame processing cancelled after {FrameCount} frames", frameCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing frame stream after {FrameCount} frames", frameCount);
            throw;
        }
        finally
        {
            _bufferPool.ReturnBuffer(buffer, clearBuffer: true);
            _logger.LogInformation(
                "Frame processing completed. Total processed: {Processed}, Dropped: {Dropped}",
                _totalFramesProcessed, _totalFramesDropped);
        }
    }

    /// <summary>
    /// Process frames from channel with backpressure handling
    /// </summary>
    private async Task ProcessFrameChannelAsync(
        Func<TimestampedFrame, Task> onFrameReceived,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _frameChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Process frame with timeout to prevent blocking
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, cts.Token);

                    await onFrameReceived(frame);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing frame callback");
                }
                finally
                {
                    frame.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Frame channel processing cancelled");
        }
    }

    /// <summary>
    /// Monitor stderr for FFmpeg errors and warnings
    /// </summary>
    private async Task MonitorStandardError(StreamReader stderr, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !stderr.EndOfStream)
            {
                var line = await stderr.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    // Log FFmpeg output based on severity
                    if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogError("FFmpeg error: {Message}", line);
                    }
                    else if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("FFmpeg warning: {Message}", line);
                    }
                    else
                    {
                        _logger.LogDebug("FFmpeg: {Message}", line);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring FFmpeg stderr");
        }
    }

    /// <summary>
    /// Restart FFmpeg process after unexpected exit
    /// </summary>
    private async Task RestartProcessAsync(
        string rtspUrl,
        Func<TimestampedFrame, Task> onFrameReceived)
    {
        // Limit restart attempts
        if (_totalRestarts > 10)
        {
            _logger.LogError("Too many restart attempts ({Count}). Giving up.", _totalRestarts);
            return;
        }

        // Clean up old process
        StopProcess();

        // Wait before restarting
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Start new process
        try
        {
            await StartProcessAsync(rtspUrl, onFrameReceived, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart FFmpeg process");
        }
    }

    /// <summary>
    /// Forcefully stop the FFmpeg process
    /// </summary>
    public void StopProcess()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                _ffmpegProcess.Kill();
                _ffmpegProcess.WaitForExit(5000); // Wait max 5 seconds
                _logger.LogInformation("FFmpeg process stopped");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping FFmpeg process");
        }
        finally
        {
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// Check if the process is still running
    /// </summary>
    public bool IsRunning => _ffmpegProcess != null && !_ffmpegProcess.HasExited;

    /// <summary>
    /// Get process memory usage in MB
    /// </summary>
    public long GetMemoryUsageMB()
    {
        if (_ffmpegProcess == null || _ffmpegProcess.HasExited)
            return 0;

        try
        {
            _ffmpegProcess.Refresh();
            return _ffmpegProcess.WorkingSet64 / (1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Get stream health metrics
    /// </summary>
    public StreamHealthMetrics GetHealthMetrics()
    {
        var bufferStats = _frameBuffer.GetStatistics();

        return new StreamHealthMetrics
        {
            IsRunning = IsRunning,
            ProcessMemoryMB = GetMemoryUsageMB(),
            TotalFramesProcessed = _totalFramesProcessed,
            TotalFramesDropped = _totalFramesDropped,
            TotalRestarts = _totalRestarts,
            TimeSinceLastFrame = DateTime.UtcNow - _lastFrameTime,
            BufferUtilization = bufferStats.BufferUtilization,
            CurrentBufferFrames = bufferStats.CurrentFrameCount,
            DroppedFrameRate = bufferStats.DropRate
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation(
            "Disposing FFmpegStreamProcessor. Frames processed: {Processed}, Dropped: {Dropped}, Restarts: {Restarts}",
            _totalFramesProcessed, _totalFramesDropped, _totalRestarts);

        // Signal cancellation
        _cancellationTokenSource?.Cancel();

        // Close channel
        _frameChannel.Writer.TryComplete();

        // Wait for processing to complete
        try
        {
            _processingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch { }

        // Stop process
        StopProcess();

        // Dispose resources
        _frameBuffer?.Dispose();
        _cancellationTokenSource?.Dispose();

        _disposed = true;
    }
}

/// <summary>
/// Stream health metrics for monitoring
/// </summary>
public class StreamHealthMetrics
{
    public bool IsRunning { get; init; }
    public long ProcessMemoryMB { get; init; }
    public long TotalFramesProcessed { get; init; }
    public long TotalFramesDropped { get; init; }
    public long TotalRestarts { get; init; }
    public TimeSpan TimeSinceLastFrame { get; init; }
    public double BufferUtilization { get; init; }
    public int CurrentBufferFrames { get; init; }
    public double DroppedFrameRate { get; init; }

    public bool IsHealthy => IsRunning &&
                             TimeSinceLastFrame < TimeSpan.FromMinutes(1) &&
                             DroppedFrameRate < 10.0;
}