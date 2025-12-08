REM unregister_service.bat
REM This script unregisters the AnafAutoTokenWorker Windows service.
REM Run this script from the same directory as the executable.

@echo off
set SERVICE_NAME=AnafAutoTokenWorker

echo Stopping and deleting service: %SERVICE_NAME%
sc stop "%SERVICE_NAME%"
sc delete "%SERVICE_NAME%"
if %errorlevel% equ 0 (
    echo Service unregistered successfully.
) else (
    echo Failed to unregister service.
)
pause