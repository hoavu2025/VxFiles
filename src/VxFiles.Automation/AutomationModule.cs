// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

public static class AutomationModule
{
	public static AutomationModuleOptions CreateDefaultOptions(
		ImmutableArray<string> actionRoots,
		string stateRoot,
		string temporaryRoot,
		Version hostVersion,
		string hostLocale) => new(
			actionRoots,
			stateRoot,
			temporaryRoot,
			PinnedAutomationPython.ExecutablePath,
			PinnedAutomationPython.Version,
			PinnedAutomationPython.ExecutableSha256,
			hostVersion,
			hostLocale);

	public static ValueTask<IAutomationBarSession> OpenAsync(
		AutomationModuleOptions options,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		cancellationToken.ThrowIfCancellationRequested();
		PinnedAutomationPython.Validate(options);

		var catalog = AutomationManifestCatalog.Discover(options);
		IAutomationBarSession session = new AutomationBarSession(
			options,
			catalog,
			new FileAutomationStateStore(options.StateRoot),
			new DenyingAutomationTrustConsent(),
			new RejectingAutomationResultRouter());
		return ValueTask.FromResult(session);
	}

	public static ValueTask<IAutomationBarSession> OpenAsync(
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

		IAutomationBarSession session = new AutomationBarSession(
			options,
			AutomationManifestCatalog.Discover(options),
			stateStore,
			trustConsent,
			resultRouter);
		return ValueTask.FromResult(session);
	}
}
