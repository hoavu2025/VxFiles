# Building VxFiles

`AGENTS.md` covers the normal build commands. This file records the one thing about them that is
not obvious and costs an afternoon to rediscover.

## Use MSBuild, not `dotnet build`

`dotnet build` cannot build `src/Files.App`. It fails during XAML compilation with:

```text
Xaml Internal Error error WMC9999: Could not find any resources appropriate for the specified
culture or the neutral culture. Make sure "Microsoft.UI.Xaml.Markup.Compiler.ErrorMessages.resources"
was correctly embedded or linked into assembly "XamlCompiler"...
```

This is not a code error. It reproduces on a clean checkout with no local changes, and it is the
XAML compiler failing to load its own error-message resources — so **whatever the real problem was,
its description is destroyed on the way out**. The build reports one internal error and nothing
else, which reads exactly like a broken page of XAML and is not.

Use MSBuild from Visual Studio. `scripts/release/Build-VxFilesRelease.ps1` already does.

```powershell
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
	-find "MSBuild\**\Bin\MSBuild.exe"

& $msbuild src/Files.App/Files.App.csproj `
	-p:Configuration=Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 `
	-v:quiet -clp:ErrorsOnly
```

## Run the automation tests through the built executable

`dotnet test tests/VxFiles.Automation.Tests` reports `Zero tests ran` and exit code 5. The project is
`OutputType=Exe` on Microsoft.Testing.Platform, so run the executable it produces instead:

```powershell
dotnet build tests/VxFiles.Automation.Tests -c Debug -p:Platform=x64
& .\tests\VxFiles.Automation.Tests\bin\x64\Debug\net10.0-windows10.0.26100.0\VxFiles.Automation.Tests.exe
```

## Reading a XAML failure that has no message

If MSBuild itself reports `WMC9999`, the real diagnostics are still on disk. The XAML compiler runs
out of process and writes its full log, stack trace included, next to its input:

```text
src/Files.App/obj/x64/<Config>/<TargetFramework>/win-x64/output.json
```

Search it for `"Type":2` (errors) and `"Type":1` (warnings). The entry after a masked error carries
the stack trace, which names the compiler stage that actually failed.
