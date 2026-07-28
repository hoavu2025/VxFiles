// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.IO;

namespace Files.App.Helpers.Application
{
	internal static class AppToastNotificationHelper
	{
		/// <summary>
		/// Registers the notification COM activator. Without package identity nothing is registered
		/// on our behalf, and every <see cref="AppNotificationManager.Show"/> call is dropped.
		/// </summary>
		public static void Register()
		{
			try
			{
				AppNotificationManager.Default.Register();
			}
			catch (Exception e)
			{
				App.Logger.LogWarning(e, "Failed to register for app notifications.");
			}
		}

		/// <summary>
		/// Releases the registration taken by <see cref="Register"/> during teardown.
		/// </summary>
		public static void Unregister()
		{
			try
			{
				AppNotificationManager.Default.Unregister();
			}
			catch (Exception e)
			{
				App.Logger.LogWarning(e, "Failed to unregister from app notifications.");
			}
		}

		// ms-appx URIs need package identity to resolve, so notification assets are addressed on disk.
		private static Uri GetAssetUri(string relativePath)
			=> new(Path.Combine(VxFilesEnvironment.InstallPath, relativePath));

		public static void ShowUnhandledExceptionToast()
		{
			var toastContent = new AppNotificationBuilder()
					.AddText(Strings.ExceptionNotificationHeader.GetLocalizedResource())
					.AddText(Strings.ExceptionNotificationBody.GetLocalizedResource())
					.SetAppLogoOverride(GetAssetUri(@"Assets\error.png"))
					.AddButton(new AppNotificationButton(Strings.ExceptionNotificationReportButton.GetLocalizedResource())
						.SetInvokeUri(new Uri(Constants.ExternalUrl.BugReportUrl)))
					.BuildNotification();
			AppNotificationManager.Default.Show(toastContent);
		}

		public static void ShowBackgroundRunningToast()
		{
			var toastContent = new AppNotificationBuilder()
				.AddText(Strings.BackgroundRunningNotificationHeader.GetLocalizedResource())
				.AddText(Strings.BackgroundRunningNotificationBody.GetLocalizedResource())
				.BuildNotification();
			AppNotificationManager.Default.Show(toastContent);
		}

		public static void ShowDriveEjectToast()
		{
			var toastContent = new AppNotificationBuilder()
				.AddText(Strings.EjectNotificationHeader.GetLocalizedResource())
				.AddText(Strings.EjectNotificationBody.GetLocalizedResource())
				.SetAttributionText("SettingsAboutAppName".GetLocalizedResource())
				.BuildNotification();
			AppNotificationManager.Default.Show(toastContent);
		}
	}
}
