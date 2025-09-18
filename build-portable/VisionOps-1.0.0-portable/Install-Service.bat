@echo off
echo Installing VisionOps Service...
echo This requires Administrator privileges
cd /d "%~dp0"
sc create VisionOps binPath="%CD%\Service\VisionOps.Service.exe" start=auto
sc description VisionOps "Edge video analytics platform"
echo Service installed successfully!
pause
