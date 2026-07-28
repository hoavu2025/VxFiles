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

Package trust covers the whole `AutomationRuntime` tree, runner scripts included, not just the interpreter. Any release that changes `Runtime\*.py` or the pinned CPython therefore re-prompts every user for trust on their next run. That is intended, but it means a runtime change is never a silent one.

## Gate before building a release

Run the headless tracer. It acquires and hash-verifies the pinned runtime, then runs the automation suite against the real interpreter, so a runtime that cannot actually execute fails here rather than on a user's machine:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\automation\Run-HeadlessTracer.ps1 -Configuration Release
```

It sets `VXFILES_AUTOMATION_REQUIRE_RUNTIME=1`, so a missing runtime fails the run instead of skipping every real-process test and still reporting success. VxFiles itself is never launched.

`Discover_disables_package_containing_reparse_point` skips on an account without the symbolic-link privilege. That is the only expected skip; any other skip means the runtime was not acquired.

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
10. Build the next version and install it over the first version to prove the per-user update path. Then re-run Tracer's **Report selection** and confirm it still works. A trust prompt here is expected only if the release changed `AutomationRuntime`; a prompt after an unchanged runtime means package state did not survive the update.

The first unsigned launch may show SmartScreen. That warning is different from administrator elevation and certificate trust.

## Automation manual matrix

Most of the Automation behaviour a release depends on is covered by `Run-HeadlessTracer.ps1` and does not need a human: invalid packages and actions, duplicate ids, Unicode path handling, trust renewal on package and runner changes, cooperative and forced cancellation, process-tree teardown on shutdown, concurrency limits, output budgets, and run-history pruning.

Two of those the tracer only covers synthetically, so they still need a real check. Its UNC coverage is string classification against literal `\\server\share` paths, never a mounted share; and its missing-tool coverage uses a manifest that names a tool nothing resolves, never a genuinely absent executable. If a release changes path handling or external-tool resolution, do these by hand:

| Check | Expected |
| --- | --- |
| Browse to a real `\\server\share`, select a file, run **Report selection** | The run reports the item and counts it as being on a UNC path |
| Add a package declaring an `externalTools` entry that is not installed | The package shows as *Missing dependency* with the tool's display name in its diagnostic, and its actions do not run |

What the tracer cannot reach at all is the Tools tab itself. Check these by hand on the installed build:

| Check | Expected |
| --- | --- |
| Type in the filter box | Matching packages and actions remain, matched roots auto-expand, and clearing restores the expansion you had |
| Select nothing, then select a file | Run enables and disables to match; a disabled Run's tooltip says why |
| Navigate to Home or the Recycle Bin | Every Run is disabled and reads "Open a folder to run this action" |
| Copy a malformed package folder into the user packages folder | It appears as a disabled root with a diagnostic, without restarting the app |
| Start **Report selection**, then press Cancel | The run ends as Cancelled and no Python process survives in Task Manager |
| Start an action, then navigate away before it finishes | The result is reported as skipped rather than applied to the folder you moved to |
| Close the window while an action is running | The process tree is gone within a few seconds |

The last two are the ones worth doing carefully: they are the only checks of result routing and shutdown against a real window, and both are silent when they fail.

## Publish manually without the script

Create a new GitHub release in `hoa-d-vu-vgames/VxFiles` with:

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
