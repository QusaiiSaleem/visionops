using System.ComponentModel.DataAnnotations;

namespace VisionOps.Data.Entities;

/// <summary>
/// Represents system configuration settings
/// </summary>
public class ConfigurationEntity
{
    /// <summary>
    /// Configuration key
    /// </summary>
    [Key]
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Configuration value (can be JSON for complex values)
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Value data type
    /// </summary>
    [MaxLength(50)]
    public string DataType { get; set; } = "String";

    /// <summary>
    /// Configuration category
    /// </summary>
    [MaxLength(100)]
    public string Category { get; set; } = "General";

    /// <summary>
    /// Description of the configuration
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this setting is encrypted
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Whether this setting requires service restart
    /// </summary>
    public bool RequiresRestart { get; set; }

    /// <summary>
    /// Default value
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Validation rules (JSON)
    /// </summary>
    public string? ValidationRulesJson { get; set; }

    /// <summary>
    /// Last modified by (user or system)
    /// </summary>
    [MaxLength(100)]
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Record update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration categories
/// </summary>
public static class ConfigurationCategories
{
    public const string General = "General";
    public const string Service = "Service";
    public const string Processing = "Processing";
    public const string Cloud = "Cloud";
    public const string Performance = "Performance";
    public const string Security = "Security";
    public const string Retention = "Retention";
}

/// <summary>
/// Common configuration keys
/// </summary>
public static class ConfigurationKeys
{
    // Cloud settings
    public const string SupabaseUrl = "Cloud.SupabaseUrl";
    public const string SupabaseKey = "Cloud.SupabaseKey";
    public const string SupabaseServiceKey = "Cloud.SupabaseServiceKey";
    public const string SyncEnabled = "Cloud.SyncEnabled";
    public const string SyncIntervalSeconds = "Cloud.SyncIntervalSeconds";
    public const string SyncBatchSize = "Cloud.SyncBatchSize";

    // Processing settings
    public const string MaxCameras = "Processing.MaxCameras";
    public const string FrameIntervalSeconds = "Processing.FrameIntervalSeconds";
    public const string KeyFrameInterval = "Processing.KeyFrameInterval";
    public const string EnableFlorence2 = "Processing.EnableFlorence2";
    public const string InferenceBatchSize = "Processing.InferenceBatchSize";
    public const string MaxProcessingThreads = "Processing.MaxProcessingThreads";

    // Performance settings
    public const string CpuThrottleTemperature = "Performance.CpuThrottleTemperature";
    public const string MaxCpuUsagePercent = "Performance.MaxCpuUsagePercent";
    public const string MaxMemoryUsageMb = "Performance.MaxMemoryUsageMb";
    public const string EnableThermalThrottling = "Performance.EnableThermalThrottling";

    // Retention settings
    public const string LocalRetentionDays = "Retention.LocalRetentionDays";
    public const string CloudRetentionDays = "Retention.CloudRetentionDays";
    public const string KeyFrameRetentionDays = "Retention.KeyFrameRetentionDays";
    public const string CleanupIntervalHours = "Retention.CleanupIntervalHours";

    // Service settings
    public const string ServiceRestartTime = "Service.DailyRestartTime";
    public const string EnableAutoRestart = "Service.EnableAutoRestart";
    public const string EnableWatchdog = "Service.EnableWatchdog";
    public const string LogLevel = "Service.LogLevel";
}