// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace VxFiles.Automation;

/// <param name="RuntimeRoot">
/// The app-local <c>AutomationRuntime</c> folder: the runner scripts and the pinned interpreter beneath them.
/// Every file under it is covered by package trust, so changing any of it renews consent for every package.
/// </param>
public sealed record AutomationModuleOptions(
	ImmutableArray<string> PackageRoots,
	string StateRoot,
	string TemporaryRoot,
	string RuntimeRoot,
	string PythonExecutablePath,
	Version PythonVersion,
	string PythonSha256,
	Version HostVersion,
	string HostLocale)
{
	internal AutomationCatalogOptions CatalogOptions => new(PackageRoots, HostVersion, PythonVersion);
}
