// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// Covers the matching rules behind the Tools tab search box. These are pure snapshot transforms, so they are
/// tested here rather than through the WinUI view model that consumes them.
/// </summary>
[TestClass]
public sealed class AutomationCatalogFilterTests
{
	private static readonly AutomationPackageSnapshot Tracer = Package(
		"vxfiles.tracer",
		"Tracer",
		"Diagnostics for the Automation runtime",
		Action("vxfiles.tracer", "check-runtime", "Check runtime", "Reports the interpreter version"),
		Action("vxfiles.tracer", "report-selection", "Report selection", "Summarizes the selected items"));

	private static readonly AutomationPackageSnapshot Images = Package(
		"contoso.images",
		"Image tools",
		"Utilities for pictures",
		Action("contoso.images", "resize", "Resize", "Scales images down"),
		Action("contoso.images", "convert", "Convert", "Changes the image format"));

	[TestMethod]
	public void Blank_filter_keeps_the_package_and_every_action()
	{
		foreach (var filter in new string?[] { null, "", "   " })
		{
			// A search box holding only spaces is not a search.
			Assert.IsTrue(AutomationCatalogFilter.TryMatch(Tracer, filter, out var actions), $"filter: '{filter}'");
			Assert.AreSequenceEqual(Tracer.Actions, actions);
		}
	}

	[TestMethod]
	public void Package_name_match_keeps_every_action()
	{
		Assert.IsTrue(AutomationCatalogFilter.TryMatch(Tracer, "tracer", out var actions));
		Assert.AreEqual(2, actions.Length, "A package the user asked for keeps all of its actions.");
	}

	[TestMethod]
	public void Package_description_match_keeps_every_action()
	{
		Assert.IsTrue(AutomationCatalogFilter.TryMatch(Images, "pictures", out var actions));
		Assert.AreEqual(2, actions.Length);
	}

	[TestMethod]
	public void Action_match_keeps_only_the_matching_children()
	{
		Assert.IsTrue(AutomationCatalogFilter.TryMatch(Images, "resize", out var actions));
		Assert.AreEqual("resize", actions.Single().Id.LocalId.Value);
	}

	[TestMethod]
	public void Action_description_match_keeps_its_package()
	{
		Assert.IsTrue(AutomationCatalogFilter.TryMatch(Tracer, "interpreter version", out var actions));
		Assert.AreEqual("check-runtime", actions.Single().Id.LocalId.Value);
	}

	[TestMethod]
	public void Matching_ignores_case_and_surrounding_whitespace()
	{
		Assert.IsTrue(AutomationCatalogFilter.TryMatch(Images, "  IMAGE TOOLS  ", out _));
	}

	[TestMethod]
	public void Unmatched_filter_drops_the_package()
	{
		Assert.IsFalse(AutomationCatalogFilter.TryMatch(Images, "spreadsheet", out var actions));
		Assert.IsTrue(actions.IsEmpty);
	}

	/// <summary>
	/// A package disabled for a duplicate id keeps its identity but loses its actions, so it must still be
	/// reachable by name. Otherwise the collision the user has to repair becomes invisible behind a filter.
	/// </summary>
	[TestMethod]
	public void Disabled_package_without_actions_still_matches_by_name()
	{
		var disabled = Tracer with
		{
			Availability = AutomationAvailability.Disabled,
			Diagnostics = ["Duplicate package id 'vxfiles.tracer'."],
			Actions = [],
		};

		Assert.IsTrue(AutomationCatalogFilter.TryMatch(disabled, "tracer", out var actions));
		Assert.IsTrue(actions.IsEmpty);
	}

	private static AutomationPackageSnapshot Package(
		string id,
		string displayName,
		string description,
		params AutomationActionSnapshot[] actions) => new(
			AutomationPackageId.Parse(id),
			"1.0.0",
			displayName,
			description,
			"VxFiles",
			null,
			AutomationAvailability.Available,
			[],
			[.. actions]);

	private static AutomationActionSnapshot Action(
		string packageId,
		string localId,
		string displayName,
		string description) => new(
			new(AutomationPackageId.Parse(packageId), AutomationActionLocalId.Parse(localId)),
			displayName,
			description,
			null,
			AutomationAvailability.Available,
			[]);
}
