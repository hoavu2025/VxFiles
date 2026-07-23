# VxFiles Portable Build

VxFiles Portable is an unsigned, self-contained x64 application distributed as a ZIP. Extract the complete archive and run `VxFiles.exe`; no installer or administrator rights are required.

## Data and system footprint

- Persistent data is stored under `%LOCALAPPDATA%\VxFiles`.
- The application does not share settings with an installed Files Community build.
- The application does not automatically register protocols, file associations, startup tasks, background updates, or shell extensions.
- Telemetry and remote crash reporting are disabled. Diagnostic logs remain in the VxFiles data directory.
- File tags are disabled because Files 4.2 stores tag data in the registry.
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

1. Extract the ZIP into a new folder.
2. Launch `VxFiles.exe` from a non-elevated account.
3. Open several folders and tabs.
4. In a disposable test folder, copy, move, rename, and delete test files.
5. Change a setting, close VxFiles, relaunch it, and confirm the setting persisted.
6. Confirm `%LOCALAPPDATA%\VxFiles` contains the app state and logs.
7. Confirm no VxFiles package was installed and no VxFiles or Files Community registry keys were created.

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
