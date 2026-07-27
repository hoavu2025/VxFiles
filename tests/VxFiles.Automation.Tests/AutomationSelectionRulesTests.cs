// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// Covers the admission rule shared by the Tools tab's Run button and the session's own invocation check. A gap
/// between the two shows up here as a wrong verdict rather than as a button that lies about what will happen.
/// </summary>
[TestClass]
public sealed class AutomationSelectionRulesTests
{
	[TestMethod]
	public void Selection_inside_the_policy_is_eligible()
	{
		var policy = Policy(1, 5, ["file"], [".txt"]);

		Assert.AreEqual(
			AutomationSelectionEligibility.Eligible,
			AutomationSelectionRules.Evaluate(policy, Selection(File(@"C:\notes\a.txt"))));
	}

	[TestMethod]
	public void Empty_selection_fails_a_policy_that_requires_items()
	{
		Assert.AreEqual(
			AutomationSelectionEligibility.TooFewItems,
			AutomationSelectionRules.Evaluate(Policy(1, 5, ["file"], []), Selection()));
	}

	/// <summary>
	/// An action declaring <c>minItems: 0</c> runs against the folder alone, so nothing selected is a legal input.
	/// </summary>
	[TestMethod]
	public void Empty_selection_is_eligible_when_the_policy_allows_none()
	{
		Assert.AreEqual(
			AutomationSelectionEligibility.Eligible,
			AutomationSelectionRules.Evaluate(Policy(0, 5, ["file"], []), Selection()));
	}

	[TestMethod]
	public void Selection_beyond_the_maximum_is_rejected()
	{
		var policy = Policy(1, 1, ["file"], []);

		Assert.AreEqual(
			AutomationSelectionEligibility.TooManyItems,
			AutomationSelectionRules.Evaluate(policy, Selection(File(@"C:\a.txt"), File(@"C:\b.txt"))));
	}

	[TestMethod]
	public void Folder_is_rejected_by_a_file_only_policy()
	{
		var policy = Policy(1, 5, ["file"], []);

		Assert.AreEqual(
			AutomationSelectionEligibility.UnsupportedItemKind,
			AutomationSelectionRules.Evaluate(policy, Selection(Folder(@"C:\pictures"))));
	}

	[TestMethod]
	public void Extension_outside_the_policy_is_rejected()
	{
		var policy = Policy(1, 5, ["file"], [".png"]);

		Assert.AreEqual(
			AutomationSelectionEligibility.UnsupportedExtension,
			AutomationSelectionRules.Evaluate(policy, Selection(File(@"C:\notes\a.txt"))));
	}

	[TestMethod]
	public void Extension_matching_ignores_case()
	{
		var policy = Policy(1, 5, ["file"], [".png"]);

		Assert.AreEqual(
			AutomationSelectionEligibility.Eligible,
			AutomationSelectionRules.Evaluate(policy, Selection(File(@"C:\pictures\a.PNG"))));
	}

	/// <summary>
	/// An empty extension list means the action takes any file, not that it takes none.
	/// </summary>
	[TestMethod]
	public void Empty_extension_list_accepts_any_file()
	{
		var policy = Policy(1, 5, ["file"], []);

		Assert.AreEqual(
			AutomationSelectionEligibility.Eligible,
			AutomationSelectionRules.Evaluate(policy, Selection(File(@"C:\notes\a.bin"))));
	}

	/// <summary>
	/// A folder carries no extension, so an extension list must not silently exclude every folder an action
	/// declares it accepts.
	/// </summary>
	[TestMethod]
	public void Extension_list_does_not_apply_to_folders()
	{
		var policy = Policy(1, 5, ["file", "folder"], [".png"]);

		Assert.AreEqual(
			AutomationSelectionEligibility.Eligible,
			AutomationSelectionRules.Evaluate(policy, Selection(Folder(@"C:\pictures"))));
	}

	[TestMethod]
	public void Relative_path_is_rejected_before_any_other_rule()
	{
		var policy = Policy(1, 5, ["file"], []);

		Assert.AreEqual(
			AutomationSelectionEligibility.PathNotFullyQualified,
			AutomationSelectionRules.Evaluate(policy, Selection(File(@"notes\a.txt"))));
	}

	/// <summary>
	/// A UNC path is fully qualified. Network locations are a routing concern, not an admission one.
	/// </summary>
	[TestMethod]
	public void Unc_path_is_accepted()
	{
		var policy = Policy(1, 5, ["file"], []);
		var selection = Selection(new SelectedPath(@"\\server\share\a.txt", SelectedPathKind.File, SelectedLocationKind.Unc));

		Assert.AreEqual(AutomationSelectionEligibility.Eligible, AutomationSelectionRules.Evaluate(policy, selection));
	}

	[TestMethod]
	public void Declared_kinds_use_the_manifest_vocabulary()
	{
		Assert.AreEqual("file", AutomationSelectionRules.ToDeclaredKind(SelectedPathKind.File));
		Assert.AreEqual("folder", AutomationSelectionRules.ToDeclaredKind(SelectedPathKind.Folder));
	}

	private static AutomationSelectionPolicy Policy(
		int minItems,
		int maxItems,
		ImmutableArray<string> itemKinds,
		ImmutableArray<string> extensions) => new(minItems, maxItems, itemKinds, extensions);

	private static SelectionSnapshot Selection(params SelectedPath[] items)
		=> new(@"C:\notes", DateTimeOffset.UtcNow, [.. items]);

	private static SelectedPath File(string path)
		=> new(path, SelectedPathKind.File, SelectedLocationKind.Local);

	private static SelectedPath Folder(string path)
		=> new(path, SelectedPathKind.Folder, SelectedLocationKind.Local);
}
