# VxFiles

VxFiles is a personalized Windows file manager derived from Files Community and intended for controlled sharing with coworkers.

## Language

**VxFiles**
The public identity used by the installed application, executable, package, protocol, and distributions. Inherited `Files.*` namespaces, project names, libraries, extension names, task identifiers, COM identities, and persistence identifiers remain unchanged.

**Installed Distribution**
The only supported VxFiles distribution: a self-contained .NET 10 x64 unpackaged app installed per-user through Velopack and downloaded or updated from the VxFiles GitHub fork. V1 does not ship MSIX or portable ZIP assets.

**Automatic Update**
A newer release found on launch is downloaded in the background and installed by the Velopack updater as the app exits, so the next launch runs it without anyone clicking. The address-bar update button takes the same update immediately instead.

**Downstream Layer**
The intentionally small, reviewable set of VxFiles-owned differences applied to a tagged Files Community baseline.

**Stable-Tag Intake**
Future upstream work starts from an accepted VxFiles line, merges a named stable Files release tag on a dedicated sync branch, and removes downstream hunks that upstream has made redundant. VxFiles does not continuously follow `upstream/main`.

Every stable-tag intake must follow `docs/VXFILES-UPSTREAM-MERGE-CHECKLIST.md` so the unpackaged compatibility layer, branding, and release path are retained.

**Automation Package**
A VxFiles automation install, update, validation, and trust unit. One package contains a `vxpackage.json` manifest and one or more Automation Actions, and appears as a root item in the Tools TreeView.

**Automation Action**
A named Python automation inside an Automation Package. Actions are independently runnable against an immutable folder-and-selection snapshot and appear as children of their package in the filterable Tools tab.

V1 does not import settings or data from Files or earlier VxFiles distributions.
