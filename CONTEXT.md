# VxFiles

VxFiles is a personalized Windows file manager derived from Files Community and intended for controlled sharing with coworkers.

## Language

**VxFiles**
The public identity used by the installed application, executable, package, protocol, and distributions. Inherited `Files.*` namespaces, project names, libraries, extension names, task identifiers, COM identities, and persistence identifiers remain unchanged.

**Installed Distribution**
The only supported VxFiles distribution: a self-contained .NET 10 x64 unpackaged app installed per-user through Velopack and downloaded or updated from the VxFiles GitHub fork. V1 does not ship MSIX or portable ZIP assets.

**Downstream Layer**
The intentionally small, reviewable set of VxFiles-owned differences applied to a tagged Files Community baseline.

**Stable-Tag Intake**
Future upstream work starts from an accepted VxFiles line, merges a named stable Files release tag on a dedicated sync branch, and removes downstream hunks that upstream has made redundant. VxFiles does not continuously follow `upstream/main`.

Every stable-tag intake must follow `docs/VXFILES-UPSTREAM-MERGE-CHECKLIST.md` so the unpackaged compatibility layer, branding, and release path are retained.

**Automation Action**
A future named automation discoverable through a filterable third right-pane tab after Details and Preview. Automation Actions are outside V1.

V1 does not import settings or data from Files or earlier VxFiles distributions.
