[CmdletBinding()]
param(
    [string]$InnoSetupCompilerPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$stagingDirectory = Join-Path $artifactsRoot 'staging\VxFiles-portable-win-x64'
$installerScript = Join-Path $repositoryRoot 'installer\VxFiles.iss'

# Build Release portable payload to ensure fresh staging artifacts
Write-Output "Generating fresh Release portable build for installer packaging..."
& (Join-Path $PSScriptRoot 'Build-Portable.ps1') -Configuration Release

# Find Inno Setup Compiler
$iscc = $null
$possiblePaths = @(
    $InnoSetupCompilerPath,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

if (Get-Command "iscc.exe" -ErrorAction SilentlyContinue) {
    $iscc = (Get-Command "iscc.exe").Source
} else {
    foreach ($path in $possiblePaths) {
        if (Test-Path -LiteralPath $path) {
            $iscc = $path
            break
        }
    }
}

if ($null -eq $iscc) {
    Write-Warning "Inno Setup Compiler (ISCC.exe) not found. Attempting to download Inno Setup..."

    # We can use winget to install Inno Setup if it's missing, but for now we'll throw an error
    # since we don't want to make system-wide changes unprompted.
    throw "Inno Setup Compiler not found. Please install Inno Setup 6/7 to build the installer (https://jrsoftware.org/isdl.php) or provide the path via -InnoSetupCompilerPath."
}

Write-Output "Using Inno Setup Compiler at: $iscc"
Write-Output "Building installer..."

# Run Inno Setup Compiler
$process = Start-Process -FilePath $iscc -ArgumentList "/Q", "`"$installerScript`"" -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) {
    throw "Inno Setup compilation failed with exit code $($process.ExitCode)."
}

$installerOutput = Join-Path $artifactsRoot 'VxFiles-Setup-win-x64.exe'
if (-not (Test-Path -LiteralPath $installerOutput)) {
    throw "Installer was not generated at $installerOutput"
}

$checksumPath = "$installerOutput.sha256"
$hash = (Get-FileHash -LiteralPath $installerOutput -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path $installerOutput -Leaf)" -Encoding ascii

Write-Output "Installer generated: $installerOutput"
Write-Output "SHA-256: $hash"
