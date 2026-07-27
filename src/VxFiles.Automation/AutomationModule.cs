// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// The composition entry point for the headless Automation module.
/// </summary>
public static class AutomationModule
{
	public static AutomationModuleOptions CreateDefaultOptions(
		ImmutableArray<string> packageRoots,
		string stateRoot,
		string temporaryRoot,
		Version hostVersion,
		string hostLocale) => new(
			packageRoots,
			stateRoot,
			temporaryRoot,
			PinnedAutomationPython.Pinned,
			hostVersion,
			hostLocale);

	public static ValueTask<IAutomationSession> OpenAsync(
		AutomationModuleOptions options,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		return OpenAsync(
			options,
			new FileAutomationStateStore(options.StateRoot),
			new DenyingAutomationTrustConsent(),
			new RejectingAutomationResultRouter(),
			cancellationToken);
	}

	public static ValueTask<IAutomationSession> OpenAsync(
		AutomationModuleOptions options,
		IAutomationStateStore stateStore,
		IAutomationTrustConsent trustConsent,
		IAutomationResultRouter resultRouter,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(stateStore);
		ArgumentNullException.ThrowIfNull(trustConsent);
		ArgumentNullException.ThrowIfNull(resultRouter);
		cancellationToken.ThrowIfCancellationRequested();
		PinnedAutomationPython.Validate(options);

		IAutomationSession session = new AutomationSession(
			options,
			AutomationManifestCatalog.Discover(options.CatalogOptions),
			stateStore,
			trustConsent,
			resultRouter);
		return ValueTask.FromResult(session);
	}
}
