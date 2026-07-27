# VxFiles manual release runbook

## Distribution model

VxFiles V1 is a self-contained .NET 10 x64 desktop app distributed with Velopack. The installer is per-user, does not require administrator rights, and installs under the user's local application data directory.

There is no portable ZIP, MSIX, PFX, certificate trust step, Microsoft Store submission, or GitHub Actions workflow. The unsigned installer can trigger a Windows SmartScreen warning until the app builds reputation or is signed later.

The internal Velopack package ID is `VxFilesApp`, so the install root is `%LocalAppData%\VxFilesApp`. The visible product and installer names remain VxFiles. Application data is stored separately under `%LocalAppData%\VxFiles Community\VxFiles`; the legacy `%LocalAppData%\VxFiles` data directory is not touched.

## Automation payload

Every release carries the app-local Automation payload so a clean install can run Automation Actions without Python:

- `AutomationRuntime\Python` — the pinned CPython 3.14.6 x64 embeddable runtime;
- `AutomationRuntime\*.py` — the runner and cancellation helper; and
- `AutomationPackages\vxfiles.tracer` — a read-only diagnostic package with two actions.

The build script acquires the runtime through `scripts\automation\Acquire-Python.ps1`, which verifies the archive and executable SHA-256 against `scripts\automation\python-3.14.6-win-x64.json`. It then re-checks the executable hash, and after publishing confirms every payload file reached the publish directory with the interpreter still matching its pinned hash. Any mismatch or missing file aborts the release rather than shipping an app that cannot run actions. Publishing without the runtime present fails in MSBuild before the build completes.

The interpreter is roughly 30 MB and is deliberately not committed; a fresh clone acquires it on first release build.

## Publish with GitHub CLI

The recommended release path is a local PowerShell script that uses GitHub CLI. It does not use GitHub Actions. Before running it, install `gh`, authenticate with `gh auth login`, and commit every change on `main`.

To build, upload, verify, and publish version 2.0.2:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\release\Publish-VxFilesGitHubRelease.ps1 `
  -Version 2.0.2
```

Optionally provide Markdown release notes:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\release\Publish-VxFilesGitHubRelease.ps1 `
  -Version 2.0.2 `
  -ReleaseNotes .\release-notes\2.0.2.md
```

The script requires a clean `main` branch, verifies GitHub authentication and version immutability, pushes `main`, builds the self-contained x64 release, and uploads all five Velopack files to a draft GitHub release. It verifies each draft asset's name, upload state, and size before making the stable release public. If verification fails, the release remains a draft.

## Build without publishing

Choose a version that has never been published. Existing Git tags and releases are immutable; do not replace the earlier MSIX `v2.0.0` release.

From a PowerShell prompt:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\release\Build-VxFilesRelease.ps1 `
  -Version 2.0.2
```

Optionally attach Markdown release notes to Velopack metadata:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\release\Build-VxFilesRelease.ps1 `
  -Version 2.0.2 `
  -ReleaseNotes .\release-notes\2.0.2.md
```

The script restores and publishes the app as self-contained x64, installs the pinned Velopack CLI locally under the ignored `artifacts` directory if needed, and writes the release to `artifacts\releases\<version>\release`.

The script refuses to reuse a non-empty output directory or an existing `v<version>` Git tag. It also verifies that Velopack produced an installer and update manifest without a portable package.

## Verify locally

Before publishing:

1. Run `VxFilesApp-win-Setup.exe` as a standard Windows user. The installer UI and installed app are named VxFiles.
2. Confirm VxFiles launches and creates a visible window.
3. Confirm the Start menu shortcut launches VxFiles.
4. Confirm the installed app is under `%LocalAppData%\VxFilesApp`.
5. Confirm settings and logs are under `%LocalAppData%\VxFiles Community\VxFiles`.
6. Confirm `%LocalAppData%\VxFilesApp\current\AutomationRuntime\Python\python.exe` and `AutomationPackages\vxfiles.tracer\vxpackage.json` exist in the installed app.
7. Open the Info Pane, select Tools, and confirm the bundled Tracer package lists both of its actions.
8. Select a file in the current folder, run Tracer's **Report selection**, and accept the trust prompt. It must name `vxfiles.tracer`, show the installed package path, and list both actions, not just the one being run. The run then reports the selection it saw. This is the only check that proves the pinned interpreter in the installed layout actually executes.
9. Run the same action again and confirm no trust prompt appears. A second prompt means trust is not being persisted under `%LocalAppData%\VxFiles Community\VxFiles`.
10. Build the next version and install it over the first version to prove the per-user update path.

The first unsigned launch may show SmartScreen. That warning is different from administrator elevation and certificate trust.

## Publish manually without the script

Create a new GitHub release in `hoavu2025/VxFiles` with:

- tag `v<version>`;
- title `VxFiles <version>`;
- a stable, non-draft, non-prerelease release for normal in-app updates; and
- every file from `artifacts\releases\<version>\release` as an asset.

Do not upload the `publish` directory. In particular, keep `VxFilesApp-win-Setup.exe`, `releases.win.json`, and the generated full package together in the release. Do not rename generated assets because Velopack records their filenames in its metadata. Velopack's GitHub update source reads this metadata and downloads the matching package from the fork's releases.

After publication, test from a different standard-user Windows account or machine:

1. Download `VxFilesApp-win-Setup.exe` from the GitHub release.
2. Install without administrator credentials.
3. Launch VxFiles.
4. Publish a higher test version when update verification is required.
5. Start the older installed version, leave it running long enough to download in the background, then close it.
6. Launch VxFiles again and confirm About reports the newer version. The update installs on exit without being clicked; the address-bar update button is only the "apply it now" shortcut.

Updating requires a real Velopack installation. A copied or unzipped build reports no update because Velopack sees it as not installed.

## V1 limitations

The unpackaged V1 deliberately omits package-identity features such as MSIX startup tasks, Jump Lists, packaged protocol/file associations, packaged COM activation, background tasks, and "set as default file manager" integration. Automation Actions are also deferred.

Keep the unpackaged compatibility code behind the small environment and update-service seams. This limits conflicts when merging future Files upstream content, features, and fixes.
