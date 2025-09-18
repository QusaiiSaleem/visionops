using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Mictlanix.DotNet.Onvif;
using Mictlanix.DotNet.Onvif.Common;
using Mictlanix.DotNet.Onvif.Media;
using VisionOps.Core.Models;

namespace VisionOps.Video.Discovery;

/// <summary>
/// ONVIF camera discovery service for automatic camera detection on the network
/// </summary>
public class OnvifDiscovery
{
    private readonly ILogger<OnvifDiscovery> _logger;
    private readonly TimeSpan _discoveryTimeout;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);

    public OnvifDiscovery(ILogger<OnvifDiscovery> logger)
    {
        _logger = logger;
        _discoveryTimeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Discover ONVIF cameras on the local network
    /// </summary>
    public async Task<List<CameraConfig>> DiscoverCamerasAsync(CancellationToken cancellationToken = default)
    {
        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting ONVIF camera discovery...");
            var discoveredCameras = new List<CameraConfig>();

            // Use WS-Discovery to find ONVIF devices
            var discovery = new DiscoveryClient();
            var devices = await discovery.DiscoverAsync(_discoveryTimeout);

            if (devices == null || devices.Length == 0)
            {
                _logger.LogWarning("No ONVIF devices found on network");
                return discoveredCameras;
            }

            _logger.LogInformation("Found {Count} ONVIF devices", devices.Length);

            // Process each discovered device
            foreach (var device in devices)
            {
                try
                {
                    var camera = await ProcessOnvifDevice(device, cancellationToken);
                    if (camera != null)
                    {
                        discoveredCameras.Add(camera);
                        _logger.LogInformation("Discovered camera: {Name} at {Url}",
                            camera.Name, camera.RtspUrl);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process ONVIF device at {Address}",
                        device.XAddrs?.FirstOrDefault());
                }
            }

            return discoveredCameras;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    /// <summary>
    /// Process a single ONVIF device and extract camera configuration
    /// </summary>
    private async Task<CameraConfig?> ProcessOnvifDevice(
        DiscoveryDevice device,
        CancellationToken cancellationToken)
    {
        if (device.XAddrs == null || !device.XAddrs.Any())
        {
            _logger.LogWarning("Device has no service addresses");
            return null;
        }

        var serviceAddress = device.XAddrs.First();

        try
        {
            // Create device client
            var deviceUri = new Uri(serviceAddress);
            var deviceClient = new DeviceClient(deviceUri);

            // Get device information
            var deviceInfo = await deviceClient.GetDeviceInformationAsync();
            var scopes = await deviceClient.GetScopesAsync();

            // Create camera configuration
            var camera = new CameraConfig
            {
                Name = $"{deviceInfo?.Manufacturer} {deviceInfo?.Model}".Trim(),
                Location = ExtractLocationFromScopes(scopes)
            };

            // Get media service for stream URLs
            var mediaUri = await GetMediaServiceUri(deviceClient);
            if (mediaUri != null)
            {
                var mediaClient = new MediaClient(mediaUri);

                // Get profiles and extract RTSP URL
                var profiles = await mediaClient.GetProfilesAsync();
                if (profiles != null && profiles.Length > 0)
                {
                    // Prefer sub-stream (lower resolution) profile if available
                    var profile = SelectOptimalProfile(profiles);

                    var streamUri = await mediaClient.GetStreamUriAsync(
                        new StreamSetup
                        {
                            Stream = StreamType.RTPUnicast,
                            Transport = new Transport
                            {
                                Protocol = TransportProtocol.RTSP
                            }
                        },
                        profile.token);

                    if (streamUri?.Uri != null)
                    {
                        camera.RtspUrl = streamUri.Uri;
                        return camera;
                    }
                }
            }

            _logger.LogWarning("Could not extract RTSP URL from device");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process ONVIF device at {Address}", serviceAddress);
            return null;
        }
    }

    /// <summary>
    /// Get media service URI from device
    /// </summary>
    private async Task<Uri?> GetMediaServiceUri(DeviceClient deviceClient)
    {
        try
        {
            var capabilities = await deviceClient.GetCapabilitiesAsync(new[] { CapabilityCategory.Media });
            if (capabilities?.Media?.XAddr != null)
            {
                return new Uri(capabilities.Media.XAddr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get media service URI");
        }
        return null;
    }

    /// <summary>
    /// Select the optimal profile (prefer sub-stream for lower bandwidth)
    /// </summary>
    private Profile SelectOptimalProfile(Profile[] profiles)
    {
        // Look for sub-stream profile (usually contains "sub" in name)
        var subStream = profiles.FirstOrDefault(p =>
            p.Name?.Contains("sub", StringComparison.OrdinalIgnoreCase) == true);

        if (subStream != null)
            return subStream;

        // Otherwise, select profile with lowest resolution
        var lowestRes = profiles
            .Where(p => p.VideoEncoderConfiguration?.Resolution != null)
            .OrderBy(p => p.VideoEncoderConfiguration.Resolution.Width *
                         p.VideoEncoderConfiguration.Resolution.Height)
            .FirstOrDefault();

        return lowestRes ?? profiles[0];
    }

    /// <summary>
    /// Extract location information from ONVIF scopes
    /// </summary>
    private string ExtractLocationFromScopes(Scope[]? scopes)
    {
        if (scopes == null)
            return "Unknown Location";

        var locationScope = scopes.FirstOrDefault(s =>
            s.ScopeItem?.Contains("location", StringComparison.OrdinalIgnoreCase) == true);

        if (locationScope != null)
        {
            // Extract location from scope URI (e.g., onvif://www.onvif.org/location/country/city)
            var parts = locationScope.ScopeItem.Split('/');
            return parts.Length > 0 ? parts[^1] : "Unknown Location";
        }

        return "Unknown Location";
    }

    /// <summary>
    /// Manually add a camera by RTSP URL
    /// </summary>
    public async Task<CameraConfig?> AddCameraManuallyAsync(
        string rtspUrl,
        string? username = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Adding camera manually: {Url}", rtspUrl);

            var camera = new CameraConfig
            {
                Name = $"Manual Camera {DateTime.Now:HHmmss}",
                RtspUrl = rtspUrl,
                Username = username,
                Password = password,
                Location = "Manual Entry"
            };

            // Test connection
            if (await TestCameraConnectionAsync(camera, cancellationToken))
            {
                camera.Status = CameraStatus.Connected;
                camera.LastConnected = DateTime.UtcNow;
                return camera;
            }

            _logger.LogWarning("Failed to connect to camera at {Url}", rtspUrl);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add camera manually");
            return null;
        }
    }

    /// <summary>
    /// Test camera connection using FFmpeg
    /// </summary>
    public async Task<bool> TestCameraConnectionAsync(
        CameraConfig camera,
        CancellationToken cancellationToken = default)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-rtsp_transport tcp -i \"{camera.GetAuthenticatedUrl()}\" -t 1 -f null -",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(processStartInfo);
            if (process == null)
                return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(camera.ConnectionTimeoutSeconds));

            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test camera connection for {Camera}", camera.Name);
            return false;
        }
    }

    /// <summary>
    /// Get local network interfaces for scanning
    /// </summary>
    public List<IPAddress> GetLocalNetworkInterfaces()
    {
        var addresses = new List<IPAddress>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            {
                var ipProperties = networkInterface.GetIPProperties();
                foreach (var address in ipProperties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(address.Address);
                    }
                }
            }
        }

        return addresses;
    }
}