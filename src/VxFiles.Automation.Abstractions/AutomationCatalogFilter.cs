// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace VxFiles.Automation.Abstractions;

/// <summary>
/// Decides what a host surface should show for one discovered package under a user-typed filter.
/// </summary>
/// <remarks>
/// This lives beside the snapshot types rather than in a view model so the matching rules can be tested
/// without a UI. It only reads the snapshot it is given: it never touches the filesystem or re-discovers.
///
/// <para>
/// The decision is per package rather than per catalog on purpose. A catalog can legitimately contain two
/// packages with the same id — <c>AutomationManifestCatalog</c> disables both and keeps both so the user can
/// see and repair the collision — so a caller must never have to match a filtered result back to its source
/// by id.
/// </para>
/// </remarks>
public static class AutomationCatalogFilter
{
	/// <summary>
	/// Reports whether <paramref name="package"/> survives <paramref name="filter"/>, and which of its actions
	/// should be listed.
	///
	/// <para>
	/// A package whose own name or description matches keeps every action, because the user asked for the
	/// package. Otherwise only its matching actions are listed, and a package with no matching action does not
	/// survive. A blank filter keeps everything.
	/// </para>
	/// </summary>
	public static bool TryMatch(
		AutomationPackageSnapshot package,
		string? filter,
		out ImmutableArray<AutomationActionSnapshot> actions)
	{
		ArgumentNullException.ThrowIfNull(package);

		if (string.IsNullOrWhiteSpace(filter))
		{
			actions = package.Actions;
			return true;
		}

		var term = filter.Trim();
		if (Matches(package.DisplayName, term) || Matches(package.Description, term))
		{
			actions = package.Actions;
			return true;
		}

		actions = package.Actions
			.Where(action => Matches(action.DisplayName, term) || Matches(action.Description, term))
			.ToImmutableArray();
		return !actions.IsEmpty;
	}

	// The user is typing in their own language, so this follows the current culture rather than ordinal rules.
	private static bool Matches(string? candidate, string term)
		=> candidate is not null && candidate.Contains(term, StringComparison.CurrentCultureIgnoreCase);
}
