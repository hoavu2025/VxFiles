# Copyright (c) VxFiles contributors
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [switch]$Run,

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src\Files.App\Files.App.csproj'
$outputRoot = Join-Path $repositoryRoot 'src\Files.App\bin\x64\Debug'

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

$msbuildArguments = @(
    $projectPath,
    '-t:Build',
    '-p:Configuration=Debug',
    '-p:Platform=x64',
    '-p:RuntimeIdentifier=win-x64',
    '-v:quiet',
    '-clp:ErrorsOnly'
)

if (-not $NoRestore) {
    $msbuildArguments = @('-restore') + $msbuildArguments
}

& $msbuildPath @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Debug build failed with exit code $LASTEXITCODE."
}

$executable = Get-ChildItem -LiteralPath $outputRoot -Recurse -Filter 'VxFiles.exe' -File |
    Where-Object { $_.FullName -notmatch '[\\/]publish[\\/]' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $executable) {
    throw "Debug build did not produce VxFiles.exe under $outputRoot."
}

Write-Output "Debug executable: $($executable.FullName)"

if ($Run) {
    if (Get-Process -Name 'VxFiles' -ErrorAction SilentlyContinue) {
        throw 'Close the running VxFiles process before using -Run so the new build is launched.'
    }

    Start-Process -FilePath $executable.FullName
}
