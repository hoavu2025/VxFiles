// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace VxFiles.Automation;

public sealed record AutomationModuleOptions(
	ImmutableArray<string> PackageRoots,
	string StateRoot,
	string TemporaryRoot,
	string PythonExecutablePath,
	Version PythonVersion,
	string PythonSha256,
	Version HostVersion,
	string HostLocale)
{
	internal AutomationCatalogOptions CatalogOptions => new(PackageRoots, HostVersion, PythonVersion);
}
