// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using VxFiles.Automation.Abstractions;

namespace Files.App.Services.Automation
{
	/// <inheritdoc cref="IAutomationHostContext"/>
	public sealed partial class AutomationHostContext : ObservableObject, IAutomationHostContext
	{
		private readonly IContentPageContext _context;

		private long _revision;

		public AutomationHostContext()
		{
			_context = Ioc.Default.GetRequiredService<IContentPageContext>();
			_context.PropertyChanged += Context_PropertyChanged;
		}

		public HostRevision Revision => new(Interlocked.Read(ref _revision));

		public bool TryCapture([NotNullWhen(true)] out SelectionSnapshot? selection)
		{
			selection = null;

			// A shell location, a search result page, or Home has no directory an action could be pointed at.
			// Rejecting here rather than passing the text through keeps such paths out of a runner's argv.
			var folder = _context.Folder?.ItemPath;
			if (string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder))
				return false;

			selection = new(folder, DateTimeOffset.UtcNow, [.. _context.SelectedItems.Select(ToSelectedPath)]);
			return true;
		}

		private static SelectedPath ToSelectedPath(ListedItem item)
			=> new(
				item.ItemPath,
				item.IsFolder ? SelectedPathKind.Folder : SelectedPathKind.File,
				AutomationSelectionRules.ClassifyLocation(item.ItemPath));

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is not (
				nameof(IContentPageContext.ShellPage) or
				nameof(IContentPageContext.Folder) or
				nameof(IContentPageContext.SelectedItems)))
			{
				return;
			}

			Interlocked.Increment(ref _revision);
			OnPropertyChanged(nameof(Revision));
		}
	}
}
