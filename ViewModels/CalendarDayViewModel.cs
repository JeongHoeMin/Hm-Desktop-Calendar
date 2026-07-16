using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HmDesktopCalendar.Calendar;

namespace HmDesktopCalendar.ViewModels;

public sealed class CalendarTaskPreviewViewModel
{
    public CalendarTaskPreviewViewModel(string timeText, string title,
        bool isCompleted, string color, bool isAnniversary = false)
    {
        TimeText = timeText;
        Title = title;
        IsCompleted = isCompleted;
        IsAnniversary = isAnniversary;
        TextBrush = Color.TryParse(color, out Color parsed)
            ? new SolidColorBrush(parsed) : Brushes.Black;
    }

    public string TimeText { get; }
    public string Title { get; }
    public bool IsCompleted { get; }
    public bool IsAnniversary { get; }
    public string BadgeText => IsAnniversary ? "기념일" : string.Empty;
    public IBrush TextBrush { get; }
    public double Opacity => IsCompleted ? 0.5 : 1.0;
    public TextDecorationCollection? TitleDecorations =>
        IsCompleted ? TextDecorations.Strikethrough : null;
}

public sealed class CalendarDayViewModel : ObservableObject
{
    private DateOnly _date;
    private bool _isCurrentMonth;
    private int _incompleteCount;
    private int _completedCount;
    private IReadOnlyList<CalendarTaskPreviewViewModel> _allTasks =
        Array.Empty<CalendarTaskPreviewViewModel>();
    private int _capacity;
    private int _hiddenCount;
    private string? _backgroundColor;
    private string? _holidayName;
    private bool _colorWeekends;
    private static readonly IBrush HolidayForegroundBrush =
        new SolidColorBrush(Color.Parse("#FF5065"));
    private static readonly IBrush SundayForegroundBrush =
        new SolidColorBrush(Color.Parse("#FF5065"));
    private static readonly IBrush SaturdayForegroundBrush =
        new SolidColorBrush(Color.Parse("#005AFF"));

    public CalendarDayViewModel(DateOnly date, bool isCurrentMonth,
        int incompleteCount, int completedCount,
        IReadOnlyList<CalendarTaskPreviewViewModel> allTasks, int capacity,
        string? backgroundColor = null, string? holidayName = null,
        bool colorWeekends = true)
    {
        _capacity = capacity;
        Update(date, isCurrentMonth, incompleteCount, completedCount, allTasks,
            backgroundColor, holidayName, colorWeekends);
    }

    public ObservableCollection<CalendarTaskPreviewViewModel> VisibleTasks
        { get; } = [];
    public IReadOnlyList<CalendarTaskPreviewViewModel> AllTasks => _allTasks;

    public DateOnly Date
    {
        get => _date;
        private set
        {
            if (SetProperty(ref _date, value))
            {
                OnPropertyChanged(nameof(DayText));
                OnPropertyChanged(nameof(DayForegroundBrush));
            }
        }
    }

    public bool IsCurrentMonth
    {
        get => _isCurrentMonth;
        private set
        {
            if (SetProperty(ref _isCurrentMonth, value))
                OnPropertyChanged(nameof(CellOpacity));
        }
    }

    public int IncompleteCount
    {
        get => _incompleteCount;
        private set
        {
            if (!SetProperty(ref _incompleteCount, value)) return;
            OnPropertyChanged(nameof(IncompleteText));
            OnPropertyChanged(nameof(HasIncomplete));
        }
    }

    public int CompletedCount
    {
        get => _completedCount;
        private set
        {
            if (!SetProperty(ref _completedCount, value)) return;
            OnPropertyChanged(nameof(CompletedText));
            OnPropertyChanged(nameof(HasCompleted));
        }
    }

    public int HiddenCount
    {
        get => _hiddenCount;
        private set
        {
            if (!SetProperty(ref _hiddenCount, value)) return;
            OnPropertyChanged(nameof(HasHiddenTasks));
            OnPropertyChanged(nameof(HiddenText));
        }
    }

    public string DayText => Date.Day.ToString();
    public string IncompleteText => $"미완료 {IncompleteCount}";
    public string CompletedText => $"완료 {CompletedCount}";
    public bool HasIncomplete => IncompleteCount > 0;
    public bool HasCompleted => CompletedCount > 0;
    public bool HasHiddenTasks => HiddenCount > 0;
    public bool IsHoliday => !string.IsNullOrWhiteSpace(_holidayName);
    public string HolidayName => _holidayName ?? string.Empty;
    public string HiddenText => $"+{HiddenCount}개 더 있음";
    public double CellOpacity => IsCurrentMonth ? 1.0 : 0.45;
    public IBrush? CellBackgroundBrush => ParseBrush(_backgroundColor);
    public IBrush? CellForegroundBrush
    {
        get
        {
            CellColorValidation validation = CalendarCellColor.Validate(
                _backgroundColor);
            return validation.IsValid ? ParseBrush(
                CalendarCellColor.GetForeground(validation.NormalizedColor)) :
                null;
        }
    }
    public IBrush? DayForegroundBrush => CellForegroundBrush ??
        (IsHoliday ? HolidayForegroundBrush : GetWeekendForeground());

    public void SetCapacity(int capacity)
    {
        capacity = Math.Max(0, capacity);
        if (_capacity == capacity) return;
        _capacity = capacity;
        ApplyVisibility();
    }

    public void Update(DateOnly date, bool isCurrentMonth,
        int incompleteCount, int completedCount,
        IReadOnlyList<CalendarTaskPreviewViewModel> allTasks,
        string? backgroundColor = null, string? holidayName = null,
        bool colorWeekends = true)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IncompleteCount = incompleteCount;
        CompletedCount = completedCount;
        _allTasks = allTasks;
        if (!string.Equals(_backgroundColor, backgroundColor,
            StringComparison.OrdinalIgnoreCase))
        {
            _backgroundColor = backgroundColor;
            OnPropertyChanged(nameof(CellBackgroundBrush));
            OnPropertyChanged(nameof(CellForegroundBrush));
            OnPropertyChanged(nameof(DayForegroundBrush));
        }
        if (!string.Equals(_holidayName, holidayName,
            StringComparison.Ordinal))
        {
            _holidayName = holidayName;
            OnPropertyChanged(nameof(IsHoliday));
            OnPropertyChanged(nameof(HolidayName));
            OnPropertyChanged(nameof(DayForegroundBrush));
        }
        if (_colorWeekends != colorWeekends)
        {
            _colorWeekends = colorWeekends;
            OnPropertyChanged(nameof(DayForegroundBrush));
        }
        ApplyVisibility();
    }

    private IBrush? GetWeekendForeground()
    {
        if (!_colorWeekends) return null;
        return Date.DayOfWeek switch
        {
            DayOfWeek.Sunday => SundayForegroundBrush,
            DayOfWeek.Saturday => SaturdayForegroundBrush,
            _ => null
        };
    }

    private void ApplyVisibility()
    {
        int visibleCount = _allTasks.Count <= _capacity
            ? _allTasks.Count
            : Math.Max(0, _capacity - 1);
        VisibleTasks.Clear();
        foreach (CalendarTaskPreviewViewModel task in
                 _allTasks.Take(visibleCount))
            VisibleTasks.Add(task);
        HiddenCount = _allTasks.Count - visibleCount;
    }

    private static IBrush? ParseBrush(string? value) =>
        Color.TryParse(value, out Color color)
            ? new SolidColorBrush(color) : null;
}
