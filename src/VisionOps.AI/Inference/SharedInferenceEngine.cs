using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VisionOps.AI.Optimization;

namespace VisionOps.AI.Inference;

/// <summary>
/// CRITICAL Phase 0 Component: Single shared ONNX session pattern.
/// Multiple sessions crash on limited hardware (Intel i3-i5 with 8-12GB RAM).
/// This singleton ensures only one inference session exists per model.
/// </summary>
public sealed class SharedInferenceEngine : IDisposable
{
    private readonly ILogger<SharedInferenceEngine> _logger;
    private readonly Dictionary<string, InferenceSession> _sessions;
    private readonly SemaphoreSlim _sessionLock;
    private readonly ArrayPool<float> _floatPool;
    private readonly SessionOptions _sessionOptions;
    private bool _disposed;

    // Memory constraints and batch processing
    private const int MaxConcurrentInferences = 1; // Sequential processing only
    private const int MinBatchSize = 8;  // Minimum batch size for efficiency
    private const int MaxBatchSize = 16; // Maximum batch size for memory constraints

    private static SharedInferenceEngine? _instance;
    private static readonly object _instanceLock = new();

    /// <summary>
    /// Get the singleton instance
    /// </summary>
    public static SharedInferenceEngine GetInstance(ILogger<SharedInferenceEngine> logger)
    {
        if (_instance == null)
        {
            lock (_instanceLock)
            {
                _instance ??= new SharedInferenceEngine(logger);
            }
        }
        return _instance;
    }

    private SharedInferenceEngine(ILogger<SharedInferenceEngine> logger)
    {
        _logger = logger;
        _sessions = new Dictionary<string, InferenceSession>();
        _sessionLock = new SemaphoreSlim(1, 1);
        _floatPool = ArrayPool<float>.Shared;

        // Configure session for optimal performance on constrained hardware
        _sessionOptions = CreateOptimizedSessionOptions();
    }

    /// <summary>
    /// Create optimized session options for Intel CPUs
    /// </summary>
    private SessionOptions CreateOptimizedSessionOptions()
    {
        var options = new SessionOptions
        {
            // Use all available CPU cores but limit inter-op parallelism
            IntraOpNumThreads = Environment.ProcessorCount,
            InterOpNumThreads = 1, // Sequential execution

            // Memory optimization
            EnableMemoryPattern = true,
            EnableCpuMemArena = true,

            // Execution mode for production
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,

            // Graph optimization level
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,

            // Profiling disabled in production
            EnableProfiling = false
        };

        // Configure OpenVINO execution provider with INT8 support
        var openVinoSettings = new OpenVinoSettings
        {
            DeviceType = OpenVinoDevice.CPU,
            EnableInt8 = true,
            NumThreads = Environment.ProcessorCount,
            EnableDynamicBatch = true,
            EnableThermalThrottling = true
        };

        OpenVinoConfig.ConfigureOpenVino(options, _logger, openVinoSettings);

        // Log hardware capabilities
        var hwInfo = OpenVinoConfig.GetHardwareInfo(_logger);
        _logger.LogInformation("Hardware: {CPU} with {Cores} cores, OpenVINO={Available}",
            hwInfo.CpuName, hwInfo.CpuCores, hwInfo.SupportsOpenVino);

        return options;
    }

    /// <summary>
    /// Get or create a shared session for a model
    /// </summary>
    public async Task<InferenceSession> GetSession(string modelPath, string modelName)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Model file not found: {modelPath}");
        }

        await _sessionLock.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(modelName, out var session))
            {
                _logger.LogInformation("Creating new inference session for {ModelName}", modelName);

                // Load model with INT8 quantization if available
                var modelBytes = await File.ReadAllBytesAsync(modelPath);
                session = new InferenceSession(modelBytes, _sessionOptions);

                _sessions[modelName] = session;

                // Log session info
                LogSessionInfo(session, modelName);
            }

            return session;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// Run inference with memory pooling and throttling support
    /// </summary>
    public async Task<T> RunInference<T>(
        string modelName,
        Func<InferenceSession, Task<T>> inferenceFunc,
        int throttleDelayMs = 0)
    {
        if (!_sessions.TryGetValue(modelName, out var session))
        {
            throw new InvalidOperationException($"No session found for model: {modelName}");
        }

        // Apply thermal throttling delay if needed
        if (throttleDelayMs > 0)
        {
            await Task.Delay(throttleDelayMs);
        }

        // Sequential processing with lock
        await _sessionLock.WaitAsync();
        try
        {
            var startTime = DateTime.UtcNow;
            var result = await inferenceFunc(session);
            var inferenceTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Log if inference is taking too long (>200ms target)
            if (inferenceTime > 200)
            {
                _logger.LogWarning("Slow inference for {ModelName}: {Time}ms", modelName, inferenceTime);
            }

            return result;
        }
        finally
        {
            _sessionLock.Release();

            // Force garbage collection if memory usage is high
            var memoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            if (memoryMB > 4000) // 4GB threshold
            {
                _logger.LogWarning("High memory usage: {Memory}MB - forcing GC", memoryMB);
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }

    /// <summary>
    /// Run batch inference for multiple inputs (optimized for 8-16 frames)
    /// </summary>
    public async Task<List<T>> RunBatchInference<T>(
        string modelName,
        Func<InferenceSession, int, Task<List<T>>> batchInferenceFunc,
        int batchSize,
        int throttleDelayMs = 0)
    {
        if (batchSize < MinBatchSize || batchSize > MaxBatchSize)
        {
            _logger.LogWarning("Batch size {Size} outside optimal range {Min}-{Max}, adjusting",
                batchSize, MinBatchSize, MaxBatchSize);
            batchSize = Math.Clamp(batchSize, MinBatchSize, MaxBatchSize);
        }

        if (!_sessions.TryGetValue(modelName, out var session))
        {
            throw new InvalidOperationException($"No session found for model: {modelName}");
        }

        // Apply thermal throttling delay if needed
        if (throttleDelayMs > 0)
        {
            await Task.Delay(throttleDelayMs);
        }

        await _sessionLock.WaitAsync();
        try
        {
            var startTime = DateTime.UtcNow;
            var results = await batchInferenceFunc(session, batchSize);
            var inferenceTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var perFrameTime = inferenceTime / batchSize;

            _logger.LogInformation("Batch inference for {ModelName}: {Total}ms total, {PerFrame}ms per frame",
                modelName, inferenceTime, perFrameTime);

            // Warn if per-frame inference exceeds target
            if (perFrameTime > 25) // 25ms per frame for batch of 8-16
            {
                _logger.LogWarning("Slow batch inference for {ModelName}: {Time}ms per frame",
                    modelName, perFrameTime);
            }

            return results;
        }
        finally
        {
            _sessionLock.Release();

            // Force garbage collection if memory usage is high
            var memoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            if (memoryMB > 4000) // 4GB threshold
            {
                _logger.LogWarning("High memory usage after batch: {Memory}MB - forcing GC", memoryMB);
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }

    /// <summary>
    /// Create tensor with memory pooling
    /// </summary>
    public DenseTensor<float> CreatePooledTensor(int[] dimensions)
    {
        var totalSize = 1;
        foreach (var dim in dimensions)
        {
            totalSize *= dim;
        }

        var buffer = _floatPool.Rent(totalSize);
        Array.Clear(buffer, 0, totalSize);

        return new DenseTensor<float>(buffer, dimensions);
    }

    /// <summary>
    /// Return pooled tensor memory
    /// </summary>
    public void ReturnPooledTensor(DenseTensor<float> tensor)
    {
        if (tensor.Buffer.ToArray() is float[] buffer)
        {
            _floatPool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Log session information for debugging
    /// </summary>
    private void LogSessionInfo(InferenceSession session, string modelName)
    {
        try
        {
            var inputMeta = session.InputMetadata;
            var outputMeta = session.OutputMetadata;

            _logger.LogInformation("Model {ModelName} loaded:", modelName);
            _logger.LogInformation("  Inputs: {Inputs}", string.Join(", ", inputMeta.Keys));
            _logger.LogInformation("  Outputs: {Outputs}", string.Join(", ", outputMeta.Keys));

            // Estimate memory usage
            long estimatedMemory = 0;
            foreach (var input in inputMeta.Values)
            {
                if (input.Dimensions != null)
                {
                    long size = 4; // float size
                    foreach (var dim in input.Dimensions)
                    {
                        if (dim > 0) size *= dim;
                    }
                    estimatedMemory += size;
                }
            }

            _logger.LogInformation("  Estimated tensor memory: {Memory}MB",
                estimatedMemory / (1024 * 1024));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log session info for {ModelName}", modelName);
        }
    }

    /// <summary>
    /// Get memory usage of all sessions
    /// </summary>
    public long GetTotalMemoryUsageMB()
    {
        // Approximate based on loaded sessions
        // Each quantized model is roughly 120-200MB
        return _sessions.Count * 150;
    }

    /// <summary>
    /// Clear all sessions (use carefully)
    /// </summary>
    public async Task ClearSessions()
    {
        await _sessionLock.WaitAsync();
        try
        {
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
            _sessions.Clear();

            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _logger.LogInformation("All inference sessions cleared");
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _sessionLock.Wait();
        try
        {
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
            _sessions.Clear();
        }
        finally
        {
            _sessionLock.Release();
        }

        _sessionLock.Dispose();
        _sessionOptions?.Dispose();
        _disposed = true;

        // Clear singleton reference
        lock (_instanceLock)
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}