// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

		var path = Path.GetFullPath(configuration.ExecutablePath);
		if (!Path.IsPathFullyQualified(configuration.ExecutablePath) ||
			!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) ||
			!File.Exists(path) ||
			(File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
		{
			throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' with an absolute ordinary executable path.");
		}

		var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
		if (definition.MinimumFileVersion is not null &&
			Version.TryParse(definition.MinimumFileVersion, out var minimum) &&
			Version.TryParse(version, out var actual) && actual < minimum)
		{
			throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' version {definition.MinimumFileVersion} or later.");
		}

		return new(
			definition.Id,
			path,
			$"sha256:{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}",
			version,
			GetSignatureStatus(path));
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

	private static string GetSignatureStatus(string path)
	{
		try
		{
			using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
			return certificate is null ? "unsigned" : "signed";
		}
		catch (CryptographicException)
		{
			return "unsigned";
		}
	}
}
