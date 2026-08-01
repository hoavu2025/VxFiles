// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace Files.App.Services
{
	public sealed partial class SideloadUpdateService : ObservableObject, IUpdateService, IUpdateStatusService, IDisposable
	{
		private const string RepositoryUrl = "https://github.com/hoa-d-vu-vgames/VxFiles";
		private const string LastSuccessfulCheckKey = "UPDATE_LAST_SUCCESSFUL_CHECK";

		private readonly HttpClient _client = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(3) });
		private readonly ILogger? _logger = Ioc.Default.GetRequiredService<ILogger<App>>();
		private readonly Lock _checkLock = new();

		private UpdateInfo? _pendingUpdate;
		private Task? _inFlightCheck;

		// A check runs at launch, so this is what the surface reports for the moment before the first
		// answer arrives. Claiming to be up to date before ever asking would be a lie.
		private UpdateStatus _status = new UpdateStatus.Checking();
		public UpdateStatus Status
		{
			get => _status;
			private set => ApplyStatus(value);
		}

		private DateTimeOffset? _lastSuccessfulCheck;
		public DateTimeOffset? LastSuccessfulCheck
		{
			get => _lastSuccessfulCheck;
			private set
			{
				if (_lastSuccessfulCheck == value)
					return;

				_lastSuccessfulCheck = value;
				Post(() => OnPropertyChanged(nameof(LastSuccessfulCheck)));
			}
		}

		// The inherited surface, computed from Status so there is one source of truth and nothing upstream
		// has to change to read it.

		public bool IsUpdateAvailable => Status is UpdateStatus.Ready;

		public bool IsUpdating => Status is UpdateStatus.Downloading;

		public int UpdateProgress => Status is UpdateStatus.Downloading downloading ? downloading.Percent : 0;

		public bool IsAppUpdated => AppLifecycleHelper.IsAppUpdated;

		private bool _areReleaseNotesAvailable;
		public bool AreReleaseNotesAvailable
		{
			get => _areReleaseNotesAvailable;
			private set => SetProperty(ref _areReleaseNotesAvailable, value);
		}

		public SideloadUpdateService()
		{
			_lastSuccessfulCheck = VxFilesEnvironment.GetState<DateTimeOffset?>(LastSuccessfulCheckKey, null);
		}

		/// <summary>
		/// Takes the staged update immediately, downloading it first if the background cycle has not finished.
		/// </summary>
		/// <remarks>
		/// This is what the address bar's update button calls. It differs from the automatic path only in
		/// that the app comes back afterwards.
		/// </remarks>
		public async Task DownloadUpdatesAsync()
		{
			if (Status is not UpdateStatus.Ready)
				await CheckAsync();

			if (Status is UpdateStatus.Ready)
				RestartToUpdate();
		}

		/// <remarks>
		/// The check and the download are one cycle here, so this awaits whatever <see cref="CheckForUpdatesAsync"/>
		/// started rather than opening a second one. Callers that invoke both in sequence, as
		/// <see cref="AppLifecycleHelper.CheckAppUpdate"/> does, therefore spend one GitHub request and not two.
		/// </remarks>
		public Task DownloadMandatoryUpdatesAsync()
			=> _inFlightCheck ?? Task.CompletedTask;

		public void ApplyPendingUpdateOnExit()
		{
			try
			{
				var updateManager = CreateUpdateManager();
				if (updateManager.UpdatePendingRestart is not { } stagedUpdate)
					return;

				// The updater waits only 60 seconds for this process to exit, so this must run during
				// teardown rather than when the download finishes.
				updateManager.WaitExitThenApplyUpdates(stagedUpdate, silent: true, restart: false);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to stage the VxFiles update for install on exit");
			}
		}

		public Task CheckForUpdatesAsync()
			=> CheckAsync();

		public Task CheckAsync()
		{
			lock (_checkLock)
			{
				if (_inFlightCheck is { IsCompleted: false })
					return _inFlightCheck;

				return _inFlightCheck = RunCheckCycleAsync();
			}
		}

		public void RestartToUpdate()
		{
			if (Status is not UpdateStatus.Ready)
				return;

			try
			{
				var updateManager = CreateUpdateManager();

				// Only ever arm the updater with what Velopack itself reports as staged. Falling back to the
				// release the last check found would point it at bytes that may not be on disk.
				if (updateManager.UpdatePendingRestart is not { } stagedUpdate)
				{
					_logger?.LogWarning("Refusing to restart into an update that Velopack does not report as staged");
					return;
				}

				// The update restart is the only self-restart in the app, so nothing else would bring the
				// tabs back.
				Ioc.Default.GetRequiredService<IUserSettingsService>().AppSettingsService.RestoreTabsOnStartup = true;

				// Arms a detached updater and returns. The install happens once this process is gone, which
				// is why the ordinary shutdown below is allowed to run to completion first.
				updateManager.WaitExitThenApplyUpdates(stagedUpdate, silent: true, restart: true);

				Post(() =>
				{
					// Without this the close is diverted to the tray, the process survives, and the update
					// never applies.
					App.AppModel.ForceProcessTermination = true;
					MainWindow.Instance.Close();
				});
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to restart VxFiles into the staged update");
			}
		}

		public async Task CheckForReleaseNotesAsync()
		{
			try
			{
				var response = await _client.GetAsync(Constants.ExternalUrl.ReleaseNotesUrl);
				AreReleaseNotesAvailable = response.IsSuccessStatusCode;
			}
			catch
			{
				AreReleaseNotesAvailable = false;
			}
		}

		public Task CheckAndUpdateFilesLauncherAsync()
			=> Task.CompletedTask;

		public void Dispose()
			=> _client.Dispose();

		/// <summary>
		/// Asks GitHub what the newest release is and, if it is newer than this one, downloads and stages it.
		/// </summary>
		private async Task RunCheckCycleAsync()
		{
			UpdateManager updateManager;
			try
			{
				updateManager = CreateUpdateManager();
				if (!updateManager.IsInstalled)
				{
					_logger?.LogInformation("Skipping update check because VxFiles is not installed by Velopack");
					Status = new UpdateStatus.Unsupported();
					return;
				}
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to create the VxFiles update manager");
				Status = new UpdateStatus.Unsupported();
				return;
			}

			UpdateInfo? found;
			try
			{
				if (Status is not (UpdateStatus.Ready or UpdateStatus.Downloading))
					Status = new UpdateStatus.Checking();

				found = await updateManager.CheckForUpdatesAsync();
				LastSuccessfulCheck = DateTimeOffset.Now;
				VxFilesEnvironment.SetState<DateTimeOffset?>(LastSuccessfulCheckKey, LastSuccessfulCheck);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to check GitHub Releases for VxFiles updates");

				// A failed poll must not discard an update that is already staged.
				if (Status is not (UpdateStatus.Ready or UpdateStatus.Downloading))
					Status = new UpdateStatus.Failed(Classify(ex));

				return;
			}

			if (found is null)
			{
				_pendingUpdate = null;
				Status = new UpdateStatus.UpToDate();
				return;
			}

			_pendingUpdate = found;

			var release = found.TargetFullRelease;
			var version = release.Version.ToString();
			var size = release.Size;

			// A release downloaded by an earlier session is still staged, so downloading it again would only
			// re-fetch bytes that are already on disk.
			if (updateManager.UpdatePendingRestart is not null)
			{
				Status = new UpdateStatus.Ready(version, release.NotesMarkdown, size);
				return;
			}

			Status = new UpdateStatus.Downloading(version, 0, size);

			try
			{
				await updateManager.DownloadUpdatesAsync(found, percent =>
					Post(() =>
					{
						if (Status is UpdateStatus.Downloading)
							Status = new UpdateStatus.Downloading(version, percent, size);
					}));

				Status = new UpdateStatus.Ready(version, release.NotesMarkdown, size);
			}
			catch (Exception ex)
			{
				// The check itself succeeded here, so this is the one case where Failed does replace
				// Downloading — the alternative is a progress bar that never finishes and never explains.
				_logger?.LogError(ex, "Failed to download the VxFiles update in the background");
				Status = new UpdateStatus.Failed(Classify(ex));
			}
		}

		/// <summary>
		/// Records a new status and tells every projection of it, on the UI thread, that it changed.
		/// </summary>
		/// <remarks>
		/// The field is written on the caller's thread so that the cycle below always reads back what it just
		/// wrote; only the notification is marshalled.
		/// </remarks>
		private void ApplyStatus(UpdateStatus status)
		{
			// Records compare by value, so an unchanged download percentage raises nothing.
			if (_status == status)
				return;

			_status = status;

			Post(() =>
			{
				OnPropertyChanged(nameof(Status));
				OnPropertyChanged(nameof(IsUpdateAvailable));
				OnPropertyChanged(nameof(IsUpdating));
				OnPropertyChanged(nameof(UpdateProgress));
			});
		}

		private static UpdateFailure Classify(Exception exception)
		{
			for (var current = exception; current is not null; current = current.InnerException)
			{
				if (current is HttpRequestException httpException)
				{
					return httpException.StatusCode switch
					{
						HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => UpdateFailure.RateLimited,

						// No status code at all means the request never reached a server.
						null => UpdateFailure.Offline,
						_ => UpdateFailure.Unknown,
					};
				}
			}

			return UpdateFailure.Unknown;
		}

		private static void Post(Action action)
		{
			var dispatcherQueue = MainWindow.Instance.DispatcherQueue;

			if (dispatcherQueue.HasThreadAccess)
				action();
			else
				dispatcherQueue.TryEnqueue(() => action());
		}

		private static UpdateManager CreateUpdateManager()
			=> new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
	}
}
