# V1 downstream delta audit

## Scope

This audit compares the clean Files Community `v4.2` tag with:

- committed VxFiles `main` at `e5e622b7c`;
- the working tree observed on 2026-07-26; and
- the agreed V1 downstream allowlist.

It classifies the current delta only. Later packaging, signing, update, and release investigations may specify new files that do not exist in a V1-compatible form today.

## Result

The committed fork is twelve commits ahead of `v4.2` and changes 120 paths: 56 added, 62 modified, and two deleted, totaling 6,773 insertions and 732 deletions. At the time of this audit, the working tree adds another 16 tracked-path changes and six untracked paths.

The smallest candidate subset of the committed delta is:

| Disposition | Paths | Meaning |
| --- | ---: | --- |
| Keep, but rewrite | 6 | Downstream-owned identity and decision documents |
| Restore, then apply narrow hunks | 15 | Upstream application files containing both allowed and rejected changes |
| Restore or remove completely | 99 | Portable, unpackaged, automation, tagging, background, diagnostics, and installer divergence |

This reduces the candidate committed patch surface from 120 paths to 21 before the rebranding and packaging tickets refine it further. The Files Dev-derived icon requires no asset delta because it already exists in `v4.2`.

## Keep, but rewrite

These downstream-owned files remain useful, but their current portable-era contents are not V1-compatible:

- `CONTEXT.md`
- `NOTICE-VXFILES.md`
- `README.md`
- `docs/adr/0001-limit-vxfiles-renaming-to-public-identity.md`
- `docs/adr/0003-base-vxfiles-on-stable-upstream-releases.md`
- `docs/adr/0005-version-vxfiles-independently.md`

`CONTEXT.md` has already been updated to define the Installed Distribution and Downstream Layer. The other five files must be rewritten or superseded so they describe the MSIX-only V1, manual releases, and current version decision.

## Restore, then apply narrow hunks

Each file below must first return to its `v4.2` implementation. Only the listed concern may then differ.

| Path | Allowed V1 concern |
| --- | --- |
| `src/Files.App/Actions/Navigation/OpenInNewWindow/BaseOpenInNewWindowAction.cs` | VxFiles protocol string only |
| `src/Files.App/App.xaml.cs` | Executable process name only, if the assembly becomes `VxFiles` |
| `src/Files.App/Constants.cs` | Fork repository/support/update URLs and VxFiles startup-error identity |
| `src/Files.App/Files.App.csproj` | Public assembly identity and the minimum self-contained properties proven necessary |
| `src/Files.App/Helpers/Application/AppLifecycleHelper.cs` | VxFiles relaunch protocol only |
| `src/Files.App/Helpers/Navigation/NavigationHelpers.cs` | Window title suffix and VxFiles protocol strings only |
| `src/Files.App/MainWindow.xaml.cs` | Visible title plus executable/protocol recognition; keep package lifecycle behavior |
| `src/Files.App/Package.appxmanifest` | Package identity, publisher, version, display names, protocol, execution alias, and startup-task display name |
| `src/Files.App/Program.cs` | Executable or execution-alias recognition only |
| `src/Files.App/Properties/PublishProfiles/win-x64.pubxml` | The minimum self-contained x64 publish properties |
| `src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs` | VxFiles executable self-association only, if the assembly is renamed |
| `src/Files.App/Utils/Taskbar/SystemTrayIcon.cs` | VxFiles activation protocol only; package display name already supplies the tooltip |
| `src/Files.App/ViewModels/Settings/GeneralViewModel.cs` | VxFiles relaunch protocol only |
| `src/Files.App/Views/SplashScreenPage.xaml` | The visible `VxFiles` name only |
| `src/Files.App/app.manifest` | Win32 assembly identity; version remains subject to the signing/version investigation |

The public rebranding ticket must decide whether changing the protected executable from `Files.exe` to `VxFiles.exe` earns its extra source touches. Package display name, protocol, and execution alias can be rebranded independently of most internal identifiers.

The self-contained build ticket must decide whether self-containment belongs in the project, the x64 publish profile, or both. The current fork's `WindowsPackageType=None`, x64-only project topology, removed background/server integration, removed Sentry references, and automation project references are explicitly rejected.

## Restore completely to v4.2

The following 47 modified paths contain no allowed V1 behavior:

- `Directory.Packages.props`
- `Files.slnx`
- `src/Files.App/Actions/FileSystem/CopyItemFromHomeAction.cs`
- `src/Files.App/Actions/Open/OpenLogFileAction.cs`
- `src/Files.App/Actions/Open/OpenLogFileLocationAction.cs`
- `src/Files.App/Actions/Open/OpenSettingsFileAction.cs`
- `src/Files.App/Actions/Sidebar/CopyItemFromSidebarAction.cs`
- `src/Files.App/Actions/Start/PinToStartAction.cs`
- `src/Files.App/Actions/Start/UnpinFromStartAction.cs`
- `src/Files.App/Data/Commands/ActionCommand.cs`
- `src/Files.App/Data/Contracts/IFileTagsSettingsService.cs`
- `src/Files.App/Data/Enums/AppEnvironment.cs`
- `src/Files.App/Data/Factories/ContentPageContextFlyoutFactory.cs`
- `src/Files.App/Data/Items/ListedItem.cs`
- `src/Files.App/Data/Items/WindowEx.cs`
- `src/Files.App/GlobalUsings.cs`
- `src/Files.App/Helpers/Application/AppLanguageHelper.cs`
- `src/Files.App/Helpers/Layout/LayoutPreferencesDatabase.cs`
- `src/Files.App/Helpers/ResourceHelpers.cs`
- `src/Files.App/Helpers/TransferHelpers.cs`
- `src/Files.App/Services/App/AppUpdateSideloadService.cs`
- `src/Files.App/Services/App/FileTagsService.cs`
- `src/Files.App/Services/Settings/FileTagsSettingsService.cs`
- `src/Files.App/Services/Settings/GeneralSettingsService.cs`
- `src/Files.App/Services/Settings/UserSettingsService.cs`
- `src/Files.App/Services/Windows/WindowsJumpListService.cs`
- `src/Files.App/Services/Windows/WindowsStartMenuService.cs`
- `src/Files.App/Strings/en-US/Resources.resw`
- `src/Files.App/UserControls/TabBar/TabBar.xaml.cs`
- `src/Files.App/Utils/Cloud/Detector/GoogleDriveCloudDetector.cs`
- `src/Files.App/Utils/Cloud/Detector/SynologyDriveCloudDetector.cs`
- `src/Files.App/Utils/FileTags/FileTagsDatabase.cs`
- `src/Files.App/Utils/FileTags/FileTagsHelper.cs`
- `src/Files.App/Utils/Git/GitHelpers.cs`
- `src/Files.App/Utils/Shell/ShellNewMenuHelper.cs`
- `src/Files.App/Utils/Storage/Helpers/StorageItemIconHelpers.cs`
- `src/Files.App/ViewModels/Settings/AboutViewModel.cs`
- `src/Files.App/ViewModels/Settings/AdvancedViewModel.cs`
- `src/Files.App/ViewModels/Settings/SettingsPageViewModel.cs`
- `src/Files.App/ViewModels/Settings/TagsViewModel.cs`
- `src/Files.App/ViewModels/UserControls/SidebarViewModel.cs`
- `src/Files.App/Views/MainPage.xaml`
- `src/Files.App/Views/MainPage.xaml.cs`
- `src/Files.App/Views/Settings/AdvancedPage.xaml`
- `src/Files.App/Views/Settings/GeneralPage.xaml`
- `src/Files.App/Views/ShellPanesPage.xaml.cs`
- `src/Files.App/Views/SplashScreenPage.xaml.cs`

The two upstream Sentry adapters deleted by the fork must also be restored:

- `src/Files.App/Utils/Logger/SentryLogger.cs`
- `src/Files.App/Utils/Logger/SentryLoggerProvider.cs`

This classification restores upstream package identity, storage, lifecycle, clipboard, shell integration, update, diagnostics, tagging, tray, and background behavior before any narrow VxFiles hunks are applied.

## Remove downstream additions

The following 50 added paths are outside the V1 allowlist:

- `.github/workflows/portable.yml`
- `docs/PORTABLE.md`
- `docs/adr/0002-keep-portable-build-free-of-automatic-integration.md`
- `docs/adr/0004-keep-diagnostics-local.md`
- `docs/adr/0006-isolate-vxfiles-data-without-registry-storage.md`
- `docs/downstream/automation-runtime.md`
- `docs/prompts/plan-restoreTagsAndBackgroundTray.prompt.md`
- `installer/VxFiles.iss`
- `scripts/Build-Debug.ps1`
- `scripts/Build-Installer.ps1`
- `scripts/Build-Portable.ps1`
- `scripts/Publish-Release.ps1`
- `scripts/Test-PortableStartup.ps1`
- `scripts/automation/Acquire-Python.ps1`
- `scripts/automation/Run-HeadlessTracer.ps1`
- `scripts/automation/python-3.14.6-win-x64.json`
- `src/Files.App.Runtime`
- `src/Files.App/AutomationActions/vxfiles.selection-list/action.json`
- `src/Files.App/AutomationActions/vxfiles.selection-list/write_selection.py`
- `src/Files.App/Helpers/Application/VxFilesEnvironment.cs`
- `src/Files.App/Helpers/Automation/FilesAutomationHostContext.cs`
- `src/Files.App/Helpers/Automation/FilesAutomationTrustConsent.cs`
- `src/Files.App/UserControls/Automation/AutomationBar.xaml`
- `src/Files.App/UserControls/Automation/AutomationBar.xaml.cs`
- `src/Files.App/ViewModels/UserControls/AutomationBarViewModel.cs`
- `src/VxFiles.Automation.Abstractions/AutomationContracts.cs`
- `src/VxFiles.Automation.Abstractions/VxFiles.Automation.Abstractions.csproj`
- `src/VxFiles.Automation/AutomationBarSession.cs`
- `src/VxFiles.Automation/AutomationDependencyResolver.cs`
- `src/VxFiles.Automation/AutomationHostBridge.cs`
- `src/VxFiles.Automation/AutomationManifestCatalog.cs`
- `src/VxFiles.Automation/AutomationManifestValidator.cs`
- `src/VxFiles.Automation/AutomationModule.cs`
- `src/VxFiles.Automation/AutomationModuleOptions.cs`
- `src/VxFiles.Automation/AutomationOutputProtocolReader.cs`
- `src/VxFiles.Automation/AutomationProcessJob.cs`
- `src/VxFiles.Automation/AutomationPythonRunner.cs`
- `src/VxFiles.Automation/AutomationTrustFingerprint.cs`
- `src/VxFiles.Automation/DefaultAutomationPorts.cs`
- `src/VxFiles.Automation/FileAutomationStateStore.cs`
- `src/VxFiles.Automation/NativeMethods.json`
- `src/VxFiles.Automation/NativeMethods.txt`
- `src/VxFiles.Automation/PinnedAutomationPython.cs`
- `src/VxFiles.Automation/Runtime/vxfiles_automation.py`
- `src/VxFiles.Automation/Runtime/vxfiles_runner.py`
- `src/VxFiles.Automation/VxFiles.Automation.csproj`
- `tests/Files.App.Runtime.Tests`
- `tests/VxFiles.Automation.Tests/AutomationCatalogTests.cs`
- `tests/VxFiles.Automation.Tests/AutomationHostBridgeTests.cs`
- `tests/VxFiles.Automation.Tests/VxFiles.Automation.Tests.csproj`

No existing portable, Inno, automation, or GitHub release script is a suitable base for the V1 MSIX/manual-release flow. Later tickets should specify small replacement release assets instead of adapting these files.

## Working-tree overlay

The working tree was intentionally left untouched. Of the observed uncommitted paths, only the `CONTEXT.md` vocabulary update belongs to the V1 direction.

All other tracked changes are out of scope for V1:

- `Files.slnx`
- `docs/PORTABLE.md`
- `docs/adr/0006-isolate-vxfiles-data-without-registry-storage.md`
- `scripts/Build-Installer.ps1`
- `src/Files.App.Runtime`
- `src/Files.App/App.xaml.cs`
- `src/Files.App/Files.App.csproj`
- `src/Files.App/Helpers/Application/AppLifecycleHelper.cs`
- `src/Files.App/Strings/en-US/Resources.resw`
- `src/Files.App/Utils/FileTags/FileTagsDatabase.cs`
- `src/Files.App/Utils/FileTags/FileTagsHelper.cs`
- `src/Files.App/Utils/Shell/LaunchHelper.cs`
- `src/Files.App/ViewModels/UserControls/AutomationBarViewModel.cs`
- `src/Files.App/Views/MainPage.xaml`
- `tests/Files.App.Runtime.Tests`

The observed untracked automation/runtime paths are also out of scope:

- `src/Files.App.Runtime/Files.App.Runtime.csproj`
- `src/Files.App.Runtime/ProcessLaunchFailurePolicy.cs`
- `src/Files.App/Services/App/AutomationService.cs`
- `src/Files.App/Services/App/IAutomationService.cs`
- `tests/Files.App.Runtime.Tests/Files.App.Runtime.Tests.csproj`
- `tests/Files.App.Runtime.Tests/ProcessLaunchFailurePolicyTests.cs`

`git status` also reported additional modified paths for which `git diff` showed no textual delta, consistent with normalization or index-stat differences. The transition ticket must take a fresh status snapshot and preserve the entire current worktree before reconstructing V1.

## Downstream seam

The V1 Downstream Layer should be a patch seam, not a parallel application architecture:

1. restore the upstream module implementations;
2. keep public identity changes as literal or manifest substitutions at their existing seams;
3. keep packaging changes in MSBuild, manifest, and release assets;
4. do not introduce a VxFiles environment abstraction, storage adapter, lifecycle fork, or replacement update module unless a later proof shows that packaged MSIX cannot work through the upstream interface; and
5. enforce the final allowed-path list as an upstream-merge guardrail.

This maximizes locality: packaging knowledge remains in packaging files, public identity remains at presentation/activation seams, and Files behavior remains owned by upstream.

## Questions handed forward

- The public rebranding ticket must finalize executable identity and the exact literal substitutions.
- The self-contained build ticket must prove the minimal MSBuild property placement without disabling package identity, background tasks, the out-of-process server, or upstream architecture configuration.
- The signing and update tickets must define package publisher/version identity and replacement release assets.
- The repository-transition ticket must preserve all current work before constructing the minimal tree.
- The acceptance ticket should turn the final allowed-path list into a measurable divergence guardrail.
