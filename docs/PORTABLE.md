# VxFiles Portable Build

VxFiles Portable is an unsigned, self-contained x64 application distributed as a ZIP. Extract the complete archive and run `VxFiles.exe`; no installer or administrator rights are required.

## Data and system footprint

- Persistent data is stored under `%LOCALAPPDATA%\VxFiles`.
- The application does not share settings with an installed Files Community build.
- The application does not automatically register protocols, file associations, startup tasks, background updates, or shell extensions.
- Telemetry and remote crash reporting are disabled. Diagnostic logs remain in the VxFiles data directory.
- File tags are stored locally in `%LOCALAPPDATA%\VxFiles\filetags.db` using JSON persistence. File tag markers are also attached via NTFS Alternate Data Streams (`:files` ADS) when supported by the filesystem.
- System tray icon (`ShowSystemTrayIcon`) and background lifetime (`LeaveAppRunning`) are optional, default off for both ZIP and Inno builds, create no persistent Windows registration, and do not enable startup-at-login.
- Per-folder layout preferences are stored in `%LOCALAPPDATA%\VxFiles\layout-preferences.json`.

## Trust limitation

Milestone 1 is unsigned. Windows SmartScreen may warn before launch, and managed company policy may block the executable. The portable build does not attempt to bypass those controls.

Verify a received archive against its companion checksum:

```powershell
Get-FileHash .\VxFiles-portable-win-x64.zip -Algorithm SHA256
```

## Smoke test

After building, verify that startup reaches the real application shell rather than merely creating the splash window:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-PortableStartup.ps1 `
	-ExecutablePath .\artifacts\staging\VxFiles-portable-win-x64\VxFiles.exe
```

The check verifies that the portable icon and splash assets are present, the splash image renders, the shell exposes both `SettingsButton` and a window icon, closing the window terminates the process, and a second launch reaches the real shell.

### Portable Verification Matrix

1. Extract the ZIP into a new folder.
2. Launch `VxFiles.exe` from a non-elevated account.
3. Open several folders and tabs.
4. In a disposable test folder, copy, move, rename, and delete test files.
5. **Tag Persistence Verification**:
   - Apply a tag to a test file on an NTFS drive. Confirm `%LOCALAPPDATA%\VxFiles\filetags.db` is updated with the tag, FRN, and path.
   - Confirm ADS tag marker (`:files`) is created on NTFS.
   - Test tagging a file on a non-ADS volume (e.g. FAT32/exFAT drive or network share); confirm JSON database persistence succeeds cleanly.
   - Rename a tagged file; verify tag remains attached by FRN matching and database records do not duplicate.
   - Restart VxFiles and verify file tags persist.
6. **Tray & Background Lifecycle Matrix**:
   - Both `ShowSystemTrayIcon` and `LeaveAppRunning` default to **OFF** (`false`) in both ZIP portable and Inno Setup installer builds.
   - **Tray OFF / Background OFF**: Standard windowed mode. Closing the main window exits the process completely.
   - **Tray ON / Background OFF**: Notification tray icon is displayed. Closing the main window exits the process completely.
   - **Tray OFF / Background ON**: App continues running in background on main window close. Relaunching `VxFiles.exe` brings up the existing session.
   - **Tray ON / Background ON**: Full system tray background mode. Closing the main window hides it to the system tray. Exiting via the tray icon context menu terminates the process cleanly.
   - Verify no `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry keys or OS startup tasks are created under any combination.
7. **Installed-Upgrade & Coexistence Verification**:
   - Confirm running the portable build on a system with an installed Files Community build does not overwrite or read `%LOCALAPPDATA%\Packages` or registry entries.
   - Confirm replacing portable binaries (upgrading) preserves `%LOCALAPPDATA%\VxFiles` data, logs, and settings.
8. Change a setting, close VxFiles, relaunch it, and confirm the setting persisted.
9. Confirm `%LOCALAPPDATA%\VxFiles` contains the app state and logs.
10. Confirm no VxFiles package was installed and no VxFiles or Files Community registry keys were created.

Always pilot the artifact on a coworker's managed machine before wider sharing, because company application-control policy varies.

## Release automation

To publish a portable release archive to GitHub Releases via `gh` CLI:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1 `
    -TagName "v1.0.0" `
    -Title "VxFiles v1.0.0" `
    -Build
```

### Script Parameters (`Publish-Release.ps1`)

| Parameter | Type | Description |
| --- | --- | --- |
| `-TagName` | String | Release tag name (e.g. `v1.0.0`). Prompts interactively if omitted. |
| `-Title` | String | Release title. Defaults to `VxFiles <TagName>`. |
| `-Notes` | String | Release notes text body. |
| `-NotesFile` | String | Path to a markdown file containing release notes. |
| `-TargetCommit` | String | Target commit or branch (e.g. `main`). |
| `-Draft` | Switch | Creates the GitHub release as a draft. |
| `-Prerelease` | Switch | Marks the GitHub release as a pre-release. |
| `-Build` | Switch | Automatically runs `Build-Portable.ps1 -Configuration Release` prior to publishing. |
