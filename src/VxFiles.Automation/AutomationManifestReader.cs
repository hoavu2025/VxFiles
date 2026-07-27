// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace VxFiles.Automation;

/// <summary>
/// JSON and schema mechanics for <c>vxpackage.json</c>. Knows nothing about Automation Package policy.
/// </summary>
internal static class AutomationManifestReader
{
	public const string ManifestFileName = "vxpackage.json";

	private const int MaximumManifestBytes = 262_144;

	public static byte[] ReadManifestBytes(string manifestPath)
	{
		if (!File.Exists(manifestPath))
			throw AutomationValidationException.Package($"{ManifestFileName} was not found.");

		var manifestBytes = File.ReadAllBytes(manifestPath);
		if (manifestBytes.Length > MaximumManifestBytes)
			throw AutomationValidationException.Package($"{ManifestFileName} exceeds {MaximumManifestBytes} bytes.");
		return manifestBytes;
	}

	public static JsonDocument ParseManifest(byte[] manifestBytes)
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
			throw AutomationValidationException.Package($"{ManifestFileName} is malformed: {exception.Message}");
		}
	}

	public static JsonElement RequireObject(JsonElement value, string location, AutomationValidationScope scope)
	{
		if (value.ValueKind is not JsonValueKind.Object)
			throw AutomationValidationException.For(scope, $"{location} must be an object.");
		return value;
	}

	public static void ValidateProperties(
		JsonElement value,
		IReadOnlyCollection<string> permittedProperties,
		string location,
		AutomationValidationScope scope)
	{
		var properties = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			if (!properties.Add(property.Name))
				throw AutomationValidationException.For(scope, $"Duplicate property '{property.Name}' in {location}.");
			if (!permittedProperties.Contains(property.Name))
				throw AutomationValidationException.For(scope, $"Unknown property '{property.Name}' in {location}.");
		}
	}

	public static JsonElement RequireProperty(JsonElement value, string name, AutomationValidationScope scope)
	{
		if (!value.TryGetProperty(name, out var property))
			throw AutomationValidationException.For(scope, $"Required property '{name}' is missing.");
		return property;
	}

	public static string RequireString(JsonElement value, string name, AutomationValidationScope scope)
	{
		var property = RequireProperty(value, name, scope);
		if (property.ValueKind is not JsonValueKind.String)
			throw AutomationValidationException.For(scope, $"{name} must be a string.");
		return property.GetString()!;
	}

	public static string RequireBoundedString(
		JsonElement value,
		string name,
		int minimumLength,
		int maximumLength,
		AutomationValidationScope scope)
	{
		var text = RequireString(value, name, scope);
		if (text.Length < minimumLength || text.Length > maximumLength)
			throw AutomationValidationException.For(scope, $"{name} must contain between {minimumLength} and {maximumLength} characters.");
		return text;
	}

	public static int RequireInt32(JsonElement value, string name, AutomationValidationScope scope)
	{
		var property = RequireProperty(value, name, scope);
		if (!property.TryGetInt32(out var result))
			throw AutomationValidationException.For(scope, $"{name} must be an integer.");
		return result;
	}

	public static ImmutableArray<string> RequireStringArray(
		JsonElement parent,
		string name,
		IReadOnlyCollection<string>? permittedValues,
		AutomationValidationScope scope)
	{
		var value = RequireProperty(parent, name, scope);
		if (value.ValueKind is not JsonValueKind.Array)
			throw AutomationValidationException.For(scope, $"{name} must be an array.");

		var values = ImmutableArray.CreateBuilder<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in value.EnumerateArray())
		{
			if (item.ValueKind is not JsonValueKind.String)
				throw AutomationValidationException.For(scope, $"{name} must contain strings.");
			var text = item.GetString()!;
			if (!seen.Add(text))
				throw AutomationValidationException.For(scope, $"{name} must not contain duplicates.");
			if (permittedValues is not null && !permittedValues.Contains(text))
				throw AutomationValidationException.For(scope, $"{name} contains unsupported value '{text}'.");
			values.Add(text);
		}

		return values.ToImmutable();
	}

	public static double? ReadOptionalNumber(JsonElement value, string name, AutomationValidationScope scope)
	{
		if (!value.TryGetProperty(name, out var property))
			return null;
		if (!property.TryGetDouble(out var number) || !double.IsFinite(number))
			throw AutomationValidationException.For(scope, $"{name} must be a finite number.");
		return number;
	}

	public static int? ReadOptionalInteger(JsonElement value, string name, AutomationValidationScope scope)
	{
		if (!value.TryGetProperty(name, out var property))
			return null;
		if (!property.TryGetInt32(out var number) || number < 0)
			throw AutomationValidationException.For(scope, $"{name} must be a non-negative integer.");
		return number;
	}
}
