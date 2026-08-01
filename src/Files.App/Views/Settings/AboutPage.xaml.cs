// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.Labs.WinUI.MarkdownTextBlock;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Files.App.Views.Settings
{
	public sealed partial class AboutPage : Page
	{
		public AboutPage()
		{
			InitializeComponent();

			UpdateNotesMarkdown.Config = MarkdownConfig.Default;
		}

		// A fresh card is built on every visit and it listens to a service that outlives the page, so both
		// exits have to dispose it: navigating to another settings page, and closing the settings tab.

		protected override void OnNavigatedFrom(NavigationEventArgs e)
		{
			ViewModel.UpdateCard.Dispose();

			base.OnNavigatedFrom(e);
		}

		private void Page_Unloaded(object sender, RoutedEventArgs e)
			=> ViewModel.UpdateCard.Dispose();
	}
}
