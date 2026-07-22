// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal enum AutomationInputMode
{
	JsonStdin,
	ArgvPaths,
}

internal enum AutomationOutputProtocol
{
	NdjsonV1,
	ExitCode,
}

internal sealed record AutomationSelectionPolicy(
	int MinItems,
	int MaxItems,
	ImmutableArray<string> ItemKinds,
	ImmutableArray<string> Extensions);

internal sealed record AutomationExternalToolDefinition(
	string Id,
	string DisplayName,
	string? MinimumFileVersion);

internal sealed record AutomationSettingDefinition(
	string Key,
	string Type,
	AutomationSettingValue DefaultValue,
	double? Minimum,
	double? Maximum,
	int? MinimumLength,
	int? MaximumLength,
	ImmutableArray<string> Values);

internal sealed record ValidatedAutomationManifest(
	string Id,
	string PackageVersion,
	string DisplayName,
	string Description,
	string EntryPointPath,
	AutomationInputMode InputMode,
	AutomationSelectionPolicy Selection,
	int TimeoutSeconds,
	int MaxOutputBytes,
	AutomationOutputProtocol OutputProtocol,
	ImmutableArray<AutomationExternalToolDefinition> ExternalTools,
	ImmutableArray<AutomationSettingDefinition> Settings,
	byte[] ManifestBytes);

internal sealed record ManifestValidationResult(
	ValidatedAutomationManifest? Manifest,
	string Id,
	string DisplayName,
	string Description,
	ImmutableArray<string> Diagnostics);

internal static partial class AutomationManifestValidator
{
	private const int MaximumManifestBytes = 262_144;

	private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
	{
		"schemaVersion", "id", "packageVersion", "minimumHostVersion", "displayName", "description",
		"author", "homepage", "icon", "entryPoint", "python", "input", "execution", "output",
		"externalTools", "settings",
	};

	public static ManifestValidationResult Validate(
		string packagePath,
		byte[] manifestBytes,
		AutomationModuleOptions options)
	{
		var fallbackId = $"invalid.{Path.GetFileName(packagePath).ToLowerInvariant()}";
		var fallbackName = Path.GetFileName(packagePath);
		try
		{
			if (manifestBytes.Length > MaximumManifestBytes)
				throw Invalid($"action.json exceeds {MaximumManifestBytes} bytes.");

			_ = new UTF8Encoding(false, true).GetString(manifestBytes);
			using var document = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
			});

			var root = RequireObject(document.RootElement, "action.json");
			var duplicate = FindDuplicateProperty(root);
			if (duplicate is not null)
				throw Invalid($"Duplicate property '{duplicate}' in action.json.");
			ValidateProperties(root, RootProperties, "action.json");

			var schemaVersion = RequireInt32(root, "schemaVersion");
			if (schemaVersion != 1)
				throw Invalid("schemaVersion must be 1.");

			var id = RequireString(root, "id");
			fallbackId = id;
			if (id.Length is < 3 or > 128 || !QualifiedIdRegex().IsMatch(id))
				throw Invalid("id must be publisher-qualified lowercase text between 3 and 128 characters.");

			var packageVersion = RequireString(root, "packageVersion");
			if (!SemVerRegex().IsMatch(packageVersion))
				throw Invalid("packageVersion must be SemVer 2.0.");

			var minimumHostVersionText = RequireString(root, "minimumHostVersion");
			if (!SemVerRegex().IsMatch(minimumHostVersionText) ||
				!Version.TryParse(minimumHostVersionText.Split('-', '+')[0], out var minimumHostVersion))
			{
				throw Invalid("minimumHostVersion must be SemVer 2.0.");
			}
			if (minimumHostVersion > options.HostVersion)
				throw Invalid($"Action requires host version {minimumHostVersionText} or later.");

			var displayName = RequireBoundedText(root, "displayName", 1, 80);
			fallbackName = displayName;
			var description = RequireBoundedText(root, "description", 1, 500);
			var entryPoint = RequireString(root, "entryPoint");
			var entryPointPath = ResolveEntryPoint(packagePath, entryPoint);

			var python = RequireObject(RequireProperty(root, "python"), "python");
			ValidateProperties(python, ["requires"], "python");
			ValidatePythonRange(RequireString(python, "requires"), options.PythonVersion);

			var input = RequireObject(RequireProperty(root, "input"), "input");
			ValidateProperties(input, ["mode", "selection"], "input");
			var inputMode = RequireString(input, "mode") switch
			{
				"json-stdin" => AutomationInputMode.JsonStdin,
				"argv-paths" => AutomationInputMode.ArgvPaths,
				_ => throw Invalid("input.mode must be 'json-stdin' or 'argv-paths'."),
			};

			var selection = ValidateSelection(RequireObject(RequireProperty(input, "selection"), "input.selection"));
			var execution = RequireObject(RequireProperty(root, "execution"), "execution");
			ValidateProperties(execution, ["timeoutSeconds", "maxOutputBytes", "concurrency"], "execution");
			var timeoutSeconds = RequireInt32(execution, "timeoutSeconds");
			if (timeoutSeconds is < 1 or > 86_400)
				throw Invalid("timeoutSeconds must be between 1 and 86400.");
			var maxOutputBytes = RequireInt32(execution, "maxOutputBytes");
			if (maxOutputBytes is < 65_536 or > 16_777_216)
				throw Invalid("maxOutputBytes must be between 65536 and 16777216.");
			if (RequireString(execution, "concurrency") is not "single")
				throw Invalid("execution.concurrency must be 'single'.");

			var output = RequireObject(RequireProperty(root, "output"), "output");
			ValidateProperties(output, ["protocol"], "output");
			var outputProtocol = RequireString(output, "protocol") switch
			{
				"ndjson-v1" => AutomationOutputProtocol.NdjsonV1,
				"exit-code" => AutomationOutputProtocol.ExitCode,
				_ => throw Invalid("output.protocol must be 'ndjson-v1' or 'exit-code'."),
			};
			var externalTools = ValidateExternalTools(root);
			var settings = ValidateSettings(root);

			if (inputMode is AutomationInputMode.ArgvPaths &&
				(root.TryGetProperty("settings", out _) || root.TryGetProperty("externalTools", out _)))
			{
				throw Invalid("argv-paths actions must omit settings and externalTools.");
			}

			return new ManifestValidationResult(
				new ValidatedAutomationManifest(
					id,
					packageVersion,
					displayName,
					description,
					entryPointPath,
					inputMode,
					selection,
					timeoutSeconds,
					maxOutputBytes,
					outputProtocol,
					externalTools,
					settings,
					manifestBytes),
				id,
				displayName,
				description,
				[]);
		}
		catch (Exception ex) when (ex is ManifestValidationException or JsonException or DecoderFallbackException)
		{
			var diagnostic = ex is ManifestValidationException ? ex.Message : $"action.json is malformed: {ex.Message}";
			return new ManifestValidationResult(null, fallbackId, fallbackName, diagnostic, [diagnostic]);
		}
	}

	private static ImmutableArray<AutomationExternalToolDefinition> ValidateExternalTools(JsonElement root)
	{
		if (!root.TryGetProperty("externalTools", out var element))
			return [];
		if (element.ValueKind is not JsonValueKind.Array)
			throw Invalid("externalTools must be an array.");

		var definitions = ImmutableArray.CreateBuilder<AutomationExternalToolDefinition>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in element.EnumerateArray())
		{
			var tool = RequireObject(item, "externalTools item");
			ValidateProperties(tool, ["id", "displayName", "homepage", "minimumFileVersion"], "externalTools item");
			var id = RequireString(tool, "id");
			if (id.Length is < 3 or > 128 || !QualifiedIdRegex().IsMatch(id))
				throw Invalid("External-tool id must be publisher-qualified lowercase text between 3 and 128 characters.");
			if (!ids.Add(id))
				throw Invalid($"Duplicate external-tool id '{id}'.");
			var displayName = RequireBoundedText(tool, "displayName", 1, 80);
			var minimumVersion = tool.TryGetProperty("minimumFileVersion", out _) ? RequireString(tool, "minimumFileVersion") : null;
			if (tool.TryGetProperty("homepage", out _))
				_ = RequireString(tool, "homepage");
			definitions.Add(new AutomationExternalToolDefinition(id, displayName, minimumVersion));
		}
		return definitions.ToImmutable();
	}

	private static ImmutableArray<AutomationSettingDefinition> ValidateSettings(JsonElement root)
	{
		if (!root.TryGetProperty("settings", out var element))
			return [];
		if (element.ValueKind is not JsonValueKind.Array)
			throw Invalid("settings must be an array.");

		var definitions = ImmutableArray.CreateBuilder<AutomationSettingDefinition>();
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in element.EnumerateArray())
		{
			var setting = RequireObject(item, "settings item");
			ValidateProperties(
				setting,
				["key", "displayName", "description", "type", "default", "minimum", "maximum", "minimumLength", "maximumLength", "values"],
				"settings item");
			var key = RequireString(setting, "key");
			if (!SettingKeyRegex().IsMatch(key))
				throw Invalid("Setting key must match [a-z][a-zA-Z0-9._-]{0,63}.");
			if (!keys.Add(key))
				throw Invalid($"Duplicate setting key '{key}'.");
			_ = RequireBoundedText(setting, "displayName", 1, 80);
			_ = RequireBoundedText(setting, "description", 1, 500);
			var type = RequireString(setting, "type");
			var defaultElement = RequireProperty(setting, "default");
			double? minimum = OptionalFiniteNumber(setting, "minimum");
			double? maximum = OptionalFiniteNumber(setting, "maximum");
			int? minimumLength = OptionalNonNegativeInt32(setting, "minimumLength");
			int? maximumLength = OptionalNonNegativeInt32(setting, "maximumLength");
			if (minimum > maximum || minimumLength > maximumLength)
				throw Invalid($"Setting '{key}' has an invalid minimum/maximum range.");

			ImmutableArray<string> values = [];
			AutomationSettingValue defaultValue;
			switch (type)
			{
				case "boolean" when defaultElement.ValueKind is JsonValueKind.True or JsonValueKind.False:
					RequireAbsentConstraints(setting, key, "minimum", "maximum", "minimumLength", "maximumLength", "values");
					defaultValue = new(AutomationSettingValueKind.Boolean, BooleanValue: defaultElement.GetBoolean());
					break;
				case "integer" when defaultElement.TryGetInt64(out var integer):
					RequireAbsentConstraints(setting, key, "minimumLength", "maximumLength", "values");
					if ((minimum is not null && integer < minimum) || (maximum is not null && integer > maximum))
						throw Invalid($"Default for setting '{key}' is outside its bounds.");
					defaultValue = new(AutomationSettingValueKind.Integer, IntegerValue: integer);
					break;
				case "number" when defaultElement.TryGetDouble(out var number) && double.IsFinite(number):
					RequireAbsentConstraints(setting, key, "minimumLength", "maximumLength", "values");
					if ((minimum is not null && number < minimum) || (maximum is not null && number > maximum))
						throw Invalid($"Default for setting '{key}' is outside its bounds.");
					defaultValue = new(AutomationSettingValueKind.Number, NumberValue: number);
					break;
				case "string" or "filePath" or "folderPath" when defaultElement.ValueKind is JsonValueKind.String:
					RequireAbsentConstraints(setting, key, "minimum", "maximum", "values");
					var text = defaultElement.GetString()!;
					if ((minimumLength is not null && text.Length < minimumLength) || (maximumLength is not null && text.Length > maximumLength))
						throw Invalid($"Default for setting '{key}' is outside its length bounds.");
					defaultValue = new(AutomationSettingValueKind.String, StringValue: text);
					break;
				case "enum" when defaultElement.ValueKind is JsonValueKind.String:
					RequireAbsentConstraints(setting, key, "minimum", "maximum", "minimumLength", "maximumLength");
					values = RequireStringArray(setting, "values");
					if (values.IsEmpty || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
						throw Invalid($"Enum setting '{key}' requires a non-empty unique values list.");
					var enumDefault = defaultElement.GetString()!;
					if (!values.Contains(enumDefault, StringComparer.Ordinal))
						throw Invalid($"Default for enum setting '{key}' must appear in values.");
					defaultValue = new(AutomationSettingValueKind.String, StringValue: enumDefault);
					break;
				default:
					throw Invalid($"Setting '{key}' has an invalid type or default value.");
			}

			definitions.Add(new AutomationSettingDefinition(key, type, defaultValue, minimum, maximum, minimumLength, maximumLength, values));
		}
		return definitions.ToImmutable();
	}

	private static void RequireAbsentConstraints(JsonElement setting, string key, params string[] names)
	{
		foreach (var name in names)
		{
			if (setting.TryGetProperty(name, out _))
				throw Invalid($"Setting '{key}' cannot declare '{name}' for its type.");
		}
	}

	private static double? OptionalFiniteNumber(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var property))
			return null;
		if (!property.TryGetDouble(out var value) || !double.IsFinite(value))
			throw Invalid($"{name} must be a finite number.");
		return value;
	}

	private static int? OptionalNonNegativeInt32(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var property))
			return null;
		if (!property.TryGetInt32(out var value) || value < 0)
			throw Invalid($"{name} must be a non-negative integer.");
		return value;
	}

	private static AutomationSelectionPolicy ValidateSelection(JsonElement selection)
	{
		ValidateProperties(selection, ["minItems", "maxItems", "itemKinds", "extensions"], "input.selection");
		var minItems = RequireInt32(selection, "minItems");
		if (minItems < 1)
			throw Invalid("minItems must be at least 1.");
		var maxItems = RequireInt32(selection, "maxItems");
		if (maxItems < minItems || maxItems > 10_000)
			throw Invalid("maxItems must be between minItems and 10000.");

		var itemKinds = RequireStringArray(selection, "itemKinds");
		if (itemKinds.IsEmpty || itemKinds.Any(kind => kind is not ("file" or "folder")))
			throw Invalid("itemKinds must be a non-empty subset of 'file' and 'folder'.");

		var extensions = RequireStringArray(selection, "extensions");
		if (extensions.Any(extension => extension.Length < 2 || extension[0] != '.'))
			throw Invalid("extensions must begin with '.'.");

		return new AutomationSelectionPolicy(minItems, maxItems, itemKinds, extensions);
	}

	private static string ResolveEntryPoint(string packagePath, string entryPoint)
	{
		if (string.IsNullOrWhiteSpace(entryPoint) || Path.IsPathRooted(entryPoint) ||
			!string.Equals(Path.GetExtension(entryPoint), ".py", StringComparison.OrdinalIgnoreCase))
		{
			throw Invalid("entryPoint must remain inside the package and name an existing .py file.");
		}

		var packageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packagePath));
		var resolved = Path.GetFullPath(Path.Join(packageRoot, entryPoint));
		if (!resolved.StartsWith(packageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
			!File.Exists(resolved))
		{
			throw Invalid("entryPoint must remain inside the package and name an existing .py file.");
		}

		return resolved;
	}

	private static void ValidatePythonRange(string value, Version pythonVersion)
	{
		var match = PythonRangeRegex().Match(value);
		if (!match.Success ||
			!Version.TryParse(match.Groups[1].Value, out var lower) ||
			!Version.TryParse(match.Groups[2].Value, out var upper))
		{
			throw Invalid("python.requires must contain one inclusive lower bound and one exclusive upper bound.");
		}

		if (pythonVersion < lower || pythonVersion >= upper)
			throw Invalid($"python.requires '{value}' does not include bundled Python {pythonVersion}.");
	}

	private static void ValidateProperties(JsonElement element, IEnumerable<string> allowed, string location)
	{
		var allowedSet = allowed as HashSet<string> ?? new HashSet<string>(allowed, StringComparer.Ordinal);
		foreach (var property in element.EnumerateObject())
		{
			if (!allowedSet.Contains(property.Name))
				throw Invalid($"Unknown property '{property.Name}' in {location}.");
		}
	}

	private static JsonElement RequireProperty(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var property))
			throw Invalid($"Required property '{name}' is missing.");
		return property;
	}

	private static JsonElement RequireObject(JsonElement element, string name)
	{
		if (element.ValueKind is not JsonValueKind.Object)
			throw Invalid($"{name} must be an object.");
		return element;
	}

	private static string RequireString(JsonElement element, string name)
	{
		var property = RequireProperty(element, name);
		if (property.ValueKind is not JsonValueKind.String)
			throw Invalid($"{name} must be a string.");
		return property.GetString()!;
	}

	private static string RequireBoundedText(JsonElement element, string name, int minimum, int maximum)
	{
		var value = RequireString(element, name).Trim();
		if (value.Length < minimum || value.Length > maximum)
			throw Invalid($"{name} must contain between {minimum} and {maximum} characters after trimming.");
		return value;
	}

	private static int RequireInt32(JsonElement element, string name)
	{
		var property = RequireProperty(element, name);
		if (property.ValueKind is not JsonValueKind.Number || !property.TryGetInt32(out var value))
			throw Invalid($"{name} must be an integer.");
		return value;
	}

	private static ImmutableArray<string> RequireStringArray(JsonElement element, string name)
	{
		var property = RequireProperty(element, name);
		if (property.ValueKind is not JsonValueKind.Array)
			throw Invalid($"{name} must be an array.");

		var values = ImmutableArray.CreateBuilder<string>();
		foreach (var item in property.EnumerateArray())
		{
			if (item.ValueKind is not JsonValueKind.String)
				throw Invalid($"{name} must contain only strings.");
			values.Add(item.GetString()!);
		}
		return values.ToImmutable();
	}

	private static string? FindDuplicateProperty(JsonElement element)
	{
		if (element.ValueKind is JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject())
			{
				if (!names.Add(property.Name))
					return property.Name;
				var nested = FindDuplicateProperty(property.Value);
				if (nested is not null)
					return nested;
			}
		}
		else if (element.ValueKind is JsonValueKind.Array)
		{
			foreach (var item in element.EnumerateArray())
			{
				var nested = FindDuplicateProperty(item);
				if (nested is not null)
					return nested;
			}
		}
		return null;
	}

	private static ManifestValidationException Invalid(string message) => new(message);

	[GeneratedRegex(@"^[a-z0-9]+(?:[.-][a-z0-9]+)+$", RegexOptions.CultureInvariant)]
	private static partial Regex QualifiedIdRegex();

	[GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
	private static partial Regex SemVerRegex();

	[GeneratedRegex(@"^>=(\d+\.\d+(?:\.\d+)?),<(\d+\.\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant)]
	private static partial Regex PythonRangeRegex();

	[GeneratedRegex(@"^[a-z][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
	private static partial Regex SettingKeyRegex();

	private sealed class ManifestValidationException(string message) : Exception(message);
}
