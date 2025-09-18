#!/bin/bash

# VisionOps Build Script (Linux/macOS)
# Bash script for building and testing the VisionOps solution

# Default values
CONFIGURATION="Release"
PLATFORM="x64"
TEST=false
PACK=false
CLEAN=false
RESTORE=false
NO_BUILD=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --configuration|-c)
            CONFIGURATION="$2"
            shift 2
            ;;
        --platform|-p)
            PLATFORM="$2"
            shift 2
            ;;
        --test|-t)
            TEST=true
            shift
            ;;
        --pack)
            PACK=true
            shift
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --restore)
            RESTORE=true
            shift
            ;;
        --no-build)
            NO_BUILD=true
            shift
            ;;
        --help|-h)
            echo "Usage: ./build.sh [OPTIONS]"
            echo "Options:"
            echo "  -c, --configuration  Build configuration (Debug/Release) [default: Release]"
            echo "  -p, --platform       Target platform [default: x64]"
            echo "  -t, --test          Run tests after build"
            echo "      --pack          Create deployment package"
            echo "      --clean         Clean before build"
            echo "      --restore       Restore packages"
            echo "      --no-build      Skip build step"
            echo "  -h, --help         Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Output functions
success() { echo -e "${GREEN}✓ $1${NC}"; }
info() { echo -e "${CYAN}$1${NC}"; }
warning() { echo -e "${YELLOW}⚠️  $1${NC}"; }
error() { echo -e "${RED}✗ $1${NC}"; }

# Banner
info "
██╗   ██╗██╗███████╗██╗ ██████╗ ███╗   ██╗ ██████╗ ██████╗ ███████╗
██║   ██║██║██╔════╝██║██╔═══██╗████╗  ██║██╔═══██╗██╔══██╗██╔════╝
██║   ██║██║███████╗██║██║   ██║██╔██╗ ██║██║   ██║██████╔╝███████╗
╚██╗ ██╔╝██║╚════██║██║██║   ██║██║╚██╗██║██║   ██║██╔═══╝ ╚════██║
 ╚████╔╝ ██║███████║██║╚██████╔╝██║ ╚████║╚██████╔╝██║     ███████║
  ╚═══╝  ╚═╝╚══════╝╚═╝ ╚═════╝ ╚═╝  ╚═══╝ ╚═════╝ ╚═╝     ╚══════╝

Edge Video Analytics Platform - Build System
"

info "Configuration: $CONFIGURATION | Platform: $PLATFORM"
info "===============================================\n"

# Check .NET SDK
info "Checking .NET SDK..."
if ! command -v dotnet &> /dev/null; then
    error ".NET SDK not found. Please install .NET 8 SDK from https://dotnet.microsoft.com"
    exit 1
fi
DOTNET_VERSION=$(dotnet --version)
success ".NET SDK $DOTNET_VERSION found"

# Clean if requested
if [ "$CLEAN" = true ]; then
    info "\nCleaning solution..."
    dotnet clean VisionOps.sln -c "$CONFIGURATION"
    if [ $? -ne 0 ]; then
        error "Clean failed"
        exit 1
    fi
    rm -rf ./artifacts
    success "Solution cleaned"
fi

# Restore packages
if [ "$RESTORE" = true ] || [ "$NO_BUILD" = false ]; then
    info "\nRestoring NuGet packages..."
    dotnet restore VisionOps.sln
    if [ $? -ne 0 ]; then
        error "Package restore failed"
        exit 1
    fi
    success "Packages restored"
fi

# Build solution
if [ "$NO_BUILD" = false ]; then
    info "\nBuilding solution..."
    dotnet build VisionOps.sln -c "$CONFIGURATION" --no-restore
    if [ $? -ne 0 ]; then
        error "Build failed"
        exit 1
    fi
    success "Build successful"
fi

# Run tests
if [ "$TEST" = true ]; then
    info "\nRunning tests..."
    dotnet test VisionOps.sln -c "$CONFIGURATION" --no-build --logger "console;verbosity=normal" --collect:"XPlat Code Coverage"
    if [ $? -ne 0 ]; then
        warning "Some tests failed"
    else
        success "All tests passed"
    fi
fi

# Package for deployment
if [ "$PACK" = true ]; then
    info "\nCreating deployment package..."

    # Create artifacts directory
    ARTIFACTS_PATH="./artifacts"
    mkdir -p "$ARTIFACTS_PATH"

    # Note: This is primarily for Windows deployment
    info "Note: VisionOps is designed for Windows deployment"
    info "Creating cross-platform package for testing purposes"

    # Publish service
    info "Publishing VisionOps.Service..."
    dotnet publish ./src/VisionOps.Service/VisionOps.Service.csproj \
        -c "$CONFIGURATION" \
        -r win-x64 \
        --self-contained \
        -o "$ARTIFACTS_PATH/Service"

    if [ $? -ne 0 ]; then
        error "Service publish failed"
        exit 1
    fi

    # Publish UI
    info "Publishing VisionOps.UI..."
    dotnet publish ./src/VisionOps.UI/VisionOps.UI.csproj \
        -c "$CONFIGURATION" \
        -r win-x64 \
        --self-contained \
        -o "$ARTIFACTS_PATH/UI"

    if [ $? -ne 0 ]; then
        error "UI publish failed"
        exit 1
    fi

    # Copy models if available
    if [ -d "./models" ]; then
        info "Copying AI models..."
        cp -r ./models "$ARTIFACTS_PATH/"
    fi

    # Copy configuration
    info "Copying configuration files..."
    cp ./src/VisionOps.Service/appsettings.json "$ARTIFACTS_PATH/Service/"
    [ -f ./src/VisionOps.Service/appsettings.Production.json ] && \
        cp ./src/VisionOps.Service/appsettings.Production.json "$ARTIFACTS_PATH/Service/"

    success "Deployment package created in $ARTIFACTS_PATH"
fi

# Summary
info "\n==============================================="
success "Build completed successfully!"

if [ "$TEST" = false ] && [ "$PACK" = false ]; then
    info "\nNext steps:"
    info "  - Run tests: ./build.sh --test"
    info "  - Create package: ./build.sh --pack"
    info "  - Deploy to Windows for production use"
fi

# Phase 0 reminder
warning "\n⚠️  IMPORTANT: Phase 0 Production Hardening Requirements"
warning "  Before deploying to production, verify:"
warning "  - Memory stability over 24 hours"
warning "  - Thermal management under load"
warning "  - Watchdog recovery functionality"
warning "  - All tests passing"
warning "  - Windows 10/11 deployment target"