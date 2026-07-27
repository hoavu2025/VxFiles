// Copyright (c) Files Community
// Licensed under the MIT License.

namespace VxFiles.Automation;

/// <summary>
/// The single CPython runtime Automation Actions may use. It is app-local and hash-pinned so a user cannot
/// redirect actions at an arbitrary interpreter, and so runner changes renew package trust.
/// </summary>
internal static class PinnedAutomationPython
{
	public static readonly Version Version = new(3, 14, 6);

	public const string ExecutableSha256 = "03168c01b7b7491423350e82c26fee71f35b43694d1319d3c668bda6903a0c38";

	/// <summary>
	/// The app-local runtime folder: the runner scripts, with the interpreter in a subfolder. Package trust
	/// covers this whole tree, so the scripts that mediate every action are as pinned as the interpreter is.
	/// </summary>
	public static string RuntimeRoot
		=> Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "AutomationRuntime"));

	/// <summary>
	/// The only runtime a session opened by this app is allowed to use.
	/// </summary>
	public static PinnedRuntime Pinned => new(RuntimeRoot, Version, ExecutableSha256);

	public static string ExecutablePath => Pinned.PythonExecutablePath;

	public static void Validate(AutomationModuleOptions options)
	{
		var pinned = Pinned;
		var runtime = options.Runtime;
		if (runtime.PythonVersion != pinned.PythonVersion ||
			!string.Equals(runtime.PythonSha256, pinned.PythonSha256, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(Path.GetFullPath(runtime.Root), pinned.Root, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Automation must use the packaged CPython {Version} runtime pinned by VxFiles.");
		}

		if (!File.Exists(pinned.PythonExecutablePath))
		{
			throw new FileNotFoundException(
				$"The packaged CPython {Version} runtime is missing. Run scripts/automation/Acquire-Python.ps1.",
				pinned.PythonExecutablePath);
		}
	}
}
