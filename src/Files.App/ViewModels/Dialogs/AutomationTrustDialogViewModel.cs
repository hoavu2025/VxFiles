// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.ViewModels.Dialogs
{
	/// <summary>
	/// What the user is agreeing to when they trust an Automation Package.
	/// </summary>
	/// <remarks>
	/// Trust is granted to the whole package, not to the one action that triggered the prompt, so every action the
	/// package contains is listed. Approving on the strength of a single harmless-looking action would otherwise
	/// silently admit the rest.
	/// </remarks>
	public sealed class AutomationTrustDialogViewModel
	{
		public AutomationTrustDialogViewModel(AutomationTrustRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			DisplayName = request.DisplayName;
			PackageId = request.PackageId.Value;
			PackageVersion = request.PackageVersion;
			PackagePath = request.PackagePath;
			RequestedAction = request.RequestedAction.Value;
			TrustFingerprint = request.TrustFingerprint;

			Actions = string.Join(
				Environment.NewLine,
				request.Actions.Select(action => action.LocalId.Value).Order(StringComparer.Ordinal));

			SelectionSummary = string.Format(
				Strings.AutomationTrustSelectionSummary.GetLocalizedResource(),
				request.SelectedItemCount);

			// A package that resolves an external executable is the sharper edge of this consent: the path is
			// what the user is really being asked about, so it is shown rather than just the tool's name.
			ExternalTools = string.Join(
				Environment.NewLine,
				request.ExternalTools.Select(tool => $"{tool.Id} — {tool.ExecutablePath}"));

			HasExternalTools = ExternalTools.Length is not 0;
		}

		public string DisplayName { get; }

		public string PackageId { get; }

		public string PackageVersion { get; }

		public string PackagePath { get; }

		public string RequestedAction { get; }

		public string Actions { get; }

		public string SelectionSummary { get; }

		public string ExternalTools { get; }

		public bool HasExternalTools { get; }

		/// <summary>
		/// Gets the fingerprint this consent is recorded against. Shown so a repeat prompt for an apparently
		/// unchanged package is explicable: the content it covers moved.
		/// </summary>
		public string TrustFingerprint { get; }
	}
}
