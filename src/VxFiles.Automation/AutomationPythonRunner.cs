// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record AutomationProcessResult(
	AutomationRunState State,
	string Status,
	string StandardError,
	bool StandardErrorTruncated,
	ImmutableArray<AutomationResultIntent> Intents,
	string? Failure)
{
	public static AutomationProcessResult Failed(string failure)
		=> new(AutomationRunState.Failed, "Failed", string.Empty, false, [], failure);
}

/// <summary>
/// Runs one Automation Action in an isolated, job-owned Python process and enforces its timeout,
/// cancellation, and output budgets.
/// </summary>
internal static class AutomationPythonRunner
{
	private const int MaximumStandardErrorBytes = 1_048_576;
	private const int MaximumCommandLineLength = 30_000;

	private static readonly TimeSpan CancellationGrace = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(2);

	public static async Task<AutomationProcessResult> RunAsync(
		AutomationModuleOptions options,
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationInvocation invocation,
		AutomationRunId runId,
		string trustFingerprint,
		ResolvedAutomationDependencies dependencies,
		Action<AutomationOutputFrame> observe,
		CancellationToken cancellationToken,
		CancellationToken shutdownCancellationToken)
	{
		VerifyPinnedPython(options);
		var runTemporaryPath = Path.Join(options.TemporaryRoot, runId.Value.ToString("N"));
		var actionDataPath = Path.Join(
			options.StateRoot,
			"action-data",
			action.Id.PackageId.Value,
			action.Id.LocalId.Value);
		Directory.CreateDirectory(runTemporaryPath);
		Directory.CreateDirectory(actionDataPath);
		var cancellationEventName = $"Local\\VxFiles.Automation.{runId.Value:N}";
		var startEventName = $"Local\\VxFiles.Automation.Start.{runId.Value:N}";
		using var cancellationEvent = new EventWaitHandle(false, EventResetMode.ManualReset, cancellationEventName);
		using var startEvent = new EventWaitHandle(false, EventResetMode.ManualReset, startEventName);

		var startInfo = CreateStartInfo(
			options,
			package,
			action,
			invocation,
			runId,
			runTemporaryPath,
			actionDataPath,
			cancellationEventName,
			startEventName);
		using var process = new Process { StartInfo = startInfo };
		try
		{
			if (!process.Start())
				return AutomationProcessResult.Failed("The bundled Python process could not be started.");

			AutomationProcessJob processJob;
			try
			{
				processJob = AutomationProcessJob.CreateAndAssign(process);
			}
			catch
			{
				KillProcessTree(process);
				throw;
			}

			using (processJob)
			{
				// The action waits on this gate, so no action code runs before its process tree is job-owned.
				startEvent.Set();
				return await RunAssignedProcessAsync(
					options,
					package,
					action,
					invocation,
					runId,
					trustFingerprint,
					dependencies,
					observe,
					process,
					cancellationEvent,
					cancellationToken,
					shutdownCancellationToken);
			}
		}
		finally
		{
			TryDeleteDirectory(runTemporaryPath);
		}
	}

	private static async Task<AutomationProcessResult> RunAssignedProcessAsync(
		AutomationModuleOptions options,
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationInvocation invocation,
		AutomationRunId runId,
		string trustFingerprint,
		ResolvedAutomationDependencies dependencies,
		Action<AutomationOutputFrame> observe,
		Process process,
		EventWaitHandle cancellationEvent,
		CancellationToken cancellationToken,
		CancellationToken shutdownCancellationToken)
	{
		using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownCancellationToken);
		try
		{
			await WriteRequestAsync(process, options, package, action, invocation, runId, trustFingerprint, dependencies, requestCancellation.Token);
		}
		catch (OperationCanceledException) when (shutdownCancellationToken.IsCancellationRequested)
		{
			return await CancelBeforeOutputAsync(
				process,
				cancellationEvent,
				ShutdownGrace,
				"The Automation Action was stopped because its host session closed.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return await CancelBeforeOutputAsync(
				process,
				cancellationEvent,
				CancellationGrace,
				"The Automation Action was cancelled.");
		}

		var stdout = DrainStandardOutputAsync(process, action, observe);
		var stderr = DrainStandardErrorAsync(process);
		var processExit = process.WaitForExitAsync(CancellationToken.None);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(action.TimeoutSeconds));
		var userCancellation = WaitForCancellationAsync(cancellationToken);
		var shutdownCancellation = WaitForCancellationAsync(shutdownCancellationToken);
		var deadline = WaitForCancellationAsync(timeout.Token);

		var terminalState = AutomationRunState.Succeeded;
		string? failure = null;

		// Only a faulted drain needs to interrupt the wait. Once it finishes cleanly it is dropped from the
		// set, because an already-completed task would otherwise make every iteration return immediately.
		Task<OutputDrainResult>? pendingOutput = stdout;
		while (!processExit.IsCompleted)
		{
			var completed = pendingOutput is null
				? await Task.WhenAny(processExit, userCancellation, shutdownCancellation, deadline)
				: await Task.WhenAny(processExit, pendingOutput, userCancellation, shutdownCancellation, deadline);
			if (completed == pendingOutput)
			{
				if (stdout.IsFaulted)
				{
					failure = stdout.Exception?.GetBaseException().Message ?? "The output protocol failed.";
					terminalState = AutomationRunState.Failed;
					KillProcessTree(process);
					break;
				}

				pendingOutput = null;
				continue;
			}

			if (completed == userCancellation)
			{
				terminalState = AutomationRunState.Cancelled;
				failure = "The Automation Action was cancelled.";
				cancellationEvent.Set();
				await StopAfterGraceAsync(process, processExit, CancellationGrace);
				break;
			}

			if (completed == shutdownCancellation)
			{
				terminalState = AutomationRunState.Cancelled;
				failure = "The Automation Action was stopped because its host session closed.";
				cancellationEvent.Set();
				await StopAfterGraceAsync(process, processExit, ShutdownGrace);
				break;
			}

			if (completed == deadline)
			{
				terminalState = AutomationRunState.TimedOut;
				failure = $"The Automation Action exceeded its {action.TimeoutSeconds}-second timeout.";
				cancellationEvent.Set();
				await StopAfterGraceAsync(process, processExit, CancellationGrace);
				break;
			}
		}

		await processExit;
		OutputDrainResult output;
		try
		{
			output = await stdout;
		}
		catch (Exception exception)
		{
			// A cancelled or timed-out run keeps the reason it ended; only an otherwise clean run becomes a failure.
			if (terminalState is AutomationRunState.Succeeded)
			{
				terminalState = AutomationRunState.Failed;
				failure = exception.Message;
			}

			output = new([]);
		}

		var error = await stderr;
		if (terminalState is AutomationRunState.Succeeded && process.ExitCode != 0)
		{
			terminalState = AutomationRunState.Failed;
			failure = $"Bundled Python exited with code {process.ExitCode}.";
		}

		// A run that reached its own terminal frame reports what the action said it did. Anything else — failed,
		// timed out, cancelled, or ended without a result frame — has only the state to report.
		return new(
			terminalState,
			terminalState is AutomationRunState.Succeeded && !string.IsNullOrWhiteSpace(output.TerminalMessage)
				? output.TerminalMessage
				: terminalState.ToString(),
			error.Text,
			error.Truncated,
			output.Intents,
			failure);
	}

	private static async Task<AutomationProcessResult> CancelBeforeOutputAsync(
		Process process,
		EventWaitHandle cancellationEvent,
		TimeSpan grace,
		string failure)
	{
		process.StandardInput.Close();
		cancellationEvent.Set();
		await StopAfterGraceAsync(process, process.WaitForExitAsync(CancellationToken.None), grace);
		return new(AutomationRunState.Cancelled, "Cancelled", string.Empty, false, [], failure);
	}

	private static ProcessStartInfo CreateStartInfo(
		AutomationModuleOptions options,
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationInvocation invocation,
		AutomationRunId runId,
		string temporaryPath,
		string actionDataPath,
		string cancellationEventName,
		string startEventName)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = options.Runtime.PythonExecutablePath,
			WorkingDirectory = package.PackagePath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardInputEncoding = new UTF8Encoding(false, true),
			StandardOutputEncoding = new UTF8Encoding(false, true),
			StandardErrorEncoding = new UTF8Encoding(false, true),
		};

		// Isolated UTF-8 mode: no user site packages, no ambient PYTHON* configuration.
		//
		// Everything here has to be a flag rather than an environment variable. -I implies -E, so the
		// interpreter ignores every PYTHON* variable in the environment below, including ones set with the
		// intent of configuring it.
		startInfo.ArgumentList.Add("-X");
		startInfo.ArgumentList.Add("utf8");
		startInfo.ArgumentList.Add("-I");

		// -B, not PYTHONDONTWRITEBYTECODE. Imports must not drop a __pycache__ into the package or the runtime
		// tree, because both are covered by the trust fingerprint and a run that mutates them makes the next run
		// prompt for trust again.
		startInfo.ArgumentList.Add("-B");
		startInfo.ArgumentList.Add(options.Runtime.BootstrapPath);
		startInfo.ArgumentList.Add(action.EntryPointPath);
		startInfo.ArgumentList.Add(package.PackagePath);
		if (action.InputMode is AutomationInputMode.ArgvPaths)
		{
			foreach (var item in invocation.Selection.Items)
				startInfo.ArgumentList.Add(item.FullPath);
		}

		var commandLineLength = QuoteWindowsArgument(startInfo.FileName).Length + 1 +
			startInfo.ArgumentList.Sum(argument => QuoteWindowsArgument(argument).Length + 1);
		if (commandLineLength > MaximumCommandLineLength)
			throw new InvalidOperationException("Selected paths exceed the safe Windows command-line size; use json-stdin.");

		startInfo.Environment.Clear();
		CopyEnvironment("SystemRoot", startInfo);
		CopyEnvironment("WINDIR", startInfo);
		startInfo.Environment["TEMP"] = temporaryPath;
		startInfo.Environment["TMP"] = temporaryPath;
		startInfo.Environment["VXFILES_AUTOMATION_RUN_ID"] = runId.Value.ToString("D");
		startInfo.Environment["VXFILES_AUTOMATION_TEMP"] = temporaryPath;
		startInfo.Environment["VXFILES_AUTOMATION_DATA"] = actionDataPath;
		startInfo.Environment["VXFILES_AUTOMATION_CANCEL_EVENT"] = cancellationEventName;
		startInfo.Environment["VXFILES_AUTOMATION_START_EVENT"] = startEventName;
		return startInfo;
	}

	/// <summary>
	/// Measures an argument the way <c>CommandLineToArgvW</c> reads it, so the launch budget matches what
	/// Windows will actually build. Selected paths are never concatenated into a shell command line.
	/// </summary>
	private static string QuoteWindowsArgument(string argument)
	{
		if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
			return argument;

		var result = new StringBuilder(argument.Length + 2).Append('"');
		var backslashes = 0;
		foreach (var character in argument)
		{
			if (character is '\\')
			{
				backslashes++;
				continue;
			}

			if (character is '"')
				result.Append('\\', (backslashes * 2) + 1);
			else
				result.Append('\\', backslashes);
			backslashes = 0;
			result.Append(character);
		}

		return result.Append('\\', backslashes * 2).Append('"').ToString();
	}

	private static async Task WriteRequestAsync(
		Process process,
		AutomationModuleOptions options,
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationInvocation invocation,
		AutomationRunId runId,
		string trustFingerprint,
		ResolvedAutomationDependencies dependencies,
		CancellationToken cancellationToken)
	{
		if (action.InputMode is AutomationInputMode.JsonStdin)
		{
			using var stream = new MemoryStream();
			using (var writer = new Utf8JsonWriter(stream))
			{
				writer.WriteStartObject();
				writer.WriteNumber("protocolVersion", 1);
				writer.WriteString("runId", runId.Value);
				writer.WriteStartObject("action");
				writer.WriteString("id", action.Id.Value);
				writer.WriteString("packageId", package.Id.Value);
				writer.WriteString("packageVersion", package.PackageVersion);
				writer.WriteString("trustFingerprint", trustFingerprint);
				writer.WriteEndObject();
				writer.WriteStartObject("host");
				writer.WriteString("version", options.HostVersion.ToString(3));
				writer.WriteString("locale", options.HostLocale);
				writer.WriteEndObject();
				writer.WriteString("capturedAtUtc", invocation.Selection.CapturedAtUtc);
				writer.WriteString("activeFolderPath", invocation.Selection.ActiveFolderPath);
				writer.WriteStartArray("items");
				foreach (var item in invocation.Selection.Items)
				{
					writer.WriteStartObject();
					writer.WriteString("path", item.FullPath);
					writer.WriteString("kind", item.Kind is SelectedPathKind.File ? "file" : "folder");
					writer.WriteString("locationKind", item.LocationKind is SelectedLocationKind.Local ? "local" : "unc");
					writer.WriteEndObject();
				}

				writer.WriteEndArray();
				writer.WriteStartObject("settings");
				foreach (var (key, value) in dependencies.Settings.OrderBy(item => item.Key, StringComparer.Ordinal))
				{
					writer.WritePropertyName(key);
					WriteSettingValue(writer, value);
				}

				writer.WriteEndObject();
				writer.WriteStartArray("externalTools");
				foreach (var tool in dependencies.ExternalTools)
				{
					writer.WriteStartObject();
					writer.WriteString("id", tool.Id);
					writer.WriteString("path", tool.ExecutablePath);
					writer.WriteString("fingerprint", tool.Fingerprint);
					if (tool.FileVersion is not null)
						writer.WriteString("fileVersion", tool.FileVersion);
					writer.WriteString("signatureStatus", tool.SignatureStatus);
					writer.WriteEndObject();
				}

				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			stream.Position = 0;
			await stream.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
		}

		process.StandardInput.Close();
	}

	private static void WriteSettingValue(Utf8JsonWriter writer, AutomationSettingValue value)
	{
		switch (value.Kind)
		{
			case AutomationSettingValueKind.Boolean:
				writer.WriteBooleanValue(value.BooleanValue);
				break;
			case AutomationSettingValueKind.Integer:
				writer.WriteNumberValue(value.IntegerValue);
				break;
			case AutomationSettingValueKind.Number:
				writer.WriteNumberValue(value.NumberValue);
				break;
			case AutomationSettingValueKind.String:
				writer.WriteStringValue(value.StringValue);
				break;
			default:
				throw new InvalidOperationException("Unknown Automation setting value kind.");
		}
	}

	private static async Task<OutputDrainResult> DrainStandardOutputAsync(
		Process process,
		AutomationActionDefinition action,
		Action<AutomationOutputFrame> observe)
	{
		var totalBytes = 0;
		var intents = ImmutableArray.CreateBuilder<AutomationResultIntent>();
		var protocol = new AutomationOutputProtocolReader();
		var line = new MemoryStream();
		var buffer = new byte[4096];
		int read;
		while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer)) > 0)
		{
			for (var index = 0; index < read; index++)
			{
				var value = buffer[index];
				totalBytes = checked(totalBytes + 1);
				if (totalBytes > action.MaxOutputBytes)
					throw new InvalidOperationException($"Standard output exceeded the {action.MaxOutputBytes}-byte budget.");
				if (value is (byte)'\n')
				{
					ProcessOutputLine(line, totalBytes, action, protocol, observe, intents);
					line.SetLength(0);
					continue;
				}

				line.WriteByte(value);
				if (action.OutputProtocol is AutomationOutputProtocol.NdjsonV1 && line.Length > 65_536)
					throw new InvalidOperationException("NDJSON frame exceeds 65536 UTF-8 bytes.");
			}
		}

		if (line.Length > 0)
			ProcessOutputLine(line, totalBytes, action, protocol, observe, intents);
		return new(intents.ToImmutable(), protocol.TerminalMessage);
	}

	private static void ProcessOutputLine(
		MemoryStream lineBytes,
		int sequence,
		AutomationActionDefinition action,
		AutomationOutputProtocolReader protocol,
		Action<AutomationOutputFrame> observe,
		ImmutableArray<AutomationResultIntent>.Builder intents)
	{
		var bytes = lineBytes.ToArray();
		if (bytes.Length > 0 && bytes[^1] is (byte)'\r')
			bytes = bytes[..^1];
		var text = new UTF8Encoding(false, true).GetString(bytes);
		if (action.OutputProtocol is AutomationOutputProtocol.ExitCode)
		{
			observe(new(null, text, new(sequence, AutomationLogLevel.Information, text), []));
			return;
		}

		if (string.IsNullOrWhiteSpace(text))
			return;

		var frame = protocol.Parse(text, bytes.Length);
		observe(frame);
		intents.AddRange(frame.Intents);
	}

	private static void VerifyPinnedPython(AutomationModuleOptions options)
	{
		var pinnedSha256 = options.Runtime.PythonSha256;
		var expected = pinnedSha256.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
			? pinnedSha256[7..]
			: pinnedSha256;
		var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(options.Runtime.PythonExecutablePath)));
		if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The app-local Python executable does not match its pinned SHA-256.");
	}

	private static async Task<StandardErrorResult> DrainStandardErrorAsync(Process process)
	{
		var builder = new StringBuilder();
		var buffer = new char[4096];
		var retainedBytes = 0;
		var truncated = false;
		int read;
		while ((read = await process.StandardError.ReadAsync(buffer)) > 0)
		{
			var chunk = new string(buffer, 0, read);
			var bytes = Encoding.UTF8.GetByteCount(chunk);
			if (retainedBytes + bytes <= MaximumStandardErrorBytes)
			{
				builder.Append(chunk);
				retainedBytes += bytes;
			}
			else
			{
				truncated = true;
			}
		}

		return new(builder.ToString(), truncated);
	}

	private static async Task StopAfterGraceAsync(Process process, Task processExit, TimeSpan grace)
	{
		if (await Task.WhenAny(processExit, Task.Delay(grace)) != processExit)
			KillProcessTree(process);
	}

	private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
		=> Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
			_ => { },
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);

	private static void CopyEnvironment(string name, ProcessStartInfo startInfo)
	{
		var value = Environment.GetEnvironmentVariable(name);
		if (!string.IsNullOrEmpty(value))
			startInfo.Environment[name] = value;
	}

	private static void KillProcessTree(Process process)
	{
		try
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException)
		{
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			Directory.Delete(path, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	private sealed record OutputDrainResult(
		ImmutableArray<AutomationResultIntent> Intents,
		string? TerminalMessage = null);

	private sealed record StandardErrorResult(string Text, bool Truncated);
}
