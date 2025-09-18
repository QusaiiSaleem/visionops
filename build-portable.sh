#!/bin/bash

# Build script for creating a portable version of VisionOps on macOS
# This creates a ZIP file that can be extracted on Windows

VERSION=${1:-1.0.0}
BUILD_DIR="build-portable"
OUTPUT_DIR="VisionOps-$VERSION-portable"

echo "🚀 Building VisionOps Portable v$VERSION"

# Clean previous builds
rm -rf $BUILD_DIR
mkdir -p $BUILD_DIR/$OUTPUT_DIR

echo "📦 Building .NET projects..."

# Build Service
dotnet publish src/VisionOps.Service/VisionOps.Service.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=true \
    -o $BUILD_DIR/$OUTPUT_DIR/Service

# Build UI
dotnet publish src/VisionOps.UI/VisionOps.UI.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=true \
    -o $BUILD_DIR/$OUTPUT_DIR/UI

# Create models directory
mkdir -p $BUILD_DIR/$OUTPUT_DIR/models

# Create config directory
mkdir -p $BUILD_DIR/$OUTPUT_DIR/config

# Add launch script for Windows
cat > $BUILD_DIR/$OUTPUT_DIR/Run-VisionOps.bat << 'EOF'
@echo off
echo Starting VisionOps Portable...
cd /d "%~dp0"
start "" "UI\VisionOps.UI.exe"
EOF

# Add install service script
cat > $BUILD_DIR/$OUTPUT_DIR/Install-Service.bat << 'EOF'
@echo off
echo Installing VisionOps Service...
echo This requires Administrator privileges
cd /d "%~dp0"
sc create VisionOps binPath="%CD%\Service\VisionOps.Service.exe" start=auto
sc description VisionOps "Edge video analytics platform"
echo Service installed successfully!
pause
EOF

# Add uninstall script
cat > $BUILD_DIR/$OUTPUT_DIR/Uninstall-Service.bat << 'EOF'
@echo off
echo Uninstalling VisionOps Service...
echo This requires Administrator privileges
sc stop VisionOps
sc delete VisionOps
echo Service uninstalled successfully!
pause
EOF

# Add README
cat > $BUILD_DIR/$OUTPUT_DIR/README.txt << 'EOF'
VisionOps Portable Edition
==========================

Quick Start:
1. Run "Install-Service.bat" as Administrator (one time only)
2. Run "Run-VisionOps.bat" to open configuration UI
3. Configure cameras and Supabase settings
4. Service will start automatically

Requirements:
- Windows 10/11 (64-bit)
- 8GB RAM minimum
- Intel i3+ CPU

Files:
- Run-VisionOps.bat: Launch configuration UI
- Install-Service.bat: Install Windows Service (run as Admin)
- Uninstall-Service.bat: Remove Windows Service (run as Admin)
- Service/: Windows Service files
- UI/: Configuration interface
- models/: AI models (auto-downloaded on first run)
- config/: Configuration files

Support:
https://github.com/QusaiiSaleem/visionops
EOF

echo "📦 Creating ZIP archive..."
cd $BUILD_DIR
zip -r ../VisionOps-$VERSION-portable.zip $OUTPUT_DIR

echo "✅ Build complete!"
echo "📦 Output: VisionOps-$VERSION-portable.zip"
echo ""
echo "This portable version can be:"
echo "1. Extracted on any Windows PC"
echo "2. Run without installation (portable mode)"
echo "3. Installed as a service using the included script"