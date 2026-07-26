// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal static class AutomationFallbackIds
{
	public static AutomationPackageId FromFolderName(string folderName)
	{
		var sanitized = new string(folderName
			.ToLowerInvariant()
			.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
			.ToArray())
			.Trim('-');
		if (string.IsNullOrEmpty(sanitized))
			sanitized = "package";

		if (sanitized.Length > 120)
		{
			var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folderName)))
				.ToLowerInvariant();
			sanitized = $"{sanitized[..102]}-{hash[..16]}";
		}

		return AutomationPackageId.Parse($"invalid.{sanitized}");
	}
}
