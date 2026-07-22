// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Buffers.Binary;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record AutomationFingerprint(
	string ActionFingerprint,
	string RunnerFingerprint);

internal static class AutomationTrustFingerprint
{
	public static AutomationFingerprint Compute(
		AutomationPackage package,
		AutomationModuleOptions options,
		ImmutableArray<AutomationExternalToolIdentity> tools)
	{
		var runnerRoot = Path.GetDirectoryName(Path.GetFullPath(options.PythonExecutablePath))!;
		var runnerFingerprint = FingerprintTree(runnerRoot, canonicalizeManifest: false);
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		AppendRecord(hash, "manifest", CanonicalizeJson(package.Manifest.ManifestBytes));
		AppendRecord(hash, "package", Encoding.UTF8.GetBytes(FingerprintTree(package.PackagePath, canonicalizeManifest: true)));
		AppendRecord(hash, "runner", Encoding.UTF8.GetBytes(runnerFingerprint));
		foreach (var tool in tools.OrderBy(tool => tool.Id, StringComparer.Ordinal))
			AppendRecord(hash, $"tool/{tool.Id}", Encoding.UTF8.GetBytes(tool.Fingerprint));

		return new AutomationFingerprint(
			$"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}",
			$"sha256:{runnerFingerprint}");
	}

	private static string FingerprintTree(string rootPath, bool canonicalizeManifest)
	{
		var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
		{
			var attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.ReparsePoint) != 0)
				throw new InvalidOperationException($"Reparse-backed package/runtime entry is forbidden: {path}");
			if ((attributes & FileAttributes.Directory) != 0)
				continue;

			var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
			var content = File.ReadAllBytes(path);
			if (canonicalizeManifest && string.Equals(relativePath, "action.json", StringComparison.Ordinal))
				content = CanonicalizeJson(content);
			AppendRecord(hash, relativePath, SHA256.HashData(content));
		}
		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}

	private static byte[] CanonicalizeJson(byte[] json)
	{
		using var document = JsonDocument.Parse(json);
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
			WriteCanonical(writer, document.RootElement);
		return stream.ToArray();
	}

	private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
				{
					writer.WritePropertyName(property.Name);
					WriteCanonical(writer, property.Value);
				}
				writer.WriteEndObject();
				break;
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (var item in element.EnumerateArray())
					WriteCanonical(writer, item);
				writer.WriteEndArray();
				break;
			case JsonValueKind.String:
				writer.WriteStringValue(element.GetString());
				break;
			case JsonValueKind.Number when element.TryGetInt64(out var integer):
				writer.WriteNumberValue(integer);
				break;
			case JsonValueKind.Number:
				writer.WriteNumberValue(element.GetDouble());
				break;
			case JsonValueKind.True:
			case JsonValueKind.False:
				writer.WriteBooleanValue(element.GetBoolean());
				break;
			case JsonValueKind.Null:
				writer.WriteNullValue();
				break;
			default:
				throw new InvalidOperationException("Unsupported JSON value in canonical manifest.");
		}
	}

	private static void AppendRecord(IncrementalHash hash, string name, byte[] value)
	{
		var nameBytes = Encoding.UTF8.GetBytes(name);
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(length, nameBytes.Length);
		hash.AppendData(length);
		hash.AppendData(nameBytes);
		BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
		hash.AppendData(length);
		hash.AppendData(value);
	}
}
