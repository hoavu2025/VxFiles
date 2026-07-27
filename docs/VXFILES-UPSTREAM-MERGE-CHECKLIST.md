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

`Files.App` references `VxFiles.Automation` and imports `AutomationPayload.props` without setting `AutomationPayloadOwner`. The reference carries the Automation payload into publish; the import exists so the pinned-runtime publish guard runs in the project that publishes. Dropping either one silently produces a release that cannot run Automation Actions, and setting `AutomationPayloadOwner` in both projects gives one publish path two sources.

## Automation Tools host seam

The Info Pane is where VxFiles-owned automation meets inherited Files code, so upstream changes to `InfoPane.xaml` conflict here. Preserve:

- `InfoPaneTabs.Tools` as the third enum member, its `ToggleToolsPaneAction`, and the third tab selector in `InfoPane.xaml`. Upstream has two tabs; a merge that restores the upstream enum silently drops the tab and resets everyone's persisted selection to Details.
- the `ToolsPaneHost` grid and its three `SelectedTab` visual states. The host is always present so the states can target it, while `AutomationToolsPane` uses `x:Load` so the headless session is opened only when Tools is first selected.
- `AutomationToolsPane` and `AutomationToolsViewModel` as the only Tools code. `InfoPaneViewModel` stays responsible for Details and Preview alone, so automation refreshes cannot interfere with preview loading.
- `IAutomationSessionService` as the single owner of the process-wide `IAutomationSession`, including the bundled and user package roots it derives from `VxFilesEnvironment`, and the three host ports it supplies to `AutomationModule.OpenAsync`. Dropping to the single-argument overload silently reinstates the module's safe defaults, which deny every trust prompt and reject every result intent, so every run would fail without the UI saying why.
- the `await Ioc.Default.GetRequiredService<IAutomationSessionService>().DisposeAsync()` call in `App.Window_Closed`. It is what stops a running action's process tree from outliving the window; without it a script keeps working on the user's files after VxFiles appears to have closed.
- `IAutomationHostContext` and `AutomationHostContext` as the only readers of live Files state on the automation path. Everything downstream works from the captured `SelectionSnapshot`, so a second reader would let a run start against a folder the user had already left.
- `AutomationResultRouter` as the only code that turns an action's declared intents into shell behaviour, including its refusal to act once the captured folder is no longer the active one.

Automation package discovery, validation, filtering, and selection admission stay in `VxFiles.Automation` and `VxFiles.Automation.Abstractions`. Do not move any of it into `Files.App` to resolve a conflict. In particular `AutomationSelectionRules` is shared by the Run button and by `AutomationSession`: reimplementing either side in `Files.App` is how a button starts promising runs the session will refuse.

### Automation touch points

Files upstream also owns, so a merge can conflict in them. Each carries VxFiles automation code that must survive:

| File | What it carries |
| --- | --- |
| `Files.slnx` | the only registration of `VxFiles.Automation`, `VxFiles.Automation.Abstractions`, and `VxFiles.Automation.Tests`. Resolving this file by taking upstream drops all three projects from the solution |
| `src/Files.App/Data/Enums/InfoPaneTabs.cs` | the `Tools` member |
| `src/Files.App/UserControls/Pane/InfoPane.xaml` | the tab selector, `ToolsPaneHost`, and three visual states |
| `src/Files.App/Actions/Show/ToggleToolsPaneAction.cs` | the generated `Commands.ToggleToolsPane` |
| `src/Files.App/App.xaml.cs` | session disposal in `Window_Closed` |
| `src/Files.App/Helpers/Application/AppLifecycleHelper.cs` | the `IAutomationSessionService`, `IAutomationHostContext`, and `AutomationToolsViewModel` registrations |
| `src/Files.App/Strings/en-US/Resources.resw` | every `Automation*` string |
| `src/Files.App/Files.App.csproj` | the `VxFiles.Automation` reference and the `AutomationPayload.props` import |

Wholly VxFiles-owned. These cannot conflict, but a merge that resolves by taking upstream wholesale will delete them:

- `src/VxFiles.Automation/` and `src/VxFiles.Automation.Abstractions/` — the headless module and its contracts, including `Runtime/*.py` and `BundledPackages/`.
- `src/Files.App/Services/Automation/` — the four host adapters: session ownership, host context, trust consent, result routing.
- `src/Files.App/Data/Contracts/IAutomation*.cs`, `Data/Items/Automation*.cs`, `Data/Enums/Automation*.cs`, `Data/TemplateSelectors/AutomationToolsItemTemplateSelector.cs`.
- `src/Files.App/UserControls/Pane/AutomationToolsPane.xaml{,.cs}`, `ViewModels/UserControls/AutomationToolsViewModel.cs`.
- `src/Files.App/Dialogs/AutomationTrustDialog.xaml{,.cs}` and `ViewModels/Dialogs/AutomationTrustDialogViewModel.cs`.
- `src/Files.App/Extensions/AutomationLabelExtensions.cs` — the only place Automation enums become user-visible text.
- `scripts/automation/` — runtime acquisition, its pinned hash manifest, and the headless tracer.
- `tests/VxFiles.Automation.Tests/`.

`src/Files.App.Controls/**/*AutomationPeer.cs` are inherited UI Automation peers and have nothing to do with this feature. Take upstream for those.

## Branding and fork ownership

Preserve:

- window, About page, splash, tray, installer title, and executable branding as VxFiles;
- the approved Files-Dev icon for both the window and system tray;
- repository, issues, support, release notes, download, and update URLs under `https://github.com/hoavu2025/VxFiles`;
- the Velopack GitHub update source pointed at `hoavu2025/VxFiles`;
- automatic updating: `SideloadUpdateService.DownloadMandatoryUpdatesAsync` fetches the release in the background and `IUpdateService.ApplyPendingUpdateOnExit`, called at the end of `App.Window_Closed`, hands it to the updater during teardown. Upstream Files only lights up the toolbar button and waits for a click, so a merge that reverts either half silently removes automatic updates;
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
- per-action settings and external-tool configuration. Automation Actions run from the Tools tab, but an action that declares settings or an external tool gets whatever the state store already holds; there is no UI to change it yet.

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
