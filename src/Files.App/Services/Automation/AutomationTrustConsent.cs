// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Files.App.Dialogs;
using Microsoft.Extensions.Logging;
using VxFiles.Automation.Abstractions;

namespace Files.App.Services.Automation
{
	/// <summary>
	/// Asks the user to trust an Automation Package before it is allowed to run.
	/// </summary>
	/// <remarks>
	/// The session calls this from the invocation's background thread, so the prompt is marshalled onto the UI
	/// thread. Every path that is not an explicit acceptance — a refusal, a dialog that could not be shown, or a
	/// failure raising it at all — denies trust, so nothing runs by default.
	/// </remarks>
	public sealed class AutomationTrustConsent : IAutomationTrustConsent
	{
		public async ValueTask<bool> RequestTrustAsync(
			AutomationTrustRequest request,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			try
			{
				return await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					var dialog = new AutomationTrustDialog { ViewModel = new(request) };
					return await dialog.RequestAsync();
				});
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Automation trust consent could not be shown for '{PackageId}'", request.PackageId.Value);
				return false;
			}
		}
	}
}
