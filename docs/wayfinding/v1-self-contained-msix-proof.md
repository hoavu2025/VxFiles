# V1 self-contained x64 MSIX proof

## Decision

Keep upstream single-project MSIX packaging and project topology. Scope runtime deployment to the existing `win-x64.pubxml` publish-profile seam:

```xml
<SelfContained>true</SelfContained>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
```

Leave the project-wide `SelfContained` and `WindowsAppSDKSelfContained` defaults, all runtime identifiers, all platforms, project references, the out-of-process server, and background tasks unchanged from Files v4.2.

The only project changes needed by packaging are the already-decided public executable identity:

```xml
<AssemblyName>VxFiles</AssemblyName>
<TrimmerRootAssembly Include="VxFiles" />
```

The package manifest supplies the matching V1 identity:

- package name `VxFiles`
- publisher `CN=VxFiles`
- display and publisher display name `VxFiles`
- protocol `vxfiles`
- execution alias `vxfiles.exe`
- startup-task display name `VxFiles`
- unchanged internal application, COM server, background-task, and preview-handler identities

## Reproduction

The proof ran from a detached worktree at the clean Files v4.2 tag with Visual Studio 2026 18.7.2, MSBuild 18.7.8, and the repository-pinned .NET 10 SDK family.

Restore the solution before packaging. Restoring only `Files.App.csproj` does not restore the out-of-process `Files.App.Server` project and fails for a missing `project.assets.json`.

```powershell
msbuild Files.slnx `
  -t:Restore `
  -p:Configuration=Release `
  -p:Platform=x64 `
  -p:PublishReadyToRun=true `
  -v:quiet `
  -clp:ErrorsOnly
```

Create a code-signing PFX whose subject matches the manifest publisher, then run the upstream packaging path locally:

```powershell
msbuild src/Files.App/Files.App.csproj `
  -t:Build `
  -p:Configuration=Release `
  -p:Platform=x64 `
  -p:AppxBundlePlatforms=x64 `
  -p:AppxBundle=Never `
  -p:GenerateAppxPackageOnBuild=true `
  -p:UapAppxPackageBuildMode=SideloadOnly `
  -p:AppxPackageDir="<output-directory>\" `
  -p:AppxPackageSigningEnabled=true `
  -p:PackageCertificateKeyFile="<VxFiles-PFX-path>" `
  -p:PackageCertificatePassword= `
  -p:PackageCertificateThumbprint= `
  -v:quiet `
  -clp:ErrorsOnly
```

This is a local/manual command. It needs no GitHub Actions service.

## Evidence

The final clean/incremental proof produced a signed 186,305,581-byte x64 MSIX. `MakeAppx unpack` and the embedded `AppxManifest.xml` showed:

- identity `VxFiles`, publisher `CN=VxFiles`, version `4.2.0.0`, architecture `x64`
- main executable `VxFiles.exe`
- public activation names `vxfiles` and `vxfiles.exe`
- zero `PackageDependency` nodes and zero dependency packages beside the MSIX
- embedded .NET runtime files: `coreclr.dll`, `hostfxr.dll`, and `hostpolicy.dll`
- embedded Windows App SDK runtime files: `Microsoft.WindowsAppRuntime.dll` and `Microsoft.WindowsAppRuntime.Bootstrap.dll`

`SignTool verify /pa` succeeded after temporarily trusting the disposable self-signed certificate. The proof certificate, private key, package, unpacked payload, build outputs, and temporary trust entries were not retained.

## Boundaries

- This proves the signed, self-contained x64 MSIX packaging seam. It does not install or launch the package on the current workstation.
- The exact reusable certificate lifecycle and coworker trust instructions belong to **Define zero-cost self-signed package identity**.
- The `.appinstaller` descriptor, GitHub release asset names/URLs, and update behavior belong to **Define GitHub-hosted App Installer updates**. `GenerateAppInstallerFile` therefore remains disabled in the project during this proof.
- The upstream output filename remains `Files.App_<version>_x64.msix` because it derives from internal project packaging metadata. A release workflow may rename the published asset without renaming upstream projects.
