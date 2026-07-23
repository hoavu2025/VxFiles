// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers.Application;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace Files.App.Utils.FileTags
{
	public sealed class FileTagsDatabase
	{
		private readonly string _dbPath = Path.Combine(VxFilesEnvironment.LocalDataPath, "filetags.db");
		private readonly object _lock = new();

		private List<TaggedFile>? _cachedFiles;
		private DateTime? _lastWriteTimeUtc;
		private long? _lastLength;
		private ulong? _lastFileId;

		private static readonly JsonSerializerOptions _jsonOptions = new()
		{
			WriteIndented = true,
			PropertyNameCaseInsensitive = true
		};

		public bool SetTags(string filePath, ulong? frn, string[] tags)
		{
			if (string.IsNullOrWhiteSpace(filePath))
				return false;

			var cleanTags = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();

			// Fast check: if untagged and db file does not exist, no-op immediately
			if (cleanTags.Length == 0 && !File.Exists(_dbPath))
				return true;

			return TryExecuteMutation(() =>
			{
				var files = LoadInternal();
				var normalizedPath = NormalizePath(filePath);
				var existingIndex = files.FindIndex(f => string.Equals(NormalizePath(f.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));

				if (existingIndex < 0)
				{
					if (cleanTags.Length == 0)
						return;

					files.Add(new TaggedFile
					{
						FilePath = filePath,
						Frn = frn,
						Tags = cleanTags
					});
					SaveInternal(files);
				}
				else
				{
					var existing = files[existingIndex];

					if (cleanTags.Length == 0)
					{
						files.RemoveAt(existingIndex);
						SaveInternal(files);
					}
					else
					{
						bool tagsChanged = !SetEquals(existing.Tags, cleanTags);
						bool frnChanged = frn.HasValue && frn.Value > 0 && existing.Frn != frn.Value;
						bool pathChanged = !string.Equals(existing.FilePath, filePath, StringComparison.Ordinal);

						if (!tagsChanged && !frnChanged && !pathChanged)
							return;

						existing.FilePath = filePath;
						if (frn.HasValue && frn.Value > 0)
							existing.Frn = frn;
						existing.Tags = cleanTags;

						SaveInternal(files);
					}
				}
			}, "set tags for {FilePath}", filePath);
		}

		public bool UpdateTag(string oldFilePath, ulong? frn, string? newFilePath)
		{
			if (string.IsNullOrWhiteSpace(oldFilePath))
				return false;

			return TryExecuteMutation(() =>
			{
				var files = LoadInternal();
				var normalizedOldPath = NormalizePath(oldFilePath);
				var existingIndex = files.FindIndex(f => string.Equals(NormalizePath(f.FilePath), normalizedOldPath, StringComparison.OrdinalIgnoreCase));

				if (existingIndex < 0)
					return;

				var existing = files[existingIndex];
				var targetPath = !string.IsNullOrWhiteSpace(newFilePath) ? newFilePath! : oldFilePath;
				var normalizedTargetPath = NormalizePath(targetPath);

				bool pathChanged = !string.Equals(NormalizePath(existing.FilePath), normalizedTargetPath, StringComparison.OrdinalIgnoreCase);
				bool frnChanged = frn.HasValue && frn.Value > 0 && existing.Frn != frn.Value;

				if (!pathChanged && !frnChanged)
					return;

				files.RemoveAt(existingIndex);
				files.RemoveAll(f => string.Equals(NormalizePath(f.FilePath), normalizedTargetPath, StringComparison.OrdinalIgnoreCase));

				existing.FilePath = targetPath;
				if (frn.HasValue && frn.Value > 0)
					existing.Frn = frn;

				files.Add(existing);
				SaveInternal(files);
			}, "update tags for {FilePath}", oldFilePath);
		}

		public bool UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath)
		{
			if (oldFrn == 0)
				return false;

			return TryExecuteMutation(() =>
			{
				var files = LoadInternal();
				var existingIndex = files.FindIndex(f => f.Frn == oldFrn);

				if (existingIndex < 0)
					return;

				var existing = files[existingIndex];

				bool pathChanged = !string.IsNullOrWhiteSpace(newFilePath) &&
					!string.Equals(NormalizePath(existing.FilePath), NormalizePath(newFilePath), StringComparison.OrdinalIgnoreCase);
				bool frnChanged = frn.HasValue && frn.Value > 0 && existing.Frn != frn.Value;

				if (!pathChanged && !frnChanged)
					return;

				files.RemoveAt(existingIndex);

				if (!string.IsNullOrWhiteSpace(newFilePath))
				{
					var normalizedNewPath = NormalizePath(newFilePath);
					files.RemoveAll(f => string.Equals(NormalizePath(f.FilePath), normalizedNewPath, StringComparison.OrdinalIgnoreCase));
					existing.FilePath = newFilePath;
				}

				if (frn.HasValue && frn.Value > 0)
					existing.Frn = frn;

				files.Add(existing);
				SaveInternal(files);
			}, "update tags for FRN {Frn}", oldFrn);
		}

		public string[] GetTags(string? filePath, ulong? frn)
		{
			return ExecuteUnderLock(() =>
			{
				var files = LoadInternal();

				if (!string.IsNullOrWhiteSpace(filePath))
				{
					var normalizedPath = NormalizePath(filePath);
					var entry = files.FirstOrDefault(f => string.Equals(NormalizePath(f.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));
					if (entry is not null)
						return entry.Tags?.ToArray() ?? Array.Empty<string>();
				}

				if (frn.HasValue && frn.Value > 0)
				{
					var entry = files.FirstOrDefault(f => f.Frn == frn.Value);
					if (entry is not null)
						return entry.Tags?.ToArray() ?? Array.Empty<string>();
				}

				return Array.Empty<string>();
			});
		}

		public IEnumerable<TaggedFile> GetAll()
		{
			return ExecuteUnderLock(() =>
			{
				var files = LoadInternal();
				return files.Select(CloneTaggedFile).ToArray();
			});
		}

		public IEnumerable<TaggedFile> GetAllUnderPath(string folderPath)
		{
			if (string.IsNullOrWhiteSpace(folderPath))
				return Array.Empty<TaggedFile>();

			return ExecuteUnderLock(() =>
			{
				var files = LoadInternal();
				var normalizedFolder = NormalizePath(folderPath);

				return files
					.Where(f => IsSubpath(NormalizePath(f.FilePath), normalizedFolder))
					.Select(CloneTaggedFile)
					.ToArray();
			});
		}

		public void Import(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return;

			ExecuteUnderLock(() =>
			{
				var imported = JsonSerializer.Deserialize<TaggedFile[]>(json, _jsonOptions);
				if (imported is null)
					return;

				var validItems = imported
					.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.FilePath))
					.Select(item => new TaggedFile
					{
						FilePath = item.FilePath,
						Frn = item.Frn,
						Tags = item.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>()
					})
					.GroupBy(item => NormalizePath(item.FilePath), StringComparer.OrdinalIgnoreCase)
					.Select(group => group.Last())
					.ToList();

				SaveInternal(validItems);
			});
		}

		public string Export()
		{
			return ExecuteUnderLock(() =>
			{
				var files = LoadInternal();
				return JsonSerializer.Serialize(files.ToArray(), _jsonOptions);
			});
		}

		private void ExecuteUnderLock(Action action)
		{
			lock (_lock)
			{
				using var mutex = AcquireDatabaseMutex();
				try
				{
					action();
				}
				finally
				{
					mutex.ReleaseMutex();
				}
			}
		}

		private TResult ExecuteUnderLock<TResult>(Func<TResult> func)
		{
			lock (_lock)
			{
				using var mutex = AcquireDatabaseMutex();
				try
				{
					return func();
				}
				finally
				{
					mutex.ReleaseMutex();
				}
			}
		}

		private bool TryExecuteMutation(Action action, string message, object value)
		{
			try
			{
				ExecuteUnderLock(action);
				return true;
			}
			catch (Exception ex)
			{
				App.Logger?.LogError(ex, $"Failed to {message} in the filetags database.", value);
				return false;
			}
		}

		private static Mutex AcquireDatabaseMutex()
		{
			var mutex = new Mutex(false, @"Local\VxFiles-FileTags");
			try
			{
				if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
				{
					mutex.Dispose();
					throw new TimeoutException("Timed out waiting for the VxFiles filetags lock.");
				}
			}
			catch (AbandonedMutexException)
			{
			}

			return mutex;
		}

		private List<TaggedFile> LoadInternal()
		{
			var info = new FileInfo(_dbPath);
			if (!info.Exists)
			{
				ClearCache();
				return [];
			}

			if (info.Length == 0)
			{
				App.Logger?.LogError("Filetags database at {DbPath} is empty. Quarantining corrupt database file.", _dbPath);
				QuarantineCorruptDatabase();
				return [];
			}

			var currentWriteTime = info.LastWriteTimeUtc;
			var currentFileId = Win32Helper.GetFileFRN(_dbPath);
			if (_cachedFiles is not null &&
				currentFileId.HasValue &&
				_lastFileId == currentFileId &&
				_lastLength == info.Length &&
				_lastWriteTimeUtc == currentWriteTime)
			{
				return _cachedFiles.Select(CloneTaggedFile).ToList();
			}

			string json;
			try
			{
				json = File.ReadAllText(_dbPath);
			}
			catch (Exception ex)
			{
				App.Logger?.LogError(ex, "Transient I/O error reading filetags database from {DbPath}.", _dbPath);
				throw;
			}

			if (string.IsNullOrWhiteSpace(json))
			{
				App.Logger?.LogError("Filetags database at {DbPath} is empty or whitespace. Quarantining corrupt database file.", _dbPath);
				QuarantineCorruptDatabase();
				return [];
			}

			try
			{
				var items = JsonSerializer.Deserialize<List<TaggedFile>>(json, _jsonOptions);
				var result = new List<TaggedFile>();

				if (items is not null)
				{
					foreach (var item in items)
					{
						if (item is null || string.IsNullOrWhiteSpace(item.FilePath))
							continue;

						item.Tags = item.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
						result.Add(item);
					}
				}

				SetCache(result, info.Length, currentWriteTime, currentFileId);
				return result;
			}
			catch (JsonException ex)
			{
				App.Logger?.LogError(ex, "Corrupt filetags database JSON at {DbPath}. Quarantining corrupt database file.", _dbPath);
				QuarantineCorruptDatabase();
				return [];
			}
		}

		private void QuarantineCorruptDatabase()
		{
			try
			{
				if (File.Exists(_dbPath))
				{
					var quarantinePath = $"{_dbPath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
					File.Move(_dbPath, quarantinePath, overwrite: true);
					ClearCache();
				}
			}
			catch (Exception ex)
			{
				App.Logger?.LogError(ex, "Failed to quarantine corrupt filetags database file at {DbPath}.", _dbPath);
				if (File.Exists(_dbPath))
				{
					throw new InvalidOperationException($"Failed to quarantine corrupt filetags database at {_dbPath}.", ex);
				}
			}
		}

		private void SaveInternal(List<TaggedFile> files)
		{
			var dir = Path.GetDirectoryName(_dbPath);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);

			var tempPath = $"{_dbPath}.{Environment.ProcessId}.tmp";
			var json = JsonSerializer.Serialize(files, _jsonOptions);

			File.WriteAllText(tempPath, json);
			File.Move(tempPath, _dbPath, overwrite: true);

			var info = new FileInfo(_dbPath);
			SetCache(files, info.Length, info.LastWriteTimeUtc, Win32Helper.GetFileFRN(_dbPath));
		}

		private void SetCache(List<TaggedFile> files, long length, DateTime lastWriteTimeUtc, ulong? fileId)
		{
			_cachedFiles = files.Select(CloneTaggedFile).ToList();
			_lastLength = length;
			_lastWriteTimeUtc = lastWriteTimeUtc;
			_lastFileId = fileId;
		}

		private void ClearCache()
		{
			_cachedFiles = null;
			_lastLength = null;
			_lastWriteTimeUtc = null;
			_lastFileId = null;
		}

		private static string NormalizePath(string path)
		{
			return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}

		private static bool IsSubpath(string filePath, string folderPath)
		{
			if (filePath.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
				return false;

			if (!filePath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
				return false;

			if (filePath.Length > folderPath.Length)
			{
				char nextChar = filePath[folderPath.Length];
				return nextChar == Path.DirectorySeparatorChar || nextChar == Path.AltDirectorySeparatorChar;
			}

			return false;
		}

		private static bool SetEquals(string[]? set1, string[]? set2)
		{
			if (ReferenceEquals(set1, set2))
				return true;

			if (set1 is null || set2 is null || set1.Length != set2.Length)
				return false;

			return set1.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(set2.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
		}

		private static TaggedFile CloneTaggedFile(TaggedFile source)
		{
			return new TaggedFile
			{
				FilePath = source.FilePath,
				Frn = source.Frn,
				Tags = source.Tags?.ToArray() ?? Array.Empty<string>()
			};
		}
	}
}
