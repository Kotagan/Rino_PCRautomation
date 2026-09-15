@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo   Rino_PCRautomation Inno Setup Installer Build Script
echo ========================================================

echo [INFO] Changing directory to project root...
cd /d "%~dp0.."

rem 1. Read version from Rino_WFP.csproj
for /f "tokens=3 delims=<>" %%a in ('findstr /i "<InformationalVersion>" Rino_WFP.csproj') do (
    set "VERSION=%%a"
)
if "%VERSION%"=="" (
    for /f "tokens=3 delims=<>" %%a in ('findstr /i "<Version>" Rino_WFP.csproj') do (
        set "VERSION=%%a"
    )
)
if "%VERSION%"=="" set "VERSION=V1.9.0"
echo [INFO] Detected version: %VERSION%

rem 2. Build, Sync version & Package application using package.ps1 with -CreateInstaller
echo [INFO] Packaging application and compiling Inno Setup installer...
powershell -ExecutionPolicy Bypass -File "tools\package.ps1" -Configuration Release -CreateInstaller

if %errorlevel% neq 0 (
    echo [ERROR] Build and packaging failed.
    pause
    exit /b 1
)

if not exist "Rino_PCRautomation_%VERSION%.exe" (
    echo [WARNING] Expected installer at Rino_PCRautomation_%VERSION%.exe was not found.
) else (
    echo.
    echo ========================================================
    echo   [SUCCESS] Custom Inno Setup installer built successfully!
    echo   Output: Rino_PCRautomation_%VERSION%.exe
    echo ========================================================
)

pause
