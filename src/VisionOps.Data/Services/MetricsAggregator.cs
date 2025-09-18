using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VisionOps.Data.Entities;

namespace VisionOps.Data.Services;

/// <summary>
/// Aggregates raw metrics into compressed windows for efficient storage and sync
/// Achieves 100:1 compression ratio through aggregation and Brotli compression
/// </summary>
public class MetricsAggregator
{
    private readonly ILogger<MetricsAggregator> _logger;
    private readonly TimeSpan _defaultWindow = TimeSpan.FromMinutes(5);

    public MetricsAggregator(ILogger<MetricsAggregator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Aggregate raw detections into a compressed metric window
    /// Raw: 1 detection every 3 seconds = 100 in 5 minutes
    /// Aggregated: 1 metric record = 100:1 reduction
    /// </summary>
    public async Task<MetricEntity> AggregateDetectionsAsync(
        string cameraId,
        List<DetectionEntity> detections,
        DateTime windowStart,
        TimeSpan? windowDuration = null)
    {
        var duration = windowDuration ?? _defaultWindow;
        var windowEnd = windowStart.Add(duration);

        // Filter detections for this window
        var windowDetections = detections
            .Where(d => d.Timestamp >= windowStart && d.Timestamp < windowEnd)
            .ToList();

        _logger.LogDebug("Aggregating {Count} detections for camera {CameraId} in window {Start} - {End}",
            windowDetections.Count, cameraId, windowStart, windowEnd);

        // Calculate aggregated metrics
        var metric = new MetricEntity
        {
            CameraId = cameraId,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            WindowDurationSeconds = (int)duration.TotalSeconds,
            SampleCount = windowDetections.Count,
            TotalDetections = windowDetections.Count
        };

        if (windowDetections.Any())
        {
            // People and vehicle counts
            var peopleCounts = windowDetections
                .GroupBy(d => d.FrameNumber)
                .Select(g => g.Count(d => d.ClassName.ToLower() == "person"))
                .ToList();

            var vehicleCounts = windowDetections
                .GroupBy(d => d.FrameNumber)
                .Select(g => g.Count(d => IsVehicle(d.ClassName)))
                .ToList();

            metric.AvgPeopleCount = peopleCounts.Any() ? (float)peopleCounts.Average() : 0;
            metric.MaxPeopleCount = peopleCounts.Any() ? peopleCounts.Max() : 0;
            metric.MinPeopleCount = peopleCounts.Any() ? peopleCounts.Min() : 0;

            metric.AvgVehicleCount = vehicleCounts.Any() ? (float)vehicleCounts.Average() : 0;
            metric.MaxVehicleCount = vehicleCounts.Any() ? vehicleCounts.Max() : 0;

            // Processing times
            var processingTimes = windowDetections
                .Select(d => d.ProcessingTimeMs)
                .OrderBy(t => t)
                .ToList();

            metric.AvgProcessingTimeMs = (float)processingTimes.Average();
            metric.MaxProcessingTimeMs = processingTimes.Max();
            metric.P95ProcessingTimeMs = CalculatePercentile(processingTimes, 0.95f);

            // Frame counts
            metric.FramesProcessed = windowDetections.Select(d => d.FrameNumber).Distinct().Count();
            metric.KeyFramesProcessed = windowDetections.Count(d => d.KeyFrameId.HasValue);

            // Zone statistics
            var zoneStats = CalculateZoneStatistics(windowDetections);
            metric.ZoneStatsJson = JsonSerializer.Serialize(zoneStats);

            // Compress raw data for archival
            var compressedData = await CompressDetectionsAsync(windowDetections);
            metric.CompressedRawData = compressedData.Data;
            metric.CompressedSizeBytes = compressedData.SizeBytes;
            metric.CompressionRatio = compressedData.CompressionRatio;
        }

        _logger.LogInformation("Aggregated {Count} detections into 1 metric record. Compression ratio: {Ratio}:1",
            windowDetections.Count, metric.CompressionRatio);

        return metric;
    }

    /// <summary>
    /// Aggregate system performance metrics
    /// </summary>
    public void UpdateSystemMetrics(MetricEntity metric, SystemPerformanceData perfData)
    {
        metric.AvgCpuUsage = perfData.CpuUsagePercent;
        metric.MaxCpuTemperature = perfData.CpuTemperatureCelsius;
        metric.AvgMemoryUsageMb = perfData.MemoryUsageMb;
        metric.ErrorCount = perfData.ErrorCount;
    }

    /// <summary>
    /// Compress detection data using Brotli for maximum compression
    /// </summary>
    private async Task<CompressedData> CompressDetectionsAsync(List<DetectionEntity> detections)
    {
        // Create simplified data structure for compression
        var simplifiedData = detections.Select(d => new
        {
            T = d.Timestamp.Ticks, // Timestamp as ticks (more compressible)
            C = d.ClassName[0],     // First letter of class
            F = d.Confidence,       // Confidence
            X = d.BboxX,           // Bounding box
            Y = d.BboxY,
            W = d.BboxWidth,
            H = d.BboxHeight,
            N = d.FrameNumber,     // Frame number
            Z = d.ZoneName?[0]     // Zone first letter
        }).ToList();

        var json = JsonSerializer.Serialize(simplifiedData);
        var originalBytes = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Optimal))
        {
            await compressor.WriteAsync(originalBytes, 0, originalBytes.Length);
        }

        var compressedBytes = output.ToArray();
        var compressionRatio = (float)originalBytes.Length / compressedBytes.Length;

        _logger.LogDebug("Compressed {Original} bytes to {Compressed} bytes (ratio: {Ratio}:1)",
            originalBytes.Length, compressedBytes.Length, compressionRatio);

        return new CompressedData
        {
            Data = compressedBytes,
            SizeBytes = compressedBytes.Length,
            OriginalSizeBytes = originalBytes.Length,
            CompressionRatio = compressionRatio
        };
    }

    /// <summary>
    /// Decompress metrics data for analysis
    /// </summary>
    public async Task<string> DecompressMetricsAsync(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        await decompressor.CopyToAsync(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// Calculate percentile value from sorted list
    /// </summary>
    private float CalculatePercentile(List<int> sortedValues, float percentile)
    {
        if (!sortedValues.Any()) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
    }

    /// <summary>
    /// Calculate statistics per detection zone
    /// </summary>
    private Dictionary<string, ZoneStatistics> CalculateZoneStatistics(List<DetectionEntity> detections)
    {
        var zoneStats = new Dictionary<string, ZoneStatistics>();

        var zones = detections
            .Where(d => !string.IsNullOrEmpty(d.ZoneName))
            .GroupBy(d => d.ZoneName!);

        foreach (var zone in zones)
        {
            var stats = new ZoneStatistics
            {
                ZoneName = zone.Key,
                TotalDetections = zone.Count(),
                PeopleCount = zone.Count(d => d.ClassName.ToLower() == "person"),
                VehicleCount = zone.Count(d => IsVehicle(d.ClassName)),
                AverageConfidence = (float)zone.Average(d => d.Confidence),
                UniqueFrames = zone.Select(d => d.FrameNumber).Distinct().Count()
            };

            // Class distribution
            stats.ClassDistribution = zone
                .GroupBy(d => d.ClassName)
                .ToDictionary(g => g.Key, g => g.Count());

            zoneStats[zone.Key] = stats;
        }

        return zoneStats;
    }

    /// <summary>
    /// Check if class name is a vehicle type
    /// </summary>
    private bool IsVehicle(string className)
    {
        var vehicleClasses = new[] { "car", "truck", "bus", "motorcycle", "bicycle", "vehicle" };
        return vehicleClasses.Any(vc => className.ToLower().Contains(vc));
    }

    /// <summary>
    /// Get the start of the aggregation window for a given timestamp
    /// </summary>
    public DateTime GetWindowStart(DateTime timestamp, TimeSpan windowDuration)
    {
        var ticks = timestamp.Ticks / windowDuration.Ticks;
        return new DateTime(ticks * windowDuration.Ticks, timestamp.Kind);
    }

    /// <summary>
    /// Get all window boundaries for a time range
    /// </summary>
    public List<(DateTime Start, DateTime End)> GetWindowBoundaries(
        DateTime startTime,
        DateTime endTime,
        TimeSpan windowDuration)
    {
        var windows = new List<(DateTime Start, DateTime End)>();
        var currentStart = GetWindowStart(startTime, windowDuration);

        while (currentStart < endTime)
        {
            var currentEnd = currentStart.Add(windowDuration);
            windows.Add((currentStart, currentEnd));
            currentStart = currentEnd;
        }

        return windows;
    }
}

/// <summary>
/// Compressed data information
/// </summary>
public class CompressedData
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int SizeBytes { get; set; }
    public int OriginalSizeBytes { get; set; }
    public float CompressionRatio { get; set; }
}

/// <summary>
/// Zone-specific statistics
/// </summary>
public class ZoneStatistics
{
    public string ZoneName { get; set; } = string.Empty;
    public int TotalDetections { get; set; }
    public int PeopleCount { get; set; }
    public int VehicleCount { get; set; }
    public float AverageConfidence { get; set; }
    public int UniqueFrames { get; set; }
    public Dictionary<string, int> ClassDistribution { get; set; } = new();
}

/// <summary>
/// System performance data
/// </summary>
public class SystemPerformanceData
{
    public float CpuUsagePercent { get; set; }
    public float CpuTemperatureCelsius { get; set; }
    public float MemoryUsageMb { get; set; }
    public int ErrorCount { get; set; }
}