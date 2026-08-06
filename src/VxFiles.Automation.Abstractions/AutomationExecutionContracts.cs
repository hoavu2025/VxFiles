// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.ComponentModel;

namespace VxFiles.Automation.Abstractions;

public readonly record struct AutomationRunId(Guid Value);

/// <summary>
/// The host's folder/selection revision at capture time. Result routing is rejected when the host has moved on.
/// </summary>
public readonly record struct HostRevision(long Value);

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

/// <summary>
/// The immutable folder-and-selection input captured when an Automation Action is invoked.
/// </summary>
public sealed record SelectionSnapshot(
	string ActiveFolderPath,
	DateTimeOffset CapturedAtUtc,
	ImmutableArray<SelectedPath> Items);

public sealed record AutomationInvocation(
	AutomationActionId ActionId,
	long CatalogRevision,
	HostRevision HostRevision,
	SelectionSnapshot Selection);

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

/// <summary>
/// Everything a host surface needs to render Automation Tools: the package/action hierarchy plus run activity.
/// </summary>
public sealed record AutomationSnapshot(
	long Revision,
	long CatalogRevision,
	ImmutableArray<AutomationPackageSnapshot> Packages,
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

/// <summary>
/// Trust and shared external-tool configuration belong to the whole Automation Package.
/// </summary>
public sealed record AutomationPackageState(
	string? TrustedFingerprint,
	ImmutableDictionary<string, AutomationExternalToolConfiguration> ExternalTools);

/// <summary>
/// Typed settings belong to a single Automation Action inside its package.
/// </summary>
public sealed record AutomationActionSettings(
	ImmutableDictionary<string, AutomationSettingValue> Values);

/// <remarks>
/// Identity rests on <paramref name="Fingerprint"/> alone: it is the only one of these the trust fingerprint
/// mixes in. Do not add a signature status. A correct check reports <c>unsigned</c> for the FFmpeg builds people
/// actually install, so the field can only lie or say nothing, and saying nothing is smaller.
/// </remarks>
public sealed record AutomationExternalToolIdentity(
	string Id,
	string ExecutablePath,
	string Fingerprint,
	string? FileVersion);

/// <summary>
/// Package-wide consent shown before the first run and whenever package, runner, or tool identity changes.
/// </summary>
public sealed record AutomationTrustRequest(
	AutomationPackageId PackageId,
	string DisplayName,
	string PackageVersion,
	string PackagePath,
	ImmutableArray<AutomationActionId> Actions,
	AutomationActionId RequestedAction,
	int SelectedItemCount,
	string TrustFingerprint,
	string RunnerFingerprint,
	ImmutableArray<AutomationExternalToolIdentity> ExternalTools);

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
	ValueTask<AutomationPackageState> ReadPackageStateAsync(
		AutomationPackageId packageId,
		CancellationToken cancellationToken = default);

	ValueTask WritePackageTrustAsync(
		AutomationPackageId packageId,
		string fingerprint,
		CancellationToken cancellationToken = default);

	ValueTask<AutomationActionSettings> ReadActionSettingsAsync(
		AutomationActionId actionId,
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

/// <summary>
/// The whole external surface of the headless Automation module.
/// </summary>
public interface IAutomationSession : INotifyPropertyChanged, IAsyncDisposable
{
	AutomationSnapshot Snapshot { get; }

	ValueTask InvokeAsync(
		AutomationInvocation invocation,
		CancellationToken cancellationToken = default);

	ValueTask CancelAsync(
		AutomationRunId runId,
		CancellationToken cancellationToken = default);
}
