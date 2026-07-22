// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed class AutomationBarSession : IAutomationBarSession
{
	private readonly object _gate = new();
	private readonly AutomationModuleOptions _options;
	private AutomationCatalog _catalog;
	private readonly IAutomationStateStore _stateStore;
	private readonly IAutomationTrustConsent _trustConsent;
	private readonly IAutomationResultRouter _resultRouter;
	private readonly Dictionary<AutomationRunId, ActiveRunControl> _activeRunControls = [];
	private readonly HashSet<AutomationActionId> _pendingActionIds = [];
	private readonly ImmutableArray<FileSystemWatcher> _catalogWatchers;
	private CancellationTokenSource? _catalogRefresh;
	private AutomationBarSnapshot _snapshot;
	private bool _disposed;

	public AutomationBarSession(
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
		_snapshot = new AutomationBarSnapshot(1, 1, catalog.Actions, [], []);
		_catalogWatchers = options.ActionRoots
			.Where(Directory.Exists)
			.Select(CreateCatalogWatcher)
			.ToImmutableArray();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public AutomationBarSnapshot Snapshot
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
		ThrowIfDisposed();
		AutomationPackage package;
		lock (_gate)
		{
			if (invocation.CatalogRevision != _snapshot.CatalogRevision)
				throw new InvalidOperationException("The Automation Action catalog changed; capture a new invocation.");
			if (!_catalog.Packages.TryGetValue(invocation.ActionId.Value, out package!))
				throw new InvalidOperationException($"Automation Action '{invocation.ActionId.Value}' is unavailable.");
			if (_activeRunControls.Count + _pendingActionIds.Count >= 2)
				throw new InvalidOperationException("Two Automation Actions are already running.");
			if (_pendingActionIds.Contains(invocation.ActionId) || _snapshot.ActiveRuns.Any(run => run.ActionId == invocation.ActionId))
				return;
			_pendingActionIds.Add(invocation.ActionId);
		}

		AutomationActionState actionState;
		ResolvedAutomationDependencies dependencies;
		AutomationFingerprint fingerprint;
		AutomationFingerprint launchFingerprint;
		try
		{
			ValidateSelection(package.Manifest, invocation.Selection);
			actionState = await _stateStore.ReadActionStateAsync(invocation.ActionId, cancellationToken);
			dependencies = AutomationDependencyResolver.Resolve(package.Manifest, actionState);
			fingerprint = AutomationTrustFingerprint.Compute(package, _options, dependencies.ExternalTools);
			if (!string.Equals(actionState.TrustedFingerprint, fingerprint.ActionFingerprint, StringComparison.Ordinal))
			{
				var accepted = await _trustConsent.RequestTrustAsync(
					new AutomationTrustRequest(
						invocation.ActionId,
						package.Manifest.DisplayName,
						package.Manifest.PackageVersion,
						package.PackagePath,
						invocation.Selection.Items.Length,
						fingerprint.ActionFingerprint,
						fingerprint.RunnerFingerprint,
						dependencies.ExternalTools),
					cancellationToken);
				if (!accepted)
				{
					ReleasePending(invocation.ActionId);
					return;
				}

				await _stateStore.WriteTrustedFingerprintAsync(invocation.ActionId, fingerprint.ActionFingerprint, cancellationToken);
			}

			launchFingerprint = AutomationTrustFingerprint.Compute(package, _options, dependencies.ExternalTools);
			if (!string.Equals(fingerprint.ActionFingerprint, launchFingerprint.ActionFingerprint, StringComparison.Ordinal))
				throw new InvalidOperationException("Automation Action content changed after trust approval; review and trust it again.");
			var currentCatalog = AutomationManifestCatalog.Discover(_options);
			if (!currentCatalog.Packages.TryGetValue(invocation.ActionId.Value, out var currentPackage) ||
				!currentPackage.Manifest.ManifestBytes.AsSpan().SequenceEqual(package.Manifest.ManifestBytes))
			{
				throw new InvalidOperationException("The Automation Action catalog changed; capture a new invocation.");
			}
			lock (_gate)
			{
				if (invocation.CatalogRevision != _snapshot.CatalogRevision)
					throw new InvalidOperationException("The Automation Action catalog changed; capture a new invocation.");
			}
		}
		catch (InvalidOperationException ex)
		{
			if (ex.Message.StartsWith("Configure the required external tool", StringComparison.Ordinal))
				MarkActionUnavailable(invocation.ActionId, AutomationActionAvailability.MissingDependency, ex.Message);
			ReleasePending(invocation.ActionId);
			throw;
		}
		catch
		{
			ReleasePending(invocation.ActionId);
			throw;
		}

		var runId = new AutomationRunId(Guid.NewGuid());
		using var userCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using var shutdownCancellation = new CancellationTokenSource();
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
			_pendingActionIds.Remove(invocation.ActionId);
			_activeRunControls.Add(runId, new ActiveRunControl(userCancellation, shutdownCancellation));
			ReplaceSnapshot(_snapshot with { ActiveRuns = _snapshot.ActiveRuns.Add(run) });
		}

		AutomationProcessResult processResult;
		try
		{
			processResult = await AutomationPythonRunner.RunAsync(
				_options,
				package,
				invocation,
				runId,
				launchFingerprint.ActionFingerprint,
				dependencies,
				frame => ApplyFrame(runId, frame),
				userCancellation.Token,
				shutdownCancellation.Token);
		}
		catch (Exception ex)
		{
			processResult = AutomationProcessResult.Failed(ex.Message);
		}

		var intentResults = processResult.State is AutomationRunState.Succeeded && !processResult.Intents.IsEmpty
			? await _resultRouter.RouteAsync(
				new AutomationResultRoutingRequest(runId, invocation.HostRevision, invocation.Selection.ActiveFolderPath, processResult.Intents),
				CancellationToken.None)
			: ImmutableArray<AutomationIntentResult>.Empty;

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
			ReplaceSnapshot(_snapshot with
			{
				ActiveRuns = _snapshot.ActiveRuns.Remove(current),
				RecentRuns = _snapshot.RecentRuns.Insert(0, terminal),
			});
		}

		await _stateStore.AppendRunRecordAsync(
			new AutomationRunRecord(terminal, package.Manifest.PackageVersion, launchFingerprint.ActionFingerprint),
			CancellationToken.None);
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
		List<ActiveRunControl> active;
		ImmutableArray<FileSystemWatcher> watchers;
		CancellationTokenSource? catalogRefresh;
		lock (_gate)
		{
			if (_disposed)
				return;
			_disposed = true;
			active = [.. _activeRunControls.Values];
			watchers = _catalogWatchers;
			catalogRefresh = _catalogRefresh;
		}
		catalogRefresh?.Cancel();
		catalogRefresh?.Dispose();
		foreach (var watcher in watchers)
			watcher.Dispose();

		foreach (var control in active)
			control.ShutdownCancellation.Cancel();

		var deadline = DateTime.UtcNow.AddSeconds(3);
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

	private static void ValidateSelection(ValidatedAutomationManifest manifest, SelectionSnapshot selection)
	{
		if (selection.Items.Length < manifest.Selection.MinItems || selection.Items.Length > manifest.Selection.MaxItems)
			throw new InvalidOperationException("The selected item count is outside this action's supported range.");
		foreach (var item in selection.Items)
		{
			if (!Path.IsPathFullyQualified(item.FullPath))
				throw new InvalidOperationException("Automation selections require canonical absolute filesystem paths.");
			var declaredKind = item.Kind is SelectedPathKind.File ? "file" : "folder";
			if (!manifest.Selection.ItemKinds.Contains(declaredKind, StringComparer.Ordinal))
				throw new InvalidOperationException($"The action does not support selected {declaredKind} items.");
			if (item.Kind is SelectedPathKind.File && !manifest.Selection.Extensions.IsEmpty &&
				!manifest.Selection.Extensions.Contains(Path.GetExtension(item.FullPath), StringComparer.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"The action does not support '{Path.GetExtension(item.FullPath)}' files.");
			}
		}
	}

	private void ReplaceSnapshot(AutomationBarSnapshot replacement)
	{
		_snapshot = replacement with { Revision = _snapshot.Revision + 1 };
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
	}

	private void MarkActionUnavailable(AutomationActionId actionId, AutomationActionAvailability availability, string diagnostic)
	{
		lock (_gate)
		{
			var action = _snapshot.Actions.First(item => item.Id == actionId);
			ReplaceSnapshot(_snapshot with
			{
				Actions = _snapshot.Actions.Replace(action, action with
				{
					Availability = availability,
					Diagnostics = [diagnostic],
				}),
			});
		}
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

	private void ReleasePending(AutomationActionId actionId)
	{
		lock (_gate)
			_pendingActionIds.Remove(actionId);
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
			_catalogRefresh = new CancellationTokenSource();
			token = _catalogRefresh.Token;
		}
		_ = RefreshCatalogAsync(token);
	}

	private async Task RefreshCatalogAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(150, cancellationToken);
			var replacement = AutomationManifestCatalog.Discover(_options);
			lock (_gate)
			{
				if (_disposed || cancellationToken.IsCancellationRequested)
					return;
				_catalog = MergeWithLastValidPackages(replacement);
				ReplaceSnapshot(_snapshot with
				{
					CatalogRevision = _snapshot.CatalogRevision + 1,
					Actions = _catalog.Actions,
				});
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (IOException)
		{
			// A partially-written package never replaces the last committed catalog.
		}
		catch (UnauthorizedAccessException)
		{
			// A temporarily inaccessible package never replaces the last committed catalog.
		}
	}

	private AutomationCatalog MergeWithLastValidPackages(AutomationCatalog replacement)
	{
		var packages = replacement.Packages.ToBuilder();
		var actions = replacement.Actions.ToBuilder();
		foreach (var invalid in replacement.InvalidPackagesByPath)
		{
			var lastValid = _catalog.Packages.Values.FirstOrDefault(package =>
				string.Equals(package.PackagePath, invalid.Key, StringComparison.OrdinalIgnoreCase));
			if (lastValid is null)
				continue;
			packages[lastValid.Manifest.Id] = lastValid;
			actions.Remove(invalid.Value);
			var previous = _snapshot.Actions.First(action => action.Id.Value == lastValid.Manifest.Id);
			actions.Add(previous with
			{
				Diagnostics = invalid.Value.Diagnostics.Add("The latest package change is invalid; the last valid catalog entry remains active."),
			});
		}
		return replacement with { Actions = actions.ToImmutable(), Packages = packages.ToImmutable() };
	}

	private sealed record ActiveRunControl(
		CancellationTokenSource UserCancellation,
		CancellationTokenSource ShutdownCancellation);
}
