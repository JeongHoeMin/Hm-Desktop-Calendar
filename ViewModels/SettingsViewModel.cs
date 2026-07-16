using System;
using Avalonia;
using HmDesktopCalendar.DesktopIntegration;
using HmDesktopCalendar.Services;

namespace HmDesktopCalendar.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public static readonly PixelRect DefaultWindowBounds =
        new(100, 100, 980, 680);

    private readonly CalendarSettingsStore _settings;
    private readonly Func<PixelRect, bool> _applyWindowBounds;
    private readonly Action<CalendarWeekStart, bool>? _applyDisplayOptions;
    private readonly Action<CalendarFontScale, double>? _applyAppearance;
    private readonly IAutoStartRegistrar? _autoStartRegistrar;
    private readonly BackupService? _backupService;
    private readonly Action<string>? _openFolder;
    private bool _isLoggedIn;
    private string _statusMessage = string.Empty;
    private bool _hasError;
    private bool _isAutoStartEnabled;
    private bool _isAutoStartAvailable;
    private string _autoStartError = string.Empty;
    private DateTimeOffset? _lastBackupAt;

    public SettingsViewModel(string appVersion,
        CalendarSettingsStore settings,
        Func<PixelRect, bool> applyWindowBounds, bool isLoggedIn = false,
        Action<CalendarWeekStart, bool>? applyDisplayOptions = null,
        Action<CalendarFontScale, double>? applyAppearance = null,
        IAutoStartRegistrar? autoStartRegistrar = null,
        BackupService? backupService = null,
        Action<string>? openFolder = null)
    {
        AppVersion = appVersion;
        _settings = settings;
        _applyWindowBounds = applyWindowBounds;
        _isLoggedIn = isLoggedIn;
        _applyDisplayOptions = applyDisplayOptions;
        _applyAppearance = applyAppearance;
        _autoStartRegistrar = autoStartRegistrar;
        _backupService = backupService;
        _openFolder = openFolder;
        _lastBackupAt = backupService?.GetLastBackupAt();
        ApplyAutoStartStatus(autoStartRegistrar?.GetStatus() ??
            AutoStartStatus.Unavailable("자동 시작 기능을 사용할 수 없습니다."));
    }

    public string AppVersion { get; }
    public bool IsLoggedIn => _isLoggedIn;
    public string AuthenticationMenuText =>
        IsLoggedIn ? "로그아웃" : "로그인 / 회원가입";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }
    public int WeekStartIndex
    {
        get => _settings.Current.WeekStart == CalendarWeekStart.Monday ? 1 : 0;
        set
        {
            if (value is not (0 or 1)) return;
            CalendarWeekStart weekStart = value == 1
                ? CalendarWeekStart.Monday : CalendarWeekStart.Sunday;
            if (_settings.Current.WeekStart == weekStart) return;
            if (!TrySaveDisplayOptions(_settings.Current with
                { WeekStart = weekStart })) return;
            OnPropertyChanged();
        }
    }
    public bool ColorWeekends
    {
        get => _settings.Current.ColorWeekends;
        set
        {
            if (_settings.Current.ColorWeekends == value) return;
            if (!TrySaveDisplayOptions(_settings.Current with
                { ColorWeekends = value })) return;
            OnPropertyChanged();
        }
    }
    public int FontScaleIndex
    {
        get => (int)_settings.Current.FontScale;
        set
        {
            if (value is < 0 or > 2) return;
            var fontScale = (CalendarFontScale)value;
            if (_settings.Current.FontScale == fontScale) return;
            if (!TrySaveAppearance(_settings.Current with
                { FontScale = fontScale })) return;
            OnPropertyChanged();
        }
    }
    public double BackgroundOpacity
    {
        get => _settings.Current.BackgroundOpacity;
        set
        {
            double opacity = Math.Round(Math.Clamp(value,
                CalendarAppearance.MinimumOpacity,
                CalendarAppearance.MaximumOpacity), 2);
            if (Math.Abs(_settings.Current.BackgroundOpacity - opacity) <
                0.001) return;
            if (!TrySaveAppearance(_settings.Current with
                { BackgroundOpacity = opacity })) return;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundOpacityText));
        }
    }
    public string BackgroundOpacityText => $"{BackgroundOpacity:P0}";
    public bool IsAutoStartEnabled
    {
        get => _isAutoStartEnabled;
        set
        {
            if (!_isAutoStartAvailable || _isAutoStartEnabled == value ||
                _autoStartRegistrar is null) return;
            ApplyAutoStartStatus(_autoStartRegistrar.SetEnabled(value));
        }
    }
    public bool IsAutoStartAvailable => _isAutoStartAvailable;
    public string AutoStartError => _autoStartError;
    public bool HasAutoStartError => !string.IsNullOrWhiteSpace(AutoStartError);
    public string LastBackupText => _lastBackupAt is { } completed
        ? $"마지막 백업: {completed.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "아직 생성된 백업이 없습니다.";

    public void UpdateSession(bool isLoggedIn)
    {
        if (!SetProperty(ref _isLoggedIn, isLoggedIn, nameof(IsLoggedIn)))
            return;
        OnPropertyChanged(nameof(AuthenticationMenuText));
    }

    public bool ResetWindowPosition()
    {
        try
        {
            if (!_applyWindowBounds(DefaultWindowBounds))
            {
                SetStatus("창 위치를 초기화하지 못했습니다.", true);
                return false;
            }
            _settings.Save(DefaultWindowBounds);
            SetStatus("창 위치와 크기를 기본값으로 초기화했습니다.", false);
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"설정 저장 실패: {exception.Message}", true);
            return false;
        }
    }

    public void UpdateBackupStatus(BackupResult result)
    {
        if (result.LastBackupAt is { } completed)
            _lastBackupAt = completed;
        OnPropertyChanged(nameof(LastBackupText));
    }

    public bool OpenBackupFolder()
    {
        if (_backupService is null || _openFolder is null) return false;
        try
        {
            _backupService.EnsureBackupRoot();
            _openFolder(_backupService.BackupRoot);
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"백업 폴더 열기 실패: {exception.Message}", true);
            return false;
        }
    }

    private bool TrySaveDisplayOptions(AppSettings settings)
    {
        try
        {
            _settings.Save(settings);
            _applyDisplayOptions?.Invoke(settings.WeekStart,
                settings.ColorWeekends);
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"설정 저장 실패: {exception.Message}", true);
            return false;
        }
    }

    private bool TrySaveAppearance(AppSettings settings)
    {
        try
        {
            _settings.Save(settings);
            _applyAppearance?.Invoke(settings.FontScale,
                settings.BackgroundOpacity);
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"설정 저장 실패: {exception.Message}", true);
            return false;
        }
    }

    private void ApplyAutoStartStatus(AutoStartStatus status)
    {
        SetProperty(ref _isAutoStartAvailable, status.IsAvailable,
            nameof(IsAutoStartAvailable));
        SetProperty(ref _isAutoStartEnabled, status.IsEnabled,
            nameof(IsAutoStartEnabled));
        if (SetProperty(ref _autoStartError, status.ErrorMessage,
                nameof(AutoStartError)))
            OnPropertyChanged(nameof(HasAutoStartError));
    }

    private void SetStatus(string message, bool hasError)
    {
        HasError = hasError;
        StatusMessage = message;
        OnPropertyChanged(nameof(HasStatus));
    }
}
