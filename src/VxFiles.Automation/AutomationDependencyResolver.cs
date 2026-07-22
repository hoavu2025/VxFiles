// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record ResolvedAutomationDependencies(
	ImmutableDictionary<string, AutomationSettingValue> Settings,
	ImmutableArray<AutomationExternalToolIdentity> ExternalTools);

internal static class AutomationDependencyResolver
{
	public static ResolvedAutomationDependencies Resolve(
		ValidatedAutomationManifest manifest,
		AutomationActionState state)
	{
		var settings = ImmutableDictionary.CreateBuilder<string, AutomationSettingValue>(StringComparer.Ordinal);
		foreach (var definition in manifest.Settings)
		{
			var value = state.Settings.GetValueOrDefault(definition.Key, definition.DefaultValue);
			ValidateSetting(definition, value);
			settings.Add(definition.Key, value);
		}

		var tools = ImmutableArray.CreateBuilder<AutomationExternalToolIdentity>();
		foreach (var definition in manifest.ExternalTools)
		{
			if (!state.ExternalTools.TryGetValue(definition.Id, out var configuration))
				throw new InvalidOperationException($"Configure the required external tool '{definition.DisplayName}'.");
			var path = Path.GetFullPath(configuration.ExecutablePath);
			if (!Path.IsPathFullyQualified(configuration.ExecutablePath) ||
				!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) ||
				!File.Exists(path) ||
				(File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
			{
				throw new InvalidOperationException($"Configure '{definition.DisplayName}' with an absolute ordinary executable path.");
			}

			var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
			if (definition.MinimumFileVersion is not null &&
				Version.TryParse(definition.MinimumFileVersion, out var minimum) &&
				Version.TryParse(version, out var actual) && actual < minimum)
			{
				throw new InvalidOperationException($"Configure '{definition.DisplayName}' version {definition.MinimumFileVersion} or later.");
			}

			var fingerprint = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}";
			tools.Add(new AutomationExternalToolIdentity(
				definition.Id,
				path,
				fingerprint,
				version,
				GetSignatureStatus(path)));
		}

		return new ResolvedAutomationDependencies(settings.ToImmutable(), tools.ToImmutable());
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
