// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One Automation Package root in the Tools TreeView, holding its Automation Action children.
	/// </summary>
	public sealed partial class AutomationPackageItem : ObservableObject
	{
		private readonly AutomationPackageSnapshot _snapshot;

		// Held only to pass on to the action rows that ShowActions rebuilds; this type never calls it.
		private readonly Func<AutomationActionItem, Task> _run;

		private bool _isExpanded;

		public AutomationPackageItem(AutomationPackageSnapshot snapshot, Func<AutomationActionItem, Task> run)
		{
			ArgumentNullException.ThrowIfNull(snapshot);
			ArgumentNullException.ThrowIfNull(run);

			_snapshot = snapshot;
			_run = run;
			Diagnostics = string.Join(Environment.NewLine, snapshot.Diagnostics);
			HealthLabel = DescribeHealth(snapshot);
			ShowActions(snapshot.Actions);
		}

		/// <summary>
		/// Gets the catalog snapshot this row was built from, including the actions a filter is hiding.
		/// </summary>
		public AutomationPackageSnapshot Snapshot => _snapshot;

		public string Id => _snapshot.Id.Value;

		public string DisplayName => _snapshot.DisplayName;

		public string PackageVersion => _snapshot.PackageVersion;

		public string Description => _snapshot.Description;

		public bool HasDescription => !string.IsNullOrWhiteSpace(_snapshot.Description);

		/// <summary>
		/// Gets the package's own availability, qualified by how many of its actions survived validation.
		/// </summary>
		public string HealthLabel { get; }

		public string Diagnostics { get; }

		public bool HasDiagnostics => Diagnostics.Length is not 0;

		/// <summary>
		/// Gets the action rows currently shown, which is a subset of the package's actions while a filter
		/// is applied.
		/// </summary>
		public ObservableCollection<AutomationActionItem> Actions { get; } = [];

		public bool IsExpanded
		{
			get => _isExpanded;
			set => SetProperty(ref _isExpanded, value);
		}

		/// <summary>
		/// Replaces the visible action rows, keeping this root's identity and health untouched.
		/// </summary>
		public void ShowActions(ImmutableArray<AutomationActionSnapshot> actions)
		{
			Actions.Clear();
			foreach (var action in actions)
				Actions.Add(new AutomationActionItem(action, _run));
		}

		/// <summary>
		/// Always counts every action the package declares, so filtering out healthy siblings cannot make a
		/// package look broken.
		/// </summary>
		private static string DescribeHealth(AutomationPackageSnapshot snapshot)
		{
			if (snapshot.Availability is not AutomationAvailability.Available)
				return snapshot.Availability.ToLabel();

			var available = snapshot.Actions.Count(action => action.Availability is AutomationAvailability.Available);
			if (available == snapshot.Actions.Length)
				return AutomationAvailability.Available.ToLabel();

			return string.Format(
				Strings.AutomationToolsActionsAvailable.GetLocalizedResource(),
				available,
				snapshot.Actions.Length);
		}
	}
}
