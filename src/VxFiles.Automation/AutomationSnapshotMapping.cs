// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Identity and display text gathered while a manifest is read, so a failure part way through still
/// produces the most informative snapshot available.
/// </summary>
internal sealed class AutomationPackageMetadata(AutomationPackageId id, string displayName)
{
	public AutomationPackageId Id { get; set; } = id;
	public string PackageVersion { get; set; } = string.Empty;
	public string DisplayName { get; set; } = displayName;
	public string Description { get; set; } = string.Empty;
	public string Author { get; set; } = string.Empty;
	public string? Icon { get; set; }
	public bool HasValidIdentity { get; set; }
}

internal sealed class AutomationActionMetadata(AutomationActionLocalId localId)
{
	public AutomationActionLocalId LocalId { get; set; } = localId;
	public string DisplayName { get; set; } = "Invalid action";
	public string Description { get; set; } = string.Empty;
	public string? Icon { get; set; }
}

/// <summary>
/// Turns validation outcomes into the immutable snapshots host surfaces consume.
/// </summary>
internal static class AutomationSnapshotMapping
{
	public static AutomationPackageSnapshot DisabledPackage(AutomationPackageMetadata metadata, string diagnostic)
		=> new(
			metadata.Id,
			metadata.PackageVersion,
			metadata.DisplayName,
			string.IsNullOrEmpty(metadata.Description) ? diagnostic : metadata.Description,
			metadata.Author,
			metadata.Icon,
			AutomationAvailability.Disabled,
			[diagnostic],
			[]);

	public static AutomationPackageSnapshot AvailablePackage(
		AutomationPackageMetadata metadata,
		ImmutableArray<AutomationActionSnapshot> actions)
		=> new(
			metadata.Id,
			metadata.PackageVersion,
			metadata.DisplayName,
			metadata.Description,
			metadata.Author,
			metadata.Icon,
			AutomationAvailability.Available,
			[],
			actions);

	public static AutomationActionSnapshot DisabledAction(
		AutomationPackageId packageId,
		AutomationActionMetadata metadata,
		string diagnostic)
		=> new(
			new(packageId, metadata.LocalId),
			metadata.DisplayName,
			string.IsNullOrEmpty(metadata.Description) ? diagnostic : metadata.Description,
			metadata.Icon,
			AutomationAvailability.Disabled,
			[diagnostic]);

	public static AutomationActionSnapshot AvailableAction(
		AutomationPackageId packageId,
		AutomationActionMetadata metadata)
		=> new(
			new(packageId, metadata.LocalId),
			metadata.DisplayName,
			metadata.Description,
			metadata.Icon,
			AutomationAvailability.Available,
			[]);

	/// <summary>
	/// Gives an action with an unusable id a content-stable identity, so diagnostics survive manifest reordering.
	/// </summary>
	public static AutomationActionLocalId FallbackActionId(JsonElement action)
	{
		var content = Encoding.UTF8.GetBytes(action.GetRawText());
		var hash = Convert.ToHexStringLower(SHA256.HashData(content));
		return AutomationActionLocalId.Parse($"invalid-{hash[..16]}");
	}
}
