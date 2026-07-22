# Headless Automation Action runtime

The v1 runtime is deliberately independent of Files browsing and command-surface models. The host opens an `IAutomationBarSession`, supplies immutable selection values, and implements narrow trust, state, and result-routing ports. The session exposes only `Snapshot`, `InvokeAsync`, and `CancelAsync`.

## Reproducible runtime

`scripts/automation/Acquire-Python.ps1` downloads the official CPython 3.14.6 x64 embeddable archive, verifies the pinned archive and executable SHA-256 values, and keeps it under the `artifacts` directory. Publish fails if this payload has not been acquired, and successful portable output includes it below `AutomationRuntime/Python`. Action execution accepts only that compile-time-pinned app-local interpreter, uses isolated UTF-8 mode, and never installs Python packages at runtime.

## Headless tracer

Run the actual-process tracer without launching the Files UI:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\automation\Run-HeadlessTracer.ps1 -Configuration Release
```

The tracer uses temporary action packages, real selected files, the pinned interpreter, named cancellation events, and Windows Job Objects. A pinned bootstrap waits on a host start gate, so action code cannot execute or create children until Job assignment succeeds. The tracer deterministically covers:

- successful ordered Unicode JSON request transport;
- malformed protocol, nonzero exit, stdout cap, and bounded stderr;
- cooperative cancellation, timeout, two-second host shutdown, and child-process cleanup;
- trust preservation across relocation and renewal after package, runner, or configured-tool identity changes;
- typed settings and explicit external-tool transport;
- exact Unicode `argv-paths`, command-line rejection, and age/size history eviction;
- one-run-per-action and two-run-global no-queue concurrency;
- refresh/reveal result intents carrying the captured host revision.

Local action state is stored atomically below the configured state root. Completed run summaries are separate JSON records retained for seven days and capped at 100 MiB. Selected file contents and script output are not copied into those records.
