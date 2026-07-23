# Copyright (c) VxFiles contributors
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string]$TagName,

    [Parameter(Mandatory = $false)]
    [string]$Title,

    [Parameter(Mandatory = $false)]
    [string]$Notes,

    [Parameter(Mandatory = $false)]
    [string]$NotesFile,

    [Parameter(Mandatory = $false)]
    [string]$TargetCommit,

    [switch]$Draft,

    [switch]$Prerelease,

    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$archivePath = Join-Path $artifactsRoot 'VxFiles-portable-win-x64.zip'
$checksumPath = "$archivePath.sha256"
$installerPath = Join-Path $artifactsRoot 'VxFiles-Setup-win-x64.exe'
$installerChecksumPath = "$installerPath.sha256"

# Ensure gh CLI is installed
$gh = Get-Command gh.exe -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw 'GitHub CLI (gh.exe) was not found on PATH. Please install it from https://cli.github.com/ or via winget install GitHub.cli'
}

# Prompt for TagName if not provided interactively
if ([string]::IsNullOrWhiteSpace($TagName)) {
    $TagName = Read-Host -Prompt 'Enter release tag name (e.g. v1.0.0)'
    if ([string]::IsNullOrWhiteSpace($TagName)) {
        throw 'Tag name is required to create a release.'
    }
}

# Default Title to TagName if empty
if ([string]::IsNullOrWhiteSpace($Title)) {
    $Title = "VxFiles $TagName"
}

# Build portable package if requested or if artifacts are missing
if ($Build -or (-not (Test-Path -LiteralPath $archivePath)) -or (-not (Test-Path -LiteralPath $checksumPath))) {
    Write-Host 'Building portable release package...' -ForegroundColor Cyan
    $buildScript = Join-Path $PSScriptRoot 'Build-Portable.ps1'
    & $buildScript -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Portable build failed with exit code $LASTEXITCODE."
    }
}

if ($Build -or (-not (Test-Path -LiteralPath $installerPath)) -or (-not (Test-Path -LiteralPath $installerChecksumPath))) {
    Write-Host 'Building installer package...' -ForegroundColor Cyan
    $buildInstallerScript = Join-Path $PSScriptRoot 'Build-Installer.ps1'
    if (Test-Path -LiteralPath $buildInstallerScript) {
        try {
            & $buildInstallerScript
        } catch {
            Write-Warning "Installer build failed. Will continue without installer. Details: $_"
        }
    }
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    throw "Release archive not found at $archivePath. Run Build-Portable.ps1 first or use -Build."
}

if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Release checksum file not found at $checksumPath. Run Build-Portable.ps1 first or use -Build."
}

# Assemble gh release create arguments
$ghArgs = @(
    'release', 'create', $TagName,
    $archivePath,
    $checksumPath,
    '--title', $Title
)

if (Test-Path -LiteralPath $installerPath) {
    $ghArgs += $installerPath
}
if (Test-Path -LiteralPath $installerChecksumPath) {
    $ghArgs += $installerChecksumPath
}

if (-not [string]::IsNullOrWhiteSpace($Notes)) {
    $ghArgs += @('--notes', $Notes)
} elseif (-not [string]::IsNullOrWhiteSpace($NotesFile)) {
    if (-not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) {
        throw "Notes file not found at $NotesFile"
    }
    $ghArgs += @('--notes-file', $NotesFile)
} else {
    $ghArgs += '--generate-notes'
}

if (-not [string]::IsNullOrWhiteSpace($TargetCommit)) {
    $ghArgs += @('--target', $TargetCommit)
}

if ($Draft) {
    $ghArgs += '--draft'
}

if ($Prerelease) {
    $ghArgs += '--prerelease'
}

Write-Host "Creating GitHub release '$TagName'..." -ForegroundColor Green
Write-Host "gh $($ghArgs -join ' ')" -ForegroundColor Gray

& $gh.Source @ghArgs
if ($LASTEXITCODE -ne 0) {
    throw "gh release create failed with exit code $LASTEXITCODE."
}

Write-Host "Successfully published release $TagName!" -ForegroundColor Green
