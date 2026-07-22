// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VxFiles.Automation;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

[TestClass]
public sealed class AutomationCatalogTests
{
	[TestMethod]
	public async Task Opening_session_discovers_a_strictly_valid_action_package()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage("video-convert", ValidManifest, "print('ready')");

		await using var session = await AutomationModule.OpenAsync(fixture.Options);

		Assert.AreEqual(1, session.Snapshot.CatalogRevision);
		Assert.HasCount(1, session.Snapshot.Actions);
		var action = session.Snapshot.Actions[0];
		Assert.AreEqual("hoa.video.convert-mp4", action.Id.Value);
		Assert.AreEqual("Convert to MP4", action.DisplayName);
		Assert.AreEqual(AutomationActionAvailability.RequiresTrust, action.Availability);
		Assert.IsEmpty(action.Diagnostics);
	}

	[TestMethod]
	public async Task Catalog_revisions_are_monotonic_stale_invocations_fail_and_parse_errors_keep_last_valid_catalog()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage("video-convert", ValidManifest, "print('ready')");
		await using var session = await AutomationModule.OpenAsync(fixture.Options);
		var originalRevision = session.Snapshot.CatalogRevision;
		var selectedPath = fixture.AddSelectedFile("input.mov");
		var staleInvocation = new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"),
			originalRevision,
			new HostRevision(1),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)]));

		fixture.UpdateScript("video-convert", "print('changed')");
		await WaitForCatalogRevisionAsync(session, originalRevision + 1);
		var changedRevision = session.Snapshot.CatalogRevision;
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.InvokeAsync(staleInvocation).AsTask());

		fixture.UpdateManifest("video-convert", "{");
		await WaitForCatalogRevisionAsync(session, changedRevision + 1);
		Assert.IsGreaterThan(changedRevision, session.Snapshot.CatalogRevision);
		Assert.AreEqual("Convert to MP4", session.Snapshot.Actions[0].DisplayName);
		Assert.AreEqual(AutomationActionAvailability.RequiresTrust, session.Snapshot.Actions[0].Availability);
		StringAssert.Contains(session.Snapshot.Actions[0].Diagnostics[^1], "last valid catalog entry remains active");
	}

	[TestMethod]
	public async Task Catalog_change_during_trust_consent_cannot_launch_the_stale_action()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage("video-convert", ValidManifest, "pass");
		var trust = new BlockingTrustConsent();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), trust, new RecordingResultRouter());
		var selectedPath = fixture.AddSelectedFile("input.mov");
		var revision = session.Snapshot.CatalogRevision;
		var invoke = session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"), revision, new HostRevision(1),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)]))).AsTask();
		await trust.WaitForRequestAsync();
		fixture.UpdateManifest("video-convert", ValidManifest.Replace("Convert to MP4", "Changed action", StringComparison.Ordinal));
		await WaitForCatalogRevisionAsync(session, revision + 1);
		trust.Accept();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => invoke);
		Assert.IsEmpty(session.Snapshot.RecentRuns);
	}

	[TestMethod]
	public async Task Unknown_manifest_property_disables_the_action_with_a_diagnostic()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"invalid",
			ValidManifest.Replace(
				"\"schemaVersion\": 1,",
				"\"schemaVersion\": 1, \"unexpected\": true,",
				StringComparison.Ordinal),
			"print('should not run')");

		await using var session = await AutomationModule.OpenAsync(fixture.Options);

		Assert.HasCount(1, session.Snapshot.Actions);
		var action = session.Snapshot.Actions[0];
		Assert.AreEqual(AutomationActionAvailability.Disabled, action.Availability);
		Assert.HasCount(1, action.Diagnostics);
		StringAssert.Contains(action.Diagnostics[0], "Unknown property 'unexpected'");
	}

	[TestMethod]
	public async Task Duplicate_json_property_disables_the_action_with_a_diagnostic()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"invalid",
			ValidManifest.Replace(
				"\"id\": \"hoa.video.convert-mp4\",",
				"\"id\": \"hoa.video.convert-mp4\", \"id\": \"hoa.video.other\",",
				StringComparison.Ordinal),
			"print('should not run')");

		await using var session = await AutomationModule.OpenAsync(fixture.Options);

		var action = session.Snapshot.Actions[0];
		Assert.AreEqual(AutomationActionAvailability.Disabled, action.Availability);
		StringAssert.Contains(action.Diagnostics[0], "Duplicate property 'id'");
	}

	[TestMethod]
	public async Task Escaping_entry_point_disables_the_action_with_a_diagnostic()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"invalid",
			ValidManifest.Replace("convert.py", "../outside.py", StringComparison.Ordinal),
			"print('should not run')");

		await using var session = await AutomationModule.OpenAsync(fixture.Options);

		var action = session.Snapshot.Actions[0];
		Assert.AreEqual(AutomationActionAvailability.Disabled, action.Availability);
		StringAssert.Contains(action.Diagnostics[0], "entryPoint must remain inside the package");
	}

	[TestMethod]
	public async Task Invalid_v1_contract_values_are_disabled_deterministically()
	{
		var cases = new (string Name, string Manifest, string ExpectedDiagnostic)[]
		{
			("schema", Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"), "schemaVersion must be 1"),
			("id", Replace("hoa.video.convert-mp4", "bad"), "id must be publisher-qualified"),
			("package-version", Replace("\"packageVersion\": \"1.0.0\"", "\"packageVersion\": \"one\""), "packageVersion must be SemVer 2.0"),
			("host-version", Replace("\"minimumHostVersion\": \"1.0.0\"", "\"minimumHostVersion\": \"9.0.0\""), "requires host version"),
			("python", Replace(">=3.14,<3.15", ">=3.13,<3.14"), "does not include bundled Python 3.14.6"),
			("mode", Replace("\"mode\": \"json-stdin\"", "\"mode\": \"shell\""), "input.mode must be 'json-stdin' or 'argv-paths'"),
			("selection-count", Replace("\"maxItems\": 100", "\"maxItems\": 10001"), "maxItems must be between minItems and 10000"),
			("extension", Replace("\".mov\"", "\"mov\""), "extensions must begin with '.'"),
			("timeout", Replace("\"timeoutSeconds\": 3600", "\"timeoutSeconds\": 0"), "timeoutSeconds must be between 1 and 86400"),
			("output-budget", Replace("\"maxOutputBytes\": 1048576", "\"maxOutputBytes\": 1024"), "maxOutputBytes must be between 65536 and 16777216"),
			("concurrency", Replace("\"concurrency\": \"single\"", "\"concurrency\": \"parallel\""), "execution.concurrency must be 'single'"),
			("protocol", Replace("\"protocol\": \"ndjson-v1\"", "\"protocol\": \"unknown\""), "output.protocol must be 'ndjson-v1' or 'exit-code'"),
			("nested-unknown", Replace("\"requires\": \">=3.14,<3.15\"", "\"requires\": \">=3.14,<3.15\", \"extra\": true"), "Unknown property 'extra'"),
			("wrong-type", Replace("\"displayName\": \"Convert to MP4\"", "\"displayName\": 42"), "displayName must be a string"),
		};

		foreach (var testCase in cases)
		{
			using var fixture = AutomationFixture.Create();
			fixture.AddPackage(testCase.Name, testCase.Manifest, "print('should not run')");

			await using var session = await AutomationModule.OpenAsync(fixture.Options);
			var action = session.Snapshot.Actions[0];

			Assert.AreEqual(
				AutomationActionAvailability.Disabled,
				action.Availability,
				$"Case '{testCase.Name}' should be disabled.");
			StringAssert.Contains(
				string.Join(Environment.NewLine, action.Diagnostics),
				testCase.ExpectedDiagnostic,
				$"Case '{testCase.Name}' should explain the contract violation.");
		}

		static string Replace(string oldValue, string newValue) =>
			ValidManifest.Replace(oldValue, newValue, StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task Duplicate_action_ids_disable_every_conflicting_package()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage("first", ValidManifest, "print('first')");
		fixture.AddPackage("second", ValidManifest, "print('second')");

		await using var session = await AutomationModule.OpenAsync(fixture.Options);

		Assert.HasCount(2, session.Snapshot.Actions);
		foreach (var action in session.Snapshot.Actions)
		{
			Assert.AreEqual(AutomationActionAvailability.Disabled, action.Availability);
			StringAssert.Contains(action.Diagnostics[0], "Duplicate action id 'hoa.video.convert-mp4'");
		}
	}

	[TestMethod]
	public async Task Trusted_json_run_preserves_ordered_unicode_paths_and_reaches_success()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage(
			"echo-selection",
			ValidManifest,
			"""
			import json, sys
			request = json.load(sys.stdin)
			message = "|".join(item["path"] for item in request["items"])
			print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":message}))
			""");
		var stateStore = new MemoryStateStore();
		var trust = new AcceptingTrustConsent();
		var router = new RecordingResultRouter();

		await using var session = await AutomationModule.OpenAsync(fixture.Options, stateStore, trust, router);
		var selectedPaths = ImmutableArray.Create(
			fixture.AddSelectedFile("clip one.mov"),
			fixture.AddSelectedFile("日本語 clip.mp4"));
		var invocation = new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"),
			session.Snapshot.CatalogRevision,
			new HostRevision(7),
			new SelectionSnapshot(
				fixture.SelectionRoot,
				DateTimeOffset.Parse("2026-07-21T10:00:00Z"),
				selectedPaths.Select(path => new SelectedPath(path, SelectedPathKind.File, SelectedLocationKind.Local)).ToImmutableArray()));

		await session.InvokeAsync(invocation);

		Assert.AreEqual(1, trust.RequestCount);
		Assert.HasCount(1, session.Snapshot.RecentRuns);
		var run = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, run.State, run.Failure + Environment.NewLine + run.StandardError);
		Assert.AreEqual(string.Join('|', selectedPaths), run.Logs[0].Message);
		Assert.IsNotNull(stateStore.State.TrustedFingerprint);
	}

	[TestMethod]
	public async Task Strict_ndjson_violations_and_nonzero_exit_fail_the_run()
	{
		var cases = new (string Name, string Script, string ExpectedFailure)[]
		{
			("malformed", "print('not json')", "invalid JSON literal"),
			("sequence", "print(r'{\"protocol\":\"ndjson-v1\",\"sequence\":2,\"type\":\"log\",\"level\":\"info\",\"message\":\"bad\"}')", "sequence must be 1"),
			("unknown-type", "print(r'{\"protocol\":\"ndjson-v1\",\"sequence\":1,\"type\":\"mystery\"}')", "Unknown NDJSON frame type"),
			("unknown-field", "print(r'{\"protocol\":\"ndjson-v1\",\"sequence\":1,\"type\":\"log\",\"level\":\"info\",\"message\":\"bad\",\"extra\":1}')", "Unknown NDJSON property 'extra'"),
			("after-result", "print(r'{\"protocol\":\"ndjson-v1\",\"sequence\":1,\"type\":\"result\",\"outcome\":\"succeeded\"}'); print(r'{\"protocol\":\"ndjson-v1\",\"sequence\":2,\"type\":\"log\",\"level\":\"info\",\"message\":\"late\"}')", "after the terminal result"),
			("nonzero", "raise SystemExit(7)", "exited with code 7"),
		};

		foreach (var testCase in cases)
		{
			var run = await RunScriptAsync(testCase.Script);
			Assert.AreEqual(AutomationRunState.Failed, run.State, $"Case '{testCase.Name}' should fail.");
			StringAssert.Contains(run.Failure!, testCase.ExpectedFailure, $"Case '{testCase.Name}' should retain a deterministic reason.");
		}
	}

	[TestMethod]
	public async Task Output_and_stderr_budgets_are_enforced_independently()
	{
		var exitCodeManifest = ValidManifest
			.Replace("\"protocol\": \"ndjson-v1\"", "\"protocol\": \"exit-code\"", StringComparison.Ordinal)
			.Replace("\"maxOutputBytes\": 1048576", "\"maxOutputBytes\": 65536", StringComparison.Ordinal);
		var outputRun = await RunScriptAsync("import sys; sys.stdout.write('x' * 70000)", exitCodeManifest);

		Assert.AreEqual(AutomationRunState.Failed, outputRun.State);
		StringAssert.Contains(outputRun.Failure!, "Standard output exceeded the 65536-byte budget");

		var stderrRun = await RunScriptAsync("import sys; sys.stderr.write('e' * 1100000)");
		Assert.AreEqual(AutomationRunState.Succeeded, stderrRun.State);
		Assert.IsTrue(stderrRun.StandardErrorTruncated);
		Assert.IsLessThanOrEqualTo(1_048_576, Encoding.UTF8.GetByteCount(stderrRun.StandardError));
	}

	[TestMethod]
	public async Task Cooperative_cancellation_closes_the_owned_child_process_job()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage(
			"child-process",
			ValidManifest,
			"""
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
			""");
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options,
			new MemoryStateStore(),
			new AcceptingTrustConsent(),
			new RecordingResultRouter());
		var selectedPath = fixture.AddSelectedFile("input.mov");
		var invocation = new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"),
			session.Snapshot.CatalogRevision,
			new HostRevision(1),
			new SelectionSnapshot(
				fixture.SelectionRoot,
				DateTimeOffset.UtcNow,
				[new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)]));

		var invokeTask = session.InvokeAsync(invocation).AsTask();
		var childPid = await WaitForChildPidAsync(session);
		var runId = session.Snapshot.ActiveRuns[0].Id;
		var stopwatch = Stopwatch.StartNew();
		await session.CancelAsync(runId);
		await invokeTask;
		stopwatch.Stop();

		Assert.IsLessThan(TimeSpan.FromSeconds(4), stopwatch.Elapsed, "The action should observe the cooperative cancellation event before forced termination.");
		Assert.AreEqual(AutomationRunState.Cancelled, session.Snapshot.RecentRuns[0].State);
		await Task.Delay(250);
		var childSurvived = false;
		try
		{
			using var child = Process.GetProcessById(childPid);
			child.Refresh();
			childSurvived = !child.HasExited;
			if (childSurvived)
				child.Kill(true);
		}
		catch (ArgumentException)
		{
			// The process no longer exists.
		}
		Assert.IsFalse(childSurvived, $"Child process {childPid} survived its Automation Job Object.");
	}

	[TestMethod]
	public async Task Closing_session_forces_an_uncooperative_process_tree_after_two_seconds()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage(
			"uncooperative",
			ValidManifest,
			"""
			import json, subprocess, sys, time
			child = subprocess.Popen(
			    [sys.executable, "-I", "-c", "import time; time.sleep(60)"],
			    stdin=subprocess.DEVNULL,
			    stdout=subprocess.DEVNULL,
			    stderr=subprocess.DEVNULL)
			print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":str(child.pid)}), flush=True)
			time.sleep(60)
			""");
		var session = await AutomationModule.OpenAsync(
			fixture.Options,
			new MemoryStateStore(),
			new AcceptingTrustConsent(),
			new RecordingResultRouter());
		var selectedPath = fixture.AddSelectedFile("input.mov");
		var invokeTask = session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"),
			session.Snapshot.CatalogRevision,
			new HostRevision(1),
			new SelectionSnapshot(
				fixture.SelectionRoot,
				DateTimeOffset.UtcNow,
				[new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)]))).AsTask();
		var childPid = await WaitForChildPidAsync(session);

		var stopwatch = Stopwatch.StartNew();
		await session.DisposeAsync();
		await invokeTask;
		stopwatch.Stop();

		Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.8), stopwatch.Elapsed);
		Assert.IsLessThan(TimeSpan.FromSeconds(4), stopwatch.Elapsed);
		await Task.Delay(250);
		Assert.IsFalse(IsProcessAlive(childPid), $"Child process {childPid} survived session shutdown.");
	}

	[TestMethod]
	public async Task Manifest_timeout_wins_even_when_worker_exits_cleanly_after_signal()
	{
		var timeoutManifest = ValidManifest.Replace(
			"\"timeoutSeconds\": 3600",
			"\"timeoutSeconds\": 1",
			StringComparison.Ordinal);
		var run = await RunScriptAsync(
			"import time, vxfiles_automation\nwhile not vxfiles_automation.cancellation_requested(): time.sleep(0.02)",
			timeoutManifest);

		Assert.AreEqual(AutomationRunState.TimedOut, run.State);
		StringAssert.Contains(run.Failure!, "exceeded its 1-second timeout");
	}

	[TestMethod]
	public async Task Unchanged_relocation_preserves_trust_but_content_change_renews_it()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage("original", ValidManifest, "pass");
		var selectedPath = fixture.AddSelectedFile("input.mov");
		var stateStore = new MemoryStateStore();
		var trust = new AcceptingTrustConsent();

		await InvokeAsync();
		Assert.AreEqual(1, trust.RequestCount);

		fixture.MovePackage("original", "relocated");
		await InvokeAsync();
		Assert.AreEqual(1, trust.RequestCount, "Package location must not participate in trust identity.");

		fixture.UpdateScript("relocated", "print('changed')");
		await InvokeAsync();
		Assert.AreEqual(2, trust.RequestCount, "Changed package content must renew trust.");

		async Task InvokeAsync()
		{
			await using var session = await AutomationModule.OpenAsync(
				fixture.Options,
				stateStore,
				trust,
				new RecordingResultRouter());
			await session.InvokeAsync(new AutomationInvocation(
				new AutomationActionId("hoa.video.convert-mp4"),
				session.Snapshot.CatalogRevision,
				new HostRevision(1),
				new SelectionSnapshot(
					fixture.SelectionRoot,
					DateTimeOffset.UtcNow,
					[new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)])));
		}
	}

	[TestMethod]
	public async Task Settings_and_explicit_external_tool_identity_are_validated_and_transported()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		var manifest = ValidManifest.Replace(
			"\"output\": { \"protocol\": \"ndjson-v1\" }",
			"""
			"output": { "protocol": "ndjson-v1" },
			"externalTools": [
			  { "id": "ffmpeg.ffmpeg", "displayName": "FFmpeg" }
			],
			"settings": [
			  {
			    "key": "quality",
			    "displayName": "Quality",
			    "description": "Encoding quality.",
			    "type": "enum",
			    "values": ["high", "balanced"],
			    "default": "balanced"
			  }
			]
			""",
			StringComparison.Ordinal);
		fixture.AddPackage(
			"configured",
			manifest,
			"""
			import json, sys
			request = json.load(sys.stdin)
			message = request["settings"]["quality"] + "|" + request["externalTools"][0]["path"]
			print(json.dumps({"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":message}))
			""");
		var toolPath = fixture.CopyBundledPythonAs("tools", "ffmpeg.exe");
		var stateStore = new MemoryStateStore();
		stateStore.Configure(
			ImmutableDictionary<string, AutomationSettingValue>.Empty.Add(
				"quality",
				new AutomationSettingValue(AutomationSettingValueKind.String, StringValue: "high")),
			ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty.Add(
				"ffmpeg.ffmpeg",
				new AutomationExternalToolConfiguration("ffmpeg.ffmpeg", toolPath)));
		var trust = new AcceptingTrustConsent();
		var selectedPath = fixture.AddSelectedFile("input.mov");

		var firstRun = await InvokeAsync();
		Assert.AreEqual(AutomationRunState.Succeeded, firstRun.State);
		Assert.AreEqual($"high|{toolPath}", firstRun.Logs[0].Message);
		Assert.AreEqual(1, trust.RequestCount);

		await File.AppendAllTextAsync(toolPath, "changed");
		var secondRun = await InvokeAsync();
		Assert.AreEqual(AutomationRunState.Succeeded, secondRun.State);
		Assert.AreEqual(2, trust.RequestCount, "Changed configured tool content must renew action trust.");

		async Task<AutomationRunSnapshot> InvokeAsync()
		{
			await using var session = await AutomationModule.OpenAsync(
				fixture.Options,
				stateStore,
				trust,
				new RecordingResultRouter());
			await session.InvokeAsync(new AutomationInvocation(
				new AutomationActionId("hoa.video.convert-mp4"),
				session.Snapshot.CatalogRevision,
				new HostRevision(1),
				new SelectionSnapshot(
					fixture.SelectionRoot,
					DateTimeOffset.UtcNow,
					[new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)])));
			return session.Snapshot.RecentRuns[0];
		}
	}

	[TestMethod]
	public async Task Missing_external_tool_disables_invocation_with_configure_guidance()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		var manifest = ValidManifest.Replace(
			"\"output\": { \"protocol\": \"ndjson-v1\" }",
			"\"output\": { \"protocol\": \"ndjson-v1\" }, \"externalTools\": [{ \"id\": \"ffmpeg.ffmpeg\", \"displayName\": \"FFmpeg\" }]",
			StringComparison.Ordinal);
		fixture.AddPackage("missing-tool", manifest, "pass");
		var selectedPath = fixture.AddSelectedFile("input.mov");
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options,
			new MemoryStateStore(),
			new AcceptingTrustConsent(),
			new RecordingResultRouter());

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"),
			session.Snapshot.CatalogRevision,
			new HostRevision(1),
			new SelectionSnapshot(
				fixture.SelectionRoot,
				DateTimeOffset.UtcNow,
				[new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)]))).AsTask());

		StringAssert.Contains(exception.Message, "Configure the required external tool 'FFmpeg'");
		Assert.AreEqual(AutomationActionAvailability.MissingDependency, session.Snapshot.Actions[0].Availability);
	}

	[TestMethod]
	public async Task File_state_store_preserves_configuration_when_trust_is_updated_and_records_runs()
	{
		using var fixture = AutomationFixture.Create();
		var actionsDirectory = Path.Join(fixture.Options.StateRoot, "actions");
		Directory.CreateDirectory(actionsDirectory);
		var actionStatePath = Path.Join(actionsDirectory, "hoa.video.convert-mp4.json");
		await File.WriteAllTextAsync(actionStatePath, """
			{
			  "trustedFingerprint": null,
			  "settings": { "quality": "high", "overwrite": true, "retries": 2 },
			  "externalTools": { "ffmpeg.ffmpeg": "C:\\Tools\\ffmpeg.exe" }
			}
			""");
		var store = new FileAutomationStateStore(fixture.Options.StateRoot);

		await store.WriteTrustedFingerprintAsync(new AutomationActionId("hoa.video.convert-mp4"), "trusted-hash");
		var state = await store.ReadActionStateAsync(new AutomationActionId("hoa.video.convert-mp4"));

		Assert.AreEqual("trusted-hash", state.TrustedFingerprint);
		Assert.AreEqual("high", state.Settings["quality"].StringValue);
		Assert.IsTrue(state.Settings["overwrite"].BooleanValue);
		Assert.AreEqual(2, state.Settings["retries"].IntegerValue);
		Assert.AreEqual("C:\\Tools\\ffmpeg.exe", state.ExternalTools["ffmpeg.ffmpeg"].ExecutablePath);

		var now = DateTimeOffset.UtcNow;
		var selectedPath = fixture.AddSelectedFile("recorded.mov");
		var selection = new SelectionSnapshot(fixture.SelectionRoot, now, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)]);
		var snapshot = new AutomationRunSnapshot(
			new AutomationRunId(Guid.NewGuid()),
			new AutomationActionId("hoa.video.convert-mp4"),
			AutomationRunState.Succeeded,
			selection,
			new HostRevision(1),
			now,
			now,
			75,
			"Complete",
			[new AutomationLogEntry(1, AutomationLogLevel.Information, "recorded log")],
			"recorded stderr",
			false,
			[],
			null);
		await store.AppendRunRecordAsync(new AutomationRunRecord(snapshot, "1.0.0", "trusted-hash"));

		var recordPath = Directory.GetFiles(Path.Join(fixture.Options.StateRoot, "runs"), "*.json").Single();
		var record = await File.ReadAllTextAsync(recordPath);
		StringAssert.Contains(record, "hoa.video.convert-mp4");
		StringAssert.Contains(record, "Succeeded");
		StringAssert.Contains(record, selectedPath.Replace("\\", "\\\\", StringComparison.Ordinal));
		StringAssert.Contains(record, "recorded log");
		StringAssert.Contains(record, "recorded stderr");
	}

	[TestMethod]
	public async Task Run_history_evicts_expired_records_and_stays_within_its_size_budget()
	{
		using var fixture = AutomationFixture.Create();
		var runs = Path.Join(fixture.Options.StateRoot, "runs");
		Directory.CreateDirectory(runs);
		var expired = Path.Join(runs, "000_expired.json");
		var oversized = Path.Join(runs, "999_oversized.json");
		await File.WriteAllTextAsync(expired, "expired");
		File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-8));
		await File.WriteAllTextAsync(oversized, new string('x', 900));
		var store = new FileAutomationStateStore(fixture.Options.StateRoot, TimeSpan.FromDays(7), 1024);
		var now = DateTimeOffset.UtcNow;
		var snapshot = new AutomationRunSnapshot(
			new AutomationRunId(Guid.NewGuid()), new AutomationActionId("hoa.video.convert-mp4"), AutomationRunState.Succeeded,
			new SelectionSnapshot(fixture.SelectionRoot, now, []), new HostRevision(1), now, now, null, "Complete", [], string.Empty, false, [], null);

		await store.AppendRunRecordAsync(new AutomationRunRecord(snapshot, "1.0.0", "hash"));

		Assert.IsFalse(File.Exists(expired));
		Assert.IsLessThanOrEqualTo(1024, Directory.GetFiles(runs, "*.json").Sum(path => new FileInfo(path).Length));
	}

	[TestMethod]
	public async Task Argv_paths_are_exact_and_oversized_windows_command_lines_fail_before_launch()
	{
		var manifest = ValidManifest
			.Replace("\"mode\": \"json-stdin\"", "\"mode\": \"argv-paths\"", StringComparison.Ordinal)
			.Replace("\"protocol\": \"ndjson-v1\"", "\"protocol\": \"exit-code\"", StringComparison.Ordinal);
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage("argv", manifest, "import json, sys; print(json.dumps(sys.argv[1:], ensure_ascii=False))");
		var firstPath = fixture.AddSelectedFile("space name.mov");
		var secondPath = fixture.AddSelectedFile("unicodé.mov");
		var stateStore = new MemoryStateStore();
		await using var session = await AutomationModule.OpenAsync(fixture.Options, stateStore, new AcceptingTrustConsent(), new RecordingResultRouter());

		await session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"), session.Snapshot.CatalogRevision, new HostRevision(1),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow,
				[new SelectedPath(firstPath, SelectedPathKind.File, SelectedLocationKind.Local), new SelectedPath(secondPath, SelectedPathKind.File, SelectedLocationKind.Local)])));
		var argvRun = session.Snapshot.RecentRuns[0];
		Assert.AreEqual(AutomationRunState.Succeeded, argvRun.State, argvRun.Failure);
		Assert.HasCount(1, argvRun.Logs);
		var transported = JsonSerializer.Deserialize<string[]>(argvRun.Logs[0].Message);
		CollectionAssert.AreEqual(new[] { firstPath, secondPath }, transported!);

		var oversizedPath = "C:\\" + new string('a', 30_000) + ".mov";
		await session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"), session.Snapshot.CatalogRevision, new HostRevision(2),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(oversizedPath, SelectedPathKind.File, SelectedLocationKind.Local)])));
		Assert.AreEqual(AutomationRunState.Failed, session.Snapshot.RecentRuns[0].State);
		StringAssert.Contains(session.Snapshot.RecentRuns[0].Failure!, "command-line size");
	}

	[TestMethod]
	public async Task Cancellation_during_request_transport_is_reported_as_cancelled()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage("cancel-request", ValidManifest, "pass");
		var selectedPath = fixture.AddSelectedFile("input.mov");
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"), session.Snapshot.CatalogRevision, new HostRevision(1),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)])),
			cancellation.Token);

		Assert.AreEqual(AutomationRunState.Cancelled, session.Snapshot.RecentRuns[0].State);
	}

	[TestMethod]
	public async Task Concurrency_is_single_per_action_two_global_and_never_queues()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage("one", ValidManifest, "import time; time.sleep(1)");
		fixture.AddPackage("two", ValidManifest.Replace("hoa.video.convert-mp4", "hoa.video.second", StringComparison.Ordinal), "import time; time.sleep(1)");
		fixture.AddPackage("three", ValidManifest.Replace("hoa.video.convert-mp4", "hoa.video.third", StringComparison.Ordinal), "import time; time.sleep(1)");
		var stateStore = new BlockingStateStore();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options,
			stateStore,
			new AcceptingTrustConsent(),
			new RecordingResultRouter());
		var selectedPath = fixture.AddSelectedFile("input.mov");

		AutomationInvocation Invocation(string id) => new(
			new AutomationActionId(id),
			session.Snapshot.CatalogRevision,
			new HostRevision(1),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)]));

		var first = session.InvokeAsync(Invocation("hoa.video.convert-mp4")).AsTask();
		await session.InvokeAsync(Invocation("hoa.video.convert-mp4"));
		var second = session.InvokeAsync(Invocation("hoa.video.second")).AsTask();
		await stateStore.WaitForTwoReadsAsync();

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.InvokeAsync(Invocation("hoa.video.third")).AsTask());
		StringAssert.Contains(exception.Message, "already running");
		stateStore.Release();
		await Task.WhenAll(first, second);
	}

	[TestMethod]
	public async Task Successful_result_intents_are_routed_with_the_captured_host_revision()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		var revealPath = fixture.AddSelectedFile("created.mov");
		fixture.AddPackage(
			"effects",
			ValidManifest,
			"import json\nprint(json.dumps({\"protocol\":\"ndjson-v1\",\"sequence\":1,\"type\":\"result\",\"outcome\":\"succeeded\",\"effects\":[{\"type\":\"refreshCurrentFolder\"},{\"type\":\"revealPaths\",\"paths\":[" + JsonSerializer.Serialize(revealPath) + "]}]}))");
		var router = new RecordingResultRouter();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options,
			new MemoryStateStore(),
			new AcceptingTrustConsent(),
			router);
		var selectedPath = fixture.AddSelectedFile("input.mov");

		await session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"),
			session.Snapshot.CatalogRevision,
			new HostRevision(42),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)])));

		Assert.IsNotNull(router.LastRequest);
		Assert.AreEqual(new HostRevision(42), router.LastRequest.HostRevision);
		Assert.AreEqual(fixture.SelectionRoot, router.LastRequest.CapturedFolderPath);
		Assert.HasCount(2, router.LastRequest.Intents);
		Assert.IsInstanceOfType<AutomationResultIntent.RefreshCurrentFolder>(router.LastRequest.Intents[0]);
		var reveal = (AutomationResultIntent.RevealPaths)router.LastRequest.Intents[1];
		CollectionAssert.AreEqual(new[] { revealPath }, reveal.Paths);
	}

	private static async Task<int> WaitForChildPidAsync(IAutomationBarSession session)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			var log = session.Snapshot.ActiveRuns.FirstOrDefault()?.Logs.FirstOrDefault();
			if (log is not null && int.TryParse(log.Message, out var processId))
				return processId;
			await Task.Delay(25);
		}
		throw new AssertFailedException("The child process id was not reported.");
	}

	private static bool IsProcessAlive(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			process.Refresh();
			if (!process.HasExited)
			{
				process.Kill(true);
				return true;
			}
		}
		catch (ArgumentException)
		{
		}
		return false;
	}

	private static async Task WaitForCatalogRevisionAsync(IAutomationBarSession session, long minimumRevision)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (session.Snapshot.CatalogRevision >= minimumRevision)
				return;
			await Task.Delay(10);
		}
		throw new AssertFailedException($"Expected Automation catalog revision {minimumRevision}.");
	}

	[TestMethod]
	public async Task Changed_app_local_runner_content_renews_trust()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true, copyBundledPython: true);
		fixture.AddPackage("action", ValidManifest, "pass");
		var selectedPath = fixture.AddSelectedFile("input.mov");
		var stateStore = new MemoryStateStore();
		var trust = new AcceptingTrustConsent();
		await InvokeAsync();
		Assert.AreEqual(1, trust.RequestCount);
		await File.WriteAllTextAsync(Path.Join(Path.GetDirectoryName(fixture.Options.PythonExecutablePath)!, "runner-change.marker"), "changed");
		await InvokeAsync();
		Assert.AreEqual(2, trust.RequestCount);

		async Task InvokeAsync()
		{
			await using IAutomationBarSession session = new AutomationBarSession(
				fixture.Options, AutomationManifestCatalog.Discover(fixture.Options), stateStore, trust, new RecordingResultRouter());
			await session.InvokeAsync(new AutomationInvocation(
				new AutomationActionId("hoa.video.convert-mp4"), session.Snapshot.CatalogRevision, new HostRevision(1),
				new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)])));
		}
	}

	[TestMethod]
	public async Task Invalid_package_does_not_block_an_unrelated_valid_action()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage("valid", ValidManifest, "pass");
		fixture.AddPackage("invalid", ValidManifest.Replace("hoa.video.convert-mp4", "bad", StringComparison.Ordinal), "pass");
		var selectedPath = fixture.AddSelectedFile("input.mov");
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options, new MemoryStateStore(), new AcceptingTrustConsent(), new RecordingResultRouter());

		await session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"), session.Snapshot.CatalogRevision, new HostRevision(1),
			new SelectionSnapshot(fixture.SelectionRoot, DateTimeOffset.UtcNow, [new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)])));

		Assert.AreEqual(AutomationRunState.Succeeded, session.Snapshot.RecentRuns[0].State);
		Assert.AreEqual(AutomationActionAvailability.Disabled, session.Snapshot.Actions.Single(action => action.Id.Value == "bad").Availability);
		var revision = session.Snapshot.CatalogRevision;
		fixture.UpdateScript("valid", "print('valid update')");
		await WaitForCatalogRevisionAsync(session, revision + 1);
	}

	[TestMethod]
	public async Task Python_executable_must_match_the_pinned_hash_before_launch()
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage("action", ValidManifest, "pass");
		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AutomationModule.OpenAsync(
			fixture.Options with { PythonSha256 = new string('0', 64) },
			new MemoryStateStore(),
			new AcceptingTrustConsent(),
			new RecordingResultRouter()).AsTask());
		StringAssert.Contains(exception.Message, "packaged CPython 3.14.6");
	}

	private static async Task<AutomationRunSnapshot> RunScriptAsync(string script, string? manifest = null)
	{
		using var fixture = AutomationFixture.Create(useBundledPython: true);
		fixture.AddPackage("action", manifest ?? ValidManifest, script);
		var stateStore = new MemoryStateStore();
		await using var session = await AutomationModule.OpenAsync(
			fixture.Options,
			stateStore,
			new AcceptingTrustConsent(),
			new RecordingResultRouter());
		var selectedPath = fixture.AddSelectedFile("input.mov");
		await session.InvokeAsync(new AutomationInvocation(
			new AutomationActionId("hoa.video.convert-mp4"),
			session.Snapshot.CatalogRevision,
			new HostRevision(1),
			new SelectionSnapshot(
				fixture.SelectionRoot,
				DateTimeOffset.Parse("2026-07-21T10:00:00Z"),
				[new SelectedPath(selectedPath, SelectedPathKind.File, SelectedLocationKind.Local)])));
		return session.Snapshot.RecentRuns[0];
	}

	private const string ValidManifest = """
		{
		  "schemaVersion": 1,
		  "id": "hoa.video.convert-mp4",
		  "packageVersion": "1.0.0",
		  "minimumHostVersion": "1.0.0",
		  "displayName": "Convert to MP4",
		  "description": "Converts selected videos.",
		  "entryPoint": "convert.py",
		  "python": { "requires": ">=3.14,<3.15" },
		  "input": {
		    "mode": "json-stdin",
		    "selection": {
		      "minItems": 1,
		      "maxItems": 100,
		      "itemKinds": ["file"],
		      "extensions": [".mov", ".mp4"]
		    }
		  },
		  "execution": {
		    "timeoutSeconds": 3600,
		    "maxOutputBytes": 1048576,
		    "concurrency": "single"
		  },
		  "output": { "protocol": "ndjson-v1" }
		}
		""";

	private sealed class AutomationFixture : IDisposable
	{
		private readonly string _root;

		private AutomationFixture(string root, AutomationModuleOptions options)
		{
			_root = root;
			Options = options;
		}

		public AutomationModuleOptions Options { get; }

		public string SelectionRoot => Path.Join(_root, "selection");

		public static AutomationFixture Create(bool useBundledPython = false, bool copyBundledPython = false)
		{
			var root = Path.Join(Path.GetTempPath(), "VxFiles.Automation.Tests", Guid.NewGuid().ToString("N"));
			var actions = Path.Join(root, "actions");
			var bundledRuntime = Path.Join(AppContext.BaseDirectory, "AutomationRuntime", "Python");
			var runtime = copyBundledPython ? Path.Join(root, "runtime") : bundledRuntime;
			if (copyBundledPython && Directory.Exists(bundledRuntime))
				CopyDirectory(bundledRuntime, runtime);
			var runner = Path.Join(runtime, "python.exe");
			Directory.CreateDirectory(actions);
			Directory.CreateDirectory(Path.GetDirectoryName(runner)!);
			if (!File.Exists(runner))
				throw new AssertInconclusiveException("Run eng/automation/Acquire-Python.ps1 before process acceptance tests.");
			var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(runner)));

			return new AutomationFixture(
				root,
				new AutomationModuleOptions(
					ImmutableArray.Create(actions),
					Path.Join(root, "state"),
					Path.Join(root, "temp"),
					runner,
					new Version(3, 14, 6),
					hash,
					new Version(1, 0, 0),
					"en-US"));
		}

		public void AddPackage(string folderName, string manifest, string script)
		{
			var package = Path.Join(Options.ActionRoots[0], folderName);
			Directory.CreateDirectory(package);
			File.WriteAllText(Path.Join(package, "action.json"), manifest);
			File.WriteAllText(Path.Join(package, "convert.py"), script);
		}

		public string AddSelectedFile(string name)
		{
			Directory.CreateDirectory(SelectionRoot);
			var path = Path.Join(SelectionRoot, name);
			File.WriteAllText(path, "selected");
			return path;
		}

		public void MovePackage(string sourceName, string destinationName) =>
			Directory.Move(Path.Join(Options.ActionRoots[0], sourceName), Path.Join(Options.ActionRoots[0], destinationName));

		public void UpdateScript(string packageName, string script) =>
			File.WriteAllText(Path.Join(Options.ActionRoots[0], packageName, "convert.py"), script);

		public void UpdateManifest(string packageName, string manifest) =>
			File.WriteAllText(Path.Join(Options.ActionRoots[0], packageName, "action.json"), manifest);

		public string CopyBundledPythonAs(string folderName, string fileName)
		{
			var destinationFolder = Path.Join(_root, folderName);
			Directory.CreateDirectory(destinationFolder);
			var destination = Path.Join(destinationFolder, fileName);
			File.Copy(Options.PythonExecutablePath, destination);
			return destination;
		}

		private static string FindRepositoryRoot()
		{
			var current = new DirectoryInfo(AppContext.BaseDirectory);
			while (current is not null && !File.Exists(Path.Join(current.FullName, "Files.slnx")))
				current = current.Parent;
			return current?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
		}

		private static void CopyDirectory(string source, string destination)
		{
			foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
				Directory.CreateDirectory(Path.Join(destination, Path.GetRelativePath(source, directory)));
			Directory.CreateDirectory(destination);
			foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
			{
				var target = Path.Join(destination, Path.GetRelativePath(source, file));
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(file, target);
			}
		}

		public void Dispose() => Directory.Delete(_root, true);
	}

	private sealed class MemoryStateStore : IAutomationStateStore
	{
		public AutomationActionState State { get; private set; } = new(null, [], []);

		public void Configure(
			ImmutableDictionary<string, AutomationSettingValue> settings,
			ImmutableDictionary<string, AutomationExternalToolConfiguration> externalTools) =>
			State = State with { Settings = settings, ExternalTools = externalTools };

		public ValueTask<AutomationActionState> ReadActionStateAsync(AutomationActionId actionId, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(State);

		public ValueTask WriteTrustedFingerprintAsync(AutomationActionId actionId, string fingerprint, CancellationToken cancellationToken = default)
		{
			State = State with { TrustedFingerprint = fingerprint };
			return ValueTask.CompletedTask;
		}

		public ValueTask AppendRunRecordAsync(AutomationRunRecord record, CancellationToken cancellationToken = default) =>
			ValueTask.CompletedTask;
	}

	private sealed class AcceptingTrustConsent : IAutomationTrustConsent
	{
		public int RequestCount { get; private set; }

		public ValueTask<bool> RequestTrustAsync(AutomationTrustRequest request, CancellationToken cancellationToken = default)
		{
			RequestCount++;
			return ValueTask.FromResult(true);
		}
	}

	private sealed class BlockingStateStore : IAutomationStateStore
	{
		private readonly TaskCompletionSource _twoReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _readCount;

		public async ValueTask<AutomationActionState> ReadActionStateAsync(AutomationActionId actionId, CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref _readCount) >= 2)
				_twoReads.TrySetResult();
			await _release.Task.WaitAsync(cancellationToken);
			return new AutomationActionState(null, [], []);
		}

		public ValueTask WriteTrustedFingerprintAsync(AutomationActionId actionId, string fingerprint, CancellationToken cancellationToken = default) =>
			ValueTask.CompletedTask;

		public ValueTask AppendRunRecordAsync(AutomationRunRecord record, CancellationToken cancellationToken = default) =>
			ValueTask.CompletedTask;

		public Task WaitForTwoReadsAsync() => _twoReads.Task.WaitAsync(TimeSpan.FromSeconds(10));

		public void Release() => _release.TrySetResult();
	}

	private sealed class BlockingTrustConsent : IAutomationTrustConsent
	{
		private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async ValueTask<bool> RequestTrustAsync(AutomationTrustRequest request, CancellationToken cancellationToken = default)
		{
			_requested.TrySetResult();
			return await _decision.Task.WaitAsync(cancellationToken);
		}

		public Task WaitForRequestAsync() => _requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

		public void Accept() => _decision.TrySetResult(true);
	}

	private sealed class RecordingResultRouter : IAutomationResultRouter
	{
		public AutomationResultRoutingRequest? LastRequest { get; private set; }

		public ValueTask<ImmutableArray<AutomationIntentResult>> RouteAsync(AutomationResultRoutingRequest request, CancellationToken cancellationToken = default)
		{
			LastRequest = request;
			return ValueTask.FromResult(request.Intents
				.Select(intent => new AutomationIntentResult(intent, AutomationIntentDisposition.Applied, "Applied by test host."))
				.ToImmutableArray());
		}
	}
}
