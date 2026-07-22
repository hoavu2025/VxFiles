// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

public sealed class FileAutomationStateStore : IAutomationStateStore
{
	private readonly string _stateRoot;
	private readonly TimeSpan _runRecordLifetime;
	private readonly long _runRecordSizeBudget;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public FileAutomationStateStore(string stateRoot) : this(stateRoot, TimeSpan.FromDays(7), 100L * 1024 * 1024)
	{
	}

	internal FileAutomationStateStore(string stateRoot, TimeSpan runRecordLifetime, long runRecordSizeBudget)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(runRecordLifetime, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(runRecordSizeBudget, 0);
		_stateRoot = Path.GetFullPath(stateRoot);
		_runRecordLifetime = runRecordLifetime;
		_runRecordSizeBudget = runRecordSizeBudget;
	}

	public async ValueTask<AutomationActionState> ReadActionStateAsync(
		AutomationActionId actionId,
		CancellationToken cancellationToken = default)
	{
		var path = GetActionStatePath(actionId);
		if (!File.Exists(path))
			return EmptyState();

		await _gate.WaitAsync(cancellationToken);
		try
		{
			await using var stream = File.OpenRead(path);
			var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
			using (document)
				return ParseActionState(document.RootElement);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask WriteTrustedFingerprintAsync(
		AutomationActionId actionId,
		string fingerprint,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var path = GetActionStatePath(actionId);
			var state = File.Exists(path)
				? await ReadActionStateWithoutLockAsync(path, cancellationToken)
				: EmptyState();
			await WriteActionStateWithoutLockAsync(path, state with { TrustedFingerprint = fingerprint }, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask AppendRunRecordAsync(
		AutomationRunRecord record,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(record);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var directory = Path.Join(_stateRoot, "runs");
			Directory.CreateDirectory(directory);
			var timestamp = record.Snapshot.CompletedAtUtc ?? record.Snapshot.StartedAtUtc;
			var fileName = $"{timestamp.UtcTicks:D19}_{record.Snapshot.Id.Value:N}.json";
			var path = Path.Join(directory, fileName);
			await WriteJsonAtomicallyAsync(path, writer => WriteRunRecord(writer, record), cancellationToken);
			PruneRunRecords(directory, DateTimeOffset.UtcNow);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async ValueTask<AutomationActionState> ReadActionStateWithoutLockAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = File.OpenRead(path);
		var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		using (document)
			return ParseActionState(document.RootElement);
	}

	private static AutomationActionState ParseActionState(JsonElement root)
	{
		if (root.ValueKind is not JsonValueKind.Object)
			throw new InvalidDataException("Automation action state must be a JSON object.");

		string? trustedFingerprint = null;
		var settings = ImmutableDictionary.CreateBuilder<string, AutomationSettingValue>(StringComparer.Ordinal);
		var tools = ImmutableDictionary.CreateBuilder<string, AutomationExternalToolConfiguration>(StringComparer.Ordinal);
		foreach (var property in root.EnumerateObject())
		{
			switch (property.Name)
			{
				case "trustedFingerprint":
					trustedFingerprint = property.Value.ValueKind is JsonValueKind.Null ? null : property.Value.GetString();
					break;
				case "settings":
					foreach (var setting in property.Value.EnumerateObject())
						settings.Add(setting.Name, ParseSetting(setting.Value));
					break;
				case "externalTools":
					foreach (var tool in property.Value.EnumerateObject())
						tools.Add(tool.Name, new AutomationExternalToolConfiguration(tool.Name, tool.Value.GetString() ?? string.Empty));
					break;
				default:
					throw new InvalidDataException($"Unknown automation action state property '{property.Name}'.");
			}
		}
		return new AutomationActionState(trustedFingerprint, settings.ToImmutable(), tools.ToImmutable());
	}

	private static AutomationSettingValue ParseSetting(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.True => new(AutomationSettingValueKind.Boolean, BooleanValue: true),
		JsonValueKind.False => new(AutomationSettingValueKind.Boolean),
		JsonValueKind.Number when value.TryGetInt64(out var integer) => new(AutomationSettingValueKind.Integer, IntegerValue: integer),
		JsonValueKind.Number => new(AutomationSettingValueKind.Number, NumberValue: value.GetDouble()),
		JsonValueKind.String => new(AutomationSettingValueKind.String, StringValue: value.GetString()),
		_ => throw new InvalidDataException("Automation setting values must be boolean, number, integer, or string values."),
	};

	private async ValueTask WriteActionStateWithoutLockAsync(
		string path,
		AutomationActionState state,
		CancellationToken cancellationToken) =>
		await WriteJsonAtomicallyAsync(path, writer => WriteActionState(writer, state), cancellationToken);

	private static async ValueTask WriteJsonAtomicallyAsync(
		string path,
		Action<Utf8JsonWriter> write,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temporaryPath = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
		try
		{
			await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
			{
				using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
				write(writer);
				await writer.FlushAsync(cancellationToken);
				await stream.FlushAsync(cancellationToken);
			}
			File.Move(temporaryPath, path, true);
		}
		finally
		{
			File.Delete(temporaryPath);
		}
	}

	private static void WriteActionState(Utf8JsonWriter writer, AutomationActionState state)
	{
		writer.WriteStartObject();
		if (state.TrustedFingerprint is null)
			writer.WriteNull("trustedFingerprint");
		else
			writer.WriteString("trustedFingerprint", state.TrustedFingerprint);
		writer.WriteStartObject("settings");
		foreach (var setting in state.Settings.OrderBy(item => item.Key, StringComparer.Ordinal))
		{
			switch (setting.Value.Kind)
			{
				case AutomationSettingValueKind.Boolean: writer.WriteBoolean(setting.Key, setting.Value.BooleanValue); break;
				case AutomationSettingValueKind.Integer: writer.WriteNumber(setting.Key, setting.Value.IntegerValue); break;
				case AutomationSettingValueKind.Number: writer.WriteNumber(setting.Key, setting.Value.NumberValue); break;
				case AutomationSettingValueKind.String: writer.WriteString(setting.Key, setting.Value.StringValue); break;
			}
		}
		writer.WriteEndObject();
		writer.WriteStartObject("externalTools");
		foreach (var tool in state.ExternalTools.OrderBy(item => item.Key, StringComparer.Ordinal))
			writer.WriteString(tool.Key, tool.Value.ExecutablePath);
		writer.WriteEndObject();
		writer.WriteEndObject();
	}

	private static void WriteRunRecord(Utf8JsonWriter writer, AutomationRunRecord record)
	{
		var snapshot = record.Snapshot;
		writer.WriteStartObject();
		writer.WriteString("runId", snapshot.Id.Value);
		writer.WriteString("actionId", snapshot.ActionId.Value);
		writer.WriteString("packageVersion", record.PackageVersion);
		writer.WriteString("trustFingerprint", record.TrustFingerprint);
		writer.WriteString("state", snapshot.State.ToString());
		writer.WriteString("startedAtUtc", snapshot.StartedAtUtc);
		if (snapshot.CompletedAtUtc is { } completed)
			writer.WriteString("completedAtUtc", completed);
		writer.WriteString("activeFolderPath", snapshot.Selection.ActiveFolderPath);
		writer.WriteStartArray("selectedPaths");
		foreach (var item in snapshot.Selection.Items)
		{
			writer.WriteStartObject();
			writer.WriteString("path", item.FullPath);
			writer.WriteString("kind", item.Kind.ToString());
			writer.WriteString("locationKind", item.LocationKind.ToString());
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
		if (snapshot.ProgressPercent is { } progress)
			writer.WriteNumber("progressPercent", progress);
		writer.WriteString("status", snapshot.Status);
		writer.WriteStartArray("logs");
		foreach (var log in snapshot.Logs)
		{
			writer.WriteStartObject();
			writer.WriteNumber("sequence", log.Sequence);
			writer.WriteString("level", log.Level.ToString());
			writer.WriteString("message", log.Message);
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
		writer.WriteString("standardError", snapshot.StandardError);
		if (snapshot.Failure is not null)
			writer.WriteString("failure", snapshot.Failure);
		writer.WriteBoolean("standardErrorTruncated", snapshot.StandardErrorTruncated);
		writer.WriteEndObject();
	}

	private void PruneRunRecords(string directory, DateTimeOffset now)
	{
		var files = new DirectoryInfo(directory).EnumerateFiles("*.json")
			.OrderByDescending(file => file.Name, StringComparer.Ordinal)
			.ToArray();
		long retainedBytes = 0;
		foreach (var file in files)
		{
			var expired = now - file.LastWriteTimeUtc > _runRecordLifetime;
			if (expired || retainedBytes + file.Length > _runRecordSizeBudget)
				file.Delete();
			else
				retainedBytes += file.Length;
		}
	}

	private string GetActionStatePath(AutomationActionId actionId)
	{
		if (string.IsNullOrWhiteSpace(actionId.Value) || actionId.Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			throw new ArgumentException("Automation action id is not valid for persistent state.", nameof(actionId));
		return Path.Join(_stateRoot, "actions", actionId.Value + ".json");
	}

	private static AutomationActionState EmptyState() => new(null, [], []);
}
