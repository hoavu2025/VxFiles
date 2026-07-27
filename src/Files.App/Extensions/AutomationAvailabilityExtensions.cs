// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Extensions
{
	public static class AutomationAvailabilityExtensions
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
	}
}
