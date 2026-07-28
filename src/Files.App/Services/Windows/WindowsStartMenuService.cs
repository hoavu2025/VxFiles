// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Windows.UI.StartScreen;

namespace Files.App.Services
{
	/// <inheritdoc cref="IStartMenuService"/>
	internal sealed class StartMenuService : IStartMenuService
	{
		// Secondary tiles need package identity. Without it the API throws rather than
		// reporting "not pinned", and an escaped throw empties whichever menu asked.
		private static readonly bool secondaryTilesSupported = VxFilesEnvironment.SupportsWindowsIntegration;

		[Obsolete("See IStartMenuService for further information.")]
		public bool IsPinned(string itemPath)
		{
			return TileExists(GetNativeTileId(itemPath));
		}

		/// <inheritdoc/>
		public Task<bool> IsPinnedAsync(IStorable storable)
		{
			return Task.FromResult(TileExists(GetNativeTileId(storable.Id)));
		}

		/// <inheritdoc/>
		public async Task PinAsync(IStorable storable, string? displayName = null)
		{
			if (!secondaryTilesSupported)
				return;

			var tileId = GetNativeTileId(storable.Id);
			displayName ??= storable.Name;

			try
			{
				var path150x150 = new Uri("ms-appx:///Assets/tile-0-300x300.png");
				var path71x71 = new Uri("ms-appx:///Assets/tile-0-250x250.png");

				var tile = new SecondaryTile(
					tileId,
					displayName,
					storable.Id,
					path150x150,
					TileSize.Square150x150)
				{
					VisualElements =
					{
						Square71x71Logo = path71x71,
						ShowNameOnSquare150x150Logo = true
					}
				};

				WinRT.Interop.InitializeWithWindow.Initialize(tile, MainWindow.Instance.WindowHandle);

				await tile.RequestCreateAsync();
			}
			catch (Exception e)
			{
				App.Logger.LogWarning(e, $"Failed to pin the Start menu tile {tileId}.");
			}

		}

		/// <inheritdoc/>
		public async Task UnpinAsync(IStorable storable)
		{
			if (!secondaryTilesSupported)
				return;

			var tileId = GetNativeTileId(storable.Id);

			try
			{
				var startScreen = StartScreenManager.GetDefault();

				await startScreen.TryRemoveSecondaryTileAsync(tileId);
			}
			catch (Exception e)
			{
				App.Logger.LogWarning(e, $"Failed to unpin the Start menu tile {tileId}.");
			}
		}

		private static bool TileExists(string tileId)
		{
			if (!secondaryTilesSupported)
				return false;

			try
			{
				return SecondaryTile.Exists(tileId);
			}
			catch (Exception e)
			{
				App.Logger.LogWarning(e, $"Failed to check the Start menu tile {tileId}.");

				return false;
			}
		}

		private static string GetNativeTileId(string id)
		{
			// Remove symbols because windows doesn't like them in the ID, and will blow up
			var str = $"folder-{new string(id.Where(c => char.IsLetterOrDigit(c)).ToArray())}";

			// If the id string is too long, Windows will throw an error, so remove every other character
			if (str.Length > 64)
				str = new string(str.Where((_, i) => i % 2 == 0).ToArray());

			return str;
		}
	}
}
