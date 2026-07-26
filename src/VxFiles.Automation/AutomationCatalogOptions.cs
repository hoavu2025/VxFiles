// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace VxFiles.Automation;

internal sealed record AutomationCatalogOptions(
	ImmutableArray<string> PackageRoots,
	Version HostVersion,
	Version PythonVersion);
