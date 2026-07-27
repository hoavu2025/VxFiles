// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class ToggleToolsPaneAction : ObservableObject, IAction
	{
		private readonly InfoPaneViewModel infoPaneViewModel = Ioc.Default.GetRequiredService<InfoPaneViewModel>();
		private readonly IInfoPaneSettingsService infoPaneSettingsService = Ioc.Default.GetRequiredService<IInfoPaneSettingsService>();

		public string Label
			=> Strings.ToggleToolsPane.GetLocalizedResource();

		public string Description
			=> Strings.ToggleToolsPaneDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Show;

		public RichGlyph Glyph
			=> new(themedIconStyle: "App.ThemedIcons.PanelRight");

		public bool IsAccessibleGlobally
			=> false;

		public bool IsExecutable
			=> infoPaneViewModel.IsEnabled;

		public ToggleToolsPaneAction()
		{
			infoPaneViewModel.PropertyChanged += ViewModel_PropertyChanged;
		}

		public Task ExecuteAsync(object? parameter = null)
		{
			infoPaneSettingsService.SelectedTab = InfoPaneTabs.Tools;

			return Task.CompletedTask;
		}

		private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(InfoPaneViewModel.IsEnabled))
				OnPropertyChanged(nameof(IsExecutable));
		}
	}
}
