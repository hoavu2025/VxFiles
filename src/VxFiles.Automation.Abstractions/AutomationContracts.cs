// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace VxFiles.Automation.Abstractions;

public readonly record struct AutomationPackageId
{
	private static readonly Regex ValuePattern = new(
		@"^(?=.{3,128}$)[a-z0-9]+(?:[.-][a-z0-9]+)+$",
		RegexOptions.CultureInvariant);

	private readonly string? value;

	public string Value => value ?? throw new InvalidOperationException("The package id is not initialized.");

	private AutomationPackageId(string value)
		=> this.value = value;

	public static AutomationPackageId Parse(string value)
		=> TryParse(value, out var result)
			? result
			: throw new ArgumentException("Package id must be publisher-qualified lowercase text between 3 and 128 characters.", nameof(value));

	public static bool TryParse(string? value, out AutomationPackageId result)
	{
		if (value is not null && ValuePattern.IsMatch(value))
		{
			result = new(value);
			return true;
		}

		result = default;
		return false;
	}

	public override string ToString() => Value;
}

public readonly record struct AutomationActionLocalId
{
	private static readonly Regex ValuePattern = new(
		@"^[a-z][a-z0-9-]{0,63}$",
		RegexOptions.CultureInvariant);

	private readonly string? value;

	public string Value => value ?? throw new InvalidOperationException("The action id is not initialized.");

	private AutomationActionLocalId(string value)
		=> this.value = value;

	public static AutomationActionLocalId Parse(string value)
		=> TryParse(value, out var result)
			? result
			: throw new ArgumentException("Action id must be lowercase text between 1 and 64 characters.", nameof(value));

	public static bool TryParse(string? value, out AutomationActionLocalId result)
	{
		if (value is not null && ValuePattern.IsMatch(value))
		{
			result = new(value);
			return true;
		}

		result = default;
		return false;
	}

	public override string ToString() => Value;
}

public readonly record struct AutomationActionId
{
	public AutomationPackageId PackageId { get; }
	public AutomationActionLocalId LocalId { get; }

	public string Value => $"{PackageId.Value}/{LocalId.Value}";

	public AutomationActionId(AutomationPackageId packageId, AutomationActionLocalId localId)
	{
		_ = packageId.Value;
		_ = localId.Value;
		PackageId = packageId;
		LocalId = localId;
	}

	public override string ToString() => Value;
}

public enum AutomationAvailability
{
	Available,
	Disabled,
	MissingDependency,
}

/// <summary>
/// What an Automation Action accepts as input: how many items, of which kinds, with which file extensions.
/// </summary>
/// <remarks>
/// This is a contract rather than a runner detail because a host surface has to answer "can this run on what
/// is selected right now?" before invoking anything. Evaluating it lives in <see cref="AutomationSelectionRules"/>
/// so the host and the session reach the same verdict.
/// </remarks>
public sealed record AutomationSelectionPolicy(
	int MinItems,
	int MaxItems,
	ImmutableArray<string> ItemKinds,
	ImmutableArray<string> Extensions);

/// <summary>
/// One Automation Action as a host surface sees it.
/// </summary>
/// <remarks>
/// <paramref name="Selection"/> is <see langword="null"/> for an action that failed validation: its manifest
/// never produced a policy, and it cannot be run regardless.
/// </remarks>
public sealed record AutomationActionSnapshot(
	AutomationActionId Id,
	string DisplayName,
	string Description,
	string? Icon,
	AutomationAvailability Availability,
	ImmutableArray<string> Diagnostics,
	AutomationSelectionPolicy? Selection = null);

public sealed record AutomationPackageSnapshot(
	AutomationPackageId Id,
	string PackageVersion,
	string DisplayName,
	string Description,
	string Author,
	string? Icon,
	AutomationAvailability Availability,
	ImmutableArray<string> Diagnostics,
	ImmutableArray<AutomationActionSnapshot> Actions);

public sealed record AutomationCatalogSnapshot(
	ImmutableArray<AutomationPackageSnapshot> Packages);
