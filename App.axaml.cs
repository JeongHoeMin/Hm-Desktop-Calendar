using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using HmDesktopCalendar.Authentication;
using HmDesktopCalendar.Calendar;
using HmDesktopCalendar.DesktopIntegration;
using HmDesktopCalendar.Reminders;
using HmDesktopCalendar.Services;
using HmDesktopCalendar.ViewModels;
using HmDesktopCalendar.Views;

namespace HmDesktopCalendar;

public partial class App : Application
{
    private readonly AuthSession _session;
    private readonly SyncingCalendarRepository _repository;
    private readonly RealtimeSyncClient _realtime;
    private readonly ReminderScheduler _reminders;
    private readonly CalendarSettingsStore _settings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _taskLock = new();
    private readonly HashSet<Task> _backgroundTasks = [];
    private readonly Queue<ReminderNotification> _reminderQueue = [];
    private readonly HashSet<string> _queuedReminderKeys = [];
    private readonly object _shutdownLock = new();
    private Task? _shutdownTask;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private CalendarViewModel? _calendar;
    private EditWindow? _editor;
    private DesktopCalendarWindowCoordinator? _windowHost;
    private DesktopInteractionCoordinator? _interaction;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayPositionItem;
    private NativeMenuItem? _trayAuthenticationItem;
    private MainWindow? _mainWindow;
    private LoginWindow? _loginWindow;
    private ReminderWindow? _reminderWindow;
    private ScheduleOverviewWindow? _overviewWindow;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsViewModel;
    private CalendarBoundsController? _positionController;
    private bool _initializing;
    private bool _sessionChanging;
    private bool _shuttingDown;

    public App()
    {
        string serverUrl = Environment.GetEnvironmentVariable(
            "HM_CALENDAR_SERVER_URL") ?? "http://127.0.0.1:3000";
        _session = new AuthSession(serverUrl);
        var local = new LocalCalendarRepository();
        _repository = new SyncingCalendarRepository(local,
            new RemoteCalendarRepository(_session, serverUrl), _session);
        _reminders = new ReminderScheduler(_repository,
            new JsonReminderDeviceStateStore(), new SystemReminderClock(),
            () => _session.User?.Id.ToString("N") ?? "anonymous");
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
            _calendar.SetDisplayOptions(_settings.Current.WeekStart,
                _settings.Current.ColorWeekends);
            ApplyCalendarAppearance(_settings.Current.FontScale,
                _settings.Current.BackgroundOpacity);
            _windowHost = new DesktopCalendarWindowCoordinator(window, saved);
            _positionController = new CalendarBoundsController();
            _settingsViewModel = new SettingsViewModel(
                typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                _settings, ApplyDefaultWindowBounds, _session.IsLoggedIn,
                ApplyCalendarDisplayOptions, ApplyCalendarAppearance,
                new AutoStartRegistrar());
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
            _reminders.ReminderDue += OnReminderDue;
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
            _reminders.Start();
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
        => OpenEditor(date, null);

    private void OpenEditor(DateOnly date, CalendarItem? item)
    {
        if (_positionController?.IsEditing == true) return;
        if (_editor is not null)
        {
            EditWindow editor = _editor;
            RunBackground(async () =>
            {
                if (item is null) await editor.ShowDateAsync(date);
                else await editor.ShowItemAsync(date, item);
                editor.Activate();
            });
            return;
        }

        var viewModel = new CalendarEditorViewModel(date, _repository);
        _editor = new EditWindow(viewModel, item);
        _editor.Closed += OnEditorClosed;
        _editor.Show();
        _editor.Activate();
    }

    private void OnMenuRequested(object? sender, EventArgs eventArgs)
    {
        _mainWindow?.ShowMenu(_session.IsLoggedIn,
            _positionController?.IsEditing == true,
            ShowSettings,
            ShowScheduleOverview,
            ShowLogin,
            () => RunBackground(() => _session.LogoutAsync(_lifetime.Token)),
            BeginPositionEdit, CompletePositionEdit, CancelPositionEdit);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        if (_settingsViewModel is null) return;
        var window = _settingsWindow = new SettingsWindow(_settingsViewModel);
        window.Closed += OnSettingsClosed;
        window.Show();
        window.Activate();
    }

    private void OnSettingsClosed(object? sender, EventArgs eventArgs)
    {
        if (_settingsWindow is not { } window) return;
        window.Closed -= OnSettingsClosed;
        _settingsWindow = null;
    }

    private void ShowScheduleOverview()
    {
        if (_overviewWindow is not null)
        {
            _overviewWindow.Activate();
            return;
        }
        var window = _overviewWindow = new ScheduleOverviewWindow(
            new ScheduleOverviewViewModel(_repository));
        window.EditRequested += OnOverviewEditRequested;
        window.Closed += OnOverviewClosed;
        window.Show();
        window.Activate();
    }

    private void OnOverviewEditRequested(object? sender,
        ScheduleOverviewEditRequestedEventArgs eventArgs) =>
        OpenEditor(eventArgs.Date, eventArgs.Item);

    private void OnOverviewClosed(object? sender, EventArgs eventArgs)
    {
        if (_overviewWindow is not { } window) return;
        window.EditRequested -= OnOverviewEditRequested;
        window.Closed -= OnOverviewClosed;
        _overviewWindow = null;
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
        RefreshTrayMenuState();
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
        RefreshTrayMenuState();
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
        RefreshTrayMenuState();
    }

    private bool ApplyDefaultWindowBounds(PixelRect bounds)
    {
        if (_windowHost is null || _positionController is null) return false;
        WindowTransitionResult result = _windowHost.TryCommitDesktop(bounds);
        if (!result.Success)
        {
            _mainWindow?.SetBoundsEditError(result.Message);
            return false;
        }
        if (_positionController.IsEditing)
            _positionController.EndEditing();
        _mainWindow?.SetBoundsEditing(false);
        RefreshTrayMenuState();
        return true;
    }

    private void ApplyCalendarDisplayOptions(CalendarWeekStart weekStart,
        bool colorWeekends)
    {
        if (_calendar?.SetDisplayOptions(weekStart, colorWeekends) != true)
            return;
        RunBackground(_calendar.RefreshAsync);
    }

    private void ApplyCalendarAppearance(CalendarFontScale fontScale,
        double backgroundOpacity)
    {
        CalendarAppearanceTokens tokens = CalendarAppearance.Create(fontScale,
            backgroundOpacity);
        Resources["SocarCalendarHeaderFontSize"] = tokens.HeaderFontSize;
        Resources["SocarCalendarWeekdayFontSize"] = tokens.WeekdayFontSize;
        Resources["SocarCalendarDayFontSize"] = tokens.DayFontSize;
        Resources["SocarCalendarBadgeFontSize"] = tokens.BadgeFontSize;
        Resources["SocarCalendarCountFontSize"] = tokens.CountFontSize;
        Resources["SocarCalendarTimeFontSize"] = tokens.TimeFontSize;
        Resources["SocarCalendarTaskFontSize"] = tokens.TaskFontSize;
        Resources["SocarCalendarMoreFontSize"] = tokens.MoreFontSize;
        Resources["SocarCalendarCellHeaderHeight"] =
            new GridLength(tokens.CellHeaderHeight);
        Resources["SocarCalendarTaskRowHeight"] = tokens.TaskRowHeight;
        Resources["SocarCalendarSurfaceBrush"] = new SolidColorBrush(
            Color.Parse("#F7F9FC")) { Opacity = tokens.BackgroundOpacity };
        Resources["SocarCalendarCellBrush"] = new SolidColorBrush(
            Colors.White) { Opacity = tokens.BackgroundOpacity };
    }

    private void OnRealtimeSync(object? sender, EventArgs eventArgs) =>
        _repository.RequestSynchronization();

    private void OnRepositoryChanged(object? sender, EventArgs eventArgs)
    {
        if (_initializing || _sessionChanging || _calendar is null) return;
        RunBackground(_calendar.RefreshAsync);
    }

    private void OnReminderDue(object? sender, ReminderNotification notification) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_shuttingDown || !_queuedReminderKeys.Add(notification.Key)) return;
            _reminderQueue.Enqueue(notification);
            ShowNextReminder();
        });

    private void ShowNextReminder()
    {
        if (_reminderWindow is not null || _reminderQueue.Count == 0 ||
            _shuttingDown) return;
        ReminderNotification notification = _reminderQueue.Dequeue();
        var window = _reminderWindow = new ReminderWindow(notification);
        window.ActionRequested += OnReminderAction;
        window.Closed += OnReminderWindowClosed;
        window.Show();
        window.Activate();
    }

    private void OnReminderAction(object? sender,
        ReminderWindowActionEventArgs eventArgs)
    {
        if (sender is not ReminderWindow window) return;
        ReminderNotification notification = window.Notification;
        _queuedReminderKeys.Remove(notification.Key);
        if (eventArgs.Action == ReminderWindowAction.Snooze)
            RunBackground(() => _reminders.SnoozeAsync(notification,
                eventArgs.SnoozeMinutes, _lifetime.Token));
        else
            RunBackground(() => _reminders.AcknowledgeAsync(notification,
                _lifetime.Token));
        if (eventArgs.Action == ReminderWindowAction.Edit)
            OnDateEdit(this, notification.OccurrenceDate);
    }

    private void OnReminderWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is ReminderWindow window)
        {
            window.ActionRequested -= OnReminderAction;
            window.Closed -= OnReminderWindowClosed;
        }
        _reminderWindow = null;
        ShowNextReminder();
    }

    private void OnSynchronizationStateChanged(object? sender,
        CalendarSynchronizationState state) => Dispatcher.UIThread.Post(() =>
        {
            if (_calendar is null) return;
            if (_session.IsLoggedIn)
                _calendar.ApplySynchronizationState(state);
            else
                _calendar.SetSynchronizationAvailability(false);
        });

    private void OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _settingsViewModel?.UpdateSession(_session.IsLoggedIn);
            RefreshTrayMenuState();
        });
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
        var settings = new NativeMenuItem("설정");
        settings.Click += (_, _) => ShowSettings();
        var overview = new NativeMenuItem("일정 모아보기");
        overview.Click += (_, _) => ShowScheduleOverview();
        _trayPositionItem = new NativeMenuItem("달력 위치 및 크기 수정");
        _trayPositionItem.Click += (_, _) =>
        {
            if (_positionController?.IsEditing == true)
                CompletePositionEdit();
            else
                BeginPositionEdit();
        };
        _trayAuthenticationItem = new NativeMenuItem(
            _settingsViewModel?.AuthenticationMenuText ?? "로그인 / 회원가입");
        _trayAuthenticationItem.Click += (_, _) =>
        {
            if (_session.IsLoggedIn)
                RunBackground(() => _session.LogoutAsync(_lifetime.Token));
            else
                ShowLogin();
        };
        var exit = new NativeMenuItem("종료");
        exit.Click += async (_, _) => await RequestShutdownAsync();
        var menu = new NativeMenu();
        menu.Add(settings);
        menu.Add(overview);
        menu.Add(_trayPositionItem);
        menu.Add(_trayAuthenticationItem);
        menu.Add(new NativeMenuItemSeparator());
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

    private void RefreshTrayMenuState()
    {
        if (_trayAuthenticationItem is not null)
            _trayAuthenticationItem.Header =
                _settingsViewModel?.AuthenticationMenuText ??
                (_session.IsLoggedIn ? "로그아웃" : "로그인 / 회원가입");
        if (_trayPositionItem is not null)
            _trayPositionItem.Header = _positionController?.IsEditing == true
                ? "위치 및 크기 저장"
                : "달력 위치 및 크기 수정";
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
        _reminders.ReminderDue -= OnReminderDue;
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

        await _reminders.StopAsync();
        _reminderWindow?.CloseSilently();
        _lifetime.Cancel();
        await _realtime.StopAsync();
        await _repository.StopAsync();
        await WaitForBackgroundTasksAsync();

        _editor?.CloseWithoutConfirmation();
        _overviewWindow?.Close();
        _settingsWindow?.Close();
        _loginWindow?.Close();
        _interaction?.Dispose();
        _windowHost?.Dispose();
        _trayIcon?.Dispose();
        await _realtime.DisposeAsync();
        await _repository.DisposeAsync();
        await _reminders.DisposeAsync();
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
