// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

[TestClass]
public sealed class AutomationSessionTests
{
	private const string EchoSelection = """
		import json, sys
		request = json.load(sys.stdin)
		message = "|".join(item["path"] for item in request["items"])
		print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":message}))
		""";

	[TestMethod]
	public async Task Invocation_resolves_a_composite_action_identity_from_the_current_catalog_revision()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py"),
				AutomationManifests.Action("thumbnails", "thumbnails.py")),
			("convert.py", EchoSelection),
			("thumbnails.py", "pass"));
		var store = new MemoryStateStore();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, store, new AcceptingTrustConsent(), new RecordingResultRouter());

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));

		var run = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, run.State, run.Failure + Environment.NewLine + run.StandardError);
		Assert.AreEqual("hoa.media/convert", run.ActionId.Value);
	}

	[TestMethod]
	public async Task Unknown_action_in_a_known_package_is_rejected()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", "pass"));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.InvokeAsync(fixture.Invocation(session, "hoa.media/missing")).AsTask());

		StringAssert.Contains(exception.Message, "hoa.media/missing");
	}

	[TestMethod]
	public async Task Stale_catalog_revision_cannot_launch_an_action()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", "pass"));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());
		var stale = fixture.Invocation(session, "hoa.media/convert");

		fixture.UpdateFile("media", "convert.py", "print('changed')");
		await WaitForCatalogRevisionAsync(session, stale.CatalogRevision + 1);

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.InvokeAsync(stale).AsTask());
		StringAssert.Contains(exception.Message, "catalog changed");
		Assert.IsEmpty(session.Snapshot.RecentRuns);
	}

	[TestMethod]
	public async Task Trusted_run_transports_ordered_unicode_paths_and_reaches_success()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", EchoSelection));
		var store = new MemoryStateStore();
		var trust = new AcceptingTrustConsent();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, store, trust, new RecordingResultRouter());
		var selectedPaths = new[]
		{
			fixture.AddSelectedFile("clip one.mov"),
			fixture.AddSelectedFile("日本語 clip.mp4"),
		};

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert", 1, selectedPaths));

		Assert.AreEqual(1, trust.RequestCount);
		var run = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, run.State, run.Failure + Environment.NewLine + run.StandardError);
		Assert.AreEqual(string.Join('|', selectedPaths), run.Logs[0].Message);
		Assert.IsNotNull(store.TrustedFingerprintFor(AutomationManifests.DefaultPackageId));
	}

	[TestMethod]
	public async Task Trust_covers_the_whole_package_so_a_sibling_action_needs_no_second_consent()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py"),
				AutomationManifests.Action("thumbnails", "thumbnails.py")),
			("convert.py", "pass"),
			("thumbnails.py", "pass"));
		var trust = new AcceptingTrustConsent();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), trust, new RecordingResultRouter());

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/thumbnails"));

		Assert.AreEqual(1, trust.RequestCount);
		CollectionAssert.AreEquivalent(
			new[] { "hoa.media/convert", "hoa.media/thumbnails" },
			trust.Requests[0].Actions.Select(action => action.Value).ToArray());
		Assert.IsTrue(session.Snapshot.RecentRuns.All(run => run.State is AutomationRunState.Succeeded));
	}

	[TestMethod]
	public async Task Changing_any_package_file_renews_trust_for_every_action()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py"),
				AutomationManifests.Action("thumbnails", "thumbnails.py")),
			("convert.py", "pass"),
			("thumbnails.py", "pass"));
		var store = new MemoryStateStore();
		var trust = new AcceptingTrustConsent();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, store, trust, new RecordingResultRouter());
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		var firstFingerprint = store.TrustedFingerprintFor(AutomationManifests.DefaultPackageId);

		var revision = session.Snapshot.CatalogRevision;
		fixture.UpdateFile("media", "thumbnails.py", "print('changed')");
		await WaitForCatalogRevisionAsync(session, revision + 1);
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));

		Assert.AreEqual(2, trust.RequestCount);
		Assert.AreNotEqual(firstFingerprint, store.TrustedFingerprintFor(AutomationManifests.DefaultPackageId));
	}

	/// <summary>
	/// Trust covers the runner scripts as well as the interpreter. <c>vxfiles_runner.py</c> stands between the
	/// host and every action, so editing it must renew consent exactly as editing <c>python.exe</c> would;
	/// otherwise trust granted to a package would carry over to a runner the user never agreed to.
	/// </summary>
	[TestMethod]
	[DataRow("runner-change.marker", DisplayName = "a new file in the runtime tree")]
	[DataRow("vxfiles_runner.py", DisplayName = "the runner script itself")]
	public async Task Changed_app_local_runner_content_renews_trust(string changedFileName)
	{
		using var fixture = AutomationFixture.Create(copyRuntime: true);
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", "pass"));
		var store = new MemoryStateStore();
		var trust = new AcceptingTrustConsent();

		await InvokeOnceAsync();
		Assert.AreEqual(1, trust.RequestCount);
		var firstFingerprint = store.TrustedFingerprintFor(AutomationManifests.DefaultPackageId);

		var changedPath = Path.Join(fixture.RuntimeDirectory, changedFileName);
		await File.AppendAllTextAsync(changedPath, $"{Environment.NewLine}# changed{Environment.NewLine}");
		await InvokeOnceAsync();

		Assert.AreEqual(2, trust.RequestCount);
		Assert.AreNotEqual(firstFingerprint, store.TrustedFingerprintFor(AutomationManifests.DefaultPackageId));

		async Task InvokeOnceAsync()
		{
			await using IAutomationSession session = new AutomationSession(
				fixture.Options,
				AutomationManifestCatalog.Discover(fixture.Options.CatalogOptions),
				store,
				trust,
				new RecordingResultRouter());
			await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		}
	}

	/// <summary>
	/// Running an action must not change anything the trust fingerprint covers. An action that imports a helper
	/// makes CPython write a <c>__pycache__</c> beside it unless it is stopped, and both the package folder and
	/// the runtime tree are fingerprinted — so the first run would silently invalidate its own trust and the
	/// second would prompt again.
	/// </summary>
	/// <remarks>
	/// This cannot be done with an environment variable: the runner launches with <c>-I</c>, which implies
	/// <c>-E</c> and makes the interpreter ignore <c>PYTHONDONTWRITEBYTECODE</c>. Only the <c>-B</c> flag works.
	/// </remarks>
	[TestMethod]
	public async Task Importing_a_helper_does_not_disturb_the_fingerprinted_trees()
	{
		using var fixture = AutomationFixture.Create();
		var packagePath = fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("helper.py", "VALUE = 1"),
			("convert.py", """
				import os, sys
				sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
				import helper
				"""));
		var trust = new AcceptingTrustConsent();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), trust, new RecordingResultRouter());

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		var first = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, first.State, first.Failure + Environment.NewLine + first.StandardError);

		Assert.IsEmpty(
			Directory.GetDirectories(packagePath, "__pycache__", SearchOption.AllDirectories),
			"A run wrote bytecode into the package it was granted trust for.");
		Assert.IsEmpty(
			Directory.GetDirectories(fixture.RuntimeDirectory, "__pycache__", SearchOption.AllDirectories),
			"A run wrote bytecode into the runtime tree that package trust covers.");

		// The observable consequence: the same action, unchanged, must not ask for consent a second time.
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		Assert.AreEqual(1, trust.RequestCount, "An unchanged package asked for trust twice.");
	}

	[TestMethod]
	public async Task Declined_trust_runs_nothing_and_records_nothing()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", "raise SystemExit(3)"));
		var store = new MemoryStateStore();
		var trust = new DecliningTrustConsent();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, store, trust, new RecordingResultRouter());

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));

		Assert.AreEqual(1, trust.RequestCount);
		Assert.IsEmpty(session.Snapshot.RecentRuns);
		Assert.IsEmpty(store.Records);
		Assert.IsNull(store.TrustedFingerprintFor(AutomationManifests.DefaultPackageId));
	}

	[TestMethod]
	public async Task Strict_ndjson_violations_and_nonzero_exit_fail_the_run()
	{
		var cases = new (string Name, string Script, string ExpectedFailure)[]
		{
			("malformed", "print('not json')", "invalid JSON literal"),
			("sequence", """print(r'{"protocol":"ndjson-v1","sequence":2,"type":"log","level":"info","message":"bad"}')""", "sequence must be 1"),
			("unknown-type", """print(r'{"protocol":"ndjson-v1","sequence":1,"type":"mystery"}')""", "Unknown NDJSON frame type"),
			("unknown-field", """print(r'{"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":"bad","extra":1}')""", "Unknown NDJSON property 'extra'"),
			("after-result", """print(r'{"protocol":"ndjson-v1","sequence":1,"type":"result","outcome":"succeeded"}'); print(r'{"protocol":"ndjson-v1","sequence":2,"type":"log","level":"info","message":"late"}')""", "after the terminal result"),
			("nonzero", "raise SystemExit(7)", "exited with code 7"),
		};

		foreach (var (name, script, expectedFailure) in cases)
		{
			var run = await RunScriptAsync(script);
			Assert.AreEqual(AutomationRunState.Failed, run.State, $"Case '{name}' should fail.");
			StringAssert.Contains(run.Failure!, expectedFailure, $"Case '{name}' should retain a deterministic reason.");
		}
	}

	[TestMethod]
	public async Task Output_and_stderr_budgets_are_enforced_independently()
	{
		var outputRun = await RunScriptAsync(
			"import sys; sys.stdout.write('x' * 70000)",
			AutomationManifests.Action("convert", "convert.py", outputProtocol: "exit-code", maximumOutputBytes: 65_536));

		Assert.AreEqual(AutomationRunState.Failed, outputRun.State);
		StringAssert.Contains(outputRun.Failure!, "Standard output exceeded the 65536-byte budget");

		var stderrRun = await RunScriptAsync("import sys; sys.stderr.write('e' * 1100000)");
		Assert.AreEqual(AutomationRunState.Succeeded, stderrRun.State, stderrRun.Failure);
		Assert.IsTrue(stderrRun.StandardErrorTruncated);
		Assert.IsLessThanOrEqualTo(1_048_576, Encoding.UTF8.GetByteCount(stderrRun.StandardError));
	}

	[TestMethod]
	public async Task Manifest_timeout_wins_even_when_the_action_ignores_the_cancellation_signal()
	{
		var run = await RunScriptAsync(
			"import time\nwhile True: time.sleep(0.05)",
			AutomationManifests.Action("convert", "convert.py", timeoutSeconds: 1));

		Assert.AreEqual(AutomationRunState.TimedOut, run.State);
		StringAssert.Contains(run.Failure!, "exceeded its 1-second timeout");
	}

	[TestMethod]
	public async Task Cooperative_cancellation_closes_the_owned_child_process_job()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", """
				import json, subprocess, sys, time
				import vxfiles_automation
				child = subprocess.Popen(
				    [sys.executable, "-I", "-c", "import time; time.sleep(60)"],
				    stdin=subprocess.DEVNULL,
				    stdout=subprocess.DEVNULL,
				    stderr=subprocess.DEVNULL)
				print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":str(child.pid)}), flush=True)
				while not vxfiles_automation.cancellation_requested():
				    time.sleep(0.02)
				"""));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());

		var invoke = session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert")).AsTask();
		var childProcessId = await WaitForChildProcessIdAsync(session);
		var runId = session.Snapshot.ActiveRuns[0].Id;
		var stopwatch = Stopwatch.StartNew();
		await session.CancelAsync(runId);
		await invoke;
		stopwatch.Stop();

		Assert.IsLessThan(TimeSpan.FromSeconds(4), stopwatch.Elapsed, "The action should observe cooperative cancellation before forced termination.");
		Assert.AreEqual(AutomationRunState.Cancelled, session.Snapshot.RecentRuns[0].State);
		await AssertProcessTreeIsGoneAsync(childProcessId);
	}

	[TestMethod]
	public async Task Closing_the_session_forces_an_uncooperative_process_tree()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", """
				import json, subprocess, sys, time
				child = subprocess.Popen(
				    [sys.executable, "-I", "-c", "import time; time.sleep(60)"],
				    stdin=subprocess.DEVNULL,
				    stdout=subprocess.DEVNULL,
				    stderr=subprocess.DEVNULL)
				print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":str(child.pid)}), flush=True)
				time.sleep(60)
				"""));
		var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());
		var invoke = session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert")).AsTask();
		var childProcessId = await WaitForChildProcessIdAsync(session);

		var stopwatch = Stopwatch.StartNew();
		await session.DisposeAsync();
		await invoke;
		stopwatch.Stop();

		Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.8), stopwatch.Elapsed);
		Assert.IsLessThan(TimeSpan.FromSeconds(6), stopwatch.Elapsed);
		await AssertProcessTreeIsGoneAsync(childProcessId);
	}

	[TestMethod]
	public async Task Cancellation_during_request_transport_is_reported_as_cancelled()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", "pass"));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());
		using var cancellation = new CancellationTokenSource();
		var invocation = fixture.Invocation(session, "hoa.media/convert");
		cancellation.Cancel();

		await session.InvokeAsync(invocation, cancellation.Token);

		Assert.IsEmpty(session.Snapshot.ActiveRuns);
		var run = session.Snapshot.RecentRuns.Single();
		Assert.AreEqual(AutomationRunState.Cancelled, run.State);
		StringAssert.Contains(run.Failure!, "cancelled");
	}

	[TestMethod]
	public async Task Concurrency_is_single_per_package_two_global_and_never_queues()
	{
		using var fixture = AutomationFixture.Create();
		foreach (var name in new[] { "one", "two", "three" })
		{
			fixture.AddPackage(
				name,
				AutomationManifests.Package(
					$"hoa.{name}",
					AutomationManifests.Action("convert", "convert.py"),
					AutomationManifests.Action("thumbnails", "thumbnails.py")),
				("convert.py", "import time; time.sleep(1)"),
				("thumbnails.py", "import time; time.sleep(1)"));
		}

		var store = new BlockingStateStore();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, store, new AcceptingTrustConsent(), new RecordingResultRouter());

		var first = session.InvokeAsync(fixture.Invocation(session, "hoa.one/convert")).AsTask();

		// A sibling action in a busy package is refused outright rather than queued behind the first run.
		var siblingRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.InvokeAsync(fixture.Invocation(session, "hoa.one/thumbnails")).AsTask());
		StringAssert.Contains(siblingRefusal.Message, "hoa.one");

		var second = session.InvokeAsync(fixture.Invocation(session, "hoa.two/convert")).AsTask();
		await store.WaitForTwoReadsAsync();

		var globalRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.InvokeAsync(fixture.Invocation(session, "hoa.three/convert")).AsTask());
		StringAssert.Contains(globalRefusal.Message, "already running");

		store.Release();
		await Task.WhenAll(first, second);
		Assert.AreEqual(2, session.Snapshot.RecentRuns.Length);
	}

	[TestMethod]
	public async Task Successful_result_intents_are_routed_with_the_captured_host_revision()
	{
		using var fixture = AutomationFixture.Create();
		var revealPath = fixture.AddSelectedFile("created.mov");
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py",
				"import json\nprint(json.dumps({\"protocol\":\"ndjson-v1\",\"sequence\":1,\"type\":\"result\",\"outcome\":\"succeeded\",\"effects\":[{\"type\":\"refreshCurrentFolder\"},{\"type\":\"revealPaths\",\"paths\":[" +
				JsonSerializer.Serialize(revealPath) + "]}]}))"));
		var router = new RecordingResultRouter();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), router);

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert", hostRevision: 42));

		Assert.AreEqual(AutomationRunState.Succeeded, session.Snapshot.RecentRuns[0].State, session.Snapshot.RecentRuns[0].Failure);
		Assert.IsNotNull(router.LastRequest);
		Assert.AreEqual(new HostRevision(42), router.LastRequest.HostRevision);
		Assert.AreEqual(fixture.SelectionRoot, router.LastRequest.CapturedFolderPath);
		Assert.HasCount(2, router.LastRequest.Intents);
		Assert.IsInstanceOfType<AutomationResultIntent.RefreshCurrentFolder>(router.LastRequest.Intents[0]);
		CollectionAssert.AreEqual(
			new[] { revealPath },
			((AutomationResultIntent.RevealPaths)router.LastRequest.Intents[1]).Paths.ToArray());
	}

	[TestMethod]
	public async Task Argv_paths_are_exact_and_oversized_command_lines_fail_before_launch()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py", inputMode: "argv-paths", outputProtocol: "exit-code")),
			("convert.py", "import json, sys; print(json.dumps(sys.argv[1:], ensure_ascii=False))"));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());
		var selectedPaths = new[]
		{
			fixture.AddSelectedFile("space name.mov"),
			fixture.AddSelectedFile("unicodé.mov"),
		};

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert", 1, selectedPaths));

		var run = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, run.State, run.Failure + Environment.NewLine + run.StandardError);
		CollectionAssert.AreEqual(selectedPaths, JsonSerializer.Deserialize<string[]>(run.Logs[0].Message)!);

		var oversizedPath = "C:\\" + new string('a', 30_000) + ".mov";
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert", 2, oversizedPath));
		Assert.AreEqual(AutomationRunState.Failed, session.Snapshot.RecentRuns[0].State);
		StringAssert.Contains(session.Snapshot.RecentRuns[0].Failure!, "command-line size");
	}

	[TestMethod]
	public async Task Missing_external_tool_reports_configure_guidance_and_marks_the_package()
	{
		using var fixture = AutomationFixture.Create();
		var manifest = AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py", extraProperties: """

				  "externalTools": ["hoa.ffmpeg"],
				"""))
			.Replace(
				"""
				  "author": "Hoa",
				""",
				"""
				  "author": "Hoa",
				  "externalTools": [{ "id": "hoa.ffmpeg", "displayName": "FFmpeg" }],
				""",
				StringComparison.Ordinal);
		fixture.AddPackage("media", manifest, ("convert.py", "pass"));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());

		var exception = await Assert.ThrowsExactlyAsync<AutomationMissingDependencyException>(
			() => session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert")).AsTask());

		StringAssert.Contains(exception.Message, "FFmpeg");
		var package = session.Snapshot.Packages.Single();
		Assert.AreEqual(AutomationAvailability.MissingDependency, package.Availability);
		StringAssert.Contains(package.Diagnostics.Single(), "FFmpeg");
	}

	[TestMethod]
	public async Task Settings_are_action_specific_and_are_transported_to_the_running_action()
	{
		using var fixture = AutomationFixture.Create();
		var setting = """

			  "settings": [
			    {
			      "key": "quality",
			      "displayName": "Quality",
			      "description": "Encoding quality",
			      "type": "integer",
			      "default": 20,
			      "minimum": 0,
			      "maximum": 51
			    }
			  ],
			""";
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py", extraProperties: setting),
				AutomationManifests.Action("thumbnails", "thumbnails.py", extraProperties: setting)),
			("convert.py", """
				import json, sys
				request = json.load(sys.stdin)
				print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":str(request["settings"]["quality"])}))
				"""),
			("thumbnails.py", """
				import json, sys
				request = json.load(sys.stdin)
				print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":str(request["settings"]["quality"])}))
				"""));
		var store = new MemoryStateStore();
		store.ConfigureSettings(
			"hoa.media/convert",
			ImmutableDictionary<string, AutomationSettingValue>.Empty
				.Add("quality", new(AutomationSettingValueKind.Integer, IntegerValue: 33)));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, store, new AcceptingTrustConsent(), new RecordingResultRouter());

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/thumbnails"));

		var configured = session.Snapshot.RecentRuns.Single(run => run.ActionId.Value is "hoa.media/convert");
		var defaulted = session.Snapshot.RecentRuns.Single(run => run.ActionId.Value is "hoa.media/thumbnails");
		Assert.AreEqual(AutomationRunState.Succeeded, configured.State, configured.Failure + configured.StandardError);
		Assert.AreEqual("33", configured.Logs[0].Message);
		Assert.AreEqual("20", defaulted.Logs[0].Message);
	}

	[TestMethod]
	public async Task Run_history_evicts_expired_records_and_stays_within_its_size_budget()
	{
		using var fixture = AutomationFixture.Create();
		var runs = Path.Join(fixture.Options.StateRoot, "runs");
		Directory.CreateDirectory(runs);
		var expired = Path.Join(runs, "000_expired.json");
		await File.WriteAllTextAsync(expired, "expired");
		File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-8));
		await File.WriteAllTextAsync(Path.Join(runs, "999_oversized.json"), new string('x', 900));
		var store = new FileAutomationStateStore(fixture.Options.StateRoot, TimeSpan.FromDays(7), 1024);

		await store.AppendRunRecordAsync(RunRecord(fixture, "hoa.media/convert"));

		Assert.IsFalse(File.Exists(expired));
		Assert.IsLessThanOrEqualTo(1024, Directory.GetFiles(runs, "*.json").Sum(path => new FileInfo(path).Length));
	}

	[TestMethod]
	public async Task Run_history_is_recorded_per_composite_action_identity()
	{
		using var fixture = AutomationFixture.Create();
		var store = new FileAutomationStateStore(fixture.Options.StateRoot);

		await store.AppendRunRecordAsync(RunRecord(fixture, "hoa.media/convert"));
		await store.AppendRunRecordAsync(RunRecord(fixture, "hoa.media/thumbnails"));

		var recordedActions = Directory.GetFiles(Path.Join(fixture.Options.StateRoot, "runs"), "*.json")
			.Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("actionId").GetString())
			.ToArray();
		CollectionAssert.AreEquivalent(new[] { "hoa.media/convert", "hoa.media/thumbnails" }, recordedActions);
	}

	private static AutomationRunRecord RunRecord(AutomationFixture fixture, string actionId)
	{
		var now = DateTimeOffset.UtcNow;
		return new(
			new(
				new(Guid.NewGuid()),
				AutomationFixture.ParseActionId(actionId),
				AutomationRunState.Succeeded,
				new(fixture.SelectionRoot, now, []),
				new(1),
				now,
				now,
				null,
				"Complete",
				[],
				string.Empty,
				false,
				[],
				null),
			"1.0.0",
			"sha256:test");
	}

	[TestMethod]
	public async Task Package_trust_and_action_settings_persist_in_separate_files()
	{
		using var fixture = AutomationFixture.Create();
		var store = new FileAutomationStateStore(fixture.Options.StateRoot);
		var packageId = AutomationPackageId.Parse(AutomationManifests.DefaultPackageId);

		await store.WritePackageTrustAsync(packageId, "sha256:trusted");

		Assert.AreEqual("sha256:trusted", (await store.ReadPackageStateAsync(packageId)).TrustedFingerprint);
		Assert.IsTrue(File.Exists(Path.Join(fixture.Options.StateRoot, "packages", "hoa.media.json")));
		Assert.IsEmpty((await store.ReadActionSettingsAsync(AutomationFixture.ParseActionId("hoa.media/convert"))).Values);
	}

	[TestMethod]
	public async Task Python_executable_must_match_the_pinned_hash_before_launch()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(AutomationManifests.DefaultPackageId, AutomationManifests.Action("convert", "convert.py")),
			("convert.py", "pass"));

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AutomationModule.OpenAsync(
			fixture.Options with { Runtime = fixture.Options.Runtime with { PythonSha256 = new string('0', 64) } },
			new MemoryStateStore(),
			new AcceptingTrustConsent(),
			new RecordingResultRouter()).AsTask());

		StringAssert.Contains(exception.Message, "pinned by VxFiles");
	}

	private static async Task<AutomationRunSnapshot> RunScriptAsync(string script, string? action = null)
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				action ?? AutomationManifests.Action("convert", "convert.py")),
			("convert.py", script));
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		return session.Snapshot.RecentRuns[0];
	}

	private static async Task<int> WaitForChildProcessIdAsync(IAutomationSession session)
	{
		var deadline = DateTime.UtcNow.AddSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			var log = session.Snapshot.ActiveRuns.FirstOrDefault()?.Logs.FirstOrDefault();
			if (log is not null && int.TryParse(log.Message, out var processId))
				return processId;
			await Task.Delay(25);
		}

		throw new AssertFailedException("The action never reported its child process id.");
	}

	private static async Task AssertProcessTreeIsGoneAsync(int processId)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (!IsProcessAlive(processId))
				return;
			await Task.Delay(50);
		}

		try
		{
			using var survivor = Process.GetProcessById(processId);
			survivor.Kill(true);
		}
		catch (ArgumentException)
		{
			return;
		}

		throw new AssertFailedException($"Child process {processId} survived its Automation Job Object.");
	}

	private static bool IsProcessAlive(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			process.Refresh();
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static async Task WaitForCatalogRevisionAsync(IAutomationSession session, long minimumRevision)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			if (session.Snapshot.CatalogRevision >= minimumRevision)
				return;
			await Task.Delay(10);
		}

		throw new AssertFailedException($"Expected Automation catalog revision {minimumRevision}.");
	}
}
