// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal static class AutomationManifestCatalog
{
	public static AutomationCatalogSnapshot Discover(AutomationCatalogOptions options)
	{
		var discovered = GetPackagePaths(options.PackageRoots)
			.Select(packagePath => DiscoverPackage(packagePath, options))
			.ToList();

		DisableDuplicatePackageIds(discovered);

		return new(discovered
			.Select(package => package.Snapshot)
			.OrderBy(package => package.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(package => package.Id.Value, StringComparer.Ordinal)
			.ToImmutableArray());
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
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or AutomationPackageValidationException)
		{
			var folderName = Path.GetFileName(packagePath);
			var snapshot = new AutomationPackageSnapshot(
				AutomationFallbackIds.FromFolderName(folderName),
				string.Empty,
				folderName,
				exception.Message,
				string.Empty,
				null,
				AutomationAvailability.Disabled,
				[exception.Message],
				[]);
			return new(snapshot, false);
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
				throw new AutomationPackageValidationException("Automation Packages cannot contain reparse points.");

			foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
			{
				var attributes = File.GetAttributes(entry);
				if ((attributes & FileAttributes.ReparsePoint) is not 0)
					throw new AutomationPackageValidationException("Automation Packages cannot contain reparse points.");
				if ((attributes & FileAttributes.Directory) is not 0)
					pendingDirectories.Push(entry);
			}
		}
	}

	internal sealed record DiscoveredPackage(
		AutomationPackageSnapshot Snapshot,
		bool HasValidIdentity);
}
