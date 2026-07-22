// Copyright (c) Files Community
// Licensed under the MIT License.

using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Files.App.Utils.FileTags
{
	public sealed class FileTagsDatabase
	{
		public void SetTags(string filePath, ulong? frn, string[] tags)
		{
		}

		public void UpdateTag(string oldFilePath, ulong? frn, string? newFilePath)
		{
		}

		public void UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath)
		{
		}

		public string[] GetTags(string? filePath, ulong? frn)
			=> [];

		public IEnumerable<TaggedFile> GetAll()
			=> [];

		public IEnumerable<TaggedFile> GetAllUnderPath(string folderPath)
			=> [];

		public void Import(string json)
		{
		}

		public string Export()
			=> JsonSerializer.Serialize(Array.Empty<TaggedFile>());
	}
}
