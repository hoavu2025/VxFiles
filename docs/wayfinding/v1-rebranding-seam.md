# V1 public rebranding seam

## Decision

V1 presents VxFiles through the package, executable, activation, shell, title, splash, documentation, and support-link interfaces. It does not create a parallel internal application identity.

The main executable remains renamed to `VxFiles.exe`, matching the current fork's visible binary identity. All inherited `Files.*` namespaces, projects, library assemblies, solution folders, extension contracts, background-task entry points, and internal persistence identifiers remain unchanged.

This is a patch seam over existing upstream interfaces, not a branding module or environment abstraction.

## Public identity contract

| Surface | V1 value |
| --- | --- |
| Product and package display name | `VxFiles` |
| Main executable | `VxFiles.exe` |
| Package identity name | `VxFiles` |
| Package publisher subject | `CN=VxFiles`, subject to confirmation by the signing ticket |
| Publisher display name | `VxFiles` |
| Protocol | `vxfiles:` |
| Execution alias | `vxfiles.exe` |
| Main window and navigation title suffix | `VxFiles` |
| Startup task display name | `VxFiles` |
| Source, support, download, and update owner | `https://github.com/hoavu2025/VxFiles` |
| Icon family | Unmodified `Assets\AppTiles\Dev` assets from Files v4.2 |

The package family name is derived by Windows from the package identity and publisher. It must not be hard-coded. Application id `App`, startup-task id, file-association names, and COM/background identifiers remain upstream values.

Package version is not part of the branding seam. The signing/package-identity ticket owns its initial value and monotonic update rules.

## Exact source patch

Every file starts from Files v4.2. Only the changes below are permitted.

### Package and executable identity

`src/Files.App/Files.App.csproj`

- Change `<AssemblyName>Files</AssemblyName>` to `<AssemblyName>VxFiles</AssemblyName>`.
- Change only the matching trimmer root from `Files` to `VxFiles`.
- Do not rename the project, directory, root namespace, project references, server, background-task, or library assemblies.
- Self-contained properties belong to the package-build ticket, not this seam.

`src/Files.App/Package.appxmanifest`

- Set identity name to `VxFiles`.
- Set publisher to `CN=VxFiles`, unless the signing ticket proves a different exact certificate subject is required.
- Set package, application, tile, and startup-task display names to `VxFiles`.
- Set publisher display name to `VxFiles`.
- Set protocol to `vxfiles`.
- Set execution alias to `vxfiles.exe`.
- Leave application id, task id, app-extension name `com.files.filepreview`, background-task entry points, file associations, capabilities, resource languages, and asset paths unchanged.
- Leave version to the signing/package-identity ticket.

`src/Files.App/app.manifest`

- No change. Keep the internal Win32 assembly identity `Files.App.app` and its upstream version.

### Activation and executable recognition

Replace only the upstream `files-dev` protocol or alias literals with `vxfiles`, retaining the existing Windows launcher and package activation implementation:

- `src/Files.App/Actions/Navigation/OpenInNewWindow/BaseOpenInNewWindowAction.cs`
- `src/Files.App/Helpers/Application/AppLifecycleHelper.cs`
- `src/Files.App/Helpers/Navigation/NavigationHelpers.cs`
- `src/Files.App/MainWindow.xaml.cs`
- `src/Files.App/Program.cs`
- `src/Files.App/Utils/Taskbar/SystemTrayIcon.cs`
- `src/Files.App/ViewModels/Settings/GeneralViewModel.cs`

Where executable recognition is necessary, replace only `Files.exe` or the `Files` process name with `VxFiles.exe` or `VxFiles`:

- `src/Files.App/App.xaml.cs`
- `src/Files.App/MainWindow.xaml.cs`
- `src/Files.App/Program.cs`
- `src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs`

Do not replace upstream package storage, app-instance activation, semaphores, clipboard handling, or launcher calls with a VxFiles environment helper.

### Visible titles

`src/Files.App/MainWindow.xaml.cs`

- Change the initial visible window title from `Files` to `VxFiles`.
- Keep `PersistenceId = "FilesMainWindow"` because it is internal and the new package identity already isolates VxFiles data.

`src/Files.App/Helpers/Navigation/NavigationHelpers.cs`

- Change only the visible ` - Files` window-title suffix to ` - VxFiles`.

`src/Files.App/Views/SplashScreenPage.xaml`

- Change only the hard-coded product-name run from `Files` to `VxFiles`.
- Keep the existing Files Dev splash image path and code-behind unchanged.

The About page and system-tray tooltip need no branding patch because upstream already reads `Package.Current.DisplayName`.

## Fork URLs

Only `src/Files.App/Constants.cs` owns runtime links.

Set:

- `GitHubRepoUrl` to `https://github.com/hoavu2025/VxFiles`;
- `FeatureRequestUrl` to `https://github.com/hoavu2025/VxFiles/issues/new`;
- `BugReportUrl` to `https://github.com/hoavu2025/VxFiles/issues/new?labels=bug&template=bug_report.yml`;
- `SupportUsUrl` to `https://github.com/hoavu2025/VxFiles`;
- `ReleaseNotesUrl` to `https://github.com/hoavu2025/VxFiles/releases/tag/v{Major}.{Minor}.{Build}`, using the installed package version components;
- the startup error title to `VxFiles - Startup Error`; and
- the startup error message to direct users to `https://github.com/hoavu2025/VxFiles/releases/latest`, without portable/archive wording.

Keep these upstream links:

- Files documentation;
- Files Discord;
- Files privacy policy;
- Files Crowdin project.

They describe inherited functionality and community resources. VxFiles attribution and independent-fork status live in the root README and `NOTICE-VXFILES.md`.

## Repository-facing identity

Keep and rewrite these downstream-owned files:

- `README.md`: VxFiles purpose, Files v4.2 base, MSIX installation, manual local build/release, certificate-trust step, fork download/update links, and upstream attribution.
- `NOTICE-VXFILES.md`: independent fork, Files Community copyright/source, VxFiles source, and current upstream base.
- `CONTEXT.md`: public VxFiles identity and internal Files identifiers, already recorded.
- `docs/adr/0001-limit-vxfiles-renaming-to-public-identity.md`: update the wording to this exact seam and retain the Files Dev icon decision.

Do not rebrand upstream contributor documentation, issue templates, workflow names, solution/project names, or source copyright notices.

## Resources and localization

No `.resw` file changes belong to V1.

Files v4.2 has 49 locale resource files and at least seventeen resource keys containing product-name prose. Editing them all would create a broad recurring merge surface, while editing only English would produce inconsistent branding. The current fork did not rebrand those localized strings.

V1 therefore rebrands canonical identity surfaces only. Upstream-authored localized feature prose, community references, and strings such as “Welcome to Files” remain upstream text. A complete localized-copy rebrand is a separate future effort, not part of the installable V1.

## Icons and assets

Use the complete existing `src/Files.App/Assets/AppTiles/Dev` family unchanged:

- `Logo.ico`;
- Store logo;
- splash images;
- tile images;
- target-size variants; and
- contrast variants.

The package manifest and project already point to this family. Do not copy, rename, regenerate, or edit icon assets. This produces zero binary-asset divergence.

## Upstream build scripts

Do not edit or run `.github/scripts/Configure-AppxManifest.ps1` for the V1 release. It rewrites identities, protocols, aliases, and icon families for official Files channels and would overwrite the VxFiles seam.

Do not edit `.github/scripts/Generate-SelfCertPfx.ps1` in this ticket. The signing ticket must decide whether to reuse it through parameters or add a small VxFiles-owned local signing script.

## Verification contract

The implementation must prove:

1. Start, Installed Apps, App Installer, taskbar, tray tooltip, window title, splash label, protocol, execution alias, startup entry, executable, and startup error identify VxFiles.
2. About opens the VxFiles fork, while inherited documentation/community links still open Files Community.
3. Bug and feature actions open the VxFiles issue tracker.
4. Download, release notes, and update flows never point to official Files binaries.
5. `Assets\AppTiles\Dev` has no diff from v4.2.
6. `Files.slnx`, `Files.*` namespaces/projects/libraries, `Files.App.app`, application id, extension names, background entry points, and internal persistence ids retain upstream names.
7. A targeted scan finds no new broad `Files`-to-`VxFiles` substitutions outside the allowlist.

## Resulting seam

The runtime rebranding patch is limited to thirteen existing application files:

- `src/Files.App/Actions/Navigation/OpenInNewWindow/BaseOpenInNewWindowAction.cs`
- `src/Files.App/App.xaml.cs`
- `src/Files.App/Constants.cs`
- `src/Files.App/Files.App.csproj`
- `src/Files.App/Helpers/Application/AppLifecycleHelper.cs`
- `src/Files.App/Helpers/Navigation/NavigationHelpers.cs`
- `src/Files.App/MainWindow.xaml.cs`
- `src/Files.App/Package.appxmanifest`
- `src/Files.App/Program.cs`
- `src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs`
- `src/Files.App/Utils/Taskbar/SystemTrayIcon.cs`
- `src/Files.App/ViewModels/Settings/GeneralViewModel.cs`
- `src/Files.App/Views/SplashScreenPage.xaml`

This removes `src/Files.App/app.manifest` and the x64 publish profile from the rebranding seam identified provisionally by the delta audit. The publish profile remains owned exclusively by the self-contained build ticket.
