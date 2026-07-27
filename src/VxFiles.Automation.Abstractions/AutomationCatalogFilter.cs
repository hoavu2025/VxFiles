// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace VxFiles.Automation.Abstractions;

/// <summary>
/// Narrows a discovered catalog to what a host surface should show for a user-typed filter.
/// </summary>
/// <remarks>
/// This lives beside the snapshot types rather than in a view model so the matching rules can be tested
/// without a UI. It only reads the snapshots it is given: it never touches the filesystem or re-discovers.
/// </remarks>
public static class AutomationCatalogFilter
{
	/// <summary>
	/// Returns the packages to display for <paramref name="filter"/>.
	///
	/// <para>
	/// A package whose own name or description matches keeps every action, because the user asked for the
	/// package. Otherwise only its matching actions survive, and a package with no surviving action drops
	/// out entirely. A blank filter returns <paramref name="packages"/> unchanged.
	/// </para>
	/// </summary>
	public static ImmutableArray<AutomationPackageSnapshot> Apply(
		ImmutableArray<AutomationPackageSnapshot> packages,
		string? filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
			return packages;

		var term = filter.Trim();
		var builder = ImmutableArray.CreateBuilder<AutomationPackageSnapshot>(packages.Length);

		foreach (var package in packages)
		{
			if (Matches(package.DisplayName, term) || Matches(package.Description, term))
			{
				builder.Add(package);
				continue;
			}

			var actions = package.Actions
				.Where(action => Matches(action.DisplayName, term) || Matches(action.Description, term))
				.ToImmutableArray();
			if (!actions.IsEmpty)
				builder.Add(package with { Actions = actions });
		}

		return builder.ToImmutable();
	}

	// The user is typing in their own language, so this follows the current culture rather than ordinal rules.
	private static bool Matches(string? candidate, string term)
		=> candidate is not null && candidate.Contains(term, StringComparison.CurrentCultureIgnoreCase);
}
