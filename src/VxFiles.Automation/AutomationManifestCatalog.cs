// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// A discovery result: what host surfaces display, plus the packages that are actually runnable.
/// </summary>
internal sealed record AutomationCatalog(
	AutomationCatalogSnapshot Snapshot,
	ImmutableDictionary<AutomationPackageId, AutomationPackageDefinition> Packages);

internal static class AutomationManifestCatalog
{
	public static AutomationCatalog Discover(AutomationCatalogOptions options)
	{
		var discovered = GetPackagePaths(options.PackageRoots)
			.Select(packagePath => DiscoverPackage(packagePath, options))
			.ToList();

		DisableDuplicatePackageIds(discovered);

		var snapshot = new AutomationCatalogSnapshot(discovered
			.Select(package => package.Snapshot)
			.OrderBy(package => package.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(package => package.Id.Value, StringComparer.Ordinal)
			.ToImmutableArray());

		var runnable = discovered
			.Where(package => package.Definition is not null)
			.ToImmutableDictionary(package => package.Snapshot.Id, package => package.Definition!);

		return new(snapshot, runnable);
	}

	private static IEnumerable<string> GetPackagePaths(ImmutableArray<string> packageRoots)
	{
		foreach (var root in packageRoots)
		{
			string[] packagePaths;
			try
			{
				packagePaths = Directory.Exists(root)
					? Directory.GetDirectories(root)
					: [];
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				continue;
			}

			foreach (var packagePath in packagePaths)
				yield return packagePath;
		}
	}

	private static DiscoveredPackage DiscoverPackage(string packagePath, AutomationCatalogOptions options)
	{
		try
		{
			RejectReparsePoints(packagePath);
			return AutomationManifestValidator.Validate(packagePath, options);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or AutomationValidationException)
		{
			var folderName = Path.GetFileName(packagePath);
			var metadata = new AutomationPackageMetadata(AutomationFallbackIds.FromFolderName(folderName), folderName);
			return new(AutomationSnapshotMapping.DisabledPackage(metadata, exception.Message), false, null);
		}
	}

	private static void DisableDuplicatePackageIds(List<DiscoveredPackage> discovered)
	{
		var duplicateIds = discovered
			.Where(package => package.HasValidIdentity)
			.GroupBy(package => package.Snapshot.Id, EqualityComparer<AutomationPackageId>.Default)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToHashSet();

		for (var index = 0; index < discovered.Count; index++)
		{
			var package = discovered[index];
			if (!package.HasValidIdentity || !duplicateIds.Contains(package.Snapshot.Id))
				continue;

			var diagnostic = $"Duplicate package id '{package.Snapshot.Id.Value}'.";
			discovered[index] = package with
			{
				Snapshot = package.Snapshot with
				{
					Availability = AutomationAvailability.Disabled,
					Diagnostics = [diagnostic],
					Actions = [],
				},
				Definition = null,
			};
		}
	}

	private static void RejectReparsePoints(string packagePath)
	{
		var pendingDirectories = new Stack<string>();
		pendingDirectories.Push(packagePath);
		while (pendingDirectories.TryPop(out var directory))
		{
			if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) is not 0)
				throw AutomationValidationException.Package("Automation Packages cannot contain reparse points.");

			foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
			{
				var attributes = File.GetAttributes(entry);
				if ((attributes & FileAttributes.ReparsePoint) is not 0)
					throw AutomationValidationException.Package("Automation Packages cannot contain reparse points.");
				if ((attributes & FileAttributes.Directory) is not 0)
					pendingDirectories.Push(entry);
			}
		}
	}
}
