# PLANNING.md - VisionOps Architecture & Implementation Blueprint

## 🎯 Project Vision

VisionOps transforms standard Windows PCs into intelligent edge video analytics systems, enabling businesses to extract operational insights from existing security cameras without cloud video streaming or expensive hardware upgrades.

### Core Value Propositions
- **Edge-First Analytics**: Process video locally, sync only metadata (100:1 data reduction)
- **Hardware Democratization**: Runs on existing office PCs (Intel i3-i5, 8-12GB RAM)
- **Privacy by Design**: No video leaves premises, GDPR-compliant with face blurring
- **Zero-Touch Operation**: Install once, runs autonomously 24/7 as Windows Service
- **AI-Powered Insights**: Florence-2 scene descriptions enable semantic search

### Target Market
- **Primary**: Small-medium retail chains (5-50 locations)
- **Secondary**: Manufacturing facilities, warehouses, office buildings
- **Use Cases**:
  - People counting and occupancy monitoring
  - Queue length detection
  - Safety compliance (PPE detection)
  - Operational analytics (dwell time, traffic patterns)
  - Incident detection with natural language alerts

### Success Metrics
- Process 5 cameras on Intel i3 with <60% CPU usage
- <6GB total memory footprint
- 99.9% uptime (excluding scheduled maintenance)
- <100KB/minute bandwidth per camera
- <5 minute setup time per camera

## 🏗️ System Architecture

### Component Architecture
```
┌─────────────────────────────────────────────────────────────┐
│                     Windows Edge PC                          │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────┐         ┌─────────────────┐              │
│  │ VisionOps.UI │◄────────┤ VisionOps.Core  │              │
│  │   (WPF)      │         │  (Business      │              │
│  │              │         │   Logic)        │              │
│  └──────┬───────┘         └────────┬────────┘              │
│         │                          │                        │
│         └──────────┬───────────────┘                        │
│                    ▼                                        │
│         ┌─────────────────────┐                            │
│         │ VisionOps.Service    │                            │
│         │ (Windows Service)    │                            │
│         ├─────────────────────┤                            │
│         │ • Camera Discovery   │                            │
│         │ • Frame Processing   │                            │
│         │ • AI Inference       │                            │
│         │ • Data Aggregation   │                            │
│         └──────────┬──────────┘                            │
│                    │                                        │
│     ┌──────────────┼──────────────┐                        │
│     ▼              ▼              ▼                        │
│ ┌────────┐  ┌──────────┐  ┌────────────┐                 │
│ │FFmpeg  │  │ONNX      │  │Florence-2  │                 │
│ │Process │  │Runtime   │  │Vision LLM  │                 │
│ └────────┘  └──────────┘  └────────────┘                 │
│                    │                                        │
│         ┌──────────┴──────────┐                           │
│         │  VisionOps.Data     │                           │
│         ├────────────────────┤                            │
│         │ • SQLite (Local)    │                           │
│         │ • EF Core           │                           │
│         │ • Sync Queue        │                           │
│         └──────────┬──────────┘                           │
│                    │                                        │
└────────────────────┼────────────────────────────────────┘
                     │
                     ▼ HTTPS (Compressed JSON)
         ┌───────────────────────┐
         │   Supabase Cloud      │
         ├───────────────────────┤
         │ • PostgreSQL          │
         │ • pgvector            │
         │ • Realtime            │
         │ • Storage (WebP)      │
         └───────────────────────┘
                     │
                     ▼
         ┌───────────────────────┐
         │   Web Dashboard       │
         │   (React + Next.js)   │
         └───────────────────────┘
```

### Data Flow Architecture
```
Camera Stream (RTSP)
    │
    ▼ (FFmpeg Process Isolation)
Frame Capture [1 fps]
    │
    ├─────────► Object Detection (YOLOv8n) [Every 3 seconds]
    │              │
    │              ▼
    │          Counting/Tracking
    │              │
    │              ▼
    │          Local SQLite
    │
    └─────────► Key Frame Extraction [Every 10 seconds]
                   │
                   ▼
               Florence-2 Description
                   │
                   ▼
               WebP Compression (3-5KB)
                   │
                   ▼
               Sync Queue
                   │
                   ▼ [Every 30 seconds]
               Supabase Upload
```

### Agent Responsibility Matrix

| Agent | Domain | Responsibilities |
|-------|--------|-----------------|
| **System Architect** | Overall Design | System decomposition, agent coordination, architecture decisions |
| **Windows Service Expert** | Service Layer | Windows Service, background processing, auto-start configuration |
| **Video Processing Engineer** | Media Pipeline | FFmpeg integration, frame extraction, buffer management |
| **AI/ML Engineer** | Inference | ONNX optimization, Florence-2 integration, model quantization |
| **Database Architect** | Data Layer | SQLite schema, Supabase sync, data aggregation |
| **UI/UX Developer** | User Interface | WPF application, camera setup, zone configuration |
| **DevOps Engineer** | Deployment | WiX installer, Velopack updates, CI/CD pipeline |
| **Performance Engineer** | Optimization | Memory profiling, CPU optimization, thermal management |
| **Security Engineer** | Security/Privacy | GDPR compliance, face blurring, credential management |

### UI Structure (from React Reference)
```
VisionOps.UI (WPF)
├── Views/
│   ├── MainWindow.xaml
│   │   ├── Camera Access Panel
│   │   │   ├── Auto-discovery
│   │   │   ├── RTSP testing
│   │   │   └── Connection status
│   │   ├── Frame Monitoring Panel
│   │   │   ├── Live preview (1 fps)
│   │   │   ├── Detection zones
│   │   │   └── Zone drawing tools
│   │   ├── Data Analysis Panel (Minimal)
│   │   │   ├── Service health
│   │   │   ├── Sync status
│   │   │   └── Error logs
│   │   └── User Settings Panel
│   │       ├── Supabase credentials
│   │       ├── Service control
│   │       └── Update settings
│   └── Dialogs/
│       ├── CameraTestDialog.xaml
│       └── ZoneEditorDialog.xaml
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── CameraViewModel.cs
│   └── SettingsViewModel.cs
└── Services/
    ├── CameraDiscoveryService.cs
    └── ServiceControlService.cs
```

## 💻 Technology Stack

### Core Technologies (LOCKED - DO NOT CHANGE)
| Category | Technology | Version | Justification |
|----------|-----------|---------|--------------|
| **Platform** | Windows 10/11 | 64-bit | Target deployment environment |
| **Runtime** | .NET 8 | Latest LTS | Performance, self-contained deployment |
| **Language** | C# 12 | w/ nullable refs | Type safety, modern features |
| **Video** | OpenCVSharp4 | 4.9.0+ | Mature, efficient for .NET |
| **AI Runtime** | ONNX Runtime | 1.17.0+ | Cross-platform, OpenVINO support |
| **Local DB** | SQLite | w/ EF Core 8 | Zero-config, embedded |
| **Cloud DB** | Supabase | PostgreSQL 15 | Managed, pgvector support |
| **Installer** | WiX Toolset | 4.0 | Windows native, reliable |
| **UI Framework** | WPF | .NET 8 | Windows native, MVVM |
| **Updates** | Velopack | 0.0.556+ | Delta updates, GitHub integration |

### NuGet Packages
```xml
<!-- Core Framework -->
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />

<!-- Video Processing -->
<PackageReference Include="OpenCvSharp4" Version="4.9.0" />
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.9.0" />
<PackageReference Include="FFMpegCore" Version="5.1.0" />

<!-- AI/ML -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.17.0" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.Extensions" Version="0.11.0" />
<PackageReference Include="Microsoft.ML.Tokenizers" Version="0.21.0" />

<!-- Database -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
<PackageReference Include="Supabase" Version="0.16.2" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.2.0" />

<!-- Image Processing -->
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.2" />
<PackageReference Include="WebP.Net" Version="1.0.0" />

<!-- WPF/UI -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
<PackageReference Include="MaterialDesignThemes" Version="4.9.0" />

<!-- Utilities -->
<PackageReference Include="Polly" Version="8.2.0" />
<PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
<PackageReference Include="Microsoft.IO.RecyclableMemoryStream" Version="3.0.0" />

<!-- Updates -->
<PackageReference Include="Velopack" Version="0.0.556" />

<!-- Testing -->
<PackageReference Include="xunit" Version="2.6.6" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
<PackageReference Include="NSubstitute" Version="5.1.0" />
```

### Development Tools
- **IDE**: Visual Studio 2022 / Rider
- **Profiling**: dotMemory, PerfView
- **Monitoring**: Windows Performance Monitor
- **Version Control**: Git with conventional commits
- **CI/CD**: GitHub Actions
- **Package Management**: NuGet, GitHub Packages

## 🧠 AI Models Strategy

### Model Selection
| Model | Purpose | Size | Quantization | Performance |
|-------|---------|------|-------------|-------------|
| **YOLOv8n** | Object Detection | 6.5MB | INT8 | 50ms @ 640x480 |
| **Florence-2-base** | Scene Description | 120MB | INT8 | 200ms @ 384x384 |
| **NanoDet-Plus** | Backup Detection | 4.5MB | INT8 | 40ms @ 416x416 |

### Optimization Strategy
```csharp
public class SharedInferenceEngine : IDisposable
{
    // CRITICAL: Single shared session for all cameras
    private readonly InferenceSession _sharedSession;
    private readonly SemaphoreSlim _sessionLock;

    public SharedInferenceEngine(string modelPath)
    {
        var options = SessionOptions.MakeSessionOptionWithOpenVINOProvider();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.ExecutionMode = ExecutionMode.ORT_PARALLEL;
        options.InterOpNumThreads = 2; // Limited for CPU conservation

        _sharedSession = new InferenceSession(modelPath, options);
        _sessionLock = new SemaphoreSlim(1, 1);
    }

    public async Task<InferenceResult> RunInferenceAsync(Tensor<float> input)
    {
        await _sessionLock.WaitAsync();
        try
        {
            // Single session, sequential processing
            return await Task.Run(() => _sharedSession.Run(input));
        }
        finally
        {
            _sessionLock.Release();
        }
    }
}
```

### Florence-2 Integration Pattern
```csharp
public class Florence2Service
{
    private readonly SharedInferenceEngine _engine;
    private readonly Tokenizer _tokenizer;

    public async Task<string> GenerateDescriptionAsync(Mat frame)
    {
        // Resize to 384x384 (Florence-2 input size)
        using var resized = frame.Resize(new Size(384, 384));

        // Convert to tensor
        var tensor = PreprocessImage(resized);

        // Run inference (shared session)
        var output = await _engine.RunInferenceAsync(tensor);

        // Decode tokens to text
        return _tokenizer.Decode(output);
    }
}
```

## 🗄️ Database Schema

### Local SQLite Schema
```sql
-- Cameras table
CREATE TABLE Cameras (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    RtspUrl TEXT NOT NULL,
    SubStreamUrl TEXT,
    IsActive BOOLEAN DEFAULT 1,
    LastSeen DATETIME,
    Configuration TEXT -- JSON
);

-- Detections table (aggregated)
CREATE TABLE Detections (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CameraId TEXT NOT NULL,
    Timestamp DATETIME NOT NULL,
    PersonCount INTEGER,
    VehicleCount INTEGER,
    OtherCounts TEXT, -- JSON
    Confidence REAL,
    FOREIGN KEY (CameraId) REFERENCES Cameras(Id)
);

-- Key frames table (temporary, 7-day retention)
CREATE TABLE KeyFrames (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CameraId TEXT NOT NULL,
    Timestamp DATETIME NOT NULL,
    CompressedImage BLOB, -- WebP, 3-5KB
    Description TEXT, -- Florence-2 output
    Embeddings BLOB, -- Optional: vector embeddings
    ExpiresAt DATETIME,
    FOREIGN KEY (CameraId) REFERENCES Cameras(Id)
);

-- Sync queue
CREATE TABLE SyncQueue (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT NOT NULL,
    EntityId TEXT NOT NULL,
    Operation TEXT NOT NULL, -- INSERT, UPDATE, DELETE
    Payload TEXT NOT NULL, -- JSON
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    RetryCount INTEGER DEFAULT 0,
    LastError TEXT
);

-- Performance metrics
CREATE TABLE Metrics (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp DATETIME NOT NULL,
    CpuUsage REAL,
    MemoryUsage INTEGER,
    Temperature REAL,
    FrameRate REAL,
    InferenceLatency REAL
);
```

### Supabase Cloud Schema
```sql
-- Locations table
CREATE TABLE locations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    address TEXT,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Cameras table
CREATE TABLE cameras (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    location_id UUID REFERENCES locations(id),
    edge_id TEXT NOT NULL, -- Maps to SQLite Camera.Id
    name TEXT NOT NULL,
    type TEXT,
    is_active BOOLEAN DEFAULT true,
    last_seen TIMESTAMPTZ,
    configuration JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Aggregated analytics (1-minute buckets)
CREATE TABLE analytics (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    camera_id UUID REFERENCES cameras(id),
    bucket TIMESTAMPTZ NOT NULL, -- 1-minute buckets
    person_count_avg REAL,
    person_count_max INTEGER,
    vehicle_count_avg REAL,
    vehicle_count_max INTEGER,
    other_counts JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Key frames with descriptions
CREATE TABLE key_frames (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    camera_id UUID REFERENCES cameras(id),
    timestamp TIMESTAMPTZ NOT NULL,
    image_url TEXT, -- Supabase Storage URL
    description TEXT, -- Florence-2 output
    description_embedding vector(768), -- For semantic search
    objects JSONB, -- Detected objects
    expires_at TIMESTAMPTZ, -- Auto-cleanup
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Alerts
CREATE TABLE alerts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    camera_id UUID REFERENCES cameras(id),
    type TEXT NOT NULL,
    severity TEXT NOT NULL,
    description TEXT,
    metadata JSONB,
    acknowledged BOOLEAN DEFAULT false,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Create indexes for performance
CREATE INDEX idx_analytics_bucket ON analytics(bucket DESC);
CREATE INDEX idx_analytics_camera ON analytics(camera_id, bucket DESC);
CREATE INDEX idx_key_frames_embedding ON key_frames USING ivfflat (description_embedding vector_cosine_ops);
CREATE INDEX idx_alerts_camera ON alerts(camera_id, created_at DESC);
```

### Data Aggregation Strategy
```csharp
public class DataAggregator
{
    // 100:1 compression ratio target
    private readonly TimeSpan _aggregationWindow = TimeSpan.FromMinutes(1);
    private readonly Dictionary<string, AggregationBuffer> _buffers = new();

    public void AddDetection(string cameraId, Detection detection)
    {
        var bucket = GetTimeBucket(detection.Timestamp);
        var buffer = GetOrCreateBuffer(cameraId, bucket);

        buffer.AddSample(detection);

        if (buffer.SampleCount >= 20) // ~3 seconds per sample
        {
            var aggregated = buffer.Compute();
            QueueForSync(aggregated);
            buffer.Reset();
        }
    }
}
```

## 📊 Performance Architecture

### Hardware Constraints
| Component | Minimum | Recommended | Maximum Load |
|-----------|---------|-------------|--------------|
| **CPU** | Intel i3-8100 | Intel i5-8400 | 60% average |
| **RAM** | 8GB DDR4 | 12GB DDR4 | 6GB for app |
| **Storage** | 256GB SSD | 512GB NVMe | 50MB/hour/camera |
| **Network** | 10 Mbps up | 25 Mbps up | 500KB/s total |
| **GPU** | Intel UHD 630 | Intel UHD 630 | OpenVINO accel |

### Resource Allocation
```yaml
Per Camera Budget:
  CPU: 10% average, 20% peak
  Memory: 500MB (300MB buffer + 200MB processing)
  Network: 50KB/s upload
  Storage: 50MB/hour local

System Overhead:
  Windows Service: 200MB
  ONNX Runtime: 500MB (shared)
  Florence-2: 300MB (when active)
  SQLite: 100MB
  UI (when open): 300MB

Total System (5 cameras):
  CPU: 50-60% utilization
  Memory: 4-6GB total
  Network: 250KB/s upload
  Storage: 6GB/day
```

### Thermal Management
```csharp
public class ThermalManager
{
    private readonly PerformanceCounter _tempCounter;
    private readonly ILogger<ThermalManager> _logger;
    private int _activeCameras = 5;

    public async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var temp = GetCpuTemperature();

            if (temp > 70) // Start throttling before CPU does (75°C)
            {
                _logger.LogWarning($"High CPU temperature: {temp}°C, throttling");
                await ThrottleProcessingAsync();
            }
            else if (temp < 60 && _activeCameras < 5)
            {
                await RestoreProcessingAsync();
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task ThrottleProcessingAsync()
    {
        // Reduce active cameras
        _activeCameras = Math.Max(1, _activeCameras - 1);

        // Increase frame skip interval
        FrameProcessor.SkipInterval = 5; // Process every 5th frame

        // Reduce inference batch size
        InferenceEngine.BatchSize = 4; // From 8
    }
}
```

### Memory Management Patterns
```csharp
public class MemoryEfficientProcessor
{
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly RecyclableMemoryStreamManager _streamManager = new();

    public async Task ProcessFrameAsync(Mat frame)
    {
        // Use pooled arrays for large buffers
        var buffer = _arrayPool.Rent(frame.Total() * frame.ElemSize());
        try
        {
            // Process with rented buffer
            Marshal.Copy(frame.Data, buffer, 0, buffer.Length);

            // Use recyclable streams
            using var stream = _streamManager.GetStream();
            await stream.WriteAsync(buffer, 0, buffer.Length);

            // Process...
        }
        finally
        {
            _arrayPool.Return(buffer, clearArray: true);
            frame.Dispose(); // CRITICAL: Always dispose OpenCV objects
        }
    }
}
```

## ⚠️ Production Hardening Requirements (Phase 0 - MANDATORY)

### Critical Issues to Address
1. **Memory Leaks**: OpenCVSharp VideoCapture with RTSP confirmed leaking
2. **Thermal Throttling**: Intel CPUs silently degrade at 75°C+
3. **ONNX Session Failures**: Multiple sessions crash on constrained hardware
4. **Network Interruptions**: RTSP streams fail without recovery
5. **Disk Space**: Logs and temp files accumulate

### FFmpeg Process Isolation
```csharp
public class FFmpegCameraCapture : IDisposable
{
    private Process _ffmpegProcess;
    private readonly string _rtspUrl;
    private readonly ILogger _logger;

    public async Task<Mat> CaptureFrameAsync()
    {
        // Start FFmpeg in separate process
        _ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                Arguments = $"-rtsp_transport tcp -i {_rtspUrl} -vf fps=1 -f image2pipe -vcodec png -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        _ffmpegProcess.Start();

        // Read frame from stdout
        using var stream = _ffmpegProcess.StandardOutput.BaseStream;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        // Convert to Mat
        return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
    }

    public void Dispose()
    {
        if (_ffmpegProcess?.HasExited == false)
        {
            _ffmpegProcess.Kill();
            _ffmpegProcess.WaitForExit(5000);
        }
        _ffmpegProcess?.Dispose();
    }
}
```

### Watchdog Service Pattern
```csharp
public class WatchdogService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WatchdogService> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Check memory usage
                var process = Process.GetCurrentProcess();
                if (process.WorkingSet64 > 6_000_000_000) // 6GB limit
                {
                    _logger.LogCritical("Memory limit exceeded, requesting restart");
                    await RequestServiceRestartAsync();
                }

                // Check CPU temperature
                if (GetCpuTemperature() > 80)
                {
                    _logger.LogCritical("Critical temperature, emergency shutdown");
                    await EmergencyShutdownAsync();
                }

                // Check disk space
                var driveInfo = new DriveInfo("C");
                if (driveInfo.AvailableFreeSpace < 1_000_000_000) // 1GB minimum
                {
                    await CleanupTempFilesAsync();
                }

                // Health check each component
                await CheckComponentHealthAsync();

                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog check failed");
            }
        }
    }
}
```

### Daily Restart Pattern
```csharp
public class MaintenanceScheduler : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRestart = now.Date.AddDays(1).AddHours(3); // 3 AM daily
            var delay = nextRestart - now;

            await Task.Delay(delay, ct);

            if (!ct.IsCancellationRequested)
            {
                _logger.LogInformation("Scheduled maintenance restart");
                await PerformMaintenanceAsync();

                // Restart service
                using var serviceController = new ServiceController("VisionOps");
                serviceController.Stop();
                serviceController.WaitForStatus(ServiceControllerStatus.Stopped);
                serviceController.Start();
            }
        }
    }
}
```

## 🔒 Security Architecture

### Privacy Compliance (GDPR)
```csharp
public class PrivacyProcessor
{
    private readonly FaceDetector _faceDetector;

    public async Task<Mat> BlurFacesAsync(Mat frame)
    {
        var faces = await _faceDetector.DetectAsync(frame);

        foreach (var face in faces)
        {
            // Apply Gaussian blur to face region
            using var roi = new Mat(frame, face);
            Cv2.GaussianBlur(roi, roi, new Size(51, 51), 30);
        }

        return frame;
    }
}
```

### Credential Management
```csharp
public class SecureCredentialStore
{
    public async Task<string> GetSupabaseKeyAsync()
    {
        // Use Windows Credential Manager
        using var cred = new Credential
        {
            Target = "VisionOps.Supabase",
            Type = CredentialType.Generic
        };

        cred.Load();
        return cred.Password;
    }

    public async Task SetSupabaseKeyAsync(string key)
    {
        using var cred = new Credential
        {
            Target = "VisionOps.Supabase",
            Type = CredentialType.Generic,
            Username = "VisionOps",
            Password = key,
            PersistanceType = PersistanceType.LocalComputer
        };

        cred.Save();
    }
}
```

### Network Security
- All Supabase communication over HTTPS
- RLS (Row Level Security) enabled on all tables
- API key rotation every 90 days
- Local network isolation for camera access
- No inbound ports required

## 🚀 Deployment Strategy

### WiX Installer Configuration
```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
    <Product Id="*"
             Name="VisionOps"
             Language="1033"
             Version="1.0.0.0"
             Manufacturer="VisionOps Inc"
             UpgradeCode="12345678-1234-1234-1234-123456789012">

        <Package InstallerVersion="500"
                 Compressed="yes"
                 InstallScope="perMachine"
                 Platform="x64" />

        <MajorUpgrade DowngradeErrorMessage="Newer version installed" />

        <!-- Install Windows Service -->
        <Component Id="ServiceComponent">
            <File Id="ServiceExe"
                  Source="VisionOps.Service.exe"
                  KeyPath="yes" />

            <ServiceInstall Id="ServiceInstaller"
                            Name="VisionOps"
                            DisplayName="VisionOps Analytics Service"
                            Type="ownProcess"
                            Start="auto"
                            ErrorControl="normal"
                            Account="LocalSystem" />

            <ServiceControl Id="StartService"
                            Start="install"
                            Stop="both"
                            Remove="uninstall"
                            Name="VisionOps"
                            Wait="yes" />
        </Component>

        <!-- Install ONNX models -->
        <Component Id="ModelsComponent">
            <File Source="models\yolov8n.onnx" />
            <File Source="models\florence2-base.onnx" />
        </Component>
    </Product>
</Wix>
```

### Velopack Auto-Update Configuration
```csharp
public class UpdateManager
{
    private readonly VelopackApp _app;

    public UpdateManager()
    {
        _app = VelopackApp.Build()
            .WithUrlOrPath("https://github.com/visionops/releases")
            .WithAutoApplyOnStartup(true)
            .Build();
    }

    public async Task CheckForUpdatesAsync()
    {
        var updateInfo = await _app.CheckForUpdatesAsync();

        if (updateInfo != null)
        {
            // Download in background
            await _app.DownloadUpdatesAsync(updateInfo);

            // Schedule restart during maintenance window
            ScheduleUpdateInstallation();
        }
    }
}
```

### CI/CD Pipeline (GitHub Actions)
```yaml
name: Build and Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x

    - name: Build
      run: |
        dotnet publish VisionOps.Service -c Release -r win-x64 --self-contained
        dotnet publish VisionOps.UI -c Release -r win-x64 --self-contained

    - name: Build Installer
      run: |
        msiexec /i wix-toolset.msi /quiet
        candle Product.wxs
        light Product.wixobj -o VisionOps.msi

    - name: Create Velopack Release
      run: |
        vpk pack -u VisionOps -v ${{ github.ref_name }} -p ./publish

    - name: Upload Release
      uses: softprops/action-gh-release@v1
      with:
        files: |
          VisionOps.msi
          releases/*
```

## 🔄 Auto-Update Strategy

### Update Channels
- **Stable**: Production releases, monthly cycle
- **Beta**: Preview features, bi-weekly
- **Nightly**: Development builds, automated

### Update Process
1. **UI Application**: Check every 12 hours, auto-download, prompt user
2. **Windows Service**: Check daily, download, install during 2 AM window
3. **Rollback**: Keep previous version for 7 days
4. **Delta Updates**: Use Velopack for minimal bandwidth

## ⚠️ Risk Analysis

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| **Memory Leaks** | High | Critical | FFmpeg isolation, daily restart, memory monitoring |
| **Thermal Throttling** | Medium | High | Active thermal management, processing throttling |
| **Network Failures** | High | Medium | Retry logic, offline queue, graceful degradation |
| **Model Accuracy** | Low | Medium | Dual model strategy, confidence thresholds |
| **Disk Space** | Medium | High | Auto-cleanup, compression, rotation policies |
| **Camera Compatibility** | Medium | Medium | ONVIF support, manual RTSP configuration |
| **Windows Updates** | Low | High | Compatibility testing, controlled updates |

### Business Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| **Scalability Limits** | Medium | High | Clear hardware requirements, upgrade path |
| **Support Burden** | High | Medium | Remote diagnostics, auto-recovery, good logging |
| **Competition** | Medium | Medium | Focus on edge processing advantage |
| **Compliance Issues** | Low | Critical | Face blurring, data retention policies |

### Mitigation Priority Matrix
```
High Priority (Phase 0):
├── Memory leak prevention (FFmpeg isolation)
├── Thermal management system
├── Watchdog service implementation
└── Daily restart mechanism

Medium Priority (Phase 1):
├── Comprehensive logging
├── Remote diagnostics
├── Automated testing suite
└── Performance monitoring

Low Priority (Phase 2+):
├── Advanced analytics
├── Multi-tenant support
├── Cloud redundancy
└── Mobile app
```

## 📋 Implementation Phases

### Phase 0: Production Hardening (Week 0 - MANDATORY)
- FFmpeg process isolation
- Shared ONNX session pattern
- Thermal management
- Watchdog service
- Memory monitoring
- Daily restart scheduler

### Phase 1: Foundation (Weeks 1-2)
- Project structure
- Windows Service skeleton
- Basic UI with camera discovery
- SQLite schema
- Logging infrastructure

### Phase 2: Video Pipeline (Weeks 3-4)
- FFmpeg integration
- Frame capture and buffering
- Sequential processing
- Memory management

### Phase 3: AI Integration (Weeks 5-6)
- YOLOv8n integration
- Florence-2 setup
- Shared inference engine
- Model quantization

### Phase 4: Data Layer (Weeks 7-8)
- SQLite implementation
- Supabase sync
- Data aggregation
- Compression

### Phase 5: UI Polish (Week 9)
- Camera configuration
- Zone drawing
- Service control
- Settings management

### Phase 6: Testing (Week 10)
- Unit tests
- Integration tests
- 24-hour stress test
- Memory profiling

### Phase 7: Deployment (Weeks 11-12)
- WiX installer
- Velopack updates
- CI/CD pipeline
- Documentation

## 📝 Success Criteria

### Performance Metrics
- ✅ Process 5 cameras on Intel i3-8100
- ✅ <60% average CPU utilization
- ✅ <6GB memory footprint
- ✅ 99.9% uptime (excluding maintenance)
- ✅ <100KB/minute bandwidth per camera

### Functional Requirements
- ✅ Auto-discovery of ONVIF cameras
- ✅ Real-time people counting
- ✅ Florence-2 scene descriptions
- ✅ Automatic cloud synchronization
- ✅ 7-day local data retention

### Quality Metrics
- ✅ Zero memory leaks over 24 hours
- ✅ Automatic recovery from failures
- ✅ <5 minute setup time
- ✅ No manual intervention required
- ✅ GDPR compliant with face blurring

---

## 🔗 Quick Reference Links

- **Task Tracking**: See TASKS.md for current implementation status
- **Session Guide**: See CLAUDE.md for development guidelines
- **API Docs**: [Supabase C# Client](https://github.com/supabase-community/supabase-csharp)
- **ONNX Models**: [ONNX Model Zoo](https://github.com/onnx/models)
- **Florence-2**: [Microsoft Florence](https://github.com/microsoft/Florence)

---

*Last Updated: January 2025*
*Version: 1.0.0*
*Status: Architecture Complete, Ready for Implementation*