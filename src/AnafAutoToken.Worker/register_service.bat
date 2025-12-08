REM register_service.bat
REM This script registers the AnafAutoToken.Worker.exe as a Windows service.
REM Run this script from the same directory as the executable.

@echo off
chcp 65001 >nul
set SERVICE_NAME=AnafAutoTokenWorker
set EXE_PATH=%~dp0AnafAutoToken.Worker.exe

echo Registering service: %SERVICE_NAME%
echo Executable path: %EXE_PATH%

sc create "%SERVICE_NAME%" binPath= "%EXE_PATH%" start= auto DisplayName= "%SERVICE_NAME%"
sc description "%SERVICE_NAME%" "Serwis do automatycznego odświeżania tokenu dla API ANAF"

if errorlevel 1 (
    echo FAILED to register service!
) else (
    echo Service registered successfully.
    sc start "%SERVICE_NAME%"
)

pause
