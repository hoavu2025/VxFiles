# VxFiles

VxFiles is a personalized Windows file manager derived from Files Community and intended for controlled sharing with coworkers.

## Language

**VxFiles**
The public identity used by the installed application, executable, package, protocol, and distributions. Inherited `Files.*` namespaces, project names, libraries, extension names, task identifiers, COM identities, and persistence identifiers remain unchanged.

**Installed Distribution**
The only supported VxFiles distribution: a self-contained .NET 10 x64 unpackaged app installed per-user through Velopack and downloaded or updated from the VxFiles GitHub fork. V1 does not ship MSIX or portable ZIP assets.

**Automatic Update**
A newer release found on launch, or by the hourly re-check while the app runs, is downloaded in the background and staged. The Velopack updater installs it as the app exits, so the next launch runs it without anyone clicking. Taking the update immediately runs the same install through the same shutdown; only whether the app relaunches differs. Nothing waits for consent, and nothing can be skipped or deferred.

**Update Surface**
Where a staged Automatic Update becomes visible: a dot on the Settings icon in the sidebar footer, and the update card at the top of the About page carrying the pending version, its release notes, the last successful check, and a restart. It informs and never gates — ignoring it changes nothing except when the update lands. The card is absent unless Velopack installed the running copy.

**Downstream Layer**
The intentionally small, reviewable set of VxFiles-owned differences applied to a tagged Files Community baseline.

**Stable-Tag Intake**
Future upstream work starts from an accepted VxFiles line, merges a named stable Files release tag on a dedicated sync branch, and removes downstream hunks that upstream has made redundant. VxFiles does not continuously follow `upstream/main`.

Every stable-tag intake must follow `docs/VXFILES-UPSTREAM-MERGE-CHECKLIST.md` so the unpackaged compatibility layer, branding, and release path are retained.

**Automation Package**
A VxFiles automation install, update, validation, and trust unit. One package contains a `vxpackage.json` manifest and one or more Automation Actions, and appears as a root item in the Tools TreeView.

**Automation Action**
A named Python automation inside an Automation Package. Actions are independently runnable against an immutable folder-and-selection snapshot and appear as children of their package in the filterable Tools tab.

**Tools Tab**
The third Info Pane tab, after Details and Preview. It lists discovered Automation Packages as TreeView roots with their Automation Actions as children, filterable by name and description, and is where actions are run, watched, and cancelled. The headless session opens the first time the tab is shown, so an app that never opens Tools never discovers packages.

**Selection Policy**
What an Automation Action declares it accepts: how many items, of which kinds, with which extensions. One evaluator in `VxFiles.Automation.Abstractions` answers it for both the Tools tab's Run button and the session's own admission check, so a button is never enabled for a run the session would refuse.

**Package Trust**
Consent granted to a whole Automation Package, recorded against a fingerprint of its content, its runner, and the external tools it resolves. It is requested before the package's first run and again whenever that fingerprint moves, and it covers every action the package contains rather than the one that triggered the prompt.

**Automation Payload**
The app-local files that make Automation work on a clean install: the hash-pinned CPython interpreter under `AutomationRuntime\Python`, the runner scripts beside it, and the bundled `vxfiles.tracer` package under `AutomationPackages`. It ships inside the ordinary Velopack release, so no user installs Python and no action ever runs on an interpreter found on PATH.

V1 does not import settings or data from Files or earlier VxFiles distributions.
