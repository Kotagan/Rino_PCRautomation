<#
.SYNOPSIS
    Rino Automated Packaging Script
.DESCRIPTION
    Publishes Rino as a clean, single-file distribution without root DLL clutter or unwanted language folders.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Release.
.PARAMETER OutputDir
    Custom output directory. Default is "distribution/output/Rino".
.PARAMETER CreateZip
    Create a .zip archive of the packaged output.
.PARAMETER CreateInstaller
    Create an Inno Setup installer executable.
#>

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [switch]$CreateZip,
    [switch]$CreateInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "distribution\output\Rino"
}

$trimChars = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir).TrimEnd($trimChars)
$protectedPaths = @(
    [System.IO.Path]::GetPathRoot($OutputDir).TrimEnd($trimChars),
    [System.IO.Path]::GetFullPath($repoRoot).TrimEnd($trimChars),
    [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile).TrimEnd($trimChars)
)
if ($protectedPaths -contains $OutputDir) {
    throw "Refusing to use protected output directory: $OutputDir"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Rino Packaging Tool" -ForegroundColor Cyan
Write-Host " Configuration : $Configuration" -ForegroundColor Cyan
Write-Host " Output Dir    : $OutputDir" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

if (Test-Path -LiteralPath $OutputDir) {
    Write-Host "Cleaning target output directory..." -ForegroundColor Gray
    try {
        Remove-Item -LiteralPath $OutputDir -Recurse -Force -ErrorAction Stop
    } catch {
        Get-ChildItem -LiteralPath $OutputDir -Force | Remove-Item -Recurse -Force
    }
}

$projectPath = Join-Path $repoRoot "Rino_WFP.csproj"

Write-Host "Running dotnet publish..." -ForegroundColor Yellow
dotnet publish $projectPath -c $Configuration -r win-x64 --no-self-contained -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$expectedExecutable = Join-Path $OutputDir "Rino_PCRautomation.exe"
if (!(Test-Path -LiteralPath $expectedExecutable)) {
    $expectedExecutable = Join-Path $OutputDir "Rino.exe"
}
$legacyExecutable = Join-Path $OutputDir "MFAWPF.exe"
if (!(Test-Path -LiteralPath $expectedExecutable)) {
    throw "Published package does not contain expected executable (Rino_PCRautomation.exe / Rino.exe)."
}
if (Test-Path -LiteralPath $legacyExecutable) {
    throw "Published package unexpectedly contains MFAWPF.exe."
}

# Remove unwanted leftover files if any (e.g. .pdb)
Get-ChildItem -Path $OutputDir -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# Ensure essential runtime asset directories are present (excluding user-specific config/debug data)
$essentialDirs = @("Default", "Resource", "DLL", "plugins", "runtimes", "MaaAgentBinary")
$fallbackDirs = @(
    (Join-Path $repoRoot "distribution\output\Rino-Release")
)

foreach ($dir in $essentialDirs) {
    $src = Join-Path $repoRoot $dir
    if (!(Test-Path $src)) {
        foreach ($fallback in $fallbackDirs) {
            if (Test-Path $fallback) {
                $candidate = Join-Path $fallback $dir
                if (Test-Path $candidate) {
                    $src = $candidate
                    break
                }
            }
        }
    }
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $OutputDir -Recurse -Force
    }
}

# Create and purge pristine runtime directories
$distDebugDir = Join-Path $OutputDir "debug"
if (Test-Path $distDebugDir) {
    Remove-Item -Path (Join-Path $distDebugDir "*") -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $distDebugDir -Force | Out-Null
}

$distLogsDir = Join-Path $OutputDir "logs"
if (Test-Path $distLogsDir) {
    Remove-Item -Path (Join-Path $distLogsDir "*") -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $distLogsDir -Force | Out-Null
}

# Purge any user-specific configuration / tasks / scripts in config directory
$distConfigDir = Join-Path $OutputDir "config"
if (Test-Path $distConfigDir) {
    Remove-Item -Path (Join-Path $distConfigDir "*") -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $distConfigDir -Force | Out-Null
}

# Ensure a pristine, empty default config is written (completely clean, zero preloaded tasks)
Set-Content -Path (Join-Path $distConfigDir "config.json") -Value "{}`n" -Encoding utf8
Set-Content -Path (Join-Path $distConfigDir "maa_option.json") -Value "{}`n" -Encoding utf8

$essentialFiles = @("interface.json", "App.config", "logo.ico")
foreach ($file in $essentialFiles) {
    $src = Join-Path $repoRoot $file
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination (Join-Path $OutputDir $file) -Force
    }
}

# Ensure native runtime libraries required by MaaFramework and ONNX/OpenCV are present
$binDirCandidates = @(
    (Join-Path $repoRoot "runtimes\win-x64\native"),
    (Join-Path $repoRoot "..\bin\AnyCPU\$Configuration"),
    (Join-Path $repoRoot "..\bin\x64\$Configuration"),
    (Join-Path $repoRoot "..\bin\AnyCPU\Debug"),
    (Join-Path $env:USERPROFILE ".nuget\packages\maa.framework.runtime.win-x64\4.4.0-beta.2\runtimes\win-x64\native")
)

$nativePatterns = @("Maa*.dll", "*_maa.dll", "DirectML.dll", "ViGEmClient.dll")
foreach ($pattern in $nativePatterns) {
    foreach ($binDir in $binDirCandidates) {
        if (Test-Path $binDir) {
            $matchingFiles = Get-ChildItem -Path $binDir -Filter $pattern -File -ErrorAction SilentlyContinue
            if ($matchingFiles) {
                foreach ($f in $matchingFiles) {
                    $destPath = Join-Path $OutputDir $f.Name
                    Copy-Item -Path $f.FullName -Destination $destPath -Force
                }
                break
            }
        }
    }
}

# Strict sanitization: remove all runtime logs, crash dumps, debug files, and user artifacts
$sensitiveExtensions = @(".log", ".pdb", ".dmp", ".trace", ".etl", ".tmp", ".bak")
Get-ChildItem -Path $OutputDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in $sensitiveExtensions } |
    Remove-Item -Force

# Remove any stray user capture / cache directories
$sensitiveDirNames = @("captures", "screenshots", "on-error", ".rino-package-data")
Get-ChildItem -Path $OutputDir -Recurse -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in $sensitiveDirNames } |
    Remove-Item -Recurse -Force

Write-Host "`nPackage created successfully in: $OutputDir" -ForegroundColor Green
Write-Host "Root contents:" -ForegroundColor Green
Get-ChildItem -Path $OutputDir | Select-Object Name, Length, Mode | Format-Table -AutoSize

# Extract version from Rino_WFP.csproj
$csprojContent = Get-Content -Path $projectPath -Raw
$infoVersionMatch = [System.Text.RegularExpressions.Regex]::Match($csprojContent, "<InformationalVersion>([^<]+)</InformationalVersion>")
if ($infoVersionMatch.Success -and -not [string]::IsNullOrWhiteSpace($infoVersionMatch.Groups[1].Value)) {
    $version = $infoVersionMatch.Groups[1].Value.Trim()
} else {
    $versionMatch = [System.Text.RegularExpressions.Regex]::Match($csprojContent, "<Version>([^<]+)</Version>")
    if ($versionMatch.Success) {
        $version = $versionMatch.Groups[1].Value.Trim()
    } else {
        $version = "0.00"
    }
}

$cleanVersion = $version.TrimStart('v', 'V')
$verWithV = if ($version.StartsWith('V') -or $version.StartsWith('v')) { $version } else { "V$version" }
$fileVersion = if ($cleanVersion.Split('.').Count -eq 3) { "$cleanVersion.0" } elseif ($cleanVersion.Split('.').Count -eq 2) { "$cleanVersion.0.0" } else { $cleanVersion }

# Sync interface.json version
$interfaceJsonPath = Join-Path $repoRoot "interface.json"
if (Test-Path $interfaceJsonPath) {
    $interfaceContent = [System.IO.File]::ReadAllText($interfaceJsonPath, [System.Text.Encoding]::UTF8)
    $interfaceContent = [System.Text.RegularExpressions.Regex]::Replace($interfaceContent, '("version"\s*:\s*")[^"]+(")', "`${1}$cleanVersion`${2}")
    [System.IO.File]::WriteAllText($interfaceJsonPath, $interfaceContent, [System.Text.Encoding]::UTF8)
    Copy-Item -Path $interfaceJsonPath -Destination (Join-Path $OutputDir "interface.json") -Force
}

if ($CreateZip) {
    $zipPath = Join-Path $repoRoot "Rino_PCRautomation_${verWithV}.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "Creating archive: $zipPath ..." -ForegroundColor Yellow
    Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -Force
    Write-Host "Archive created: $zipPath" -ForegroundColor Green
}

if ($CreateInstaller) {
    $issPath = Join-Path $repoRoot "installer\Rino.iss"
    if (!(Test-Path $issPath)) {
        throw "Inno Setup script not found at: $issPath"
    }

    Write-Host "`nSyncing version ($version / $verWithV / $fileVersion) to $issPath ..." -ForegroundColor Yellow
    $issContent = [System.IO.File]::ReadAllText($issPath, [System.Text.Encoding]::UTF8)
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "AppName=[^\r\n]+", "AppName=Rino_PCRautomation_${verWithV}")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "AppVersion=[0-9.]+", "AppVersion=$cleanVersion")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "VersionInfoVersion=[0-9.]+", "VersionInfoVersion=$fileVersion")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "VersionInfoTextVersion=[^\r\n]+", "VersionInfoTextVersion=$verWithV")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "VersionInfoProductVersion=[^\r\n]+", "VersionInfoProductVersion=$fileVersion")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "OutputBaseFilename=[^\r\n]+", "OutputBaseFilename=Rino_PCRautomation_${verWithV}")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "TitleLabel\.Caption\s*:=\s*'[^']+';", "TitleLabel.Caption := 'Rino_PCRautomation_${verWithV}';")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, "WizardForm\.Caption\s*:=\s*'[^']+';", "WizardForm.Caption := 'Rino_PCRautomation_${verWithV}';")
    [System.IO.File]::WriteAllText($issPath, $issContent, [System.Text.Encoding]::UTF8)

    # Kill any running setup process before compilation
    Get-Process | Where-Object { $_.ProcessName -match "Rino.*Setup|Setup\.tmp" } | Stop-Process -Force -ErrorAction SilentlyContinue

    # Find ISCC compiler
    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
        "C:\Program Files\Inno Setup 5\ISCC.exe"
    )
    $isccPath = $null
    foreach ($cand in $isccCandidates) {
        if (Test-Path $cand) {
            $isccPath = $cand
            break
        }
    }
    if ($null -eq $isccPath) {
        $whereIscc = Get-Command "iscc" -ErrorAction SilentlyContinue
        if ($whereIscc) {
            $isccPath = $whereIscc.Source
        }
    }

    if ($null -eq $isccPath) {
        throw "Inno Setup compiler (ISCC.exe) was not found. Please install Inno Setup 6."
    }

    Write-Host "Compiling Inno Setup installer via $isccPath ..." -ForegroundColor Yellow
    & "$isccPath" "$issPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }

    $setupExe = Join-Path $repoRoot "Rino_PCRautomation_${verWithV}.exe"
    Write-Host "Installer created successfully: $setupExe" -ForegroundColor Green
}
