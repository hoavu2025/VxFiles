// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.UI.Dispatching;
using Windows.Storage;
using global::VxFiles.Automation;
using global::VxFiles.Automation.Abstractions;

namespace Files.App.Helpers.Automation;

internal sealed class FilesAutomationHostContext : IAutomationHostContext, IDisposable
{
	private readonly IContentPageContext _context;
	private readonly DispatcherQueue _dispatcherQueue;
	private bool _disposed;

	public FilesAutomationHostContext(IContentPageContext context)
	{
		_context = context;
		_dispatcherQueue = MainWindow.Instance.DispatcherQueue;
		_context.PropertyChanged += Context_PropertyChanged;
	}

	public event EventHandler? LocationChanged;

	public string? CurrentFolderPath => _context.Folder?.ItemPath;

	public IReadOnlyList<AutomationHostItem> SelectedItems => _context.SelectedItems
		.Select(item => new AutomationHostItem(
			item.ItemPath,
			item.PrimaryItemAttribute is StorageItemTypes.Folder
				? SelectedPathKind.Folder
				: SelectedPathKind.File))
		.ToArray();

	public ValueTask RefreshCurrentFolderAsync(CancellationToken cancellationToken) =>
		EnqueueAsync(async () =>
		{
			if (_context.ShellPage is { } shellPage)
				await shellPage.Refresh_Click();
		}, cancellationToken);

	public ValueTask RevealDirectChildrenAsync(
		ImmutableArray<string> paths,
		CancellationToken cancellationToken) => EnqueueAsync(() =>
	{
		var shellPage = _context.ShellPage;
		if (shellPage?.SlimContentPage is not { } layout)
			return Task.CompletedTask;

		var requested = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var items = shellPage.ShellViewModel.FilesAndFolders
			.Where(item => requested.Contains(item.ItemPath))
			.ToList();
		if (items.Count is not 0)
		{
			layout.ItemManipulationModel.SetSelectedItems(items);
			layout.ItemManipulationModel.FocusSelectedItems();
		}
		return Task.CompletedTask;
	}, cancellationToken);

	public void Dispose()
	{
		if (_disposed)
			return;
		_context.PropertyChanged -= Context_PropertyChanged;
		_disposed = true;
	}

	private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(IContentPageContext.ShellPage)
			or nameof(IContentPageContext.Folder)
			or nameof(IContentPageContext.PageType))
		{
			LocationChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private ValueTask EnqueueAsync(Func<Task> operation, CancellationToken cancellationToken)
	{
		if (_dispatcherQueue.HasThreadAccess)
			return new(operation());

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcherQueue.TryEnqueue(async () =>
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				await operation();
				completion.SetResult();
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
		{
			completion.SetException(new InvalidOperationException("The Files window is no longer available."));
		}

		return new(completion.Task.WaitAsync(cancellationToken));
	}
}
