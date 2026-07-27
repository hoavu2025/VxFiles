// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record DiscoveredPackage(
	AutomationPackageSnapshot Snapshot,
	bool HasValidIdentity,
	AutomationPackageDefinition? Definition);

/// <summary>
/// Applies Automation Package policy to a manifest. JSON mechanics, path containment, compatibility,
/// settings/tool rules, and snapshot mapping live in their own types.
/// </summary>
internal static class AutomationManifestValidator
{
	private static readonly string[] PackageProperties =
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

	private static readonly string[] ActionProperties =
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

	public static DiscoveredPackage Validate(string packagePath, AutomationCatalogOptions options)
	{
		var folderName = Path.GetFileName(packagePath);
		var metadata = new AutomationPackageMetadata(AutomationFallbackIds.FromFolderName(folderName), folderName);
		try
		{
			return ValidateCore(packagePath, options, metadata);
		}
		catch (AutomationValidationException exception) when (exception.Scope is AutomationValidationScope.Package)
		{
			return new(AutomationSnapshotMapping.DisabledPackage(metadata, exception.Message), metadata.HasValidIdentity, null);
		}
	}

	private static DiscoveredPackage ValidateCore(
		string packagePath,
		AutomationCatalogOptions options,
		AutomationPackageMetadata metadata)
	{
		const AutomationValidationScope scope = AutomationValidationScope.Package;
		var manifestFileName = AutomationManifestReader.ManifestFileName;
		var manifestBytes = AutomationManifestReader.ReadManifestBytes(Path.Combine(packagePath, manifestFileName));
		using var document = AutomationManifestReader.ParseManifest(manifestBytes);
		var root = AutomationManifestReader.RequireObject(document.RootElement, manifestFileName, scope);
		AutomationManifestReader.ValidateProperties(root, PackageProperties, manifestFileName, scope);

		if (AutomationManifestReader.RequireInt32(root, "schemaVersion", scope) is not 1)
			throw AutomationValidationException.Package("schemaVersion must be 1.");

		var packageIdText = AutomationManifestReader.RequireString(root, "id", scope);
		if (!AutomationPackageId.TryParse(packageIdText, out var packageId))
			throw AutomationValidationException.Package("Package id must be publisher-qualified lowercase text between 3 and 128 characters.");
		metadata.Id = packageId;
		metadata.HasValidIdentity = true;
		metadata.PackageVersion = AutomationCompatibilityRules.ValidatePackageVersion(root);

		var displayName = AutomationManifestReader.RequireBoundedString(root, "displayName", 1, 80, scope);
		var description = AutomationManifestReader.RequireBoundedString(root, "description", 1, 500, scope);
		var author = AutomationManifestReader.RequireBoundedString(root, "author", 1, 120, scope);
		var icon = AutomationPackagePaths.ValidateOptionalFile(packagePath, root, "icon", scope);
		metadata.DisplayName = displayName;
		metadata.Description = description;
		metadata.Author = author;
		metadata.Icon = icon;
		AutomationCompatibilityRules.ValidateMinimumHostVersion(root, options.HostVersion);
		AutomationCompatibilityRules.ValidatePython(root, options.PythonVersion);
		var externalTools = AutomationExternalToolRules.ValidatePackageTools(root);

		var actionsElement = AutomationManifestReader.RequireProperty(root, "actions", scope);
		if (actionsElement.ValueKind is not JsonValueKind.Array || actionsElement.GetArrayLength() is 0)
			throw AutomationValidationException.Package("actions must be a non-empty array.");

		RejectDuplicateActionIds(actionsElement);
		var actions = actionsElement
			.EnumerateArray()
			.Select(action => ValidateAction(packagePath, packageId, action, externalTools))
			.OrderBy(action => action.Snapshot.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(action => action.Snapshot.Id.LocalId.Value, StringComparer.Ordinal)
			.ToImmutableArray();

		var definition = new AutomationPackageDefinition(
			packageId,
			Path.GetFullPath(packagePath),
			metadata.PackageVersion,
			displayName,
			manifestBytes,
			externalTools,
			actions
				.Where(action => action.Definition is not null)
				.ToImmutableDictionary(action => action.Snapshot.Id.LocalId, action => action.Definition!));

		return new(
			AutomationSnapshotMapping.AvailablePackage(metadata, [.. actions.Select(action => action.Snapshot)]),
			true,
			definition);
	}

	private static ValidatedAction ValidateAction(
		string packagePath,
		AutomationPackageId packageId,
		JsonElement action,
		ImmutableArray<AutomationExternalToolDefinition> packageExternalTools)
	{
		const AutomationValidationScope scope = AutomationValidationScope.Action;
		var metadata = new AutomationActionMetadata(AutomationSnapshotMapping.FallbackActionId(action));
		try
		{
			var actionObject = AutomationManifestReader.RequireObject(action, "actions item", scope);
			AutomationManifestReader.ValidateProperties(actionObject, ActionProperties, "actions item", scope);

			var localIdText = AutomationManifestReader.RequireString(actionObject, "id", scope);
			metadata.DisplayName = AutomationManifestReader.RequireBoundedString(actionObject, "displayName", 1, 80, scope);
			metadata.Description = AutomationManifestReader.RequireBoundedString(actionObject, "description", 1, 500, scope);
			if (!AutomationActionLocalId.TryParse(localIdText, out var localId))
				throw AutomationValidationException.Action("Action id must be lowercase text between 1 and 64 characters.");
			metadata.LocalId = localId;
			metadata.Icon = AutomationPackagePaths.ValidateOptionalFile(packagePath, actionObject, "icon", scope);

			var entryPointPath = AutomationPackagePaths.ValidateEntryPoint(packagePath, actionObject);
			var (inputMode, selection) = ValidateInput(actionObject);
			var (timeoutSeconds, maximumOutputBytes) = ValidateExecution(actionObject);
			var outputProtocol = ValidateOutput(actionObject);
			var settings = AutomationSettingRules.Validate(actionObject);
			var externalToolIds = AutomationExternalToolRules.ValidateActionReferences(actionObject, packageExternalTools);

			return new(
				AutomationSnapshotMapping.AvailableAction(packageId, metadata),
				new AutomationActionDefinition(
					new(packageId, localId),
					entryPointPath,
					inputMode,
					selection,
					timeoutSeconds,
					maximumOutputBytes,
					outputProtocol,
					externalToolIds,
					settings));
		}
		catch (AutomationValidationException exception) when (exception.Scope is AutomationValidationScope.Action)
		{
			return new(AutomationSnapshotMapping.DisabledAction(packageId, metadata, exception.Message), null);
		}
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
				throw AutomationValidationException.Package($"Duplicate action id '{id}'.");
		}
	}

	private static (AutomationInputMode Mode, AutomationSelectionPolicy Selection) ValidateInput(JsonElement action)
	{
		const AutomationValidationScope scope = AutomationValidationScope.Action;
		var input = AutomationManifestReader.RequireObject(
			AutomationManifestReader.RequireProperty(action, "input", scope),
			"action.input",
			scope);
		AutomationManifestReader.ValidateProperties(input, ["mode", "selection"], "action.input", scope);
		var mode = AutomationManifestReader.RequireString(input, "mode", scope) switch
		{
			"json-stdin" => AutomationInputMode.JsonStdin,
			"argv-paths" => AutomationInputMode.ArgvPaths,
			_ => throw AutomationValidationException.Action("input.mode must be 'json-stdin' or 'argv-paths'."),
		};

		var selection = AutomationManifestReader.RequireObject(
			AutomationManifestReader.RequireProperty(input, "selection", scope),
			"action.input.selection",
			scope);
		AutomationManifestReader.ValidateProperties(
			selection,
			["minItems", "maxItems", "itemKinds", "extensions"],
			"action.input.selection",
			scope);
		var minimumItems = AutomationManifestReader.RequireInt32(selection, "minItems", scope);
		var maximumItems = AutomationManifestReader.RequireInt32(selection, "maxItems", scope);
		if (minimumItems < 0 || maximumItems < Math.Max(1, minimumItems) || maximumItems > 10_000)
			throw AutomationValidationException.Action("Selection item bounds are invalid.");

		var itemKinds = AutomationManifestReader.RequireStringArray(selection, "itemKinds", ["file", "folder"], scope);
		var extensions = AutomationManifestReader.RequireStringArray(selection, "extensions", null, scope);
		return (mode, new(minimumItems, maximumItems, itemKinds, extensions));
	}

	private static (int TimeoutSeconds, int MaximumOutputBytes) ValidateExecution(JsonElement action)
	{
		const AutomationValidationScope scope = AutomationValidationScope.Action;
		var execution = AutomationManifestReader.RequireObject(
			AutomationManifestReader.RequireProperty(action, "execution", scope),
			"action.execution",
			scope);
		AutomationManifestReader.ValidateProperties(
			execution,
			["timeoutSeconds", "maxOutputBytes", "concurrency"],
			"action.execution",
			scope);
		var timeoutSeconds = AutomationManifestReader.RequireInt32(execution, "timeoutSeconds", scope);
		var maximumOutputBytes = AutomationManifestReader.RequireInt32(execution, "maxOutputBytes", scope);
		if (timeoutSeconds is < 1 or > 86_400)
			throw AutomationValidationException.Action("execution.timeoutSeconds must be between 1 and 86400.");
		if (maximumOutputBytes is < 65_536 or > 16_777_216)
			throw AutomationValidationException.Action("execution.maxOutputBytes must be between 65536 and 16777216.");
		if (execution.TryGetProperty("concurrency", out var concurrency) &&
			(concurrency.ValueKind is not JsonValueKind.String || concurrency.GetString() is not "single"))
		{
			throw AutomationValidationException.Action("execution.concurrency must be 'single'.");
		}

		return (timeoutSeconds, maximumOutputBytes);
	}

	private static AutomationOutputProtocol ValidateOutput(JsonElement action)
	{
		const AutomationValidationScope scope = AutomationValidationScope.Action;
		var output = AutomationManifestReader.RequireObject(
			AutomationManifestReader.RequireProperty(action, "output", scope),
			"action.output",
			scope);
		AutomationManifestReader.ValidateProperties(output, ["protocol"], "action.output", scope);
		return AutomationManifestReader.RequireString(output, "protocol", scope) switch
		{
			"ndjson-v1" => AutomationOutputProtocol.NdjsonV1,
			"exit-code" => AutomationOutputProtocol.ExitCode,
			_ => throw AutomationValidationException.Action("output.protocol must be 'ndjson-v1' or 'exit-code'."),
		};
	}

	private sealed record ValidatedAction(
		AutomationActionSnapshot Snapshot,
		AutomationActionDefinition? Definition);
}
