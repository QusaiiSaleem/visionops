# CLAUDE.md - VisionOps Master Guide for Claude Code Sessions

## 🚨 CRITICAL: MANDATORY STARTUP PROTOCOL

**EVERY NEW CONVERSATION MUST:**
1. Read this CLAUDE.md file completely
2. Review PLANNING.md for system architecture
3. Check TASKS.md for current phase and pending work
4. Deploy appropriate specialized agents for the task
5. Update TASKS.md immediately upon task completion

**FAILURE TO FOLLOW THIS PROTOCOL WILL RESULT IN INCONSISTENT IMPLEMENTATION**

## 🎯 Project Overview

VisionOps is a production-grade edge video analytics platform that transforms standard Windows office PCs into intelligent surveillance systems. The system processes camera streams locally using AI (YOLOv8 + Florence-2), extracting operational insights without uploading video data, ensuring privacy compliance and bandwidth efficiency.

### 📦 Repository Information
**GitHub Repository**: https://github.com/QusaiiSaleem/visionops

**Implementation Status**:
1. ✅ Created public GitHub repository
2. ✅ Pushed all 167 files (complete Phase 0 implementation)
3. ✅ Updated auto-update URLs to point to production repo
4. ✅ Added GitHub Actions workflow for automated builds
5. ✅ Configured Velopack for auto-updates from GitHub releases

### Core System Components

**Edge Components (Windows PC):**
- **VisionOps.Service**: 24/7 Windows Service for autonomous video processing
  - Camera discovery and RTSP stream management
  - Real-time AI inference (YOLOv8 object detection)
  - Florence-2 scene descriptions every 10 seconds
  - Local SQLite caching with 7-day retention
  - Supabase cloud synchronization (metadata only)
  - Memory leak protection via FFmpeg process isolation
  - Thermal throttling at 70°C CPU temperature
  - Watchdog service for auto-recovery

- **VisionOps.UI**: Minimal WPF configuration interface
  - Based on React UI design with 4 sections:
    - Camera Access: Discovery and RTSP testing
    - Frame Monitoring: Live preview and zones
    - Data Analysis: Local metrics only
    - User Settings: Service configuration
  - NO dashboards, NO reports, NO analytics
  - "Configure once and forget" philosophy

**Cloud Components (Supabase):**
- Web dashboard for all analytics and business intelligence
- Multi-location management and reporting
- Historical trend analysis with pgvector
- Real-time alerts and notifications
- This is where users actually work daily

## 👥 MANDATORY AGENT TEAM STRUCTURE

### You Are: System Architect (Primary Orchestrator)
- Coordinate all specialized agents
- Decompose complex tasks into domain-specific work
- Ensure architectural consistency
- Monitor agent progress and synthesize results
- Prevent scope creep and over-engineering

### Specialized Agent Roster

#### 1. Windows Service Agent
**Domain**: Background service implementation
**Expertise**:
- Windows Service lifecycle management
- Multi-threading and async patterns
- Process isolation for FFmpeg
- Memory management and pooling
- Watchdog implementation

**When to Deploy**:
- Service installation/configuration
- Background processing logic
- Process management
- Auto-recovery mechanisms

#### 2. Video Processing Agent
**Domain**: Camera and frame handling
**Expertise**:
- RTSP stream management
- FFmpeg process isolation (NOT OpenCVSharp direct)
- Frame buffering and circular queues
- Key frame extraction
- WebP compression

**When to Deploy**:
- Camera connection issues
- Frame processing pipelines
- Video codec problems
- Stream reliability

#### 3. AI Inference Agent
**Domain**: ML model integration
**Expertise**:
- ONNX Runtime optimization
- YOLOv8 and Florence-2 models
- Shared session management
- INT8 quantization
- Batch processing strategies

**When to Deploy**:
- Model integration
- Inference optimization
- Florence-2 descriptions
- Performance tuning

#### 4. Database Agent
**Domain**: Data persistence and sync
**Expertise**:
- SQLite with EF Core
- Supabase PostgreSQL
- pgvector for embeddings
- Batch synchronization
- Offline-first patterns

**When to Deploy**:
- Schema design
- Data synchronization
- Query optimization
- Retention policies

#### 5. UI/UX Agent
**Domain**: WPF application interface
**Expertise**:
- MVVM pattern implementation
- Based on React UI design:
  - Camera Access tab
  - Frame Monitoring tab
  - Data Analysis tab
  - User Settings tab
- Reactive bindings
- Material Design

**When to Deploy**:
- UI implementation
- User interactions
- Configuration screens
- Visual feedback

#### 6. Production Hardening Agent
**Domain**: System reliability
**Expertise**:
- Memory leak detection/prevention
- Thermal management
- Error recovery patterns
- Performance profiling
- Logging and monitoring

**When to Deploy**:
- Phase 0 requirements
- Production issues
- Performance problems
- Stability concerns

#### 7. Deployment Agent
**Domain**: Installation and updates
**Expertise**:
- WiX installer creation
- Velopack auto-updates
- GitHub Actions CI/CD
- Certificate signing
- MSI packaging

**When to Deploy**:
- Build pipelines
- Installer creation
- Update mechanisms
- Release management

## 🚦 Agent Coordination Protocol

### Step 1: Task Assessment
```yaml
Questions to Ask:
- Is this a complex multi-domain task?
- Does it require specialized expertise?
- Can it be parallelized across agents?
- What are the dependencies?
```

### Step 2: Agent Deployment
```yaml
Single Domain Task:
  → Deploy one specialized agent
  → Monitor progress
  → Review output

Multi-Domain Task:
  → Decompose into domain tasks
  → Deploy multiple agents in parallel
  → Coordinate dependencies
  → Synthesize results
```

### Step 3: Quality Gates
```yaml
Before Marking Complete:
- Memory profiling passed?
- CPU usage < 60%?
- Error handling implemented?
- Tests written and passing?
- Documentation updated?
```

## 🔴 PHASE 0: PRODUCTION HARDENING (MANDATORY BEFORE FEATURES)

### Critical Issues That MUST Be Addressed

#### 1. Memory Leak Mitigation
**Problem**: OpenCVSharp VideoCapture leaks memory with RTSP streams
**Solution**: FFmpeg process isolation
```csharp
// NEVER use this:
var capture = new VideoCapture("rtsp://...");

// ALWAYS use this:
var ffmpeg = new FFmpegStreamProcessor();
ffmpeg.StartProcess("rtsp://...", OnFrameReceived);
```

#### 2. Thermal Management
**Problem**: Intel CPUs throttle at 75°C causing silent degradation
**Solution**: Proactive throttling at 70°C
```csharp
public class ThermalManager
{
    private const int THROTTLE_TEMP = 70;

    public async Task MonitorTemperature()
    {
        var temp = await GetCpuTemperature();
        if (temp > THROTTLE_TEMP)
        {
            await ReduceProcessingLoad();
        }
    }
}
```

#### 3. ONNX Session Management
**Problem**: Multiple sessions crash on limited hardware
**Solution**: Single shared session pattern
```csharp
// Singleton pattern for inference
public class SharedInferenceEngine
{
    private static InferenceSession _sharedSession;
    private static readonly object _lock = new();

    public static InferenceSession GetSession()
    {
        if (_sharedSession == null)
        {
            lock (_lock)
            {
                _sharedSession ??= new InferenceSession(modelPath);
            }
        }
        return _sharedSession;
    }
}
```

#### 4. Service Stability
**Problem**: Long-running service degradation
**Solution**: Daily restart at 3 AM
```csharp
public class ServiceLifecycleManager
{
    private readonly Timer _restartTimer;

    public void ScheduleDailyRestart()
    {
        var now = DateTime.Now;
        var threeAM = now.Date.AddDays(1).AddHours(3);
        var delay = threeAM - now;

        _restartTimer = new Timer(RestartService, null, delay, TimeSpan.FromDays(1));
    }
}
```

## 🤖 Florence-2 Vision-Language Integration

### Architecture
- **Model**: Florence-2-base (232M params → 120MB INT8 quantized)
- **Frequency**: 1 key frame every 10 seconds per camera
- **Compression**: WebP Q=20 at 320x240 (3-5KB per frame)
- **Description**: ~200 bytes natural language

### Performance Budget
```yaml
Per Camera:
  CPU Impact: +15%
  Memory: +400MB
  Processing: <1s per frame

5 Camera System:
  Total CPU: 75% (at limit)
  Total Memory: 2GB for Florence-2
  Daily Storage: 215MB
  Upload Bandwidth: 2.5KB/s
```

### Implementation Pattern
```csharp
public class Florence2Processor
{
    private readonly SharedInferenceEngine _inference;
    private int _frameCounter = 0;

    public async Task ProcessKeyFrame(Mat frame, string cameraId)
    {
        if (++_frameCounter % 10 != 0) return; // Every 10 seconds

        using var small = frame.Resize(new Size(320, 240));
        var description = await GenerateDescription(small);
        var compressed = CompressWebP(small, quality: 20);

        await QueueKeyFrame(new KeyFrame
        {
            CameraId = cameraId,
            Timestamp = DateTime.UtcNow,
            Image = compressed,      // 3-5KB
            Description = description // "Two customers at checkout, employee scanning items"
        });
    }
}
```

## 📐 UI Structure (Based on React Design Reference)

The Windows UI follows the React design in `/VisionOps UI Design/` with four main sections:

### 1. Camera Access Tab
- Auto-discovery with network scanning
- RTSP connection testing
- Stream health indicators
- Sub-stream configuration

### 2. Frame Monitoring Tab
- Live preview (1 fps thumbnail)
- Detection zone drawing
- Current object counts
- Florence-2 descriptions display

### 3. Data Analysis Tab
- Local metrics only (no cloud data)
- Processing statistics
- Memory/CPU usage graphs
- Error logs

### 4. User Settings Tab
- Service start/stop controls
- Supabase credentials
- Processing thresholds
- Update preferences

## 💻 Technology Stack (LOCKED - NO CHANGES)

### Core Technologies
```yaml
Platform: Windows 10/11 (64-bit only)
Runtime: .NET 8 (self-contained)
Language: C# 12 with nullable refs
UI Framework: WPF with MVVM
Video: FFmpeg process (NOT OpenCVSharp direct)
AI Runtime: ONNX Runtime 1.17+ with OpenVINO
Local DB: SQLite with EF Core 8
Cloud DB: Supabase (PostgreSQL + pgvector)
Compression: WebP.Net for images
Installer: WiX Toolset 4.0
Updates: Velopack for auto-updates
```

### Critical NuGet Packages
```xml
<!-- Core -->
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.*" />

<!-- Video (Process Isolation) -->
<PackageReference Include="FFMpegCore" Version="5.1.0" />
<PackageReference Include="OpenCvSharp4.Windows" Version="4.9.0" /> <!-- For Mat only -->

<!-- AI -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.17.0" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.Extensions" Version="0.11.0" />

<!-- Florence-2 -->
<PackageReference Include="Microsoft.ML.Tokenizers" Version="0.21.0" />

<!-- Compression -->
<PackageReference Include="WebP.Net" Version="1.0.0" />

<!-- Cloud -->
<PackageReference Include="Supabase" Version="0.13.*" />

<!-- Updates -->
<PackageReference Include="Velopack" Version="0.0.359" />
```

## 🏗️ Project Structure

```
VisionOps/
├── src/
│   ├── VisionOps.Core/           # Domain models, interfaces
│   ├── VisionOps.Service/        # Windows Service implementation
│   ├── VisionOps.UI/             # WPF configuration tool
│   ├── VisionOps.Data/           # EF Core, repositories
│   ├── VisionOps.Video/          # FFmpeg process management
│   ├── VisionOps.AI/             # ONNX inference, Florence-2
│   ├── VisionOps.Cloud/          # Supabase sync
│   └── VisionOps.Tests/          # Unit and integration tests
├── models/
│   ├── yolov8n.onnx             # Object detection (INT8)
│   └── florence2-base.onnx      # Vision-language (INT8)
├── tools/
│   └── VisionOps.Installer/     # WiX MSI installer
└── docs/
    ├── CLAUDE.md                # This file - master guide
    ├── PLANNING.md              # Architecture decisions
    └── TASKS.md                 # Implementation tracking
```

## 🎯 Performance Constraints (STRICTLY ENFORCED)

### Hardware Limits
```yaml
CPU: Intel i3-i5 (4-8 cores)
RAM: 8GB minimum, 12GB with Florence-2
Storage: 256GB SSD minimum, 512GB recommended
Network: 10 Mbps upload minimum
Cameras: 5 maximum (was 10, reduced for Florence-2)
```

### Performance Targets
```yaml
CPU Usage: <60% average, <80% peak
Memory: <6GB total (including 2GB for Florence-2)
Processing: 1 frame per 3 seconds per camera
Inference: <200ms per batch
Key Frames: 1 per 10 seconds per camera
Sync Interval: Every 30 seconds
Startup Time: <30 seconds
```

### Memory Management Rules

#### ALWAYS Use:
- `ArrayPool<byte>` for video buffers
- `RecyclableMemoryStream` for streams
- Object pooling for Mat objects
- Disposal patterns with `using`
- Weak references for caches

#### NEVER:
- Keep >30 frames in memory
- Process cameras in parallel
- Create arrays in hot loops
- Use LINQ in performance paths
- Ignore IDisposable

## 🔥 Common Anti-Patterns to Avoid

### 1. Over-Engineering
```csharp
// BAD: Complex abstraction
public interface IFrameProcessorFactory<TConfig>
    where TConfig : IProcessorConfiguration { }

// GOOD: Simple and direct
public class FrameProcessor { }
```

### 2. Parallel Camera Processing
```csharp
// BAD: Will cause CPU/memory spikes
await Task.WhenAll(cameras.Select(ProcessCamera));

// GOOD: Sequential processing
foreach (var camera in cameras)
{
    await ProcessCamera(camera);
    await Task.Delay(3000); // Frame interval
}
```

### 3. Multiple ONNX Sessions
```csharp
// BAD: Each camera gets own session
var session = new InferenceSession(modelPath);

// GOOD: Shared singleton session
var session = SharedInferenceEngine.GetSession();
```

### 4. Direct OpenCV Capture
```csharp
// BAD: Memory leaks with RTSP
using var capture = new VideoCapture(rtspUrl);

// GOOD: FFmpeg process isolation
var ffmpeg = new FFmpegStreamProcessor();
await ffmpeg.ProcessStream(rtspUrl);
```

## 📋 Testing Requirements

### Unit Tests (80% Coverage Minimum)
- xUnit test framework
- FluentAssertions for readability
- NSubstitute for mocking
- Test data builders

### Integration Tests
- Real RTSP stream testing
- 24-hour memory leak tests
- Thermal throttling validation
- Supabase sync verification

### Performance Tests
- CPU profiling with PerfView
- Memory profiling with dotMemory
- Load testing with 5 cameras
- Inference benchmarking

## 🚀 Quick Commands

```bash
# Build
dotnet build VisionOps.sln -c Release

# Test
dotnet test --collect:"XPlat Code Coverage" --logger:console

# Run Service Locally
dotnet run --project src/VisionOps.Service

# Install Service
sc create VisionOps binPath="C:\Program Files\VisionOps\VisionOps.Service.exe"
sc config VisionOps start=auto
sc start VisionOps

# Create Installer
dotnet publish -c Release -r win-x64 --self-contained
msiexec /i VisionOps.Installer\bin\Release\VisionOps.msi

# Check Service Logs
eventvwr.msc
# Navigate to: Applications and Services Logs > VisionOps
```

## 📊 Monitoring & Diagnostics

### Key Metrics to Track
```csharp
public class SystemMetrics
{
    public float CpuUsage { get; set; }          // Target: <60%
    public long MemoryUsageMB { get; set; }      // Target: <6000
    public int CpuTemperature { get; set; }      // Target: <70°C
    public int FramesProcessed { get; set; }     // Target: 0.3fps/camera
    public int InferenceLatencyMs { get; set; }  // Target: <200ms
    public int SyncQueueSize { get; set; }       // Warn: >1000
    public int ActiveCameras { get; set; }       // Max: 5
}
```

### Health Check Endpoint
```csharp
public class HealthCheckService
{
    public async Task<HealthStatus> GetStatus()
    {
        return new HealthStatus
        {
            ServiceRunning = IsServiceRunning(),
            CamerasConnected = GetConnectedCameras(),
            MemoryUsageMB = GC.GetTotalMemory(false) / 1_048_576,
            CpuTemperature = await GetCpuTemperature(),
            LastSyncTime = GetLastSyncTime(),
            Errors = GetRecentErrors(TimeSpan.FromMinutes(5))
        };
    }
}
```

## 🔐 Security Considerations

### Credentials Management
- Use Windows Credential Manager for Supabase keys
- Never hardcode credentials
- Implement key rotation support
- Use service accounts for deployment

### Network Security
- RTSP over secure networks only
- TLS 1.2+ for Supabase communication
- Local firewall rules for service
- No internet access required for core operation

### Privacy Compliance
- Face blurring before compression
- 7-day retention policy
- No raw video storage
- Audit logs for data access

## 📝 Session State Management Protocol

### Starting a New Session
1. **Read Documentation**
   - This CLAUDE.md file completely
   - PLANNING.md for architecture context
   - TASKS.md for current work items

2. **Assess Current State**
   - Check completed milestones
   - Identify blocked tasks
   - Review recent commits

3. **Deploy Agents**
   - Identify required expertise
   - Assign specialized agents
   - Coordinate multi-domain work

4. **Execute Implementation**
   - Follow established patterns
   - Maintain simplicity
   - Test continuously

5. **Update Progress**
   - Mark tasks complete in TASKS.md
   - Update documentation if needed
   - Commit with descriptive messages

### End of Session Checklist
- [ ] All code compiles without warnings
- [ ] Tests are passing
- [ ] TASKS.md is updated
- [ ] Memory leaks checked
- [ ] Performance validated
- [ ] Documentation current

## 🎓 Learning Resources

### Internal Documentation
- PLANNING.md - System architecture and decisions
- TASKS.md - Implementation roadmap and progress
- /VisionOps UI Design/ - React reference implementation

### External Resources
- [ONNX Runtime C# Guide](https://onnxruntime.ai/docs/api/csharp-api.html)
- [FFMpegCore Documentation](https://github.com/rosenbjerg/FFMpegCore)
- [Supabase C# Client](https://github.com/supabase-community/supabase-csharp)
- [WiX Toolset v4](https://wixtoolset.org/docs/fourthree/)
- [Velopack Auto-Updates](https://docs.velopack.io/)

## ⚠️ CRITICAL REMINDERS

1. **ALWAYS use specialized agents for domain work**
2. **NEVER skip Phase 0 production hardening**
3. **ALWAYS use FFmpeg process isolation for video**
4. **NEVER process cameras in parallel**
5. **ALWAYS use shared ONNX sessions**
6. **NEVER store raw video data**
7. **ALWAYS implement thermal throttling**
8. **NEVER ignore memory management**
9. **ALWAYS update TASKS.md immediately**
10. **NEVER over-engineer solutions**

## 🏁 Final Words

VisionOps is a production system that must run reliably 24/7 on constrained hardware. Every decision should prioritize:

1. **Reliability** - It must not crash
2. **Performance** - It must stay within resource limits
3. **Simplicity** - It must be maintainable
4. **Privacy** - It must protect user data

When in doubt, choose the simpler solution. When facing issues, profile first. When implementing features, hardening comes first.

**Remember**: This system runs unattended in production environments. Build it like lives depend on it working correctly, because business operations do.

---

*Last Updated: January 2025*
*Version: 2.0 - Post-Production Hardening Requirements*
*Primary Author: VisionOps System Architect*