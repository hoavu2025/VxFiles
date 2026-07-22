// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.App.Views
{
	/// <summary>
	/// Display the app splash screen.
	/// </summary>
	public sealed partial class SplashScreenPage : Page
	{
		private string AppName => VxFilesEnvironment.DisplayName;
		private BitmapImage SplashScreenImageSource { get; } = new(new Uri(SystemIO.Path.Combine(
			VxFilesEnvironment.InstallPath,
			"Assets",
			"AppTiles",
			"Dev",
			"SplashScreen.scale-200.png")));

		private string BranchLabel =>
			AppLifecycleHelper.AppEnvironment switch
			{
				AppEnvironment.Dev => "Dev",
				AppEnvironment.SideloadPreview or AppEnvironment.StorePreview => "Preview",
				_ => string.Empty,
			};

		public SplashScreenPage()
		{
			InitializeComponent();
		}

		private void Image_ImageOpened(object sender, RoutedEventArgs e)
		{
			App.SetSplashScreenImageResult(true);
			App.SplashScreenLoadingTCS?.TrySetResult();
		}

		private void Image_ImageFailed(object sender, RoutedEventArgs e)
		{
			App.SetSplashScreenImageResult(false);
			App.SplashScreenLoadingTCS?.TrySetResult();
		}
	}
}
