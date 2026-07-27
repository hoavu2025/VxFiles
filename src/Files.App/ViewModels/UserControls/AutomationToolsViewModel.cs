// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace Files.App.ViewModels.UserControls
{
	/// <summary>
	/// Projects the headless Automation catalog into the filterable package/action tree shown by the Tools tab,
	/// and drives runs from it.
	/// </summary>
	/// <remarks>
	/// This view model reads snapshots and calls the session. Manifest parsing, filesystem discovery, trust, and
	/// execution all stay inside the Automation module, and admission is decided by the same
	/// <see cref="AutomationSelectionRules"/> the session enforces, so a Run button can never disagree with what
	/// invoking would actually do.
	///
	/// <para>
	/// Registered as a singleton and never torn down. Switching away from the Tools tab unloads the pane but keeps
	/// this alive, so a run started there keeps reporting and is still there on the way back. The session it
	/// subscribes to outlives it too, and is closed by <c>IAutomationSessionService</c> during window shutdown.
	/// </para>
	/// </remarks>
	public sealed partial class AutomationToolsViewModel : ObservableObject
	{
		private readonly IAutomationSessionService _sessionService = Ioc.Default.GetRequiredService<IAutomationSessionService>();
		private readonly IAutomationHostContext _hostContext = Ioc.Default.GetRequiredService<IAutomationHostContext>();

		/// <summary>
		/// Every discovered package, in catalog order. <see cref="Packages"/> is the filtered view of this.
		/// </summary>
		private readonly List<AutomationPackageItem> _allPackages = [];

		/// <summary>
		/// Expansion captured when filtering started, so clearing the filter restores what the user had open
		/// rather than the auto-expansion filtering forced.
		/// </summary>
		private readonly Dictionary<string, bool> _expansionBeforeFilter = new(StringComparer.Ordinal);

		private IAutomationSession? _session;
		private long _catalogRevision = -1;
		private bool _isFiltering;
		private bool _isLoading;

		/// <summary>
		/// Actions whose invocation has been started here but has not yet surfaced as an active run.
		/// </summary>
		/// <remarks>
		/// An invocation waiting at its trust prompt already occupies a slot in the session, but there is nothing
		/// in the snapshot to see it by. Counting only the snapshot would leave a second Run button enabled for an
		/// invocation the session is going to refuse, and the refusal would land in the failure bar as if
		/// something had gone wrong.
		/// </remarks>
		private readonly HashSet<AutomationActionId> _invoking = [];

		private string _filter = string.Empty;
		private AutomationToolsState _state = AutomationToolsState.Loading;
		private string _runFailure = string.Empty;

		public ObservableCollection<AutomationPackageItem> Packages { get; } = [];

		/// <summary>
		/// Gets the runs currently in flight, newest last, so a started action is visible while it works.
		/// </summary>
		public ObservableCollection<AutomationRunItem> ActiveRuns { get; } = [];

		/// <summary>
		/// Gets the bounded tail of finished runs, newest first.
		/// </summary>
		public ObservableCollection<AutomationRunItem> RecentRuns { get; } = [];

		public bool HasActiveRuns => ActiveRuns.Count is not 0;

		public bool HasRecentRuns => RecentRuns.Count is not 0;

		public string PackagesFolderPath => _sessionService.UserPackagesPath;

		public string Filter
		{
			get => _filter;
			set
			{
				if (SetProperty(ref _filter, value))
					ApplyFilter();
			}
		}

		/// <summary>
		/// Gets the localized reason the last Run press did not start anything, or an empty string.
		/// </summary>
		/// <remarks>
		/// The session's own refusals are unlocalized developer text naming internal concepts, so the exception is
		/// logged and this says the one thing the user can act on.
		/// </remarks>
		public string RunFailure
		{
			get => _runFailure;
			private set
			{
				if (SetProperty(ref _runFailure, value))
					OnPropertyChanged(nameof(HasRunFailure));
			}
		}

		public bool HasRunFailure => RunFailure.Length is not 0;

		public AutomationToolsState State
		{
			get => _state;
			private set
			{
				if (SetProperty(ref _state, value))
				{
					OnPropertyChanged(nameof(ShowTree));
					OnPropertyChanged(nameof(ShowPackagesFolderHint));
					OnPropertyChanged(nameof(Message));
				}
			}
		}

		public bool ShowTree => State is AutomationToolsState.Ready;

		/// <summary>
		/// Gets whether to explain where packages are installed from. Saying so under "Automation is
		/// unavailable" would send the user to copy folders that were never the problem.
		/// </summary>
		public bool ShowPackagesFolderHint => State is AutomationToolsState.Empty or AutomationToolsState.NoMatches;

		/// <summary>
		/// Gets the localized explanation shown in place of the tree, or an empty string when the tree is shown.
		/// </summary>
		/// <remarks>
		/// The underlying exception is logged rather than shown. It is unlocalized developer text such as
		/// "Run scripts/automation/Acquire-Python.ps1", which means nothing to the person reading this pane.
		/// </remarks>
		public string Message => State switch
		{
			AutomationToolsState.Loading => Strings.AutomationToolsLoading.GetLocalizedResource(),
			AutomationToolsState.Empty => Strings.AutomationToolsEmpty.GetLocalizedResource(),
			AutomationToolsState.NoMatches => Strings.AutomationToolsNoMatches.GetLocalizedResource(),
			AutomationToolsState.Unavailable => Strings.AutomationToolsUnavailable.GetLocalizedResource(),
			_ => string.Empty,
		};

		/// <summary>
		/// Opens the headless session the first time the Tools tab is shown, then keeps the tree in step with
		/// the catalog.
		/// </summary>
		public async Task EnsureLoadedAsync()
		{
			if (_session is not null || _isLoading)
				return;

			_isLoading = true;
			State = AutomationToolsState.Loading;

			try
			{
				var session = await _sessionService.GetSessionAsync();
				session.PropertyChanged += Session_PropertyChanged;
				_hostContext.PropertyChanged += HostContext_PropertyChanged;
				_session = session;
				ApplySnapshot(session.Snapshot);
			}
			catch (Exception ex)
			{
				// Most often the pinned interpreter is missing from a build that never ran the Automation
				// payload. Say so in the pane instead of leaving an empty tree that looks like "no packages".
				App.Logger.LogWarning(ex, "Automation Tools could not open the headless session");
				State = AutomationToolsState.Unavailable;
			}
			finally
			{
				_isLoading = false;
			}
		}

		/// <summary>
		/// Opens the user packages folder in a new tab, which is how a package is installed: copy its folder in.
		/// </summary>
		[RelayCommand]
		private Task OpenPackagesFolderAsync()
			=> NavigationHelpers.OpenPathInNewTab(_sessionService.EnsureUserPackagesFolder());

		/// <summary>
		/// Starts one Automation Action against the folder and selection as they stand right now.
		/// </summary>
		/// <remarks>
		/// The capture used for the eligibility re-check is the same object handed to the session. Capturing twice
		/// would leave a window in which the user changes the selection between the two, and the action would run
		/// on something other than what was just approved.
		/// </remarks>
		private async Task RunActionAsync(AutomationActionItem item)
		{
			if (_session is not { } session || item.Snapshot.Selection is not { } policy)
				return;

			RunFailure = string.Empty;

			if (!_hostContext.TryCapture(out var selection) ||
				AutomationSelectionRules.Evaluate(policy, selection) is not AutomationSelectionEligibility.Eligible)
			{
				// The context moved between the button being enabled and this click. Nothing is started, and the
				// rows are brought back in line with what is actually true now.
				RefreshRunAvailability();
				return;
			}

			_invoking.Add(item.Snapshot.Id);
			RefreshRunAvailability();

			try
			{
				// The revision this row was projected from, not the session's current one. Passing the live value
				// would defeat the session's own staleness guard: a row built from a superseded catalog would be
				// admitted instead of refused, and the action that ran would not be the one the user clicked.
				await session.InvokeAsync(new(item.Snapshot.Id, _catalogRevision, _hostContext.Revision, selection));
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Automation Action '{ActionId}' could not be started", item.Snapshot.Id.Value);
				RunFailure = Strings.AutomationToolsRunFailed.GetLocalizedResource();
			}
			finally
			{
				_invoking.Remove(item.Snapshot.Id);
				RefreshRunAvailability();
			}
		}

		private Task CancelRunAsync(AutomationRunId runId)
		{
			if (_session is not { } session)
				return Task.CompletedTask;

			return session.CancelAsync(runId).AsTask();
		}

		private void HostContext_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			// Navigating or changing the selection can make a Ready action ineligible and an ineligible one Ready.
			if (e.PropertyName is nameof(IAutomationHostContext.Revision))
				RefreshRunAvailability();
		}

		private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is not nameof(IAutomationSession.Snapshot) || _session is null)
				return;

			// The session raises this from its filesystem watcher and from run threads, so the rebuild has to be
			// marshalled. Nothing upstream of the dispatcher can catch a failure here, so a bad projection must
			// not reach it.
			var snapshot = _session.Snapshot;
			_ = MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				try
				{
					ApplySnapshot(snapshot);
				}
				catch (Exception ex)
				{
					App.Logger.LogWarning(ex, "Automation Tools could not apply a session snapshot");
				}
			});
		}

		private void ApplySnapshot(AutomationSnapshot snapshot)
		{
			ApplyCatalog(snapshot);
			ApplyRuns(snapshot);
			RefreshRunAvailability();
		}

		/// <summary>
		/// Rebuilds the roots from a catalog revision, carrying expansion across so a package folder edit does
		/// not collapse what the user opened.
		/// </summary>
		private void ApplyCatalog(AutomationSnapshot snapshot)
		{
			// Runs also bump the snapshot revision; only a catalog change affects this tree.
			if (snapshot.CatalogRevision == _catalogRevision)
				return;

			_catalogRevision = snapshot.CatalogRevision;

			// Two packages can share an id: the catalog disables both and keeps both so the collision stays
			// visible and repairable. TryAdd rather than ToDictionary because that is not an error here.
			var expansion = new Dictionary<string, bool>(StringComparer.Ordinal);
			foreach (var package in _allPackages)
				expansion.TryAdd(package.Id, package.IsExpanded);

			_allPackages.Clear();

			foreach (var package in snapshot.Packages)
			{
				var item = new AutomationPackageItem(package, RunActionAsync);
				if (expansion.TryGetValue(item.Id, out var wasExpanded))
					item.IsExpanded = wasExpanded;

				_allPackages.Add(item);
			}

			ApplyFilter();
		}

		private void ApplyRuns(AutomationSnapshot snapshot)
		{
			MergeRunRows(ActiveRuns, snapshot.ActiveRuns, snapshot);
			MergeRunRows(RecentRuns, snapshot.RecentRuns, snapshot);
			OnPropertyChanged(nameof(HasActiveRuns));
			OnPropertyChanged(nameof(HasRecentRuns));
		}

		/// <summary>
		/// Brings one run list in step with the snapshot's, preserving the snapshot's order.
		/// </summary>
		/// <remarks>
		/// Rows that are still present are updated in place rather than replaced. Rebuilding the list would
		/// restart the progress bar on every frame the action reports and drop the selection out from under a
		/// user reading a row.
		/// </remarks>
		private void MergeRunRows(
			ObservableCollection<AutomationRunItem> rows,
			ImmutableArray<AutomationRunSnapshot> runs,
			AutomationSnapshot snapshot)
		{
			for (var index = rows.Count - 1; index >= 0; index--)
			{
				if (!runs.Any(run => run.Id == rows[index].Id))
					rows.RemoveAt(index);
			}

			for (var index = 0; index < runs.Length; index++)
			{
				var run = runs[index];
				var existing = rows.FirstOrDefault(row => row.Id == run.Id);
				if (existing is not null)
				{
					existing.Update(run);
					continue;
				}

				rows.Insert(Math.Min(index, rows.Count), new AutomationRunItem(run, DescribeAction(snapshot, run.ActionId), CancelRunAsync));
			}
		}

		/// <summary>
		/// Names the action a run belongs to. A run can outlive the package it came from — the folder may have
		/// been edited mid-run — so the composite id is the fallback rather than an error.
		/// </summary>
		private static string DescribeAction(AutomationSnapshot snapshot, AutomationActionId actionId)
			=> snapshot.Packages
				.SelectMany(package => package.Actions)
				.FirstOrDefault(action => action.Id == actionId)
				?.DisplayName ?? actionId.Value;

		/// <summary>
		/// Recomputes every visible action row's run state from the catalog, the current Files context, and what
		/// is already running or about to be.
		/// </summary>
		private void RefreshRunAvailability()
		{
			if (_session?.Snapshot is not { } snapshot)
				return;

			_hostContext.TryCapture(out var selection);

			// Mirrors what the session admits on: runs it has started, plus invocations it has accepted but not
			// yet turned into runs. An invocation still at its trust prompt is one of the latter.
			var occupied = snapshot.ActiveRuns.Length +
				_invoking.Count(id => !snapshot.ActiveRuns.Any(run => run.ActionId == id));

			foreach (var package in Packages)
			{
				// One action at a time per package, so a package with a run in flight blocks all of its rows,
				// not just the one that started it.
				var packageBusy =
					snapshot.ActiveRuns.Any(run => run.ActionId.PackageId.Value == package.Id) ||
					_invoking.Any(id => id.PackageId.Value == package.Id);

				foreach (var action in package.Actions)
					action.RunState = Evaluate(action, new(snapshot, selection, packageBusy, occupied));
			}
		}

		private static AutomationActionRunState Evaluate(AutomationActionItem action, RunAvailabilityContext context)
		{
			if (action.Snapshot.Availability is not AutomationAvailability.Available ||
				action.Snapshot.Selection is not { } policy)
			{
				return AutomationActionRunState.Unavailable;
			}

			if (context.Snapshot.ActiveRuns.Any(run => run.ActionId == action.Snapshot.Id))
				return AutomationActionRunState.Running;

			if (context.PackageBusy || context.OccupiedSlots >= AutomationLimits.MaximumConcurrentRuns)
				return AutomationActionRunState.Busy;

			if (context.Selection is not { } selection)
				return AutomationActionRunState.NoFolder;

			return AutomationSelectionRules.Evaluate(policy, selection) is AutomationSelectionEligibility.Eligible
				? AutomationActionRunState.Ready
				: AutomationActionRunState.IncompatibleSelection;
		}

		/// <summary>
		/// Everything shared by every row in one availability pass, so the per-row decision reads as one question
		/// rather than a list of loose flags.
		/// </summary>
		private readonly record struct RunAvailabilityContext(
			AutomationSnapshot Snapshot,
			SelectionSnapshot? Selection,
			bool PackageBusy,
			int OccupiedSlots);

		private void ApplyFilter()
		{
			// Typing before the session opens, or after it failed to, must not replace the message explaining
			// why the tree is not there with a count-based one.
			if (_session is null)
				return;

			var isFiltering = !string.IsNullOrWhiteSpace(_filter);
			if (isFiltering && !_isFiltering)
				CaptureExpansion();
			else if (!isFiltering && _isFiltering)
				RestoreExpansion();

			_isFiltering = isFiltering;

			// Asking about each item in place keeps the answer attached to the row it came from. Matching a
			// filtered result back by id would pick the wrong row whenever two packages share an id.
			Packages.Clear();
			foreach (var package in _allPackages)
			{
				if (!AutomationCatalogFilter.TryMatch(package.Snapshot, _filter, out var actions))
					continue;

				package.ShowActions(actions);

				// A root that survived the filter is a root the user is looking for.
				if (isFiltering)
					package.IsExpanded = true;

				Packages.Add(package);
			}

			State = _allPackages.Count is 0
				? AutomationToolsState.Empty
				: Packages.Count is 0
					? AutomationToolsState.NoMatches
					: AutomationToolsState.Ready;

			// ShowActions built fresh rows, which start out unavailable until they are told otherwise.
			RefreshRunAvailability();
		}

		private void CaptureExpansion()
		{
			_expansionBeforeFilter.Clear();
			foreach (var package in _allPackages)
				_expansionBeforeFilter[package.Id] = package.IsExpanded;
		}

		private void RestoreExpansion()
		{
			foreach (var package in _allPackages)
			{
				// A package discovered while the filter was applied has nothing to restore, so it stays closed.
				package.IsExpanded = _expansionBeforeFilter.TryGetValue(package.Id, out var wasExpanded) && wasExpanded;
			}

			_expansionBeforeFilter.Clear();
		}
	}
}
