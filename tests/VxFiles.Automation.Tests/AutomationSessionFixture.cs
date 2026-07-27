// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// A disposable Automation Package root, state root, and selection folder wired to the app-local pinned
/// Python runtime. Tests that need a real process report inconclusive when the runtime was never acquired.
/// </summary>
internal sealed class AutomationFixture : IDisposable
{
	private readonly string _root;

	private AutomationFixture(string root, AutomationModuleOptions options)
	{
		_root = root;
		Options = options;
	}

	public AutomationModuleOptions Options { get; }

	public string PackageRoot => Options.PackageRoots[0];

	public string SelectionRoot => Path.Join(_root, "selection");

	/// <summary>
	/// The Automation Packages folder shipped beside the app. Actions here are the ones a clean install can
	/// run before the user has added anything.
	/// </summary>
	public static string BundledPackageRoot
		=> Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "AutomationPackages"));

	/// <param name="copyRuntime">
	/// Copies the pinned runtime into the fixture so a test can mutate it without touching the shared
	/// app-local copy. Sessions must then be constructed directly, because the pinned-location check rejects it.
	/// </param>
	public static AutomationFixture Create(bool copyRuntime = false)
	{
		var pinnedRuntime = Path.GetDirectoryName(PinnedAutomationPython.ExecutablePath)!;
		if (!File.Exists(PinnedAutomationPython.ExecutablePath))
		{
			const string message = "The pinned runtime is missing. Run scripts\\automation\\Acquire-Python.ps1.";

			// The tracer sets this so a missing runtime fails the run instead of quietly skipping every
			// real-process test and still reporting success.
			throw Environment.GetEnvironmentVariable("VXFILES_AUTOMATION_REQUIRE_RUNTIME") is "1"
				? new AssertFailedException(message)
				: new AssertInconclusiveException(message);
		}

		var root = Path.Join(Path.GetTempPath(), "VxFiles.Automation.Tests", Guid.NewGuid().ToString("N"));
		var packages = Path.Join(root, "packages");
		Directory.CreateDirectory(packages);

		var runtime = pinnedRuntime;
		if (copyRuntime)
		{
			runtime = Path.Join(root, "runtime");
			CopyDirectory(pinnedRuntime, runtime);
		}

		var executable = Path.Join(runtime, "python.exe");
		return new(
			root,
			new(
				[packages],
				Path.Join(root, "state"),
				Path.Join(root, "temp"),
				executable,
				PinnedAutomationPython.Version,
				Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))),
				new Version(2, 1, 0),
				"en-US"));
	}

	/// <summary>
	/// A fixture whose catalog is the bundled package folder rather than a temporary one, so tests exercise
	/// the manifests that actually ship.
	/// </summary>
	public static AutomationFixture CreateForBundledPackages()
	{
		var fixture = Create();
		return new(fixture._root, fixture.Options with { PackageRoots = [BundledPackageRoot] });
	}

	public string AddPackage(string folderName, string manifest, params (string Name, string Content)[] files)
	{
		var packagePath = Path.Join(PackageRoot, folderName);
		Directory.CreateDirectory(packagePath);
		WriteUtf8(Path.Join(packagePath, "vxpackage.json"), manifest);
		foreach (var (name, content) in files)
			WriteUtf8(Path.Join(packagePath, name), content);
		return packagePath;
	}

	public void UpdateFile(string folderName, string name, string content)
		=> WriteUtf8(Path.Join(PackageRoot, folderName, name), content);

	public string AddSelectedFile(string name)
	{
		Directory.CreateDirectory(SelectionRoot);
		var path = Path.Join(SelectionRoot, name);
		WriteUtf8(path, "selected");
		return path;
	}

	public string RuntimeDirectory => Path.GetDirectoryName(Options.PythonExecutablePath)!;

	public AutomationInvocation Invocation(
		IAutomationSession session,
		string actionId,
		long hostRevision = 1,
		params string[] selectedPaths)
	{
		var items = selectedPaths.Length is 0
			? [AddSelectedFile("input.mov")]
			: selectedPaths;
		return new(
			ParseActionId(actionId),
			session.Snapshot.CatalogRevision,
			new(hostRevision),
			new(
				SelectionRoot,
				DateTimeOffset.Parse("2026-07-21T10:00:00Z"),
				[.. items.Select(path => new SelectedPath(path, SelectedPathKind.File, SelectedLocationKind.Local))]));
	}

	public static AutomationActionId ParseActionId(string value)
	{
		var separator = value.LastIndexOf('/');
		return new(
			AutomationPackageId.Parse(value[..separator]),
			AutomationActionLocalId.Parse(value[(separator + 1)..]));
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_root, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// A killed process tree can briefly hold a handle; the temp root is reclaimed later.
		}
	}

	private static void WriteUtf8(string path, string content)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content, new UTF8Encoding(false));
	}

	private static void CopyDirectory(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(Path.Join(destination, Path.GetRelativePath(source, directory)));
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			var target = Path.Join(destination, Path.GetRelativePath(source, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target);
		}
	}
}

/// <summary>
/// Builds <c>vxpackage.json</c> text for multi-action packages.
/// </summary>
internal static class AutomationManifests
{
	public const string DefaultPackageId = "hoa.media";

	public static string Package(string packageId, params string[] actions)
		=> $$"""
		{
		  "schemaVersion": 1,
		  "id": "{{packageId}}",
		  "packageVersion": "1.0.0",
		  "displayName": "Media tools",
		  "description": "Media utilities",
		  "author": "Hoa",
		  "minimumHostVersion": "2.1.0",
		  "python": { "requires": ">=3.14,<3.15" },
		  "actions": [
		    {{string.Join("," + Environment.NewLine + "    ", actions)}}
		  ]
		}
		""";

	public static string Action(
		string id,
		string entryPoint,
		string inputMode = "json-stdin",
		string outputProtocol = "ndjson-v1",
		int timeoutSeconds = 3600,
		int maximumOutputBytes = 1_048_576,
		string extraProperties = "")
		=> $$"""
		{
		  "id": "{{id}}",
		  "displayName": "Action {{id}}",
		  "description": "Action {{id}} description",
		  "entryPoint": "{{entryPoint}}",{{extraProperties}}
		  "input": {
		    "mode": "{{inputMode}}",
		    "selection": { "minItems": 1, "maxItems": 100, "itemKinds": ["file"], "extensions": [] }
		  },
		  "execution": { "timeoutSeconds": {{timeoutSeconds}}, "maxOutputBytes": {{maximumOutputBytes}} },
		  "output": { "protocol": "{{outputProtocol}}" }
		}
		""";
}

internal sealed class MemoryStateStore : IAutomationStateStore
{
	private readonly Dictionary<string, AutomationPackageState> _packages = [];
	private readonly Dictionary<string, AutomationActionSettings> _actions = [];

	public List<AutomationRunRecord> Records { get; } = [];

	public void ConfigureExternalTools(
		string packageId,
		ImmutableDictionary<string, AutomationExternalToolConfiguration> externalTools)
		=> _packages[packageId] = ReadPackage(packageId) with { ExternalTools = externalTools };

	public void ConfigureSettings(string actionId, ImmutableDictionary<string, AutomationSettingValue> settings)
		=> _actions[actionId] = new(settings);

	public string? TrustedFingerprintFor(string packageId) => ReadPackage(packageId).TrustedFingerprint;

	public ValueTask<AutomationPackageState> ReadPackageStateAsync(
		AutomationPackageId packageId,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(ReadPackage(packageId.Value));

	public ValueTask WritePackageTrustAsync(
		AutomationPackageId packageId,
		string fingerprint,
		CancellationToken cancellationToken = default)
	{
		_packages[packageId.Value] = ReadPackage(packageId.Value) with { TrustedFingerprint = fingerprint };
		return ValueTask.CompletedTask;
	}

	public ValueTask<AutomationActionSettings> ReadActionSettingsAsync(
		AutomationActionId actionId,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(_actions.TryGetValue(actionId.Value, out var settings)
			? settings
			: new AutomationActionSettings(ImmutableDictionary<string, AutomationSettingValue>.Empty));

	public ValueTask AppendRunRecordAsync(AutomationRunRecord record, CancellationToken cancellationToken = default)
	{
		Records.Add(record);
		return ValueTask.CompletedTask;
	}

	private AutomationPackageState ReadPackage(string packageId)
		=> _packages.TryGetValue(packageId, out var state)
			? state
			: new(null, ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty);
}

internal sealed class AcceptingTrustConsent : IAutomationTrustConsent
{
	public List<AutomationTrustRequest> Requests { get; } = [];

	public int RequestCount => Requests.Count;

	public ValueTask<bool> RequestTrustAsync(AutomationTrustRequest request, CancellationToken cancellationToken = default)
	{
		Requests.Add(request);
		return ValueTask.FromResult(true);
	}
}

internal sealed class DecliningTrustConsent : IAutomationTrustConsent
{
	public int RequestCount { get; private set; }

	public ValueTask<bool> RequestTrustAsync(AutomationTrustRequest request, CancellationToken cancellationToken = default)
	{
		RequestCount++;
		return ValueTask.FromResult(false);
	}
}

internal sealed class BlockingStateStore : IAutomationStateStore
{
	private readonly TaskCompletionSource _twoReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _readCount;

	public async ValueTask<AutomationPackageState> ReadPackageStateAsync(
		AutomationPackageId packageId,
		CancellationToken cancellationToken = default)
	{
		if (Interlocked.Increment(ref _readCount) >= 2)
			_twoReads.TrySetResult();
		await _release.Task.WaitAsync(cancellationToken);
		return new(null, ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty);
	}

	public ValueTask WritePackageTrustAsync(
		AutomationPackageId packageId,
		string fingerprint,
		CancellationToken cancellationToken = default)
		=> ValueTask.CompletedTask;

	public ValueTask<AutomationActionSettings> ReadActionSettingsAsync(
		AutomationActionId actionId,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(new AutomationActionSettings(ImmutableDictionary<string, AutomationSettingValue>.Empty));

	public ValueTask AppendRunRecordAsync(AutomationRunRecord record, CancellationToken cancellationToken = default)
		=> ValueTask.CompletedTask;

	public Task WaitForTwoReadsAsync() => _twoReads.Task.WaitAsync(TimeSpan.FromSeconds(10));

	public void Release() => _release.TrySetResult();
}

internal sealed class RecordingResultRouter : IAutomationResultRouter
{
	public AutomationResultRoutingRequest? LastRequest { get; private set; }

	public ValueTask<ImmutableArray<AutomationIntentResult>> RouteAsync(
		AutomationResultRoutingRequest request,
		CancellationToken cancellationToken = default)
	{
		LastRequest = request;
		return ValueTask.FromResult(request.Intents
			.Select(intent => new AutomationIntentResult(intent, AutomationIntentDisposition.Applied, "Applied by test host."))
			.ToImmutableArray());
	}
}
