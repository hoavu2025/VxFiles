// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Contracts
{
	/// <summary>
	/// Owns the single headless Automation session for this process.
	/// </summary>
	/// <remarks>
	/// The session is opened on first use rather than at startup, so an app that never shows the Tools tab
	/// never pays for package discovery or its filesystem watchers.
	/// </remarks>
	public interface IAutomationSessionService : IAsyncDisposable
	{
		/// <summary>
		/// Gets the folder users copy Automation Packages into. It may not exist yet; use
		/// <see cref="EnsureUserPackagesFolder"/> before pointing anything at it.
		/// </summary>
		string UserPackagesPath { get; }

		/// <summary>
		/// Creates the user packages folder if it is missing and returns its path.
		/// </summary>
		string EnsureUserPackagesFolder();

		/// <summary>
		/// Opens the session on first use and returns the same instance afterwards.
		/// </summary>
		/// <remarks>
		/// Throws when the pinned runtime is missing or unusable. A failed attempt is not cached, so a caller
		/// that surfaces the error can retry later.
		/// </remarks>
		ValueTask<IAutomationSession> GetSessionAsync(CancellationToken cancellationToken = default);
	}
}
