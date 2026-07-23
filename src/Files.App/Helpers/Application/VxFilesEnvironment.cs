// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace Files.App.Helpers.Application
{
	public static class VxFilesEnvironment
	{
		private const string StateFileName = "state.json";
		private const string ActiveInstanceProcessIdKey = "INSTANCE_ACTIVE";
		private static readonly object StateLock = new();

		public const string DisplayName = "VxFiles";
		public const string UpstreamDisplayName = "Files 4.2";
		public const bool SupportsPackageIdentity = false;
		public const bool SupportsWindowsIntegration = false;
		public static Version Version { get; } = new(1, 0, 0, 0);
		public static string InstallPath { get; } = AppContext.BaseDirectory;
		public const string InstanceSemaphoreName = "VxFiles-Portable-Instance";
		public static string LocalDataPath { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			DisplayName);
		public static string TemporaryDataPath { get; } = Path.Combine(LocalDataPath, "Temp");
		private static string StateFilePath { get; } = Path.Combine(LocalDataPath, StateFileName);

		static VxFilesEnvironment()
		{
			Directory.CreateDirectory(LocalDataPath);
			Directory.CreateDirectory(TemporaryDataPath);
		}

		public static Task<StorageFolder> GetLocalFolderAsync()
			=> StorageFolder.GetFolderFromPathAsync(LocalDataPath).AsTask();

		public static Task<StorageFolder> GetTemporaryFolderAsync()
			=> StorageFolder.GetFolderFromPathAsync(TemporaryDataPath).AsTask();

		public static int GetActiveInstanceProcessId()
			=> GetState(ActiveInstanceProcessIdKey, -1);

		public static void SetActiveInstanceProcessId(int processId)
			=> SetState(ActiveInstanceProcessIdKey, processId);

		public static Task<bool> LaunchAsync(params string[] arguments)
		{
			var executablePath = Environment.ProcessPath ?? Path.Combine(InstallPath, "VxFiles.exe");
			var startInfo = new ProcessStartInfo(executablePath)
			{
				UseShellExecute = true,
			};

			foreach (var argument in arguments)
				startInfo.ArgumentList.Add(argument);

			return Task.FromResult(Process.Start(startInfo) is not null);
		}

		public static TValue GetState<TValue>(string key, TValue defaultValue = default!)
		{
			lock (StateLock)
			{
				using var mutex = AcquireStateMutex();
				try
				{
					var state = ReadState();
					return state.TryGetValue(key, out var value)
						? value.Deserialize<TValue>() ?? defaultValue
						: defaultValue;
				}
				finally
				{
					mutex.ReleaseMutex();
				}
			}
		}

		public static void SetState<TValue>(string key, TValue value)
		{
			lock (StateLock)
			{
				using var mutex = AcquireStateMutex();
				try
				{
					var state = ReadState();
					state[key] = JsonSerializer.SerializeToElement(value);
					WriteState(state);
				}
				finally
				{
					mutex.ReleaseMutex();
				}
			}
		}

		public static void RemoveState(string key)
		{
			lock (StateLock)
			{
				using var mutex = AcquireStateMutex();
				try
				{
					var state = ReadState();
					if (state.Remove(key))
						WriteState(state);
				}
				finally
				{
					mutex.ReleaseMutex();
				}
			}
		}

		private static Mutex AcquireStateMutex()
		{
			var mutex = new Mutex(false, @"Local\VxFiles-State");
			try
			{
				if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
				{
					mutex.Dispose();
					throw new TimeoutException("Timed out waiting for the VxFiles state lock.");
				}
			}
			catch (AbandonedMutexException)
			{
			}

			return mutex;
		}

		private static Dictionary<string, JsonElement> ReadState()
		{
			try
			{
				return File.Exists(StateFilePath)
					? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(StateFilePath)) ?? []
					: [];
			}
			catch (JsonException)
			{
				return [];
			}
		}

		private static void WriteState(Dictionary<string, JsonElement> state)
		{
			var temporaryPath = $"{StateFilePath}.{Environment.ProcessId}.tmp";
			File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
			File.Move(temporaryPath, StateFilePath, true);
		}
	}
}
