using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VisionOps.Service.Stability;

/// <summary>
/// Manages service state persistence and recovery across restarts
/// </summary>
public interface IServiceStateManager
{
    Task SaveStateAsync<T>(string key, T state) where T : class;
    Task<T?> LoadStateAsync<T>(string key) where T : class;
    Task SaveCheckpointAsync(ServiceCheckpoint checkpoint);
    Task<ServiceCheckpoint?> LoadLastCheckpointAsync();
    Task ClearStateAsync(string key);
    Task ClearAllStateAsync();
}

/// <summary>
/// Service checkpoint for recovery
/// </summary>
public sealed class ServiceCheckpoint
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0.0";
    public ServiceStatus Status { get; set; } = new();
    public Dictionary<string, CameraState> CameraStates { get; set; } = new();
    public ProcessingState Processing { get; set; } = new();
    public Dictionary<string, object> CustomData { get; set; } = new();
}

/// <summary>
/// Overall service status
/// </summary>
public sealed class ServiceStatus
{
    public bool IsRunning { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public int RestartCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime? LastErrorTime { get; set; }
}

/// <summary>
/// Camera state for recovery
/// </summary>
public sealed class CameraState
{
    public string CameraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public DateTime? LastFrameTime { get; set; }
    public int FramesProcessed { get; set; }
    public int ErrorCount { get; set; }
    public string LastError { get; set; } = string.Empty;
}

/// <summary>
/// Processing state for recovery
/// </summary>
public sealed class ProcessingState
{
    public int TotalFramesProcessed { get; set; }
    public int TotalDetections { get; set; }
    public int QueuedFrames { get; set; }
    public int PendingSyncs { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public bool IsThrottled { get; set; }
    public int ThrottleDelayMs { get; set; }
}

/// <summary>
/// Implementation of service state manager
/// </summary>
public sealed class ServiceStateManager : IServiceStateManager, IDisposable
{
    private readonly ILogger<ServiceStateManager> _logger;
    private readonly string _stateDirectory;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private Timer? _autoSaveTimer;
    private ServiceCheckpoint? _lastCheckpoint;
    private bool _disposed;

    // State management settings
    private const int AUTO_SAVE_INTERVAL_SECONDS = 300; // 5 minutes
    private const int MAX_STATE_FILES = 10;
    private const string CHECKPOINT_FILE = "checkpoint.json";
    private const string STATE_FILE_PREFIX = "state_";

    public ServiceStateManager(ILogger<ServiceStateManager> logger)
    {
        _logger = logger;

        // Setup state directory
        _stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VisionOps",
            "State");
        Directory.CreateDirectory(_stateDirectory);

        // Configure JSON options
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        // Start auto-save timer
        _autoSaveTimer = new Timer(
            AutoSaveCallback,
            null,
            TimeSpan.FromSeconds(AUTO_SAVE_INTERVAL_SECONDS),
            TimeSpan.FromSeconds(AUTO_SAVE_INTERVAL_SECONDS));

        _logger.LogInformation("Service state manager initialized. State directory: {Directory}",
            _stateDirectory);
    }

    /// <summary>
    /// Save state for a specific key
    /// </summary>
    public async Task SaveStateAsync<T>(string key, T state) where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("State key cannot be empty", nameof(key));

        await _stateLock.WaitAsync();
        try
        {
            var stateFile = GetStateFilePath(key);
            var stateData = new StateData<T>
            {
                Key = key,
                TypeName = typeof(T).FullName ?? typeof(T).Name,
                Timestamp = DateTime.UtcNow,
                Data = state
            };

            var json = JsonSerializer.Serialize(stateData, _jsonOptions);
            await File.WriteAllTextAsync(stateFile, json);

            _logger.LogDebug("State saved for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state for key: {Key}", key);
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Load state for a specific key
    /// </summary>
    public async Task<T?> LoadStateAsync<T>(string key) where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("State key cannot be empty", nameof(key));

        await _stateLock.WaitAsync();
        try
        {
            var stateFile = GetStateFilePath(key);
            if (!File.Exists(stateFile))
            {
                _logger.LogDebug("No state found for key: {Key}", key);
                return null;
            }

            var json = await File.ReadAllTextAsync(stateFile);
            var stateData = JsonSerializer.Deserialize<StateData<T>>(json, _jsonOptions);

            if (stateData?.Data != null)
            {
                // Check if state is not too old (24 hours)
                if ((DateTime.UtcNow - stateData.Timestamp).TotalHours < 24)
                {
                    _logger.LogInformation("State loaded for key: {Key} (Age: {Age})",
                        key, DateTime.UtcNow - stateData.Timestamp);
                    return stateData.Data;
                }
                else
                {
                    _logger.LogWarning("State for key {Key} is too old ({Age}), ignoring",
                        key, DateTime.UtcNow - stateData.Timestamp);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load state for key: {Key}", key);
            return null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Save service checkpoint
    /// </summary>
    public async Task SaveCheckpointAsync(ServiceCheckpoint checkpoint)
    {
        await _stateLock.WaitAsync();
        try
        {
            _lastCheckpoint = checkpoint;
            checkpoint.Timestamp = DateTime.UtcNow;

            var checkpointFile = Path.Combine(_stateDirectory, CHECKPOINT_FILE);
            var json = JsonSerializer.Serialize(checkpoint, _jsonOptions);
            await File.WriteAllTextAsync(checkpointFile, json);

            // Also save timestamped backup
            var backupFile = Path.Combine(_stateDirectory,
                $"checkpoint_{checkpoint.Timestamp:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(backupFile, json);

            _logger.LogInformation("Service checkpoint saved");

            // Cleanup old checkpoints
            CleanupOldCheckpoints();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save checkpoint");
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Load last service checkpoint
    /// </summary>
    public async Task<ServiceCheckpoint?> LoadLastCheckpointAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            var checkpointFile = Path.Combine(_stateDirectory, CHECKPOINT_FILE);
            if (!File.Exists(checkpointFile))
            {
                _logger.LogInformation("No checkpoint found");
                return null;
            }

            var json = await File.ReadAllTextAsync(checkpointFile);
            var checkpoint = JsonSerializer.Deserialize<ServiceCheckpoint>(json, _jsonOptions);

            if (checkpoint != null)
            {
                var age = DateTime.UtcNow - checkpoint.Timestamp;
                _logger.LogInformation("Checkpoint loaded (Age: {Age}, Cameras: {Cameras}, Frames: {Frames})",
                    age, checkpoint.CameraStates.Count, checkpoint.Processing.TotalFramesProcessed);

                // Validate checkpoint age
                if (age.TotalHours > 24)
                {
                    _logger.LogWarning("Checkpoint is too old, may not be fully applicable");
                }

                _lastCheckpoint = checkpoint;
                return checkpoint;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load checkpoint");
            return null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Clear state for a specific key
    /// </summary>
    public async Task ClearStateAsync(string key)
    {
        await _stateLock.WaitAsync();
        try
        {
            var stateFile = GetStateFilePath(key);
            if (File.Exists(stateFile))
            {
                File.Delete(stateFile);
                _logger.LogInformation("State cleared for key: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear state for key: {Key}", key);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Clear all state files
    /// </summary>
    public async Task ClearAllStateAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            var stateFiles = Directory.GetFiles(_stateDirectory, $"{STATE_FILE_PREFIX}*.json");
            foreach (var file in stateFiles)
            {
                File.Delete(file);
            }

            _logger.LogInformation("All state files cleared ({Count} files)", stateFiles.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all state");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Auto-save callback
    /// </summary>
    private async void AutoSaveCallback(object? state)
    {
        if (_lastCheckpoint != null)
        {
            try
            {
                await SaveCheckpointAsync(_lastCheckpoint);
                _logger.LogDebug("Auto-save checkpoint completed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-save checkpoint failed");
            }
        }
    }

    /// <summary>
    /// Get state file path for a key
    /// </summary>
    private string GetStateFilePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_stateDirectory, $"{STATE_FILE_PREFIX}{safeKey}.json");
    }

    /// <summary>
    /// Cleanup old checkpoint files
    /// </summary>
    private void CleanupOldCheckpoints()
    {
        try
        {
            var checkpointFiles = Directory.GetFiles(_stateDirectory, "checkpoint_*.json")
                .OrderByDescending(f => File.GetCreationTimeUtc(f))
                .Skip(MAX_STATE_FILES);

            foreach (var file in checkpointFiles)
            {
                File.Delete(file);
                _logger.LogDebug("Deleted old checkpoint: {File}", Path.GetFileName(file));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old checkpoints");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _autoSaveTimer?.Dispose();
        _stateLock?.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// State data wrapper
    /// </summary>
    private class StateData<T>
    {
        public string Key { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public T? Data { get; set; }
    }
}