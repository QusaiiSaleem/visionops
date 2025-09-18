using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace VisionOps.UI.Services;

/// <summary>
/// Background service that manages automatic updates using Velopack.
/// Checks for updates periodically and downloads them silently.
/// Updates are applied on next application restart.
/// </summary>
public class AutoUpdateService : BackgroundService
{
    private readonly ILogger<AutoUpdateService> _logger;
    private readonly UpdateManager _updateManager;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);
    private readonly string _updateFeedUrl;
    private UpdateInfo? _pendingUpdate;

    public AutoUpdateService(ILogger<AutoUpdateService> logger)
    {
        _logger = logger;

        // Configure update feed URL - can be GitHub releases or custom server
        _updateFeedUrl = Environment.GetEnvironmentVariable("VISIONOPS_UPDATE_URL")
            ?? "https://github.com/QusaiiSaleem/visionops/releases";

        try
        {
            // Initialize Velopack UpdateManager
            _updateManager = new UpdateManager(
                new GithubSource(_updateFeedUrl, null, false),
                new UpdateOptions
                {
                    AllowVersionDowngrade = false,
                    ExplicitChannel = "stable" // or "beta" for testing
                });

            _logger.LogInformation("Auto-update service initialized with feed: {UpdateFeed}", _updateFeedUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize UpdateManager");
            throw;
        }
    }

    public bool IsUpdateAvailable => _pendingUpdate != null;
    public string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to let application fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        _logger.LogInformation("Auto-update service started, checking every {Hours} hours", _checkInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForUpdatesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during update check");
            }

            // Wait for next check interval
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Checks for available updates and downloads them if found.
    /// </summary>
    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Checking for updates...");

            // Check if an update is available
            var updateInfo = await _updateManager.CheckForUpdatesAsync(cancellationToken);

            if (updateInfo == null)
            {
                _logger.LogDebug("No updates available");
                return;
            }

            var currentVersion = _updateManager.CurrentVersion;
            var newVersion = updateInfo.TargetFullRelease?.Version;

            if (newVersion != null && newVersion > currentVersion)
            {
                _logger.LogInformation("Update available: v{Current} -> v{New}",
                    currentVersion, newVersion);

                _pendingUpdate = updateInfo;

                // Download the update in the background
                await DownloadUpdateAsync(updateInfo, cancellationToken);
            }
            else
            {
                _logger.LogDebug("Already on latest version: v{Version}", currentVersion);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Update check cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
        }
    }

    /// <summary>
    /// Downloads the update package silently in the background.
    /// </summary>
    private async Task DownloadUpdateAsync(UpdateInfo updateInfo, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Downloading update v{Version}...",
                updateInfo.TargetFullRelease?.Version);

            var progress = new Progress<int>(percent =>
            {
                if (percent % 10 == 0) // Log every 10%
                {
                    _logger.LogDebug("Update download progress: {Percent}%", percent);
                }
            });

            // Download the update
            await _updateManager.DownloadUpdatesAsync(updateInfo, progress, cancellationToken);

            _logger.LogInformation("Update downloaded successfully, will be applied on next restart");

            // Notify user that update is ready (optional)
            OnUpdateReady(updateInfo);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Update download cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update");
            _pendingUpdate = null;
        }
    }

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// Should be called when user agrees to restart or on graceful shutdown.
    /// </summary>
    public async Task<bool> ApplyUpdateAsync()
    {
        if (_pendingUpdate == null)
        {
            _logger.LogWarning("No pending update to apply");
            return false;
        }

        try
        {
            _logger.LogInformation("Applying update v{Version}...",
                _pendingUpdate.TargetFullRelease?.Version);

            // Apply the update
            await _updateManager.ApplyUpdatesAndRestartAsync(_pendingUpdate);

            // This line will only be reached if restart failed
            _logger.LogError("Failed to restart after update");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update");
            return false;
        }
    }

    /// <summary>
    /// Checks for updates immediately (bypass timer).
    /// </summary>
    public async Task<bool> CheckNowAsync()
    {
        try
        {
            await CheckForUpdatesAsync(CancellationToken.None);
            return _pendingUpdate != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual update check failed");
            return false;
        }
    }

    /// <summary>
    /// Event raised when an update is ready to be applied.
    /// </summary>
    public event EventHandler<UpdateReadyEventArgs>? UpdateReady;

    private void OnUpdateReady(UpdateInfo updateInfo)
    {
        var args = new UpdateReadyEventArgs
        {
            Version = updateInfo.TargetFullRelease?.Version?.ToString() ?? "Unknown",
            ReleaseNotes = ExtractReleaseNotes(updateInfo),
            IsMandat ory = updateInfo.TargetFullRelease?.Mandatory ?? false
        };

        UpdateReady?.Invoke(this, args);
    }

    private string ExtractReleaseNotes(UpdateInfo updateInfo)
    {
        // Extract release notes from the update info
        // This depends on how release notes are provided in your releases
        try
        {
            var release = updateInfo.TargetFullRelease;
            if (release?.ReleaseNotes != null)
            {
                return release.ReleaseNotes;
            }
        }
        catch
        {
            // Ignore errors extracting release notes
        }

        return "New version available with improvements and bug fixes.";
    }

    public override void Dispose()
    {
        _updateManager?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Event args for when an update is ready to be applied.
/// </summary>
public class UpdateReadyEventArgs : EventArgs
{
    public required string Version { get; init; }
    public required string ReleaseNotes { get; init; }
    public bool IsMandatory { get; init; }
}