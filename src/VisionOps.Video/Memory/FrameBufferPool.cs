using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using OpenCvSharp;

namespace VisionOps.Video.Memory;

/// <summary>
/// CRITICAL Phase 0 Component: Centralized memory pool for all video operations.
/// This prevents memory fragmentation and ensures zero memory growth over 24 hours.
/// All video buffers MUST come from this pool.
/// </summary>
public sealed class FrameBufferPool : IDisposable
{
    private readonly ILogger<FrameBufferPool> _logger;
    private readonly ArrayPool<byte> _arrayPool;
    private readonly RecyclableMemoryStreamManager _streamManager;
    private readonly ConcurrentBag<PooledMat> _matPool;
    private readonly SemaphoreSlim _poolSemaphore;

    // Memory constraints
    private const int MaxFrameSize = 1920 * 1080 * 3; // HD frame BGR
    private const int MaxPooledMats = 30; // Maximum Mat objects in pool
    private const int MaxArraysInPool = 50; // Maximum byte arrays
    private const int StreamBlockSize = 128 * 1024; // 128KB blocks
    private const int MaxStreamSize = 10 * 1024 * 1024; // 10MB max stream

    // Tracking
    private long _totalAllocated;
    private long _totalReturned;
    private long _currentlyInUse;
    private int _peakUsage;
    private readonly object _statsLock = new();

    public FrameBufferPool(ILogger<FrameBufferPool> logger)
    {
        _logger = logger;
        _poolSemaphore = new SemaphoreSlim(MaxPooledMats, MaxPooledMats);

        // Configure ArrayPool with specific size
        _arrayPool = ArrayPool<byte>.Create(MaxFrameSize, MaxArraysInPool);

        // Configure RecyclableMemoryStreamManager for video operations
        // v3.0 API changes: removed blockSize parameter, removed obsolete properties
        _streamManager = new RecyclableMemoryStreamManager();

        // Pre-populate Mat pool
        _matPool = new ConcurrentBag<PooledMat>();
        for (int i = 0; i < MaxPooledMats / 2; i++)
        {
            _matPool.Add(new PooledMat());
        }

        _logger.LogInformation(
            "FrameBufferPool initialized: MaxFrameSize={MaxFrameSize}, MaxMats={MaxMats}, MaxArrays={MaxArrays}",
            MaxFrameSize, MaxPooledMats, MaxArraysInPool);
    }

    /// <summary>
    /// Rent a byte array for frame data. MUST be returned after use.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] RentBuffer(int size)
    {
        if (size > MaxFrameSize)
        {
            throw new ArgumentException($"Requested size {size} exceeds maximum frame size {MaxFrameSize}");
        }

        var buffer = _arrayPool.Rent(size);

        lock (_statsLock)
        {
            _totalAllocated++;
            _currentlyInUse++;
            if (_currentlyInUse > _peakUsage)
                _peakUsage = (int)_currentlyInUse;
        }

        return buffer;
    }

    /// <summary>
    /// Return a rented buffer to the pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReturnBuffer(byte[] buffer, bool clearBuffer = false)
    {
        if (buffer == null) return;

        _arrayPool.Return(buffer, clearBuffer);

        lock (_statsLock)
        {
            _totalReturned++;
            _currentlyInUse--;
        }
    }

    /// <summary>
    /// Get a pooled Mat object. MUST be disposed after use.
    /// </summary>
    public async Task<PooledMat> RentMatAsync(int height, int width, MatType? type = null)
    {
        await _poolSemaphore.WaitAsync();

        try
        {
            var matType = type ?? MatType.CV_8UC3;
            if (_matPool.TryTake(out var pooledMat))
            {
                pooledMat.Reset(height, width, matType);
                return pooledMat;
            }

            // Create new if pool is empty (shouldn't happen after warmup)
            _logger.LogWarning("Mat pool empty, creating new Mat");
            return new PooledMat(height, width, matType);
        }
        finally
        {
            // Semaphore will be released when PooledMat is disposed
        }
    }

    /// <summary>
    /// Return a Mat to the pool
    /// </summary>
    internal void ReturnMat(PooledMat mat)
    {
        if (mat == null) return;

        mat.Clear();

        if (_matPool.Count < MaxPooledMats)
        {
            _matPool.Add(mat);
        }
        else
        {
            mat.Dispose();
        }

        _poolSemaphore.Release();
    }

    /// <summary>
    /// Get a recyclable memory stream
    /// </summary>
    public RecyclableMemoryStream GetStream(string? tag = null)
    {
        return _streamManager.GetStream(tag ?? "VideoFrame");
    }

    /// <summary>
    /// Get a stream with initial capacity
    /// </summary>
    public RecyclableMemoryStream GetStream(int capacity, string? tag = null)
    {
        return _streamManager.GetStream(tag ?? "VideoFrame", capacity);
    }

    /// <summary>
    /// Get memory statistics for monitoring
    /// </summary>
    public MemoryPoolStats GetStatistics()
    {
        lock (_statsLock)
        {
            return new MemoryPoolStats
            {
                TotalAllocated = _totalAllocated,
                TotalReturned = _totalReturned,
                CurrentlyInUse = _currentlyInUse,
                PeakUsage = _peakUsage,
                LeakedBuffers = _totalAllocated - _totalReturned,
                SmallPoolInUse = _streamManager.SmallPoolInUseSize,
                LargePoolInUse = _streamManager.LargePoolInUseSize,
                MatPoolCount = _matPool.Count
            };
        }
    }

    /// <summary>
    /// Check for memory leaks
    /// </summary>
    public bool HasMemoryLeak()
    {
        var stats = GetStatistics();

        // Leak detected if more than 10 buffers not returned after 1 hour
        if (stats.LeakedBuffers > 10)
        {
            _logger.LogWarning(
                "Potential memory leak detected: {Leaked} buffers not returned (Allocated: {Allocated}, Returned: {Returned})",
                stats.LeakedBuffers, stats.TotalAllocated, stats.TotalReturned);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Force garbage collection and compact LOH
    /// </summary>
    public void ForceCleanup()
    {
        _logger.LogInformation("Forcing memory cleanup");

        // Clear excess buffers from pools
        while (_matPool.Count > MaxPooledMats / 2 && _matPool.TryTake(out var mat))
        {
            mat.Dispose();
        }

        // Force GC
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        // Compact LOH
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();

        _logger.LogInformation("Memory cleanup completed");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing FrameBufferPool");

        // Dispose all pooled Mats
        while (_matPool.TryTake(out var mat))
        {
            mat.Dispose();
        }

        // RecyclableMemoryStreamManager v3.0 doesn't implement IDisposable
        // _streamManager.Dispose(); // Removed in v3.0
        _poolSemaphore.Dispose();

        // Log final statistics
        var stats = GetStatistics();
        _logger.LogInformation(
            "FrameBufferPool disposed. Final stats: Leaked={Leaked}, Peak={Peak}",
            stats.LeakedBuffers, stats.PeakUsage);
    }
}

/// <summary>
/// Pooled Mat wrapper that automatically returns to pool on dispose
/// </summary>
public sealed class PooledMat : IDisposable
{
    private Mat _mat;
    private static FrameBufferPool? _pool;
    private bool _disposed;

    internal PooledMat(int height = 1080, int width = 1920, MatType? type = null)
    {
        _mat = new Mat(height, width, type ?? MatType.CV_8UC3);
    }

    internal void Reset(int height, int width, MatType type)
    {
        if (_mat.Height != height || _mat.Width != width || _mat.Type() != type)
        {
            _mat.Dispose();
            _mat = new Mat(height, width, type);
        }
    }

    internal void Clear()
    {
        _mat.SetTo(0);
    }

    public Mat Mat => _mat;

    public static void SetPool(FrameBufferPool pool)
    {
        _pool = pool;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _pool?.ReturnMat(this);
        _disposed = true;
    }
}

/// <summary>
/// Memory pool statistics for monitoring
/// </summary>
public class MemoryPoolStats
{
    public long TotalAllocated { get; init; }
    public long TotalReturned { get; init; }
    public long CurrentlyInUse { get; init; }
    public int PeakUsage { get; init; }
    public long LeakedBuffers { get; init; }
    public long SmallPoolInUse { get; init; }
    public long LargePoolInUse { get; init; }
    public int MatPoolCount { get; init; }

    public double LeakRatePerHour(TimeSpan elapsed)
    {
        if (elapsed.TotalHours == 0) return 0;
        return LeakedBuffers / elapsed.TotalHours;
    }
}

/// <summary>
/// Legacy compatibility - maps to MemoryPoolStats
/// </summary>
public class PoolStatistics
{
    public long TotalRentals { get; init; }
    public long TotalReturns { get; init; }
    public long CurrentActive { get; init; }
    public long LeakedBuffers { get; init; }
}