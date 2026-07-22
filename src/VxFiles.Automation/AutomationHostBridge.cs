// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

public sealed record AutomationHostItem(string FullPath, SelectedPathKind Kind);

public interface IAutomationHostContext
{
	event EventHandler? LocationChanged;

	string? CurrentFolderPath { get; }

	IReadOnlyList<AutomationHostItem> SelectedItems { get; }

	ValueTask RefreshCurrentFolderAsync(CancellationToken cancellationToken);

	ValueTask RevealDirectChildrenAsync(
		ImmutableArray<string> paths,
		CancellationToken cancellationToken);
}

public sealed class AutomationHostBridge : IAutomationResultRouter, IDisposable
{
	private readonly IAutomationHostContext _context;
	private long _revision = 1;
	private bool _disposed;

	public AutomationHostBridge(IAutomationHostContext context)
	{
		_context = context ?? throw new ArgumentNullException(nameof(context));
		_context.LocationChanged += Context_LocationChanged;
	}

	public HostRevision Revision => new(Interlocked.Read(ref _revision));

	public SelectionSnapshot CaptureSelection()
	{
		ThrowIfDisposed();
		var folder = _context.CurrentFolderPath;
		if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
			throw new InvalidOperationException("Automation Actions require an active filesystem folder.");

		var items = ImmutableArray.CreateBuilder<SelectedPath>(_context.SelectedItems.Count);
		foreach (var item in _context.SelectedItems)
		{
			if (string.IsNullOrWhiteSpace(item.FullPath) || !Path.IsPathFullyQualified(item.FullPath))
				throw new InvalidOperationException("Automation Actions require filesystem selections with absolute paths.");
			items.Add(new SelectedPath(
				item.FullPath,
				item.Kind,
				item.FullPath.StartsWith(@"\\", StringComparison.Ordinal)
					? SelectedLocationKind.Unc
					: SelectedLocationKind.Local));
		}

		return new SelectionSnapshot(folder, DateTimeOffset.UtcNow, items.MoveToImmutable());
	}

	public async ValueTask<ImmutableArray<AutomationIntentResult>> RouteAsync(
		AutomationResultRoutingRequest request,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(request);

		if (request.HostRevision != Revision ||
			!PathsEqual(request.CapturedFolderPath, _context.CurrentFolderPath))
		{
			return MarkAll(request.Intents, AutomationIntentDisposition.Stale,
				"The active folder changed while the Automation Action was running.");
		}

		var results = ImmutableArray.CreateBuilder<AutomationIntentResult>(request.Intents.Length);
		foreach (var intent in request.Intents)
		{
			cancellationToken.ThrowIfCancellationRequested();
			switch (intent)
			{
				case AutomationResultIntent.RefreshCurrentFolder:
					await _context.RefreshCurrentFolderAsync(cancellationToken);
					results.Add(new(intent, AutomationIntentDisposition.Applied, "The current folder was refreshed."));
					break;

				case AutomationResultIntent.RevealPaths reveal when
					reveal.Paths.Length > 0 && AllDirectChildren(request.CapturedFolderPath, reveal.Paths):
					await _context.RevealDirectChildrenAsync(reveal.Paths, cancellationToken);
					results.Add(new(intent, AutomationIntentDisposition.Applied, "The output items were revealed."));
					break;

				case AutomationResultIntent.RevealPaths:
					results.Add(new(intent, AutomationIntentDisposition.Rejected,
						"Reveal paths must be direct children of the captured folder."));
					break;

				default:
					results.Add(new(intent, AutomationIntentDisposition.Rejected, "The result intent is unsupported."));
					break;
			}
		}

		return results.MoveToImmutable();
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_context.LocationChanged -= Context_LocationChanged;
		_disposed = true;
	}

	private void Context_LocationChanged(object? sender, EventArgs e) => Interlocked.Increment(ref _revision);

	private static bool AllDirectChildren(string folder, ImmutableArray<string> paths)
	{
		var normalizedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
		foreach (var path in paths)
		{
			if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
				return false;
			var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
			if (!PathsEqual(normalizedFolder, parent))
				return false;
		}
		return true;
	}

	private static bool PathsEqual(string? left, string? right) =>
		!string.IsNullOrWhiteSpace(left) &&
		!string.IsNullOrWhiteSpace(right) &&
		string.Equals(
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
			StringComparison.OrdinalIgnoreCase);

	private static ImmutableArray<AutomationIntentResult> MarkAll(
		ImmutableArray<AutomationResultIntent> intents,
		AutomationIntentDisposition disposition,
		string message)
	{
		var results = ImmutableArray.CreateBuilder<AutomationIntentResult>(intents.Length);
		foreach (var intent in intents)
			results.Add(new(intent, disposition, message));
		return results.MoveToImmutable();
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
