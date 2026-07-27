// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace VxFiles.Automation;

public sealed record AutomationModuleOptions(
	ImmutableArray<string> PackageRoots,
	string StateRoot,
	string TemporaryRoot,
	PinnedRuntime Runtime,
	Version HostVersion,
	string HostLocale)
{
	internal AutomationCatalogOptions CatalogOptions => new(PackageRoots, HostVersion, Runtime.PythonVersion);
}
