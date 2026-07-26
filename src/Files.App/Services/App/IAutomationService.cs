// Copyright (c) Files Community
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Services;

public interface IAutomationService
{
	Task<IAutomationBarSession?> InitializeSessionAsync(CancellationToken cancellationToken = default);
}
