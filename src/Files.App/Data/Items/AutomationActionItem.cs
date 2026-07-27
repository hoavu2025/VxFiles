// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One Automation Action row under its Automation Package in the Tools TreeView.
	/// </summary>
	/// <remarks>
	/// A read-only projection of the snapshot the headless catalog produced. It resolves nothing itself, so a
	/// row can never disagree with what the session discovered.
	/// </remarks>
	public sealed class AutomationActionItem
	{
		private readonly AutomationActionSnapshot _snapshot;

		public AutomationActionItem(AutomationActionSnapshot snapshot)
		{
			_snapshot = snapshot;
			Diagnostics = string.Join(Environment.NewLine, snapshot.Diagnostics);
		}

		public string DisplayName => _snapshot.DisplayName;

		public string Description => _snapshot.Description;

		public bool HasDescription => !string.IsNullOrWhiteSpace(_snapshot.Description);

		public string AvailabilityLabel => _snapshot.Availability.ToLabel();

		public string Diagnostics { get; }

		public bool HasDiagnostics => Diagnostics.Length is not 0;
	}
}
