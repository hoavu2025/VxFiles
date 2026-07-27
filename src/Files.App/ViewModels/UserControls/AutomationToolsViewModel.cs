// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using VxFiles.Automation.Abstractions;

namespace Files.App.ViewModels.UserControls
{
	/// <summary>
	/// Projects the headless Automation catalog into the filterable package/action tree shown by the Tools tab.
	/// </summary>
	/// <remarks>
	/// This view model reads snapshots and nothing else. Manifest parsing, filesystem discovery, trust, and
	/// execution all stay inside the Automation module, so a Tools refresh can never disagree with a run.
	/// Running actions arrives with issue #16; every row here is read-only.
	/// </remarks>
	public sealed partial class AutomationToolsViewModel : ObservableObject, IDisposable
	{
		private readonly IAutomationSessionService _sessionService = Ioc.Default.GetRequiredService<IAutomationSessionService>();

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

		private string _filter = string.Empty;
		private AutomationToolsState _state = AutomationToolsState.Loading;

		public ObservableCollection<AutomationPackageItem> Packages { get; } = [];

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
				_session = session;
				ApplyCatalog(session.Snapshot);
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

		public void Dispose()
		{
			if (_session is not null)
				_session.PropertyChanged -= Session_PropertyChanged;

			_session = null;
		}

		/// <summary>
		/// Opens the user packages folder in a new tab, which is how a package is installed: copy its folder in.
		/// </summary>
		[RelayCommand]
		private Task OpenPackagesFolderAsync()
			=> NavigationHelpers.OpenPathInNewTab(_sessionService.EnsureUserPackagesFolder());

		private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is not nameof(IAutomationSession.Snapshot) || _session is null)
				return;

			// The session raises this from its filesystem watcher, so the rebuild has to be marshalled. Nothing
			// upstream of the dispatcher can catch a failure here, so a bad projection must not reach it.
			var snapshot = _session.Snapshot;
			_ = MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				try
				{
					ApplyCatalog(snapshot);
				}
				catch (Exception ex)
				{
					App.Logger.LogWarning(ex, "Automation Tools could not rebuild the package tree");
				}
			});
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
				var item = new AutomationPackageItem(package);
				if (expansion.TryGetValue(item.Id, out var wasExpanded))
					item.IsExpanded = wasExpanded;

				_allPackages.Add(item);
			}

			ApplyFilter();
		}

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
