REM register_service.bat
REM This script registers the AnafAutoToken.Worker.exe as a Windows service.
REM Run this script from the same directory as the executable.

@echo off
set SERVICE_NAME=AnafAutoTokenWorker
set EXE_PATH=%~dp0AnafAutoToken.Worker.exe

echo Registering service: %SERVICE_NAME%
sc create "%SERVICE_NAME%" binpath= "%EXE_PATH%" start= auto
if %errorlevel% equ 0 (
    echo Service registered successfully.
    sc start "%SERVICE_NAME%"
) else (
    echo Failed to register service.
)
pause