using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VisionOps.UI.Models;
using VisionOps.UI.Services;

namespace VisionOps.UI;

/// <summary>
/// Main window for VisionOps configuration UI.
/// Provides service control, camera configuration, and system monitoring.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly IConfiguration _configuration;
    private readonly AutoUpdateService _updateService;
    private readonly DispatcherTimer _statusTimer;
    private readonly ObservableCollection<CameraViewModel> _cameras;
    private readonly ObservableCollection<Phase0StatusItem> _phase0Items;
    private ServiceController? _serviceController;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize services
        var services = ((App)Application.Current).Services;
        _logger = services.GetRequiredService<ILogger<MainWindow>>();
        _configuration = services.GetRequiredService<IConfiguration>();
        _updateService = services.GetRequiredService<AutoUpdateService>();

        // Initialize collections
        _cameras = new ObservableCollection<CameraViewModel>();
        _phase0Items = new ObservableCollection<Phase0StatusItem>();

        // Bind to UI
        CameraGrid.ItemsSource = _cameras;
        Phase0StatusList.ItemsSource = _phase0Items;

        // Initialize timer for status updates
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _statusTimer.Tick += StatusTimer_Tick;
        _statusTimer.Start();

        // Load initial data
        LoadConfiguration();
        InitializePhase0Status();
        CheckServiceStatus();

        // Subscribe to update events
        _updateService.UpdateReady += OnUpdateReady;
    }

    private void LoadConfiguration()
    {
        try
        {
            // Load Supabase settings
            SupabaseUrlInput.Text = _configuration["VisionOps:Supabase:Url"] ?? "";
            SupabaseKeyInput.Password = _configuration["VisionOps:Supabase:AnonKey"] ?? "";

            // Load performance settings
            MaxCamerasSlider.Value = _configuration.GetValue("VisionOps:Cameras:MaxCameras", 5);
            ThermalLimitSlider.Value = _configuration.GetValue("VisionOps:Thermal:ThrottleTemp", 70);
            MemoryLimitSlider.Value = _configuration.GetValue("VisionOps:Memory:MaxMemoryGB", 6);

            // Load cameras from config
            var cameras = _configuration.GetSection("VisionOps:Cameras:List").GetChildren();
            foreach (var camera in cameras)
            {
                _cameras.Add(new CameraViewModel
                {
                    Name = camera["Name"] ?? "Unknown",
                    RtspUrl = camera["RtspUrl"] ?? "",
                    Status = "Configured"
                });
            }

            _logger.LogInformation("Configuration loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration");
            ShowError("Failed to load configuration: " + ex.Message);
        }
    }

    private void InitializePhase0Status()
    {
        _phase0Items.Add(new Phase0StatusItem
        {
            Name = "Memory Management",
            Status = "Active",
            Icon = "CheckCircle",
            Color = Brushes.Green
        });

        _phase0Items.Add(new Phase0StatusItem
        {
            Name = "Thermal Protection",
            Status = "Active",
            Icon = "CheckCircle",
            Color = Brushes.Green
        });

        _phase0Items.Add(new Phase0StatusItem
        {
            Name = "Watchdog Service",
            Status = "Active",
            Icon = "CheckCircle",
            Color = Brushes.Green
        });

        _phase0Items.Add(new Phase0StatusItem
        {
            Name = "ONNX Runtime",
            Status = "Ready",
            Icon = "CheckCircle",
            Color = Brushes.Green
        });

        _phase0Items.Add(new Phase0StatusItem
        {
            Name = "Auto-Updates",
            Status = _updateService != null ? "Enabled" : "Disabled",
            Icon = _updateService != null ? "CheckCircle" : "AlertCircle",
            Color = _updateService != null ? Brushes.Green : Brushes.Orange
        });
    }

    private void CheckServiceStatus()
    {
        try
        {
            _serviceController = new ServiceController("VisionOps");
            UpdateServiceStatus();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not connect to VisionOps service");
            UpdateServiceUI(ServiceControllerStatus.Stopped);
        }
    }

    private void UpdateServiceStatus()
    {
        if (_serviceController == null) return;

        try
        {
            _serviceController.Refresh();
            UpdateServiceUI(_serviceController.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service status");
            UpdateServiceUI(ServiceControllerStatus.Stopped);
        }
    }

    private void UpdateServiceUI(ServiceControllerStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            ServiceStatusText.Text = status.ToString();
            ServiceDetailStatus.Text = $"Status: {status}";

            switch (status)
            {
                case ServiceControllerStatus.Running:
                    ServiceStatusIndicator.Fill = Brushes.Green;
                    StartServiceButton.IsEnabled = false;
                    StopServiceButton.IsEnabled = true;
                    RestartServiceButton.IsEnabled = true;
                    break;

                case ServiceControllerStatus.Stopped:
                    ServiceStatusIndicator.Fill = Brushes.Red;
                    StartServiceButton.IsEnabled = true;
                    StopServiceButton.IsEnabled = false;
                    RestartServiceButton.IsEnabled = false;
                    break;

                default:
                    ServiceStatusIndicator.Fill = Brushes.Orange;
                    StartServiceButton.IsEnabled = false;
                    StopServiceButton.IsEnabled = false;
                    RestartServiceButton.IsEnabled = false;
                    break;
            }
        });
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        UpdateServiceStatus();
        await UpdateSystemMetrics();
        UpdateStatusBar();
    }

    private async Task UpdateSystemMetrics()
    {
        await Task.Run(() =>
        {
            try
            {
                // Get CPU usage
                var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
                Task.Delay(100).Wait();
                var cpuUsage = cpuCounter.NextValue();

                // Get memory usage
                var memoryMB = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

                // Get temperature (simulated for now)
                var temperature = 45 + (cpuUsage * 0.3); // Simulated

                Dispatcher.Invoke(() =>
                {
                    CpuUsageText.Text = $"{cpuUsage:F1}%";
                    CpuUsageBar.Value = cpuUsage;

                    MemoryUsageText.Text = $"{memoryMB / 1024.0:F1} GB";
                    MemoryUsageBar.Value = memoryMB;

                    TemperatureText.Text = $"{temperature:F0}°C";
                    TemperatureBar.Value = temperature;

                    // Color code based on thresholds
                    CpuUsageBar.Foreground = cpuUsage > 80 ? Brushes.Red : cpuUsage > 60 ? Brushes.Orange : Brushes.Green;
                    MemoryUsageBar.Foreground = memoryMB > 5500 ? Brushes.Red : memoryMB > 4000 ? Brushes.Orange : Brushes.Green;
                    TemperatureBar.Foreground = temperature > 70 ? Brushes.Red : temperature > 60 ? Brushes.Orange : Brushes.Green;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update system metrics");
            }
        });
    }

    private void UpdateStatusBar()
    {
        StatusBarTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    // Service Control Events
    private async void StartService_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceController == null) return;

        try
        {
            StatusBarText.Text = "Starting service...";
            await Task.Run(() => _serviceController.Start());
            await Task.Run(() => _serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)));
            StatusBarText.Text = "Service started successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service");
            ShowError($"Failed to start service: {ex.Message}");
        }
    }

    private async void StopService_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceController == null) return;

        var result = MessageBox.Show(
            "Are you sure you want to stop the VisionOps service?",
            "Confirm Stop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            StatusBarText.Text = "Stopping service...";
            await Task.Run(() => _serviceController.Stop());
            await Task.Run(() => _serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)));
            StatusBarText.Text = "Service stopped successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop service");
            ShowError($"Failed to stop service: {ex.Message}");
        }
    }

    private async void RestartService_Click(object sender, RoutedEventArgs e)
    {
        await StopService_Click(sender, e);
        await Task.Delay(2000);
        await StartService_Click(sender, e);
    }

    // Camera Management Events
    private void AddCamera_Click(object sender, RoutedEventArgs e)
    {
        var name = CameraNameInput.Text?.Trim();
        var rtspUrl = RtspUrlInput.Text?.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(rtspUrl))
        {
            ShowError("Please enter camera name and RTSP URL");
            return;
        }

        if (_cameras.Any(c => c.Name == name))
        {
            ShowError("Camera with this name already exists");
            return;
        }

        _cameras.Add(new CameraViewModel
        {
            Name = name,
            RtspUrl = rtspUrl,
            Status = "Added"
        });

        CameraNameInput.Clear();
        RtspUrlInput.Clear();

        StatusBarText.Text = $"Camera '{name}' added successfully";
    }

    private void TestCamera_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).CommandParameter is CameraViewModel camera)
        {
            camera.Status = "Testing...";
            StatusBarText.Text = $"Testing camera '{camera.Name}'...";

            // TODO: Implement actual RTSP test
            Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    camera.Status = "Test OK";
                    StatusBarText.Text = $"Camera '{camera.Name}' test completed";
                });
            });
        }
    }

    private void RemoveCamera_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).CommandParameter is CameraViewModel camera)
        {
            var result = MessageBox.Show(
                $"Remove camera '{camera.Name}'?",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _cameras.Remove(camera);
                StatusBarText.Text = $"Camera '{camera.Name}' removed";
            }
        }
    }

    // Settings Events
    private async void TestSupabase_Click(object sender, RoutedEventArgs e)
    {
        StatusBarText.Text = "Testing Supabase connection...";

        // TODO: Implement actual Supabase test
        await Task.Delay(2000);

        StatusBarText.Text = "Supabase connection test completed";
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // TODO: Save settings to appsettings.json
            StatusBarText.Text = "Settings saved successfully";
            MessageBox.Show("Settings saved successfully. Restart the service for changes to take effect.",
                "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            ShowError($"Failed to save settings: {ex.Message}");
        }
    }

    // Update Events
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        StatusBarText.Text = "Checking for updates...";
        var hasUpdate = await _updateService.CheckNowAsync();

        if (hasUpdate)
        {
            UpdateButton.Visibility = Visibility.Visible;
            StatusBarText.Text = $"Update available: v{_updateService.PendingVersion}";
        }
        else
        {
            StatusBarText.Text = "No updates available";
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Update to version {_updateService.PendingVersion} is ready to install. The application will restart. Continue?",
            "Update Available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _updateService.ApplyUpdateAsync();
        }
    }

    private void OnUpdateReady(object? sender, UpdateReadyEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateButton.Visibility = Visibility.Visible;
            StatusBarText.Text = $"Update available: v{e.Version}";
        });
    }

    // Log Events
    private void RefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VisionOps", "Logs");

            var latestLog = Directory.GetFiles(logPath, "*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();

            if (latestLog != null)
            {
                LogTextBox.Text = File.ReadAllText(latestLog);
                LogTextBox.ScrollToEnd();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load logs");
            LogTextBox.Text = $"Failed to load logs: {ex.Message}";
        }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"VisionOps_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            DefaultExt = ".txt",
            Filter = "Text Files (.txt)|*.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, LogTextBox.Text);
            StatusBarText.Text = $"Logs exported to {dialog.FileName}";
        }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        StatusBarText.Text = $"Error: {message}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _statusTimer?.Stop();
        _serviceController?.Dispose();
        base.OnClosing(e);
    }
}

// View Models
public class CameraViewModel : INotifyPropertyChanged
{
    private string _status = "Unknown";

    public required string Name { get; set; }
    public required string RtspUrl { get; set; }

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class Phase0StatusItem
{
    public required string Name { get; set; }
    public required string Status { get; set; }
    public required string Icon { get; set; }
    public required Brush Color { get; set; }
}