@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo   Rino Inno Setup Installer Build Script
echo ========================================================

echo [INFO] Changing directory to project root...
cd /d "%~dp0.."

rem 1. Read version from MFAWPF.csproj
for /f "tokens=3 delims=<>" %%a in ('findstr /i "<Version>" MFAWPF.csproj') do (
    set "VERSION=%%a"
)
if "%VERSION%"=="" set "VERSION=0.1.0"
echo [INFO] Detected version: %VERSION%

rem 2. Build, Sync version & Package application using package.ps1 with -CreateInstaller
echo [INFO] Packaging application and compiling Inno Setup installer...
powershell -ExecutionPolicy Bypass -File "tools\package.ps1" -Configuration Release -CreateInstaller

if %errorlevel% neq 0 (
    echo [ERROR] Build and packaging failed.
    pause
    exit /b 1
)

if not exist "Rino_%VERSION%_Setup.exe" (
    echo [WARNING] Expected installer at Rino_%VERSION%_Setup.exe was not found.
) else (
    echo.
    echo ========================================================
    echo   [SUCCESS] Custom Inno Setup installer built successfully!
    echo   Output: Rino_%VERSION%_Setup.exe
    echo ========================================================
)

pause
