// Copyright (c) Files Community
// Licensed under the MIT License.

namespace VxFiles.Automation.Abstractions;

/// <summary>
/// Why a selection can or cannot be handed to an Automation Action.
/// </summary>
/// <remarks>
/// A verdict rather than a message: the session turns it into exception text, and a host surface turns it into
/// localized text. Returning a string here would put developer English on a user-facing button.
/// </remarks>
public enum AutomationSelectionEligibility
{
	Eligible,
	TooFewItems,
	TooManyItems,
	UnsupportedItemKind,
	UnsupportedExtension,
	PathNotFullyQualified,
}

/// <summary>
/// Decides whether a captured selection satisfies an Automation Action's declared selection policy.
/// </summary>
/// <remarks>
/// Both the Tools tab and <c>AutomationSession</c> call this. A disabled Run button that disagreed with the
/// session's own admission check would either block a legal run or offer one that is refused on click, so the
/// rule exists once and is shared rather than restated on each side.
/// </remarks>
public static class AutomationSelectionRules
{
	/// <summary>
	/// The manifest vocabulary for <see cref="SelectedPathKind"/>, as written in <c>input.selection.itemKinds</c>.
	/// </summary>
	public static string ToDeclaredKind(SelectedPathKind kind)
		=> kind is SelectedPathKind.File ? "file" : "folder";

	/// <summary>
	/// Classifies a path as local or on a network share.
	/// </summary>
	/// <remarks>
	/// Recognizes both spellings of a network path: <c>\\server\share</c> and its extended-length form
	/// <c>\\?\UNC\server\share</c>. The extended-length device form <c>\\?\C:\</c> is local despite the leading
	/// backslashes, which is the case a plain "starts with two backslashes" test gets wrong.
	///
	/// <para>
	/// An action is told which it got so it can decide for itself — a network path can vanish mid-run in ways a
	/// local one cannot. It is not an admission rule: a share the user can browse is one they can act on.
	/// </para>
	/// </remarks>
	public static SelectedLocationKind ClassifyLocation(string path)
	{
		ArgumentNullException.ThrowIfNull(path);

		const string ExtendedPrefix = @"\\?\";

		if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
		{
			return path.AsSpan(ExtendedPrefix.Length).StartsWith("UNC\\", StringComparison.OrdinalIgnoreCase)
				? SelectedLocationKind.Unc
				: SelectedLocationKind.Local;
		}

		return path.StartsWith(@"\\", StringComparison.Ordinal)
			? SelectedLocationKind.Unc
			: SelectedLocationKind.Local;
	}

	/// <summary>
	/// Reports the first reason <paramref name="selection"/> fails <paramref name="policy"/>, or
	/// <see cref="AutomationSelectionEligibility.Eligible"/> when it satisfies every rule.
	/// </summary>
	public static AutomationSelectionEligibility Evaluate(
		AutomationSelectionPolicy policy,
		SelectionSnapshot selection)
	{
		ArgumentNullException.ThrowIfNull(policy);
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Items.Length < policy.MinItems)
			return AutomationSelectionEligibility.TooFewItems;
		if (selection.Items.Length > policy.MaxItems)
			return AutomationSelectionEligibility.TooManyItems;

		foreach (var item in selection.Items)
		{
			// A relative or device path would be resolved against whatever the runner's working directory
			// happens to be, so it is rejected before it can reach one.
			if (!Path.IsPathFullyQualified(item.FullPath))
				return AutomationSelectionEligibility.PathNotFullyQualified;

			if (!policy.ItemKinds.Contains(ToDeclaredKind(item.Kind), StringComparer.Ordinal))
				return AutomationSelectionEligibility.UnsupportedItemKind;

			// An empty extension list means the action takes any file. Folders never carry one.
			if (item.Kind is SelectedPathKind.File &&
				!policy.Extensions.IsEmpty &&
				!policy.Extensions.Contains(Path.GetExtension(item.FullPath), StringComparer.OrdinalIgnoreCase))
			{
				return AutomationSelectionEligibility.UnsupportedExtension;
			}
		}

		return AutomationSelectionEligibility.Eligible;
	}
}
