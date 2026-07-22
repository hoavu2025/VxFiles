// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.UserControls.Automation;

public sealed partial class AutomationBar : UserControl
{
	public AutomationBarViewModel ViewModel { get; }

	public string AutomationName => Strings.AutomationBar.GetLocalizedResource();

	public string CancelLabel => Strings.Cancel.GetLocalizedResource();

	public AutomationBar()
	{
		ViewModel = Ioc.Default.GetRequiredService<AutomationBarViewModel>();
		InitializeComponent();
	}

	public bool FocusFirstAction()
	{
		if (ActionScroller.FindDescendant<Button>() is not { } button)
			return false;
		return button.Focus(FocusState.Keyboard);
	}

	private async void AutomationBar_Loaded(object sender, RoutedEventArgs e) => await ViewModel.InitializeAsync();
}
