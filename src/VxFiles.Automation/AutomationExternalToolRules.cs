// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// External executables declared once per Automation Package and referenced by its actions.
/// </summary>
internal static class AutomationExternalToolRules
{
	private static readonly string[] ToolProperties =
	[
		"id",
		"displayName",
		"homepage",
		"minimumFileVersion",
	];

	public static ImmutableArray<AutomationExternalToolDefinition> ValidatePackageTools(JsonElement root)
	{
		if (!root.TryGetProperty("externalTools", out var externalTools))
			return [];
		if (externalTools.ValueKind is not JsonValueKind.Array)
			throw AutomationValidationException.Package("externalTools must be an array.");

		const AutomationValidationScope scope = AutomationValidationScope.Package;
		var definitions = ImmutableArray.CreateBuilder<AutomationExternalToolDefinition>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var externalTool in externalTools.EnumerateArray())
		{
			var tool = AutomationManifestReader.RequireObject(externalTool, "externalTools item", scope);
			AutomationManifestReader.ValidateProperties(tool, ToolProperties, "externalTools item", scope);
			var id = AutomationManifestReader.RequireString(tool, "id", scope);
			if (!AutomationPackageId.TryParse(id, out _))
				throw AutomationValidationException.Package($"External-tool id '{id}' is invalid.");
			if (!ids.Add(id))
				throw AutomationValidationException.Package($"Duplicate external-tool id '{id}'.");
			var displayName = AutomationManifestReader.RequireBoundedString(tool, "displayName", 1, 80, scope);
			if (tool.TryGetProperty("homepage", out var homepage))
			{
				if (homepage.ValueKind is not JsonValueKind.String ||
					!Uri.TryCreate(homepage.GetString(), UriKind.Absolute, out _))
				{
					throw AutomationValidationException.Package($"External tool '{id}' has an invalid homepage.");
				}
			}

			string? minimumFileVersion = null;
			if (tool.TryGetProperty("minimumFileVersion", out var minimumVersion))
			{
				if (minimumVersion.ValueKind is not JsonValueKind.String)
					throw AutomationValidationException.Package($"External tool '{id}' minimumFileVersion must be a string.");
				minimumFileVersion = minimumVersion.GetString();
			}

			definitions.Add(new(id, displayName, minimumFileVersion));
		}

		return definitions.ToImmutable();
	}

	public static ImmutableArray<string> ValidateActionReferences(
		JsonElement action,
		ImmutableArray<AutomationExternalToolDefinition> packageExternalTools)
	{
		if (!action.TryGetProperty("externalTools", out var externalTools))
			return [];
		if (externalTools.ValueKind is not JsonValueKind.Array)
			throw AutomationValidationException.Action("action.externalTools must be an array.");

		var references = ImmutableArray.CreateBuilder<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in externalTools.EnumerateArray())
		{
			if (item.ValueKind is not JsonValueKind.String)
				throw AutomationValidationException.Action("action.externalTools must contain strings.");
			var id = item.GetString()!;
			if (!seen.Add(id))
				throw AutomationValidationException.Action($"Duplicate action external-tool reference '{id}'.");
			if (!packageExternalTools.Any(tool => string.Equals(tool.Id, id, StringComparison.Ordinal)))
				throw AutomationValidationException.Action($"Action references unknown external tool '{id}'.");
			references.Add(id);
		}

		return references.ToImmutable();
	}
}
