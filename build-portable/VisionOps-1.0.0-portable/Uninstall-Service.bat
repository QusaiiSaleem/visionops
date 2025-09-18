@echo off
echo Uninstalling VisionOps Service...
echo This requires Administrator privileges
sc stop VisionOps
sc delete VisionOps
echo Service uninstalled successfully!
pause
