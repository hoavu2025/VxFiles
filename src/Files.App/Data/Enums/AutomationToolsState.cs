// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify what the Tools tab is currently showing.
	/// </summary>
	public enum AutomationToolsState
	{
		/// <summary>
		/// The headless session has not been opened yet.
		/// </summary>
		Loading,

		/// <summary>
		/// At least one Automation Package is listed.
		/// </summary>
		Ready,

		/// <summary>
		/// Discovery succeeded and found no Automation Packages at all.
		/// </summary>
		Empty,

		/// <summary>
		/// Packages exist, but none of them matches the current filter.
		/// </summary>
		NoMatches,

		/// <summary>
		/// The session could not be opened, so nothing can be discovered.
		/// </summary>
		Unavailable,
	}
}
