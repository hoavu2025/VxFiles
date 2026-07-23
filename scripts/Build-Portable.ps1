# Copyright (c) VxFiles contributors
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = Join-Path $artifactsRoot 'publish\win-x64'
$stagingDirectory = Join-Path $artifactsRoot 'staging\VxFiles-portable-win-x64'
$archivePath = Join-Path $artifactsRoot 'VxFiles-portable-win-x64.zip'
$checksumPath = "$archivePath.sha256"
$projectPath = Join-Path $repositoryRoot 'src\Files.App\Files.App.csproj'

function Remove-BuildDirectory([string]$Path) {
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $artifactsPrefix = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the artifacts directory: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

$msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue
if ($null -eq $msbuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'MSBuild was not found and Visual Studio Installer is unavailable.'
    }

    $visualStudioPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
        throw 'A Visual Studio installation with MSBuild was not found.'
    }

    $msbuildPath = Join-Path $visualStudioPath 'MSBuild\Current\Bin\MSBuild.exe'
} else {
    $msbuildPath = $msbuild.Source
}

Remove-BuildDirectory $publishDirectory
Remove-BuildDirectory $stagingDirectory
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

& $msbuildPath $projectPath `
    -restore `
    -t:Publish `
    -p:Configuration=$Configuration `
    -p:Platform=x64 `
    -p:RuntimeIdentifier=win-x64 `
    -p:PublishDir="$publishDirectory\" `
    -v:quiet `
    -clp:ErrorsOnly

if ($LASTEXITCODE -ne 0) {
    throw "Portable publish failed with exit code $LASTEXITCODE."
}

$requiredPublishFiles = @(
    (Join-Path $publishDirectory 'VxFiles.exe'),
    (Join-Path $publishDirectory 'Assets\AppTiles\Dev\Logo.ico'),
    (Join-Path $publishDirectory 'Assets\AppTiles\Dev\SplashScreen.scale-200.png')
)

foreach ($requiredFile in $requiredPublishFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Portable publish did not produce $requiredFile."
    }
}

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stagingDirectory -Recurse -Force
Get-ChildItem -LiteralPath $stagingDirectory -Recurse -Filter '*.pdb' -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE-MIT') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE-MPL') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'NOTICE-VXFILES.md') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\PORTABLE.md') -Destination (Join-Path $stagingDirectory 'PORTABLE.md')

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

$stagingItems = Get-ChildItem -LiteralPath $stagingDirectory
Compress-Archive -Path $stagingItems.FullName -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path $archivePath -Leaf)" -Encoding ascii

Write-Output "Portable archive: $archivePath"
Write-Output "SHA-256: $hash"
