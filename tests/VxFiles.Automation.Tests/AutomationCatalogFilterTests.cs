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
	private static readonly ImmutableArray<AutomationPackageSnapshot> Catalog =
	[
		Package(
			"vxfiles.tracer",
			"Tracer",
			"Diagnostics for the Automation runtime",
			Action("vxfiles.tracer", "check-runtime", "Check runtime", "Reports the interpreter version"),
			Action("vxfiles.tracer", "report-selection", "Report selection", "Summarizes the selected items")),
		Package(
			"contoso.images",
			"Image tools",
			"Utilities for pictures",
			Action("contoso.images", "resize", "Resize", "Scales images down"),
			Action("contoso.images", "convert", "Convert", "Changes the image format")),
	];

	[TestMethod]
	public void Blank_filter_returns_the_whole_catalog()
	{
		Assert.AreSequenceEqual(Catalog, AutomationCatalogFilter.Apply(Catalog, null));
		Assert.AreSequenceEqual(Catalog, AutomationCatalogFilter.Apply(Catalog, string.Empty));

		// A search box that only contains spaces is not a search.
		Assert.AreSequenceEqual(Catalog, AutomationCatalogFilter.Apply(Catalog, "   "));
	}

	[TestMethod]
	public void Package_name_match_keeps_every_action()
	{
		var matches = AutomationCatalogFilter.Apply(Catalog, "tracer");

		var package = matches.Single();
		Assert.AreEqual("vxfiles.tracer", package.Id.Value);
		Assert.AreEqual(2, package.Actions.Length, "A package the user asked for keeps all of its actions.");
	}

	[TestMethod]
	public void Package_description_match_keeps_every_action()
	{
		var matches = AutomationCatalogFilter.Apply(Catalog, "pictures");

		var package = matches.Single();
		Assert.AreEqual("contoso.images", package.Id.Value);
		Assert.AreEqual(2, package.Actions.Length);
	}

	[TestMethod]
	public void Action_match_keeps_only_the_matching_children()
	{
		var matches = AutomationCatalogFilter.Apply(Catalog, "resize");

		var package = matches.Single();
		Assert.AreEqual("contoso.images", package.Id.Value);
		Assert.AreEqual("resize", package.Actions.Single().Id.LocalId.Value);
	}

	[TestMethod]
	public void Action_description_match_keeps_its_package()
	{
		var matches = AutomationCatalogFilter.Apply(Catalog, "interpreter version");

		Assert.AreEqual("vxfiles.tracer", matches.Single().Id.Value);
		Assert.AreEqual("check-runtime", matches.Single().Actions.Single().Id.LocalId.Value);
	}

	[TestMethod]
	public void Matching_ignores_case_and_surrounding_whitespace()
	{
		var matches = AutomationCatalogFilter.Apply(Catalog, "  IMAGE TOOLS  ");

		Assert.AreEqual("contoso.images", matches.Single().Id.Value);
	}

	[TestMethod]
	public void Unmatched_filter_returns_nothing()
	{
		Assert.IsTrue(AutomationCatalogFilter.Apply(Catalog, "spreadsheet").IsEmpty);
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
