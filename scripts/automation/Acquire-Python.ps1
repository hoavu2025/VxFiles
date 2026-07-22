# Copyright (c) Files Community
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [string]$DestinationRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $PSScriptRoot '..\..\artifacts\python'
}
$metadataPath = Join-Path $PSScriptRoot 'python-3.14.6-win-x64.json'
$metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
$destination = Join-Path $DestinationRoot "$($metadata.version)-win-$($metadata.architecture)"
$completionMarker = Join-Path $destination '.complete'

if (Test-Path -LiteralPath $completionMarker) {
    $installedHash = (Get-Content -Raw -LiteralPath $completionMarker).Trim()
	$installedExecutable = Join-Path $destination 'python.exe'
	if ($installedHash -eq $metadata.sha256 -and (Test-Path -LiteralPath $installedExecutable)) {
		$installedExecutableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installedExecutable).Hash.ToLowerInvariant()
		if ($installedExecutableHash -eq $metadata.executableSha256) {
			Write-Output $destination
			return
		}
	}
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) "VxFiles-Python-$([Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $workRoot $metadata.artifact
$extractPath = Join-Path $workRoot 'runtime'

try {
    New-Item -ItemType Directory -Path $workRoot | Out-Null
    Invoke-WebRequest -Uri $metadata.uri -OutFile $archivePath
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
    if ($actualHash -ne $metadata.sha256) {
        throw "Python artifact hash mismatch. Expected $($metadata.sha256), received $actualHash."
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath
    if (-not (Test-Path -LiteralPath (Join-Path $extractPath 'python.exe'))) {
        throw 'The verified Python artifact did not contain python.exe.'
    }
	$executableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $extractPath 'python.exe')).Hash.ToLowerInvariant()
	if ($executableHash -ne $metadata.executableSha256) {
		throw "Python executable hash mismatch. Expected $($metadata.executableSha256), received $executableHash."
	}

    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Move-Item -LiteralPath $extractPath -Destination $destination
    [IO.File]::WriteAllText($completionMarker, $actualHash, [Text.UTF8Encoding]::new($false))
    Write-Output $destination
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
