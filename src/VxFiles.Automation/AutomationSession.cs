// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.ComponentModel;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// The headless Automation session: resolves composite action identities against the current catalog,
/// gates runs on package trust, enforces concurrency, and owns every running process tree.
/// </summary>
internal sealed class AutomationSession : IAutomationSession
{
	/// <summary>
	/// Two simultaneous runs at most, and never more than one per Automation Package. Requests beyond that
	/// are refused rather than queued, so an invocation always maps to an observable outcome.
	/// </summary>
	private const int MaximumGlobalRuns = 2;

	private const string StaleCatalogMessage = "The Automation catalog changed; capture a new invocation.";

	private static readonly TimeSpan CatalogRefreshDebounce = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(3);

	private readonly object _gate = new();
	private readonly AutomationModuleOptions _options;
	private readonly IAutomationStateStore _stateStore;
	private readonly IAutomationTrustConsent _trustConsent;
	private readonly IAutomationResultRouter _resultRouter;
	private readonly Dictionary<AutomationRunId, ActiveRunControl> _activeRunControls = [];
	private readonly HashSet<AutomationPackageId> _pendingPackageIds = [];
	private readonly ImmutableArray<FileSystemWatcher> _catalogWatchers;
	private AutomationCatalog _catalog;
	private CancellationTokenSource? _catalogRefresh;
	private AutomationSnapshot _snapshot;
	private bool _disposed;

	public AutomationSession(
		AutomationModuleOptions options,
		AutomationCatalog catalog,
		IAutomationStateStore stateStore,
		IAutomationTrustConsent trustConsent,
		IAutomationResultRouter resultRouter)
	{
		_options = options;
		_catalog = catalog;
		_stateStore = stateStore;
		_trustConsent = trustConsent;
		_resultRouter = resultRouter;
		_snapshot = new(1, 1, catalog.Snapshot.Packages, [], []);
		_catalogWatchers = options.PackageRoots
			.Where(Directory.Exists)
			.Select(CreateCatalogWatcher)
			.ToImmutableArray();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public AutomationSnapshot Snapshot
	{
		get
		{
			lock (_gate)
				return _snapshot;
		}
	}

	public async ValueTask InvokeAsync(
		AutomationInvocation invocation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(invocation);
		ThrowIfDisposed();

		var packageId = invocation.ActionId.PackageId;
		AutomationPackageDefinition package;
		AutomationActionDefinition action;
		lock (_gate)
		{
			if (invocation.CatalogRevision != _snapshot.CatalogRevision)
				throw new InvalidOperationException(StaleCatalogMessage);
			if (!_catalog.Packages.TryGetValue(packageId, out package!) ||
				!package.Actions.TryGetValue(invocation.ActionId.LocalId, out action!))
			{
				throw new InvalidOperationException($"Automation Action '{invocation.ActionId.Value}' is unavailable.");
			}

			if (IsPackageBusy(packageId))
				throw new InvalidOperationException($"Automation Package '{packageId.Value}' is already running an action.");
			if (_activeRunControls.Count + _pendingPackageIds.Count >= MaximumGlobalRuns)
				throw new InvalidOperationException("Two Automation Actions are already running.");
			_pendingPackageIds.Add(packageId);
		}

		RunPreparation? preparation;
		try
		{
			preparation = await PrepareRunAsync(package, action, invocation, cancellationToken);
		}
		catch (AutomationMissingDependencyException exception)
		{
			MarkPackageUnavailable(packageId, AutomationAvailability.MissingDependency, exception.Message);
			ReleasePending(packageId);
			throw;
		}
		catch
		{
			ReleasePending(packageId);
			throw;
		}

		if (preparation is null)
		{
			// Trust was declined; nothing ran and nothing is recorded.
			ReleasePending(packageId);
			return;
		}

		await ExecuteRunAsync(package, action, invocation, preparation, cancellationToken);
	}

	public ValueTask CancelAsync(AutomationRunId runId, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		lock (_gate)
		{
			if (_activeRunControls.TryGetValue(runId, out var control))
			{
				var current = _snapshot.ActiveRuns.First(run => run.Id == runId);
				ReplaceSnapshot(_snapshot with
				{
					ActiveRuns = _snapshot.ActiveRuns.Replace(current, current with
					{
						State = AutomationRunState.Cancelling,
						Status = "Cancelling",
					}),
				});
				control.UserCancellation.Cancel();
			}
		}

		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		ImmutableArray<FileSystemWatcher> watchers;
		CancellationTokenSource? catalogRefresh;
		lock (_gate)
		{
			if (_disposed)
				return;
			_disposed = true;
			watchers = _catalogWatchers;
			catalogRefresh = _catalogRefresh;

			// Signalling under the same lock that retires a run keeps this off a control we just disposed.
			foreach (var control in _activeRunControls.Values)
				control.ShutdownCancellation.Cancel();
		}

		catalogRefresh?.Cancel();
		catalogRefresh?.Dispose();
		foreach (var watcher in watchers)
			watcher.Dispose();

		var deadline = DateTime.UtcNow + ShutdownDrainTimeout;
		while (DateTime.UtcNow < deadline)
		{
			lock (_gate)
			{
				if (_activeRunControls.Count is 0)
					break;
			}

			await Task.Delay(25);
		}

		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Resolves configuration, obtains package trust when needed, and re-verifies that neither the package nor
	/// the catalog changed while consent was pending. Returns <see langword="null"/> when trust was declined.
	/// </summary>
	private async Task<RunPreparation?> PrepareRunAsync(
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationInvocation invocation,
		CancellationToken cancellationToken)
	{
		ValidateSelection(action, invocation.Selection);
		var packageState = await _stateStore.ReadPackageStateAsync(package.Id, cancellationToken);
		var actionSettings = await _stateStore.ReadActionSettingsAsync(invocation.ActionId, cancellationToken);
		var dependencies = AutomationDependencyResolver.Resolve(package, action, packageState, actionSettings);
		var fingerprint = AutomationTrustFingerprint.Compute(package, _options, dependencies.ExternalTools);

		if (!string.Equals(packageState.TrustedFingerprint, fingerprint.PackageFingerprint, StringComparison.Ordinal))
		{
			var accepted = await _trustConsent.RequestTrustAsync(
				new(
					package.Id,
					package.DisplayName,
					package.PackageVersion,
					package.PackagePath,
					[.. package.Actions.Values.Select(candidate => candidate.Id)],
					invocation.ActionId,
					invocation.Selection.Items.Length,
					fingerprint.PackageFingerprint,
					fingerprint.RunnerFingerprint,
					dependencies.ExternalTools),
				cancellationToken);
			if (!accepted)
				return null;

			await _stateStore.WritePackageTrustAsync(package.Id, fingerprint.PackageFingerprint, cancellationToken);
		}

		var launchFingerprint = AutomationTrustFingerprint.Compute(package, _options, dependencies.ExternalTools);
		if (!string.Equals(fingerprint.PackageFingerprint, launchFingerprint.PackageFingerprint, StringComparison.Ordinal))
			throw new InvalidOperationException("Automation Package content changed after trust approval; review and trust it again.");

		var currentCatalog = AutomationManifestCatalog.Discover(_options.CatalogOptions);
		if (!currentCatalog.Packages.TryGetValue(package.Id, out var currentPackage) ||
			!currentPackage.ManifestBytes.AsSpan().SequenceEqual(package.ManifestBytes))
		{
			throw new InvalidOperationException(StaleCatalogMessage);
		}

		lock (_gate)
		{
			if (invocation.CatalogRevision != _snapshot.CatalogRevision)
				throw new InvalidOperationException(StaleCatalogMessage);
		}

		return new(dependencies, launchFingerprint.PackageFingerprint);
	}

	private async Task ExecuteRunAsync(
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationInvocation invocation,
		RunPreparation preparation,
		CancellationToken cancellationToken)
	{
		var runId = new AutomationRunId(Guid.NewGuid());
		var control = new ActiveRunControl(
			package.Id,
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
			new CancellationTokenSource());
		var run = new AutomationRunSnapshot(
			runId,
			invocation.ActionId,
			AutomationRunState.Starting,
			invocation.Selection,
			invocation.HostRevision,
			DateTimeOffset.UtcNow,
			null,
			null,
			"Starting",
			[],
			string.Empty,
			false,
			[],
			null);
		lock (_gate)
		{
			_pendingPackageIds.Remove(package.Id);
			_activeRunControls.Add(runId, control);
			ReplaceSnapshot(_snapshot with { ActiveRuns = _snapshot.ActiveRuns.Add(run) });
		}

		AutomationProcessResult processResult;
		try
		{
			processResult = await AutomationPythonRunner.RunAsync(
				_options,
				package,
				action,
				invocation,
				runId,
				preparation.LaunchFingerprint,
				preparation.Dependencies,
				frame => ApplyFrame(runId, frame),
				control.UserCancellation.Token,
				control.ShutdownCancellation.Token);
		}
		catch (Exception exception)
		{
			processResult = AutomationProcessResult.Failed(exception.Message);
		}

		var intentResults = processResult.State is AutomationRunState.Succeeded && !processResult.Intents.IsEmpty
			? await _resultRouter.RouteAsync(
				new(runId, invocation.HostRevision, invocation.Selection.ActiveFolderPath, processResult.Intents),
				CancellationToken.None)
			: [];

		AutomationRunSnapshot terminal;
		lock (_gate)
		{
			var current = _snapshot.ActiveRuns.First(active => active.Id == runId);
			terminal = current with
			{
				State = processResult.State,
				CompletedAtUtc = DateTimeOffset.UtcNow,
				Status = processResult.Status,
				StandardError = processResult.StandardError,
				StandardErrorTruncated = processResult.StandardErrorTruncated,
				IntentResults = intentResults,
				Failure = processResult.Failure,
			};
			_activeRunControls.Remove(runId);
			control.Dispose();
			ReplaceSnapshot(_snapshot with
			{
				ActiveRuns = _snapshot.ActiveRuns.Remove(current),
				RecentRuns = _snapshot.RecentRuns.Insert(0, terminal),
			});
		}

		await _stateStore.AppendRunRecordAsync(
			new(terminal, package.PackageVersion, preparation.LaunchFingerprint),
			CancellationToken.None);
	}

	private bool IsPackageBusy(AutomationPackageId packageId)
		=> _pendingPackageIds.Contains(packageId) ||
			_activeRunControls.Values.Any(control => control.PackageId == packageId);

	private void ApplyFrame(AutomationRunId runId, AutomationOutputFrame frame)
	{
		lock (_gate)
		{
			var current = _snapshot.ActiveRuns.FirstOrDefault(run => run.Id == runId);
			if (current is null)
				return;

			var updated = current with
			{
				State = AutomationRunState.Running,
				ProgressPercent = frame.ProgressPercent ?? current.ProgressPercent,
				Status = frame.Message ?? current.Status,
				Logs = frame.Log is null ? current.Logs : current.Logs.Add(frame.Log),
			};
			ReplaceSnapshot(_snapshot with { ActiveRuns = _snapshot.ActiveRuns.Replace(current, updated) });
		}
	}

	private static void ValidateSelection(AutomationActionDefinition action, SelectionSnapshot selection)
	{
		if (selection.Items.Length < action.Selection.MinItems || selection.Items.Length > action.Selection.MaxItems)
			throw new InvalidOperationException("The selected item count is outside this action's supported range.");

		foreach (var item in selection.Items)
		{
			if (!Path.IsPathFullyQualified(item.FullPath))
				throw new InvalidOperationException("Automation selections require canonical absolute filesystem paths.");

			var declaredKind = item.Kind is SelectedPathKind.File ? "file" : "folder";
			if (!action.Selection.ItemKinds.Contains(declaredKind, StringComparer.Ordinal))
				throw new InvalidOperationException($"The action does not support selected {declaredKind} items.");

			if (item.Kind is SelectedPathKind.File && !action.Selection.Extensions.IsEmpty &&
				!action.Selection.Extensions.Contains(Path.GetExtension(item.FullPath), StringComparer.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"The action does not support '{Path.GetExtension(item.FullPath)}' files.");
			}
		}
	}

	private void ReplaceSnapshot(AutomationSnapshot replacement)
	{
		_snapshot = replacement with { Revision = _snapshot.Revision + 1 };
		PropertyChanged?.Invoke(this, new(nameof(Snapshot)));
	}

	private void MarkPackageUnavailable(AutomationPackageId packageId, AutomationAvailability availability, string diagnostic)
	{
		lock (_gate)
		{
			var package = _snapshot.Packages.FirstOrDefault(item => item.Id == packageId);
			if (package is null)
				return;

			ReplaceSnapshot(_snapshot with
			{
				Packages = _snapshot.Packages.Replace(package, package with
				{
					Availability = availability,
					Diagnostics = [diagnostic],
				}),
			});
		}
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

	private void ReleasePending(AutomationPackageId packageId)
	{
		lock (_gate)
			_pendingPackageIds.Remove(packageId);
	}

	private FileSystemWatcher CreateCatalogWatcher(string root)
	{
		var watcher = new FileSystemWatcher(root)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
			EnableRaisingEvents = true,
		};
		watcher.Changed += CatalogChanged;
		watcher.Created += CatalogChanged;
		watcher.Deleted += CatalogChanged;
		watcher.Renamed += CatalogChanged;
		return watcher;
	}

	private void CatalogChanged(object sender, FileSystemEventArgs e)
	{
		CancellationToken token;
		lock (_gate)
		{
			if (_disposed)
				return;
			_catalogRefresh?.Cancel();
			_catalogRefresh?.Dispose();
			_catalogRefresh = new();
			token = _catalogRefresh.Token;
		}

		_ = RefreshCatalogAsync(token);
	}

	private async Task RefreshCatalogAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(CatalogRefreshDebounce, cancellationToken);
			var replacement = AutomationManifestCatalog.Discover(_options.CatalogOptions);
			lock (_gate)
			{
				if (_disposed || cancellationToken.IsCancellationRequested)
					return;
				_catalog = replacement;
				ReplaceSnapshot(_snapshot with
				{
					CatalogRevision = _snapshot.CatalogRevision + 1,
					Packages = replacement.Snapshot.Packages,
				});
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// A partially written or temporarily inaccessible package never replaces the committed catalog.
		}
	}

	private sealed record RunPreparation(
		ResolvedAutomationDependencies Dependencies,
		string LaunchFingerprint);

	private sealed class ActiveRunControl(
		AutomationPackageId packageId,
		CancellationTokenSource userCancellation,
		CancellationTokenSource shutdownCancellation) : IDisposable
	{
		public AutomationPackageId PackageId { get; } = packageId;

		public CancellationTokenSource UserCancellation { get; } = userCancellation;

		public CancellationTokenSource ShutdownCancellation { get; } = shutdownCancellation;

		public void Dispose()
		{
			UserCancellation.Dispose();
			ShutdownCancellation.Dispose();
		}
	}
}
