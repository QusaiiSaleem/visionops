using System.Buffers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using WebPWrapper;

namespace VisionOps.Video.Compression;

/// <summary>
/// WebP compression service for key frames.
/// Target: 3-5KB per frame at 320x240 resolution.
/// </summary>
public class WebPCompressor : IDisposable
{
    private readonly ILogger<WebPCompressor> _logger;
    private readonly ArrayPool<byte> _arrayPool;
    private readonly object _compressionLock = new();
    private bool _disposed;

    // Compression settings for 3-5KB target
    private const int TargetWidth = 320;
    private const int TargetHeight = 240;
    private const int WebPQuality = 20; // Low quality for extreme compression
    private const int JpegFallbackQuality = 30; // Fallback if WebP fails
    private const int MaxOutputSize = 5120; // 5KB maximum

    public WebPCompressor(ILogger<WebPCompressor> logger)
    {
        _logger = logger;
        _arrayPool = ArrayPool<byte>.Shared;
    }

    /// <summary>
    /// Compress a frame to WebP format with extreme compression
    /// </summary>
    public async Task<byte[]?> CompressFrameAsync(
        Mat frame,
        bool isKeyFrame = false,
        CancellationToken cancellationToken = default)
    {
        if (frame == null || frame.Empty())
        {
            _logger.LogWarning("Cannot compress null or empty frame");
            return null;
        }

        return await Task.Run(() => CompressFrameInternal(frame, isKeyFrame), cancellationToken);
    }

    /// <summary>
    /// Internal compression implementation
    /// </summary>
    private byte[]? CompressFrameInternal(Mat frame, bool isKeyFrame)
    {
        lock (_compressionLock)
        {
            Mat? resized = null;
            Mat? blurred = null;

            try
            {
                // Step 1: Resize to target dimensions
                var targetSize = isKeyFrame
                    ? new Size(TargetWidth, TargetHeight)  // Key frames: 320x240
                    : new Size(160, 120);  // Regular frames: even smaller

                resized = new Mat();
                Cv2.Resize(frame, resized, targetSize, interpolation: InterpolationFlags.Area);

                // Step 2: Apply face blurring for GDPR compliance
                blurred = ApplyPrivacyBlur(resized);

                // Step 3: Try WebP compression
                var webpData = CompressToWebP(blurred ?? resized, isKeyFrame);

                if (webpData != null && webpData.Length <= MaxOutputSize)
                {
                    _logger.LogDebug("WebP compression successful: {Size} bytes", webpData.Length);
                    return webpData;
                }

                // Step 4: Fallback to JPEG if WebP fails or is too large
                var jpegData = CompressToJpeg(blurred ?? resized, isKeyFrame);

                if (jpegData != null)
                {
                    _logger.LogDebug("JPEG fallback compression: {Size} bytes", jpegData.Length);
                    return jpegData;
                }

                _logger.LogWarning("All compression methods failed");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during frame compression");
                return null;
            }
            finally
            {
                resized?.Dispose();
                blurred?.Dispose();
            }
        }
    }

    /// <summary>
    /// Compress frame to WebP format
    /// </summary>
    private byte[]? CompressToWebP(Mat frame, bool isKeyFrame)
    {
        try
        {
            // Convert Mat to byte array (BGR format)
            var imageBytes = new byte[frame.Total() * frame.Channels()];
            System.Runtime.InteropServices.Marshal.Copy(
                frame.Data, imageBytes, 0, imageBytes.Length);

            var quality = isKeyFrame ? WebPQuality : WebPQuality - 10;

            using var webp = new WebP();
            var compressed = webp.EncodeBGR(
                imageBytes,
                frame.Width,
                frame.Height,
                frame.Width * frame.Channels(),
                quality);

            if (compressed != null && compressed.Length > 0)
            {
                return compressed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebP compression failed");
        }

        return null;
    }

    /// <summary>
    /// Fallback compression to JPEG format
    /// </summary>
    private byte[]? CompressToJpeg(Mat frame, bool isKeyFrame)
    {
        try
        {
            var quality = isKeyFrame ? JpegFallbackQuality : JpegFallbackQuality - 10;

            var parameters = new ImageEncodingParam[]
            {
                new(ImwriteFlags.JpegQuality, quality),
                new(ImwriteFlags.JpegOptimize, 1),
                new(ImwriteFlags.JpegProgressive, 0)
            };

            return frame.ImEncode(".jpg", parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JPEG compression failed");
            return null;
        }
    }

    /// <summary>
    /// Apply privacy blur to detected faces (GDPR compliance)
    /// </summary>
    private Mat? ApplyPrivacyBlur(Mat frame)
    {
        try
        {
            // For now, return null to use original
            // In production, implement face detection and blurring
            // using a lightweight face detector
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Privacy blur failed");
            return null;
        }
    }

    /// <summary>
    /// Batch compress multiple frames
    /// </summary>
    public async Task<Dictionary<string, byte[]>> CompressBatchAsync(
        Dictionary<string, Mat> frames,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, byte[]>();

        // Process sequentially to avoid memory spikes
        foreach (var kvp in frames)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var compressed = await CompressFrameAsync(kvp.Value, true, cancellationToken);
            if (compressed != null)
            {
                results[kvp.Key] = compressed;
            }

            // Small delay to prevent CPU saturation
            await Task.Delay(10, cancellationToken);
        }

        return results;
    }

    /// <summary>
    /// Get compression statistics
    /// </summary>
    public CompressionStats GetStats(byte[] originalData, byte[] compressedData)
    {
        return new CompressionStats
        {
            OriginalSize = originalData.Length,
            CompressedSize = compressedData.Length,
            CompressionRatio = (float)compressedData.Length / originalData.Length,
            SavingsPercent = (1.0f - ((float)compressedData.Length / originalData.Length)) * 100
        };
    }

    /// <summary>
    /// Estimate memory usage for compression
    /// </summary>
    public long EstimateMemoryUsage(int frameCount, bool keyFrames = false)
    {
        var frameSize = keyFrames
            ? TargetWidth * TargetHeight * 3  // RGB channels
            : 160 * 120 * 3;

        // Account for input, output, and working buffers
        return frameCount * frameSize * 3;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// Compression statistics
/// </summary>
public class CompressionStats
{
    public int OriginalSize { get; set; }
    public int CompressedSize { get; set; }
    public float CompressionRatio { get; set; }
    public float SavingsPercent { get; set; }

    public override string ToString()
    {
        return $"Original: {OriginalSize:N0} bytes, " +
               $"Compressed: {CompressedSize:N0} bytes, " +
               $"Ratio: {CompressionRatio:P2}, " +
               $"Savings: {SavingsPercent:F1}%";
    }
}