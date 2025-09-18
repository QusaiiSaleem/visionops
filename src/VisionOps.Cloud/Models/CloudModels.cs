using Postgrest.Attributes;
using Postgrest.Models;

namespace VisionOps.Cloud.Models;

/// <summary>
/// Cloud detection model for Supabase
/// </summary>
[Table("detections")]
public class CloudDetection : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("camera_id")]
    public string CameraId { get; set; } = string.Empty;

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("class_name")]
    public string ClassName { get; set; } = string.Empty;

    [Column("confidence")]
    public float Confidence { get; set; }

    [Column("bbox_x")]
    public int BboxX { get; set; }

    [Column("bbox_y")]
    public int BboxY { get; set; }

    [Column("bbox_width")]
    public int BboxWidth { get; set; }

    [Column("bbox_height")]
    public int BboxHeight { get; set; }

    [Column("frame_number")]
    public long FrameNumber { get; set; }

    [Column("zone_name")]
    public string? ZoneName { get; set; }

    [Column("processing_time_ms")]
    public int ProcessingTimeMs { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Cloud key frame model for Supabase
/// </summary>
[Table("key_frames")]
public class CloudKeyFrame : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("camera_id")]
    public string CameraId { get; set; } = string.Empty;

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("compressed_image")]
    public string CompressedImage { get; set; } = string.Empty; // Base64 encoded

    [Column("image_size_bytes")]
    public int ImageSizeBytes { get; set; }

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("people_count")]
    public int PeopleCount { get; set; }

    [Column("vehicle_count")]
    public int VehicleCount { get; set; }

    [Column("other_objects_json")]
    public string? OtherObjectsJson { get; set; }

    [Column("scene_attributes_json")]
    public string? SceneAttributesJson { get; set; }

    [Column("frame_number")]
    public long FrameNumber { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Cloud metric model for Supabase
/// </summary>
[Table("metrics")]
public class CloudMetric : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("camera_id")]
    public string CameraId { get; set; } = string.Empty;

    [Column("window_start")]
    public DateTime WindowStart { get; set; }

    [Column("window_end")]
    public DateTime WindowEnd { get; set; }

    [Column("window_duration_seconds")]
    public int WindowDurationSeconds { get; set; }

    [Column("sample_count")]
    public int SampleCount { get; set; }

    [Column("avg_people_count")]
    public float AvgPeopleCount { get; set; }

    [Column("max_people_count")]
    public int MaxPeopleCount { get; set; }

    [Column("avg_vehicle_count")]
    public float AvgVehicleCount { get; set; }

    [Column("max_vehicle_count")]
    public int MaxVehicleCount { get; set; }

    [Column("total_detections")]
    public int TotalDetections { get; set; }

    [Column("avg_processing_time_ms")]
    public float AvgProcessingTimeMs { get; set; }

    [Column("p95_processing_time_ms")]
    public float P95ProcessingTimeMs { get; set; }

    [Column("frames_processed")]
    public int FramesProcessed { get; set; }

    [Column("key_frames_processed")]
    public int KeyFramesProcessed { get; set; }

    [Column("error_count")]
    public int ErrorCount { get; set; }

    [Column("avg_cpu_usage")]
    public float AvgCpuUsage { get; set; }

    [Column("max_cpu_temperature")]
    public float MaxCpuTemperature { get; set; }

    [Column("avg_memory_usage_mb")]
    public float AvgMemoryUsageMb { get; set; }

    [Column("zone_stats_json")]
    public string? ZoneStatsJson { get; set; }

    [Column("compression_ratio")]
    public float CompressionRatio { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Cloud camera configuration model
/// </summary>
[Table("cameras")]
public class CloudCamera : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("location")]
    public string? Location { get; set; }

    [Column("rtsp_url")]
    public string RtspUrl { get; set; } = string.Empty;

    [Column("is_enabled")]
    public bool IsEnabled { get; set; }

    [Column("last_connected_at")]
    public DateTime? LastConnectedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}