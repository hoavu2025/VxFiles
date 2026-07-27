// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify whether an Automation Action row can be run right now, and why not when it
	/// cannot.
	/// </summary>
	/// <remarks>
	/// This is a host concept, not a catalog one. <c>AutomationAvailability</c> answers "did this action survive
	/// validation?", which does not move as the user navigates; everything below except
	/// <see cref="Unavailable"/> depends on what is open and selected at this moment.
	/// </remarks>
	public enum AutomationActionRunState
	{
		/// <summary>
		/// The action can be started against the current folder and selection.
		/// </summary>
		Ready,

		/// <summary>
		/// This action is already running. A package runs one action at a time.
		/// </summary>
		Running,

		/// <summary>
		/// The action did not survive validation, or a dependency it needs is missing.
		/// </summary>
		Unavailable,

		/// <summary>
		/// No filesystem folder is open — Home, search results, and shell locations have nothing to run against.
		/// </summary>
		NoFolder,

		/// <summary>
		/// What is selected does not satisfy the action's declared selection policy.
		/// </summary>
		IncompatibleSelection,

		/// <summary>
		/// Another action is occupying the slot: this action's package is busy, or every concurrent run is taken.
		/// </summary>
		Busy,
	}
}
