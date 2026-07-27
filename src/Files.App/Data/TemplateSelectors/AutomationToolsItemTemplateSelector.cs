// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Data.TemplateSelectors
{
	/// <summary>
	/// Chooses between the Automation Package root template and the Automation Action child template.
	/// </summary>
	/// <remarks>
	/// A TreeView applies one item template to every level, so the two row shapes have to be told apart here.
	/// </remarks>
	public sealed class AutomationToolsItemTemplateSelector : DataTemplateSelector
	{
		public DataTemplate? PackageTemplate { get; set; }

		public DataTemplate? ActionTemplate { get; set; }

		protected override DataTemplate? SelectTemplateCore(object item)
			=> item is AutomationPackageItem ? PackageTemplate : ActionTemplate;

		protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
			=> SelectTemplateCore(item);
	}
}
