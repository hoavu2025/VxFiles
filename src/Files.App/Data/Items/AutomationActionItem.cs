// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One Automation Action row under its Automation Package in the Tools TreeView.
	/// </summary>
	/// <remarks>
	/// A projection of the snapshot the headless catalog produced, plus the run state the view model computes from
	/// the current Files context. It resolves nothing and decides nothing itself, so a row can never disagree with
	/// what the session discovered or with what the session will admit.
	/// </remarks>
	public sealed partial class AutomationActionItem : ObservableObject
	{
		private readonly Func<AutomationActionItem, Task> _run;

		private AutomationActionRunState _runState = AutomationActionRunState.Unavailable;

		public AutomationActionItem(AutomationActionSnapshot snapshot, Func<AutomationActionItem, Task> run)
		{
			ArgumentNullException.ThrowIfNull(snapshot);
			ArgumentNullException.ThrowIfNull(run);

			Snapshot = snapshot;
			_run = run;
			Diagnostics = string.Join(Environment.NewLine, snapshot.Diagnostics);
		}

		/// <summary>
		/// Gets the catalog snapshot this row was built from, including the selection policy a run is admitted by.
		/// </summary>
		public AutomationActionSnapshot Snapshot { get; }

		public string DisplayName => Snapshot.DisplayName;

		public string Description => Snapshot.Description;

		public bool HasDescription => !string.IsNullOrWhiteSpace(Snapshot.Description);

		public string AvailabilityLabel => Snapshot.Availability.ToLabel();

		public string Diagnostics { get; }

		public bool HasDiagnostics => Diagnostics.Length is not 0;

		public AutomationActionRunState RunState
		{
			get => _runState;
			set
			{
				if (SetProperty(ref _runState, value))
				{
					OnPropertyChanged(nameof(CanRun));
					OnPropertyChanged(nameof(RunStateLabel));
					RunCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public bool CanRun => RunState is AutomationActionRunState.Ready;

		/// <summary>
		/// Gets the localized explanation shown on the Run button's tooltip, so a disabled button says why.
		/// </summary>
		public string RunStateLabel => RunState switch
		{
			AutomationActionRunState.Ready => Strings.AutomationToolsRunReady.GetLocalizedResource(),
			AutomationActionRunState.Running => Strings.AutomationToolsRunRunning.GetLocalizedResource(),
			AutomationActionRunState.NoFolder => Strings.AutomationToolsRunNoFolder.GetLocalizedResource(),
			AutomationActionRunState.IncompatibleSelection => Strings.AutomationToolsRunIncompatibleSelection.GetLocalizedResource(),
			AutomationActionRunState.Busy => Strings.AutomationToolsRunBusy.GetLocalizedResource(),
			_ => Strings.AutomationToolsRunUnavailable.GetLocalizedResource(),
		};

		[RelayCommand(CanExecute = nameof(CanRun))]
		private Task RunAsync() => _run(this);
	}
}
