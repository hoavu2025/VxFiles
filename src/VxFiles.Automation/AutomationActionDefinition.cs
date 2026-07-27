// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
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

/// <summary>
/// Everything the runner needs to execute one Automation Action.
/// </summary>
internal sealed record AutomationActionDefinition(
	AutomationActionId Id,
	string EntryPointPath,
	AutomationInputMode InputMode,
	AutomationSelectionPolicy Selection,
	int TimeoutSeconds,
	int MaxOutputBytes,
	AutomationOutputProtocol OutputProtocol,
	ImmutableArray<string> ExternalToolIds,
	ImmutableArray<AutomationSettingDefinition> Settings);

/// <summary>
/// The trust, update, and validation unit that owns runnable Automation Actions.
/// </summary>
internal sealed record AutomationPackageDefinition(
	AutomationPackageId Id,
	string PackagePath,
	string PackageVersion,
	string DisplayName,
	byte[] ManifestBytes,
	ImmutableArray<AutomationExternalToolDefinition> ExternalTools,
	ImmutableDictionary<AutomationActionLocalId, AutomationActionDefinition> Actions);
