# VisionOps Installer Build Script
# Creates MSI installer and Velopack update packages

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$CreateRelease
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warning { Write-Host $args -ForegroundColor Yellow }
function Write-Error { Write-Host $args -ForegroundColor Red }

Write-Info @"

██╗   ██╗██╗███████╗██╗ ██████╗ ███╗   ██╗ ██████╗ ██████╗ ███████╗
██║   ██║██║██╔════╝██║██╔═══██╗████╗  ██║██╔═══██╗██╔══██╗██╔════╝
██║   ██║██║███████╗██║██║   ██║██╔██╗ ██║██║   ██║██████╔╝███████╗
╚██╗ ██╔╝██║╚════██║██║██║   ██║██║╚██╗██║██║   ██║██╔═══╝ ╚════██║
 ╚████╔╝ ██║███████║██║╚██████╔╝██║ ╚████║╚██████╔╝██║     ███████║
  ╚═══╝  ╚═╝╚══════╝╚═╝ ╚═════╝ ╚═╝  ╚═══╝ ╚═════╝ ╚═╝     ╚══════╝

Installer Build System - Version $Version
"@

Write-Info "===============================================`n"

# Check prerequisites
Write-Info "Checking prerequisites..."

# Check .NET SDK
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK not found. Please install from https://dotnet.microsoft.com"
    exit 1
}
Write-Success "✓ .NET SDK $dotnetVersion"

# Check WiX Toolset
$wixInstalled = $false
try {
    $wixVersion = wix --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        $wixInstalled = $true
        Write-Success "✓ WiX Toolset found"
    }
} catch {
    # WiX not found
}

if (-not $wixInstalled) {
    Write-Warning "WiX Toolset not found. Installing..."
    dotnet tool install -g wix
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to install WiX Toolset"
        exit 1
    }
    Write-Success "✓ WiX Toolset installed"
}

# Check Velopack
$velopackInstalled = $false
try {
    $vpkVersion = vpk --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        $velopackInstalled = $true
        Write-Success "✓ Velopack found"
    }
} catch {
    # Velopack not found
}

if (-not $velopackInstalled) {
    Write-Warning "Velopack not found. Installing..."
    dotnet tool install -g Velopack
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to install Velopack"
        exit 1
    }
    Write-Success "✓ Velopack installed"
}

# Build solution
if (-not $SkipBuild) {
    Write-Info "`nBuilding solution..."
    dotnet build VisionOps.sln -c $Configuration -p:Version=$Version
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    Write-Success "✓ Build completed"
}

# Run tests
if (-not $SkipTests) {
    Write-Info "`nRunning Phase 0 tests..."
    dotnet test src/VisionOps.Tests/VisionOps.Tests.csproj `
        -c $Configuration `
        --no-build `
        --filter "Category=Phase0" `
        --logger "console;verbosity=normal"

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Some tests failed"
        $response = Read-Host "Continue anyway? (y/n)"
        if ($response -ne 'y') {
            exit 1
        }
    } else {
        Write-Success "✓ All tests passed"
    }
}

# Create artifacts directory
Write-Info "`nPreparing artifacts..."
$artifactsPath = ".\artifacts"
if (Test-Path $artifactsPath) {
    Remove-Item -Path $artifactsPath -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsPath | Out-Null

# Publish Service
Write-Info "Publishing VisionOps.Service..."
dotnet publish .\src\VisionOps.Service\VisionOps.Service.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o "$artifactsPath\Service"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Service publish failed"
    exit 1
}
Write-Success "✓ Service published"

# Publish UI
Write-Info "Publishing VisionOps.UI..."
dotnet publish .\src\VisionOps.UI\VisionOps.UI.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o "$artifactsPath\UI"

if ($LASTEXITCODE -ne 0) {
    Write-Error "UI publish failed"
    exit 1
}
Write-Success "✓ UI published"

# Copy configuration files
Write-Info "Copying configuration files..."
Copy-Item -Path ".\src\VisionOps.Service\appsettings.json" -Destination "$artifactsPath\Service\"
Copy-Item -Path ".\src\VisionOps.UI\appsettings.json" -Destination "$artifactsPath\UI\"

# Create models directory placeholder
New-Item -ItemType Directory -Path "$artifactsPath\models" -Force | Out-Null
@"
AI Models Required:
- yolov8n.onnx (6.3MB) - Download from: https://github.com/ultralytics/assets/releases
- florence2-base.onnx (120MB) - Download from: https://huggingface.co/microsoft/Florence-2-base

Place downloaded models in this directory.
"@ | Out-File -FilePath "$artifactsPath\models\README.txt"

Write-Success "✓ Artifacts prepared"

# Build MSI Installer
Write-Info "`nBuilding MSI installer..."

# Create installer working directory
$installerPath = ".\tools\VisionOps.Installer"

# Ensure required files exist
if (-not (Test-Path "$installerPath\License.rtf")) {
    Write-Info "Creating default license file..."
    @"
{\rtf1\ansi\deff0 {\fonttbl {\f0 Times New Roman;}}
\f0\fs24
VisionOps License Agreement\par
\par
This is a proprietary software product.\par
All rights reserved.\par
\par
By installing this software, you agree to the terms and conditions.\par
}
"@ | Out-File -FilePath "$installerPath\License.rtf"
}

# Create banner images if not exist
$imagesPath = "$installerPath\Images"
if (-not (Test-Path $imagesPath)) {
    New-Item -ItemType Directory -Path $imagesPath -Force | Out-Null
    Write-Info "Note: Add custom banner.bmp (493x58) and dialog.bmp (493x312) to $imagesPath"
}

# Build the MSI
Push-Location $installerPath
try {
    wix build `
        -d "ArtifactsPath=$((Get-Location).Path)\..\..\artifacts" `
        -d "ProductVersion=$Version" `
        -d "ProjectDir=$((Get-Location).Path)\" `
        -arch x64 `
        -o "..\..\VisionOps-$Version.msi" `
        Product.wxs

    if ($LASTEXITCODE -ne 0) {
        Write-Error "MSI build failed"
        exit 1
    }
} finally {
    Pop-Location
}

Write-Success "✓ MSI installer created: VisionOps-$Version.msi"

# Create Velopack release
Write-Info "`nCreating Velopack update package..."

vpk pack `
    --packId "VisionOps" `
    --packVersion $Version `
    --packDir "$artifactsPath\UI" `
    --mainExe "VisionOps.UI.exe" `
    --outputDir ".\velopack-releases" `
    --channel "stable" `
    --icon ".\src\VisionOps.UI\visionops.ico"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Velopack package creation failed"
    exit 1
}

Write-Success "✓ Velopack package created"

# Create portable ZIP
Write-Info "`nCreating portable ZIP package..."
$zipPath = ".\VisionOps-$Version-portable.zip"
Compress-Archive -Path "$artifactsPath\*" -DestinationPath $zipPath -Force
Write-Success "✓ Portable package created: $zipPath"

# Summary
Write-Info "`n==============================================="
Write-Success "Installer build completed successfully!"
Write-Info "`nPackages created:"
Write-Info "  - MSI Installer: VisionOps-$Version.msi"
Write-Info "  - Portable ZIP: VisionOps-$Version-portable.zip"
Write-Info "  - Velopack Updates: .\velopack-releases\"

# Create release if requested
if ($CreateRelease) {
    Write-Info "`nCreating GitHub release..."

    # Create release notes
    $releaseNotes = @"
# VisionOps v$Version

## Release Highlights
- Phase 0 Production Hardening Complete
- Memory leak prevention with FFmpeg isolation
- Thermal management and service stability
- Auto-update system via Velopack
- Basic configuration UI

## Installation
1. Download VisionOps-$Version.msi
2. Run as Administrator
3. Launch VisionOps Configuration from Start Menu
4. Configure settings and start service

## System Requirements
- Windows 10/11 (64-bit)
- .NET 8 Runtime
- 8GB RAM minimum
- Intel i3+ CPU
"@

    $releaseNotes | Out-File -FilePath "RELEASE_NOTES.md"

    Write-Info "Ready to create release with:"
    Write-Info "  - Tag: v$Version"
    Write-Info "  - Files: MSI, ZIP, Velopack packages"
    Write-Info "`nRun: git tag v$Version && git push origin v$Version"
    Write-Info "GitHub Actions will create the release automatically"
}

Write-Warning "`n⚠️  Next Steps:"
Write-Warning "  1. Test the installer on a clean Windows machine"
Write-Warning "  2. Verify service starts and runs correctly"
Write-Warning "  3. Test auto-update functionality"
Write-Warning "  4. Create GitHub release with 'git tag v$Version'"