# V1 self-signed package identity

## Decision

V1 uses one long-lived, zero-cost self-signed release certificate and one immutable MSIX package identity:

| Field | V1 value |
| --- | --- |
| Package `Identity Name` | `VxFiles` |
| Package `Identity Publisher` | `CN=VxFiles` |
| Certificate subject | `CN=VxFiles` |
| Certificate purpose | Code signing only |
| First package version | `2.0.0.0` |
| Signing digest | SHA-256 |
| Timestamp protocol and digest | RFC 3161 and SHA-256 |
| Installer trust store | `Cert:\LocalMachine\TrustedPeople` |

The manifest publisher and certificate subject must match exactly, including distinguished-name fields, order, case, and whitespace. The signing certificate must contain a private key, permit digital signatures, and either omit Extended Key Usage or include the code-signing EKU `1.3.6.1.5.5.7.3.3`. [Microsoft's package-signing requirements](https://learn.microsoft.com/en-us/windows/win32/appxpkg/how-to-sign-a-package-using-signtool) define these checks.

`VxFiles` and `CN=VxFiles` are permanent update identity, not release-time inputs. Windows forms the package family from package name and publisher, and an update must remain in that family. A normal update also needs a higher four-part package version. [Microsoft's app-update constraints](https://learn.microsoft.com/en-us/windows/msix/app-package-updates#app-update-constraints) document both rules.

The VxFiles release version is independent from the Files upstream version. The fork already published unrelated portable/EXE releases under `v1.0.0` through `v1.0.2`; do not delete or reuse those tags. Start the refactored, installable line at package `2.0.0.0` and GitHub tag `v2.0.0`, marking the intentional distribution and compatibility break. Keep the fourth package component at zero. Each release increments one of the first three components and never reuses or decreases a published package version. This makes the release tag, package version, and existing VxFiles release-notes URL pattern agree without coupling downstream releases to the upstream Files tag.

## Generate the release identity once

Run this locally under the release-owner Windows account. Do not run it for every release.

```powershell
$certificate = New-SelfSignedCertificate `
  -Type Custom `
  -Subject 'CN=VxFiles' `
  -FriendlyName 'VxFiles Release Signing' `
  -CertStoreLocation 'Cert:\CurrentUser\My' `
  -KeyAlgorithm RSA `
  -KeyLength 3072 `
  -HashAlgorithm SHA256 `
  -KeyUsage DigitalSignature `
  -KeyExportPolicy Exportable `
  -TextExtension @(
    '2.5.29.37={text}1.3.6.1.5.5.7.3.3',
    '2.5.29.19={text}'
  ) `
  -NotAfter (Get-Date).AddYears(3)

$pfxPassword = Read-Host 'New VxFiles release PFX password' -AsSecureString

Export-PfxCertificate `
  -Cert $certificate `
  -FilePath '<private-location>\VxFiles.Release.pfx' `
  -Password $pfxPassword `
  -CryptoAlgorithmOption AES256_SHA256 `
  -ChainOption EndEntityCertOnly `
  -NoProperties

Export-Certificate `
  -Cert $certificate `
  -FilePath '.\VxFiles.Release.cer' `
  -Type CERT
```

The chosen extensions follow [Microsoft's self-signed package certificate recipe](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing#create-a-self-signed-certificate): digital-signature key usage, code-signing EKU, and an end-entity rather than a certificate-authority certificate. The explicit three-year lifetime avoids yearly coworker retrust while keeping rotation scheduled; `New-SelfSignedCertificate` supports an explicit `NotAfter` value. [The PKI cmdlet reference](https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate) documents that control.

The password-protected `.pfx` contains the private key. The `.cer` contains only the public certificate. Microsoft recommends password protection for general PFX export, and `Export-PfxCertificate` requires either a password or domain-bound `ProtectTo` access. [Microsoft's PFX export guidance](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing#export-the-certificate-to-a-pfx-file) describes those protections.

After checking both exports, remove the generated certificate and private key from `CurrentUser\My`. Import the PFX only temporarily when a release must be signed, then remove that imported private-key entry again. The encrypted PFX is the controlled release input; the CER is the distributable trust input.

## Protect the release key

- Keep `VxFiles.Release.pfx` outside the repository and every Git working tree, on a BitLocker-protected volume with filesystem access restricted to the release owner. BitLocker protects volumes against offline data theft. [Microsoft's BitLocker overview](https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/) describes that protection.
- Use a unique, strong PFX password. Keep it in a password manager, not beside the PFX, not in a script, and not in shell history.
- Keep one encrypted offline backup of the PFX, separately from the password. Losing this key does not destroy installed apps, but forces certificate rotation and coworker retrust before another update can be delivered.
- Never commit, upload, email, or attach the PFX or its password. GitHub releases contain the signed MSIX, `.appinstaller`, and public CER only.
- Record the certificate subject, SHA-256 fingerprint, thumbprint, serial number, creation time, and expiry in the manual release log. Verify the public CER fingerprint through a separate trusted coworker channel before they install it.

These controls are release policy derived from the fact that a PFX contains both the certificate and its private key, while a CER is sufficient for trust. [Microsoft's PFX documentation](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-pfxcertificate) states that a PFX includes the private key; Microsoft's MSIX identity guidance explicitly says to keep the PFX private and distribute/import the public CER for trust. [See the Microsoft identity-package signing example](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps#build-and-sign-the-identity-package).

## Sign and verify every release

Sign the final MSIX once, after all package bytes are final:

```powershell
$pfxPassword = Read-Host 'VxFiles release PFX password' -AsSecureString
$certificate = Import-PfxCertificate `
  -FilePath '<private-location>\VxFiles.Release.pfx' `
  -CertStoreLocation 'Cert:\CurrentUser\My' `
  -Password $pfxPassword
try {
  signtool.exe sign `
    /fd SHA256 `
    /sha1 $certificate.Thumbprint `
    /tr 'http://timestamp.digicert.com' `
    /td SHA256 `
    '<release-output>\VxFiles-2.0.0-x64.msixbundle'
} finally {
  Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)"
  $certificate = $null
  $pfxPassword = $null
}

signtool.exe verify /pa /v '<release-output>\VxFiles-2.0.0-x64.msixbundle'
```

The release workstation trusts the matching public CER in `LocalMachine\TrustedPeople`, allowing the final policy verification to validate both package integrity and the expected trust chain. Importing the PFX only for the signing operation avoids exposing its password as a SignTool command-line argument and avoids leaving the private key in the certificate store.

MSIX must be timestamped during the signing operation; SignTool cannot add a timestamp to an already-signed app package. The package signing hash must also match its block-map hash, SHA-256 by default. [Microsoft's MSIX SignTool procedure](https://learn.microsoft.com/en-us/windows/win32/appxpkg/how-to-sign-a-package-using-signtool) documents both constraints. Microsoft's SignTool reference uses DigiCert's publicly available timestamp endpoint, and RFC 3161 with `/td SHA256` is the preferred modern timestamp form. [See the SignTool examples](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool#examples).

Timestamping matters even for a self-signed release: Windows can accept a timestamped package after the signing certificate expires, whereas an untimestamped package is evaluated at installation time and fails once that certificate is expired. An app already installed continues to run after expiry either way. [Microsoft's MSIX timestamping table](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview#timestamping) defines this behavior.

The release procedure must fail if signing, timestamping, or `signtool verify` returns a non-success status. Do not publish an untimestamped fallback under the same release.

## Coworker trust and installation

Publish `VxFiles.Release.cer` beside the first V1 release. A coworker verifies its fingerprint, then imports it from an elevated PowerShell session:

```powershell
Import-Certificate `
  -FilePath '.\VxFiles.Release.cer' `
  -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
```

The App Installer UI does not search current-user certificate stores. A self-signed package therefore requires an administrator to trust the public certificate in Local Computer `Trusted People`; Local Computer `Trusted Root Certification Authorities` also works but is explicitly not recommended. [Microsoft's App Installer troubleshooting guide](https://learn.microsoft.com/en-us/windows/msix/app-installer/troubleshoot-appinstaller-issues#trusted-certificates) states these store and elevation requirements.

Do not give coworkers the PFX and do not import it for trust. Trusting the CER gives them the public key needed to validate packages without exposing signing capability.

The certificate affects trust for every user on that machine. Remove it when VxFiles is no longer used:

```powershell
Remove-Item -LiteralPath 'Cert:\LocalMachine\TrustedPeople\<published-thumbprint>'
```

Microsoft recommends promptly removing test signing certificates when no longer necessary because local-machine trust affects all users. [See Microsoft's signature-error security guidance](https://learn.microsoft.com/en-us/windows/win32/appxpkg/how-to-troubleshoot-app-package-signature-errors#step-2-determine-the-certificate-chain-used-to-sign-the-app-package).

## Update and rotation continuity

Every VxFiles update must preserve:

- package name `VxFiles`;
- publisher string `CN=VxFiles`;
- the installed package's other stable identity metadata;
- a package version higher than the installed version;
- a valid signature from the current release certificate; and
- a timestamp made while that certificate is valid.

The `.appinstaller` `MainPackage` fields must exactly match the referenced MSIX identity fields, including name, publisher, version, architecture, and URI. [Microsoft's manual App Installer file guide](https://learn.microsoft.com/en-us/windows/msix/app-installer/how-to-create-appinstaller-file#step-3-add-the-main-package) defines that validation. The update-hosting ticket owns the descriptor and GitHub URLs, but it must consume this identity unchanged.

Schedule rotation six months before expiry:

1. Generate a new code-signing certificate with the same exact subject `CN=VxFiles`.
2. Export a new password-protected PFX and a public CER.
3. Publish the new CER fingerprint and have every coworker trust it before the first package signed by the new key.
4. Sign and timestamp subsequent packages with the new certificate while retaining package name, publisher, and monotonically increasing version.
5. Keep old timestamped release packages and the old public CER available for verification. Retire and securely destroy the old private key after the transition.

Keeping the same name and publisher preserves the package family; changing the certificate key alone still requires the new certificate to be trusted on each device. This same-subject rotation conclusion is an inference from Microsoft's package-family and signature-trust rules rather than an explicit Microsoft rotation recipe, so it must be proven with an installed V1-to-rotated-certificate update before the real rotation.

Do not change the publisher distinguished name casually. Microsoft offers publisher-bridging artifacts for a transition from an old publisher to a new publisher, but the feature starts at Windows 11 version 21H2 and requires a catalog signed by the old certificate; an untimestamped bridging catalog becomes useless when that certificate expires. [Microsoft's persistent-identity procedure](https://learn.microsoft.com/en-us/windows/msix/package/persistent-identity) documents those constraints. Keeping `CN=VxFiles` avoids introducing that Windows-version-specific mechanism.

If the private key is compromised, stop publishing immediately, remove the old CER from target devices, create a replacement certificate, pre-trust its CER, and test update continuity. A directly trusted self-signed certificate has no public CA revocation service, so this small-cohort manual response is a known V1 limitation.

## Upstream alignment

Files v4.2 already has the right minimal certificate-generation shape in `.github/scripts/Generate-SelfCertPfx.ps1`: `New-SelfSignedCertificate`, `DigitalSignature`, code-signing EKU, basic constraints, and a publisher that matches its manifest. VxFiles should retain that shape but must not reuse the script unchanged for releases:

- it hard-codes `CN=Files` instead of `CN=VxFiles`;
- it exports an unprotected PFX;
- it generates a fresh certificate on each run rather than retaining release identity; and
- the upstream CI imports that PFX into Local Machine `Root`, exposing the private key and granting broader trust than needed.

The V1 manual release flow therefore needs a small release-only signing script or runbook that accepts the stable external PFX and password at execution time. It must not add a new signing module to the application or alter the upstream project topology.

## Remaining proof

This ticket defines the zero-cost identity and lifecycle without modifying a certificate store. Before V1 acceptance, the release runbook must prove on a clean coworker-like Windows account that:

1. only the published CER is imported into `LocalMachine\TrustedPeople`;
2. the signed and timestamped `2.0.0.0` MSIX bundle installs;
3. a higher version signed with the same certificate updates it through App Installer; and
4. a disposable replacement certificate with the same `CN=VxFiles` subject can update it after its CER is pre-trusted.

The fourth check is specifically required because Microsoft documents publisher changes through persistent-identity artifacts but does not explicitly document the same-subject, new-key self-signed rotation case.
