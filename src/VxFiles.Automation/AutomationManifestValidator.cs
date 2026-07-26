// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed class AutomationPackageValidationException(string message) : Exception(message);

internal sealed class AutomationActionValidationException(string message) : Exception(message);

internal static partial class AutomationManifestValidator
{
	private const int MaximumManifestBytes = 262_144;
	private const string ManifestFileName = "vxpackage.json";

	private static readonly HashSet<string> PackageProperties =
	[
		"schemaVersion",
		"id",
		"packageVersion",
		"displayName",
		"description",
		"author",
		"icon",
		"minimumHostVersion",
		"python",
		"externalTools",
		"actions",
	];

	private static readonly HashSet<string> ActionProperties =
	[
		"id",
		"displayName",
		"description",
		"icon",
		"entryPoint",
		"input",
		"execution",
		"output",
		"settings",
		"externalTools",
	];

	public static AutomationManifestCatalog.DiscoveredPackage Validate(
		string packagePath,
		AutomationCatalogOptions options)
	{
		var folderName = Path.GetFileName(packagePath);
		var metadata = new PackageMetadata(
			AutomationFallbackIds.FromFolderName(folderName),
			string.Empty,
			folderName,
			string.Empty,
			string.Empty,
			null,
			false);
		try
		{
			return ValidateCore(packagePath, options, metadata);
		}
		catch (AutomationPackageValidationException exception)
		{
			var snapshot = new AutomationPackageSnapshot(
				metadata.Id,
				metadata.PackageVersion,
				metadata.DisplayName,
				string.IsNullOrEmpty(metadata.Description) ? exception.Message : metadata.Description,
				metadata.Author,
				metadata.Icon,
				AutomationAvailability.Disabled,
				[exception.Message],
				[]);
			return new(snapshot, metadata.HasValidIdentity);
		}
	}

	private static AutomationManifestCatalog.DiscoveredPackage ValidateCore(
		string packagePath,
		AutomationCatalogOptions options,
		PackageMetadata metadata)
	{
		var manifestPath = Path.Combine(packagePath, ManifestFileName);
		var manifestBytes = ReadManifest(manifestPath);
		using var document = ParseManifest(manifestBytes);
		var root = RequireObject(document.RootElement, ManifestFileName, true);
		ValidateProperties(root, PackageProperties, ManifestFileName, true);

		if (RequireInt32(root, "schemaVersion", true) is not 1)
			throw InvalidPackage("schemaVersion must be 1.");

		var packageIdText = RequireString(root, "id", true);
		if (!AutomationPackageId.TryParse(packageIdText, out var packageId))
			throw InvalidPackage("Package id must be publisher-qualified lowercase text between 3 and 128 characters.");
		metadata.Id = packageId;
		metadata.HasValidIdentity = true;

		var packageVersion = RequireString(root, "packageVersion", true);
		if (!SemanticVersionRegex().IsMatch(packageVersion))
			throw InvalidPackage("packageVersion must be SemVer 2.0.");
		metadata.PackageVersion = packageVersion;

		var displayName = RequireBoundedString(root, "displayName", 1, 80, true);
		var description = RequireBoundedString(root, "description", 1, 500, true);
		var author = RequireBoundedString(root, "author", 1, 120, true);
		var icon = ValidateOptionalPackageFile(packagePath, root, "icon", true);
		metadata.DisplayName = displayName;
		metadata.Description = description;
		metadata.Author = author;
		metadata.Icon = icon;
		ValidateMinimumHostVersion(root, options.HostVersion);
		ValidatePython(root, options.PythonVersion);
		var externalTools = ValidateExternalTools(root);

		var actionsElement = RequireProperty(root, "actions", true);
		if (actionsElement.ValueKind is not JsonValueKind.Array || actionsElement.GetArrayLength() is 0)
			throw InvalidPackage("actions must be a non-empty array.");

		RejectDuplicateActionIds(actionsElement);
		var actions = actionsElement
			.EnumerateArray()
			.Select(action => ValidateAction(packagePath, packageId, action, externalTools))
			.OrderBy(action => action.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(action => action.Id.LocalId.Value, StringComparer.Ordinal)
			.ToImmutableArray();

		var snapshot = new AutomationPackageSnapshot(
			packageId,
			packageVersion,
			displayName,
			description,
			author,
			icon,
			AutomationAvailability.Available,
			[],
			actions);
		return new(snapshot, true);
	}

	private sealed class PackageMetadata(
		AutomationPackageId id,
		string packageVersion,
		string displayName,
		string description,
		string author,
		string? icon,
		bool hasValidIdentity)
	{
		public AutomationPackageId Id { get; set; } = id;
		public string PackageVersion { get; set; } = packageVersion;
		public string DisplayName { get; set; } = displayName;
		public string Description { get; set; } = description;
		public string Author { get; set; } = author;
		public string? Icon { get; set; } = icon;
		public bool HasValidIdentity { get; set; } = hasValidIdentity;
	}

	private static AutomationActionSnapshot ValidateAction(
		string packagePath,
		AutomationPackageId packageId,
		JsonElement action,
		IReadOnlySet<string> packageExternalTools)
	{
		var localId = AutomationActionLocalId.Parse(CreateInvalidActionId(action));
		var displayName = "Invalid action";
		var description = string.Empty;
		string? icon = null;
		try
		{
			var actionObject = RequireObject(action, "actions item", false);
			ValidateProperties(actionObject, ActionProperties, "actions item", false);

			var localIdText = RequireString(actionObject, "id", false);
			displayName = RequireBoundedString(actionObject, "displayName", 1, 80, false);
			description = RequireBoundedString(actionObject, "description", 1, 500, false);
			if (!AutomationActionLocalId.TryParse(localIdText, out var parsedLocalId))
				throw InvalidAction("Action id must be lowercase text between 1 and 64 characters.");
			localId = parsedLocalId;
			icon = ValidateOptionalPackageFile(packagePath, actionObject, "icon", false);
			ValidateEntryPoint(packagePath, actionObject);
			ValidateInput(actionObject);
			ValidateExecution(actionObject);
			ValidateOutput(actionObject);
			ValidateSettings(actionObject);
			ValidateActionExternalTools(actionObject, packageExternalTools);

			return new(
				new(packageId, localId),
				displayName,
				description,
				icon,
				AutomationAvailability.Available,
				[]);
		}
		catch (AutomationActionValidationException exception)
		{
			return new(
				new(packageId, localId),
				displayName,
				string.IsNullOrEmpty(description) ? exception.Message : description,
				icon,
				AutomationAvailability.Disabled,
				[exception.Message]);
		}
	}

	private static byte[] ReadManifest(string manifestPath)
	{
		if (!File.Exists(manifestPath))
			throw InvalidPackage($"{ManifestFileName} was not found.");

		var manifestBytes = File.ReadAllBytes(manifestPath);
		if (manifestBytes.Length > MaximumManifestBytes)
			throw InvalidPackage($"{ManifestFileName} exceeds {MaximumManifestBytes} bytes.");
		return manifestBytes;
	}

	private static JsonDocument ParseManifest(byte[] manifestBytes)
	{
		try
		{
			_ = new UTF8Encoding(false, true).GetString(manifestBytes);
			return JsonDocument.Parse(manifestBytes, new()
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
			});
		}
		catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
		{
			throw InvalidPackage($"{ManifestFileName} is malformed: {exception.Message}");
		}
	}

	private static void ValidateMinimumHostVersion(JsonElement root, Version hostVersion)
	{
		var minimumVersionText = RequireString(root, "minimumHostVersion", true);
		var versionCore = minimumVersionText.Split('-', '+')[0];
		if (!SemanticVersionRegex().IsMatch(minimumVersionText) ||
			!Version.TryParse(versionCore, out var minimumVersion))
		{
			throw InvalidPackage("minimumHostVersion must be SemVer 2.0.");
		}
		if (minimumVersion > hostVersion)
			throw InvalidPackage($"Package requires VxFiles {minimumVersionText} or later.");
	}

	private static void ValidatePython(JsonElement root, Version pythonVersion)
	{
		var python = RequireObject(RequireProperty(root, "python", true), "python", true);
		ValidateProperties(python, ["requires"], "python", true);
		var requiredRange = RequireString(python, "requires", true);
		var expectedRange = $">={pythonVersion.Major}.{pythonVersion.Minor},<{pythonVersion.Major}.{pythonVersion.Minor + 1}";
		if (!string.Equals(requiredRange, expectedRange, StringComparison.Ordinal))
			throw InvalidPackage($"python.requires must be '{expectedRange}'.");
	}

	private static IReadOnlySet<string> ValidateExternalTools(JsonElement root)
	{
		if (!root.TryGetProperty("externalTools", out var externalTools))
			return new HashSet<string>(StringComparer.Ordinal);
		if (externalTools.ValueKind is not JsonValueKind.Array)
			throw InvalidPackage("externalTools must be an array.");

		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var externalTool in externalTools.EnumerateArray())
		{
			var tool = RequireObject(externalTool, "externalTools item", true);
			ValidateProperties(
				tool,
				["id", "displayName", "homepage", "minimumFileVersion"],
				"externalTools item",
				true);
			var id = RequireString(tool, "id", true);
			if (!AutomationPackageId.TryParse(id, out _))
				throw InvalidPackage($"External-tool id '{id}' is invalid.");
			if (!ids.Add(id))
				throw InvalidPackage($"Duplicate external-tool id '{id}'.");
			_ = RequireBoundedString(tool, "displayName", 1, 80, true);
			if (tool.TryGetProperty("homepage", out var homepage))
			{
				if (homepage.ValueKind is not JsonValueKind.String ||
					!Uri.TryCreate(homepage.GetString(), UriKind.Absolute, out _))
				{
					throw InvalidPackage($"External tool '{id}' has an invalid homepage.");
				}
			}
			if (tool.TryGetProperty("minimumFileVersion", out var minimumVersion) &&
				minimumVersion.ValueKind is not JsonValueKind.String)
			{
				throw InvalidPackage($"External tool '{id}' minimumFileVersion must be a string.");
			}
		}

		return ids;
	}

	private static void RejectDuplicateActionIds(JsonElement actions)
	{
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var action in actions.EnumerateArray())
		{
			if (action.ValueKind is not JsonValueKind.Object ||
				!action.TryGetProperty("id", out var idElement) ||
				idElement.ValueKind is not JsonValueKind.String)
			{
				continue;
			}

			var id = idElement.GetString()!;
			if (!ids.Add(id))
				throw InvalidPackage($"Duplicate action id '{id}'.");
		}
	}

	private static void ValidateEntryPoint(string packagePath, JsonElement action)
	{
		var entryPoint = RequireString(action, "entryPoint", false);
		var entryPointPath = ResolvePackageFile(packagePath, entryPoint, "Action entryPoint", false);
		if (!File.Exists(entryPointPath))
			throw InvalidAction($"Action entryPoint was not found: {entryPoint}");
	}

	private static string? ValidateOptionalPackageFile(
		string packagePath,
		JsonElement parent,
		string propertyName,
		bool packageFatal)
	{
		if (!parent.TryGetProperty(propertyName, out var property))
			return null;
		if (property.ValueKind is not JsonValueKind.String)
			throw ValidationError($"{propertyName} must be a string.", packageFatal);

		var relativePath = property.GetString()!;
		var path = ResolvePackageFile(packagePath, relativePath, propertyName, packageFatal);
		if (!File.Exists(path))
			throw ValidationError($"{propertyName} was not found: {relativePath}", packageFatal);
		return relativePath;
	}

	private static string ResolvePackageFile(
		string packagePath,
		string relativePath,
		string fieldName,
		bool packageFatal)
	{
		try
		{
			if (Path.IsPathRooted(relativePath))
				throw InvalidPackage($"{fieldName} must resolve inside the package.");

			var packageRoot = Path.GetFullPath(packagePath)
				.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var path = Path.GetFullPath(Path.Combine(packagePath, relativePath));
			if (!path.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
				throw InvalidPackage($"{fieldName} must resolve inside the package.");
			return path;
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw ValidationError($"{fieldName} contains invalid path text.", packageFatal);
		}
	}

	private static void ValidateInput(JsonElement action)
	{
		var input = RequireObject(RequireProperty(action, "input", false), "action.input", false);
		ValidateProperties(input, ["mode", "selection"], "action.input", false);
		var mode = RequireString(input, "mode", false);
		if (mode is not ("json-stdin" or "argv-paths"))
			throw InvalidAction("input.mode must be 'json-stdin' or 'argv-paths'.");

		var selection = RequireObject(RequireProperty(input, "selection", false), "action.input.selection", false);
		ValidateProperties(selection, ["minItems", "maxItems", "itemKinds", "extensions"], "action.input.selection", false);
		var minimumItems = RequireInt32(selection, "minItems", false);
		var maximumItems = RequireInt32(selection, "maxItems", false);
		if (minimumItems < 0 || maximumItems < Math.Max(1, minimumItems) || maximumItems > 10_000)
			throw InvalidAction("Selection item bounds are invalid.");

		ValidateStringArray(selection, "itemKinds", ["file", "folder"]);
		ValidateStringArray(selection, "extensions", null);
	}

	private static void ValidateExecution(JsonElement action)
	{
		var execution = RequireObject(RequireProperty(action, "execution", false), "action.execution", false);
		ValidateProperties(execution, ["timeoutSeconds", "maxOutputBytes", "concurrency"], "action.execution", false);
		var timeoutSeconds = RequireInt32(execution, "timeoutSeconds", false);
		var maximumOutputBytes = RequireInt32(execution, "maxOutputBytes", false);
		if (timeoutSeconds is < 1 or > 86_400)
			throw InvalidAction("execution.timeoutSeconds must be between 1 and 86400.");
		if (maximumOutputBytes is < 65_536 or > 16_777_216)
			throw InvalidAction("execution.maxOutputBytes must be between 65536 and 16777216.");
		if (execution.TryGetProperty("concurrency", out var concurrency) &&
			(concurrency.ValueKind is not JsonValueKind.String || concurrency.GetString() is not "single"))
		{
			throw InvalidAction("execution.concurrency must be 'single'.");
		}
	}

	private static void ValidateOutput(JsonElement action)
	{
		var output = RequireObject(RequireProperty(action, "output", false), "action.output", false);
		ValidateProperties(output, ["protocol"], "action.output", false);
		if (RequireString(output, "protocol", false) is not ("ndjson-v1" or "exit-code"))
			throw InvalidAction("output.protocol must be 'ndjson-v1' or 'exit-code'.");
	}

	private static void ValidateActionExternalTools(
		JsonElement action,
		IReadOnlySet<string> packageExternalTools)
	{
		if (!action.TryGetProperty("externalTools", out var externalTools))
			return;
		if (externalTools.ValueKind is not JsonValueKind.Array)
			throw InvalidAction("action.externalTools must be an array.");

		var references = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in externalTools.EnumerateArray())
		{
			if (item.ValueKind is not JsonValueKind.String)
				throw InvalidAction("action.externalTools must contain strings.");
			var id = item.GetString()!;
			if (!references.Add(id))
				throw InvalidAction($"Duplicate action external-tool reference '{id}'.");
			if (!packageExternalTools.Contains(id))
				throw InvalidAction($"Action references unknown external tool '{id}'.");
		}
	}

	private static void ValidateSettings(JsonElement action)
	{
		if (!action.TryGetProperty("settings", out var settings))
			return;
		if (settings.ValueKind is not JsonValueKind.Array)
			throw InvalidAction("settings must be an array.");

		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in settings.EnumerateArray())
		{
			var setting = RequireObject(item, "settings item", false);
			ValidateProperties(
				setting,
				["key", "displayName", "description", "type", "default", "minimum", "maximum", "minimumLength", "maximumLength", "values"],
				"settings item",
				false);
			var key = RequireString(setting, "key", false);
			if (!SettingKeyRegex().IsMatch(key) || !keys.Add(key))
				throw InvalidAction($"Setting key '{key}' is invalid or duplicated.");
			_ = RequireBoundedString(setting, "displayName", 1, 80, false);
			_ = RequireBoundedString(setting, "description", 1, 500, false);
			var type = RequireString(setting, "type", false);
			var defaultValue = RequireProperty(setting, "default", false);
			var validDefault = type switch
			{
				"boolean" => defaultValue.ValueKind is JsonValueKind.True or JsonValueKind.False,
				"integer" => defaultValue.TryGetInt64(out _),
				"number" => defaultValue.TryGetDouble(out var number) && double.IsFinite(number),
				"string" or "filePath" or "folderPath" or "enum" => defaultValue.ValueKind is JsonValueKind.String,
				_ => false,
			};
			if (!validDefault)
				throw InvalidAction($"Setting '{key}' has an invalid type or default value.");

			ValidateOptionalNumber(setting, "minimum");
			ValidateOptionalNumber(setting, "maximum");
			ValidateOptionalInteger(setting, "minimumLength");
			ValidateOptionalInteger(setting, "maximumLength");
			if (type is "enum")
				ValidateEnumSetting(setting, key, defaultValue.GetString()!);
		}
	}

	private static void ValidateOptionalNumber(JsonElement setting, string name)
	{
		if (setting.TryGetProperty(name, out var value) &&
			(!value.TryGetDouble(out var number) || !double.IsFinite(number)))
		{
			throw InvalidAction($"{name} must be a finite number.");
		}
	}

	private static void ValidateOptionalInteger(JsonElement setting, string name)
	{
		if (setting.TryGetProperty(name, out var value) &&
			(!value.TryGetInt32(out var number) || number < 0))
		{
			throw InvalidAction($"{name} must be a non-negative integer.");
		}
	}

	private static void ValidateEnumSetting(JsonElement setting, string key, string defaultValue)
	{
		if (!setting.TryGetProperty("values", out var values) || values.ValueKind is not JsonValueKind.Array)
			throw InvalidAction($"Enum setting '{key}' requires a values array.");
		var permittedValues = values.EnumerateArray()
			.Select(value => value.ValueKind is JsonValueKind.String
				? value.GetString()!
				: throw InvalidAction($"Enum setting '{key}' values must be strings."))
			.ToArray();
		if (permittedValues.Length is 0 ||
			permittedValues.Distinct(StringComparer.Ordinal).Count() != permittedValues.Length ||
			!permittedValues.Contains(defaultValue, StringComparer.Ordinal))
		{
			throw InvalidAction($"Enum setting '{key}' values are invalid.");
		}
	}

	private static void ValidateStringArray(
		JsonElement parent,
		string name,
		IReadOnlyCollection<string>? permittedValues)
	{
		var value = RequireProperty(parent, name, false);
		if (value.ValueKind is not JsonValueKind.Array)
			throw InvalidAction($"{name} must be an array.");

		var values = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in value.EnumerateArray())
		{
			if (item.ValueKind is not JsonValueKind.String)
				throw InvalidAction($"{name} must contain strings.");
			var text = item.GetString()!;
			if (!values.Add(text))
				throw InvalidAction($"{name} must not contain duplicates.");
			if (permittedValues is not null && !permittedValues.Contains(text))
				throw InvalidAction($"{name} contains unsupported value '{text}'.");
		}
	}

	private static string CreateInvalidActionId(JsonElement action)
	{
		var content = Encoding.UTF8.GetBytes(action.GetRawText());
		var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
		return $"invalid-{hash[..16]}";
	}

	private static JsonElement RequireObject(JsonElement value, string location, bool packageFatal)
	{
		if (value.ValueKind is not JsonValueKind.Object)
			throw ValidationError($"{location} must be an object.", packageFatal);
		return value;
	}

	private static void ValidateProperties(
		JsonElement value,
		IReadOnlyCollection<string> permittedProperties,
		string location,
		bool packageFatal)
	{
		var properties = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			if (!properties.Add(property.Name))
				throw ValidationError($"Duplicate property '{property.Name}' in {location}.", packageFatal);
			if (!permittedProperties.Contains(property.Name))
				throw ValidationError($"Unknown property '{property.Name}' in {location}.", packageFatal);
		}
	}

	private static JsonElement RequireProperty(JsonElement value, string name, bool packageFatal)
	{
		if (!value.TryGetProperty(name, out var property))
			throw ValidationError($"Required property '{name}' is missing.", packageFatal);
		return property;
	}

	private static string RequireString(JsonElement value, string name, bool packageFatal)
	{
		var property = RequireProperty(value, name, packageFatal);
		if (property.ValueKind is not JsonValueKind.String)
			throw ValidationError($"{name} must be a string.", packageFatal);
		return property.GetString()!;
	}

	private static string RequireBoundedString(
		JsonElement value,
		string name,
		int minimumLength,
		int maximumLength,
		bool packageFatal)
	{
		var text = RequireString(value, name, packageFatal);
		if (text.Length < minimumLength || text.Length > maximumLength)
			throw ValidationError($"{name} must contain between {minimumLength} and {maximumLength} characters.", packageFatal);
		return text;
	}

	private static int RequireInt32(JsonElement value, string name, bool packageFatal)
	{
		var property = RequireProperty(value, name, packageFatal);
		if (!property.TryGetInt32(out var result))
			throw ValidationError($"{name} must be an integer.", packageFatal);
		return result;
	}

	private static Exception ValidationError(string message, bool packageFatal)
		=> packageFatal ? InvalidPackage(message) : InvalidAction(message);

	private static AutomationPackageValidationException InvalidPackage(string message) => new(message);

	private static AutomationActionValidationException InvalidAction(string message) => new(message);

	[GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
	private static partial Regex SemanticVersionRegex();

	[GeneratedRegex(@"^[a-z][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
	private static partial Regex SettingKeyRegex();
}
