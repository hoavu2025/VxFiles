// Copyright (c) Files Community
// Licensed under the MIT License.

namespace VxFiles.Automation;

/// <summary>
/// How far a manifest rule violation reaches: an Automation Package root or a single Automation Action.
/// </summary>
internal enum AutomationValidationScope
{
	Package,
	Action,
}

internal sealed class AutomationValidationException(AutomationValidationScope scope, string message) : Exception(message)
{
	public AutomationValidationScope Scope { get; } = scope;

	public static AutomationValidationException Package(string message)
		=> new(AutomationValidationScope.Package, message);

	public static AutomationValidationException Action(string message)
		=> new(AutomationValidationScope.Action, message);

	public static AutomationValidationException For(AutomationValidationScope scope, string message)
		=> new(scope, message);
}
