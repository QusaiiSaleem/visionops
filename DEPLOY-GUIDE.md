# 🚀 VisionOps Deployment Guide

## Step 1: Create GitHub Repository

### A. Create the Repo
1. Go to https://github.com/new
2. Repository name: `visionops` (or your preferred name)
3. Description: "Edge video analytics platform with AI-powered monitoring"
4. Set to **Public** (for auto-updates to work without authentication)
5. **DON'T** initialize with README (we already have one)
6. Click "Create repository"

### B. Push Your Code
```bash
# In your VisionOps directory
cd /Users/qusaiabushanap/dev/VisionOps

# Initialize git if not already done
git init

# Add all files
git add .

# Commit
git commit -m "Initial commit - VisionOps Phase 0 Complete"

# Add your GitHub repo as origin (replace with YOUR username/repo)
git remote add origin https://github.com/YOUR_USERNAME/visionops.git

# Push to GitHub
git push -u origin main
```

## Step 2: Build the MSI Installer (Windows Required)

### A. Transfer to Windows Machine
Since you're on macOS, you need to get the code to a Windows machine:

**Option 1: Clone from GitHub (after Step 1)**
```powershell
# On Windows machine
git clone https://github.com/YOUR_USERNAME/visionops.git
cd visionops
```

**Option 2: Copy via USB/Network**
- Copy the entire VisionOps folder to Windows

### B. Build the Installer
```powershell
# On Windows, in the VisionOps directory

# Install prerequisites (one-time)
dotnet tool install -g wix
dotnet tool install -g velopack

# Build the installer
.\build-installer.ps1 -Version 1.0.0

# This creates:
# - VisionOps-1.0.0.msi (installer)
# - VisionOps-1.0.0-portable.zip (portable version)
# - velopack-releases/ (update packages)
```

## Step 3: Create GitHub Release

### Option A: Manual Release (Easier)
1. Go to `https://github.com/YOUR_USERNAME/visionops/releases/new`
2. Click "Choose a tag" and create new tag: `v1.0.0`
3. Release title: "VisionOps v1.0.0 - Production Ready"
4. Upload files:
   - `VisionOps-1.0.0.msi`
   - `VisionOps-1.0.0-portable.zip`
   - All files from `velopack-releases/` folder
5. Add release notes:
```markdown
## 🎯 First Release - Phase 0 Complete

### Features
- ✅ Production-hardened Windows Service
- ✅ Memory leak prevention with FFmpeg isolation
- ✅ Thermal management and monitoring
- ✅ Auto-update system via GitHub
- ✅ Configuration UI
- ✅ Automatic AI model downloads

### Installation
1. Download `VisionOps-1.0.0.msi`
2. Run as Administrator
3. Launch VisionOps from Start Menu
4. Configure Supabase (optional)
5. Add cameras

### System Requirements
- Windows 10/11 (64-bit)
- .NET 8 Runtime
- 8GB RAM minimum
- Intel i3+ CPU

### Auto-Updates
The application will automatically check for updates every 6 hours.
```
6. Click "Publish release"

### Option B: Automated Release (via GitHub Actions)
```bash
# Tag and push - this triggers automatic build and release
git tag v1.0.0
git push origin v1.0.0

# GitHub Actions will:
# - Build the project
# - Run tests
# - Create MSI installer
# - Create Velopack packages
# - Create GitHub Release
# - Upload all artifacts
```

## Step 4: Update Configuration for Auto-Updates

### Update the Auto-Update URL
Once your GitHub repo is created, update the configuration:

**Edit:** `src/VisionOps.UI/Services/AutoUpdateService.cs`
```csharp
// Line 24 - Update with YOUR GitHub repo
_updateFeedUrl = "https://github.com/YOUR_USERNAME/visionops/releases";
```

**Edit:** `src/VisionOps.UI/appsettings.json`
```json
"AutoUpdate": {
    "UpdateFeedUrl": "https://github.com/YOUR_USERNAME/visionops/releases"
}
```

Then rebuild and create a new release (v1.0.1) with this fix.

## Step 5: Test the Deployment

### A. Test Installation
1. Download the MSI from GitHub Releases
2. Install on a clean Windows machine
3. Verify service starts
4. Check UI works

### B. Test Auto-Updates
1. Make a small change (e.g., version number)
2. Build new installer with version 1.0.1
3. Create new GitHub Release
4. Wait for installed app to check for updates (or click "Check Now")
5. Verify update notification appears
6. Apply update and verify it works

## 📝 Quick Checklist

- [ ] Create GitHub repository
- [ ] Push code to GitHub
- [ ] Transfer to Windows machine
- [ ] Run `build-installer.ps1`
- [ ] Create GitHub Release
- [ ] Upload MSI and Velopack files
- [ ] Test installation
- [ ] Test auto-updates

## 🔧 Troubleshooting

### Build Fails on Windows
```powershell
# Ensure you have .NET 8 SDK
dotnet --version

# Install required tools
dotnet tool install -g wix
dotnet tool install -g velopack

# Clean and retry
dotnet clean
.\build-installer.ps1 -Version 1.0.0
```

### Auto-Updates Not Working
1. Check GitHub repo is public
2. Verify URL in AutoUpdateService.cs
3. Check releases have Velopack files
4. Look at logs in `%LOCALAPPDATA%\VisionOps\UI\Logs`

### MSI Won't Install
- Run as Administrator
- Check Windows version (needs Windows 10+)
- Ensure .NET 8 runtime installed
- Check Windows Event Log for errors

## 🎯 What Happens After Deployment

1. **Users Download MSI** from `https://github.com/YOUR_USERNAME/visionops/releases`
2. **Install and Run** - Service starts automatically
3. **AI Models Download** - Automatically from official sources
4. **Configure** - Add cameras and Supabase (optional)
5. **Auto-Updates** - Check GitHub every 6 hours
6. **You Push Updates** - Users get them automatically!

## 📦 File Locations After Installation

```
C:\Program Files\VisionOps\
├── Service\              # Windows Service files
├── UI\                   # Configuration UI
├── models\               # AI models (auto-downloaded)
└── config\               # Configuration files

C:\ProgramData\VisionOps\
├── data\                 # SQLite database
├── logs\                 # Service logs
└── temp\                 # Temporary files

%LOCALAPPDATA%\VisionOps\
└── UI\
    └── Logs\            # UI application logs
```

## 🚀 Next Steps After First Release

1. **Monitor Issues** - Check GitHub Issues for user reports
2. **Plan v1.1.0** - Add camera auto-discovery
3. **Incremental Updates** - Push small improvements
4. **User Feedback** - Gather from early adopters

---

**Need Help?**
- Check the logs
- Open an issue on GitHub
- Review `CLAUDE.md` for architecture details