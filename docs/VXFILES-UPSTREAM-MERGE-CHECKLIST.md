# VxFiles upstream merge checklist

## Purpose

VxFiles stays close to a tagged Files Community release, but V1 must remain an unpackaged, self-contained, per-user Velopack application. Use this checklist for every upstream stable-tag intake. Do not resolve conflicts by restoring package-identity assumptions or upstream distribution URLs.

The release runbook is in `docs/VXFILES-RELEASE.md`. The architectural baseline is in `CONTEXT.md`.

## Before merging

1. Start from a clean `main` and create a dedicated `codex/upstream-<version>` sync branch.
2. Fetch `upstream` and merge the named stable tag, not `upstream/main`.
3. Record the accepted upstream tag and the current VxFiles release tag in the merge description.
4. Preserve conflict resolutions only where this checklist identifies a downstream seam. Prefer upstream code everywhere else.
5. Never merge the archived pre-V2 implementation or Automation Actions into V1.

## Distribution and project configuration

Preserve these `src/Files.App/Files.App.csproj` decisions:

- `WindowsPackageType=None`.
- x64 is the only V1 platform and `win-x64` is the only runtime identifier.
- .NET and Windows App SDK are both self-contained.
- the assembly and visible executable are named `VxFiles`.
- the release version is supplied by the manual release script.
- the Velopack package reference remains centrally versioned.
- the Files-Dev `Logo.ico` and scale-100 splash image are copied to build and publish output.
- packaged background-task and out-of-process server project references, WinMD inputs, trimmer roots, and build targets remain excluded.

Preserve `VelopackApp.Build().Run()` as the first statement executed by `Program.Main`. No static initializer, single-instance redirect, WinRT call, settings access, or process exit may run before it.

Do not add GitHub Actions, portable ZIP output, MSIX output, certificate/PFX steps, or Store publishing to the V1 release path.

## Installed identity, paths, and persistence

`VxFilesEnvironment` is the downstream compatibility boundary. Preserve:

- display name `VxFiles`;
- install path based on `AppContext.BaseDirectory`;
- installed version discovery through Velopack with assembly-version fallback;
- application data under `%LocalAppData%\VxFiles Community\VxFiles`;
- temporary data beneath that application-data root;
- the cross-process JSON state store and named mutex;
- the VxFiles single-instance semaphore and active-process state;
- process relaunch through the physical executable;
- physical reading of trusted `ms-appx:///` application-resource text from the self-contained install directory.

The internal Velopack package ID is `VxFilesApp`, so the installer owns `%LocalAppData%\VxFilesApp`. Do not move user data into that directory or back into the legacy `%LocalAppData%\VxFiles` directory.

Do not reintroduce direct `ApplicationData.Current` or `Package.Current` access in `src/Files.App`. Route local folders, settings, version, install path, and state through `VxFilesEnvironment`.

## Branding and fork ownership

Preserve:

- window, About page, splash, tray, installer title, and executable branding as VxFiles;
- the approved Files-Dev icon for both the window and system tray;
- repository, issues, support, release notes, download, and update URLs under `https://github.com/hoavu2025/VxFiles`;
- the Velopack GitHub update source pointed at `hoavu2025/VxFiles`;
- the existing Files Community copyright and MIT notices where inherited code requires them.

Inherited `Files.*` namespaces, internal project names, persistence identifiers, and interoperability names should remain unchanged unless VxFiles functionality requires otherwise. Renaming them increases upstream merge conflicts.

## Unpackaged compatibility guards

Preserve these fixes even when upstream rewrites nearby startup or resource code:

- Sentry initializes only when its CI-injected DSN is a valid absolute HTTP or HTTPS URI. Manual V1 builds leave the upstream placeholder unresolved and must continue without telemetry.
- XAML `ResourceString` uses `Microsoft.Windows.ApplicationModel.Resources.ResourceManager`, not the package-only `Windows.ApplicationModel.Resources.ResourceLoader`.
- language discovery uses `ApplicationLanguages.Languages` plus `en-US`, not `ApplicationLanguages.ManifestLanguages`.
- the preferred language is stored through `VxFilesEnvironment`, not `ApplicationLanguages.PrimaryLanguageOverride`.
- app resource JSON used by Properties and the Details pane is read through `VxFilesEnvironment.ReadAppResourceTextAsync`; `StorageFile.GetFileFromApplicationUriAsync` throws for these `ms-appx` URIs without package identity.
- splash loading uses the physical scale-100 image shipped in publish output and must always transition to the main UI on both image success and failure.
- the application icon path always resolves to the published Files-Dev icon.

Known regression signatures:

- Endless splash: check Sentry placeholder parsing, package-only resource/language APIs, and missing published icon paths.
- Details pane works for folders but spins for files: check that property-list JSON is read through the physical app-resource seam and that `LoadBasicPreviewAsync` does not swallow an exception while leaving `PreviewPaneState` at `LoadingPreview`.

## Package-only feature omissions

V1 deliberately omits or disables:

- MSIX startup tasks;
- Jump List initialization;
- packaged protocol and file associations;
- packaged COM activation and Files.App.Server integration;
- packaged background tasks;
- packaged clipboard package-family metadata;
- packaged launcher replacement and default-file-manager registration;
- package-dependent shell integration shown on Advanced settings;
- Automation Actions.

When upstream changes one of these areas, merge the source when harmless but keep its entry point hidden, disabled, guarded, or excluded from the V1 build until an unpackaged implementation is explicitly approved.

## Velopack release invariants

Preserve `scripts/release/Build-VxFilesRelease.ps1` and its invariants:

- manual local build only;
- pinned Velopack CLI version;
- self-contained Release x64 publish;
- package ID `VxFilesApp`, title `VxFiles`, main executable `VxFiles.exe`;
- Start menu shortcut;
- `--noPortable true`;
- immutable, version-specific artifact directories;
- refusal to reuse an existing Git tag;
- verification that installer, update manifests, and every metadata-referenced asset exist;
- upload every file from the generated `release` directory without renaming it.

Normal GitHub releases are stable, non-draft, and non-prerelease so the in-app updater can discover them.

## Required post-merge checks

Run a focused Release x64 build, then a complete release-candidate publish. Verify:

1. `rg -n "ApplicationData\\.Current|Package\\.Current" src/Files.App` returns no matches.
2. `VelopackApp.Build().Run()` remains the first executed startup statement.
3. Direct Release, published, and installed executables leave the splash and expose the main tab UI.
4. A standard user installs and upgrades without elevation.
5. No portable asset is generated.
6. Every filename in `assets.win.json` exists in the release directory.
7. Settings and logs remain outside the Velopack install root.
8. About, window, tray, installer, links, and update source identify VxFiles and the fork.
9. Details pane loading completes for both one folder and one ordinary file.
10. Package-only Advanced settings remain unavailable.
11. `git diff --check` passes and all changed text files use CRLF.

Before publishing, test the installer on a separate standard-user Windows machine. Treat an endless splash, a details spinner, missing icon, elevation prompt, portable output, or upstream download link as a release blocker.
