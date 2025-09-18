# VisionOps Phase 0 - Quick Implementation Checklist

## 🏃 Quick Start Commands

```bash
# Install required NuGet packages
dotnet add package Polly --version 8.2.0
dotnet add package Microsoft.Extensions.Http.Polly --version 8.0.0
dotnet add package LibreHardwareMonitorLib --version 0.9.3
dotnet add package FFMpegCore --version 5.1.0
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks --version 8.0.0
dotnet add package AspNetCore.HealthChecks.UI --version 8.0.1
dotnet add package Microsoft.Diagnostics.Runtime --version 3.1.0
dotnet add package System.IO.Pipes --version 4.3.0
dotnet add package System.Buffers --version 4.5.1
```

## ✅ Memory Monitoring Implementation Checklist

- [ ] Create `MemoryMonitorService` class
- [ ] Add performance counters for Private Bytes and GC Heap
- [ ] Implement 5-minute monitoring interval
- [ ] Track memory growth rate (MB/hour)
- [ ] Add automatic restart scheduling at 3 AM
- [ ] Log to Windows Event Log (Event ID 1000-1099)
- [ ] Create health check endpoint
- [ ] Test: Run for 24 hours, verify <50MB growth

## ✅ FFmpeg Camera Implementation Checklist

- [ ] Create `FFmpegCameraConnection` class
- [ ] Download FFmpeg.exe to application folder
- [ ] Implement named pipe server for frame transfer
- [ ] Configure FFmpeg arguments:
  ```
  -rtsp_transport tcp -i {url} -vf fps=1/3,scale=640:480 -f rawvideo -pix_fmt bgr24 pipe:
  ```
- [ ] Add process health monitoring
- [ ] Implement automatic process restart on crash
- [ ] Create abstraction interface for camera switching
- [ ] Test: Verify 0MB/hour memory leak over 24 hours

## ✅ Watchdog Service Implementation Checklist

- [ ] Create separate `VisionOps.Watchdog` project
- [ ] Implement named pipe client/server
- [ ] Configure 30-second heartbeat interval
- [ ] Add timeout detection (60 seconds)
- [ ] Implement service restart using `ServiceController`
- [ ] Create Windows Event Log source
- [ ] Add email/SMS alerting
- [ ] Configure as Windows Service
- [ ] Test: Kill main service, verify restart within 60s

## ✅ Thermal Management Implementation Checklist

- [ ] Install LibreHardwareMonitorLib
- [ ] Create `ThermalManagementService`
- [ ] Configure temperature zones:
  - Normal: <65°C
  - Warning: 65-70°C
  - Throttle: 70-75°C
  - Critical: >75°C
- [ ] Implement CPU temperature reading
- [ ] Add WMI fallback for temperature
- [ ] Create adaptive processing levels
- [ ] Reduce cameras/batch size based on temperature
- [ ] Test: Heat stress test, verify throttling at 70°C

## ✅ Circuit Breaker Implementation Checklist

- [ ] Install Polly and extensions
- [ ] Create `CameraCircuitBreaker` class
- [ ] Configure failure threshold (3 failures)
- [ ] Set break duration (5 minutes)
- [ ] Implement retry with exponential backoff
- [ ] Add timeout policy (30 seconds)
- [ ] Create fallback strategy
- [ ] Log state changes to Event Log
- [ ] Add circuit breaker metrics
- [ ] Test: Disconnect camera, verify circuit opens after 3 failures

## ✅ Data Aggregation Implementation Checklist

- [ ] Create `MetricsAggregationService`
- [ ] Implement 5-minute tumbling windows
- [ ] Use `System.Threading.Channels` for queue
- [ ] Calculate statistics (avg, min, max, p50, p95, p99)
- [ ] Add Brotli compression
- [ ] Implement late arrival tolerance (1 minute)
- [ ] Create aggregated metrics table
- [ ] Set up retention policies
- [ ] Test: Verify 100:1 data reduction ratio

## 📊 Testing Verification

### Memory Leak Test (24 hours)
```powershell
# Monitor private bytes
Get-Counter "\Process(VisionOps.Service)\Private Bytes" -Continuous -SampleInterval 60 | 
    Export-Counter -Path "memory_test.csv" -FileFormat CSV
```

### Thermal Test
```powershell
# Run CPU stress test
Start-Process "prime95.exe"
# Monitor service behavior
Get-EventLog -LogName Application -Source "VisionOps" -Newest 100
```

### Circuit Breaker Test
```powershell
# Block RTSP port to simulate failure
New-NetFirewallRule -DisplayName "Block RTSP" -Direction Outbound -Protocol TCP -LocalPort 554 -Action Block
# Check Event Log for circuit breaker events
Get-EventLog -LogName Application -Source "VisionOps" | Where {$_.EventID -eq 2001}
```

## 🚦 Go/No-Go Criteria

Before proceeding to Phase 1:

| Metric | Target | How to Measure |
|--------|--------|----------------|
| Memory Growth | <50MB/24hr | Performance Monitor |
| Unhandled Exceptions | 0 | Event Viewer |
| Watchdog Recovery | <60 seconds | Manual test |
| Thermal Throttle | Maintains >70% speed | Task Manager |
| Circuit Breaker | Opens in <3 failures | Event Log |
| Data Compression | 100:1 ratio | Database query |
| Service Uptime | 72 hours continuous | PowerShell Get-Service |

## 🛠️ Configuration Files

### appsettings.Production.json
```json
{
  "VisionOps": {
    "MemoryMonitoring": {
      "MaxMemoryGB": 6,
      "UnmanagedLeakThresholdMB": 500,
      "LeakRateThresholdMBPerHour": 10,
      "MonitoringIntervalMinutes": 5
    },
    "Watchdog": {
      "HeartbeatIntervalSeconds": 30,
      "TimeoutSeconds": 60,
      "MaxConsecutiveFailures": 3,
      "RestartDelaySeconds": 10
    },
    "ThermalManagement": {
      "NormalTempC": 65,
      "WarningTempC": 70,
      "ThrottleTempC": 75,
      "CriticalTempC": 80,
      "MonitoringIntervalSeconds": 10
    },
    "CircuitBreaker": {
      "FailureThreshold": 0.5,
      "SamplingDurationMinutes": 1,
      "MinimumThroughput": 3,
      "BreakDurationMinutes": 5
    },
    "DataAggregation": {
      "WindowSizeMinutes": 5,
      "LateArrivalToleranceMinutes": 1,
      "CompressionLevel": "Optimal"
    }
  }
}
```

## 🚀 Deployment Steps

1. **Deploy Watchdog First**
   ```powershell
   sc create "VisionOps.Watchdog" binPath="C:\VisionOps\Watchdog\VisionOps.Watchdog.exe"
   sc config "VisionOps.Watchdog" start=delayed-auto
   sc start "VisionOps.Watchdog"
   ```

2. **Deploy Main Service**
   ```powershell
   sc create "VisionOps" binPath="C:\VisionOps\Service\VisionOps.Service.exe"
   sc config "VisionOps" depend="VisionOps.Watchdog"
   sc failure "VisionOps" reset=86400 actions=restart/60000/restart/60000/restart/60000
   sc start "VisionOps"
   ```

3. **Verify Health Checks**
   ```powershell
   Invoke-WebRequest -Uri "http://localhost:5000/health" | Select-Object -ExpandProperty Content
   ```

4. **Monitor Event Logs**
   ```powershell
   Get-EventLog -LogName Application -Source "VisionOps*" -Newest 50
   ```

## 📈 Performance Baseline

After Phase 0 implementation, you should see:

| Metric | Before | After |
|--------|--------|-------|
| Memory per camera | 800MB+ | 500MB |
| Memory leak rate | 1-2MB/hour | <2MB/24hr |
| Recovery from failure | Never | <60 seconds |
| Performance at 75°C | 30% | 70%+ |
| Database records/hour | 11,880 | 120 |
| MTBF (Mean Time Between Failures) | 3-5 days | 30+ days |

## 🔗 Quick Links

- [PRODUCTION_HARDENING.md](PRODUCTION_HARDENING.md) - Detailed implementations
- [PRODUCTION_ASSESSMENT.md](PRODUCTION_ASSESSMENT.md) - Gap analysis
- [TASKS.md](TASKS.md) - Full task list
- [PLANNING.md](PLANNING.md) - Architecture overview

---

**Remember**: These aren't optional enhancements - they're **survival requirements** for production deployment!
