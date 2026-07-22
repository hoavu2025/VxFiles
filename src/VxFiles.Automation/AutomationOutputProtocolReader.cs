// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record AutomationOutputFrame(
	double? ProgressPercent,
	string? Message,
	AutomationLogEntry? Log,
	ImmutableArray<AutomationResultIntent> Intents);

internal sealed class AutomationOutputProtocolReader
{
	private const int MaximumFrameBytes = 65_536;
	private long _expectedSequence = 1;
	private bool _terminalResultSeen;

	public AutomationOutputFrame Parse(string line, int utf8Bytes)
	{
		if (utf8Bytes > MaximumFrameBytes)
			throw new InvalidOperationException($"NDJSON frame exceeds {MaximumFrameBytes} UTF-8 bytes.");
		if (_terminalResultSeen)
			throw new InvalidOperationException("NDJSON data appeared after the terminal result frame.");

		using var document = JsonDocument.Parse(line, new JsonDocumentOptions
		{
			AllowTrailingCommas = false,
			CommentHandling = JsonCommentHandling.Disallow,
		});
		var root = document.RootElement;
		if (root.ValueKind is not JsonValueKind.Object)
			throw new InvalidOperationException("Each NDJSON frame must be an object.");
		var duplicate = FindDuplicateProperty(root);
		if (duplicate is not null)
			throw new InvalidOperationException($"NDJSON frame contains duplicate property '{duplicate}'.");
		if (RequireString(root, "protocol") is not "ndjson-v1")
			throw new InvalidOperationException("NDJSON frame protocol must be 'ndjson-v1'.");
		var sequence = RequireInt64(root, "sequence");
		if (sequence != _expectedSequence || sequence < 1)
			throw new InvalidOperationException($"NDJSON sequence must be {_expectedSequence}.");
		_expectedSequence++;

		return RequireString(root, "type") switch
		{
			"progress" => ParseProgress(root, sequence),
			"log" => ParseLog(root, sequence),
			"result" => ParseResult(root),
			var type => throw new InvalidOperationException($"Unknown NDJSON frame type '{type}'."),
		};
	}

	private static AutomationOutputFrame ParseProgress(JsonElement root, long sequence)
	{
		ValidateProperties(root, ["protocol", "sequence", "type", "percent", "message"]);
		double? percent = null;
		if (root.TryGetProperty("percent", out var percentElement))
		{
			if (!percentElement.TryGetDouble(out var value) || !double.IsFinite(value) || value is < 0 or > 100)
				throw new InvalidOperationException("Progress percent must be a finite number from 0 through 100.");
			percent = value;
		}
		var message = OptionalString(root, "message");
		return new AutomationOutputFrame(percent, message, null, []);
	}

	private static AutomationOutputFrame ParseLog(JsonElement root, long sequence)
	{
		ValidateProperties(root, ["protocol", "sequence", "type", "level", "message"]);
		var level = RequireString(root, "level") switch
		{
			"debug" => AutomationLogLevel.Debug,
			"info" => AutomationLogLevel.Information,
			"warning" => AutomationLogLevel.Warning,
			"error" => AutomationLogLevel.Error,
			var value => throw new InvalidOperationException($"Unknown NDJSON log level '{value}'."),
		};
		var message = RequireString(root, "message");
		return new AutomationOutputFrame(null, message, new AutomationLogEntry(sequence, level, message), []);
	}

	private AutomationOutputFrame ParseResult(JsonElement root)
	{
		ValidateProperties(root, ["protocol", "sequence", "type", "outcome", "message", "effects"]);
		if (RequireString(root, "outcome") is not "succeeded")
			throw new InvalidOperationException("NDJSON result outcome must be 'succeeded'.");
		var message = OptionalString(root, "message");
		var intents = ImmutableArray.CreateBuilder<AutomationResultIntent>();
		if (root.TryGetProperty("effects", out var effects))
		{
			if (effects.ValueKind is not JsonValueKind.Array)
				throw new InvalidOperationException("NDJSON result effects must be an array.");
			foreach (var effect in effects.EnumerateArray())
				intents.Add(ParseEffect(effect));
		}
		_terminalResultSeen = true;
		return new AutomationOutputFrame(null, message, null, intents.ToImmutable());
	}

	private static AutomationResultIntent ParseEffect(JsonElement effect)
	{
		if (effect.ValueKind is not JsonValueKind.Object)
			throw new InvalidOperationException("Each result effect must be an object.");
		return RequireString(effect, "type") switch
		{
			"refreshCurrentFolder" => ParseRefresh(effect),
			"revealPaths" => ParseReveal(effect),
			var type => throw new InvalidOperationException($"Unknown result effect type '{type}'."),
		};
	}

	private static AutomationResultIntent ParseRefresh(JsonElement effect)
	{
		ValidateProperties(effect, ["type"]);
		return new AutomationResultIntent.RefreshCurrentFolder();
	}

	private static AutomationResultIntent ParseReveal(JsonElement effect)
	{
		ValidateProperties(effect, ["type", "paths"]);
		if (!effect.TryGetProperty("paths", out var paths) || paths.ValueKind is not JsonValueKind.Array)
			throw new InvalidOperationException("revealPaths requires a paths array.");
		var values = ImmutableArray.CreateBuilder<string>();
		foreach (var path in paths.EnumerateArray())
		{
			if (path.ValueKind is not JsonValueKind.String)
				throw new InvalidOperationException("revealPaths paths must be strings.");
			values.Add(path.GetString()!);
		}
		return new AutomationResultIntent.RevealPaths(values.ToImmutable());
	}

	private static void ValidateProperties(JsonElement element, IEnumerable<string> allowed)
	{
		var names = new HashSet<string>(allowed, StringComparer.Ordinal);
		foreach (var property in element.EnumerateObject())
		{
			if (!names.Contains(property.Name))
				throw new InvalidOperationException($"Unknown NDJSON property '{property.Name}'.");
		}
	}

	private static string RequireString(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var property) || property.ValueKind is not JsonValueKind.String)
			throw new InvalidOperationException($"NDJSON property '{name}' must be a string.");
		return property.GetString()!;
	}

	private static string? OptionalString(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var property))
			return null;
		if (property.ValueKind is not JsonValueKind.String)
			throw new InvalidOperationException($"NDJSON property '{name}' must be a string.");
		return property.GetString();
	}

	private static long RequireInt64(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var property) || !property.TryGetInt64(out var value))
			throw new InvalidOperationException($"NDJSON property '{name}' must be an integer.");
		return value;
	}

	private static string? FindDuplicateProperty(JsonElement element)
	{
		if (element.ValueKind is JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject())
			{
				if (!names.Add(property.Name))
					return property.Name;
				var nested = FindDuplicateProperty(property.Value);
				if (nested is not null)
					return nested;
			}
		}
		else if (element.ValueKind is JsonValueKind.Array)
		{
			foreach (var item in element.EnumerateArray())
			{
				var nested = FindDuplicateProperty(item);
				if (nested is not null)
					return nested;
			}
		}
		return null;
	}
}
