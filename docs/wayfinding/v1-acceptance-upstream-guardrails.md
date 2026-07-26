# V1 acceptance and upstream-merge guardrails

## Decision

V1 is accepted only when every gate in this document passes. There are no waivers. A failed gate is fixed or its owning design decision is reopened.

Acceptance uses two immutable public releases:

- `v2.0.0` / package `2.0.0.0` is the clean-install seed.
- `v2.0.1` / package `2.0.1.0` changes only the version and release metadata needed to prove automatic update.

V1 is ready for coworkers only after the separate-machine update to `v2.0.1` succeeds. Both releases remain in history and neither tag, version, nor published asset is replaced.

## Evidence record

Create one private acceptance record tied to the exact `v2.0.1` commit. It contains:

- VxFiles commit SHA and Files baseline tag/SHA;
- `git status`, bounded-diff, restore, and build results;
- tool and Windows versions;
- package and bundle manifest extracts;
- file inventories proving self-containment and rejected-artifact absence;
- SignTool verification and timestamp output;
- release asset names, byte lengths, SHA-256 hashes, and redirect results;
- the separate-machine test report for install, launch, update, and uninstall;
- branding, activation, and fork-link observations; and
- operator/user sign-off with UTC timestamps.

Secrets, PFX paths, PFX passwords, private keys, personal machine identifiers, and unrelated coworker data never enter the record.

## Gate 1: clean baseline and bounded downstream seam

The release candidate must descend from the clean Files `v4.2` tag and from the repository transition agreed in **Choose a safe repository transition to the minimal V1 tree**.

Run from the clean release worktree:

```powershell
git status --short --branch
git merge-base --is-ancestor v4.2 HEAD
git diff --name-status v4.2...HEAD
git diff --stat v4.2...HEAD
git diff --check v4.2...HEAD
```

Acceptance requires:

- an empty working tree;
- `v4.2` is an ancestor;
- no restoration, automation, portable, tag-storage, migration, or unrelated feature commit was merged or cherry-picked from the legacy line;
- every changed path appears in the final implementation specification’s path allowlist;
- every changed hunk in a mixed upstream file maps to one named V1 requirement;
- all other tracked paths are byte-equivalent to `v4.2`; and
- `docs/wayfinding` planning assets are not copied onto the clean release line.

The runtime identity seam is limited to the thirteen files fixed by **Define the public VxFiles rebranding seam**:

```text
src/Files.App/Actions/Navigation/OpenInNewWindow/BaseOpenInNewWindowAction.cs
src/Files.App/App.xaml.cs
src/Files.App/Constants.cs
src/Files.App/Files.App.csproj
src/Files.App/Helpers/Application/AppLifecycleHelper.cs
src/Files.App/Helpers/Navigation/NavigationHelpers.cs
src/Files.App/MainWindow.xaml.cs
src/Files.App/Package.appxmanifest
src/Files.App/Program.cs
src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs
src/Files.App/Utils/Taskbar/SystemTrayIcon.cs
src/Files.App/ViewModels/Settings/GeneralViewModel.cs
src/Files.App/Views/SplashScreenPage.xaml
```

Self-contained deployment adds only:

```text
src/Files.App/Properties/PublishProfiles/win-x64.pubxml
```

The final implementation specification may additionally allow the already-audited downstream documentation and the smallest manual release adapter/runbook files. Those paths must be enumerated individually; there is no wildcard directory allowance. Generated output, certificates, credentials, private logs, and release artifacts are never allowlisted repository content.

Any new path or concern blocks acceptance until the specification is updated and the user explicitly approves it.

## Gate 2: source and configuration exclusions

Targeted scans and the bounded diff must prove:

- no portable ZIP/unpackaged distribution or Inno Setup build path;
- no `WindowsPackageType=None` downstream packaging override;
- no GitHub Actions release workflow;
- no custom updater;
- no paid signing, hosting, or release dependency;
- no Automation Bar, Automation Actions, Python runtime, tagging/background fork, or fork-specific storage implementation;
- no settings/data migration or backward-compatibility layer;
- no downstream x86 or ARM64 release artifact generation;
- no broad Files-to-VxFiles namespace, project, resource, or localization rename; and
- no changes to inherited internal application, COM server, background-task, preview-handler, app-extension, persistence, or project identities outside the public rebranding seam.

Upstream x86/ARM64 project configuration remains present even though V1 publishes only x64.

## Gate 3: reproducible Release x64 build

In the x64 Visual Studio developer environment:

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
  -p:GenerateAppxPackageOnBuild=true `
  -p:UapAppxPackageBuildMode=SideloadOnly `
  -p:AppxPackageSigningEnabled=false `
  -v:quiet `
  -clp:ErrorsOnly
```

The build must succeed from a clean output state using the repository-pinned .NET 10 SDK. The release bundle is then created and signed exactly as specified by **Specify the manual V1 release and recovery runbook**.

## Gate 4: package identity, signature, and self-containment

Use MakeAppx to unbundle and unpack the final downloaded bundle. Use SignTool against the downloaded bytes, not only the local staging copy.

Acceptance requires:

| Check | Required evidence |
| --- | --- |
| Package identity | Name `VxFiles`, publisher `CN=VxFiles`, expected four-part version, architecture `x64` |
| Public executable | `VxFiles.exe` |
| Public activation | Protocol `vxfiles`, execution alias `vxfiles.exe`, startup-task display name `VxFiles` |
| Signature | `signtool verify /pa /v` succeeds against the expected certificate |
| Timestamp | RFC 3161 SHA-256 timestamp is present and falls within certificate validity |
| Public trust input | Published CER SHA-256 fingerprint matches the signing certificate |
| Framework dependencies | Zero package/framework dependency nodes and no separate dependency assets |
| .NET runtime | `coreclr.dll`, `hostfxr.dll`, and `hostpolicy.dll` are embedded |
| Windows App SDK runtime | `Microsoft.WindowsAppRuntime.dll` and `Microsoft.WindowsAppRuntime.Bootstrap.dll` are embedded |
| Architecture payload | Exactly one x64 application package; no x86/ARM64 package |
| Distribution | No portable launcher, ZIP, EXE installer, or unpackaged payload |

The Files Dev-derived icon assets must be referenced without an asset delta from `v4.2`.

## Gate 5: release and App Installer contract

Each stable release contains exactly:

```text
VxFiles.appinstaller
VxFiles-<version>-x64.msixbundle
VxFiles.Release.cer
SHA256SUMS.txt
```

For each version, prove:

- the tag, release title, commit, descriptor version, bundle version, filename, tagged URL, and checksums agree;
- `VxFiles.appinstaller` identifies itself with the permanent `releases/latest/download/VxFiles.appinstaller` URL;
- `MainBundle` uses the immutable tagged bundle URL;
- descriptor identity fields exactly match the bundle;
- `OnLaunch HoursBetweenUpdateChecks="0"` is present;
- the release is stable, not draft or prerelease, and is latest at the applicable test step;
- all anonymous GitHub redirects reach the expected nonzero bytes and filename; and
- downloaded asset hashes equal `SHA256SUMS.txt`.

GitHub’s `application/octet-stream` response is tolerated only if the separate machine completes the App Installer workflow. A MIME-related failure reopens the hosting decision; direct bundle installation cannot substitute for this gate.

## Gate 6: canonical branding and fork links

On the separate machine, verify the public identity surfaces:

- installed package, Start menu entry, App Installer UI, window title, splash screen, startup-task display name, and executable show `VxFiles`;
- `vxfiles:` activates the installed app;
- `vxfiles.exe` resolves through the execution alias;
- update/download, repository, issue/support, startup-error, and release-note links target `hoavu2025/VxFiles`;
- unchanged upstream documentation, privacy, Crowdin, Discord, internal `Files.*` names, localized feature prose, and internal extension/task identifiers are not reported as failed rebranding; and
- no public download or update route points at Files Community release assets or an expiring `release-assets.githubusercontent.com` URL.

Capture the observed destination of every clickable fork-owned link. Screenshots may demonstrate visible identity, but text URLs and package-manifest extracts remain the authoritative link/identity evidence.

## Gate 7: separate-machine install, update, and uninstall

The user performs this gate manually on a separate coworker-controlled x64 Windows machine that has never had Files/VxFiles package identity or VxFiles certificate trust installed.

Record:

- Windows edition, version, OS build, architecture, and App Installer version;
- confirmation that no `VxFiles` AppX package is installed;
- confirmation that no `CN=VxFiles` certificate exists in Local Machine or Current User trust stores; and
- the SHA-256 fingerprint independently received for `VxFiles.Release.cer`.

### Install seed `v2.0.0`

1. Download the four public assets anonymously.
2. Verify checksums and the CER fingerprint.
3. From an elevated shell, import only the CER into `Cert:\LocalMachine\TrustedPeople`.
4. Install through the stable latest `VxFiles.appinstaller` URL, not by opening the bundle.
5. Confirm installed version `2.0.0.0`, package name `VxFiles`, publisher `CN=VxFiles`, and x64 architecture.
6. Launch VxFiles and perform a bounded smoke test: open the app, navigate to a normal folder, open Settings, and return to the file view.
7. Run the branding, fork-link, protocol, and execution-alias checks.
8. Close and relaunch once to establish the on-launch update path.

### Automatic update to `v2.0.1`

1. Publish immutable stable `v2.0.1` from the same accepted code, changing only version/release metadata needed for the update proof.
2. Confirm the stable descriptor URL now returns version `2.0.1.0` and its immutable bundle URL.
3. Launch the installed `2.0.0.0` app normally.
4. Allow App Installer’s on-launch update path to complete without manually opening the new bundle.
5. Confirm installed version `2.0.1.0`, unchanged package family, and a valid signature from the same certificate.
6. Repeat the bounded launch/navigation/Settings smoke test and public identity checks.

If App Installer defers applying the update while the app is running, close the app and follow the normal App Installer completion behavior. Do not substitute `Add-AppxPackage` on the new bundle.

### Uninstall and trust cleanup

1. Uninstall VxFiles through normal Windows installed-app controls.
2. Confirm no VxFiles AppX package remains.
3. Remove the exact published certificate thumbprint from `Cert:\LocalMachine\TrustedPeople`.
4. Confirm no private key was ever present on the test machine.
5. Confirm no package-specific files remain outside normal Windows-managed package remnants; no legacy data cleanup or migration is required.

The user reports each item as pass/fail with error codes, relevant screenshots, and downloaded asset hashes. Any failure remains blocking.

## Gate 8: final no-waiver review

Before marking V1 accepted:

- reconcile the recorded commit and hashes with both published releases;
- review every changed path and mixed-file hunk once more against the final specification;
- confirm every gate has affirmative evidence rather than an assumption or a result inherited from an earlier disposable proof;
- record known non-blocking limitations only when they are already explicit V1 scope decisions; and
- obtain the user’s final acceptance sign-off.

The GitHub MIME mismatch is a known risk, not a waived failure. It remains acceptable only while the tested clean-machine workflow passes.

## Future upstream stable-tag sync

Upstream intake uses this interface:

1. Fetch tags from `https://github.com/files-community/Files.git`.
2. Select a named stable Files release tag; never sync directly from `upstream/main`, a prerelease, or an arbitrary commit.
3. Create a dedicated sync branch from the current accepted VxFiles `main`.
4. Merge the selected upstream tag with an explicit merge commit.
5. Resolve conflicts by restoring the upstream implementation first, then reapplying only still-required VxFiles allowlisted concerns.
6. Delete any downstream hunk made redundant by upstream.
7. Change the comparison baseline from the previous Files tag to the selected new tag.
8. Run the bounded-path/hunk checks and Release x64 build before promoting the sync.

Never merge a sync branch merely because conflicts are resolved. The resulting diff against the new upstream tag must still expose only the named VxFiles seam.

Every upstream sync requires the bounded-diff and build gates. A sync intended for public release also requires the package, release, and separate-machine gates appropriate to that release. The full two-release seed sequence is a one-time V1 acceptance proof; later ordinary releases prove a higher-version update from the current known-good stable release.

## Boundaries

- This document specifies evidence and does not perform implementation, publication, installation, certificate trust, or branch promotion.
- General Files functional regression testing remains upstream’s responsibility. V1 adds bounded smoke tests for the downstream identity, packaging, and release seam.
- Automation Actions, backward compatibility, non-x64 artifacts, publicly trusted signing, paid infrastructure, and portable distribution remain out of scope.
