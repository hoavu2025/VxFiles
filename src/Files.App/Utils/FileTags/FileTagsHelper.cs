// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Win32;
using IO = System.IO;

namespace Files.App.Utils.FileTags
{

	public static class FileTagsHelper
	{
		private static readonly Lazy<FileTagsDatabase> dbInstance = new(() => new());

		public static FileTagsDatabase GetDbInstance() => dbInstance.Value;

		public static string[] ReadFileTag(string filePath)
		{
			var tagString = Win32Helper.ReadStringFromFile($"{filePath}:files");
			return tagString?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
		}

		public static async void WriteFileTag(string filePath, string[] tag)
		{
			await WriteFileTagAsync(filePath, tag);
		}

		public static async void SetFileTags(string filePath, ulong? frn, string[] tags)
		{
			await SetFileTagsAsync(filePath, frn, tags);
		}

		public static async Task<bool> SetFileTagsAsync(string filePath, ulong? frn, string[] tags)
		{
			if (TrySetFileTags(filePath, frn, tags))
				return true;

			await ShowWriteErrorAsync();
			return false;
		}

		public static bool TrySetFileTags(string filePath, ulong? frn, string[] tags)
		{
			try
			{
				var previousTags = ReadFileTag(filePath);
				bool adsWritten = TryWriteFileTag(filePath, tags);

				if (GetDbInstance().SetTags(filePath, frn, tags))
					return true;

				// If authoritative DB write failed, restore previous ADS tags to maintain consistency
				if (adsWritten)
				{
					if (!TryWriteFileTag(filePath, previousTags))
						App.Logger?.LogError("Failed to restore file tags ADS for {FilePath} after a database write failure.", filePath);
				}

				return false;
			}
			catch (Exception ex)
			{
				App.Logger?.LogError(ex, "Failed to update file tags for {FilePath}.", filePath);
				return false;
			}
		}

		public static async Task<bool> WriteFileTagAsync(string filePath, string[] tag)
		{
			if (TryWriteFileTag(filePath, tag))
				return true;

			await ShowWriteErrorAsync();
			return false;
		}

		private static async Task ShowWriteErrorAsync()
		{
			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				ContentDialog dialog = new()
				{
					Title = Strings.ErrorApplyingTagTitle.GetLocalizedResource(),
					Content = Strings.ErrorApplyingTagContent.GetLocalizedResource(),
					PrimaryButtonText = "Ok".GetLocalizedResource()
				};

				if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
					dialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

				await dialog.TryShowAsync();
			});
		}

		public static bool TryWriteFileTag(string filePath, string[] tag)
		{
			var isDateOk = Win32Helper.GetFileDateModified(filePath, out var dateModified); // Backup date modified
			var isReadOnly = Win32Helper.HasFileAttribute(filePath, IO.FileAttributes.ReadOnly);
			try
			{
				if (isReadOnly) // Unset read-only attribute (#7534)
				{
					Win32Helper.UnsetFileAttribute(filePath, IO.FileAttributes.ReadOnly);
				}

				if (!tag.Any())
					return PInvoke.DeleteFileFromApp($"{filePath}:files") || !IO.File.Exists($"{filePath}:files");

				if (ReadFileTag(filePath) is string[] existingTags && tag.SequenceEqual(existingTags))
					return true;

				return Win32Helper.WriteStringToFile($"{filePath}:files", string.Join(',', tag));
			}
			catch (Exception ex)
			{
				App.Logger?.LogError(ex, "Failed to write file tags ADS for {FilePath}.", filePath);
				return false;
			}
			finally
			{
				if (isReadOnly) // Restore read-only attribute (#7534)
					Win32Helper.SetFileAttribute(filePath, IO.FileAttributes.ReadOnly);

				if (isDateOk)
					Win32Helper.SetFileDateModified(filePath, dateModified); // Restore date modified
			}
		}

		public static void UpdateTagsDb()
		{
			var dbInstance = GetDbInstance();
			foreach (var file in dbInstance.GetAll())
			{
				var pathFromFrn = Win32Helper.PathFromFileId(file.Frn ?? 0, file.FilePath);
				if (pathFromFrn is not null)
				{
					var cleanPath = pathFromFrn.Replace(@"\\?\", "", StringComparison.Ordinal);
					var adsTags = ReadFileTag(cleanPath);

					if (adsTags is not null && adsTags.Any())
					{
						dbInstance.UpdateTag(file.Frn ?? 0, null, cleanPath);
						dbInstance.SetTags(cleanPath, file.Frn, adsTags);
					}
					else
					{
						dbInstance.UpdateTag(file.Frn ?? 0, null, cleanPath);
						if (file.Tags is not null && file.Tags.Any())
						{
							dbInstance.SetTags(cleanPath, file.Frn, file.Tags);
							TryWriteFileTag(cleanPath, file.Tags);
						}
					}
				}
				else
				{
					bool fileExists = IO.File.Exists(file.FilePath) || IO.Directory.Exists(file.FilePath);
					if (fileExists)
					{
						var adsTags = ReadFileTag(file.FilePath);
						var currentFrn = GetFileFRN(file.FilePath);

						if (adsTags is not null && adsTags.Any())
						{
							dbInstance.UpdateTag(file.FilePath, currentFrn, null);
							dbInstance.SetTags(file.FilePath, currentFrn, adsTags);
						}
						else if (file.Tags is not null && file.Tags.Any())
						{
							dbInstance.UpdateTag(file.FilePath, currentFrn, null);
							dbInstance.SetTags(file.FilePath, currentFrn, file.Tags);
							TryWriteFileTag(file.FilePath, file.Tags);
						}
					}
					else
					{
						dbInstance.SetTags(file.FilePath, file.Frn, []);
					}
				}
			}
		}

		/// <summary>
		/// Prompts the user for confirmation, then removes all tags from the given items that have tags.
		/// </summary>
		/// <returns>True if the user confirmed and tags were removed; otherwise false.</returns>
		public static async Task<bool> RemoveTagsAsync(IEnumerable<ListedItem> items)
		{
			var itemsWithTags = items.Where(item => item.FileTags is { Length: > 0 }).ToList();
			if (itemsWithTags.Count == 0)
				return false;

			var confirmed = await DialogDisplayHelper.ShowDialogAsync(
				Strings.RemoveTags.GetLocalizedResource(),
				Strings.ConfirmRemoveTagsDialogContent.GetLocalizedResource(),
				Strings.Yes.GetLocalizedResource(),
				Strings.Cancel.GetLocalizedResource());

			if (!confirmed)
				return false;

			foreach (var item in itemsWithTags)
				item.FileTags = [];

			return true;
		}

		public static ulong? GetFileFRN(string filePath) => Win32Helper.GetFileFRN(filePath);

		public static Task<ulong?> GetFileFRN(IStorageItem item)
		{
			return item switch
			{
				BaseStorageFolder { Properties: not null } folder => GetFileFRN(folder.Properties),
				BaseStorageFile { Properties: not null } file => GetFileFRN(file.Properties),
				_ => Task.FromResult<ulong?>(null),
			};

			static async Task<ulong?> GetFileFRN(IStorageItemExtraProperties properties)
			{
				var extra = await properties.RetrievePropertiesAsync(["System.FileFRN"]);
				return (ulong?)extra["System.FileFRN"];
			}
		}
	}
}