// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using Files.App.Services.DateTimeFormatter;
using Microsoft.UI.Xaml;
using System.Windows.Input;
using ByteSize = ByteSizeLib.ByteSize;

namespace Files.App.ViewModels.Settings
{
	/// <summary>
	/// Represents the update half of the App Info card on <see cref="Views.Settings.AboutPage"/>.
	/// </summary>
	/// <remarks>
	/// Lives beside <see cref="AboutViewModel"/> rather than inside it so that the whole update surface is a
	/// VxFiles-owned file, and the inherited About view model gains one property instead of a dozen.
	/// <para>
	/// It informs and never gates. A staged update installs on the next exit whether or not anyone reads this,
	/// so nothing here can skip, defer, or decline one — the button only decides <em>when</em>.
	/// </para>
	/// </remarks>
	public sealed partial class UpdateCardViewModel : ObservableObject, IDisposable
	{
		// Dependency injections

		/// <summary>
		/// Null in Store and dev builds, where nothing can be staged and the card must say nothing about updates.
		/// </summary>
		private IUpdateStatusService? UpdateStatusService { get; } = Ioc.Default.GetService<IUpdateStatusService>();

		private IDateTimeFormatter DateTimeFormatter { get; } = Ioc.Default.GetRequiredService<IDateTimeFormatter>();

		private readonly string _installedVersionLabel;


		// Properties
		//
		// All computed from the single status the service holds, so nothing here can drift out of step with
		// what the updater is actually doing.

		private UpdateStatus? State
			=> UpdateStatusService?.Status;

		private string AppName
			=> VxFilesEnvironment.DisplayName;

		/// <summary>
		/// Whether there is an updater to report on at all. Where there is not, the App Info card keeps its
		/// inherited shape and simply carries no update button.
		/// </summary>
		public bool IsVisible
			=> UpdateStatusService is not null && State is not UpdateStatus.Unsupported;

		/// <summary>
		/// The App Info card's description: the version that is running, then what the updater has to say about
		/// it. One line, because the installed version and the one waiting to replace it are one subject.
		/// </summary>
		public string Summary
			=> IsVisible
				? $"{_installedVersionLabel}  ·  {StatusLabel}"
				: _installedVersionLabel;

		private string StatusLabel
			=> State switch
			{
				UpdateStatus.Checking => Strings.UpdateChecking.GetLocalizedResource(),

				UpdateStatus.Downloading downloading => string.Join(
					" — ",
					string.Format(Strings.UpdateDownloading.GetLocalizedResource(), AppName, downloading.Version),
					string.Format(
						Strings.UpdateDownloadProgress.GetLocalizedResource(),
						downloading.Percent,
						ByteSize.FromBytes(downloading.Bytes).ToSizeString())),

				UpdateStatus.Ready ready => string.Format(Strings.UpdateReady.GetLocalizedResource(), AppName, ready.Version),

				// A failure reports both what went wrong and how stale the last real answer is, because either
				// on its own reads as a passing glitch.
				UpdateStatus.Failed { Reason: UpdateFailure.Offline } => $"{Strings.UpdateFailedOffline.GetLocalizedResource()} — {LastCheckedLabel}",
				UpdateStatus.Failed { Reason: UpdateFailure.RateLimited } => $"{Strings.UpdateFailedRateLimited.GetLocalizedResource()} — {LastCheckedLabel}",
				UpdateStatus.Failed => $"{Strings.UpdateFailedUnknown.GetLocalizedResource()} — {LastCheckedLabel}",

				_ => LastCheckedLabel,
			};

		/// <summary>
		/// The part that is easy to dismiss as decoration: an updater that has been failing for a week says so
		/// here, and this fork carries no telemetry that would say it anywhere else.
		/// </summary>
		private string LastCheckedLabel
			=> UpdateStatusService?.LastSuccessfulCheck is { } lastChecked
				? string.Format(Strings.UpdateLastChecked.GetLocalizedResource(), DateTimeFormatter.ToLongLabel(lastChecked))
				: Strings.UpdateNeverChecked.GetLocalizedResource();

		/// <summary>
		/// Shows a progress ring beside the button, and disables it, while a check or a download is running.
		/// </summary>
		public bool IsBusy
			=> State is UpdateStatus.Checking or UpdateStatus.Downloading;

		public string ActionText
			=> State switch
			{
				UpdateStatus.Ready => Strings.UpdateRestartNow.GetLocalizedResource(),
				UpdateStatus.Failed { Reason: not UpdateFailure.RateLimited } => Strings.UpdateTryAgain.GetLocalizedResource(),
				_ => Strings.UpdateCheckNow.GetLocalizedResource(),
			};

		/// <summary>
		/// Disabled only while something is already running. A rate-limited check still leaves the button live,
		/// because refusing to try is worse than spending one of the hour's requests.
		/// </summary>
		public bool IsActionEnabled
			=> !IsBusy;

		/// <summary>
		/// Accented once there is something to install, plain otherwise.
		/// </summary>
		/// <remarks>
		/// Never null. Binding a control's Style to null is not the same as leaving it unset — it counts as a
		/// local value and suppresses the implicit default style, so the button would lose its template and
		/// render bare next to the Copy button beside it.
		/// </remarks>
		public Style? ActionStyle
			=> Application.Current.Resources.TryGetValue(
				State is UpdateStatus.Ready ? "AccentButtonStyle" : "DefaultButtonStyle",
				out var style)
					? style as Style
					: null;

		/// <summary>
		/// Notes for the release that is waiting, not the one that is running. The version in
		/// <see cref="Summary"/> is the installed one, and the two must not be conflated.
		/// </summary>
		public string? Notes
			=> (State as UpdateStatus.Ready)?.NotesMarkdown;

		public bool AreNotesVisible
			=> !string.IsNullOrWhiteSpace(Notes);


		// Commands

		/// <summary>
		/// Restarts into a staged update, or runs a fresh check when there is nothing staged yet.
		/// </summary>
		public ICommand ActionCommand { get; }


		// Constructor

		/// <param name="installedVersionLabel">
		/// How the About page names the running version. Passed in rather than rebuilt here so there is one
		/// place that decides what a version looks like.
		/// </param>
		public UpdateCardViewModel(string installedVersionLabel)
		{
			_installedVersionLabel = installedVersionLabel;
			ActionCommand = new AsyncRelayCommand(RunActionAsync);

			if (UpdateStatusService is not null)
				UpdateStatusService.PropertyChanged += UpdateStatusService_PropertyChanged;
		}


		// Methods

		/// <summary>
		/// Detaches from the service singleton. The About page builds a new card on every visit, so skipping
		/// this would leak one handler per visit for the life of the process.
		/// </summary>
		public void Dispose()
		{
			if (UpdateStatusService is not null)
				UpdateStatusService.PropertyChanged -= UpdateStatusService_PropertyChanged;
		}

		private void UpdateStatusService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			// The whole card derives from one status, so it is redrawn as a unit rather than tracking which of
			// these the change happened to touch.
			OnPropertyChanged(nameof(IsVisible));
			OnPropertyChanged(nameof(Summary));
			OnPropertyChanged(nameof(IsBusy));
			OnPropertyChanged(nameof(ActionText));
			OnPropertyChanged(nameof(IsActionEnabled));
			OnPropertyChanged(nameof(ActionStyle));
			OnPropertyChanged(nameof(Notes));
			OnPropertyChanged(nameof(AreNotesVisible));
		}

		private async Task RunActionAsync()
		{
			if (UpdateStatusService is null)
				return;

			if (UpdateStatusService.Status is UpdateStatus.Ready)
				UpdateStatusService.RestartToUpdate();
			else
				await UpdateStatusService.CheckAsync();
		}
	}
}
