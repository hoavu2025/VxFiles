# Minimal upstream-aligned VxFiles V1 refactor specification

## Problem Statement

VxFiles currently diverges too far from Files Community. Its committed line is twelve commits beyond Files `v4.2`, changes 120 paths, and mixes public rebranding with portable distribution, installer automation, Automation Actions, Python runtime, tagging/background work, storage/lifecycle changes, and other fork-specific behavior. The current working tree adds more unfinished work.

That breadth makes future Files fixes and features difficult to merge safely. It also does not yet provide the desired distribution: a free, installable, self-contained x64 Windows package downloaded and automatically updated from the VxFiles GitHub fork.

V1 must return VxFiles to a small downstream patch over Files `v4.2`, preserve the public VxFiles identity and fork links, and deliver a manually released MSIX/App Installer chain without paid services or GitHub Actions. Existing legacy work must not be lost while the clean line is constructed.

## Solution

Construct a new clean implementation line directly from the Files `v4.2` tag in a separate worktree. Preserve the existing public line and all uncommitted work independently. Reapply only a strict 21-path downstream allowlist:

- six VxFiles-owned identity and decision documents;
- thirteen existing application files at public identity/activation/title/link seams;
- the inherited x64 publish profile for self-contained deployment; and
- one VxFiles-owned manual release document.

Keep upstream modules, project topology, internal `Files.*` identifiers, localized feature prose, background tasks, server integration, package lifecycle, storage, diagnostics, and non-x64 project configuration unchanged. Use the existing upstream package and bundle tooling. Publish only a signed, self-contained x64 MSIX bundle, App Installer descriptor, public certificate, and checksums.

Use one protected three-year self-signed release certificate with subject `CN=VxFiles`. Coworkers perform a one-time elevated trust import of its public CER into Local Machine `Trusted People`. GitHub Releases hosts the stable descriptor and immutable versioned bundles. The first installable release is `v2.0.0`; `v2.0.1` proves automatic update on a separate machine before V1 is accepted.

## User Stories

1. As a VxFiles user, I want to install the app through Windows App Installer, so that I receive a normal installed application rather than a portable archive.
2. As a VxFiles user, I want the installed app to identify itself as VxFiles, so that it is distinct from Files.
3. As a VxFiles user, I want Start, Installed Apps, App Installer, the splash screen, and window titles to show VxFiles, so that the public identity is consistent.
4. As a VxFiles user, I want the executable to be named `VxFiles.exe`, so that command and process surfaces use the fork identity.
5. As a VxFiles user, I want `vxfiles:` activation to open VxFiles, so that protocol-based launches target the fork.
6. As a VxFiles user, I want `vxfiles.exe` to work as the execution alias, so that command-line activation uses the fork name.
7. As a VxFiles user, I want support, issue, source, download, update, and release-note links to target `hoavu2025/VxFiles`, so that I never obtain fork binaries from Files Community.
8. As a VxFiles user, I want inherited Files documentation and community resources to remain available where appropriate, so that upstream feature guidance is not unnecessarily duplicated.
9. As a VxFiles user, I want the app to include its .NET 10 runtime, so that I do not install .NET separately.
10. As a VxFiles user, I want the app to include its Windows App SDK runtime, so that no framework dependency package is required.
11. As a VxFiles user, I want updates to arrive through Windows App Installer from the VxFiles GitHub releases, so that updates use the normal installed-app mechanism.
12. As a VxFiles user, I want every published update to have a higher immutable version, so that Windows can update predictably and releases remain auditable.
13. As a VxFiles user, I want the package signature and public certificate fingerprint to be verifiable, so that I can confirm who produced the package.
14. As a coworker, I want a clear one-time certificate trust procedure, so that I can install the free self-signed V1 safely.
15. As a coworker, I want only the public CER, never the private PFX, so that I cannot accidentally receive signing capability.
16. As a release owner, I want the PFX encrypted and stored outside Git, so that publishing the source cannot expose the release key.
17. As a release owner, I want a manual three-checkpoint release process, so that incomplete or unverified assets never become the stable update source.
18. As a release owner, I want publication recovery rules, so that a failed release is contained without replacing signed bytes or reusing a version.
19. As a maintainer, I want the VxFiles application delta limited to existing upstream seams, so that future Files changes remain mergeable.
20. As a maintainer, I want every downstream path and hunk justified by a named requirement, so that unrelated fork features cannot quietly re-enter V1.
21. As a maintainer, I want upstream x86 and ARM64 configuration preserved, so that the fork does not create avoidable project-file conflicts even though V1 publishes only x64.
22. As a maintainer, I want future upstream intake to use stable Files tags, so that sync points are explicit and reproducible.
23. As a maintainer, I want redundant downstream hunks removed when upstream gains equivalent behavior, so that the fork becomes smaller over time.
24. As the owner of the current WIP, I want it archived privately before refactoring, so that the clean V1 work cannot destroy unfinished experiments.
25. As the repository owner, I want the old public history retained under a legacy branch, so that the clean default line does not erase audit history.
26. As the repository owner, I want the clean line promoted only after acceptance, so that an incomplete refactor does not replace `main`.
27. As a V1 user, I do not want portable ZIP, Inno Setup, Automation Actions, Python automation, or migration behavior, so that the first release remains small and dependable.
28. As a future VxFiles user, I want Automation Actions to be designed separately as a filterable third right-pane tab, so that V1 is not delayed by an unfinished feature.

## Implementation Decisions

### Downstream seam

- V1 is a patch seam over Files `v4.2`, not a parallel application architecture.
- Existing upstream modules remain the implementation. Public identity changes use their current manifest, MSBuild, activation, title, and link interfaces.
- No VxFiles environment abstraction, storage adapter, replacement lifecycle, replacement update module, or branding module is introduced.
- The interface for mergeability is the complete diff against the selected Files stable tag. A change outside the allowlist, or an unexplained hunk inside it, is a failure.
- Internal `Files.*` namespaces, projects, libraries, solution structure, application id, extension names, background entry points, COM identities, and persistence ids remain upstream values.

### Repository transition

- Preserve the existing dirty working tree before implementation.
- Inspect all current uncommitted paths for secrets and generated/oversized artifacts.
- Snapshot retained WIP on a dedicated local-only archive branch based on the current `main`.
- Create an external Git bundle containing that archive ref as a second recovery copy.
- Do not push unreviewed WIP to the public repository.
- Create a separate worktree and clean implementation branch directly from `v4.2`.
- Do not merge or wholesale cherry-pick the twelve existing downstream commits.
- After acceptance, preserve the currently published `main` tip as `legacy/pre-v2`, then promote the clean accepted line to `main`.
- Existing tags `v1.0.0` through `v1.0.2` and their releases remain unchanged.

### Public VxFiles identity

- Public product, package display, publisher display, window, splash, startup-task, and startup-error identity is `VxFiles`.
- Main executable and execution alias are `VxFiles.exe` and `vxfiles.exe`.
- Package identity name is `VxFiles`.
- Protocol is `vxfiles`.
- Package publisher and certificate subject are exactly `CN=VxFiles`.
- The existing Files Dev icon family is reused byte-for-byte.
- Runtime source, issue, support, release, download, and update links target `https://github.com/hoavu2025/VxFiles`.
- Upstream documentation, Discord, privacy, and Crowdin links remain Files Community links.
- Upstream localized product prose remains unchanged; V1 rebrands canonical identity surfaces only.

### Package and runtime

- Keep the inherited single-project MSIX topology, app server, background tasks, project references, platforms, runtime identifiers, and Windows packaging behavior.
- Set only the existing x64 publish profile to .NET self-contained and Windows App SDK self-contained.
- Retain upstream x86 and ARM64 project configuration.
- Publish only an x64 artifact for V1.
- Restore the full solution before packaging so the out-of-process server is restored.
- Keep the inherited bundle-building script unchanged and use it to wrap the one x64 MSIX.
- Generate the GitHub-oriented descriptor and flat release layout manually through the release document.

### Package identity and signing

- The first installable line begins at tag `v2.0.0` and package version `2.0.0.0`; V1 is the scope name.
- Package versions use `MAJOR.MINOR.PATCH.0`, always increase, and never reuse published values.
- Generate one RSA-3072, SHA-256, code-signing-only, non-CA certificate for `CN=VxFiles` with a three-year lifetime.
- Protect the exported PFX with AES-256 and a unique password stored separately.
- Keep the PFX outside every repository/worktree on an owner-restricted, BitLocker-protected volume, with one separately encrypted offline backup.
- Publish only the CER. Coworkers trust it in `Cert:\LocalMachine\TrustedPeople`.
- Import the PFX temporarily into `Cert:\CurrentUser\My` for final-bundle signing, use its thumbprint with SignTool, and remove it in `finally`.
- Sign with SHA-256 and an RFC 3161 SHA-256 timestamp.
- Begin certificate rotation six months before expiry and pre-trust the new CER before using the new key.

### GitHub App Installer delivery

- GitHub Releases is the only V1 download/update host.
- The permanent descriptor URL is `https://github.com/hoavu2025/VxFiles/releases/latest/download/VxFiles.appinstaller`.
- Every descriptor references an immutable tagged bundle URL.
- The descriptor uses the 2018 schema, `MainBundle`, exact package identity fields, and `OnLaunch HoursBetweenUpdateChecks="0"`.
- Each release has exactly four distribution assets: stable-name descriptor, versioned x64 bundle, stable-name public CER, and checksum file.
- Assemble and verify a draft first. Publish it as stable/latest only after all bytes and metadata pass.
- GitHub’s octet-stream response remains a known risk and is acceptable only after the separate-machine App Installer proof.
- Never replace a published asset, move a published tag, or reuse a published package version. Recover with a higher version.

### Acceptance

- Every acceptance item is blocking; there are no waivers.
- `v2.0.0` is the public clean-install seed.
- `v2.0.1` changes only required version/release metadata and is the public automatic-update proof.
- V1 is accepted only after a separate clean x64 Windows machine installs `v2.0.0` through the stable descriptor URL and automatically updates to `v2.0.1`.
- The user performs the separate-machine checklist and returns pass/fail evidence, errors, relevant screenshots, versions, and asset hashes.
- Direct bundle installation cannot substitute for App Installer delivery.

### Future upstream intake

- Fetch and merge only named Files stable tags, never `upstream/main`, prereleases, or arbitrary commits.
- Create a dedicated sync branch from accepted VxFiles `main`.
- Use an explicit merge commit.
- Resolve conflicts by restoring upstream behavior first, then reapplying only still-required downstream concerns.
- Remove downstream hunks made redundant by upstream.
- Compare the result against the new Files tag and pass bounded-diff/build gates before promotion.

## File-Level Implementation Contract

The release line may differ from Files `v4.2` in exactly the paths below. No directory wildcard is permitted.

### Downstream-owned documents

| Path | Required change |
| --- | --- |
| `CONTEXT.md` | Define Installed Distribution, Downstream Layer, public VxFiles identity, retained internal Files identity, and stable-tag intake. |
| `NOTICE-VXFILES.md` | State independent-fork status, Files Community attribution/source, VxFiles source, and current upstream base. |
| `README.md` | Describe VxFiles, Files `v4.2` base, MSIX-only installation, one-time CER trust, GitHub fork downloads/updates, manual build/release, and upstream attribution. Remove portable/Inno/Actions instructions. |
| `docs/adr/0001-limit-vxfiles-renaming-to-public-identity.md` | Record the exact public-identity seam and unchanged Files Dev icon family. |
| `docs/adr/0003-base-vxfiles-on-stable-upstream-releases.md` | Record clean `v4.2` baseline and future explicit stable-tag sync branches. |
| `docs/adr/0005-version-vxfiles-independently.md` | Record independent `vMAJOR.MINOR.PATCH` / package `MAJOR.MINOR.PATCH.0`, beginning at `v2.0.0`. |
| `docs/VXFILES-RELEASE.md` | Provide the manual build, bundle, signing, descriptor, checksum, draft, publication, clean-machine verification, rollback, and certificate-recovery runbook. |

### Existing application files

| Path | Only permitted concern |
| --- | --- |
| `src/Files.App/Actions/Navigation/OpenInNewWindow/BaseOpenInNewWindowAction.cs` | Replace the Files Dev activation protocol literal with `vxfiles`. |
| `src/Files.App/App.xaml.cs` | Recognize the `VxFiles` process/executable name only. |
| `src/Files.App/Constants.cs` | Fork repository, issue, support, update/release URLs and VxFiles startup-error identity only. |
| `src/Files.App/Files.App.csproj` | Set assembly name and matching trimmer root to `VxFiles`; leave topology/default deployment properties upstream. |
| `src/Files.App/Helpers/Application/AppLifecycleHelper.cs` | Replace the relaunch protocol literal with `vxfiles`. |
| `src/Files.App/Helpers/Navigation/NavigationHelpers.cs` | Replace activation protocol literals and visible window-title suffix only. |
| `src/Files.App/MainWindow.xaml.cs` | Visible title plus VxFiles executable/protocol recognition only; retain upstream lifecycle and `FilesMainWindow` persistence id. |
| `src/Files.App/Package.appxmanifest` | Set package name/publisher/version, public display names, protocol, execution alias, and startup-task display name only. |
| `src/Files.App/Program.cs` | Recognize the VxFiles executable/execution alias only. |
| `src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs` | Use `VxFiles.exe` only where the app associates itself with supported archives; do not add portable ZIP distribution. |
| `src/Files.App/Utils/Taskbar/SystemTrayIcon.cs` | Replace the activation protocol literal only. |
| `src/Files.App/ViewModels/Settings/GeneralViewModel.cs` | Replace the relaunch protocol literal only. |
| `src/Files.App/Views/SplashScreenPage.xaml` | Change the hard-coded visible product-name run only; retain upstream image and code-behind. |

### Existing deployment file

| Path | Only permitted concern |
| --- | --- |
| `src/Files.App/Properties/PublishProfiles/win-x64.pubxml` | Set `SelfContained=true` and `WindowsAppSDKSelfContained=true`. |

### Explicitly unchanged or absent

- `src/Files.App/app.manifest` remains byte-equivalent to `v4.2`.
- `Files.slnx`, project names, namespaces, package server/background integration, Sentry adapters, storage, tags, shell operations, settings, and localized `.resw` files remain `v4.2`.
- `src/Files.App/Assets/AppTiles/Dev` remains byte-equivalent to `v4.2`.
- Upstream `.github/scripts/Create-MsixBundle.ps1` remains byte-equivalent to `v4.2`.
- Upstream `.github/scripts/Configure-AppxManifest.ps1` and `Generate-SelfCertPfx.ps1` are not used or edited for the release.
- All 99 paths classified for restoration/removal by the downstream delta audit are restored to `v4.2` or absent.
- Planning files under `docs/wayfinding` remain on the preserved planning/legacy line and are not copied to the clean release line.

## Ordered Implementation Plan

1. **Protect legacy work.** Re-read repository safety instructions; capture a fresh status; scan every current uncommitted path (including planning assets) for secrets/generated artifacts; create a local-only WIP archive branch and external Git bundle. Verify both refs before proceeding.
2. **Create the clean line.** Add a separate worktree and implementation branch at tag `v4.2`. Do not modify the existing dirty worktree.
3. **Establish the guardrail.** Record the exact 21-path allowlist and fail the work if any other path changes.
4. **Rewrite downstream documents.** Recreate the seven downstream-owned documents from the decisions in this spec. Do not copy portable-era wording.
5. **Apply public identity.** Starting from upstream bytes, make only the listed literal, manifest, executable, title, and URL changes in the thirteen application files.
6. **Apply self-containment.** Add the two switches only to the x64 publish profile.
7. **Review the complete delta.** Confirm all other paths match `v4.2`, all mixed-file hunks map to a requirement, Dev assets are unchanged, and forbidden features/scripts are absent.
8. **Restore and build.** Restore `Files.slnx`, build Release x64 through the existing package topology, and inspect errors only.
9. **Create release identity once.** Generate/protect/export the release PFX and CER outside the worktree, record the public fingerprint, remove the generated private-key store entry, and establish the release workstation’s public trust.
10. **Build release seed.** Use the manual release document to build the unsigned intermediate, wrap the x64 bundle, import the PFX temporarily, sign/timestamp the final bundle, remove the key, inspect identity/self-containment, create the descriptor/CER/checksum layout, and verify locally.
11. **Publish `v2.0.0`.** Create an explicit annotated tag for the accepted commit, upload exactly four assets to a draft, re-download and verify it, then publish stable/latest.
12. **Run clean install proof.** The user imports only the CER on the separate machine, installs through the stable descriptor URL, launches, checks branding/links/activation, and reports evidence.
13. **Publish `v2.0.1`.** Change only version and release metadata required for monotonic update, repeat the build/sign/draft checks, then publish stable/latest.
14. **Run automatic update proof.** The user launches the installed `2.0.0.0` package, proves App Installer updates it to `2.0.1.0`, repeats the smoke/identity checks, uninstalls, removes trust, and reports evidence.
15. **Accept or recover.** If every no-waiver gate passes, record final acceptance. Otherwise quarantine as specified and recover only through a higher version or a reopened design.
16. **Promote clean history.** Preserve the old public `main` as `legacy/pre-v2`, promote the accepted clean line to `main`, and retain all old tags/releases and the local WIP archive.

## Testing Decisions

- Test at the highest useful seam: the signed App Installer descriptor and bundle as consumed by Windows on a separate machine.
- Treat the complete diff against `v4.2` as the internal interface for upstream alignment.
- Do not add tests for inherited Files implementation; upstream behavior remains upstream-owned.
- Follow repository policy: no current AI-suitable automated test suite is required, but the focused Release x64 build must succeed.
- Static verification must inspect the downloaded bundle/package manifests, signature, timestamp, certificate, dependency declarations, embedded runtimes, architecture, asset hashes, and redirects.
- Public identity verification covers Start/Installed Apps/App Installer, executable, titles, splash, startup task, protocol, alias, startup errors, fork links, and unchanged inherited community links.
- The separate-machine test starts without VxFiles package/certificate state, trusts only the CER, installs `v2.0.0` through the stable App Installer URL, launches and navigates, then automatically updates to `v2.0.1`.
- The update test cannot use direct `Add-AppxPackage` on the new bundle.
- Uninstall must remove the package; the tester then removes the exact CER trust entry and confirms no private key was present.
- Every gate is pass/fail and blocking. GitHub’s octet-stream response is not waived; it is accepted only through successful external behavior.

## Acceptance Criteria

1. The clean release line differs from Files `v4.2` in no more than the exact 21 paths in this specification.
2. Every changed hunk maps to a named implementation decision.
3. All 99 rejected committed paths and all rejected WIP are restored/absent from the clean line.
4. Release x64 restore/build succeeds through the inherited topology.
5. The signed bundle contains one x64 `VxFiles` package with `VxFiles.exe`, publisher `CN=VxFiles`, expected version, protocol, and alias.
6. The bundle embeds .NET 10 and Windows App SDK runtime files and declares no framework-package dependency.
7. SignTool verifies the expected signer and RFC 3161 timestamp.
8. Each release contains exactly the descriptor, versioned bundle, CER, and checksum assets with matching hashes and identity.
9. All fork-owned public URLs target `hoavu2025/VxFiles`; no binary/update route targets official Files releases.
10. A separate clean machine installs `v2.0.0` through the stable GitHub descriptor URL.
11. Normal launch causes App Installer to update that installation to `v2.0.1`.
12. Branding, fork links, protocol, alias, launch/navigation, uninstall, and CER cleanup pass on the separate machine.
13. The current public history, old tags/releases, and local WIP archive remain recoverable.
14. The clean line is promoted to `main` only after all criteria pass.

## Out of Scope

- Portable ZIP or unpackaged distribution.
- Inno Setup or another EXE installer.
- GitHub Actions.
- Paid signing, hosting, build, or release services.
- Publicly trusted signing for V1.
- x86 or ARM64 V1 release artifacts.
- Automation Actions implementation, including the future filterable third right-pane tab.
- Automation Bar, Python automation runtime, tagging/background fork features, portable lifecycle/storage behavior, or other legacy experiments.
- Backward compatibility, settings migration, or data preservation from Files or older VxFiles distributions.
- Mass rebranding of localized upstream feature prose.
- Renaming internal Files projects, namespaces, libraries, extensions, tasks, COM identities, persistence ids, or solution structure.
- Constraints unique to Files releases after `v4.2`; later stable-tag syncs handle them.

## Further Notes

- This specification is the destination of the [V1 Wayfinder map](https://github.com/hoavu2025/VxFiles/issues/1); its closed child tickets retain the detailed proofs and rationale.
- GitHub serves release assets as `application/octet-stream`, while Microsoft documents specific App Installer media types. The installed Windows stack accepted descriptor traversal in the disposable proof, but the separate-machine test remains authoritative.
- The self-signed model is deliberately limited to a small coworker cohort. Trust installation and removal require administrator access.
- The PFX is the continuity secret. Losing or compromising it requires a pre-trusted replacement certificate and proven higher-version rotation; there is no public CA revocation service.
- Existing portable releases `v1.0.0` through `v1.0.2` remain historical. They are not installation/update sources for the new line.
- The map’s investigation documents remain useful implementation context but are intentionally excluded from the clean release diff.
