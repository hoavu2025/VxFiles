// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers.Application;
using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Files.App.Helpers
{
	public sealed class LayoutPreferencesDatabase
	{
		private static readonly object DatabaseLock = new();
		private static readonly string DatabasePath = Path.Combine(VxFilesEnvironment.LocalDataPath, "layout-preferences.json");

		public LayoutPreferencesItem? GetPreferences(string filePath, ulong? frn)
		{
			lock (DatabaseLock)
				return FindPreferences(ReadAll(), filePath, frn)?.LayoutPreferencesManager;
		}

		public void SetPreferences(string filePath, ulong? frn, LayoutPreferencesItem? preferencesItem)
		{
			lock (DatabaseLock)
			{
				var preferences = ReadAll();
				var existing = FindPreferences(preferences, filePath, frn);

				if (existing is not null)
					preferences.Remove(existing);

				if (preferencesItem is not null)
				{
					preferences.Add(new LayoutPreferencesDatabaseItem
					{
						FilePath = filePath,
						Frn = frn,
						LayoutPreferencesManager = preferencesItem,
					});
				}

				WriteAll(preferences);
			}
		}

		public void ResetAll()
		{
			lock (DatabaseLock)
				File.Delete(DatabasePath);
		}

		public void Import(string json)
		{
			var preferences = JsonSerializer.Deserialize<List<LayoutPreferencesDatabaseItem>>(json) ?? [];
			lock (DatabaseLock)
				WriteAll(preferences);
		}

		public string Export()
		{
			lock (DatabaseLock)
				return JsonSerializer.Serialize(ReadAll());
		}

		private static LayoutPreferencesDatabaseItem? FindPreferences(
			IEnumerable<LayoutPreferencesDatabaseItem> preferences,
			string? filePath,
			ulong? frn)
		{
			return preferences.FirstOrDefault(item =>
				(filePath is not null && string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) ||
				(frn is not null && item.Frn == frn));
		}

		private static List<LayoutPreferencesDatabaseItem> ReadAll()
		{
			try
			{
				return File.Exists(DatabasePath)
					? JsonSerializer.Deserialize<List<LayoutPreferencesDatabaseItem>>(File.ReadAllText(DatabasePath)) ?? []
					: [];
			}
			catch (JsonException)
			{
				return [];
			}
		}

		private static void WriteAll(List<LayoutPreferencesDatabaseItem> preferences)
		{
			var temporaryPath = $"{DatabasePath}.{Environment.ProcessId}.tmp";
			File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences));
			File.Move(temporaryPath, DatabasePath, true);
		}
	}
}
