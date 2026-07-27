// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// Covers the tracer package that ships beside the app. These run against the real manifest and the real
/// pinned interpreter, so a packaging mistake fails here rather than on a user's machine.
/// </summary>
[TestClass]
public sealed class BundledAutomationPackageTests
{
	private const string TracerPackageId = "vxfiles.tracer";

	[TestMethod]
	public void Bundled_payload_is_laid_out_beside_the_app()
	{
		Assert.IsTrue(
			File.Exists(Path.Join(AutomationFixture.BundledPackageRoot, TracerPackageId, "vxpackage.json")),
			"The bundled tracer manifest is missing from the build output.");
		Assert.IsTrue(
			File.Exists(Path.Join(AppContext.BaseDirectory, "AutomationRuntime", "vxfiles_runner.py")),
			"The Automation runner script is missing from the build output.");
	}

	[TestMethod]
	public async Task Bundled_tracer_package_exposes_both_actions()
	{
		using var fixture = AutomationFixture.CreateForBundledPackages();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());

		var package = session.Snapshot.Packages.Single(item => item.Id.Value == TracerPackageId);

		Assert.AreEqual(
			AutomationAvailability.Available,
			package.Availability,
			string.Join("; ", package.Diagnostics));
		CollectionAssert.AreEquivalent(
			new[] { "check-runtime", "report-selection" },
			package.Actions.Select(action => action.Id.LocalId.Value).ToArray());
	}

	[TestMethod]
	public async Task Bundled_runtime_check_runs_on_the_packaged_interpreter()
	{
		using var fixture = AutomationFixture.CreateForBundledPackages();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());

		await session.InvokeAsync(fixture.Invocation(session, $"{TracerPackageId}/check-runtime"));

		var run = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, run.State, run.Failure + Environment.NewLine + run.StandardError);

		// Proves the action ran on the app-local pinned build rather than any interpreter on PATH.
		var logged = string.Join(Environment.NewLine, run.Logs.Select(entry => entry.Message));
		StringAssert.Contains(logged, "3.14.6");
		StringAssert.Contains(logged, "Isolated: True");
		Assert.IsTrue(
			logged.Contains(AutomationFixture.BundledPackageRoot, StringComparison.OrdinalIgnoreCase),
			$"Expected the working directory to sit under the bundled package root.{Environment.NewLine}{logged}");
	}

	[TestMethod]
	public async Task Bundled_selection_report_summarizes_without_touching_the_files()
	{
		using var fixture = AutomationFixture.CreateForBundledPackages();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());
		var selected = fixture.AddSelectedFile("holiday.mov");
		var contentBefore = File.ReadAllText(selected);

		await session.InvokeAsync(
			fixture.Invocation(session, $"{TracerPackageId}/report-selection", selectedPaths: [selected]));

		var run = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, run.State, run.Failure + Environment.NewLine + run.StandardError);

		// The terminal frame's summary is not retained on the snapshot, so the log is what proves the action
		// actually received the selection.
		var logged = string.Join(Environment.NewLine, run.Logs.Select(entry => entry.Message));
		StringAssert.Contains(logged, "holiday.mov");
		Assert.AreEqual(contentBefore, File.ReadAllText(selected), "The tracer must not modify the selection.");
	}
}
