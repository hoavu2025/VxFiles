// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.UserControls
{
	/// <summary>
	/// The Tools tab of the Info Pane: a filterable tree of Automation Packages and their Automation Actions.
	/// </summary>
	public sealed partial class AutomationToolsPane : UserControl
	{
		public AutomationToolsViewModel ViewModel { get; }

		public AutomationToolsPane()
		{
			ViewModel = Ioc.Default.GetRequiredService<AutomationToolsViewModel>();
			InitializeComponent();
		}

		/// <summary>
		/// The Info Pane defers creating this control until Tools is first selected, so loading here is what
		/// makes the headless session lazy.
		/// </summary>
		private async void Root_Loaded(object sender, RoutedEventArgs e)
			=> await ViewModel.EnsureLoadedAsync();
	}
}
