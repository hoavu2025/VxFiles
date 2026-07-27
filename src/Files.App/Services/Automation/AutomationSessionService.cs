// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using VxFiles.Automation;
using VxFiles.Automation.Abstractions;

namespace Files.App.Services.Automation
{
	/// <inheritdoc cref="IAutomationSessionService"/>
	public sealed class AutomationSessionService : IAutomationSessionService
	{
		/// <summary>
		/// The bundled packages that ship inside the installed app, laid down beside the executable by the
		/// Automation payload. It is absent from a build that never ran the payload, which is not an error.
		/// </summary>
		private static readonly string BundledPackagesPath =
			Path.Combine(VxFilesEnvironment.InstallPath, "AutomationPackages");

		private static readonly string AutomationDataPath =
			Path.Combine(VxFilesEnvironment.LocalDataPath, "Automation");

		private readonly SemaphoreSlim _openGate = new(1, 1);

		private IAutomationSession? _session;

		public string UserPackagesPath { get; } = Path.Combine(AutomationDataPath, "Packages");

		public string EnsureUserPackagesFolder()
		{
			Directory.CreateDirectory(UserPackagesPath);
			return UserPackagesPath;
		}

		public async ValueTask<IAutomationSession> GetSessionAsync(CancellationToken cancellationToken = default)
		{
			if (_session is { } alreadyOpen)
				return alreadyOpen;

			await _openGate.WaitAsync(cancellationToken);
			try
			{
				// Assigned only on success, so a missing runtime can be repaired and the tab reopened.
				return _session ??= await OpenAsync(cancellationToken);
			}
			finally
			{
				_openGate.Release();
			}
		}

		public async ValueTask DisposeAsync()
		{
			var session = Interlocked.Exchange(ref _session, null);
			if (session is not null)
				await session.DisposeAsync();

			_openGate.Dispose();
		}

		/// <summary>
		/// Opens the session off the UI thread. Discovery walks every package root synchronously, and a root on
		/// a slow or disconnected drive would otherwise freeze the window on the click that selects Tools.
		/// </summary>
		private Task<IAutomationSession> OpenAsync(CancellationToken cancellationToken)
			=> Task.Run(async () => await OpenCoreAsync(cancellationToken), cancellationToken);

		private ValueTask<IAutomationSession> OpenCoreAsync(CancellationToken cancellationToken)
		{
			var stateRoot = Path.Combine(AutomationDataPath, "State");
			var temporaryRoot = Path.Combine(VxFilesEnvironment.TemporaryDataPath, "Automation");

			// The session only watches roots that exist when it opens, so the user folder is created first.
			// Otherwise a package copied in later would stay invisible until the app restarted.
			EnsureUserPackagesFolder();
			Directory.CreateDirectory(stateRoot);
			Directory.CreateDirectory(temporaryRoot);

			var packageRoots = Directory.Exists(BundledPackagesPath)
				? ImmutableArray.Create(BundledPackagesPath, UserPackagesPath)
				: ImmutableArray.Create(UserPackagesPath);

			var options = AutomationModule.CreateDefaultOptions(
				packageRoots,
				stateRoot,
				temporaryRoot,
				VxFilesEnvironment.Version,
				CultureInfo.CurrentUICulture.Name);

			// The host ports are supplied here rather than left to the module's safe defaults, which deny every
			// trust prompt and reject every result intent — correct for a headless session, useless in the app.
			return AutomationModule.OpenAsync(
				options,
				new FileAutomationStateStore(stateRoot),
				new AutomationTrustConsent(),
				new AutomationResultRouter(),
				cancellationToken);
		}
	}
}
