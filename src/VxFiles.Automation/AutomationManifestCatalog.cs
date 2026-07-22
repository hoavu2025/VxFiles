// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record AutomationPackage(
	string PackagePath,
	ValidatedAutomationManifest Manifest);

internal sealed record AutomationCatalog(
	ImmutableArray<AutomationActionSnapshot> Actions,
	ImmutableDictionary<string, AutomationPackage> Packages,
	ImmutableDictionary<string, AutomationActionSnapshot> InvalidPackagesByPath,
	bool HasValidationErrors);

internal static class AutomationManifestCatalog
{
	public static AutomationCatalog Discover(AutomationModuleOptions options)
	{
		var actions = ImmutableArray.CreateBuilder<AutomationActionSnapshot>();
		var packages = ImmutableDictionary.CreateBuilder<string, AutomationPackage>(StringComparer.Ordinal);
		var invalidPackages = ImmutableDictionary.CreateBuilder<string, AutomationActionSnapshot>(StringComparer.OrdinalIgnoreCase);
		foreach (var actionRoot in options.ActionRoots)
		{
			if (!Directory.Exists(actionRoot))
				continue;

			foreach (var packagePath in Directory.EnumerateDirectories(actionRoot).Order(StringComparer.Ordinal))
			{
				var manifestPath = Path.Join(packagePath, "action.json");
				if (!File.Exists(manifestPath))
					continue;
				if (ContainsReparsePoint(packagePath))
				{
					var folderName = Path.GetFileName(packagePath);
					var reparseSnapshot = new AutomationActionSnapshot(
						new AutomationActionId($"invalid.{folderName}"),
						folderName,
						string.Empty,
						AutomationActionAvailability.Disabled,
						["Automation Action packages cannot contain symbolic links, junctions, or other reparse points."]);
					actions.Add(reparseSnapshot);
					invalidPackages[packagePath] = reparseSnapshot;
					continue;
				}

				var validation = AutomationManifestValidator.Validate(packagePath, File.ReadAllBytes(manifestPath), options);
				var actionSnapshot = new AutomationActionSnapshot(
					new AutomationActionId(validation.Id),
					validation.DisplayName,
					validation.Description,
					validation.Diagnostics.IsEmpty
						? AutomationActionAvailability.RequiresTrust
						: AutomationActionAvailability.Disabled,
					validation.Diagnostics);
				actions.Add(actionSnapshot);

				if (validation.Manifest is not null && !packages.ContainsKey(validation.Id))
					packages.Add(validation.Id, new AutomationPackage(packagePath, validation.Manifest));
				else if (validation.Manifest is null)
					invalidPackages[packagePath] = actionSnapshot;
			}
		}

		var discovered = actions.ToImmutable();
		var duplicateIds = discovered
			.Where(action => action.Availability is not AutomationActionAvailability.Disabled)
			.GroupBy(action => action.Id.Value, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToHashSet(StringComparer.Ordinal);
		foreach (var duplicateId in duplicateIds)
			packages.Remove(duplicateId);

		return new AutomationCatalog(
			discovered
				.Select(action => duplicateIds.Contains(action.Id.Value)
					? action with
					{
						Availability = AutomationActionAvailability.Disabled,
						Diagnostics = action.Diagnostics.Add($"Duplicate action id '{action.Id.Value}' disables every conflicting package."),
					}
					: action)
				.ToImmutableArray(),
			packages.ToImmutable(),
			invalidPackages.ToImmutable(),
			discovered.Any(action => action.Availability is AutomationActionAvailability.Disabled) || duplicateIds.Count > 0);
	}

	private static bool ContainsReparsePoint(string packagePath)
	{
		var pending = new Stack<string>();
		pending.Push(packagePath);
		while (pending.TryPop(out var directory))
		{
			if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) is not 0)
				return true;
			foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
			{
				var attributes = File.GetAttributes(entry);
				if ((attributes & FileAttributes.ReparsePoint) is not 0)
					return true;
				if ((attributes & FileAttributes.Directory) is not 0)
					pending.Push(entry);
			}
		}
		return false;
	}
}
