// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace VxFiles.Automation.Abstractions;

public readonly record struct AutomationActionId(string Value);

public readonly record struct AutomationRunId(Guid Value);

public readonly record struct HostRevision(long Value);

public enum AutomationActionAvailability
{
	Available,
	RequiresTrust,
	Disabled,
	MissingDependency,
}

public enum SelectedPathKind
{
	File,
	Folder,
}

public enum SelectedLocationKind
{
	Local,
	Unc,
}

public enum AutomationRunState
{
	Starting,
	Running,
	Cancelling,
	Succeeded,
	Failed,
	TimedOut,
	Cancelled,
}

public enum AutomationLogLevel
{
	Debug,
	Information,
	Warning,
	Error,
}

public enum AutomationIntentDisposition
{
	Applied,
	Rejected,
	Stale,
}

public sealed record SelectedPath(
	string FullPath,
	SelectedPathKind Kind,
	SelectedLocationKind LocationKind);

public sealed record SelectionSnapshot(
	string ActiveFolderPath,
	DateTimeOffset CapturedAtUtc,
	ImmutableArray<SelectedPath> Items);

public sealed record AutomationInvocation(
	AutomationActionId ActionId,
	long CatalogRevision,
	HostRevision HostRevision,
	SelectionSnapshot Selection);

public sealed record AutomationActionSnapshot(
	AutomationActionId Id,
	string DisplayName,
	string Description,
	AutomationActionAvailability Availability,
	ImmutableArray<string> Diagnostics);

public sealed record AutomationLogEntry(
	long Sequence,
	AutomationLogLevel Level,
	string Message);

public abstract record AutomationResultIntent
{
	private AutomationResultIntent()
	{
	}

	public sealed record RefreshCurrentFolder : AutomationResultIntent;

	public sealed record RevealPaths(ImmutableArray<string> Paths) : AutomationResultIntent;
}

public sealed record AutomationIntentResult(
	AutomationResultIntent Intent,
	AutomationIntentDisposition Disposition,
	string Message);

public sealed record AutomationRunSnapshot(
	AutomationRunId Id,
	AutomationActionId ActionId,
	AutomationRunState State,
	SelectionSnapshot Selection,
	HostRevision HostRevision,
	DateTimeOffset StartedAtUtc,
	DateTimeOffset? CompletedAtUtc,
	double? ProgressPercent,
	string Status,
	ImmutableArray<AutomationLogEntry> Logs,
	string StandardError,
	bool StandardErrorTruncated,
	ImmutableArray<AutomationIntentResult> IntentResults,
	string? Failure);

public sealed record AutomationBarSnapshot(
	long Revision,
	long CatalogRevision,
	ImmutableArray<AutomationActionSnapshot> Actions,
	ImmutableArray<AutomationRunSnapshot> ActiveRuns,
	ImmutableArray<AutomationRunSnapshot> RecentRuns);

public enum AutomationSettingValueKind
{
	Boolean,
	Integer,
	Number,
	String,
}

public sealed record AutomationSettingValue(
	AutomationSettingValueKind Kind,
	bool BooleanValue = false,
	long IntegerValue = 0,
	double NumberValue = 0,
	string? StringValue = null);

public sealed record AutomationExternalToolConfiguration(
	string Id,
	string ExecutablePath);

public sealed record AutomationActionState(
	string? TrustedFingerprint,
	ImmutableDictionary<string, AutomationSettingValue> Settings,
	ImmutableDictionary<string, AutomationExternalToolConfiguration> ExternalTools);

public sealed record AutomationTrustRequest(
	AutomationActionId ActionId,
	string DisplayName,
	string PackageVersion,
	string PackagePath,
	int SelectedItemCount,
	string TrustFingerprint,
	string RunnerFingerprint,
	ImmutableArray<AutomationExternalToolIdentity> ExternalTools);

public sealed record AutomationExternalToolIdentity(
	string Id,
	string ExecutablePath,
	string Fingerprint,
	string? FileVersion,
	string SignatureStatus);

public sealed record AutomationResultRoutingRequest(
	AutomationRunId RunId,
	HostRevision HostRevision,
	string CapturedFolderPath,
	ImmutableArray<AutomationResultIntent> Intents);

public sealed record AutomationRunRecord(
	AutomationRunSnapshot Snapshot,
	string PackageVersion,
	string TrustFingerprint);

public interface IAutomationStateStore
{
	ValueTask<AutomationActionState> ReadActionStateAsync(
		AutomationActionId actionId,
		CancellationToken cancellationToken = default);

	ValueTask WriteTrustedFingerprintAsync(
		AutomationActionId actionId,
		string fingerprint,
		CancellationToken cancellationToken = default);

	ValueTask AppendRunRecordAsync(
		AutomationRunRecord record,
		CancellationToken cancellationToken = default);
}

public interface IAutomationTrustConsent
{
	ValueTask<bool> RequestTrustAsync(
		AutomationTrustRequest request,
		CancellationToken cancellationToken = default);
}

public interface IAutomationResultRouter
{
	ValueTask<ImmutableArray<AutomationIntentResult>> RouteAsync(
		AutomationResultRoutingRequest request,
		CancellationToken cancellationToken = default);
}

public interface IAutomationBarSession : INotifyPropertyChanged, IAsyncDisposable
{
	AutomationBarSnapshot Snapshot { get; }

	ValueTask InvokeAsync(
		AutomationInvocation invocation,
		CancellationToken cancellationToken = default);

	ValueTask CancelAsync(
		AutomationRunId runId,
		CancellationToken cancellationToken = default);
}
