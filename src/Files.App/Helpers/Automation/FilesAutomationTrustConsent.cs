// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using global::VxFiles.Automation.Abstractions;

namespace Files.App.Helpers.Automation;

public sealed class FilesAutomationTrustConsent : IAutomationTrustConsent
{
	public async ValueTask<bool> RequestTrustAsync(
		AutomationTrustRequest request,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var dialog = new ContentDialog
		{
			XamlRoot = MainWindow.Instance.Content.XamlRoot,
			Title = Strings.AutomationTrustTitle.GetLocalizedResource(),
			Content = string.Format(
				Strings.AutomationTrustDescription.GetLocalizedResource(),
				request.DisplayName,
				request.SelectedItemCount,
				request.PackagePath),
			PrimaryButtonText = Strings.AutomationTrustRun.GetLocalizedResource(),
			CloseButtonText = Strings.Cancel.GetLocalizedResource(),
			DefaultButton = ContentDialogButton.Close,
		};

		return await dialog.TryShowAsync() is ContentDialogResult.Primary;
	}
}
