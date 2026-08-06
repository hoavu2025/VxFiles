// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record ResolvedAutomationDependencies(
	ImmutableDictionary<string, AutomationSettingValue> Settings,
	ImmutableArray<AutomationExternalToolIdentity> ExternalTools);

/// <summary>
/// A declared external tool is absent or unusable, so the package needs configuration before any run.
/// </summary>
internal sealed class AutomationMissingDependencyException(string message) : InvalidOperationException(message);

/// <summary>
/// Binds an action's declared settings and its package's external tools to the persisted configuration,
/// producing the concrete identities transported to the action and mixed into the trust fingerprint.
/// </summary>
internal static class AutomationDependencyResolver
{
	public static ResolvedAutomationDependencies Resolve(
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationPackageState packageState,
		AutomationActionSettings actionSettings)
	{
		var settings = ImmutableDictionary.CreateBuilder<string, AutomationSettingValue>(StringComparer.Ordinal);
		foreach (var definition in action.Settings)
		{
			var value = actionSettings.Values.GetValueOrDefault(definition.Key, definition.DefaultValue);
			ValidateSetting(definition, value);
			settings.Add(definition.Key, value);
		}

		var tools = ImmutableArray.CreateBuilder<AutomationExternalToolIdentity>();
		foreach (var toolId in action.ExternalToolIds)
		{
			var definition = package.ExternalTools.First(tool => string.Equals(tool.Id, toolId, StringComparison.Ordinal));
			tools.Add(ResolveExternalTool(definition, packageState));
		}

		return new(settings.ToImmutable(), tools.ToImmutable());
	}

	private static AutomationExternalToolIdentity ResolveExternalTool(
		AutomationExternalToolDefinition definition,
		AutomationPackageState packageState)
	{
		if (!packageState.ExternalTools.TryGetValue(definition.Id, out var configuration))
			throw new AutomationMissingDependencyException($"Configure the required external tool '{definition.DisplayName}'.");

		// File.Exists is false for a directory, which is one of the two checks the old attribute test spelled
		// out. What it no longer carries is the refusal of links themselves.
		var configuredPath = Path.GetFullPath(configuration.ExecutablePath);
		if (!Path.IsPathFullyQualified(configuration.ExecutablePath) ||
			!HasExecutableExtension(configuredPath) ||
			!File.Exists(configuredPath))
		{
			throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' with an absolute ordinary executable path.");
		}

		// Everything above was asked of the link. Ask it again of the target, because neither guard survives the
		// hop: File.Exists reports a dangling link as present, and ResolveLinkTarget hands back a target that is
		// not there rather than throwing. A link may stand in for a program; it may not stand in for nothing, nor
		// smuggle in something that is not a program.
		var path = ResolveLinkTarget(configuredPath, definition.DisplayName);
		if (!File.Exists(path))
			throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}': its path leads to '{path}', which is not there. Reinstall it or configure the new location.");
		if (!HasExecutableExtension(path))
			throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' with a path that leads to a program; this one leads to '{Path.GetFileName(path)}'.");

		var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
		if (definition.MinimumFileVersion is not null)
		{
			// Fails closed on both sides. FFmpeg's own Windows builds carry no version resource at all, and
			// reading that silence as a pass handed the manifest author a guarantee nothing ever enforced. The
			// declared floor is never parsed when the manifest is read, so it can be unusable in the same way.
			if (!Version.TryParse(definition.MinimumFileVersion, out var minimum))
				throw new AutomationMissingDependencyException($"'{definition.DisplayName}' cannot be used: its package requires version '{definition.MinimumFileVersion}', which is not a version number.");
			if (!Version.TryParse(version, out var actual))
				throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' with a build that reports its file version; version {definition.MinimumFileVersion} or later is required.");
			if (actual < minimum)
				throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' version {definition.MinimumFileVersion} or later.");
		}

		return new(
			definition.Id,
			path,
			$"sha256:{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}",
			version);
	}

	private static bool HasExecutableExtension(string path)
		=> string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Holds a link's target rather than the link, resolved on every run.
	/// </summary>
	/// <remarks>
	/// winget and scoop put their executables behind a shim, which is how most people have FFmpeg, and refusing
	/// the link protected nothing: reads follow it, so the hash was always the target's. Resolving per run rather
	/// than once at configuration time is what keeps such an install configured across an upgrade that re-points
	/// the shim. Package and runtime trees are held to the opposite rule — see <see cref="AutomationTrustFingerprint"/>
	/// — because those are content VxFiles hashes wholesale, not one file the user chose.
	/// </remarks>
	private static string ResolveLinkTarget(string path, string displayName)
	{
		try
		{
			// Null means the path was never a link; returnFinalTarget walks a chain rather than one hop.
			return File.ResolveLinkTarget(path, returnFinalTarget: true) is { } target
				? Path.GetFullPath(target.FullName)
				: path;
		}
		catch (IOException exception)
		{
			throw new AutomationMissingDependencyException($"Configure '{displayName}' with an executable path that resolves: {exception.Message}");
		}
	}

	private static void ValidateSetting(AutomationSettingDefinition definition, AutomationSettingValue value)
	{
		var valid = definition.Type switch
		{
			"boolean" => value.Kind is AutomationSettingValueKind.Boolean,
			"integer" => value.Kind is AutomationSettingValueKind.Integer &&
				(definition.Minimum is null || value.IntegerValue >= definition.Minimum) &&
				(definition.Maximum is null || value.IntegerValue <= definition.Maximum),
			"number" => value.Kind is AutomationSettingValueKind.Number && double.IsFinite(value.NumberValue) &&
				(definition.Minimum is null || value.NumberValue >= definition.Minimum) &&
				(definition.Maximum is null || value.NumberValue <= definition.Maximum),
			"string" or "filePath" or "folderPath" => value.Kind is AutomationSettingValueKind.String && value.StringValue is not null &&
				(definition.MinimumLength is null || value.StringValue.Length >= definition.MinimumLength) &&
				(definition.MaximumLength is null || value.StringValue.Length <= definition.MaximumLength),
			"enum" => value.Kind is AutomationSettingValueKind.String && value.StringValue is not null &&
				definition.Values.Contains(value.StringValue, StringComparer.Ordinal),
			_ => false,
		};
		if (!valid)
			throw new InvalidOperationException($"Configured setting '{definition.Key}' is invalid; configure the action again.");
	}
}
