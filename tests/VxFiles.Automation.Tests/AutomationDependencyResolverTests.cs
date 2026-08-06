// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// External-tool identity. The tool is held to its hash: the executable is never run, so nothing else about it
/// can be established, and nothing else reaches the trust fingerprint.
/// </summary>
[TestClass]
public sealed class AutomationDependencyResolverTests
{
	[TestMethod]
	public void Resolve_accepts_a_tool_reached_through_a_link_and_reports_its_target()
	{
		using var tools = new ToolFolder();
		var target = tools.AddExecutable("ffmpeg.exe");
		var shim = tools.AddLink("shim.exe", target);

		var resolved = Resolve(Configure(shim));

		// The path handed to the action is the target, not the shim: this is how a winget or scoop install
		// stays configured across an upgrade that re-points its shim.
		Assert.AreEqual(target, resolved.ExecutablePath);
	}

	[TestMethod]
	public void Resolve_hashes_the_link_target_and_not_the_link()
	{
		using var tools = new ToolFolder();
		var target = tools.AddExecutable("ffmpeg.exe");
		var shim = tools.AddLink("shim.exe", target);

		Assert.AreEqual(Resolve(Configure(target)).Fingerprint, Resolve(Configure(shim)).Fingerprint);
	}

	[TestMethod]
	public void Resolve_rejects_a_link_whose_target_is_gone()
	{
		using var tools = new ToolFolder();
		var target = tools.AddExecutable("ffmpeg.exe");
		var shim = tools.AddLink("shim.exe", target);
		File.Delete(target);

		var exception = Assert.ThrowsExactly<AutomationMissingDependencyException>(() => Resolve(Configure(shim)));
		StringAssert.Contains(exception.Message, "FFmpeg");
	}

	[TestMethod]
	public void Resolve_rejects_a_link_to_something_that_is_not_a_program()
	{
		using var tools = new ToolFolder();
		var payload = tools.AddExecutable("payload.dll");
		var shim = tools.AddLink("ffmpeg.exe", payload);

		var exception = Assert.ThrowsExactly<AutomationMissingDependencyException>(() => Resolve(Configure(shim)));
		StringAssert.Contains(exception.Message, "payload.dll");
	}

	[TestMethod]
	public void Resolve_rejects_a_directory()
	{
		using var tools = new ToolFolder();
		var folder = Path.Join(tools.Path, "ffmpeg.exe");
		Directory.CreateDirectory(folder);

		Assert.ThrowsExactly<AutomationMissingDependencyException>(() => Resolve(Configure(folder)));
	}

	[TestMethod]
	public void Resolve_rejects_a_relative_path()
	{
		using var tools = new ToolFolder();
		tools.AddExecutable("ffmpeg.exe");

		Assert.ThrowsExactly<AutomationMissingDependencyException>(() => Resolve(Configure("ffmpeg.exe")));
	}

	[TestMethod]
	public void Resolve_rejects_a_tool_that_reports_no_file_version_when_one_is_required()
	{
		using var tools = new ToolFolder();
		var path = tools.AddExecutable("ffmpeg.exe");

		// FFmpeg's own Windows builds are exactly this case: no version resource at all.
		var exception = Assert.ThrowsExactly<AutomationMissingDependencyException>(
			() => Resolve(Configure(path), minimumFileVersion: "7.1"));

		StringAssert.Contains(exception.Message, "FFmpeg");
		StringAssert.Contains(exception.Message, "7.1");
	}

	[TestMethod]
	public void Resolve_rejects_a_declared_minimum_that_is_not_a_version()
	{
		using var tools = new ToolFolder();
		var path = tools.AddExecutable("ffmpeg.exe");

		// The manifest reader accepts any string here, so the resolver is where an unusable floor is caught.
		// It must surface as guidance, not as the FormatException an unguarded Version.Parse would throw.
		var exception = Assert.ThrowsExactly<AutomationMissingDependencyException>(
			() => Resolve(Configure(path), minimumFileVersion: "n7.1-full_build"));

		StringAssert.Contains(exception.Message, "FFmpeg");
		StringAssert.Contains(exception.Message, "n7.1-full_build");
	}

	[TestMethod]
	public void Resolve_accepts_a_tool_with_no_file_version_when_none_is_required()
	{
		using var tools = new ToolFolder();
		var path = tools.AddExecutable("ffmpeg.exe");

		Assert.AreEqual(path, Resolve(Configure(path)).ExecutablePath);
	}

	private static AutomationExternalToolIdentity Resolve(
		AutomationPackageState packageState,
		string? minimumFileVersion = null)
	{
		var toolDefinition = new AutomationExternalToolDefinition("hoa.ffmpeg", "FFmpeg", minimumFileVersion);
		var action = new AutomationActionDefinition(
			AutomationFixture.ParseActionId("hoa.media/convert"),
			"convert.py",
			AutomationInputMode.JsonStdin,
			new AutomationSelectionPolicy(1, 100, ["file"], []),
			3600,
			1_048_576,
			AutomationOutputProtocol.NdjsonV1,
			["hoa.ffmpeg"],
			[]);
		var package = new AutomationPackageDefinition(
			AutomationPackageId.Parse("hoa.media"),
			"C:\\packages\\media",
			"1.0.0",
			"Media tools",
			Encoding.UTF8.GetBytes("{}"),
			[toolDefinition],
			ImmutableDictionary<AutomationActionLocalId, AutomationActionDefinition>.Empty.Add(action.Id.LocalId, action));

		return AutomationDependencyResolver
			.Resolve(package, action, packageState, new(ImmutableDictionary<string, AutomationSettingValue>.Empty))
			.ExternalTools
			.Single();
	}

	private static AutomationPackageState Configure(string executablePath)
		=> new(
			null,
			ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty
				.Add("hoa.ffmpeg", new("hoa.ffmpeg", executablePath)));

	private sealed class ToolFolder : IDisposable
	{
		public string Path { get; } = System.IO.Path.Join(
			System.IO.Path.GetTempPath(),
			$"VxFiles-Automation-Tools-{Guid.NewGuid():N}");

		public ToolFolder()
			=> Directory.CreateDirectory(Path);

		public string AddExecutable(string name)
		{
			var path = System.IO.Path.Join(Path, name);
			File.WriteAllBytes(path, Encoding.UTF8.GetBytes(name));
			return path;
		}

		public string AddLink(string name, string target)
			=> TestLinks.Create(System.IO.Path.Join(Path, name), target);

		public void Dispose()
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, true);
		}
	}
}
