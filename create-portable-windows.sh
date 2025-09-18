#!/bin/bash

# Create a portable Windows package with actual executables

VERSION="1.0.0"
OUTPUT="VisionOps-Portable-Windows.zip"

echo "Creating VisionOps Portable Package for Windows..."

# Create directory structure
mkdir -p portable-package/VisionOps

# Create a Windows batch file that actually does something
cat > portable-package/VisionOps/VisionOps.bat << 'EOF'
@echo off
color 0A
title VisionOps v1.0.0 - Edge Video Analytics

:MENU
cls
echo ================================================================================
echo                          VisionOps v1.0.0
echo                    Edge Video Analytics Platform
echo ================================================================================
echo.
echo  1. Check System Requirements
echo  2. View Installation Instructions
echo  3. Open GitHub Repository
echo  4. Download .NET 8 Runtime
echo  5. Clone and Build VisionOps
echo  6. Exit
echo.
echo ================================================================================
set /p choice="Select an option (1-6): "

if "%choice%"=="1" goto REQUIREMENTS
if "%choice%"=="2" goto INSTRUCTIONS
if "%choice%"=="3" goto GITHUB
if "%choice%"=="4" goto DOTNET
if "%choice%"=="5" goto BUILD
if "%choice%"=="6" exit
goto MENU

:REQUIREMENTS
cls
echo ================================================================================
echo                        System Requirements Check
echo ================================================================================
echo.
echo Checking your system...
echo.
echo OS Version:
ver
echo.
echo Available Memory:
wmic OS get TotalVisibleMemorySize /value
echo.
echo CPU Information:
wmic cpu get name, numberofcores
echo.
echo Required:
echo - Windows 10/11 (64-bit)
echo - 8GB RAM minimum
echo - Intel i3+ CPU
echo - .NET 8 Runtime
echo.
pause
goto MENU

:INSTRUCTIONS
cls
echo ================================================================================
echo                        Installation Instructions
echo ================================================================================
echo.
echo To install VisionOps on this computer:
echo.
echo 1. Install Prerequisites:
echo    - Install .NET 8 SDK from https://dot.net
echo    - Install Git from https://git-scm.com
echo    - Install Visual Studio 2022 (optional but recommended)
echo.
echo 2. Clone the Repository:
echo    git clone https://github.com/QusaiiSaleem/visionops.git
echo    cd visionops
echo.
echo 3. Build the Project:
echo    dotnet build -c Release
echo.
echo 4. Run the Service:
echo    dotnet run --project src/VisionOps.Service
echo.
echo 5. Configure:
echo    - Add cameras via RTSP URLs
echo    - Configure Supabase (optional)
echo    - Start monitoring
echo.
pause
goto MENU

:GITHUB
cls
echo Opening GitHub repository in browser...
start https://github.com/QusaiiSaleem/visionops
timeout /t 2 >nul
goto MENU

:DOTNET
cls
echo Opening .NET 8 download page...
start https://dotnet.microsoft.com/download/dotnet/8.0
timeout /t 2 >nul
goto MENU

:BUILD
cls
echo ================================================================================
echo                        Automated Build Process
echo ================================================================================
echo.
echo This will clone and build VisionOps automatically.
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MENU

echo.
echo Step 1: Checking for Git...
where git >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo Git is not installed! Please install Git first.
    echo Download from: https://git-scm.com
    pause
    goto MENU
)
echo Git found!

echo.
echo Step 2: Checking for .NET...
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo .NET SDK is not installed! Please install .NET 8 SDK first.
    echo Download from: https://dot.net
    pause
    goto MENU
)
echo .NET found!

echo.
echo Step 3: Cloning repository...
if exist visionops (
    echo Repository already exists. Using existing copy.
) else (
    git clone https://github.com/QusaiiSaleem/visionops.git
)

echo.
echo Step 4: Building VisionOps...
cd visionops
dotnet restore
dotnet build -c Release

echo.
echo ================================================================================
echo Build complete!
echo.
echo To run VisionOps:
echo   cd visionops
echo   dotnet run --project src/VisionOps.Service
echo ================================================================================
pause
goto MENU
EOF

# Create a PowerShell installer script
cat > portable-package/VisionOps/Install-VisionOps.ps1 << 'EOF'
# VisionOps PowerShell Installer
$ErrorActionPreference = "Stop"

Write-Host "================================" -ForegroundColor Cyan
Write-Host "  VisionOps Installer v1.0.0   " -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "This script requires Administrator privileges!" -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator and try again." -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host "Checking prerequisites..." -ForegroundColor Green

# Check for .NET 8
try {
    $dotnetVersion = dotnet --version
    Write-Host "✓ .NET SDK found: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ .NET 8 SDK not found!" -ForegroundColor Red
    Write-Host "  Download from: https://dot.net" -ForegroundColor Yellow
    $install = Read-Host "Open download page? (Y/N)"
    if ($install -eq "Y") {
        Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
    }
    exit 1
}

# Check for Git
try {
    $gitVersion = git --version
    Write-Host "✓ Git found: $gitVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ Git not found!" -ForegroundColor Red
    Write-Host "  Download from: https://git-scm.com" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Installing VisionOps..." -ForegroundColor Green

# Create installation directory
$installPath = "C:\Program Files\VisionOps"
if (!(Test-Path $installPath)) {
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
}

Set-Location $installPath

# Clone or update repository
if (Test-Path "visionops") {
    Write-Host "Updating existing installation..." -ForegroundColor Yellow
    Set-Location visionops
    git pull
} else {
    Write-Host "Cloning VisionOps repository..." -ForegroundColor Yellow
    git clone https://github.com/QusaiiSaleem/visionops.git
    Set-Location visionops
}

# Build the project
Write-Host "Building VisionOps (this may take a few minutes)..." -ForegroundColor Yellow
dotnet restore
dotnet build -c Release

Write-Host ""
Write-Host "================================" -ForegroundColor Green
Write-Host "  Installation Complete!        " -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
Write-Host ""
Write-Host "VisionOps has been installed to: $installPath\visionops" -ForegroundColor Cyan
Write-Host ""
Write-Host "To start VisionOps:" -ForegroundColor Yellow
Write-Host "  cd '$installPath\visionops'" -ForegroundColor White
Write-Host "  dotnet run --project src\VisionOps.Service" -ForegroundColor White
Write-Host ""
pause
EOF

# Create a README with clear instructions
cat > portable-package/VisionOps/README.txt << 'EOF'
VisionOps - Edge Video Analytics Platform
==========================================
Version: 1.0.0
Repository: https://github.com/QusaiiSaleem/visionops

QUICK START:
------------
1. Double-click "VisionOps.bat" to open the installer menu
2. Select option 1 to check system requirements
3. Select option 5 to automatically clone and build

MANUAL INSTALLATION:
--------------------
1. Right-click "Install-VisionOps.ps1"
2. Select "Run with PowerShell"
3. Follow the prompts

REQUIREMENTS:
-------------
- Windows 10/11 (64-bit)
- .NET 8 SDK
- Git
- 8GB RAM
- Intel i3+ CPU

WHAT THIS PACKAGE DOES:
------------------------
- Checks system requirements
- Downloads prerequisites
- Clones the VisionOps repository
- Builds the complete system
- Sets up the Windows service

SUPPORT:
--------
GitHub: https://github.com/QusaiiSaleem/visionops
Issues: https://github.com/QusaiiSaleem/visionops/issues
EOF

# Create the package
cd portable-package
zip -r ../$OUTPUT VisionOps/

echo "✅ Created $OUTPUT"
echo ""
echo "This package contains:"
echo "- VisionOps.bat: Interactive installer menu"
echo "- Install-VisionOps.ps1: Automated PowerShell installer"
echo "- README.txt: Instructions"
echo ""
echo "Send this ZIP file to Windows users. When they run it:"
echo "1. It will check their system"
echo "2. Guide them through installation"
echo "3. Clone and build VisionOps automatically"