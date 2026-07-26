[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
	[string]$Version,

	[string]$OutputRoot,

	[string]$ReleaseNotes
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (-not $OutputRoot) {
	$OutputRoot = Join-Path $repositoryRoot "artifacts\releases\$Version"
}

$releaseRoot = [IO.Path]::GetFullPath($OutputRoot)
$publishDirectory = Join-Path $releaseRoot 'publish'
$releaseDirectory = Join-Path $releaseRoot 'release'
$toolDirectory = Join-Path $repositoryRoot 'artifacts\tools\vpk'
$vpk = Join-Path $toolDirectory 'vpk.exe'
$project = Join-Path $repositoryRoot 'src\Files.App\Files.App.csproj'
$icon = Join-Path $repositoryRoot 'src\Files.App\Assets\AppTiles\Dev\Logo.ico'

if (Test-Path -LiteralPath $releaseRoot) {
	$existingItem = Get-ChildItem -LiteralPath $releaseRoot -Force | Select-Object -First 1
	if ($existingItem) {
		throw "Release output is not empty: $releaseRoot"
	}
}

if ($ReleaseNotes) {
	$ReleaseNotes = (Resolve-Path -LiteralPath $ReleaseNotes).Path
}

$existingTag = & git -C $repositoryRoot tag --list "v$Version"
if ($LASTEXITCODE -ne 0) {
	throw 'Unable to inspect Git tags.'
}
if ($existingTag) {
	throw "Git tag v$Version already exists. Build a new version instead of replacing a published release."
}

$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
	throw 'Visual Studio Installer vswhere.exe was not found.'
}

$visualStudioPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $visualStudioPath) {
	throw 'Visual Studio with MSBuild was not found.'
}

$devShellModule = Join-Path $visualStudioPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
Import-Module $devShellModule
Enter-VsDevShell -VsInstallPath $visualStudioPath -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
	msbuild -restore $project `
		-p:Configuration=Release `
		-p:Platform=x64 `
		-p:RuntimeIdentifier=win-x64 `
		-v:quiet `
		-clp:ErrorsOnly
	if ($LASTEXITCODE -ne 0) {
		throw 'VxFiles restore failed.'
	}

	msbuild $project `
		-t:Publish `
		-p:Configuration=Release `
		-p:Platform=x64 `
		-p:RuntimeIdentifier=win-x64 `
		-p:Version=$Version `
		-p:PublishDir="$publishDirectory\" `
		-v:quiet `
		-clp:ErrorsOnly
	if ($LASTEXITCODE -ne 0) {
		throw 'VxFiles publish failed.'
	}
}
finally {
	Pop-Location
}

if (-not (Test-Path -LiteralPath $vpk)) {
	New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
	dotnet tool install vpk --tool-path $toolDirectory --version 1.2.0
	if ($LASTEXITCODE -ne 0) {
		throw 'Velopack CLI installation failed.'
	}
}

$packArguments = @(
	'pack',
	'--packId', 'VxFilesApp',
	'--packVersion', $Version,
	'--packDir', $publishDirectory,
	'--mainExe', 'VxFiles.exe',
	'--outputDir', $releaseDirectory,
	'--packTitle', 'VxFiles',
	'--packAuthors', 'VxFiles',
	'--icon', $icon,
	'--shortcuts', 'StartMenuRoot',
	'--noPortable', 'true'
)
if ($ReleaseNotes) {
	$packArguments += @('--releaseNotes', $ReleaseNotes)
}

& $vpk @packArguments
if ($LASTEXITCODE -ne 0) {
	throw 'Velopack release packaging failed.'
}

$portableAssets = Get-ChildItem -LiteralPath $releaseDirectory -File |
	Where-Object Name -Match 'Portable'
if ($portableAssets) {
	throw 'Velopack produced a portable asset even though portable distribution is disabled.'
}

$releaseAssets = Get-ChildItem -LiteralPath $releaseDirectory -File |
	Sort-Object Name
if (-not ($releaseAssets.Name -contains 'VxFilesApp-win-Setup.exe')) {
	throw 'Velopack did not produce VxFilesApp-win-Setup.exe.'
}
if (-not ($releaseAssets.Name -contains 'releases.win.json')) {
	throw 'Velopack did not produce releases.win.json.'
}

$assetManifestPath = Join-Path $releaseDirectory 'assets.win.json'
$assetManifest = Get-Content -LiteralPath $assetManifestPath -Raw | ConvertFrom-Json
$missingAssets = @($assetManifest | Where-Object {
	-not (Test-Path -LiteralPath (Join-Path $releaseDirectory $_.RelativeFileName))
})
if ($missingAssets.Count -gt 0) {
	throw "Velopack metadata references missing release assets: $($missingAssets.RelativeFileName -join ', ')"
}

Write-Host ''
Write-Host "VxFiles $Version release is ready for local testing."
Write-Host "Installer: $(Join-Path $releaseDirectory 'VxFilesApp-win-Setup.exe')"
Write-Host 'Upload every file in this release directory to the matching GitHub release:'
$releaseAssets | ForEach-Object { Write-Host "  $($_.Name)" }
