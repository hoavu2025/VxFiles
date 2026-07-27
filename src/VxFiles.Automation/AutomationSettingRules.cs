// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Typed action settings declared by a manifest.
/// </summary>
internal static partial class AutomationSettingRules
{
	private const AutomationValidationScope Scope = AutomationValidationScope.Action;

	private static readonly string[] SettingProperties =
	[
		"key",
		"displayName",
		"description",
		"type",
		"default",
		"minimum",
		"maximum",
		"minimumLength",
		"maximumLength",
		"values",
	];

	public static ImmutableArray<AutomationSettingDefinition> Validate(JsonElement action)
	{
		if (!action.TryGetProperty("settings", out var settings))
			return [];
		if (settings.ValueKind is not JsonValueKind.Array)
			throw AutomationValidationException.Action("settings must be an array.");

		var definitions = ImmutableArray.CreateBuilder<AutomationSettingDefinition>();
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in settings.EnumerateArray())
		{
			var setting = AutomationManifestReader.RequireObject(item, "settings item", Scope);
			AutomationManifestReader.ValidateProperties(setting, SettingProperties, "settings item", Scope);
			var key = AutomationManifestReader.RequireString(setting, "key", Scope);
			if (!SettingKeyRegex().IsMatch(key) || !keys.Add(key))
				throw AutomationValidationException.Action($"Setting key '{key}' is invalid or duplicated.");
			_ = AutomationManifestReader.RequireBoundedString(setting, "displayName", 1, 80, Scope);
			_ = AutomationManifestReader.RequireBoundedString(setting, "description", 1, 500, Scope);
			var type = AutomationManifestReader.RequireString(setting, "type", Scope);
			var defaultElement = AutomationManifestReader.RequireProperty(setting, "default", Scope);
			var defaultValue = ReadDefaultValue(key, type, defaultElement);

			var minimum = AutomationManifestReader.ReadOptionalNumber(setting, "minimum", Scope);
			var maximum = AutomationManifestReader.ReadOptionalNumber(setting, "maximum", Scope);
			var minimumLength = AutomationManifestReader.ReadOptionalInteger(setting, "minimumLength", Scope);
			var maximumLength = AutomationManifestReader.ReadOptionalInteger(setting, "maximumLength", Scope);
			var permittedValues = type is "enum"
				? ValidateEnumValues(setting, key, defaultValue.StringValue!)
				: ImmutableArray<string>.Empty;
			definitions.Add(new(
				key,
				type,
				defaultValue,
				minimum,
				maximum,
				minimumLength,
				maximumLength,
				permittedValues));
		}

		return definitions.ToImmutable();
	}

	private static AutomationSettingValue ReadDefaultValue(string key, string type, JsonElement defaultElement)
	{
		var value = type switch
		{
			"boolean" when defaultElement.ValueKind is JsonValueKind.True or JsonValueKind.False =>
				new AutomationSettingValue(AutomationSettingValueKind.Boolean, BooleanValue: defaultElement.GetBoolean()),
			"integer" when defaultElement.TryGetInt64(out var integer) =>
				new AutomationSettingValue(AutomationSettingValueKind.Integer, IntegerValue: integer),
			"number" when defaultElement.TryGetDouble(out var number) && double.IsFinite(number) =>
				new AutomationSettingValue(AutomationSettingValueKind.Number, NumberValue: number),
			"string" or "filePath" or "folderPath" or "enum" when defaultElement.ValueKind is JsonValueKind.String =>
				new AutomationSettingValue(AutomationSettingValueKind.String, StringValue: defaultElement.GetString()),
			_ => null,
		};
		return value ?? throw AutomationValidationException.Action($"Setting '{key}' has an invalid type or default value.");
	}

	private static ImmutableArray<string> ValidateEnumValues(JsonElement setting, string key, string defaultValue)
	{
		if (!setting.TryGetProperty("values", out var values) || values.ValueKind is not JsonValueKind.Array)
			throw AutomationValidationException.Action($"Enum setting '{key}' requires a values array.");
		var permittedValues = values.EnumerateArray()
			.Select(value => value.ValueKind is JsonValueKind.String
				? value.GetString()!
				: throw AutomationValidationException.Action($"Enum setting '{key}' values must be strings."))
			.ToImmutableArray();
		if (permittedValues.Length is 0 ||
			permittedValues.Distinct(StringComparer.Ordinal).Count() != permittedValues.Length ||
			!permittedValues.Contains(defaultValue, StringComparer.Ordinal))
		{
			throw AutomationValidationException.Action($"Enum setting '{key}' values are invalid.");
		}
		return permittedValues;
	}

	[GeneratedRegex(@"^[a-z][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
	private static partial Regex SettingKeyRegex();
}
