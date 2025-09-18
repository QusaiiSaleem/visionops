using Microsoft.EntityFrameworkCore;
using VisionOps.Data.Entities;

namespace VisionOps.Data.Repositories;

/// <summary>
/// Repository implementation for detection data access
/// </summary>
public class DetectionRepository : Repository<DetectionEntity>, IDetectionRepository
{
    public DetectionRepository(VisionOpsDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<DetectionEntity>> GetByCameraAsync(string cameraId, DateTime startTime, DateTime endTime)
    {
        return await _dbSet
            .Where(d => d.CameraId == cameraId &&
                       d.Timestamp >= startTime &&
                       d.Timestamp <= endTime)
            .OrderBy(d => d.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<DetectionEntity>> GetUnsyncedAsync(int batchSize = 100)
    {
        return await _dbSet
            .Where(d => !d.IsSynced)
            .OrderBy(d => d.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task MarkAsSyncedAsync(IEnumerable<Guid> detectionIds)
    {
        await _dbSet
            .Where(d => detectionIds.Contains(d.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.IsSynced, true)
                .SetProperty(d => d.SyncedAt, DateTime.UtcNow));
    }

    public async Task<DetectionStats> GetStatsAsync(string cameraId, DateTime startTime, DateTime endTime)
    {
        var detections = await _dbSet
            .Where(d => d.CameraId == cameraId &&
                       d.Timestamp >= startTime &&
                       d.Timestamp <= endTime)
            .ToListAsync();

        var stats = new DetectionStats
        {
            TotalDetections = detections.Count,
            PeopleCount = detections.Count(d => d.ClassName.ToLower() == "person"),
            VehicleCount = detections.Count(d => d.ClassName.ToLower().Contains("vehicle") ||
                                                d.ClassName.ToLower().Contains("car") ||
                                                d.ClassName.ToLower().Contains("truck")),
            UniqueFrames = detections.Select(d => d.FrameNumber).Distinct().Count()
        };

        // Calculate class counts
        var classCounts = detections
            .GroupBy(d => d.ClassName)
            .ToDictionary(g => g.Key, g => g.Count());
        stats.ClassCounts = classCounts;

        // Calculate average confidence
        if (detections.Any())
        {
            stats.AverageConfidence = detections.Average(d => d.Confidence);
        }

        return stats;
    }

    public async Task<IEnumerable<DetectionEntity>> GetByZoneAsync(string cameraId, string zoneName, DateTime startTime, DateTime endTime)
    {
        return await _dbSet
            .Where(d => d.CameraId == cameraId &&
                       d.ZoneName == zoneName &&
                       d.Timestamp >= startTime &&
                       d.Timestamp <= endTime)
            .OrderBy(d => d.Timestamp)
            .ToListAsync();
    }

    public async Task<int> DeleteOldDetectionsAsync(int retentionDays)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        return await _dbSet
            .Where(d => d.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync();
    }
}