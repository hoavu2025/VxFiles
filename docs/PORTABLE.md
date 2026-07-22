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

The check passes only when the shell's `SettingsButton` is present in the launched process's accessibility tree.

1. Extract the ZIP into a new folder.
2. Launch `VxFiles.exe` from a non-elevated account.
3. Open several folders and tabs.
4. In a disposable test folder, copy, move, rename, and delete test files.
5. Change a setting, close VxFiles, relaunch it, and confirm the setting persisted.
6. Confirm `%LOCALAPPDATA%\VxFiles` contains the app state and logs.
7. Confirm no VxFiles package was installed and no VxFiles or Files Community registry keys were created.

Always pilot the artifact on a coworker's managed machine before wider sharing, because company application-control policy varies.
