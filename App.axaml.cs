using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using HmDesktopCalendar.Authentication;
using HmDesktopCalendar.DesktopIntegration;
using HmDesktopCalendar.Services;
using HmDesktopCalendar.Todos;
using HmDesktopCalendar.ViewModels;
using HmDesktopCalendar.Views;

namespace HmDesktopCalendar;

public partial class App : Application
{
    private readonly AuthSession _session;
    private readonly SyncingTodoRepository _repository;
    private readonly RealtimeSyncClient _realtime;
    private readonly CalendarSettingsStore _settings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _taskLock = new();
    private readonly HashSet<Task> _backgroundTasks = [];
    private readonly object _shutdownLock = new();
    private Task? _shutdownTask;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private CalendarViewModel? _calendar;
    private EditWindow? _editor;
    private DesktopCalendarWindowCoordinator? _windowHost;
    private DesktopInteractionCoordinator? _interaction;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private LoginWindow? _loginWindow;
    private CalendarBoundsController? _positionController;
    private bool _initializing;
    private bool _sessionChanging;
    private bool _shuttingDown;

    public App()
    {
        string serverUrl = Environment.GetEnvironmentVariable(
            "HM_CALENDAR_SERVER_URL") ?? "http://127.0.0.1:3000";
        _session = new AuthSession(serverUrl);
        var local = new LocalTodoRepository();
        _repository = new SyncingTodoRepository(local,
            new RemoteTodoRepository(_session, serverUrl), _session);
        string realtimeUrl = serverUrl.Replace("http://", "ws://")
            .Replace("https://", "wss://").TrimEnd('/') + "/v1/realtime";
        _realtime = new RealtimeSyncClient(_session, realtimeUrl);
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _calendar = new CalendarViewModel(_repository);
            var window = _mainWindow = new MainWindow { DataContext = _calendar };
            desktop.MainWindow = window;
            PixelRect saved = _settings.Load(MainWindow.CalendarWidth,
                MainWindow.CalendarHeight);
            _windowHost = new DesktopCalendarWindowCoordinator(window, saved);
            _positionController = new CalendarBoundsController();
            var interactionNative = new Win32WindowNativeApi();
            _interaction = new DesktopInteractionCoordinator(
                new GlobalPointerMonitor(interactionNative),
                new DesktopIconHitTester(),
                new DesktopSurfaceHitTester(interactionNative,
                    new DesktopShellLocator(interactionNative)),
                (x, y) => window.HitTestPoint(_windowHost.ToClientPoint(x, y)));
            _interaction.PreviousMonthRequested += OnPreviousMonth;
            _interaction.NextMonthRequested += OnNextMonth;
            _interaction.YearPickerRequested += OnYearPickerRequested;
            _interaction.MonthPickerRequested += OnMonthPickerRequested;
            _interaction.FlyoutDismissRequested += OnFlyoutDismissRequested;
            _interaction.DateEditRequested += OnDateEdit;
            _interaction.MenuRequested += OnMenuRequested;
            _realtime.SyncRequested += OnRealtimeSync;
            _repository.Changed += OnRepositoryChanged;
            _repository.SynchronizationStateChanged += OnSynchronizationStateChanged;
            _session.Changed += OnSessionChanged;
            window.Opened += OnMainWindowOpened;
            _windowHost.Start();
            CreateTrayIcon();
            desktop.Exit += OnDesktopExit;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void OnMainWindowOpened(object? sender, EventArgs eventArgs) =>
        RunBackground(InitializeApplicationAsync);

    private async Task InitializeApplicationAsync()
    {
        if (_calendar is null || _interaction is null) return;
        _initializing = true;
        try
        {
            await _session.TryRestoreAsync(_lifetime.Token);
            _calendar.SetSynchronizationAvailability(_session.IsLoggedIn);
            await _repository.SwitchScopeAsync(_session.User?.Id, _lifetime.Token);
            if (_session.IsLoggedIn)
                await _repository.SynchronizeAsync(_lifetime.Token);
            await _calendar.InitializeAsync();
        }
        finally { _initializing = false; }

        if (_lifetime.IsCancellationRequested) return;
        _realtime.Start();
        try { _interaction.Start(); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"바탕화면 입력 감시 시작 실패: {exception}");
        }
    }

    private void OnPreviousMonth(object? sender, EventArgs eventArgs)
    {
        if (_calendar is not null)
            RunBackground(_calendar.PreviousMonthAsync);
    }

    private void OnNextMonth(object? sender, EventArgs eventArgs)
    {
        if (_calendar is not null)
            RunBackground(_calendar.NextMonthAsync);
    }

    private void OnYearPickerRequested(object? sender, EventArgs eventArgs) =>
        _mainWindow?.ShowYearPicker();

    private void OnMonthPickerRequested(object? sender, EventArgs eventArgs) =>
        _mainWindow?.ShowMonthPicker();

    private void OnFlyoutDismissRequested(object? sender, EventArgs eventArgs) =>
        _mainWindow?.DismissFlyouts();

    private void OnDateEdit(object? sender, DateOnly date)
    {
        if (_positionController?.IsEditing == true) return;
        if (_editor is not null)
        {
            EditWindow editor = _editor;
            RunBackground(async () =>
            {
                await editor.ShowDateAsync(date);
                editor.Activate();
            });
            return;
        }

        _editor = new EditWindow(new TodoEditorViewModel(date, _repository));
        _editor.Closed += OnEditorClosed;
        _editor.Show();
        _editor.Activate();
    }

    private void OnMenuRequested(object? sender, EventArgs eventArgs)
    {
        _mainWindow?.ShowMenu(_session.IsLoggedIn,
            _positionController?.IsEditing == true,
            ShowLogin,
            () => RunBackground(() => _session.LogoutAsync(_lifetime.Token)),
            BeginPositionEdit, CompletePositionEdit, CancelPositionEdit);
    }

    private void ShowLogin()
    {
        if (_loginWindow is not null)
        {
            _loginWindow.Activate();
            return;
        }
        _loginWindow = new LoginWindow(_session);
        _loginWindow.Closed += (_, _) => _loginWindow = null;
        _loginWindow.Show();
        _loginWindow.Activate();
    }

    private void BeginPositionEdit()
    {
        if (_windowHost is null || _positionController is null) return;
        WindowTransitionResult result = _windowHost.BeginForegroundEditing();
        if (!result.Success)
        {
            _mainWindow?.SetBoundsEditError(result.Message);
            return;
        }
        _positionController.BeginEditing(_windowHost.Bounds);
        _mainWindow?.SetBoundsEditing(true);
    }

    private void CompletePositionEdit()
    {
        if (_positionController is null || _windowHost is null) return;
        PixelRect bounds = _windowHost.CaptureCurrentBounds();
        WindowTransitionResult result = _windowHost.TryCommitDesktop(bounds);
        if (!result.Success)
        {
            _mainWindow?.SetBoundsEditError(result.Message);
            return;
        }
        try { _settings.Save(_windowHost.Bounds); }
        catch (Exception exception)
        {
            _windowHost.BeginForegroundEditing();
            _mainWindow?.SetBoundsEditError(
                $"설정 파일 저장 실패: {exception.Message}");
            return;
        }
        _positionController.EndEditing();
        _mainWindow?.SetBoundsEditing(false);
    }

    private void CancelPositionEdit()
    {
        if (_positionController is null || _windowHost is null) return;
        PixelRect restored = _positionController.OriginalBounds;
        WindowTransitionResult result = _windowHost.TryCommitDesktop(restored);
        if (!result.Success)
        {
            _mainWindow?.SetBoundsEditError(result.Message);
            return;
        }
        _positionController.EndEditing();
        _mainWindow?.SetBoundsEditing(false);
    }

    private void OnRealtimeSync(object? sender, EventArgs eventArgs) =>
        _repository.RequestSynchronization();

    private void OnRepositoryChanged(object? sender, EventArgs eventArgs)
    {
        if (_initializing || _sessionChanging || _calendar is null) return;
        RunBackground(_calendar.RefreshAsync);
    }

    private void OnSynchronizationStateChanged(object? sender,
        TodoSynchronizationState state) => Dispatcher.UIThread.Post(() =>
        {
            if (_calendar is null) return;
            if (_session.IsLoggedIn)
                _calendar.ApplySynchronizationState(state);
            else
                _calendar.SetSynchronizationAvailability(false);
        });

    private void OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        if (_initializing) return;
        _calendar?.SetSynchronizationAvailability(_session.IsLoggedIn);
        RunBackground(ApplySessionChangeAsync);
    }

    private async Task ApplySessionChangeAsync()
    {
        await _sessionGate.WaitAsync(_lifetime.Token);
        _sessionChanging = true;
        try
        {
            await _repository.SwitchScopeAsync(_session.User?.Id, _lifetime.Token);
            if (_session.IsLoggedIn)
                await _repository.SynchronizeAsync(_lifetime.Token);
            if (_calendar is not null) await _calendar.RefreshAsync();
        }
        finally
        {
            _sessionChanging = false;
            _sessionGate.Release();
        }
    }

    private void OnEditorClosed(object? sender, EventArgs eventArgs)
    {
        if (_editor is null) return;
        _editor.Closed -= OnEditorClosed;
        _editor = null;
    }

    private void CreateTrayIcon()
    {
        var exit = new NativeMenuItem("종료");
        exit.Click += async (_, _) => await RequestShutdownAsync();
        var menu = new NativeMenu();
        menu.Add(exit);
        _trayIcon = new TrayIcon
        {
            ToolTipText = "HmDesktopCalendar",
            Menu = menu,
            Icon = new WindowIcon(AssetLoader.Open(new Uri(
                "avares://HmDesktopCalendar/Assets/avalonia-logo.ico")))
        };
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    public async Task RequestShutdownAsync()
    {
        await EnsureShutdownAsync();
        _desktop?.Shutdown();
    }

    private Task EnsureShutdownAsync()
    {
        lock (_shutdownLock)
            return _shutdownTask ??= ShutdownCoreAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        _realtime.SyncRequested -= OnRealtimeSync;
        _repository.Changed -= OnRepositoryChanged;
        _repository.SynchronizationStateChanged -= OnSynchronizationStateChanged;
        _session.Changed -= OnSessionChanged;
        if (_mainWindow is not null)
            _mainWindow.Opened -= OnMainWindowOpened;
        if (_interaction is not null)
        {
            _interaction.PreviousMonthRequested -= OnPreviousMonth;
            _interaction.NextMonthRequested -= OnNextMonth;
            _interaction.YearPickerRequested -= OnYearPickerRequested;
            _interaction.MonthPickerRequested -= OnMonthPickerRequested;
            _interaction.FlyoutDismissRequested -= OnFlyoutDismissRequested;
            _interaction.DateEditRequested -= OnDateEdit;
            _interaction.MenuRequested -= OnMenuRequested;
        }

        _lifetime.Cancel();
        await _realtime.StopAsync();
        await _repository.StopAsync();
        await WaitForBackgroundTasksAsync();

        _editor?.Close();
        _loginWindow?.Close();
        _interaction?.Dispose();
        _windowHost?.Dispose();
        _trayIcon?.Dispose();
        await _realtime.DisposeAsync();
        await _repository.DisposeAsync();
        _session.Dispose();
        _sessionGate.Dispose();
        _lifetime.Dispose();
    }

    private async void OnDesktopExit(object? sender,
        ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        if (_shuttingDown) return;
        try { await EnsureShutdownAsync(); }
        catch (Exception exception) { Console.Error.WriteLine(exception); }
    }

    private void RunBackground(Func<Task> action)
    {
        if (_shuttingDown) return;
        Task task = RunSafelyAsync(action);
        lock (_taskLock) _backgroundTasks.Add(task);
        _ = task.ContinueWith(completed =>
        {
            lock (_taskLock) _backgroundTasks.Remove(completed);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_shuttingDown) { }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
    }

    private async Task WaitForBackgroundTasksAsync()
    {
        Task[] tasks;
        lock (_taskLock) tasks = [.. _backgroundTasks];
        if (tasks.Length > 0) await Task.WhenAll(tasks);
    }
}
