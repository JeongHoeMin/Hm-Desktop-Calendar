using System;
using Avalonia;
using HmDesktopCalendar.Services;

namespace HmDesktopCalendar.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public static readonly PixelRect DefaultWindowBounds =
        new(100, 100, 980, 680);

    private readonly CalendarSettingsStore _settings;
    private readonly Func<PixelRect, bool> _applyWindowBounds;
    private readonly Action<CalendarWeekStart, bool>? _applyDisplayOptions;
    private bool _isLoggedIn;
    private string _statusMessage = string.Empty;
    private bool _hasError;

    public SettingsViewModel(string appVersion,
        CalendarSettingsStore settings,
        Func<PixelRect, bool> applyWindowBounds, bool isLoggedIn = false,
        Action<CalendarWeekStart, bool>? applyDisplayOptions = null)
    {
        AppVersion = appVersion;
        _settings = settings;
        _applyWindowBounds = applyWindowBounds;
        _isLoggedIn = isLoggedIn;
        _applyDisplayOptions = applyDisplayOptions;
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

    private void SetStatus(string message, bool hasError)
    {
        HasError = hasError;
        StatusMessage = message;
        OnPropertyChanged(nameof(HasStatus));
    }
}
