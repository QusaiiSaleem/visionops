using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisionOps.Service.Events;

namespace VisionOps.Service.Stability;

/// <summary>
/// Watchdog service to monitor application health and trigger recovery if needed.
/// Expects regular pulses from the main service - triggers recovery if no pulse received.
/// Generates minidumps on crash and persists state for recovery.
/// </summary>
public sealed class WatchdogService : IHostedService, IDisposable
{
    private readonly ILogger<WatchdogService> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IEventBus? _eventBus;
    private Timer? _watchdogTimer;
    private Timer? _stateTimer;
    private DateTime _lastPulse;
    private bool _disposed;
    private readonly string _stateDirectory;
    private readonly Dictionary<string, DateTime> _componentHeartbeats = new();
    private int _recoveryAttempts = 0;
    private DateTime _lastRecoveryTime = DateTime.MinValue;

    // Watchdog settings
    private const int WATCHDOG_TIMEOUT_SECONDS = 30; // 30 seconds without pulse = problem (per CLAUDE.md requirement)
    private const int WATCHDOG_CHECK_INTERVAL_SECONDS = 10;
    private const int STATE_SAVE_INTERVAL_SECONDS = 60;
    private const int MAX_RECOVERY_ATTEMPTS = 3;
    private const int RECOVERY_COOLDOWN_MINUTES = 5;

    public WatchdogService(ILogger<WatchdogService> logger, IHostApplicationLifetime appLifetime, IEventBus? eventBus = null)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _eventBus = eventBus;
        _lastPulse = DateTime.UtcNow;

        // Setup state directory
        _stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VisionOps",
            "Watchdog");
        Directory.CreateDirectory(_stateDirectory);

        // Setup unhandled exception handler for crash dumps
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Watchdog service started with {Timeout}s timeout for recovery in <30s",
            WATCHDOG_TIMEOUT_SECONDS);

        // Load previous state if exists
        LoadWatchdogState();

        // Start watchdog timer
        _watchdogTimer = new Timer(
            WatchdogCheck,
            null,
            TimeSpan.FromSeconds(WATCHDOG_CHECK_INTERVAL_SECONDS),
            TimeSpan.FromSeconds(WATCHDOG_CHECK_INTERVAL_SECONDS));

        // Start state persistence timer
        _stateTimer = new Timer(
            SaveState,
            null,
            TimeSpan.FromSeconds(STATE_SAVE_INTERVAL_SECONDS),
            TimeSpan.FromSeconds(STATE_SAVE_INTERVAL_SECONDS));

        // Subscribe to thermal events if event bus is available
        _eventBus?.Subscribe<ThermalThrottleEvent>(OnThermalEvent);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Watchdog service stopping");
        _watchdogTimer?.Change(Timeout.Infinite, 0);
        _stateTimer?.Change(Timeout.Infinite, 0);

        // Save final state
        SaveWatchdogState();

        // Unsubscribe from events
        if (_eventBus != null)
        {
            _eventBus.Unsubscribe<ThermalThrottleEvent>(OnThermalEvent);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Pulse the watchdog to indicate the service is healthy
    /// </summary>
    public void Pulse(string component = "Main")
    {
        _lastPulse = DateTime.UtcNow;
        _componentHeartbeats[component] = DateTime.UtcNow;

        // Reset recovery attempts on successful pulse
        if (_recoveryAttempts > 0 && (DateTime.UtcNow - _lastRecoveryTime).TotalMinutes > RECOVERY_COOLDOWN_MINUTES)
        {
            _recoveryAttempts = 0;
            _logger.LogInformation("Recovery attempts reset after successful operation");
        }
    }

    /// <summary>
    /// Check if watchdog has been pulsed recently
    /// </summary>
    private void WatchdogCheck(object? state)
    {
        var timeSinceLastPulse = DateTime.UtcNow - _lastPulse;

        if (timeSinceLastPulse.TotalSeconds > WATCHDOG_TIMEOUT_SECONDS)
        {
            _logger.LogCritical("Watchdog timeout! No pulse for {Seconds}s. Triggering recovery",
                timeSinceLastPulse.TotalSeconds);

            // Check which components are not responding
            var staleComponents = _componentHeartbeats
                .Where(kvp => (DateTime.UtcNow - kvp.Value).TotalSeconds > WATCHDOG_TIMEOUT_SECONDS)
                .Select(kvp => kvp.Key)
                .ToList();

            if (staleComponents.Any())
            {
                _logger.LogError("Stale components: {Components}", string.Join(", ", staleComponents));
            }

            // Attempt recovery based on attempts
            AttemptRecovery();
        }
        else
        {
            _logger.LogDebug("Watchdog check OK - last pulse {Seconds}s ago",
                timeSinceLastPulse.TotalSeconds);
        }
    }

    /// <summary>
    /// Attempt recovery based on failure count
    /// </summary>
    private void AttemptRecovery()
    {
        _recoveryAttempts++;
        _lastRecoveryTime = DateTime.UtcNow;

        _logger.LogWarning("Recovery attempt {Attempt}/{Max}", _recoveryAttempts, MAX_RECOVERY_ATTEMPTS);

        // Save state before recovery
        SaveWatchdogState();

        if (_recoveryAttempts <= MAX_RECOVERY_ATTEMPTS)
        {
            try
            {
                // First attempts: Try to recover gracefully
                if (_recoveryAttempts == 1)
                {
                    _logger.LogInformation("Attempting soft recovery - forcing garbage collection");
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, true);

                    // Give it another chance
                    _lastPulse = DateTime.UtcNow;
                }
                else if (_recoveryAttempts == 2)
                {
                    _logger.LogInformation("Attempting component restart");
                    // This would notify components to restart via event bus
                    _eventBus?.PublishAsync(new ComponentRestartEvent()).Wait(5000);

                    // Give it another chance
                    _lastPulse = DateTime.UtcNow;
                }
                else
                {
                    _logger.LogError("Soft recovery failed, initiating service restart");
                    GenerateMinidump("watchdog_timeout");
                    _appLifetime.StopApplication();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during recovery attempt");
                _appLifetime.StopApplication();
            }
        }
        else
        {
            _logger.LogCritical("Maximum recovery attempts exceeded. Forcing restart");
            GenerateMinidump("max_recovery_exceeded");

            // Force exit if graceful shutdown doesn't work
            Task.Delay(5000).ContinueWith(_ => Environment.Exit(1));
            _appLifetime.StopApplication();
        }
    }

    /// <summary>
    /// Handle unhandled exceptions and generate crash dumps
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _logger.LogCritical("Unhandled exception detected. Generating crash dump.");

            if (e.ExceptionObject is Exception ex)
            {
                _logger.LogCritical(ex, "Unhandled exception details");
            }

            GenerateMinidump("unhandled_exception");
            SaveCrashInfo(e.ExceptionObject as Exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate crash dump");
        }
    }

    /// <summary>
    /// Generate a minidump for debugging
    /// </summary>
    private void GenerateMinidump(string reason)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var dumpFile = Path.Combine(_stateDirectory, $"VisionOps_{reason}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.dmp");

            using (var fs = new FileStream(dumpFile, FileMode.Create, FileAccess.Write))
            {
                var processId = (uint)Process.GetCurrentProcess().Id;
                MiniDumpWriteDump(
                    Process.GetCurrentProcess().Handle,
                    processId,
                    fs.SafeFileHandle,
                    MiniDumpType.MiniDumpNormal,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }

            _logger.LogInformation("Minidump created: {DumpFile}", dumpFile);

            // Clean up old dumps (keep last 5)
            CleanupOldDumps();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create minidump");
        }
    }

    /// <summary>
    /// Save crash information
    /// </summary>
    private void SaveCrashInfo(Exception? exception)
    {
        try
        {
            var crashFile = Path.Combine(_stateDirectory, $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            var crashInfo = new
            {
                Timestamp = DateTime.UtcNow,
                RecoveryAttempts = _recoveryAttempts,
                LastPulse = _lastPulse,
                ComponentHeartbeats = _componentHeartbeats,
                Exception = exception?.ToString(),
                StackTrace = exception?.StackTrace,
                ProcessInfo = new
                {
                    WorkingSet = Process.GetCurrentProcess().WorkingSet64,
                    ThreadCount = Process.GetCurrentProcess().Threads.Count,
                    HandleCount = Process.GetCurrentProcess().HandleCount,
                    Uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime
                }
            };

            File.WriteAllText(crashFile, JsonSerializer.Serialize(crashInfo, new JsonSerializerOptions { WriteIndented = true }));
            _logger.LogInformation("Crash info saved: {CrashFile}", crashFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save crash info");
        }
    }

    /// <summary>
    /// Clean up old dump files
    /// </summary>
    private void CleanupOldDumps()
    {
        try
        {
            var dumpFiles = Directory.GetFiles(_stateDirectory, "*.dmp")
                .OrderByDescending(f => File.GetCreationTimeUtc(f))
                .Skip(5);

            foreach (var file in dumpFiles)
            {
                File.Delete(file);
                _logger.LogDebug("Deleted old dump: {File}", file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old dumps");
        }
    }

    /// <summary>
    /// Save watchdog state
    /// </summary>
    private void SaveState(object? state)
    {
        SaveWatchdogState();
    }

    private void SaveWatchdogState()
    {
        try
        {
            var stateFile = Path.Combine(_stateDirectory, "watchdog_state.json");
            var state = new WatchdogState
            {
                LastPulse = _lastPulse,
                RecoveryAttempts = _recoveryAttempts,
                LastRecoveryTime = _lastRecoveryTime,
                ComponentHeartbeats = _componentHeartbeats,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(stateFile, json);

            _logger.LogDebug("Watchdog state saved");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save watchdog state");
        }
    }

    /// <summary>
    /// Load previous watchdog state
    /// </summary>
    private void LoadWatchdogState()
    {
        try
        {
            var stateFile = Path.Combine(_stateDirectory, "watchdog_state.json");
            if (File.Exists(stateFile))
            {
                var json = File.ReadAllText(stateFile);
                var state = JsonSerializer.Deserialize<WatchdogState>(json);

                if (state != null && (DateTime.UtcNow - state.Timestamp).TotalMinutes < 10)
                {
                    _recoveryAttempts = state.RecoveryAttempts;
                    _lastRecoveryTime = state.LastRecoveryTime;

                    _logger.LogInformation("Previous watchdog state loaded. Recovery attempts: {Attempts}",
                        _recoveryAttempts);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load watchdog state");
        }
    }

    /// <summary>
    /// Handle thermal events
    /// </summary>
    private void OnThermalEvent(ThermalThrottleEvent thermalEvent)
    {
        if (thermalEvent.Temperature >= 75)
        {
            _logger.LogWarning("Critical temperature detected: {Temp}°C. Adjusting watchdog sensitivity",
                thermalEvent.Temperature);

            // Be more lenient during thermal events
            _lastPulse = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _watchdogTimer?.Dispose();
        _stateTimer?.Dispose();
        _disposed = true;
    }

    // P/Invoke for minidump generation
    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeHandle hFile,
        MiniDumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [Flags]
    private enum MiniDumpType : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithFullMemory = 0x00000002,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpFilterMemory = 0x00000008,
        MiniDumpScanMemory = 0x00000010
    }

    private class WatchdogState
    {
        public DateTime LastPulse { get; set; }
        public int RecoveryAttempts { get; set; }
        public DateTime LastRecoveryTime { get; set; }
        public Dictionary<string, DateTime> ComponentHeartbeats { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}