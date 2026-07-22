# VxFiles

VxFiles is a personalized, portable Windows file manager based on [Files Community 4.2](https://github.com/files-community/Files/releases/tag/v4.2).

Milestone 1 provides an unsigned x64 build that runs from an extracted folder without installation or administrator rights. It is self-contained, stores its state under `%LOCALAPPDATA%\VxFiles`, and does not configure shell integration, startup tasks, automatic updates, telemetry, or remote crash reporting.

## Fast Debug iteration

Use the incremental Debug build while changing C# or XAML:

```powershell
.\scripts\Build-Debug.ps1 -Run
```

Close VxFiles before rebuilding. Subsequent builds reuse existing outputs; add `-NoRestore` for the shortest loop after dependencies are already restored. Add `-FrameworkDependent` to skip bundling runtime libraries when local .NET and Windows App SDK runtimes are installed.

`Build-Portable.ps1 -Configuration Debug` also works, but it still publishes, stages, compresses, and hashes the portable archive, so use it only when you need a Debug ZIP.

## Build the portable archive

Requirements:

- Windows 10 build 19041 or newer
- Visual Studio 2026 with the managed desktop workload
- .NET SDK 10

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Portable.ps1
```

The script creates:

- `artifacts\VxFiles-portable-win-x64.zip`
- `artifacts\VxFiles-portable-win-x64.zip.sha256`

See [docs/PORTABLE.md](docs/PORTABLE.md) for distribution limitations and smoke-test guidance.

## Upstream and licensing

This repository retains its GitHub fork relationship with `files-community/Files`. Internal `Files.*` project and namespace names are intentionally retained to make stable upstream release merges manageable.

Files Community copyright and license notices are preserved. VxFiles modifications are described in [NOTICE-VXFILES.md](NOTICE-VXFILES.md). See [LICENSE-MIT](LICENSE-MIT) and [LICENSE-MPL](LICENSE-MPL).
