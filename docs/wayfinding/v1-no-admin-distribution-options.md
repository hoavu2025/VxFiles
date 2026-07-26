# V1 no-admin distribution options

## Decision constraints

VxFiles V1 needs a zero-cost distribution path that lets a standard Windows user install and update the app without administrator credentials. The preferred delivery remains GitHub Releases, GitHub Actions are excluded, .NET 10 and the Windows App SDK must remain self-contained, and the downstream delta from Files `v4.2` should remain small.

## Findings

### The current self-signed App Installer path cannot meet the requirement

Microsoft states that App Installer does not search the current-user certificate stores when it validates package identity. A self-signed package certificate must be imported into **Local Computer > Trusted People** by a local administrator. Therefore, changing the certificate import command from `LocalMachine` to `CurrentUser` does not fix the current `.appinstaller` flow.

Primary source: [Troubleshoot installation issues with the App Installer file](https://learn.microsoft.com/windows/msix/app-installer/troubleshoot-appinstaller-issues)

Microsoft also states that an MSIX package must be signed and its certificate chain must terminate at a root trusted by the device. A self-signed certificate is free, but is categorized for development and local testing rather than public distribution.

Primary source: [Sign an MSIX package](https://learn.microsoft.com/windows/msix/package/signing-package-overview)

### Current-user certificate trust applies to a different deployment model

Microsoft documents importing a self-signed certificate into `Cert:\CurrentUser\TrustedPeople` for an app **packaged with external location**, also called a sparse package. That model installs the application binaries outside MSIX and registers a small identity package with `Add-AppxPackage`. Microsoft separately says a machine-wide sparse registration uses `LocalMachine` and requires elevation.

This evidence is specific to sparse packages. It should not be generalized to the current full MSIX bundle or App Installer.

A sparse identity can restore features unavailable to a fully unpackaged app, including file associations, startup tasks, background tasks, share targets, and some app extensions. However, VxFiles would need a per-user binary installer, sparse registration logic, and its own update mechanism. Compatibility of every Files manifest extension would need proof.

Primary source: [Grant package identity by packaging with external location manually](https://learn.microsoft.com/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)

### Unsigned MSIX is not a distribution solution

Windows 11 supports `Add-AppxPackage -AllowUnsigned`, but Microsoft says an unsigned package containing executable content normally requires an administrator because it must be installed for all users. Microsoft describes the feature as a quick-testing aid and says not to use it for broad distribution.

Primary source: [Create an unsigned MSIX package](https://learn.microsoft.com/windows/msix/package/unsigned-package)

## Viable options

### Option A: Microsoft Store MSIX with a direct-link-only listing

This is the smallest technical departure from the completed V1:

- Microsoft now offers free Partner Center registration to new individual developers when onboarding starts at `storedeveloper.microsoft.com`. Identity verification requires a government-issued ID and selfie.
- Microsoft re-signs Store-submitted MSIX packages with a trusted certificate at no charge.
- Users can install without trusting a VxFiles certificate or supplying administrator credentials.
- Existing users receive approved updates through the Store.
- The listing can use the **Direct link only** discoverability option so it is not generally discoverable through Store search.

Primary sources:

- [Free developer registration for individual developers](https://learn.microsoft.com/windows/apps/publish/whats-new-individual-developer)
- [Code signing options for Windows app developers](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)
- [Manage and update your app](https://learn.microsoft.com/windows/apps/publish/faq/manage-and-update-your-app)
- [Choose visibility options for an MSIX app](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/visibility-options)

Tradeoff: GitHub can remain the project and release-notes landing page and can link directly to the unlisted Store listing, but Microsoft—not GitHub—serves the install package and updates. This option requires relaxing the strict GitHub-download constraint.

### Option B: SignPath Foundation with non-GitHub CI

SignPath Foundation offers free publicly trusted code signing to accepted open-source projects. Its published conditions explicitly contemplate modified upstream forks when the fork is visible, upstream publishes signed builds, release branches are based on normally signed upstream branches, and all other review and policy obligations are met.

SignPath supports signing `.msix` and `.msixbundle` files. The manifest `Identity Publisher` must match the subject of the assigned SignPath certificate, so VxFiles package identity would need to change from `CN=VxFiles`. Product and display branding can remain VxFiles.

Primary sources:

- [SignPath Foundation](https://signpath.org/)
- [SignPath Foundation conditions for open-source projects](https://signpath.org/terms.html)
- [SignPath artifact format reference](https://docs.signpath.io/artifact-configuration/reference)

The free Foundation service does **not** support the current manual local build-and-upload release process. Its rules require:

- binaries built from source in a verifiable automated build;
- a SignPath-supported Trusted Build System with origin verification;
- manual approval of every signing request.

Interactive users cannot submit under an Open Source Code Signing policy. SignPath supports GitHub, Jenkins, AppVeyor, Azure DevOps, and TeamCity as Trusted Build Systems.

Primary sources:

- [SignPath Trusted Build Systems](https://docs.signpath.io/trusted-build-systems/)
- [SignPath project and signing-policy configuration](https://docs.signpath.io/projects)

Avoiding GitHub Actions is still possible. AppVeyor advertises a free plan for unlimited public open-source projects with one concurrent job, and SignPath supports AppVeyor origin verification.

Primary sources:

- [AppVeyor plans and pricing](https://www.appveyor.com/pricing/)
- [SignPath build-system integration](https://docs.signpath.io/build-system-integration)

Tradeoffs and uncertainties:

- SignPath acceptance is discretionary and depends on project reputation and compliance.
- The repository needs a documented code-signing policy, named roles, privacy disclosure, MFA, review rules, and signing metadata constraints.
- The hosted or self-hosted CI environment must be proven capable of building Files with the required Visual Studio and .NET 10 toolchain.
- This preserves GitHub-hosted downloads and the MSIX/App Installer user experience once accepted, but adds release infrastructure and package-identity changes.

### Option C: Per-user Velopack installer with unpackaged or sparse identity

Microsoft supports unpackaged, self-contained WinUI 3 deployment by using `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, and .NET self-contained publishing. The output can be copied directly or wrapped in a traditional installer.

Velopack provides:

- a per-user `Setup.exe` that installs under `%LocalAppData%\{packId}` without elevation;
- per-user updates without UAC;
- a built-in GitHub Releases update source.

Primary sources:

- [Distribute an unpackaged WinUI 3 app](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app)
- [Windows App SDK self-contained deployment](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [Velopack Windows overview](https://docs.velopack.io/packaging/operating-systems/windows)
- [Velopack update sources](https://docs.velopack.io/integrating/update-sources)

Inno Setup can also run without elevation when `PrivilegesRequired=lowest`, but it does not itself provide the integrated GitHub update model offered by Velopack.

Primary source: [Inno Setup `PrivilegesRequired`](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)

Tradeoffs:

- A fully unpackaged app loses package identity and package-manifest features.
- Adding sparse identity can recover many Windows integrations, but creates a two-part installer and identity-registration design.
- Files relies heavily on packaging, shell integration, app services, background tasks, and manifest extensions, so this route is a substantial downstream refactor and every affected feature requires testing.
- An unsigned traditional installer can run as a standard user but may show Microsoft Defender SmartScreen warnings or be blocked by organizational policy. “No elevation” is not the same as “publicly trusted.”

## Enterprise-managed alternative

An organization can deploy certificate trust and MSIX packages through Group Policy or Intune. This removes the action from the coworker and can enable silent installation, but an organizational administrator still authorizes and configures the deployment. It is therefore useful for managed company devices, but is not independent no-admin distribution.

Primary sources:

- [Distribute certificates by using Group Policy](https://learn.microsoft.com/windows-server/identity/ad-cs/distribute-certificates-group-policy)
- [Deploy MSIX apps with Microsoft Intune](https://learn.microsoft.com/windows/msix/desktop/managing-your-msix-deployment-intune)

## Recommendation

1. Use a free, direct-link-only Microsoft Store MSIX listing if serving install bytes from Microsoft is acceptable. It preserves the clean upstream-aligned code line and is the fastest supported route.
2. If GitHub-hosted install assets are mandatory, apply to SignPath Foundation and validate a free AppVeyor build. Continue distributing the trusted MSIX/App Installer assets from GitHub only after acceptance and an end-to-end CI signing proof.
3. Treat Velopack plus unpackaged or sparse identity as a separate refactor, not a small V1 packaging adjustment. Prototype it only if both Store delivery and SignPath-backed CI are unacceptable.

No supported combination of full MSIX, self-signing, App Installer, GitHub-only hosting, and standard-user trust satisfies all current constraints.
