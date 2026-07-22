// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

[TestClass]
public sealed class AutomationHostBridgeTests
{
	[TestMethod]
	public void CaptureSelection_PreservesOrderKindAndLocation()
	{
		var context = new FakeHostContext(@"C:\work",
		[
			new(@"C:\work\second.txt", SelectedPathKind.File),
			new(@"\\server\share\folder", SelectedPathKind.Folder),
		]);
		using var bridge = new AutomationHostBridge(context);

		var snapshot = bridge.CaptureSelection();

		Assert.AreEqual(@"C:\work", snapshot.ActiveFolderPath);
		Assert.AreEqual(@"C:\work\second.txt", snapshot.Items[0].FullPath);
		Assert.AreEqual(SelectedPathKind.File, snapshot.Items[0].Kind);
		Assert.AreEqual(SelectedLocationKind.Local, snapshot.Items[0].LocationKind);
		Assert.AreEqual(SelectedLocationKind.Unc, snapshot.Items[1].LocationKind);
	}

	[TestMethod]
	public async Task RouteAsync_RefreshesAndRevealsOnlyCapturedFolderChildren()
	{
		var context = new FakeHostContext(@"C:\work", []);
		using var bridge = new AutomationHostBridge(context);
		var reveal = new AutomationResultIntent.RevealPaths([@"C:\work\output.mp4"]);
		var request = new AutomationResultRoutingRequest(
			new(Guid.NewGuid()), bridge.Revision, @"C:\work",
			[new AutomationResultIntent.RefreshCurrentFolder(), reveal]);

		var results = await bridge.RouteAsync(request);

		Assert.AreEqual(2, results.Length);
		Assert.IsTrue(results.All(result => result.Disposition is AutomationIntentDisposition.Applied));
		Assert.AreEqual(1, context.RefreshCount);
		CollectionAssert.AreEqual(new[] { @"C:\work\output.mp4" }, context.RevealedPaths.ToArray());
	}

	[TestMethod]
	public async Task RouteAsync_RejectsRevealOutsideCapturedFolder()
	{
		var context = new FakeHostContext(@"C:\work", []);
		using var bridge = new AutomationHostBridge(context);
		var reveal = new AutomationResultIntent.RevealPaths([@"C:\other\output.mp4"]);

		var results = await bridge.RouteAsync(new(
			new(Guid.NewGuid()), bridge.Revision, @"C:\work", [reveal]));

		Assert.AreEqual(AutomationIntentDisposition.Rejected, results[0].Disposition);
		Assert.IsTrue(context.RevealedPaths.IsEmpty);
	}

	[TestMethod]
	public async Task RouteAsync_MarksEveryIntentStaleAfterLocationChanges()
	{
		var context = new FakeHostContext(@"C:\work", []);
		using var bridge = new AutomationHostBridge(context);
		var capturedRevision = bridge.Revision;
		context.ChangeLocation(@"C:\elsewhere");

		var results = await bridge.RouteAsync(new(
			new(Guid.NewGuid()), capturedRevision, @"C:\work",
			[new AutomationResultIntent.RefreshCurrentFolder(), new AutomationResultIntent.RevealPaths([@"C:\work\output.mp4"])]));

		Assert.IsTrue(results.All(result => result.Disposition is AutomationIntentDisposition.Stale));
		Assert.AreEqual(0, context.RefreshCount);
		Assert.IsTrue(context.RevealedPaths.IsEmpty);
	}

	private sealed class FakeHostContext(
		string? currentFolderPath,
		IReadOnlyList<AutomationHostItem> selectedItems) : IAutomationHostContext
	{
		public event EventHandler? LocationChanged;

		public string? CurrentFolderPath { get; private set; } = currentFolderPath;

		public IReadOnlyList<AutomationHostItem> SelectedItems { get; } = selectedItems;

		public int RefreshCount { get; private set; }

		public ImmutableArray<string> RevealedPaths { get; private set; } = [];

		public ValueTask RefreshCurrentFolderAsync(CancellationToken cancellationToken)
		{
			RefreshCount++;
			return ValueTask.CompletedTask;
		}

		public ValueTask RevealDirectChildrenAsync(ImmutableArray<string> paths, CancellationToken cancellationToken)
		{
			RevealedPaths = paths;
			return ValueTask.CompletedTask;
		}

		public void ChangeLocation(string folder)
		{
			CurrentFolderPath = folder;
			LocationChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
