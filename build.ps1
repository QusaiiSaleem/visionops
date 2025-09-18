# VisionOps Build Script
# PowerShell script for building and testing the VisionOps solution

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$Test,
    [switch]$Pack,
    [switch]$Clean,
    [switch]$Restore,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warning { Write-Host $args -ForegroundColor Yellow }
function Write-Error { Write-Host $args -ForegroundColor Red }

# Banner
Write-Info @"

██╗   ██╗██╗███████╗██╗ ██████╗ ███╗   ██╗ ██████╗ ██████╗ ███████╗
██║   ██║██║██╔════╝██║██╔═══██╗████╗  ██║██╔═══██╗██╔══██╗██╔════╝
██║   ██║██║███████╗██║██║   ██║██╔██╗ ██║██║   ██║██████╔╝███████╗
╚██╗ ██╔╝██║╚════██║██║██║   ██║██║╚██╗██║██║   ██║██╔═══╝ ╚════██║
 ╚████╔╝ ██║███████║██║╚██████╔╝██║ ╚████║╚██████╔╝██║     ███████║
  ╚═══╝  ╚═╝╚══════╝╚═╝ ╚═════╝ ╚═╝  ╚═══╝ ╚═════╝ ╚═╝     ╚══════╝

Edge Video Analytics Platform - Build System
"@

Write-Info "Configuration: $Configuration | Platform: $Platform"
Write-Info "===============================================`n"

# Check .NET SDK
Write-Info "Checking .NET SDK..."
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK not found. Please install .NET 8 SDK from https://dotnet.microsoft.com"
    exit 1
}
Write-Success "✓ .NET SDK $dotnetVersion found"

# Clean if requested
if ($Clean) {
    Write-Info "`nCleaning solution..."
    dotnet clean VisionOps.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Clean failed"
        exit 1
    }
    Remove-Item -Path ".\artifacts" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success "✓ Solution cleaned"
}

# Restore packages
if ($Restore -or -not $NoBuild) {
    Write-Info "`nRestoring NuGet packages..."
    dotnet restore VisionOps.sln
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Package restore failed"
        exit 1
    }
    Write-Success "✓ Packages restored"
}

# Build solution
if (-not $NoBuild) {
    Write-Info "`nBuilding solution..."
    dotnet build VisionOps.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    Write-Success "✓ Build successful"
}

# Run tests
if ($Test) {
    Write-Info "`nRunning tests..."
    dotnet test VisionOps.sln -c $Configuration --no-build --logger "console;verbosity=normal" --collect:"XPlat Code Coverage"
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Some tests failed"
    } else {
        Write-Success "✓ All tests passed"
    }
}

# Package for deployment
if ($Pack) {
    Write-Info "`nCreating deployment package..."

    # Create artifacts directory
    $artifactsPath = ".\artifacts"
    New-Item -ItemType Directory -Path $artifactsPath -Force | Out-Null

    # Publish service
    Write-Info "Publishing VisionOps.Service..."
    dotnet publish .\src\VisionOps.Service\VisionOps.Service.csproj -c $Configuration -r win-x64 --self-contained -o "$artifactsPath\Service"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Service publish failed"
        exit 1
    }

    # Publish UI
    Write-Info "Publishing VisionOps.UI..."
    dotnet publish .\src\VisionOps.UI\VisionOps.UI.csproj -c $Configuration -r win-x64 --self-contained -o "$artifactsPath\UI"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "UI publish failed"
        exit 1
    }

    # Copy models (when available)
    if (Test-Path ".\models") {
        Write-Info "Copying AI models..."
        Copy-Item -Path ".\models" -Destination "$artifactsPath\" -Recurse
    }

    # Copy configuration
    Write-Info "Copying configuration files..."
    Copy-Item -Path ".\src\VisionOps.Service\appsettings.json" -Destination "$artifactsPath\Service\"
    Copy-Item -Path ".\src\VisionOps.Service\appsettings.Production.json" -Destination "$artifactsPath\Service\" -ErrorAction SilentlyContinue

    Write-Success "✓ Deployment package created in $artifactsPath"
}

# Summary
Write-Info "`n==============================================="
Write-Success "Build completed successfully!"

if (-not $Test -and -not $Pack) {
    Write-Info "`nNext steps:"
    Write-Info "  - Run tests: .\build.ps1 -Test"
    Write-Info "  - Create package: .\build.ps1 -Pack"
    Write-Info "  - Install service: Run installer from tools\VisionOps.Installer"
}

# Phase 0 reminder
Write-Warning "`n⚠️  IMPORTANT: Phase 0 Production Hardening Requirements"
Write-Warning "  Before deploying to production, verify:"
Write-Warning "  - Memory stability over 24 hours"
Write-Warning "  - Thermal management under load"
Write-Warning "  - Watchdog recovery functionality"
Write-Warning "  - All tests passing"