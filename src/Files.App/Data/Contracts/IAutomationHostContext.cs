// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Contracts
{
	/// <summary>
	/// The Files side of an Automation invocation: which folder is active, what is selected, and a revision that
	/// moves whenever either changes.
	/// </summary>
	/// <remarks>
	/// An Automation Action runs against the folder and selection as they stood when Run was pressed. This is the
	/// only seam that reads live Files state on the Automation path, so the snapshot a Run button was enabled
	/// against is the same object handed to the session — the two cannot drift apart.
	/// </remarks>
	public interface IAutomationHostContext : INotifyPropertyChanged
	{
		/// <summary>
		/// Gets the current folder-and-selection revision, which moves on every navigation and every selection
		/// change. Observing it is how Run availability is kept honest as the user works.
		/// </summary>
		/// <remarks>
		/// This is a change signal, not the staleness test for a completed run's effects. Selecting a different
		/// file moves the revision but does not invalidate a refresh of the folder the action ran against, so
		/// <c>AutomationResultRouter</c> compares the captured folder instead.
		/// </remarks>
		HostRevision Revision { get; }

		/// <summary>
		/// Captures the active folder and selection as an immutable snapshot.
		/// </summary>
		/// <returns>
		/// <see langword="false"/> when no filesystem folder is open — Home, search results, and shell locations
		/// such as the Recycle Bin have no folder an action could run against.
		/// </returns>
		bool TryCapture([NotNullWhen(true)] out SelectionSnapshot? selection);
	}
}
