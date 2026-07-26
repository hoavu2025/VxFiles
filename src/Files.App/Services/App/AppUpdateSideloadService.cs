// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace Files.App.Services
{
	public sealed partial class SideloadUpdateService : ObservableObject, IUpdateService, IDisposable
	{
		private const string RepositoryUrl = "https://github.com/hoavu2025/VxFiles";

		private readonly HttpClient _client = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(3) });
		private readonly ILogger? _logger = Ioc.Default.GetRequiredService<ILogger<App>>();
		private UpdateInfo? _pendingUpdate;

		private bool _isUpdateAvailable;
		public bool IsUpdateAvailable
		{
			get => _isUpdateAvailable;
			private set => SetProperty(ref _isUpdateAvailable, value);
		}

		private bool _isUpdating;
		public bool IsUpdating
		{
			get => _isUpdating;
			private set => SetProperty(ref _isUpdating, value);
		}

		private int _updateProgress;
		public int UpdateProgress
		{
			get => _updateProgress;
			private set => SetProperty(ref _updateProgress, value);
		}

		public bool IsAppUpdated => AppLifecycleHelper.IsAppUpdated;

		private bool _areReleaseNotesAvailable;
		public bool AreReleaseNotesAvailable
		{
			get => _areReleaseNotesAvailable;
			private set => SetProperty(ref _areReleaseNotesAvailable, value);
		}

		public async Task DownloadUpdatesAsync()
		{
			if (_pendingUpdate is null)
				return;

			IsUpdating = true;
			try
			{
				var updateManager = CreateUpdateManager();
				await updateManager.DownloadUpdatesAsync(_pendingUpdate, progress =>
				{
					MainWindow.Instance.DispatcherQueue.TryEnqueue(() => UpdateProgress = progress);
				});

				App.AppModel.ForceProcessTermination = true;
				updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to download or apply the VxFiles update");
			}
			finally
			{
				IsUpdating = false;
				UpdateProgress = 0;
			}
		}

		public Task DownloadMandatoryUpdatesAsync()
			=> Task.CompletedTask;

		public async Task CheckForUpdatesAsync()
		{
			IsUpdateAvailable = false;
			_pendingUpdate = null;

			try
			{
				var updateManager = CreateUpdateManager();
				if (!updateManager.IsInstalled)
				{
					_logger?.LogInformation("Skipping update check because VxFiles is not installed by Velopack");
					return;
				}

				_pendingUpdate = await updateManager.CheckForUpdatesAsync();
				IsUpdateAvailable = _pendingUpdate is not null;
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Failed to check GitHub Releases for VxFiles updates");
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

		private static UpdateManager CreateUpdateManager()
			=> new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
	}
}
