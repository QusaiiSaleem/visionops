using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System.Runtime.InteropServices;

namespace VisionOps.AI.Optimization;

/// <summary>
/// OpenVINO execution provider configuration for Intel hardware optimization
/// Provides INT8 inference acceleration on Intel CPUs and integrated GPUs
/// </summary>
public static class OpenVinoConfig
{
    /// <summary>
    /// Configure session options for OpenVINO execution provider
    /// </summary>
    public static void ConfigureOpenVino(
        SessionOptions options,
        ILogger? logger = null,
        OpenVinoSettings? settings = null)
    {
        settings ??= new OpenVinoSettings();

        try
        {
            // Check if OpenVINO is available
            if (!IsOpenVinoAvailable())
            {
                logger?.LogWarning("OpenVINO runtime not found, falling back to CPU provider");
                options.AppendExecutionProvider_CPU();
                return;
            }

            // Configure OpenVINO settings
            var openvinoOptions = new Dictionary<string, string>();

            // Device selection (CPU, GPU, AUTO)
            openvinoOptions["device_type"] = settings.DeviceType.ToString();

            // Enable INT8 inference if supported
            if (settings.EnableInt8)
            {
                openvinoOptions["precision"] = "INT8";
                logger?.LogInformation("OpenVINO INT8 precision enabled");
            }

            // Cache directory for compiled models
            if (!string.IsNullOrEmpty(settings.CacheDir))
            {
                Directory.CreateDirectory(settings.CacheDir);
                openvinoOptions["cache_dir"] = settings.CacheDir;
                logger?.LogDebug("OpenVINO cache directory: {Dir}", settings.CacheDir);
            }

            // Number of threads for CPU inference
            if (settings.NumThreads > 0)
            {
                openvinoOptions["num_threads"] = settings.NumThreads.ToString();
            }

            // Enable dynamic batching
            if (settings.EnableDynamicBatch)
            {
                openvinoOptions["enable_dynamic_batch"] = "true";
            }

            // Thermal throttling hint
            if (settings.EnableThermalThrottling)
            {
                openvinoOptions["hint_performance_mode"] = "LATENCY";
                openvinoOptions["hint_num_requests"] = "1";
            }

            // Add OpenVINO execution provider
            options.AppendExecutionProvider("OpenVINO", openvinoOptions);

            logger?.LogInformation(
                "OpenVINO execution provider configured: Device={Device}, INT8={Int8}, Threads={Threads}",
                settings.DeviceType,
                settings.EnableInt8,
                settings.NumThreads);

            // Verify configuration
            VerifyOpenVinoConfig(options, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to configure OpenVINO, falling back to CPU");
            options.AppendExecutionProvider_CPU();
        }
    }

    /// <summary>
    /// Check if OpenVINO runtime is available
    /// </summary>
    private static bool IsOpenVinoAvailable()
    {
        try
        {
            // Check for OpenVINO DLL on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var openvinoDll = "openvino.dll";
                var handle = NativeLibrary.TryLoad(openvinoDll, out _);
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                    return true;
                }
            }
            // Check for OpenVINO shared library on Linux/Mac
            else
            {
                var openvinoLib = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "libopenvino.so"
                    : "libopenvino.dylib";

                var handle = NativeLibrary.TryLoad(openvinoLib, out _);
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                    return true;
                }
            }

            // Check environment variable
            var openvinoPath = Environment.GetEnvironmentVariable("INTEL_OPENVINO_DIR");
            return !string.IsNullOrEmpty(openvinoPath) && Directory.Exists(openvinoPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verify OpenVINO configuration
    /// </summary>
    private static void VerifyOpenVinoConfig(SessionOptions options, ILogger? logger)
    {
        try
        {
            // Create a simple test model to verify OpenVINO works
            var testModelPath = CreateTestModel();
            if (File.Exists(testModelPath))
            {
                using var testSession = new InferenceSession(testModelPath, options);
                var providers = GetExecutionProviders(testSession);

                if (providers.Contains("OpenVINOExecutionProvider"))
                {
                    logger?.LogInformation("OpenVINO execution provider verified successfully");
                }
                else
                {
                    logger?.LogWarning("OpenVINO provider not active, available providers: {Providers}",
                        string.Join(", ", providers));
                }

                // Clean up test model
                File.Delete(testModelPath);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "OpenVINO verification failed");
        }
    }

    /// <summary>
    /// Get list of execution providers from session
    /// </summary>
    private static List<string> GetExecutionProviders(InferenceSession session)
    {
        // This would normally use session.GetProviders() if available
        // For now, return a placeholder list
        return new List<string> { "CPUExecutionProvider" };
    }

    /// <summary>
    /// Create a simple test ONNX model
    /// </summary>
    private static string CreateTestModel()
    {
        // In production, this would create a minimal ONNX model
        // For now, return a path that doesn't exist
        return Path.GetTempFileName() + ".onnx";
    }

    /// <summary>
    /// Get Intel CPU/GPU information
    /// </summary>
    public static IntelHardwareInfo GetHardwareInfo(ILogger? logger = null)
    {
        var info = new IntelHardwareInfo();

        try
        {
            // Get CPU information
            info.CpuName = GetCpuName();
            info.CpuCores = Environment.ProcessorCount;
            info.HasAvx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported;
            info.HasAvx512 = System.Runtime.Intrinsics.X86.Avx512F.IsSupported;

            // Check for Intel integrated GPU
            info.HasIntelGpu = CheckForIntelGpu();

            // Check OpenVINO support
            info.SupportsOpenVino = IsOpenVinoAvailable();

            logger?.LogInformation(
                "Intel Hardware: {Cpu} ({Cores} cores), AVX2={Avx2}, AVX512={Avx512}, GPU={Gpu}, OpenVINO={Ovino}",
                info.CpuName,
                info.CpuCores,
                info.HasAvx2,
                info.HasAvx512,
                info.HasIntelGpu,
                info.SupportsOpenVino);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to get hardware info");
        }

        return info;
    }

    /// <summary>
    /// Get CPU name
    /// </summary>
    private static string GetCpuName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString")?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
        else
        {
            // On Linux/Mac, read from /proc/cpuinfo or system_profiler
            return "Intel CPU";
        }
    }

    /// <summary>
    /// Check for Intel integrated GPU
    /// </summary>
    private static bool CheckForIntelGpu()
    {
        // This would normally query GPU devices
        // For now, return false as a safe default
        return false;
    }
}

/// <summary>
/// OpenVINO execution provider settings
/// </summary>
public class OpenVinoSettings
{
    /// <summary>
    /// Device to run inference on
    /// </summary>
    public OpenVinoDevice DeviceType { get; set; } = OpenVinoDevice.CPU;

    /// <summary>
    /// Enable INT8 quantized inference
    /// </summary>
    public bool EnableInt8 { get; set; } = true;

    /// <summary>
    /// Number of CPU threads (0 = auto)
    /// </summary>
    public int NumThreads { get; set; } = 0;

    /// <summary>
    /// Directory for caching compiled models
    /// </summary>
    public string CacheDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisionOps",
        "OpenVinoCache");

    /// <summary>
    /// Enable dynamic batching
    /// </summary>
    public bool EnableDynamicBatch { get; set; } = true;

    /// <summary>
    /// Enable thermal throttling optimizations
    /// </summary>
    public bool EnableThermalThrottling { get; set; } = true;

    /// <summary>
    /// Memory pool size in MB
    /// </summary>
    public int MemoryPoolSizeMB { get; set; } = 512;
}

/// <summary>
/// OpenVINO device types
/// </summary>
public enum OpenVinoDevice
{
    CPU,     // Intel CPU
    GPU,     // Intel integrated GPU
    AUTO,    // Automatic device selection
    MULTI,   // Multiple devices
    HETERO   // Heterogeneous (fallback chain)
}

/// <summary>
/// Intel hardware information
/// </summary>
public class IntelHardwareInfo
{
    public string CpuName { get; set; } = "Unknown";
    public int CpuCores { get; set; }
    public bool HasAvx2 { get; set; }
    public bool HasAvx512 { get; set; }
    public bool HasIntelGpu { get; set; }
    public bool SupportsOpenVino { get; set; }
    public string OpenVinoVersion { get; set; } = "Unknown";
}