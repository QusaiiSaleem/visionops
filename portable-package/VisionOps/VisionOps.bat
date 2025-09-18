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
