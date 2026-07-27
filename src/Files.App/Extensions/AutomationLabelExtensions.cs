// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Extensions
{
	/// <summary>
	/// Turns the Automation domain's enums into the text shown in the Tools tab.
	/// </summary>
	/// <remarks>
	/// The domain deals in verdicts, never in prose, so that nothing it returns can reach the UI unlocalized.
	/// Naming them is the host's job, and this is where the host does it.
	/// </remarks>
	public static class AutomationLabelExtensions
	{
		/// <summary>
		/// Returns the localized text shown beside an Automation Package or Automation Action.
		/// </summary>
		public static string ToLabel(this AutomationAvailability availability)
			=> availability switch
			{
				AutomationAvailability.Available => Strings.AutomationAvailabilityAvailable.GetLocalizedResource(),
				AutomationAvailability.MissingDependency => Strings.AutomationAvailabilityMissingDependency.GetLocalizedResource(),
				_ => Strings.AutomationAvailabilityDisabled.GetLocalizedResource(),
			};

		/// <summary>
		/// Returns the localized text shown for a run's outcome.
		/// </summary>
		public static string ToLabel(this AutomationRunState state)
			=> state switch
			{
				AutomationRunState.Starting => Strings.AutomationRunStateStarting.GetLocalizedResource(),
				AutomationRunState.Running => Strings.AutomationRunStateRunning.GetLocalizedResource(),
				AutomationRunState.Cancelling => Strings.AutomationRunStateCancelling.GetLocalizedResource(),
				AutomationRunState.Succeeded => Strings.AutomationRunStateSucceeded.GetLocalizedResource(),
				AutomationRunState.TimedOut => Strings.AutomationRunStateTimedOut.GetLocalizedResource(),
				AutomationRunState.Cancelled => Strings.AutomationRunStateCancelled.GetLocalizedResource(),
				_ => Strings.AutomationRunStateFailed.GetLocalizedResource(),
			};
	}
}
