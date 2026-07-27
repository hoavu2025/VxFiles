// Copyright (c) Files Community
// Licensed under the MIT License.

namespace VxFiles.Automation;

/// <summary>
/// The app-local Automation runtime an action is launched from: the runner scripts, the interpreter beneath
/// them, and the identity that interpreter must match.
/// </summary>
/// <remarks>
/// These four facts are one thing and travel together everywhere — launching a process, verifying the pinned
/// hash, and fingerprinting for trust all need them at once. Keeping them as one value is also what stops the
/// payload's folder layout from being spelled out in more than one place.
///
/// <para>
/// <see cref="Root"/> is fingerprinted whole, so package trust covers the runner scripts and not just the
/// interpreter. See <c>AutomationTrustFingerprint</c>.
/// </para>
/// </remarks>
/// <param name="Root">The <c>AutomationRuntime</c> folder laid down beside the executable by the payload.</param>
/// <param name="PythonVersion">The CPython version a package manifest must declare compatibility with.</param>
/// <param name="PythonSha256">The interpreter's expected SHA-256, re-checked before every launch.</param>
public sealed record PinnedRuntime(
	string Root,
	Version PythonVersion,
	string PythonSha256)
{
	/// <summary>
	/// The interpreter's location within a runtime tree. The payload fixes this layout, so it is derived rather
	/// than carried: a root and an executable that disagreed would be a state with no correct meaning.
	/// </summary>
	public static string GetPythonExecutablePath(string root)
		=> Path.Join(root, "Python", "python.exe");

	public string PythonExecutablePath => GetPythonExecutablePath(Root);

	/// <summary>
	/// The script every action is launched through. Actions never start their own entry point directly.
	/// </summary>
	public string BootstrapPath => Path.Join(Root, "vxfiles_runner.py");
}
