# V1 manual release and recovery runbook

## Purpose

This runbook is the operator interface for a zero-cost VxFiles release. It builds one self-contained x64 MSIX bundle, signs it with the protected self-signed release key, verifies the complete update chain, and publishes four assets to GitHub without GitHub Actions or a paid service.

The first refactored release uses GitHub tag `v2.0.0` and package version `2.0.0.0`. “V1” names the refactor scope, not its public version.

The release is a transaction with three checkpoints:

1. locally verified artifacts;
2. a complete, unpublished GitHub draft; and
3. an immutable stable publication.

Nothing after a failed checkpoint advances to the next one. Published package bytes, tags, and versions are never replaced or reused.

## Inputs and permanent invariants

The operator supplies:

- an accepted release commit on the clean V1 line;
- a three-part release version such as `2.0.0`;
- the encrypted `VxFiles.Release.pfx` from its private, non-repository location;
- the PFX password from a separate password manager;
- the matching public `VxFiles.Release.cer`;
- an empty release-output directory outside the repository; and
- release notes reviewed for the same commit.

Every release preserves:

| Field | Required value |
| --- | --- |
| Package name | `VxFiles` |
| Publisher and certificate subject | `CN=VxFiles` |
| Architecture | `x64` |
| Bundle asset | `VxFiles-<three-part-version>-x64.msixbundle` |
| Descriptor asset | `VxFiles.appinstaller` |
| Public certificate asset | `VxFiles.Release.cer` |
| Checksum asset | `SHA256SUMS.txt` |
| Descriptor URL | `https://github.com/hoavu2025/VxFiles/releases/latest/download/VxFiles.appinstaller` |
| Bundle URL | `https://github.com/hoavu2025/VxFiles/releases/download/v<three-part-version>/VxFiles-<three-part-version>-x64.msixbundle` |

Convert the three-part release version to a four-part package version by appending `.0`. Versions increase monotonically. The release tag, release title, descriptor version, bundle identity, asset filename, and tagged URL must all describe the same version.

## Workstation prerequisites

Use the release-owner Windows account on a BitLocker-protected workstation. It must have:

- the repository-pinned .NET 10 SDK;
- Visual Studio with the x64 MSBuild and Windows application packaging workload;
- the Windows SDK versions providing `makeappx.exe` and `signtool.exe`;
- PowerShell 7;
- Git and an authenticated GitHub CLI session with release permission; and
- administrator access for the one-time import of the public CER into `LocalMachine\TrustedPeople`.

The PFX stays outside every repository and worktree. Never put its password in a command argument, environment variable, script, transcript, release asset, or shell history.

## 1. Establish release variables

Run from the accepted clean worktree. Use a new empty output root for every attempt.

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
$cerPath = '<public-certificate-location>\VxFiles.Release.cer'
$releaseNotes = '<reviewed-release-notes>\RELEASE-NOTES.md'
$releaseCommit = (git rev-parse HEAD).Trim()
```

Abort unless:

- `git status --porcelain` is empty;
- `git tag --list $tag` returns nothing;
- `git ls-remote --exit-code --tags origin "refs/tags/$tag"` reports no existing tag;
- `gh release view $tag --repo hoavu2025/VxFiles` reports no existing release;
- the accepted release commit is on the clean V1 line;
- the package version is higher than every published VxFiles MSIX version;
- both certificate files exist outside the worktree; and
- both output directories are new and empty.

Do not automate away the “not found” exit codes in the tag and release checks: for this preflight, absence is success and any network/authentication error is a stop.

## 2. Validate the certificate inputs

Read the public and private certificates without printing or exporting private-key material:

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

Also verify that the certificate:

- uses RSA with a 3072-bit key;
- permits digital signatures;
- contains the code-signing EKU `1.3.6.1.5.5.7.3.3`;
- is not a certificate authority; and
- is not inside its six-month rotation window unless an approved rotation is already underway.

Record the subject, SHA-256 fingerprint, thumbprint, serial number, and expiry in the private release log. Do not record the PFX path or password.

The public CER must already be trusted on the release workstation:

```powershell
Import-Certificate `
  -FilePath $cerPath `
  -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
```

This is a one-time elevated trust operation, not a private-key import.

## 3. Restore and build the unsigned intermediate MSIX

Enter the x64 Visual Studio developer environment. Restore the full solution because the app-only restore omits `Files.App.Server`:

```powershell
msbuild Files.slnx `
  -t:Restore `
  -p:Configuration=Release `
  -p:Platform=x64 `
  -p:PublishReadyToRun=true `
  -v:quiet `
  -clp:ErrorsOnly
```

Build only the x64 package and keep signing disabled for this intermediate. The protected key is used only after the final bundle bytes exist:

```powershell
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

Abort on any build error or if the output contains anything other than one x64 application MSIX plus normal build metadata. Never publish the intermediate MSIX.

## 4. Create and flatten the one-architecture bundle

Use the inherited upstream bundler, but do not ask it to create the CDN-oriented App Installer descriptor:

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

The upstream script may construct a CDN folder and dependency folders. They are build intermediates only. The GitHub release contains the flattened, renamed bundle and no dependency packages because the application is self-contained.

## 5. Sign and timestamp the final bundle

Import the PFX only for the signing operation. Use the certificate thumbprint so its password never appears in the SignTool process arguments:

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

Save the verbose verification output in the private release log and confirm it reports both the expected `CN=VxFiles` signer and an RFC 3161 timestamp made while the certificate was valid. An untimestamped fallback is not releasable.

## 6. Inspect bundle identity and self-containment

Unbundle and unpack into disposable directories:

```powershell
$bundleInspection = Join-Path $releaseOutput '_bundle-inspection'
$packageInspection = Join-Path $releaseOutput '_package-inspection'

makeappx.exe unbundle /p $bundleAsset /d $bundleInspection /o
$innerPackages = @(Get-ChildItem $bundleInspection -Filter '*.msix' -File)
if ($innerPackages.Count -ne 1) { throw 'Expected exactly one inner x64 MSIX.' }
$innerPackage = $innerPackages[0]
makeappx.exe unpack /p $innerPackage.FullName /d $packageInspection /o
```

Abort unless inspection proves:

- exactly one x64 application package;
- bundle and package versions equal `$packageVersion`;
- package name `VxFiles` and publisher `CN=VxFiles`;
- main executable `VxFiles.exe`;
- protocol `vxfiles` and execution alias `vxfiles.exe`;
- no framework or resource-package dependency;
- `coreclr.dll`, `hostfxr.dll`, and `hostpolicy.dll` are present;
- `Microsoft.WindowsAppRuntime.dll` and `Microsoft.WindowsAppRuntime.Bootstrap.dll` are present; and
- no portable launcher, Inno Setup executable, ZIP distribution, Automation Actions implementation, or other non-allowlisted feature is present.

Delete only the two explicitly named inspection directories after recording the results. Do not recursively delete a computed or broad output path.

## 7. Generate the GitHub-oriented descriptor

Create `VxFiles.appinstaller` in the release directory using the fixed latest descriptor URL and immutable tagged bundle URL:

```powershell
$descriptorAsset = Join-Path $releaseOutput 'VxFiles.appinstaller'
$bundleUrl = "https://github.com/hoavu2025/VxFiles/releases/download/$tag/$assetStem.msixbundle"

$descriptor = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
  Uri="https://github.com/hoavu2025/VxFiles/releases/latest/download/VxFiles.appinstaller"
  Version="$packageVersion"
  xmlns="http://schemas.microsoft.com/appx/appinstaller/2018">
  <MainBundle
    Name="VxFiles"
    Publisher="CN=VxFiles"
    Version="$packageVersion"
    Uri="$bundleUrl" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="0" />
  </UpdateSettings>
</AppInstaller>
"@

[System.IO.File]::WriteAllText(
  $descriptorAsset,
  $descriptor,
  [System.Text.UTF8Encoding]::new($false))
```

Parse the result as XML and compare every identity field to the unpacked bundle and package manifests. Reject `latest` in the bundle URI, a `release-assets.githubusercontent.com` URI, any dependency entries, or any version mismatch.

## 8. Stage the public CER and checksums

```powershell
$cerAsset = Join-Path $releaseOutput 'VxFiles.Release.cer'
Copy-Item -LiteralPath $cerPath -Destination $cerAsset

$checksumAsset = Join-Path $releaseOutput 'SHA256SUMS.txt'
$publicAssets = @($descriptorAsset, $bundleAsset, $cerAsset)
$checksumLines = foreach ($asset in $publicAssets) {
  $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $asset
  "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($asset))"
}
[System.IO.File]::WriteAllLines(
  $checksumAsset,
  $checksumLines,
  [System.Text.UTF8Encoding]::new($false))
```

Confirm the release directory contains exactly:

```text
VxFiles.appinstaller
VxFiles-2.0.0-x64.msixbundle
VxFiles.Release.cer
SHA256SUMS.txt
```

Recompute every checksum independently. Confirm the CER thumbprint still matches the signer reported by SignTool.

## 9. Perform local installation checks

Before creating a public release, install from the local descriptor or final bundle on a disposable clean coworker-like x64 Windows account that trusts only the published CER in `LocalMachine\TrustedPeople`.

Verify installation, launch, VxFiles branding, fork download/support links, package identity/version, and uninstall. A manual bundle installation may prove the package but does not satisfy the separate GitHub App Installer delivery gate.

For an update release, also install the previous known-good stable release first and prove the new local bundle is accepted as a higher-version update without migrating or preserving fork-specific legacy data.

## 10. Create a complete GitHub draft

Create and push an annotated tag pointing at the already-recorded release commit, then require that exact remote tag when creating the draft:

```powershell
git tag -a $tag $releaseCommit -m "VxFiles $($releaseVersion.ToString(3))"
git push origin "refs/tags/$tag"

gh release create $tag `
  --repo hoavu2025/VxFiles `
  --verify-tag `
  --draft `
  --title "VxFiles $($releaseVersion.ToString(3))" `
  --notes-file $releaseNotes `
  $descriptorAsset `
  $bundleAsset `
  $cerAsset `
  $checksumAsset
```

Do not use `--generate-notes` as a substitute for reviewed release notes, and do not let GitHub create the tag implicitly.

While the release remains a draft:

- query its API metadata and confirm it targets `$releaseCommit`;
- confirm it is a draft, not a prerelease;
- confirm the four exact asset names, nonzero lengths, and no extras;
- download every asset through the authenticated draft API into a second empty directory;
- recompute and compare every checksum;
- re-run SignTool verification on the downloaded bundle;
- parse the downloaded descriptor and CER; and
- compare every downloaded byte to its staged source.

A draft is not a public App Installer endpoint. Its successful verification does not prove anonymous GitHub redirects.

## 11. Publish, probe, and declare readiness

Publication is the irreversible boundary:

```powershell
gh release edit $tag `
  --repo hoavu2025/VxFiles `
  --draft=false `
  --prerelease=false `
  --latest
```

Immediately verify:

- the release is stable and marked latest;
- the stable descriptor URL redirects to this tag and returns the exact descriptor bytes;
- the immutable tagged bundle URL redirects and returns the exact bundle bytes;
- all four anonymous downloads have the expected filename and nonzero content length;
- downloaded hashes match `SHA256SUMS.txt`; and
- the bundle signature, timestamp, certificate fingerprint, and descriptor identity still verify.

GitHub currently serves these assets as `application/octet-stream`, contrary to Microsoft’s documented media types. On the clean coworker-like machine, import only the public CER into `LocalMachine\TrustedPeople`, install through the stable latest descriptor URL, launch VxFiles, and verify automatic update traversal from a lower version. Ordinary launch must exercise `HoursBetweenUpdateChecks="0"`.

The release is not “ready” until the clean-machine install/update/uninstall and the acceptance checklist both pass. Remove the disposable package and certificate trust entry after the test.

## Recovery matrix

| Failure point | Required response |
| --- | --- |
| Before signing | Stop, retain logs, and discard only the explicitly named attempt output. No GitHub state exists. |
| Signing or timestamping fails | Remove the temporary private-key store entry in `finally`. Never publish an untimestamped bundle. Start a fresh output attempt after fixing the cause. |
| Local inspection or install fails | Do not tag or create a release. Fix the implementation and use a fresh output directory; the version may remain reserved locally because it was never published. |
| Tag pushed but draft not published | Keep the tag fixed to the same accepted commit. Repair or recreate the draft and its assets only if the package identity/version and commit remain unchanged. If the release commit or package bytes must change, abandon that version and use a higher one. |
| Draft upload or verification fails | Delete or repair the draft while it is unpublished, then re-download and verify all four assets. Never publish a partial draft. |
| Anonymous redirect or clean-machine test fails after publication | Quarantine the release by marking it prerelease and not latest if repository policy permits. Do not replace its assets, move its tag, or reuse its version. Publish the correction under a higher version. |
| First MSIX release is quarantined | The latest URL may fall back to an older release with no descriptor, intentionally stopping new App Installer installs/updates. Existing installed packages remain installed; recovery still requires a higher corrected MSIX version. |
| A bad update is already installed | Windows will not accept a lower-version repair. Publish a higher-version hotfix, or uninstall the bad package and reinstall a known-good package as an explicit manual recovery. |
| Last known-good release exists | During containment, explicitly mark that release latest only if it contains a descriptor whose immutable bundle URL and identity remain valid. Never edit its published assets. |
| PFX is lost | Stop releases. Generate a replacement certificate with the same exact subject, distribute and pre-trust its CER, and prove same-subject key-rotation update continuity before publishing a higher version. |
| PFX may be compromised | Stop publishing immediately, quarantine affected releases, remove the old CER from target machines, generate a replacement key, pre-trust it, and prove rotation. A directly trusted self-signed certificate has no public revocation service. |
| GitHub octet-stream delivery is rejected | Reopen the hosting decision. Do not claim success based on direct MSIX installation and do not add a custom updater without a new scoped decision. |

After any published failure, preserve the release, tag, checksums, logs, and hashes for audit even if release metadata is changed to quarantine it.

## Private release record

For each attempt, retain outside the repository:

- release version, tag, commit SHA, operator, and UTC timestamps;
- tool versions for .NET, MSBuild, Windows SDK, SignTool, MakeAppx, PowerShell, Git, and GitHub CLI;
- certificate subject, fingerprint, thumbprint, serial number, and expiry;
- build, bundle, signing, verification, redirect, install, update, launch, and uninstall results;
- final asset names, byte lengths, and SHA-256 hashes;
- draft and published release URLs; and
- any failure, containment, rotation, or recovery action.

Never retain the PFX password, private-key bytes, or a transcript containing the SecureString input.

## Boundaries

- This runbook does not perform the repository transition; the clean release line and accepted commit are prerequisites.
- It does not define acceptance thresholds beyond ordering the gate; those belong to **Set V1 acceptance and upstream-merge guardrails**.
- It does not create GitHub Actions, a custom updater, portable artifacts, paid infrastructure, backward-compatibility migration, or Automation Actions.
- Certificate generation and rotation follow **Define the zero-cost self-signed package identity**.
- Descriptor fields and GitHub URLs follow **Validate GitHub-hosted App Installer updates without Actions**.
