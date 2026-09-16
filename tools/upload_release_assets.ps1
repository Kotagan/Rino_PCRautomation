$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$proxy = "socks5h://127.0.0.1:10808"

Write-Host "Getting GitHub auth token..." -ForegroundColor Cyan
$token = (gh auth token).Trim()
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "GitHub token is empty. Please run 'gh auth login' first."
}

$releaseTag = "V1.9.0"
$repo = "miaoyu2233/Rino_PCRautomation"

Write-Host "Querying release information for $releaseTag..." -ForegroundColor Cyan
$releaseJson = & curl.exe -s -x $proxy -H "Authorization: Bearer $token" "https://api.github.com/repos/$repo/releases/tags/$releaseTag"
$releaseObj = $releaseJson | ConvertFrom-Json
$releaseId = $releaseObj.id

if (!$releaseId) {
    throw "Could not find release $releaseTag"
}
Write-Host "Found release ID: $releaseId" -ForegroundColor Green

$files = @(
    "Rino_PCRautomation_V1.9.0.exe",
    "Rino_PCRautomation_V1.9.0.zip"
)

foreach ($file in $files) {
    $filePath = Join-Path $repoRoot $file
    if (!(Test-Path $filePath)) {
        throw "File not found: $filePath"
    }
    $fileSizeMB = [math]::Round((Get-Item $filePath).Length / 1MB, 2)

    # Check if an asset with this name already exists and delete it
    $currentReleaseJson = & curl.exe -s -x $proxy -H "Authorization: Bearer $token" "https://api.github.com/repos/$repo/releases/tags/$releaseTag"
    $currentReleaseObj = $currentReleaseJson | ConvertFrom-Json
    if ($currentReleaseObj.assets) {
        foreach ($asset in $currentReleaseObj.assets) {
            if ($asset.name -eq $file) {
                Write-Host "Deleting existing asset $($asset.name) (ID: $($asset.id))..." -ForegroundColor Yellow
                & curl.exe -s -x $proxy -X DELETE -H "Authorization: Bearer $token" "https://api.github.com/repos/$repo/releases/assets/$($asset.id)"
            }
        }
    }

    Write-Host "Uploading $file ($fileSizeMB MB)..." -ForegroundColor Yellow
    $uploadUrl = "https://uploads.github.com/repos/$repo/releases/$releaseId/assets?name=$file"

    $startTime = Get-Date
    & curl.exe -x $proxy --retry 5 --retry-delay 3 -X POST `
        -H "Authorization: Bearer $token" `
        -H "Content-Type: application/octet-stream" `
        --data-binary "@$filePath" `
        $uploadUrl `
        --progress-bar

    if ($LASTEXITCODE -ne 0) {
        throw "curl upload failed for $file with exit code $LASTEXITCODE"
    }

    $elapsed = ((Get-Date) - $startTime).TotalSeconds
    Write-Host "`nUploaded $file successfully in $([math]::Round($elapsed, 1))s!" -ForegroundColor Green
}

Write-Host "`nAll assets uploaded successfully to release $releaseTag!" -ForegroundColor Green
