# VxFiles manual release runbook

## Purpose

This is the operator interface for a zero-cost VxFiles release. It creates one self-contained x64 MSIX bundle, signs it with the protected self-signed release key, generates a GitHub App Installer descriptor, and publishes exactly four assets without GitHub Actions.

The first releases are:

- `v2.0.0` / package `2.0.0.0`: clean-install seed.
- `v2.0.1` / package `2.0.1.0`: automatic-update proof.

A release advances through three checkpoints: locally verified artifacts, a complete GitHub draft, and immutable stable publication. Stop at the first failed checkpoint.

## Permanent identity and layout

| Field | Required value |
| --- | --- |
| Package name | `VxFiles` |
| Publisher and certificate subject | `CN=VxFiles` |
| Architecture | `x64` |
| Bundle | `VxFiles-<version>-x64.msixbundle` |
| Descriptor | `VxFiles.appinstaller` |
| Public certificate | `VxFiles.Release.cer` |
| Checksums | `SHA256SUMS.txt` |
| Descriptor URL | `https://github.com/hoavu2025/VxFiles/releases/latest/download/VxFiles.appinstaller` |
| Bundle URL | `https://github.com/hoavu2025/VxFiles/releases/download/v<version>/VxFiles-<version>-x64.msixbundle` |

The release tag, title, descriptor, package identity, filename, and tagged URL must describe the same monotonically increasing version.

## Workstation and key requirements

Use the release owner's BitLocker-protected Windows workstation with:

- the repository-pinned .NET 10 SDK;
- Visual Studio x64 MSBuild and Windows application packaging workload;
- Windows SDK `makeappx.exe` and `signtool.exe`;
- PowerShell 7, Git, and an authenticated GitHub CLI; and
- administrator access for the public trust-store import.

Keep the encrypted PFX outside every repository and worktree on an owner-restricted volume. Keep its unique password in a separate password manager and one separately encrypted offline backup. Never place the PFX path or password in source, arguments, environment variables, transcripts, release assets, or shell history.

## One-time release certificate creation

Create one three-year RSA-3072, SHA-256, code-signing-only, non-CA certificate. Run this outside the repository and replace the placeholders with private paths:

```powershell
$releaseCert = $null
$releaseThumbprint = $null
try {
  $releaseCert = New-SelfSignedCertificate `
    -Subject 'CN=VxFiles' `
    -Type Custom `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(3) `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @(
      '2.5.29.19={critical}{text}ca=0',
      '2.5.29.37={text}1.3.6.1.5.5.7.3.3'
    )
  $releaseThumbprint = $releaseCert.Thumbprint

  $pfxPassword = Read-Host 'New unique PFX password' -AsSecureString
  Export-PfxCertificate `
    -Cert $releaseCert `
    -FilePath '<private-location>\VxFiles.Release.pfx' `
    -Password $pfxPassword `
    -CryptoAlgorithmOption AES256_SHA256
  Export-Certificate `
    -Cert $releaseCert `
    -FilePath '<public-location>\VxFiles.Release.cer' `
    -Type CERT
} finally {
  if ($null -ne $releaseThumbprint) {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$releaseThumbprint" -ErrorAction SilentlyContinue
  }
  $releaseCert = $null
  $pfxPassword = $null
}

if (Test-Path -LiteralPath "Cert:\CurrentUser\My\$releaseThumbprint") {
  throw 'Temporary release private key was not removed.'
}
```

Verify that the CER has subject `CN=VxFiles`, a 3072-bit RSA public key, digital-signature key usage, code-signing EKU `1.3.6.1.5.5.7.3.3`, no CA capability, and the intended expiry. Record its SHA-256 fingerprint, thumbprint, serial number, and expiry in the private release log.

Import only the public CER into the release workstation's trusted store:

```powershell
Import-Certificate `
  -FilePath '<public-location>\VxFiles.Release.cer' `
  -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
```

Begin rotation six months before expiry. Distribute and pre-trust the replacement CER before signing an update with its key.

## 1. Preflight

Use an accepted clean worktree and a new empty output directory:

```powershell
$ErrorActionPreference = 'Stop'
$releaseVersion = [version]'2.0.0'
$packageVersion = "$($releaseVersion.Major).$($releaseVersion.Minor).$($releaseVersion.Build).0"
$tag = "v$($releaseVersion.ToString(3))"
$assetStem = "VxFiles-$($releaseVersion.ToString(3))-x64"
$repoRoot = (Get-Location).Path
$packageOutput = '<empty-private-output>\package'
$releaseOutput = '<empty-private-output>\release'
$pfxPath = '<private-location>\VxFiles.Release.pfx'
$cerPath = '<public-location>\VxFiles.Release.cer'
$releaseNotes = '<reviewed-notes>\RELEASE-NOTES.md'
$releaseCommit = (git rev-parse HEAD).Trim()
```

Abort unless:

- `git status --porcelain` is empty;
- the tag, remote tag, and GitHub release do not exist;
- the commit is accepted on the clean V1 line;
- the package version is higher than every published VxFiles MSIX;
- the PFX and CER exist outside the worktree; and
- both output directories are new and empty.

Validate the CER and PFX without printing private material:

```powershell
$publicCert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cerPath)
$pfxPassword = Read-Host 'VxFiles release PFX password' -AsSecureString
$privateCert = Get-PfxData -FilePath $pfxPath -Password $pfxPassword |
  Select-Object -ExpandProperty EndEntityCertificates |
  Select-Object -First 1

if ($publicCert.Subject -cne 'CN=VxFiles') { throw 'Unexpected CER subject.' }
if ($privateCert.Subject -cne 'CN=VxFiles') { throw 'Unexpected PFX subject.' }
if ($publicCert.Thumbprint -ne $privateCert.Thumbprint) { throw 'CER and PFX do not match.' }
if ($publicCert.NotAfter -le (Get-Date)) { throw 'Release certificate has expired.' }
```

Repeat the algorithm, key-size, usage, EKU, CA, and rotation-window checks before every release.

## 2. Restore and build the unsigned x64 MSIX

Enter an x64 Visual Studio developer environment. Restore the full solution because app-only restore omits the out-of-process server:

```powershell
msbuild Files.slnx `
  -t:Restore `
  -p:Configuration=Release `
  -p:Platform=x64 `
  -p:PublishReadyToRun=true `
  -v:quiet `
  -clp:ErrorsOnly

msbuild src/Files.App/Files.App.csproj `
  -t:Build `
  -p:Configuration=Release `
  -p:Platform=x64 `
  -p:AppxBundlePlatforms=x64 `
  -p:AppxBundle=Never `
  -p:AppxPackageVersion=$packageVersion `
  -p:GenerateAppxPackageOnBuild=true `
  -p:UapAppxPackageBuildMode=SideloadOnly `
  -p:AppxPackageDir="$packageOutput\" `
  -p:AppxPackageSigningEnabled=false `
  -v:quiet `
  -clp:ErrorsOnly
```

Abort on errors or unless the output contains exactly one x64 application MSIX plus normal build metadata. Never publish the intermediate MSIX.

## 3. Bundle and flatten

Use the unchanged upstream bundle helper without its CDN-oriented descriptor:

```powershell
& .github/scripts/Create-MsixBundle.ps1 `
  -AppxPackageDir $packageOutput `
  -BundleName 'VxFiles' `
  -Version $packageVersion `
  -PackageManifestPath 'src/Files.App/Package.appxmanifest' `
  -BuildMode SideloadOnly

$upstreamBundle = Join-Path $packageOutput `
  "VxFiles_${packageVersion}_Test\VxFiles_${packageVersion}_x64.msixbundle"
$bundleAsset = Join-Path $releaseOutput "$assetStem.msixbundle"

New-Item -ItemType Directory -Path $releaseOutput | Out-Null
Copy-Item -LiteralPath $upstreamBundle -Destination $bundleAsset
```

Dependency and CDN directories are intermediates only. The GitHub release uses the flat four-asset layout.

## 4. Sign and timestamp the final bundle

Temporarily import the private key, select it by thumbprint, and remove it even on failure:

```powershell
$importedCertificate = $null
try {
  $importedCertificate = Import-PfxCertificate `
    -FilePath $pfxPath `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -Password $pfxPassword

  signtool.exe sign `
    /fd SHA256 `
    /sha1 $importedCertificate.Thumbprint `
    /tr 'http://timestamp.digicert.com' `
    /td SHA256 `
    $bundleAsset

  if ($LASTEXITCODE -ne 0) { throw 'Bundle signing or timestamping failed.' }
} finally {
  if ($null -ne $importedCertificate) {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($importedCertificate.Thumbprint)"
  }
  $importedCertificate = $null
  $privateCert = $null
  $pfxPassword = $null
}

signtool.exe verify /pa /v $bundleAsset
if ($LASTEXITCODE -ne 0) { throw 'Bundle signature verification failed.' }
```

The verification log must show `CN=VxFiles` and an RFC 3161 timestamp made while the certificate was valid. An untimestamped bundle is not releasable.

## 5. Inspect identity and self-containment

Unbundle and unpack into two explicitly named disposable directories:

```powershell
$bundleInspection = Join-Path $releaseOutput '_bundle-inspection'
$packageInspection = Join-Path $releaseOutput '_package-inspection'
makeappx.exe unbundle /p $bundleAsset /d $bundleInspection /o
$innerPackages = @(Get-ChildItem $bundleInspection -Filter '*.msix' -File)
if ($innerPackages.Count -ne 1) { throw 'Expected exactly one inner x64 MSIX.' }
makeappx.exe unpack /p $innerPackages[0].FullName /d $packageInspection /o
```

Abort unless inspection proves:

- one x64 application package at `$packageVersion`;
- package name `VxFiles`, publisher `CN=VxFiles`, and executable `VxFiles.exe`;
- protocol `vxfiles` and alias `vxfiles.exe`;
- no framework or resource-package dependency;
- `coreclr.dll`, `hostfxr.dll`, and `hostpolicy.dll` are embedded;
- `Microsoft.WindowsAppRuntime.dll` and `Microsoft.WindowsAppRuntime.Bootstrap.dll` are embedded; and
- no portable launcher, Inno installer, ZIP distribution, Automation Actions implementation, or other non-allowlisted feature is present.

After recording results, remove only `$bundleInspection` and `$packageInspection`.

## 6. Create the App Installer descriptor

Create `VxFiles.appinstaller` with the permanent latest URL and immutable bundle URL:

```xml
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
  Uri="https://github.com/hoavu2025/VxFiles/releases/latest/download/VxFiles.appinstaller"
  Version="2.0.0.0"
  xmlns="http://schemas.microsoft.com/appx/appinstaller/2018">
  <MainBundle
    Name="VxFiles"
    Publisher="CN=VxFiles"
    Version="2.0.0.0"
    Uri="https://github.com/hoavu2025/VxFiles/releases/download/v2.0.0/VxFiles-2.0.0-x64.msixbundle" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="0" />
  </UpdateSettings>
</AppInstaller>
```

Substitute the current version in all three versioned fields. Parse the XML and assert the namespace, descriptor URI, `MainBundle` identity, immutable bundle URI, and `OnLaunch` value.

Copy the matching public CER to `$releaseOutput\VxFiles.Release.cer`. Generate `SHA256SUMS.txt` from the other three assets using SHA-256, stable filenames, and lowercase hexadecimal hashes. Verify the checksum file independently.

The release directory must now contain exactly:

```text
VxFiles.appinstaller
VxFiles-<version>-x64.msixbundle
VxFiles.Release.cer
SHA256SUMS.txt
```

## 7. Verify a GitHub draft

Create an annotated tag only after local verification, then create a draft:

```powershell
git tag -a $tag $releaseCommit -m "VxFiles $($releaseVersion.ToString(3))"
git push origin "refs/tags/$tag"

gh release create $tag `
  --repo hoavu2025/VxFiles `
  --title "VxFiles $($releaseVersion.ToString(3))" `
  --notes-file $releaseNotes `
  --draft `
  (Join-Path $releaseOutput 'VxFiles.appinstaller') `
  $bundleAsset `
  (Join-Path $releaseOutput 'VxFiles.Release.cer') `
  (Join-Path $releaseOutput 'SHA256SUMS.txt')
```

Before publication:

- list the draft and confirm it targets the intended tag and commit;
- confirm there are exactly four assets with exact names;
- download all assets into a new empty verification directory;
- compare each downloaded hash and size with the local artifact;
- repeat signature, certificate, manifest, descriptor, redirect, and checksum checks on downloaded bytes; and
- confirm the descriptor's bundle URL resolves to this draft's immutable asset.

Do not repair a bad draft by overwriting assets. Delete the unpublished draft and tag, increment the version if signed bytes escaped the workstation, and start again with an empty output directory.

## 8. Publish and clean-machine acceptance

Publish only after the draft is complete:

```powershell
gh release edit $tag --repo hoavu2025/VxFiles --draft=false --latest
```

Re-download the four stable assets and repeat the verification. Published assets and tags are immutable.

On a separate clean x64 Windows machine, the tester must:

1. Confirm no VxFiles package or VxFiles certificate trust exists.
2. Download the CER from the stable release, verify its fingerprint, and import only it into `LocalMachine\TrustedPeople`.
3. Open the stable `VxFiles.appinstaller` URL and install through App Installer.
4. Confirm Start, Installed Apps, App Installer, splash, window title, startup task, executable, `vxfiles:` protocol, `vxfiles.exe` alias, and fork links.
5. Launch and navigate through normal file-manager workflows.
6. Record package version `2.0.0.0`, screenshots, errors, and downloaded asset hashes.
7. After `v2.0.1` is published, launch normally and prove App Installer updates the same installation to `2.0.1.0`. Direct bundle installation is not a substitute.
8. Repeat identity and smoke checks, uninstall VxFiles, remove the exact CER from Trusted People, and confirm no private key was ever present.

Every item is blocking. V1 is accepted only after both the clean install and automatic update pass.

## Recovery rules

- Before stable publication, delete an invalid draft and its unshared tag, fix the cause, and rebuild in a new empty directory.
- After stable publication, never replace an asset, move a tag, or reuse a package version. Quarantine the release in its notes and recover with a higher version.
- If the PFX is lost but not compromised, continue only after coworkers have pre-trusted a replacement CER and a higher-version update has been proven.
- If compromise is suspected, stop releases, remove trust in the compromised CER, generate a new key and certificate, distribute the new fingerprint out of band, and resume only with a higher version.
- Keep historical `v1.0.0` through `v1.0.2` releases unchanged; they are not App Installer sources.
