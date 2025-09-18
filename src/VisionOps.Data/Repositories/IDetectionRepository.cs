using VisionOps.Data.Entities;

namespace VisionOps.Data.Repositories;

/// <summary>
/// Repository interface for detection data access
/// </summary>
public interface IDetectionRepository : IRepository<DetectionEntity>
{
    /// <summary>
    /// Get detections for a specific camera within a time range
    /// </summary>
    Task<IEnumerable<DetectionEntity>> GetByCameraAsync(string cameraId, DateTime startTime, DateTime endTime);

    /// <summary>
    /// Get unsynced detections for batch upload
    /// </summary>
    Task<IEnumerable<DetectionEntity>> GetUnsyncedAsync(int batchSize = 100);

    /// <summary>
    /// Mark detections as synced
    /// </summary>
    Task MarkAsSyncedAsync(IEnumerable<Guid> detectionIds);

    /// <summary>
    /// Get detection statistics for a time window
    /// </summary>
    Task<DetectionStats> GetStatsAsync(string cameraId, DateTime startTime, DateTime endTime);

    /// <summary>
    /// Get detections by zone
    /// </summary>
    Task<IEnumerable<DetectionEntity>> GetByZoneAsync(string cameraId, string zoneName, DateTime startTime, DateTime endTime);

    /// <summary>
    /// Delete old detections based on retention policy
    /// </summary>
    Task<int> DeleteOldDetectionsAsync(int retentionDays);
}

/// <summary>
/// Detection statistics
/// </summary>
public class DetectionStats
{
    public int TotalDetections { get; set; }
    public int PeopleCount { get; set; }
    public int VehicleCount { get; set; }
    public Dictionary<string, int> ClassCounts { get; set; } = new();
    public float AverageConfidence { get; set; }
    public int UniqueFrames { get; set; }
}