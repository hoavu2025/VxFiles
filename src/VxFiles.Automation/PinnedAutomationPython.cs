// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;

namespace VxFiles.Automation;

internal static class PinnedAutomationPython
{
	public static readonly Version Version = new(3, 14, 6);
	public const string ExecutableSha256 = "03168c01b7b7491423350e82c26fee71f35b43694d1319d3c668bda6903a0c38";

	public static string ExecutablePath => Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "AutomationRuntime", "Python", "python.exe"));

	public static void Validate(AutomationModuleOptions options)
	{
		if (options.PythonVersion != Version ||
			!string.Equals(options.PythonSha256, ExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(Path.GetFullPath(options.PythonExecutablePath), ExecutablePath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Automation must use the packaged CPython 3.14.6 runtime pinned by VxFiles.");
		}
		if (!File.Exists(ExecutablePath))
			throw new FileNotFoundException("The packaged CPython 3.14.6 runtime is missing. Acquire it before packaging VxFiles.", ExecutablePath);
	}
}
