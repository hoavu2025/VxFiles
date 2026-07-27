// Copyright (c) Files Community
// Licensed under the MIT License.

namespace VxFiles.Automation.Abstractions;

/// <summary>
/// The session's admission and retention bounds, published so a host surface can present them instead of
/// discovering them by having an invocation refused.
/// </summary>
public static class AutomationLimits
{
	/// <summary>
	/// How many Automation Actions may run at once, across every Automation Package. A request beyond this is
	/// refused rather than queued, so an invocation always maps to an observable outcome.
	/// </summary>
	public const int MaximumConcurrentRuns = 2;

	/// <summary>
	/// How many finished runs the in-memory snapshot keeps. The durable history on disk is pruned separately by
	/// age and size; this bound is what stops a long session from accumulating run summaries without limit.
	/// </summary>
	public const int MaximumRecentRuns = 20;
}
