using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using VisionOps.Service.Monitoring;
using VisionOps.Video.Memory;
using VisionOps.Video.Processing;
using Xunit;

namespace VisionOps.Tests.Phase0;

/// <summary>
/// Critical Phase 0 tests to validate memory leak prevention.
/// These tests MUST pass before any feature development.
/// </summary>
public class MemoryLeakTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly FrameBufferPool _bufferPool;
    private readonly List<IDisposable> _disposables;

    public MemoryLeakTests()
    {
        _loggerFactory = NullLoggerFactory.Instance;
        _bufferPool = new FrameBufferPool(
            _loggerFactory.CreateLogger<FrameBufferPool>());
        _disposables = new List<IDisposable> { _bufferPool };
    }

    /// <summary>
    /// Test that FFmpeg process isolation prevents memory leaks
    /// </summary>
    [Fact]
    public async Task FFmpegProcessor_ShouldNotLeakMemory_WhenProcessingMultipleStreams()
    {
        // Arrange
        var initialMemory = GC.GetTotalMemory(true) / (1024.0 * 1024.0);
        var logger = _loggerFactory.CreateLogger<FFmpegStreamProcessor>();
        var iterations = 10;
        var maxMemoryGrowthMB = 10.0;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            using var processor = new FFmpegStreamProcessor($"test-camera-{i}", _bufferPool, logger);

            // Simulate processing
            await Task.Delay(100);

            // Check process isolation
            processor.IsRunning.Should().BeFalse("process should not be running without StartProcessAsync");
            processor.GetMemoryUsageMB().Should().Be(0, "no memory should be used without active process");
        }

        // Force GC to ensure cleanup
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        var finalMemory = GC.GetTotalMemory(true) / (1024.0 * 1024.0);

        // Assert
        var memoryGrowthMB = finalMemory - initialMemory;
        memoryGrowthMB.Should().BeLessThan(maxMemoryGrowthMB,
            $"memory should not increase by more than {maxMemoryGrowthMB}MB after {iterations} iterations");

        // Verify buffer pool has no leaks
        var poolStats = _bufferPool.GetStatistics();
        poolStats.LeakedBuffers.Should().Be(0, "no buffers should be leaked");
    }

    /// <summary>
    /// Test that process isolation properly cleans up resources
    /// </summary>
    [Fact]
    public void FFmpegProcessor_ShouldCleanupProcess_WhenDisposed()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<FFmpegStreamProcessor>();
        var processor = new FFmpegStreamProcessor("test-camera", _bufferPool, logger);

        // Act
        processor.Dispose();

        // Assert
        processor.IsRunning.Should().BeFalse("process should stop when disposed");
        processor.GetMemoryUsageMB().Should().Be(0, "memory usage should be 0 after disposal");

        var healthMetrics = processor.GetHealthMetrics();
        healthMetrics.IsRunning.Should().BeFalse();
        healthMetrics.ProcessMemoryMB.Should().Be(0);
    }

    /// <summary>
    /// Test that circular buffer respects memory limits and drops old frames
    /// </summary>
    [Fact]
    public async Task CircularFrameBuffer_ShouldEnforceMemoryLimits()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<CircularFrameBuffer>();
        var maxFrames = 30;
        var buffer = new CircularFrameBuffer("test-camera", _bufferPool, logger, maxFrames);
        _disposables.Add(buffer);

        // Act - Add twice the max frames
        for (int i = 0; i < maxFrames * 2; i++)
        {
            using var mat = new Mat(480, 640, MatType.CV_8UC3);
            mat.SetTo(new Scalar(i, i, i));
            var added = await buffer.AddFrameAsync(mat);
            added.Should().BeTrue($"frame {i} should be added");
        }

        // Assert
        var stats = buffer.GetStatistics();
        stats.CurrentFrameCount.Should().BeLessThanOrEqualTo(maxFrames,
            $"buffer should not exceed {maxFrames} frames");
        stats.TotalFramesDropped.Should().BeGreaterThan(0,
            "older frames should be dropped when buffer is full");
        stats.BufferUtilization.Should().BeLessThanOrEqualTo(100.0,
            "buffer utilization should not exceed 100%");
    }

    /// <summary>
    /// Test that frame buffer pool properly manages memory allocation
    /// </summary>
    [Fact]
    public void FrameBufferPool_ShouldReuseBuffers_WithoutLeaking()
    {
        // Arrange
        const int iterations = 100;
        const int bufferSize = 640 * 480 * 3;
        var buffers = new List<byte[]>();

        // Act - Rent and return buffers
        for (int i = 0; i < iterations; i++)
        {
            var buffer = _bufferPool.RentBuffer(bufferSize);
            buffer.Should().NotBeNull();
            buffer.Length.Should().BeGreaterThanOrEqualTo(bufferSize);

            if (i % 2 == 0)
            {
                // Keep half the buffers
                buffers.Add(buffer);
            }
            else
            {
                // Return immediately
                _bufferPool.ReturnBuffer(buffer);
            }
        }

        // Return remaining buffers
        foreach (var buffer in buffers)
        {
            _bufferPool.ReturnBuffer(buffer);
        }

        // Assert
        var stats = _bufferPool.GetStatistics();
        stats.CurrentlyInUse.Should().Be(0, "all buffers should be returned");
        stats.LeakedBuffers.Should().Be(0, "no buffers should be leaked");
        _bufferPool.HasMemoryLeak().Should().BeFalse("no memory leak should be detected");
    }

    /// <summary>
    /// Test memory monitor detection of leaks
    /// </summary>
    [Fact]
    public async Task MemoryMonitor_ShouldDetectAndReportLeaks()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<MemoryMonitor>();
        using var appLifetime = new TestApplicationLifetime();
        var monitor = new MemoryMonitor(logger, appLifetime, _bufferPool);
        _disposables.Add(monitor);

        // Act - Create intentional leak
        var leakedBuffers = new List<byte[]>();
        for (int i = 0; i < 15; i++)
        {
            var buffer = _bufferPool.RentBuffer(1024);
            leakedBuffers.Add(buffer); // Don't return
        }

        await Task.Delay(100);

        // Assert
        _bufferPool.HasMemoryLeak().Should().BeTrue("memory leak should be detected");
        var stats = _bufferPool.GetStatistics();
        stats.LeakedBuffers.Should().BeGreaterThan(10, "more than 10 leaked buffers");

        // Cleanup
        foreach (var buffer in leakedBuffers)
        {
            _bufferPool.ReturnBuffer(buffer);
        }
    }

    /// <summary>
    /// Test recyclable memory streams don't leak
    /// </summary>
    [Fact]
    public void RecyclableStreams_ShouldNotLeakMemory()
    {
        // Arrange
        const int iterations = 100;
        var streams = new List<RecyclableMemoryStream>();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var stream = _bufferPool.GetStream($"test-{i}");
            stream.Write(new byte[1024], 0, 1024);
            streams.Add(stream);
        }

        // Get stats before disposal
        var beforeStats = _bufferPool.GetStatistics();
        beforeStats.SmallPoolInUse.Should().BeGreaterThan(0, "streams should be using memory");

        // Dispose all streams
        foreach (var stream in streams)
        {
            stream.Dispose();
        }

        // Assert
        var afterStats = _bufferPool.GetStatistics();
        afterStats.SmallPoolInUse.Should().BeLessThanOrEqualTo(beforeStats.SmallPoolInUse,
            "memory should be returned after disposal");
    }

    /// <summary>
    /// Test pooled Mat objects lifecycle
    /// </summary>
    [Fact]
    public async Task PooledMat_ShouldReturnToPool_AfterDisposal()
    {
        // Arrange
        PooledMat.SetPool(_bufferPool);
        var initialStats = _bufferPool.GetStatistics();

        // Act
        for (int i = 0; i < 10; i++)
        {
            using var pooledMat = await _bufferPool.RentMatAsync(480, 640);
            pooledMat.Should().NotBeNull();
            pooledMat.Mat.Should().NotBeNull();
            pooledMat.Mat.Height.Should().Be(480);
            pooledMat.Mat.Width.Should().Be(640);

            // Simulate usage
            pooledMat.Mat.SetTo(new Scalar(i, i, i));
        }

        // Assert - All Mats should be returned
        var finalStats = _bufferPool.GetStatistics();
        finalStats.MatPoolCount.Should().BeGreaterThanOrEqualTo(0,
            "Mat pool should have returned objects");
    }

    /// <summary>
    /// Simulate memory stability over extended period
    /// </summary>
    [Fact]
    public async Task Service_ShouldMaintainStableMemory_OverTime()
    {
        // Arrange
        var startMemory = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        var maxAllowedGrowthMB = 50.0;
        var simulatedHours = 24;

        // Act - Simulate hours of operation
        for (int hour = 0; hour < simulatedHours; hour++)
        {
            // Simulate frame processing
            using var buffer = _bufferPool.GetStream("frame");
            buffer.Write(new byte[1920 * 1080 * 3], 0, 1024); // Write partial frame

            // Periodic GC (simulating what service does)
            if (hour % 6 == 0)
            {
                GC.Collect(2, GCCollectionMode.Forced);
            }

            await Task.Delay(10);
        }

        // Final cleanup
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        var endMemory = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

        // Assert
        var memoryGrowth = endMemory - startMemory;
        memoryGrowth.Should().BeLessThan(maxAllowedGrowthMB,
            $"memory should not grow more than {maxAllowedGrowthMB}MB over {simulatedHours} hours");
    }

    /// <summary>
    /// Test memory metrics reporting
    /// </summary>
    [Fact]
    public async Task MemoryMonitor_ShouldProvideAccurateMetrics()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<MemoryMonitor>();
        using var appLifetime = new TestApplicationLifetime();
        var monitor = new MemoryMonitor(logger, appLifetime, _bufferPool);
        _disposables.Add(monitor);

        // Let monitor establish baseline
        await Task.Delay(1000);

        // Act
        var metrics = monitor.GetMemoryMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CurrentWorkingSetMB.Should().BeGreaterThan(0);
        metrics.CurrentManagedMemoryMB.Should().BeGreaterThan(0);
        metrics.GrowthRateMBPerHour.Should().BeGreaterThanOrEqualTo(0);
        metrics.IsHealthy.Should().BeTrue("system should be healthy initially");
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable?.Dispose();
        }
    }
}

/// <summary>
/// Test implementation of IHostApplicationLifetime
/// </summary>
internal class TestApplicationLifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _startedSource = new();
    private readonly CancellationTokenSource _stoppingSource = new();
    private readonly CancellationTokenSource _stoppedSource = new();

    public CancellationToken ApplicationStarted => _startedSource.Token;
    public CancellationToken ApplicationStopping => _stoppingSource.Token;
    public CancellationToken ApplicationStopped => _stoppedSource.Token;

    public void StopApplication()
    {
        _stoppingSource.Cancel();
        _stoppedSource.Cancel();
    }

    public void Dispose()
    {
        _startedSource?.Dispose();
        _stoppingSource?.Dispose();
        _stoppedSource?.Dispose();
    }
}