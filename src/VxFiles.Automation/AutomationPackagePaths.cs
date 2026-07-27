// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text.Json;

namespace VxFiles.Automation;

/// <summary>
/// Filesystem containment for manifest-declared package files.
/// </summary>
internal static class AutomationPackagePaths
{
	/// <summary>
	/// Resolves <paramref name="relativePath"/> inside <paramref name="packagePath"/>. Escaping the package is always
	/// package-fatal; unusable path text stays inside the caller's scope.
	/// </summary>
	public static string Resolve(
		string packagePath,
		string relativePath,
		string fieldName,
		AutomationValidationScope scope)
	{
		try
		{
			if (Path.IsPathRooted(relativePath))
				throw AutomationValidationException.Package($"{fieldName} must resolve inside the package.");

			var packageRoot = Path.GetFullPath(packagePath)
				.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var path = Path.GetFullPath(Path.Combine(packagePath, relativePath));
			if (!path.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
				throw AutomationValidationException.Package($"{fieldName} must resolve inside the package.");
			return path;
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw AutomationValidationException.For(scope, $"{fieldName} contains invalid path text.");
		}
	}

	public static string? ValidateOptionalFile(
		string packagePath,
		JsonElement parent,
		string propertyName,
		AutomationValidationScope scope)
	{
		if (!parent.TryGetProperty(propertyName, out var property))
			return null;
		if (property.ValueKind is not JsonValueKind.String)
			throw AutomationValidationException.For(scope, $"{propertyName} must be a string.");

		var relativePath = property.GetString()!;
		var path = Resolve(packagePath, relativePath, propertyName, scope);
		if (!File.Exists(path))
			throw AutomationValidationException.For(scope, $"{propertyName} was not found: {relativePath}");
		return relativePath;
	}

	public static string ValidateEntryPoint(string packagePath, JsonElement action)
	{
		var entryPoint = AutomationManifestReader.RequireString(action, "entryPoint", AutomationValidationScope.Action);
		var entryPointPath = Resolve(packagePath, entryPoint, "Action entryPoint", AutomationValidationScope.Action);
		if (!File.Exists(entryPointPath))
			throw AutomationValidationException.Action($"Action entryPoint was not found: {entryPoint}");
		return entryPointPath;
	}
}
