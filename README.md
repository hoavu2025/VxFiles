# VxFiles

VxFiles is a personalized Windows file manager based on [Files Community 4.2](https://github.com/files-community/Files/releases/tag/v4.2). The fork deliberately keeps inherited `Files.*` source identifiers and project structure so stable upstream releases remain practical to merge.

V1 is an x64, self-contained MSIX application. It includes .NET 10 and the Windows App SDK runtime. Portable ZIP and Inno Setup distributions are not supported.

## Install

V1 uses a free self-signed release certificate. Before the first installation, download `VxFiles.Release.cer` from the latest [VxFiles release](https://github.com/hoavu2025/VxFiles/releases/latest), verify its published fingerprint, and import it into:

```text
Local Computer\Trusted People
```

This one-time trust operation requires administrator access. Install or update VxFiles through:

```text
https://github.com/hoavu2025/VxFiles/releases/latest/download/VxFiles.appinstaller
```

Only the public CER is shared. The private PFX must never be downloaded from GitHub or copied to another user.

## Build

Requirements:

- Windows 10 build 19041 or newer
- Visual Studio with the managed desktop and Windows application packaging workloads
- .NET SDK 10

From an x64 Visual Studio developer shell:

```powershell
msbuild Files.slnx -t:Restore -p:Configuration=Release -p:Platform=x64 -v:quiet -clp:ErrorsOnly
msbuild src/Files.App/Files.App.csproj -t:Build -p:Configuration=Release -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

Building is not the release process. Packages must be versioned, bundled, signed, timestamped, inspected, and published using [docs/VXFILES-RELEASE.md](docs/VXFILES-RELEASE.md). Releases are performed manually; this repository does not use GitHub Actions for V1.

## Downloads, updates, and support

VxFiles binaries, App Installer updates, release notes, issues, and support are hosted only by the [VxFiles fork](https://github.com/hoavu2025/VxFiles). Do not use Files Community releases as a VxFiles installation or update source.

## Upstream and licensing

Files Community documentation and community resources remain relevant to inherited features. VxFiles takes future upstream changes from explicit stable Files tags on dedicated sync branches.

Files Community copyright and license notices are preserved. VxFiles modifications are described in [NOTICE-VXFILES.md](NOTICE-VXFILES.md). See [LICENSE-MIT](LICENSE-MIT) and [LICENSE-MPL](LICENSE-MPL).
