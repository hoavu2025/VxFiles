// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One Automation run in the Tools tab, either still going or already finished.
	/// </summary>
	/// <remarks>
	/// Deliberately a summary. The run's log frames and captured paths stay on the snapshot and in the durable
	/// run history; what is shown here is what tells the user whether the thing they started worked.
	/// </remarks>
	public sealed partial class AutomationRunItem : ObservableObject
	{
		private readonly Func<AutomationRunId, Task> _cancel;

		private AutomationRunSnapshot _snapshot;

		public AutomationRunItem(
			AutomationRunSnapshot snapshot,
			string displayName,
			Func<AutomationRunId, Task> cancel)
		{
			ArgumentNullException.ThrowIfNull(snapshot);
			ArgumentNullException.ThrowIfNull(cancel);

			_snapshot = snapshot;
			_cancel = cancel;
			DisplayName = displayName;
		}

		public AutomationRunId Id => _snapshot.Id;

		/// <summary>
		/// Gets the action's display name, resolved once from the catalog. A finished run keeps the name it was
		/// started under even if the package is later edited or removed.
		/// </summary>
		public string DisplayName { get; }

		public string Status => _snapshot.Status;

		public string StateLabel => _snapshot.State.ToLabel();

		/// <summary>
		/// Gets the reported progress, or zero when the action has not reported any. Read together with
		/// <see cref="IsIndeterminate"/>, which is what stops that zero from being shown as no progress made.
		/// </summary>
		public double ProgressPercent => _snapshot.ProgressPercent ?? 0;

		/// <summary>
		/// Gets whether progress is unknown. Reporting it is optional, and a short action commonly never does.
		/// </summary>
		public bool IsIndeterminate => _snapshot.ProgressPercent is null;

		public bool IsActive => _snapshot.State is
			AutomationRunState.Starting or AutomationRunState.Running or AutomationRunState.Cancelling;

		/// <summary>
		/// Gets whether Cancel is offered. A run already cancelling has nothing further to ask for.
		/// </summary>
		public bool CanCancel => _snapshot.State is AutomationRunState.Starting or AutomationRunState.Running;

		public string Failure => _snapshot.Failure ?? string.Empty;

		public bool HasFailure => Failure.Length is not 0;

		/// <summary>
		/// Gets what became of the effects the action asked for, one line each. Present only once a run has
		/// finished, since intents are routed on completion.
		/// </summary>
		public string Effects => string.Join(
			Environment.NewLine,
			_snapshot.IntentResults.Select(result => result.Message));

		public bool HasEffects => Effects.Length is not 0;

		/// <summary>
		/// Adopts a newer snapshot of the same run, so progress and status move without the row being replaced
		/// underneath a user who is reading it.
		/// </summary>
		public void Update(AutomationRunSnapshot snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);

			_snapshot = snapshot;
			OnPropertyChanged(nameof(Status));
			OnPropertyChanged(nameof(StateLabel));
			OnPropertyChanged(nameof(ProgressPercent));
			OnPropertyChanged(nameof(IsIndeterminate));
			OnPropertyChanged(nameof(IsActive));
			OnPropertyChanged(nameof(CanCancel));
			OnPropertyChanged(nameof(Failure));
			OnPropertyChanged(nameof(HasFailure));
			OnPropertyChanged(nameof(Effects));
			OnPropertyChanged(nameof(HasEffects));
			CancelCommand.NotifyCanExecuteChanged();
		}

		[RelayCommand(CanExecute = nameof(CanCancel))]
		private Task CancelAsync() => _cancel(Id);
	}
}
