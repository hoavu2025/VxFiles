// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

namespace Files.App.Data.Contracts
{
	/// <summary>
	/// Everything the update surface needs: the dot on the sidebar's Settings icon, the update card on the
	/// About page, and the hourly re-check behind both.
	/// </summary>
	/// <remarks>
	/// Deliberately separate from <see cref="IUpdateService"/> rather than added to it. That interface has
	/// three implementations, and neither the Store one nor the dev one can answer a question about Velopack
	/// staging. Only the sideload build registers this service, so a <c>null</c> resolution is the signal
	/// that a build has no update surface at all — there is no <c>IsSupported</c> flag to check.
	/// </remarks>
	public interface IUpdateStatusService : INotifyPropertyChanged
	{
		/// <summary>
		/// What the update is doing right now. Never null.
		/// </summary>
		UpdateStatus Status { get; }

		/// <summary>
		/// When a check last actually reached GitHub, or null if one never has on this machine.
		/// </summary>
		/// <remarks>
		/// Advances only on success, so an updater that has been failing for a week says so instead of
		/// reporting a cheerful recent attempt. This fork carries no telemetry, and a silently broken
		/// updater is otherwise invisible to everyone.
		/// </remarks>
		DateTimeOffset? LastSuccessfulCheck { get; }

		/// <summary>
		/// Runs one full cycle — ask GitHub, then download and stage anything newer.
		/// </summary>
		/// <remarks>
		/// Callers share a single in-flight cycle, so the hourly timer and the button on the About page cannot
		/// spend two requests at once. Starting a cycle always costs a request — there is no cache window and
		/// no refusal, even when the last attempt was rate limited.
		/// </remarks>
		Task CheckAsync();

		/// <summary>
		/// Applies a staged update now by closing the window and letting the app come back on the new version.
		/// </summary>
		/// <remarks>
		/// Returns immediately. The ordinary shutdown runs in full — file operations drain, the Automation
		/// session is cancelled, tabs are saved and restored — and the external updater installs once this
		/// process is gone. Does nothing unless <see cref="Status"/> is <see cref="UpdateStatus.Ready"/>.
		/// </remarks>
		void RestartToUpdate();
	}
}
