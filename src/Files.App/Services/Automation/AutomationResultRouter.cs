// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using System.Collections.Immutable;
using System.IO;
using VxFiles.Automation.Abstractions;

namespace Files.App.Services.Automation
{
	/// <summary>
	/// Applies the effects an Automation Action asked for once its run completed.
	/// </summary>
	/// <remarks>
	/// An action never touches the shell; it declares intent and this decides whether to honour it. Every intent
	/// is checked against the folder the run was captured in, so a run that finishes after the user has navigated
	/// elsewhere reports what it wanted rather than acting on a folder it never saw.
	/// </remarks>
	public sealed class AutomationResultRouter : IAutomationResultRouter
	{
		private readonly IContentPageContext _context = Ioc.Default.GetRequiredService<IContentPageContext>();

		public async ValueTask<ImmutableArray<AutomationIntentResult>> RouteAsync(
			AutomationResultRoutingRequest request,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			if (request.Intents.IsEmpty)
				return [];

			// Runs complete on a background thread; everything below reads or drives the shell.
			return await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => Route(request));
		}

		private ImmutableArray<AutomationIntentResult> Route(AutomationResultRoutingRequest request)
		{
			if (!IsStillActiveFolder(request.CapturedFolderPath))
			{
				var stale = Strings.AutomationToolsResultStale.GetLocalizedResource();
				return [.. request.Intents.Select(intent => new AutomationIntentResult(intent, AutomationIntentDisposition.Stale, stale))];
			}

			var results = ImmutableArray.CreateBuilder<AutomationIntentResult>(request.Intents.Length);
			foreach (var intent in request.Intents)
			{
				results.Add(intent switch
				{
					AutomationResultIntent.RefreshCurrentFolder => Refresh(intent),
					AutomationResultIntent.RevealPaths reveal => Reveal(request.CapturedFolderPath, reveal),
					_ => new(intent, AutomationIntentDisposition.Rejected, Strings.AutomationToolsResultUnsupported.GetLocalizedResource()),
				});
			}

			return results.ToImmutable();
		}

		private bool IsStillActiveFolder(string capturedFolderPath)
			=> _context.Folder?.ItemPath is { } current && IsSameFolder(current, capturedFolderPath);

		private AutomationIntentResult Refresh(AutomationResultIntent intent)
		{
			if (_context.ShellPage is not { } shellPage)
				return new(intent, AutomationIntentDisposition.Rejected, Strings.AutomationToolsResultNoFolder.GetLocalizedResource());

			_ = shellPage.Refresh_Click();
			return new(intent, AutomationIntentDisposition.Applied, Strings.AutomationToolsResultRefreshed.GetLocalizedResource());
		}

		/// <summary>
		/// Selects the named items in the folder the run was captured in.
		/// </summary>
		/// <remarks>
		/// Revealing is deliberately confined to that folder. Honouring a path elsewhere would navigate the user
		/// away from where they pressed Run, on the say-so of a script's output.
		/// </remarks>
		private AutomationIntentResult Reveal(string capturedFolderPath, AutomationResultIntent.RevealPaths reveal)
		{
			if (_context.ShellPage?.SlimContentPage?.ItemManipulationModel is not { } manipulation)
				return new(reveal, AutomationIntentDisposition.Rejected, Strings.AutomationToolsResultNoFolder.GetLocalizedResource());

			// Matched on the full path rather than the display name: Name hides the extension when the user has
			// turned extensions off, which would make "report.txt" and a folder named "report" indistinguishable.
			var paths = reveal.Paths
				.Where(path => Path.IsPathFullyQualified(path) && IsDirectChild(capturedFolderPath, path))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			if (paths.Count is 0)
				return new(reveal, AutomationIntentDisposition.Rejected, Strings.AutomationToolsResultOutsideFolder.GetLocalizedResource());

			var items = _context.ShellPage.ShellViewModel.FilesAndFolders
				.Where(item => paths.Contains(item.ItemPath))
				.ToList();
			if (items.Count is 0)
				return new(reveal, AutomationIntentDisposition.Rejected, Strings.AutomationToolsResultNotListed.GetLocalizedResource());

			manipulation.SetSelectedItems(items);
			manipulation.ScrollIntoView(items[0]);
			return new(
				reveal,
				AutomationIntentDisposition.Applied,
				string.Format(Strings.AutomationToolsResultRevealed.GetLocalizedResource(), items.Count));
		}

		private static bool IsDirectChild(string folderPath, string path)
			=> Path.GetDirectoryName(path) is { } parent && IsSameFolder(parent, folderPath);

		/// <summary>
		/// Compares two folder paths as the shell does: case-insensitively, and ignoring a trailing separator,
		/// which a drive root carries and a subfolder does not.
		/// </summary>
		private static bool IsSameFolder(string left, string right)
			=> string.Equals(
				left.TrimEnd(Path.DirectorySeparatorChar),
				right.TrimEnd(Path.DirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase);
	}
}
