// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.Logging;
using Files.App.Helpers.Application;
using Files.App.Helpers.Automation;
using global::VxFiles.Automation;
using global::VxFiles.Automation.Abstractions;

namespace Files.App.ViewModels.UserControls;

public sealed partial class AutomationBarViewModel : ObservableObject, IAsyncDisposable
{
	private readonly IAutomationService _automationService;
	private readonly AutomationHostBridge _hostBridge;
	private readonly DispatcherQueue _dispatcherQueue;
	private IAutomationBarSession? _session;
	private Task? _initialization;

	public ObservableCollection<AutomationActionButtonViewModel> Actions { get; } = [];

	[ObservableProperty]
	private Visibility visibility = Visibility.Collapsed;

	[ObservableProperty]
	private Visibility runStatusVisibility = Visibility.Collapsed;

	[ObservableProperty]
	private string runStatus = string.Empty;

	[ObservableProperty]
	private bool isRunning;

	public IAsyncRelayCommand CancelCommand { get; }

	public AutomationBarViewModel(
		IAutomationService automationService,
		AutomationHostBridge hostBridge)
	{
		_automationService = automationService;
		_hostBridge = hostBridge;
		_dispatcherQueue = MainWindow.Instance.DispatcherQueue;
		CancelCommand = new AsyncRelayCommand(CancelAsync, () => IsRunning);
	}

	public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

	public async ValueTask DisposeAsync()
	{
		if (_session is not null)
		{
			_session.PropertyChanged -= Session_PropertyChanged;
			await _session.DisposeAsync();
		}
	}

	private async Task InitializeCoreAsync()
	{
		try
		{
			_session = await _automationService.InitializeSessionAsync();
			if (_session is not null)
			{
				_session.PropertyChanged += Session_PropertyChanged;
				RefreshSnapshot();
			}
		}
		catch (Exception exception)
		{
			App.Logger.LogWarning(exception, "Automation Bar initialization failed.");
			RunStatus = "AutomationBarInitializationFailed".GetLocalizedResource();
			RunStatusVisibility = Visibility.Visible;
		}
	}

	private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (_dispatcherQueue.HasThreadAccess)
			RefreshSnapshot();
		else
			_dispatcherQueue.TryEnqueue(RefreshSnapshot);
	}

	private void RefreshSnapshot()
	{
		if (_session is null)
			return;

		var snapshot = _session.Snapshot;
		var existing = Actions.ToDictionary(action => action.Id);
		Actions.Clear();
		foreach (var action in snapshot.Actions)
		{
			if (!existing.TryGetValue(action.Id, out var button))
				button = new AutomationActionButtonViewModel(action.Id, InvokeAsync);
			button.Update(action);
			Actions.Add(button);
		}

		Visibility = Actions.Count is 0 && snapshot.ActiveRuns.Length is 0
			? Visibility.Collapsed
			: Visibility.Visible;
		IsRunning = snapshot.ActiveRuns.Length is not 0;
		RunStatusVisibility = IsRunning ? Visibility.Visible : Visibility.Collapsed;
		RunStatus = snapshot.ActiveRuns.FirstOrDefault()?.Status ?? string.Empty;
		CancelCommand.NotifyCanExecuteChanged();
	}

	private async Task InvokeAsync(AutomationActionId actionId)
	{
		if (_session is null)
			return;
		try
		{
			var selection = _hostBridge.CaptureSelection();
			await _session.InvokeAsync(new(
				actionId,
				_session.Snapshot.CatalogRevision,
				_hostBridge.Revision,
				selection));
		}
		catch (Exception exception)
		{
			App.Logger.LogWarning(exception, "Automation Action {ActionId} failed to start.", actionId.Value);
			RunStatus = "AutomationActionFailed".GetLocalizedResource();
			RunStatusVisibility = Visibility.Visible;
		}
	}

	private async Task CancelAsync()
	{
		if (_session?.Snapshot.ActiveRuns.FirstOrDefault() is { } run)
			await _session.CancelAsync(run.Id);
	}
}

public sealed partial class AutomationActionButtonViewModel : ObservableObject
{
	private readonly Func<AutomationActionId, Task> _invoke;

	public AutomationActionId Id { get; }

	public string AutomationId => $"AutomationAction_{Id.Value}";

	[ObservableProperty]
	private string displayName = string.Empty;

	[ObservableProperty]
	private string description = string.Empty;

	[ObservableProperty]
	private bool isEnabled;

	public IAsyncRelayCommand InvokeCommand { get; }

	internal AutomationActionButtonViewModel(
		AutomationActionId id,
		Func<AutomationActionId, Task> invoke)
	{
		Id = id;
		_invoke = invoke;
		InvokeCommand = new AsyncRelayCommand(() => _invoke(Id), () => IsEnabled);
	}

	internal void Update(AutomationActionSnapshot snapshot)
	{
		DisplayName = snapshot.DisplayName;
		Description = snapshot.Description;
		IsEnabled = snapshot.Availability is AutomationActionAvailability.Available
			or AutomationActionAvailability.RequiresTrust;
		InvokeCommand.NotifyCanExecuteChanged();
	}
}
