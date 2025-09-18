using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VisionOps.Data.Entities;
using System.Text.Json;

namespace VisionOps.Data;

/// <summary>
/// Entity Framework database context for VisionOps
/// </summary>
public class VisionOpsDbContext : DbContext
{
    private readonly string _connectionString;

    public VisionOpsDbContext(DbContextOptions<VisionOpsDbContext> options)
        : base(options)
    {
        _connectionString = Database.GetConnectionString() ?? "Data Source=visionops.db";
    }

    public VisionOpsDbContext(string connectionString)
        : base()
    {
        _connectionString = connectionString;
    }

    // DbSets
    public DbSet<CameraEntity> Cameras => Set<CameraEntity>();
    public DbSet<DetectionEntity> Detections => Set<DetectionEntity>();
    public DbSet<KeyFrameEntity> KeyFrames => Set<KeyFrameEntity>();
    public DbSet<MetricEntity> Metrics => Set<MetricEntity>();
    public DbSet<SyncQueueEntity> SyncQueue => Set<SyncQueueEntity>();
    public DbSet<ConfigurationEntity> Configurations => Set<ConfigurationEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(_connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });

            // Enable logging in debug mode
#if DEBUG
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
#endif
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Camera entity configuration
        modelBuilder.Entity<CameraEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.IsEnabled);
            entity.HasIndex(e => e.LastConnectedAt);

            // Relationships
            entity.HasMany(e => e.Detections)
                .WithOne(d => d.Camera)
                .HasForeignKey(d => d.CameraId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.KeyFrames)
                .WithOne(k => k.Camera)
                .HasForeignKey(k => k.CameraId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Metrics)
                .WithOne(m => m.Camera)
                .HasForeignKey(m => m.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Detection entity configuration
        modelBuilder.Entity<DetectionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CameraId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ClassName);
            entity.HasIndex(e => e.IsSynced);
            entity.HasIndex(e => new { e.CameraId, e.Timestamp });
            entity.HasIndex(e => new { e.IsSynced, e.CreatedAt });

            // Relationship with KeyFrame
            entity.HasOne(e => e.KeyFrame)
                .WithMany(k => k.Detections)
                .HasForeignKey(e => e.KeyFrameId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // KeyFrame entity configuration
        modelBuilder.Entity<KeyFrameEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CameraId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.IsSynced);
            entity.HasIndex(e => new { e.CameraId, e.Timestamp });
            entity.HasIndex(e => new { e.IsSynced, e.CreatedAt });
        });

        // Metric entity configuration
        modelBuilder.Entity<MetricEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CameraId);
            entity.HasIndex(e => e.WindowStart);
            entity.HasIndex(e => e.WindowEnd);
            entity.HasIndex(e => e.IsSynced);
            entity.HasIndex(e => new { e.CameraId, e.WindowStart });
            entity.HasIndex(e => new { e.IsSynced, e.CreatedAt });
        });

        // SyncQueue entity configuration
        modelBuilder.Entity<SyncQueueEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.NextRetryAt);
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => new { e.Status, e.Priority, e.NextRetryAt });
            entity.HasIndex(e => e.ExpiresAt);
        });

        // Configuration entity configuration
        modelBuilder.Entity<ConfigurationEntity>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.HasIndex(e => e.Category);
        });

        // Seed default configurations
        SeedConfigurations(modelBuilder);
    }

    private void SeedConfigurations(ModelBuilder modelBuilder)
    {
        var defaultConfigs = new List<ConfigurationEntity>
        {
            // Cloud settings
            new()
            {
                Key = ConfigurationKeys.SupabaseUrl,
                Value = "",
                DataType = "String",
                Category = ConfigurationCategories.Cloud,
                Description = "Supabase project URL",
                IsEncrypted = false,
                RequiresRestart = false
            },
            new()
            {
                Key = ConfigurationKeys.SupabaseKey,
                Value = "",
                DataType = "String",
                Category = ConfigurationCategories.Cloud,
                Description = "Supabase anonymous key",
                IsEncrypted = true,
                RequiresRestart = false
            },
            new()
            {
                Key = ConfigurationKeys.SyncEnabled,
                Value = "true",
                DataType = "Boolean",
                Category = ConfigurationCategories.Cloud,
                Description = "Enable cloud synchronization",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "true"
            },
            new()
            {
                Key = ConfigurationKeys.SyncIntervalSeconds,
                Value = "30",
                DataType = "Integer",
                Category = ConfigurationCategories.Cloud,
                Description = "Cloud sync interval in seconds",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "30"
            },
            new()
            {
                Key = ConfigurationKeys.SyncBatchSize,
                Value = "100",
                DataType = "Integer",
                Category = ConfigurationCategories.Cloud,
                Description = "Maximum items per sync batch",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "100"
            },

            // Processing settings
            new()
            {
                Key = ConfigurationKeys.MaxCameras,
                Value = "5",
                DataType = "Integer",
                Category = ConfigurationCategories.Processing,
                Description = "Maximum number of cameras to process",
                IsEncrypted = false,
                RequiresRestart = true,
                DefaultValue = "5"
            },
            new()
            {
                Key = ConfigurationKeys.FrameIntervalSeconds,
                Value = "3",
                DataType = "Integer",
                Category = ConfigurationCategories.Processing,
                Description = "Frame processing interval in seconds",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "3"
            },
            new()
            {
                Key = ConfigurationKeys.KeyFrameInterval,
                Value = "10",
                DataType = "Integer",
                Category = ConfigurationCategories.Processing,
                Description = "Key frame interval (every N frames)",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "10"
            },
            new()
            {
                Key = ConfigurationKeys.EnableFlorence2,
                Value = "true",
                DataType = "Boolean",
                Category = ConfigurationCategories.Processing,
                Description = "Enable Florence-2 scene descriptions",
                IsEncrypted = false,
                RequiresRestart = true,
                DefaultValue = "true"
            },

            // Performance settings
            new()
            {
                Key = ConfigurationKeys.CpuThrottleTemperature,
                Value = "70",
                DataType = "Integer",
                Category = ConfigurationCategories.Performance,
                Description = "CPU throttle temperature in Celsius",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "70"
            },
            new()
            {
                Key = ConfigurationKeys.MaxCpuUsagePercent,
                Value = "60",
                DataType = "Integer",
                Category = ConfigurationCategories.Performance,
                Description = "Maximum CPU usage percentage",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "60"
            },
            new()
            {
                Key = ConfigurationKeys.MaxMemoryUsageMb,
                Value = "6000",
                DataType = "Integer",
                Category = ConfigurationCategories.Performance,
                Description = "Maximum memory usage in MB",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "6000"
            },

            // Retention settings
            new()
            {
                Key = ConfigurationKeys.LocalRetentionDays,
                Value = "7",
                DataType = "Integer",
                Category = ConfigurationCategories.Retention,
                Description = "Local data retention in days",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "7"
            },
            new()
            {
                Key = ConfigurationKeys.CleanupIntervalHours,
                Value = "6",
                DataType = "Integer",
                Category = ConfigurationCategories.Retention,
                Description = "Data cleanup interval in hours",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "6"
            },

            // Service settings
            new()
            {
                Key = ConfigurationKeys.ServiceRestartTime,
                Value = "03:00",
                DataType = "Time",
                Category = ConfigurationCategories.Service,
                Description = "Daily service restart time (HH:mm)",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "03:00"
            },
            new()
            {
                Key = ConfigurationKeys.EnableAutoRestart,
                Value = "true",
                DataType = "Boolean",
                Category = ConfigurationCategories.Service,
                Description = "Enable automatic daily restart",
                IsEncrypted = false,
                RequiresRestart = false,
                DefaultValue = "true"
            },
            new()
            {
                Key = ConfigurationKeys.LogLevel,
                Value = "Information",
                DataType = "String",
                Category = ConfigurationCategories.Service,
                Description = "Logging level (Trace, Debug, Information, Warning, Error, Critical)",
                IsEncrypted = false,
                RequiresRestart = true,
                DefaultValue = "Information"
            }
        };

        modelBuilder.Entity<ConfigurationEntity>().HasData(defaultConfigs);
    }

    /// <summary>
    /// Initialize database and apply migrations
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        // Ensure database is created
        await Database.EnsureCreatedAsync();

        // Enable WAL mode for better concurrency
        await Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        await Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL");
        await Database.ExecuteSqlRawAsync("PRAGMA cache_size=10000");
        await Database.ExecuteSqlRawAsync("PRAGMA temp_store=MEMORY");
        await Database.ExecuteSqlRawAsync("PRAGMA mmap_size=30000000000");
    }

    /// <summary>
    /// Clean up old data based on retention policy
    /// </summary>
    public async Task<int> CleanupOldDataAsync(int retentionDays = 7)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var deletedCount = 0;

        // Delete old detections
        deletedCount += await Detections
            .Where(d => d.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync();

        // Delete old key frames
        deletedCount += await KeyFrames
            .Where(k => k.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync();

        // Delete old metrics
        deletedCount += await Metrics
            .Where(m => m.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync();

        // Delete expired sync queue items
        deletedCount += await SyncQueue
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync();

        // Vacuum database to reclaim space (run occasionally)
        if (deletedCount > 1000)
        {
            await Database.ExecuteSqlRawAsync("VACUUM");
        }

        return deletedCount;
    }

    /// <summary>
    /// Get database statistics
    /// </summary>
    public async Task<DatabaseStats> GetDatabaseStatsAsync()
    {
        var stats = new DatabaseStats
        {
            CameraCount = await Cameras.CountAsync(),
            TotalDetections = await Detections.CountAsync(),
            UnsyncedDetections = await Detections.CountAsync(d => !d.IsSynced),
            TotalKeyFrames = await KeyFrames.CountAsync(),
            UnsyncedKeyFrames = await KeyFrames.CountAsync(k => !k.IsSynced),
            TotalMetrics = await Metrics.CountAsync(),
            UnsyncedMetrics = await Metrics.CountAsync(m => !m.IsSynced),
            PendingSyncItems = await SyncQueue.CountAsync(s => s.Status == "Pending"),
            FailedSyncItems = await SyncQueue.CountAsync(s => s.Status == "Failed")
        };

        // Get database file size
        var dbPath = Database.GetDbConnection().DataSource;
        if (File.Exists(dbPath))
        {
            var fileInfo = new FileInfo(dbPath);
            stats.DatabaseSizeMb = fileInfo.Length / (1024.0 * 1024.0);
        }

        return stats;
    }
}

/// <summary>
/// Database statistics
/// </summary>
public class DatabaseStats
{
    public int CameraCount { get; set; }
    public int TotalDetections { get; set; }
    public int UnsyncedDetections { get; set; }
    public int TotalKeyFrames { get; set; }
    public int UnsyncedKeyFrames { get; set; }
    public int TotalMetrics { get; set; }
    public int UnsyncedMetrics { get; set; }
    public int PendingSyncItems { get; set; }
    public int FailedSyncItems { get; set; }
    public double DatabaseSizeMb { get; set; }
}