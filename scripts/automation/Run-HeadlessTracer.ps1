# Copyright (c) Files Community
# Licensed under the MIT License.

# Proves the headless Automation runtime against the real pinned interpreter: acquires and verifies the
# runtime, then runs the focused automation suite. VxFiles itself is never launched.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

$runtimeRoot = & (Join-Path $PSScriptRoot 'Acquire-Python.ps1')
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    throw "Pinned Python acquisition failed with exit code $LASTEXITCODE."
}

$interpreter = Join-Path $runtimeRoot 'python.exe'
if (-not (Test-Path -LiteralPath $interpreter)) {
    throw "The pinned interpreter is missing at $interpreter; the tracer cannot prove real-process behavior."
}

# Without this a missing runtime would skip every real-process test and still exit zero.
$env:VXFILES_AUTOMATION_REQUIRE_RUNTIME = '1'

$project = Join-Path $repositoryRoot 'tests\VxFiles.Automation.Tests\VxFiles.Automation.Tests.csproj'
dotnet test $project -c $Configuration -p:Platform=x64 -v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Headless Automation tracer failed with exit code $LASTEXITCODE."
}

Write-Output 'Headless Automation tracer passed.'
