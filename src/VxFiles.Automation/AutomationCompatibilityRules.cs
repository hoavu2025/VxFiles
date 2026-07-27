// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace VxFiles.Automation;

/// <summary>
/// Version compatibility between an Automation Package, the VxFiles host, and the pinned Python runtime.
/// </summary>
internal static partial class AutomationCompatibilityRules
{
	public static string ValidatePackageVersion(JsonElement root)
	{
		var packageVersion = AutomationManifestReader.RequireString(root, "packageVersion", AutomationValidationScope.Package);
		if (!SemanticVersionRegex().IsMatch(packageVersion))
			throw AutomationValidationException.Package("packageVersion must be SemVer 2.0.");
		return packageVersion;
	}

	public static void ValidateMinimumHostVersion(JsonElement root, Version hostVersion)
	{
		var minimumVersionText = AutomationManifestReader.RequireString(root, "minimumHostVersion", AutomationValidationScope.Package);
		var versionCore = minimumVersionText.Split('-', '+')[0];
		if (!SemanticVersionRegex().IsMatch(minimumVersionText) ||
			!Version.TryParse(versionCore, out var minimumVersion))
		{
			throw AutomationValidationException.Package("minimumHostVersion must be SemVer 2.0.");
		}
		if (minimumVersion > hostVersion)
			throw AutomationValidationException.Package($"Package requires VxFiles {minimumVersionText} or later.");
	}

	public static void ValidatePython(JsonElement root, Version pythonVersion)
	{
		var python = AutomationManifestReader.RequireObject(
			AutomationManifestReader.RequireProperty(root, "python", AutomationValidationScope.Package),
			"python",
			AutomationValidationScope.Package);
		AutomationManifestReader.ValidateProperties(python, ["requires"], "python", AutomationValidationScope.Package);
		var requiredRange = AutomationManifestReader.RequireString(python, "requires", AutomationValidationScope.Package);
		var expectedRange = $">={pythonVersion.Major}.{pythonVersion.Minor},<{pythonVersion.Major}.{pythonVersion.Minor + 1}";
		if (!string.Equals(requiredRange, expectedRange, StringComparison.Ordinal))
			throw AutomationValidationException.Package($"python.requires must be '{expectedRange}'.");
	}

	[GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
	private static partial Regex SemanticVersionRegex();
}
