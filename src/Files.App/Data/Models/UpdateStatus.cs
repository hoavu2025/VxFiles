// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

namespace Files.App.Data.Models
{
	/// <summary>
	/// Everything the update surface can be showing at one moment.
	/// </summary>
	/// <remarks>
	/// The hierarchy is closed: the private constructor means only the nested records below can derive from
	/// it, so a <c>switch</c> over these cases is exhaustive for the lifetime of the file.
	/// </remarks>
	public abstract record UpdateStatus
	{
		private UpdateStatus()
		{
		}

		/// <summary>
		/// Velopack did not install this copy, so nothing can ever be staged and the card stays hidden.
		/// </summary>
		public sealed record Unsupported : UpdateStatus;

		/// <summary>
		/// The last check reached GitHub and found nothing newer.
		/// </summary>
		public sealed record UpToDate : UpdateStatus;

		/// <summary>
		/// A check is in flight.
		/// </summary>
		public sealed record Checking : UpdateStatus;

		/// <summary>
		/// A newer release is being fetched. <paramref name="Bytes"/> is the full download size.
		/// </summary>
		public sealed record Downloading(string Version, int Percent, long Bytes) : UpdateStatus;

		/// <summary>
		/// A newer release is downloaded and staged. It installs on the next exit whether or not anyone acts.
		/// </summary>
		public sealed record Ready(string Version, string? NotesMarkdown, long Bytes) : UpdateStatus;

		/// <summary>
		/// The last check could not reach GitHub. Never replaces <see cref="Ready"/> — a staged update
		/// outranks a failed poll.
		/// </summary>
		public sealed record Failed(UpdateFailure Reason) : UpdateStatus;
	}

	/// <summary>
	/// Why a check failed, to the extent the transport can tell us.
	/// </summary>
	public enum UpdateFailure
	{
		/// <summary>
		/// No response at all — almost always no network.
		/// </summary>
		Offline,

		/// <summary>
		/// GitHub answered 403 or 429. The unauthenticated ceiling is 60 requests an hour pooled per IP,
		/// so this is shared with everyone behind the same NAT.
		/// </summary>
		RateLimited,

		/// <summary>
		/// Anything else, including a malformed release feed.
		/// </summary>
		Unknown,
	}
}
