# VisionOps Deployment Checklist

## 📋 Pre-Deployment Phase 0 Verification

### ✅ Memory Management
- [ ] **24-Hour Memory Test**: Run system with 5 cameras for 24 hours
  - [ ] Memory usage stays below 6GB
  - [ ] No memory growth > 50MB/day
  - [ ] No memory leaks detected
  - [ ] FFmpeg processes properly isolated
  - [ ] Buffer pools functioning correctly

### 🌡️ Thermal Management
- [ ] **Load Testing**: Run at maximum capacity for 4 hours
  - [ ] CPU temperature stays below 70°C
  - [ ] Thermal throttling activates correctly
  - [ ] Camera reduction works (5→3→1)
  - [ ] Recovery after cooling
  - [ ] No emergency shutdowns

### 🛡️ Service Stability
- [ ] **Recovery Testing**
  - [ ] Watchdog recovers service in <30 seconds
  - [ ] Daily 3 AM restart works correctly
  - [ ] State persists across restarts
  - [ ] Minidumps generated on crash
  - [ ] Crash recovery successful

### 🤖 AI Performance
- [ ] **Inference Testing**
  - [ ] Single ONNX session stable
  - [ ] Batch processing <200ms
  - [ ] Florence-2 descriptions generating
  - [ ] Key frames compressed to 3-5KB
  - [ ] Model quantization verified

## 🖥️ System Requirements

### Hardware Verification
- [ ] **Minimum Specifications Met**
  - [ ] CPU: Intel i3 or better (4+ cores)
  - [ ] RAM: 8GB minimum (12GB with Florence-2)
  - [ ] Storage: 256GB SSD with >100GB free
  - [ ] Network: 10 Mbps upload verified
  - [ ] Windows 10/11 64-bit

### Software Prerequisites
- [ ] **.NET 8 Runtime** installed
  ```powershell
  winget install Microsoft.DotNet.Runtime.8
  ```
- [ ] **Visual C++ Redistributables** installed
  ```powershell
  winget install Microsoft.VCRedist.2015+.x64
  ```
- [ ] **FFmpeg** available in PATH or bundled
- [ ] **Windows Updates** current

## 🔧 Configuration

### Supabase Setup
- [ ] **Cloud Configuration**
  - [ ] Supabase project created
  - [ ] Database schema deployed
  - [ ] RLS policies configured
  - [ ] API keys obtained
  - [ ] Connection tested

### Camera Configuration
- [ ] **RTSP Streams**
  - [ ] Camera discovery working
  - [ ] RTSP URLs verified
  - [ ] Authentication configured
  - [ ] Sub-streams selected (640x480)
  - [ ] Connection stable for 1 hour

### AI Models
- [ ] **Model Files**
  - [ ] YOLOv8n.onnx downloaded (6.3MB)
  - [ ] Florence-2-base.onnx downloaded (120MB)
  - [ ] Models placed in /models folder
  - [ ] INT8 quantization verified
  - [ ] Inference test passed

## 🚀 Installation Process

### Service Installation
```powershell
# Run as Administrator
- [ ] Create installation directory
      New-Item -Path "C:\Program Files\VisionOps" -ItemType Directory

- [ ] Copy files
      Copy-Item -Path ".\artifacts\*" -Destination "C:\Program Files\VisionOps\" -Recurse

- [ ] Install service
      sc create VisionOps binPath="C:\Program Files\VisionOps\VisionOps.Service.exe"
      sc config VisionOps start=auto
      sc description VisionOps "VisionOps Edge Video Analytics Service"

- [ ] Configure service recovery
      sc failure VisionOps reset=86400 actions=restart/30000/restart/60000/restart/120000

- [ ] Set service account (if needed)
      sc config VisionOps obj=LocalSystem

- [ ] Start service
      sc start VisionOps
```

### Firewall Configuration
- [ ] **Windows Firewall Rules**
  ```powershell
  - [ ] Allow service executable
        New-NetFirewallRule -DisplayName "VisionOps Service" -Direction Inbound -Program "C:\Program Files\VisionOps\VisionOps.Service.exe" -Action Allow

  - [ ] Allow RTSP ports
        New-NetFirewallRule -DisplayName "RTSP" -Direction Inbound -Protocol TCP -LocalPort 554 -Action Allow

  - [ ] Allow ONVIF discovery
        New-NetFirewallRule -DisplayName "ONVIF Discovery" -Direction Inbound -Protocol UDP -LocalPort 3702 -Action Allow
  ```

### Windows Defender Exclusions
- [ ] **Add Exclusions**
  ```powershell
  - [ ] Exclude VisionOps folder
        Add-MpPreference -ExclusionPath "C:\Program Files\VisionOps"

  - [ ] Exclude database folder
        Add-MpPreference -ExclusionPath "C:\ProgramData\VisionOps"

  - [ ] Exclude FFmpeg processes
        Add-MpPreference -ExclusionProcess "ffmpeg.exe"
  ```

## 📊 Post-Installation Verification

### Service Health
- [ ] **Initial Checks**
  - [ ] Service status: Running
  - [ ] Event log: No errors
  - [ ] CPU usage: <20% idle
  - [ ] Memory usage: <2GB idle
  - [ ] Database created

### Camera Connectivity
- [ ] **Stream Verification**
  - [ ] All cameras discovered
  - [ ] Streams connected
  - [ ] Frames processing
  - [ ] No disconnections in 10 minutes

### AI Processing
- [ ] **Inference Verification**
  - [ ] YOLOv8 detecting objects
  - [ ] Florence-2 generating descriptions
  - [ ] Key frames being saved
  - [ ] Batch processing working

### Cloud Sync
- [ ] **Synchronization**
  - [ ] Connection to Supabase established
  - [ ] Data uploading successfully
  - [ ] Sync queue processing
  - [ ] No sync errors

## 🔍 Monitoring Setup

### Windows Event Log
- [ ] **Configure Logging**
  ```powershell
  - [ ] Create custom event log
        New-EventLog -LogName "VisionOps" -Source "VisionOps.Service"

  - [ ] Set log size
        Limit-EventLog -LogName "VisionOps" -MaximumSize 100MB
  ```

### Performance Monitoring
- [ ] **Performance Counters**
  - [ ] CPU usage counter configured
  - [ ] Memory counter configured
  - [ ] Disk I/O counter configured
  - [ ] Network counter configured

### Alerting
- [ ] **Alert Configuration**
  - [ ] Email alerts configured
  - [ ] Thermal alerts set (>70°C)
  - [ ] Memory alerts set (>5.5GB)
  - [ ] Service down alerts
  - [ ] Sync failure alerts

## 📝 Documentation

### System Documentation
- [ ] **Document Configuration**
  - [ ] Camera IPs and credentials
  - [ ] Supabase connection details
  - [ ] Performance thresholds
  - [ ] Alert recipients
  - [ ] Maintenance schedule

### Runbook Creation
- [ ] **Operational Procedures**
  - [ ] Service restart procedure
  - [ ] Camera troubleshooting
  - [ ] Memory issue resolution
  - [ ] Thermal throttling response
  - [ ] Backup procedures

## 🔄 Maintenance Planning

### Scheduled Maintenance
- [ ] **Maintenance Windows**
  - [ ] Daily restart at 3 AM configured
  - [ ] Weekly database cleanup scheduled
  - [ ] Monthly model updates planned
  - [ ] Quarterly performance review

### Backup Strategy
- [ ] **Data Protection**
  - [ ] Database backup scheduled
  - [ ] Configuration backup
  - [ ] Key frames archive
  - [ ] Disaster recovery plan

## ✅ Final Sign-off

### Production Readiness
- [ ] **All Phase 0 requirements met**
- [ ] **24-hour stability test passed**
- [ ] **Performance targets achieved**
- [ ] **Documentation complete**
- [ ] **Team trained on operations**
- [ ] **Support contact established**

### Approval
- [ ] Technical Lead approval
- [ ] Operations team approval
- [ ] Security review passed
- [ ] Business stakeholder sign-off

---

## 🚨 Emergency Contacts

- **Technical Support**: support@visionops.com
- **On-Call Engineer**: [Phone Number]
- **Escalation Manager**: [Phone Number]
- **Supabase Support**: [Ticket System]

## 📅 Deployment Log

| Date | Version | Deployed By | Notes |
|------|---------|-------------|-------|
| | | | |
| | | | |
| | | | |

---

*Use this checklist for every VisionOps deployment to ensure production readiness*