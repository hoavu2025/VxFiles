AGENTS.md

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `hoavu2025/VxFiles`; external pull requests are not a triage surface. See `docs/agents/issue-tracker.md`.

### Triage labels

The repository uses the five canonical triage-state labels without aliases. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository with root `CONTEXT.md` and `docs/adr/`. See `docs/agents/domain.md`.

### Build

`dotnet build` cannot build `src/Files.App` — it dies in the XAML compiler with a masked internal error that looks like broken markup and is not. Use MSBuild. See `docs/agents/build.md`.
