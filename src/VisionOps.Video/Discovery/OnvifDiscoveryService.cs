using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OnvifDiscovery.Models;
using VisionOps.Core.Models;

namespace VisionOps.Video.Discovery;

/// <summary>
/// ONVIF camera discovery service for automatic camera detection on the network
/// </summary>
public class OnvifDiscoveryService
{
    private readonly ILogger<OnvifDiscoveryService> _logger;
    private readonly TimeSpan _discoveryTimeout;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);

    public OnvifDiscoveryService(ILogger<OnvifDiscoveryService> logger)
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

            // Use OnvifDiscovery package to find devices
            var discovery = new OnvifDiscovery.Discovery();

            // Create a CTS for the timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_discoveryTimeout);

            var devices = await discovery.Discover(1, cts.Token);

            if (!devices.Any())
            {
                _logger.LogWarning("No ONVIF devices found on network");
                return discoveredCameras;
            }

            _logger.LogInformation("Found {Count} ONVIF devices", devices.Count());

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
                        device.Address);
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
        if (device.Address == null)
        {
            _logger.LogWarning("Device has no address");
            return null;
        }

        try
        {
            // Extract device information from the discovery response
            var camera = new CameraConfig
            {
                Name = $"ONVIF Camera {device.Address}",
                Location = device.Address.ToString()
            };

            // Build RTSP URL - this is a common pattern for most ONVIF cameras
            // The actual URL might need to be obtained through ONVIF GetStreamUri
            // For now, we'll use a common pattern
            if (IPAddress.TryParse(device.Address, out var ipAddress))
            {
                var rtspUrl = BuildRtspUrl(ipAddress);
                if (!string.IsNullOrEmpty(rtspUrl))
                {
                    camera.RtspUrl = rtspUrl;
                    return camera;
                }
            }

            _logger.LogWarning("Could not build RTSP URL for device at {Address}", device.Address);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process ONVIF device at {Address}", device.Address);
            return null;
        }
    }

    /// <summary>
    /// Build RTSP URL from device address
    /// </summary>
    private string? BuildRtspUrl(IPAddress address)
    {
        // Common RTSP URL patterns for ONVIF cameras
        // Most cameras use port 554 for RTSP
        // The actual stream path varies by manufacturer
        // This would ideally be obtained via ONVIF GetStreamUri
        return $"rtsp://{address}:554/stream1";
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

    /// <summary>
    /// Scan specific IP range for cameras
    /// </summary>
    public async Task<List<CameraConfig>> ScanIpRangeAsync(
        string startIp,
        string endIp,
        CancellationToken cancellationToken = default)
    {
        var cameras = new List<CameraConfig>();

        try
        {
            var start = IPAddress.Parse(startIp);
            var end = IPAddress.Parse(endIp);

            // Convert to uint for easy iteration
            var startBytes = start.GetAddressBytes();
            var endBytes = end.GetAddressBytes();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(startBytes);
                Array.Reverse(endBytes);
            }

            var startNum = BitConverter.ToUInt32(startBytes, 0);
            var endNum = BitConverter.ToUInt32(endBytes, 0);

            for (uint i = startNum; i <= endNum && !cancellationToken.IsCancellationRequested; i++)
            {
                var bytes = BitConverter.GetBytes(i);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                var ip = new IPAddress(bytes);
                var rtspUrl = $"rtsp://{ip}:554/stream1";

                // Quick test if port is open
                if (await IsPortOpenAsync(ip, 554, cancellationToken))
                {
                    var camera = new CameraConfig
                    {
                        Name = $"Camera {ip}",
                        RtspUrl = rtspUrl,
                        Location = ip.ToString()
                    };

                    // Test actual RTSP connection
                    if (await TestCameraConnectionAsync(camera, cancellationToken))
                    {
                        camera.Status = CameraStatus.Connected;
                        camera.LastConnected = DateTime.UtcNow;
                        cameras.Add(camera);
                        _logger.LogInformation("Found camera at {Ip}", ip);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan IP range");
        }

        return cameras;
    }

    /// <summary>
    /// Check if a port is open on a given IP
    /// </summary>
    private async Task<bool> IsPortOpenAsync(IPAddress ip, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(1));

            await client.ConnectAsync(ip, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}