// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Dialogs
{
	/// <summary>
	/// Package-level consent shown before an Automation Package runs for the first time, and again whenever the
	/// content that consent covers changes.
	/// </summary>
	public sealed partial class AutomationTrustDialog : ContentDialog
	{
		private FrameworkElement RootAppElement
			=> (FrameworkElement)MainWindow.Instance.Content;

		public AutomationTrustDialogViewModel ViewModel
		{
			get => (AutomationTrustDialogViewModel)DataContext;
			set => DataContext = value;
		}

		public AutomationTrustDialog()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Shows the prompt and reports whether the user accepted. Anything other than the primary button —
		/// Cancel, Escape, or a dialog that could not be shown — is a refusal.
		/// </summary>
		public async Task<bool> RequestAsync()
			=> await this.TryShowAsync() is ContentDialogResult.Primary;
	}
}
