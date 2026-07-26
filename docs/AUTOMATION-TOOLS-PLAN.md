# Automation Tools plan

## Decision summary

- Add a third tab named **Tools** to the existing Info Pane, after **Details** and **Preview**.
- Keep **Automation Action** as the internal domain term. “Tools” is only the shorter user-facing label.
- Treat the placement as the right/bottom Info Pane hosted by `MainPage`, not the left navigation sidebar and not the left file pane in dual-pane mode.
- Recover the headless runtime and its tests selectively from `codex/legacy-pre-v2-wip`; do not merge the archived branch or restore its horizontal Automation Bar.
- Keep the runtime in VxFiles-owned projects so future Files Community merges normally touch only the small Info Pane host seam.
- Ship a pinned app-local Python runtime inside the self-contained Velopack installation. Users must not need Python, administrator rights, or a package manager.
- Do not provide backward compatibility with pre-V2 Automation Actions.
- Make an Automation Package the install, update, validation, and trust unit. Each package can expose multiple independently runnable Automation Actions.
- Display packages as root items and actions as children in the Tools TreeView.

## V2 minimum usable feature

The Tools tab contains:

1. A search box that filters packages and actions by display name, then by description.
2. A virtualized TreeView sorted by package and action display name.
3. One root row per package with identity, version, aggregate health, and package commands.
4. One child row per action with its name, description, availability, and Run button.
5. Clear states for available, trust required, incompatible selection, missing dependency, invalid, and running.
6. A compact active-run area with status and Cancel.
7. A recent-runs area containing bounded status summaries and diagnostics.
8. An **Open packages folder** command for installing a package by copying its folder.
9. Empty and error states that explain where packages are loaded from.

The selected action runs against an immutable snapshot of the active filesystem folder and selected files/folders. Changing tabs, folders, or selection after invocation must not change the captured input.

## Explicit non-goals

- Scheduled or background automation.
- A visual action editor.
- An online marketplace or automatic action download.
- PowerShell, JavaScript, arbitrary shell-command, or plugin-host support.
- Importing state or packages from the archived VxFiles implementation.
- Editing Files Community’s built-in `IAction` command system. Those commands and Automation Actions are different domains.
- Portable distribution, MSIX identity, elevation, or machine-wide installation.
- More than x64 for the first Automation Tools release.

## Module design

### Headless automation module

Use two VxFiles-owned projects:

- `VxFiles.Automation.Abstractions`: immutable domain values and the small session interface.
- `VxFiles.Automation`: manifest discovery, validation, trust fingerprints, state, process isolation, Python execution, output parsing, concurrency, cancellation, and run history.

The external interface should expose only:

```text
Snapshot
InvokeAsync(invocation)
CancelAsync(runId)
```

The archived names `IAutomationBarSession` and `AutomationBarSnapshot` leak the deleted horizontal-bar presentation. Rename them to `IAutomationSession` and `AutomationSnapshot` while recovering the projects. The implementation remains headless and has no dependency on WinUI or Files browsing models.

The deletion test for this module passes: removing it would spread manifest validation, trust, execution safety, cancellation, and state management into the app UI.

### Files app adapter

The app adapter owns only Files-specific knowledge:

- capture the current folder, selection, item kinds, and a host revision;
- display trust consent;
- route successful result intents such as refresh-current-folder and reveal-paths;
- choose VxFiles data, temporary, bundled-action, and user-action directories;
- open and dispose one automation session for the Tools pane.

The adapter must return immutable values from the abstraction project. The headless module must not reference `ListedItem`, `IContentPageContext`, `MainWindow`, or WinUI types.

### Tools pane module

Create a new `AutomationToolsPane` user control and `AutomationToolsViewModel`. `InfoPane.xaml` should only:

- add the third tab selector;
- host the new control;
- switch visibility from the persisted `InfoPaneTabs.Tools` value.

The Tools view model projects the hierarchical `AutomationSnapshot` into filterable package roots and action children, and forwards Run/Cancel requests across the session interface. It must not reconstruct package relationships or contain manifest parsing, filesystem discovery, trust hashing, process execution, or output-protocol logic.

Do not add Tools behavior to `InfoPaneViewModel`; that view model remains responsible for Details and Preview selection data. This preserves locality and prevents automation refreshes from interfering with preview loading.

## Archived code recovery

Recover and adapt these areas from commit `3907827f0` on `codex/legacy-pre-v2-wip`:

- `src/VxFiles.Automation.Abstractions`
- `src/VxFiles.Automation`
- `tests/VxFiles.Automation.Tests`
- the manifest schema and bundled selection-list tracer action
- the pinned-Python acquisition metadata and script
- the Files host-context, trust-consent, and result-routing adapters

Use these only as design/reference material:

- `AutomationBar.xaml`
- `AutomationBarViewModel`
- old MainPage placement
- archived packaging and portable-release integration

Before accepting recovered runtime code:

- target the current .NET 10 properties and x64 release path;
- update “Bar” terminology at the external seam;
- replace portable-specific build messages and paths with installed Velopack terminology;
- confirm all native declarations use the project’s existing CsWin32 approach;
- re-run the focused automation test project;
- verify the runtime shuts down all child processes when VxFiles exits.

## Automation Package model

Each Automation Package is a folder containing one `vxpackage.json` manifest, one or more actions, and optional shared Python modules and assets. The new schema starts at version 1 and has no compatibility contract with the archived single-action `action.json` format.

Package-level fields include:

- publisher-qualified package ID, semantic package version, display name, description, author, icon, and minimum VxFiles host version;
- pinned Python compatibility;
- optional external-tool definitions shared by actions;
- an `actions` array.

Each action contains:

- a local ID unique inside the package, display name, description, optional icon, and entry point;
- selection rules for item count, file/folder kinds, and extensions;
- timeout, output cap, and concurrency policy;
- `json-stdin` or exact `argv-paths` input;
- `ndjson-v1` or exit-code output;
- optional action-specific typed settings and references to package external tools.

The stable executable identity is the composite `<package-id>/<action-id>`. The headless snapshot preserves the hierarchy as package snapshots containing action snapshots so callers do not rebuild domain relationships.

Discover packages from:

- bundled packages under the installed VxFiles directory;
- user packages under `%LocalAppData%\VxFiles Community\VxFiles\Automation\Packages`.

Package identity, version, duplicate action IDs, reparse points, and paths escaping the package are fatal and disable the package root. An invalid action remains visible and disabled without preventing valid sibling actions from running. Malformed packages and action diagnostics remain visible in the TreeView so users can find and repair them.

While filtering:

- a package-name match shows all children;
- an action-name match shows its package and matching children;
- matching roots expand automatically;
- clearing the filter restores the previous expansion state.

## Trust and process safety

- Require package-level consent before the first action run and whenever package content, runner, or configured-tool identity changes.
- Show package identity, package location, contained actions, selected-item count, and external executables in the consent dialog.
- Start the pinned interpreter in isolated UTF-8 mode.
- Assign the process to a kill-on-close Windows Job Object before action code can proceed.
- Enforce timeout, cancellation, output limits, bounded stderr, and child-process cleanup.
- Allow one run per package and at most two simultaneous runs globally; do not queue silently.
- Never construct a shell command line from selected paths.
- Store only settings, trust fingerprints, and bounded run summaries. Do not copy selected file contents or full action output into history.

## Delivery sequence

### 1. Establish multi-action package discovery

- Define package/action identities, hierarchical snapshots, and the `vxpackage.json` schema.
- Recover and adapt only the archived catalog and validation behavior needed to discover multi-action packages.
- Prove valid siblings survive an invalid action while package-fatal errors disable the root.

Acceptance: the headless catalog discovers a package with multiple actions, returns their stable composite identities, and reports package/action diagnostics through focused tests.

### 2. Recover execution and state

- Restore the two automation projects and focused tests without connecting them to `Files.App`.
- Adapt them to .NET 10, current repo properties, current CsWin32 generation, CRLF, and installed-app terminology.
- Prove catalog validation, trust renewal, process isolation, cancellation, timeouts, output limits, concurrency, and state retention through tests and the real-process tracer.

Acceptance: the automation projects build and their focused tests/tracer pass without launching VxFiles.

### 3. Make the installed runtime self-contained

- Restore the hash-pinned Python acquisition script.
- Include Python and runner scripts in `dotnet publish` and therefore in the Velopack full package and installer.
- Teach the release builder to acquire/verify the pinned runtime before publishing.
- Add a bundled harmless selection-list action as an end-to-end tracer.

Acceptance: a clean standard-user machine can run the bundled tracer action without installed Python or elevation, and no portable asset is produced.

### 4. Add the read-only Tools catalog

- Add `InfoPaneTabs.Tools`, localization, the third tab button, and persisted selection.
- Add `AutomationToolsPane` with the filterable package/action TreeView, empty/error states, and Open packages folder.
- Open the headless session lazily when Tools is first selected.
- Display availability and diagnostics but keep Run disabled in this step.

Acceptance: users can install, discover, expand, filter, and diagnose packages and actions without affecting Details or Preview.

### 5. Connect invocation and trust

- Capture the active folder and selection through the app adapter.
- Enable Run only when both the action policy and current selection allow it.
- Add trust consent, active status, cancellation, recent results, refresh, and reveal result routing.
- Dispose the session and kill active process trees when the window closes.

Acceptance: success, failure, timeout, cancellation, selection changes, folder changes, trust changes, and app shutdown all behave deterministically.

### 6. Release hardening

- Build `Files.App` Release/x64 and run focused automation tests plus the real-process tracer.
- Install through Velopack on a clean standard-user machine.
- Test action discovery from bundled and user roots, filtering, trust renewal, missing external tools, Unicode paths, UNC paths, and update from the preceding VxFiles release.
- Update the release runbook and upstream merge checklist with the exact automation-owned touch points.

Acceptance: the installed app passes the manual matrix and future upstream merges can identify the entire downstream feature from the checklist.

## Expected upstream-conflict surface

Files Community merge conflicts should normally be limited to:

- `Files.slnx`
- `src/Files.App/Files.App.csproj`
- `src/Files.App/UserControls/Pane/InfoPane.xaml`
- `src/Files.App/Data/Enums/InfoPaneTabs.cs`
- dependency registration/composition
- localization resources
- the VxFiles release builder

All runtime behavior, tests, package validation, and process safety remain in VxFiles-owned paths. Avoid copying Files types or editing broad Files command, selection, navigation, or settings modules.

## Confirmed implementation decisions

1. Tools is the third tab in the existing right/bottom Info Pane.
2. **Tools** is the visible tab label; **Automation Action** and **Automation Package** are domain terms.
3. V2 supports Python Automation Packages only.
4. Manual package-folder copy plus **Open packages folder** is sufficient installation UX for V2.
5. One package can contain multiple actions and appears as a TreeView root with action children.
