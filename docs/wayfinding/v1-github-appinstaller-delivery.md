# V1 GitHub App Installer delivery

## Decision

GitHub Releases can host the V1 App Installer update chain without GitHub Actions, but the clean-machine install/update remains a release acceptance gate because GitHub serves release assets as `application/octet-stream` rather than Microsoft's documented MSIX media types.

Keep the upstream `MainBundle` interface and single-project packaging:

1. Build the proven self-contained x64 MSIX.
2. Use upstream `Create-MsixBundle.ps1` to wrap it in a one-architecture bundle.
3. Use a thin local/manual release adapter to flatten and rename the upstream CDN-oriented output, generate the GitHub-oriented descriptor, calculate checksums, and upload a complete draft release.
4. Publish the draft as a normal stable release only after every asset and URL validates.

Do not add a custom updater, change the application project topology, or use GitHub Actions.

## Stable and immutable URLs

The App Installer association needs one URL that does not change between releases:

```text
https://github.com/hoavu2025/VxFiles/releases/latest/download/VxFiles.appinstaller
```

Every descriptor points its bundle at an immutable tagged URL:

```text
https://github.com/hoavu2025/VxFiles/releases/download/v2.0.0/VxFiles-2.0.0-x64.msixbundle
```

Never embed a `release-assets.githubusercontent.com` URL. GitHub generates those signed redirect targets dynamically and they expire. The stable `github.com/.../latest/download/...` and immutable `github.com/.../download/<tag>/...` URLs refresh those redirects on every request.

GitHub's latest-release endpoint excludes drafts and prereleases. The manual flow must therefore assemble and validate a draft, then publish it as a non-prerelease in one final step. [GitHub's Releases REST documentation](https://docs.github.com/en/rest/releases/releases) defines draft, prerelease, and latest-release behavior.

## First refactored release

The repository already has unrelated portable/EXE releases tagged `v1.0.0`, `v1.0.1`, and `v1.0.2`. Tags are immutable history and must not be deleted or reused. The first refactored MSIX release is:

| Field | Value |
| --- | --- |
| Git tag and release | `v2.0.0` |
| MSIX/App Installer version | `2.0.0.0` |
| Release character | stable, not draft, not prerelease |

“V1” names the scope of this refactor; `v2.0.0` communicates the breaking distribution and compatibility change to users.

## Release asset layout

Each stable release contains exactly these V1 distribution assets:

```text
VxFiles.appinstaller
VxFiles-2.0.0-x64.msixbundle
VxFiles.Release.cer
SHA256SUMS.txt
```

Future releases keep `VxFiles.appinstaller` and `VxFiles.Release.cer` stable by name while versioning the bundle:

```text
VxFiles-2.0.1-x64.msixbundle
```

Do not attach portable ZIP or EXE installer artifacts to the refactored release. The older releases remain historical and are not update sources.

GitHub assets are flat. Upstream `Create-MsixBundle.ps1` generates a CDN directory such as `VxFiles.Package_2.0.0.0_Test/...`; the local release adapter consumes that output but publishes the flat names above. This isolates GitHub layout knowledge from the upstream packaging module.

## Descriptor contract

Use the existing 2018 namespace and `MainBundle` shape so both Windows App Installer and the inherited `SideloadUpdateService` can consume the same document:

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

`Name`, `Publisher`, and four-part `Version` must exactly match the embedded bundle/package identity. Microsoft documents that App Installer validates those fields against the referenced package. `HoursBetweenUpdateChecks="0"` asks Windows to check on every launch; only higher versions are accepted because V1 does not enable downgrade updates. [Microsoft's manual App Installer guide](https://learn.microsoft.com/en-us/windows/msix/app-installer/how-to-create-appinstaller-file) and [UpdateSettings schema](https://learn.microsoft.com/en-us/uwp/schemas/appinstallerschema/element-update-settings) define this contract.

## GitHub hosting proof

The proof used disposable public prereleases with explicitly non-installable, tiny assets. Every proof release and its unique tag was deleted in the same guarded operation, and no package was installed.

Observed behavior on 2026-07-26:

- A normal `gh release upload` recorded and served both `.appinstaller` and `.msixbundle` as `application/octet-stream`.
- Uploading through GitHub's release-asset API with explicit `application/appinstaller` and `application/msixbundle` made the API metadata report those values, but the public `github.com` download redirect still forced the final `release-assets.githubusercontent.com` response to `application/octet-stream`.
- Tagged asset requests returned HTTP 302 and then HTTP 200 with the correct bytes, content length, and attachment filename.
- The existing `/releases/latest/download/<asset>` form returned HTTP 302 to the latest stable tag, proving the stable redirect shape independently of the disposable prerelease.
- `Add-AppxPackage -AppInstallerFile` accepted the GitHub-hosted descriptor through the redirect and octet-stream response, parsed it, followed its tagged bundle URI, and failed with `0x8007000D` specifically while opening the intentionally invalid bundle. This proves descriptor retrieval, XML processing, and bundle URL traversal on the installed App Installer version.
- The probe left zero `VxFiles` AppX packages, releases, or tags.

GitHub documents that callers select a release asset's upload `Content-Type`, but the public download behavior above is empirical. [GitHub's release-asset upload documentation](https://docs.github.com/en/rest/releases/assets#upload-a-release-asset) owns the upload interface. Microsoft documents `application/appinstaller` and `application/msixbundle` as the expected response types in its [App Installer troubleshooting guide](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide#app-installer-and-web-delivery-errors).

Because the current Windows deployment stack tolerated GitHub's octet-stream response, GitHub Releases are an acceptable zero-cost V1 host for the small coworker cohort. The mismatch remains a known compatibility risk and must not be treated as universally supported behavior.

## Manual publication invariants

The release adapter and runbook must enforce all of these before publishing:

- the tag, release title, descriptor version, bundle identity version, tagged bundle URL, filename, and checksum agree;
- `VxFiles.appinstaller` uses the permanent latest URL for its own `Uri`;
- the bundle URI is tagged and immutable, never `latest` and never an expiring redirect target;
- the bundle and descriptor contain `VxFiles` and `CN=VxFiles`;
- the bundle signature and RFC 3161 timestamp verify;
- the public CER fingerprint matches the signing certificate;
- a HEAD/GET probe follows every GitHub redirect and returns the expected filename and nonzero content length;
- the stable release is published only after all assets exist in the draft;
- no Actions workflow or paid service participates.

Do not replace an asset after publishing a version. If any published bytes are wrong, issue a higher version and preserve the bad release for audit or mark it clearly withdrawn; never silently reuse a tag or version.

## Remaining acceptance proof

This workstation is not elevated, so it cannot place the self-signed public CER in `LocalMachine\TrustedPeople`. The following end-to-end checks remain mandatory before `v2.0.0` is called ready:

1. On a clean coworker-like x64 Windows account, import only `VxFiles.Release.cer` into `LocalMachine\TrustedPeople`.
2. Install through the stable `VxFiles.appinstaller` latest URL.
3. Launch the app and verify the `2.0.0.0` identity and self-contained runtime behavior.
4. Publish a disposable higher-version draft/test chain using the same certificate, exercise the tagged GitHub redirects, and prove App Installer updates the installed package.
5. Confirm an ordinary launch triggers the `HoursBetweenUpdateChecks="0"` update check.
6. Remove the test package and certificate trust entry.

Failure caused by GitHub's octet-stream response reopens the hosting decision; it must not be bypassed with manual MSIX installation while claiming App Installer delivery succeeded.
