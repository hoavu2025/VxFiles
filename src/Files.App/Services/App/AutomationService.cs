// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using Files.App.Helpers.Application;
using Files.App.Helpers.Automation;
using VxFiles.Automation;
using VxFiles.Automation.Abstractions;

namespace Files.App.Services;

public sealed class AutomationService : IAutomationService
{
	private readonly AutomationHostBridge _hostBridge;
	private readonly FilesAutomationTrustConsent _trustConsent;

	public AutomationService(
		AutomationHostBridge hostBridge,
		FilesAutomationTrustConsent trustConsent)
	{
		_hostBridge = hostBridge;
		_trustConsent = trustConsent;
	}

	public async Task<IAutomationBarSession?> InitializeSessionAsync(CancellationToken cancellationToken = default)
	{
		var userActions = Path.Join(VxFilesEnvironment.LocalDataPath, "Automation", "Actions");
		var bundledActions = Path.Join(VxFilesEnvironment.InstallPath, "AutomationActions");
		var stateRoot = Path.Join(VxFilesEnvironment.LocalDataPath, "Automation", "State");
		var temporaryRoot = Path.Join(VxFilesEnvironment.TemporaryDataPath, "VxFiles", "Automation");

		Directory.CreateDirectory(userActions);
		Directory.CreateDirectory(stateRoot);
		Directory.CreateDirectory(temporaryRoot);

		var roots = Directory.Exists(bundledActions)
			? ImmutableArray.Create(bundledActions, userActions)
			: ImmutableArray.Create(userActions);

		var options = AutomationModule.CreateDefaultOptions(
			roots,
			stateRoot,
			temporaryRoot,
			VxFilesEnvironment.Version,
			CultureInfo.CurrentUICulture.Name);

		var stateStore = new FileAutomationStateStore(stateRoot);

		return await Task.Run(async () => await AutomationModule.OpenAsync(
			options,
			stateStore,
			_trustConsent,
			_hostBridge), cancellationToken);
	}
}
