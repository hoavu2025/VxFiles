// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Safe defaults for a session opened without a host: nothing is trusted and no result intent is applied.
/// </summary>
internal sealed class DenyingAutomationTrustConsent : IAutomationTrustConsent
{
	public ValueTask<bool> RequestTrustAsync(AutomationTrustRequest request, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(false);
}

internal sealed class RejectingAutomationResultRouter : IAutomationResultRouter
{
	public ValueTask<ImmutableArray<AutomationIntentResult>> RouteAsync(
		AutomationResultRoutingRequest request,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(request.Intents
			.Select(intent => new AutomationIntentResult(intent, AutomationIntentDisposition.Rejected, "No host result router is configured."))
			.ToImmutableArray());
}
