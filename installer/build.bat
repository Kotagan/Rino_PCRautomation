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

rem 2. Sync version to installer/Rino.iss
echo [INFO] Syncing version to installer/Rino.iss...
powershell -NoProfile -Command " = 'installer/Rino.iss';  = [System.IO.File]::ReadAllText(, [System.Text.Encoding]::UTF8);  = [System.Text.RegularExpressions.Regex]::Replace(, 'AppVersion=[0-9.]+', 'AppVersion=%VERSION%');  = [System.Text.RegularExpressions.Regex]::Replace(, 'OutputBaseFilename=Rino_[0-9.]+_Setup(_Local)?', 'OutputBaseFilename=Rino_%VERSION%_Setup'); [System.IO.File]::WriteAllText(, , [System.Text.Encoding]::UTF8)"

rem 3. Build & Package published binaries using package.ps1
echo [INFO] Publishing and packaging application...
powershell -ExecutionPolicy Bypass -File "tools\package.ps1" -Configuration Release

if %errorlevel% neq 0 (
    echo [ERROR] Application packaging failed.
    pause
    exit /b 1
)

if not exist "distribution\output\Rino\Rino.exe" (
    echo [ERROR] Could not find published binary at distribution\output\Rino\Rino.exe
    pause
    exit /b 1
)

rem 4. Find Inno Setup Compiler (ISCC.exe)
set "ISCC_PATH="
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" goto SET_ISCC_USER
if exist "%USERPROFILE%\AppData\Local\Programs\Inno Setup 6\ISCC.exe" goto SET_ISCC_USER2
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" goto SET_ISCC_6_X86
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" goto SET_ISCC_6_X64
if exist "C:\Program Files (x86)\Inno Setup 5\ISCC.exe" goto SET_ISCC_5_X86
if exist "C:\Program Files\Inno Setup 5\ISCC.exe" goto SET_ISCC_5_X64
goto CHECK_PATH

:SET_ISCC_USER
set "ISCC_PATH=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
goto START_BUILD

:SET_ISCC_USER2
set "ISCC_PATH=%USERPROFILE%\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
goto START_BUILD

:SET_ISCC_6_X86
set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
goto START_BUILD

:SET_ISCC_6_X64
set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
goto START_BUILD

:SET_ISCC_5_X86
set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
goto START_BUILD

:SET_ISCC_5_X64
set "ISCC_PATH=C:\Program Files\Inno Setup 5\ISCC.exe"
goto START_BUILD

:CHECK_PATH
where iscc >nul 2>nul
if %errorlevel% equ 0 goto FOUND_ISCC
goto START_BUILD

:FOUND_ISCC
for /f "tokens=*" %%i in ('where iscc') do (
    set "ISCC_PATH=%%i"
)
goto START_BUILD

:START_BUILD
if "%ISCC_PATH%"=="" goto ERROR_NO_ISCC

rem Kill any running setup process to avoid Error 32 file lock
taskkill /f /im Rino*.exe 2>nul

echo [INFO] Found Inno Setup Compiler: "%ISCC_PATH%"
echo [INFO] Compiling custom Inno Setup installer...
echo.

rem 5. Execute Compile
"%ISCC_PATH%" "installer\Rino.iss"

if %errorlevel% equ 0 (
    echo.
    echo ========================================================
    echo   [SUCCESS] Custom Inno Setup installer built successfully!
    echo   Output: distribution\output\Rino_%VERSION%_Setup.exe
    echo ========================================================
) else (
    echo.
    echo [ERROR] Inno Setup compilation failed.
)
goto END

:ERROR_NO_ISCC
echo [ERROR] Inno Setup Compiler ISCC.exe was not found.
echo Please install Inno Setup 6 (https://jrsoftware.org/isdl.php) and try again.
pause
exit /b 1

:END
pause
