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
#>

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [switch]$CreateZip
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
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

$projectPath = Join-Path $repoRoot "MFAWPF.csproj"

Write-Host "Running dotnet publish..." -ForegroundColor Yellow
dotnet publish $projectPath -c $Configuration -r win-x64 --no-self-contained -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$expectedExecutable = Join-Path $OutputDir "Rino.exe"
$legacyExecutable = Join-Path $OutputDir "MFAWPF.exe"
if (!(Test-Path -LiteralPath $expectedExecutable)) {
    throw "Published package does not contain Rino.exe."
}
if (Test-Path -LiteralPath $legacyExecutable) {
    throw "Published package unexpectedly contains MFAWPF.exe."
}

# Remove unwanted leftover files if any (e.g. .pdb)
Get-ChildItem -Path $OutputDir -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# Ensure essential runtime asset directories and files are present
$essentialDirs = @("Default", "config", "Resource", "DLL", "plugins", "runtimes", "MaaAgentBinary", "debug")
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

$essentialFiles = @("interface.json", "App.config", "logo.ico")
foreach ($file in $essentialFiles) {
    $src = Join-Path $repoRoot $file
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination (Join-Path $OutputDir $file) -Force
    }
}

# Runtime logs are machine-specific and must not be carried into a distributable package.
Get-ChildItem -Path $OutputDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in @(".log", ".pdb") } |
    Remove-Item -Force

Write-Host "`nPackage created successfully in: $OutputDir" -ForegroundColor Green
Write-Host "Root contents:" -ForegroundColor Green
Get-ChildItem -Path $OutputDir | Select-Object Name, Length, Mode | Format-Table -AutoSize

if ($CreateZip) {
    $zipParent = Split-Path -Parent $OutputDir
    $zipPath = Join-Path $zipParent "Rino-$Configuration.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "Creating archive: $zipPath ..." -ForegroundColor Yellow
    Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -Force
    Write-Host "Archive created: $zipPath" -ForegroundColor Green
}
