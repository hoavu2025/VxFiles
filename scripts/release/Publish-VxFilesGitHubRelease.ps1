[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
	[string]$Version,

	[string]$ReleaseNotes
)

$ErrorActionPreference = 'Stop'
$repository = 'hoa-d-vu-vgames/VxFiles'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildScript = Join-Path $PSScriptRoot 'Build-VxFilesRelease.ps1'
$releaseDirectory = Join-Path $repositoryRoot "artifacts\releases\$Version\release"
$tag = "v$Version"

function Assert-NativeCommandSucceeded {
	param(
		[Parameter(Mandatory)]
		[string]$Message
	)

	if ($LASTEXITCODE -ne 0) {
		throw $Message
	}
}

if ($ReleaseNotes) {
	$ReleaseNotes = (Resolve-Path -LiteralPath $ReleaseNotes).Path
}

$gh = Get-Command gh -ErrorAction Stop

$branch = (& git -C $repositoryRoot branch --show-current | Out-String).Trim()
Assert-NativeCommandSucceeded 'Unable to determine the current Git branch.'
if ($branch -ne 'main') {
	throw "Releases must be published from main. Current branch: $branch"
}

$worktreeStatus = @(& git -C $repositoryRoot status --porcelain)
Assert-NativeCommandSucceeded 'Unable to inspect the Git worktree.'
if ($worktreeStatus.Count -gt 0) {
	throw 'The Git worktree is not clean. Commit or stash every change before publishing.'
}

& $gh.Source auth status --hostname github.com
Assert-NativeCommandSucceeded 'GitHub CLI is not authenticated for github.com. Run gh auth login first.'

$previousLocation = Get-Location
try {
	Set-Location -LiteralPath $repositoryRoot
	$repositoryJson = & $gh.Source repo view --json nameWithOwner
	Assert-NativeCommandSucceeded 'Unable to inspect the current GitHub repository.'
}
finally {
	Set-Location -LiteralPath $previousLocation
}
$currentRepository = ($repositoryJson | ConvertFrom-Json).nameWithOwner
if ($currentRepository -ne $repository) {
	throw "This checkout resolves to $currentRepository, but releases must target $repository."
}

$remoteTag = (& git -C $repositoryRoot ls-remote --tags origin "refs/tags/$tag" | Out-String).Trim()
Assert-NativeCommandSucceeded 'Unable to inspect remote Git tags.'
if ($remoteTag) {
	throw "Remote tag $tag already exists. Published versions are immutable."
}

$releaseListJson = & $gh.Source release list --repo $repository --limit 1000 --json tagName
Assert-NativeCommandSucceeded 'Unable to inspect existing GitHub releases.'
$existingRelease = @($releaseListJson | ConvertFrom-Json) |
	Where-Object tagName -EQ $tag |
	Select-Object -First 1
if ($existingRelease) {
	throw "GitHub release $tag already exists. Published versions are immutable."
}

Write-Host "Pushing main to $repository..."
& git -C $repositoryRoot push origin main
Assert-NativeCommandSucceeded 'Unable to push main. Resolve the divergence or authentication problem and retry.'

& git -C $repositoryRoot fetch origin main
Assert-NativeCommandSucceeded 'Unable to refresh origin/main after pushing.'
$localCommit = (& git -C $repositoryRoot rev-parse HEAD | Out-String).Trim()
Assert-NativeCommandSucceeded 'Unable to resolve the local commit.'
$remoteCommit = (& git -C $repositoryRoot rev-parse origin/main | Out-String).Trim()
Assert-NativeCommandSucceeded 'Unable to resolve origin/main.'
if ($localCommit -ne $remoteCommit) {
	throw 'Local main and origin/main do not point to the same commit after pushing.'
}

$buildArguments = @{
	Version = $Version
}
if ($ReleaseNotes) {
	$buildArguments.ReleaseNotes = $ReleaseNotes
}

& $buildScript @buildArguments

if (-not (Test-Path -LiteralPath $releaseDirectory -PathType Container)) {
	throw "Release directory was not found: $releaseDirectory"
}

$expectedAssetNames = @(
	'assets.win.json'
	'RELEASES'
	'releases.win.json'
	"VxFilesApp-$Version-full.nupkg"
	'VxFilesApp-win-Setup.exe'
)
$releaseAssets = @(Get-ChildItem -LiteralPath $releaseDirectory -File)
$actualAssetNames = @($releaseAssets.Name | Sort-Object)
$unexpectedAssetNames = @($actualAssetNames | Where-Object { $_ -notin $expectedAssetNames })
$missingAssetNames = @($expectedAssetNames | Where-Object { $_ -notin $actualAssetNames })
if ($missingAssetNames.Count -gt 0 -or $unexpectedAssetNames.Count -gt 0) {
	throw "Release assets do not match the expected Velopack set. Missing: $($missingAssetNames -join ', '). Unexpected: $($unexpectedAssetNames -join ', ')."
}
$emptyAssetNames = @($releaseAssets | Where-Object Length -EQ 0 | Select-Object -ExpandProperty Name)
if ($emptyAssetNames.Count -gt 0) {
	throw "Release assets must not be empty: $($emptyAssetNames -join ', ')"
}

$assetManifestPath = Join-Path $releaseDirectory 'assets.win.json'
$assetManifest = Get-Content -LiteralPath $assetManifestPath -Raw | ConvertFrom-Json
$missingManifestAssets = @($assetManifest | Where-Object {
	-not (Test-Path -LiteralPath (Join-Path $releaseDirectory $_.RelativeFileName) -PathType Leaf)
})
if ($missingManifestAssets.Count -gt 0) {
	throw "Velopack metadata references missing assets: $($missingManifestAssets.RelativeFileName -join ', ')"
}

$assetPaths = @($expectedAssetNames | ForEach-Object { Join-Path $releaseDirectory $_ })
$createArguments = @(
	'release'
	'create'
	$tag
) + $assetPaths + @(
	'--repo'
	$repository
	'--target'
	$localCommit
	'--title'
	"VxFiles $Version"
	'--draft'
)
if ($ReleaseNotes) {
	$createArguments += @('--notes-file', $ReleaseNotes)
}
else {
	$defaultNotes = @"
VxFiles $Version

Download **VxFilesApp-win-Setup.exe** to install VxFiles for the current Windows user. Administrator rights are not required. Windows SmartScreen may warn about the unsigned installer.
"@
	$createArguments += @('--notes', $defaultNotes)
}

Write-Host "Uploading $tag as a draft release..."
& $gh.Source @createArguments
Assert-NativeCommandSucceeded "Unable to create draft release $tag. Inspect the draft on GitHub before retrying."

$draftJson = & $gh.Source release view $tag --repo $repository --json tagName,isDraft,isPrerelease,targetCommitish,url,assets
Assert-NativeCommandSucceeded "Unable to verify draft release $tag."
$draft = $draftJson | ConvertFrom-Json
$uploadedAssetNames = @($draft.assets.name | Sort-Object)
$missingUploadedAssets = @($expectedAssetNames | Where-Object { $_ -notin $uploadedAssetNames })
$unexpectedUploadedAssets = @($uploadedAssetNames | Where-Object { $_ -notin $expectedAssetNames })
$invalidUploadedAssets = @($draft.assets | Where-Object {
	$_.state -ne 'uploaded' -or $_.size -le 0
})
if (-not $draft.isDraft -or $draft.isPrerelease -or
	$missingUploadedAssets.Count -gt 0 -or $unexpectedUploadedAssets.Count -gt 0 -or
	$invalidUploadedAssets.Count -gt 0) {
	throw "Draft verification failed. The release remains private. Missing: $($missingUploadedAssets -join ', '). Unexpected: $($unexpectedUploadedAssets -join ', '). Invalid: $($invalidUploadedAssets.name -join ', ')."
}

Write-Host "Publishing verified release $tag..."
& $gh.Source release edit $tag --repo $repository --draft=false --prerelease=false
Assert-NativeCommandSucceeded "Unable to publish release $tag. The verified draft remains on GitHub."

$publishedJson = & $gh.Source release view $tag --repo $repository --json isDraft,isPrerelease,url
Assert-NativeCommandSucceeded "Unable to verify published release $tag."
$published = $publishedJson | ConvertFrom-Json
if ($published.isDraft -or $published.isPrerelease) {
	throw "GitHub still reports $tag as a draft or prerelease."
}

$installerUrl = "https://github.com/$repository/releases/download/$tag/VxFilesApp-win-Setup.exe"
Write-Host ''
Write-Host "Published VxFiles $Version successfully."
Write-Host "Release: $($published.url)"
Write-Host "Installer: $installerUrl"
