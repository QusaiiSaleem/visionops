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
