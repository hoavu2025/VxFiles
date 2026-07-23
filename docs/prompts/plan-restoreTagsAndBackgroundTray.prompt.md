## Plan: Restore Tags and Background Tray

Keep the ZIP and Inno distributions on one unpackaged WinUI runtime; restore file tags, the Win32 system tray, and Leave App Running in the first delivery. All three work without elevation or MSIX identity. Inno still does not justify restoring package APIs such as `StartupTask`, `SecondaryTile`, manifest activation, or packaged background tasks.

**Steps**

### Phase 1: Restore file tags
1. Replace the no-op `FileTagsDatabase` with package-independent JSON persistence at `%LOCALAPPDATA%\VxFiles\filetags.db`, retaining the existing `TaggedFile[]` import/export shape used by `AdvancedViewModel`. Implement case-insensitive path lookup, FRN fallback, path/FRN upsert and removal, boundary-safe descendant queries, duplicate elimination, immutable result snapshots, malformed-file fallback/logging, and atomic temp-file replacement. Serialize read-modify-write operations under an in-process lock and a named per-user mutex.
2. Restore the upstream tag startup path in `AppLifecycleHelper`: initialize `App.FileTagsManager` when its sidebar section is enabled and run `FileTagsHelper.UpdateTagsDb()` in existing non-critical background startup work. Preserve FRN/ADS reconciliation and keep it off the shell-critical startup path. *Depends on step 1.*
3. Restore tag settings in `GeneralSettingsService`: `ShowFileTagsWidget` remains opt-in (`Get(false)`), while `ShowFileTagsSection` and `ShowEditTagsMenu` use persisted values with upstream defaults (`Get(true)`).
4. Restore tag discoverability and controls: add `TagsPage` back to settings navigation and search; add the File Tags widget and Edit Tags context-menu cards back to General settings. Do not restore Pin to Start. Reuse the existing tags page, manager, sidebar/widget, context menu, search, file-operation hooks, ADS writer, and settings import/export callers. *Parallel with step 3 after step 1 defines behavior.*

### Phase 2: Restore tray and background lifecycle
5. Restore `LeaveAppRunning` and `ShowSystemTrayIcon` as normal persisted settings in `GeneralSettingsService`, both defaulting to `false` for ZIP and Inno builds. This preserves the portable zero-integration default and requires an explicit user choice.
6. Expose only the Leave App Running and System Tray Icon cards in `AdvancedPage.xaml`; remove their dependency on `SupportsPackageIntegrations`, while leaving Startup, default Explorer, and Open/Save dialog controls guarded. The existing `AdvancedViewModel` bindings can be reused unchanged.
7. Create the existing Win32 `SystemTrayIcon` whenever app services and the main window are ready, regardless of package identity; call `Show()` only when the persisted setting is enabled. Keep `AppLifecycleHelper.GeneralSettingsService_PropertyChanged` as the live show/hide path so changing the toggle does not require restart. Dispose and clear the tray object during final teardown as the code already intends.
8. Fix background activation at its controlling abstraction: define one VxFiles instance semaphore name in `VxFilesEnvironment` (or an equivalently central runtime helper) and use it in `Program`, both sleep/resume branches in `App.xaml.cs`, and tray reopen/restart/Quit actions. Today those paths mix `Files-…-Instance` and `VxFiles-…-Instance`, which would prevent the restored background process from waking or quitting reliably. *Blocks lifecycle validation.*
9. Validate close behavior in both modes: with Leave App Running off, closing performs normal teardown and removes the tray icon; with it on, closing hides/unloads the window while retaining the process, a new launch or tray left-click wakes the same process, and tray Quit sets `ForceProcessTermination`, releases the shared semaphore, disposes the tray window, and exits. Preserve the existing first-use background notification. Do not add startup-at-login behavior.

### Phase 3: Documentation and focused validation
10. Update `docs/PORTABLE.md` and ADR 0006 to document the local tag index and ADS limitation. Also document that tray/background operation is optional, defaults off, creates no persistent Windows registration, and does not imply startup-at-login. Add tag and tray lifecycle checks to the smoke guidance. *Parallel with phases 1-2.*
11. Build `src/Files.App/Files.App.csproj` for Debug x64 with restore and errors-only logging. Repair only failures caused by this restoration and rerun the same build.
12. Run the portable startup smoke test against a fresh staged build with Leave App Running off, then manually run the non-admin tag and tray matrices below. Build the Inno installer from the same validated payload and repeat concise installed checks.

### Phase 4: Keep a classified restoration backlog
13. Keep required unpackaged-runtime adaptations: `WindowsPackageType=None`; self-contained publishing; `VxFilesEnvironment`; local settings/temp/layout storage; unpackaged MRT/language handling; executable activation/relaunch; package-family clipboard removals; and removal of `Files.App.Server`/packaged background-task references.
14. Keep intentional VxFiles changes: branding and independent versioning, VxFiles Automation, local-only diagnostics, and Sentry removal.
15. Queue installer-owned opt-in integrations separately: startup via HKCU Run or a Startup-folder shortcut; `vxfiles:` via HKCU `Software\Classes`; and separately reviewed file associations/App Paths. Inno must own registration and uninstall cleanup; do not call packaged `StartupTask`.
16. Treat Jump Lists and updates as redesigns: use an explicit AppUserModelID and Win32 Jump List APIs if desired; create an Inno/GitHub-release updater instead of re-enabling MSIX/AppInstaller updates.
17. Keep package-identity integrations disabled unless independently replaced: `SecondaryTile` Start pin/unpin, manifest aliases, packaged startup/background update tasks, and package activation. Keep default Explorer/Open/Save dialog replacement outside this work pending dedicated COM, architecture, rollback, and managed-machine review.
18. Before steps 15-17 are implemented, replace the remaining broad `SupportsWindowsIntegration` switch with narrowly named capabilities and an explicit distribution profile. This broader capability refactor is not needed for tags or the package-independent tray and remains outside the first delivery.

**Relevant files**
- `c:\Dev\vxfiles\src\Files.App\Utils\FileTags\FileTagsDatabase.cs` — implement the local JSON index while preserving its public API.
- `c:\Dev\vxfiles\src\Files.App\Utils\FileTags\TaggedFile.cs` — reuse the persisted schema; modify only for explicit serialization compatibility.
- `c:\Dev\vxfiles\src\Files.App\Helpers\Application\AppLifecycleHelper.cs` — restore tag startup and retain live tray visibility updates.
- `c:\Dev\vxfiles\src\Files.App\Services\Settings\GeneralSettingsService.cs` — restore persisted tag, background, and tray settings with the chosen defaults.
- `c:\Dev\vxfiles\src\Files.App\ViewModels\Settings\SettingsPageViewModel.cs` — restore Tags navigation.
- `c:\Dev\vxfiles\src\Files.App\ViewModels\Settings\SettingsSearchIndexer.cs` — restore Tags search indexing.
- `c:\Dev\vxfiles\src\Files.App\Views\Settings\GeneralPage.xaml` — restore only File Tags and Edit Tags cards.
- `c:\Dev\vxfiles\src\Files.App\Views\Settings\AdvancedPage.xaml` — expose Leave App Running and System Tray Icon without exposing packaged integrations.
- `c:\Dev\vxfiles\src\Files.App\App.xaml.cs` — instantiate/dispose the tray and execute close-to-background sleep/resume with the shared semaphore.
- `c:\Dev\vxfiles\src\Files.App\Program.cs` — reuse the shared semaphore when a launch wakes a cached instance.
- `c:\Dev\vxfiles\src\Files.App\Utils\Taskbar\SystemTrayIcon.cs` — use direct VxFiles activation and the shared semaphore for reopen/restart/Quit.
- `c:\Dev\vxfiles\src\Files.App\Helpers\Application\VxFilesEnvironment.cs` — own the stable VxFiles instance semaphore name; do not change package-identity capabilities in this slice.
- `c:\Dev\vxfiles\src\Files.App\Utils\FileTags\FileTagsHelper.cs` and `c:\Dev\vxfiles\src\Files.App\Utils\Storage\Operations\FileOperationsHelpers.cs` — reuse existing ADS, FRN, and file-operation behavior.
- `c:\Dev\vxfiles\src\Files.App\ViewModels\Settings\AdvancedViewModel.cs` — reuse existing tag import/export and tray/background bindings.
- `c:\Dev\vxfiles\docs\PORTABLE.md` and `c:\Dev\vxfiles\docs\adr\0006-isolate-vxfiles-data-without-registry-storage.md` — document restored behavior and constraints.

**Verification**
1. Run `msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`.
2. Rebuild portable staging and run `scripts/Test-PortableStartup.ps1` with clean/default settings so close is expected to terminate.
3. Non-admin tag matrix: create/edit/delete definitions; tag files/folders; verify context menu, sidebar, widget, search, sorting, restart persistence, rename/move/copy/delete including descendants, settings export/import, atomic `%LOCALAPPDATA%\VxFiles\filetags.db`, and no VxFiles/Files Community registry keys.
4. Tray live-toggle matrix: enable and disable Show System Tray Icon without restart; verify icon, tooltip, left-click activation, context menu, Explorer/taskbar restart recovery, and icon removal.
5. Background matrix: verify all four combinations of Leave App Running and Show System Tray Icon; confirm close exits when Leave App Running is off, close retains one process when on, executable relaunch and tray click reopen the same cached process, and tray Quit fully terminates it without an orphan icon/window.
6. Repeat concise tag/tray/background checks after a lowest-privilege Inno install and upgrade. Confirm neither feature requires elevation or creates startup/protocol/association registration.
7. Run `git diff --check` and review a focused diff limited to first-delivery files.

**Decisions**
- Support both ZIP and Inno with one unpackaged runtime and identical tag/tray/background behavior.
- Tags, Win32 notification-area icons, and an in-process background lifetime require neither package identity nor administrator rights.
- Store the tag index as JSON rather than HKCU or a new SQLite schema; preserve atomicity and multi-process locking.
- Do not migrate Files Community registry tags automatically; VxFiles data remains isolated.
- Leave App Running and Show System Tray Icon are independent, persisted, explicit opt-ins with defaults off.
- Restoring the tray does not restore startup-at-login. Packaged `StartupTask` UI and behavior remain hidden.
- First implementation now ends after tags, tray/background lifecycle, docs, builds, and smoke tests. Other Windows integrations remain separate follow-up work.
