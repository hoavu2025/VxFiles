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

# The Automation payload must be hash-verified before it is published, because the installed app trusts
# this interpreter to run package code and renews package trust whenever the runner changes.
$pythonMetadata = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'scripts\automation\python-3.14.6-win-x64.json') | ConvertFrom-Json
try {
	$pythonRoot = & (Join-Path $repositoryRoot 'scripts\automation\Acquire-Python.ps1')
}
catch {
	throw "The pinned CPython runtime could not be acquired or verified, so this release cannot run Automation Actions. $($_.Exception.Message)"
}

$pythonExecutable = Join-Path $pythonRoot 'python.exe'
if (-not (Test-Path -LiteralPath $pythonExecutable)) {
	throw "The pinned CPython runtime is missing at $pythonExecutable. Run scripts\automation\Acquire-Python.ps1."
}

$pythonHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pythonExecutable).Hash.ToLowerInvariant()
if ($pythonHash -ne $pythonMetadata.executableSha256) {
	throw "The pinned CPython executable does not match its pinned hash. Expected $($pythonMetadata.executableSha256), found $pythonHash."
}

Write-Host "Verified pinned CPython $($pythonMetadata.version) ($pythonHash)."

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

# Velopack packs whatever the publish directory holds, so proving the payload landed here proves it ships.
$publishedPayload = @(
	'AutomationRuntime\Python\python.exe',
	'AutomationRuntime\vxfiles_runner.py',
	'AutomationRuntime\vxfiles_automation.py',
	'AutomationPackages\vxfiles.tracer\vxpackage.json'
)
foreach ($relativePath in $publishedPayload) {
	if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $relativePath))) {
		throw "The published app is missing the Automation payload file $relativePath, so Automation Actions could not run once installed."
	}
}

$publishedPythonHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $publishDirectory 'AutomationRuntime\Python\python.exe')).Hash.ToLowerInvariant()
if ($publishedPythonHash -ne $pythonMetadata.executableSha256) {
	throw "The published CPython executable does not match its pinned hash. Expected $($pythonMetadata.executableSha256), found $publishedPythonHash."
}

Write-Host 'Verified the Automation payload in the publish output.'

# Losing the asset copy leaves an app that still launches with every ms-appx image blank, so the
# publish output is checked rather than trusted. See docs/VXFILES-UPSTREAM-MERGE-CHECKLIST.md.
# These are named first because comparing the two trees alone passes when both are empty.
$publishedAssets = @(
	'Assets\FluentIcons\SidebarSections\Home.scale-100.png',
	'Assets\FluentIcons\SidebarSections\Pinned.scale-100.png',
	'Assets\FolderIcon.png',
	'Assets\error.png'
)
foreach ($relativePath in $publishedAssets) {
	if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $relativePath))) {
		throw "The published app is missing the asset $relativePath, so icons loaded through ms-appx would render blank once installed."
	}
}

# Then the whole tree, so assets added upstream are covered without editing the list above.
$buildAssetRoots = @(
	Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src\Files.App\bin\x64\Release') -Directory -ErrorAction SilentlyContinue |
		ForEach-Object { Join-Path $_.FullName 'win-x64\Assets' } |
		Where-Object { Test-Path -LiteralPath $_ }
)
if ($buildAssetRoots.Count -ne 1) {
	throw "Expected exactly one Release build asset directory under src\Files.App\bin\x64\Release, found $($buildAssetRoots.Count). Delete src\Files.App\bin and build again."
}

$buildAssetRoot = $buildAssetRoots[0]
$expectedAssets = Get-ChildItem -LiteralPath $buildAssetRoot -Recurse -File |
	ForEach-Object { $_.FullName.Substring($buildAssetRoot.Length + 1) }
$publishedAssetRoot = Join-Path $publishDirectory 'Assets'
$missingPublishedAssets = @($expectedAssets | Where-Object {
		-not (Test-Path -LiteralPath (Join-Path $publishedAssetRoot $_))
	})
if ($missingPublishedAssets.Count -gt 0) {
	throw "The published app is missing $($missingPublishedAssets.Count) of $($expectedAssets.Count) asset files that the build produced, starting with $($missingPublishedAssets[0]). If those files are stale outputs from an earlier target framework, delete src\Files.App\bin and build again."
}

Write-Host "Verified all $($expectedAssets.Count) build assets reached the publish output."

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
