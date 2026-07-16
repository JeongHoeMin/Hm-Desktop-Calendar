using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HmDesktopCalendar.Calendar;
using HmDesktopCalendar.Services;

namespace HmDesktopCalendar.ViewModels;

public sealed record CalendarWeekdayViewModel(string Text,
    DayOfWeek Day)
{
    public bool IsSunday => Day == DayOfWeek.Sunday;
    public bool IsSaturday => Day == DayOfWeek.Saturday;
}

public sealed partial class CalendarViewModel : ViewModelBase
{
    private readonly ICalendarRepository _repository;
    private readonly Func<DateTime> _todayProvider;
    private readonly Func<Action, Task> _updateUi;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CalendarMonth _month;
    private int _taskRowCapacity = 3;
    private CalendarWeekStart _weekStart = CalendarWeekStart.Sunday;
    private bool _colorWeekends = true;

    [ObservableProperty] private string _displayMonth = string.Empty;
    [ObservableProperty] private string _displayYear = string.Empty;
    [ObservableProperty] private string _displayMonthName = string.Empty;
    [ObservableProperty] private ObservableCollection<CalendarDayViewModel> _days = [];
    [ObservableProperty] private string _synchronizationStatus = "로컬 전용";
    [ObservableProperty] private bool _isSynchronizing;
    [ObservableProperty] private bool _isSynchronizationFailed;
    public ObservableCollection<CalendarWeekdayViewModel> WeekdayHeaders
        { get; } = [];
    private DateTimeOffset? _lastSuccessfulSynchronization;

    public CalendarViewModel(ICalendarRepository repository) : this(repository,
        () => DateTime.Today, UpdateOnUiThreadAsync)
    {
    }

    public CalendarViewModel(ICalendarRepository repository,
        Func<DateTime> todayProvider, Func<Action, Task> updateUi)
    {
        _repository = repository;
        _todayProvider = todayProvider;
        _updateUi = updateUi;
        DateTime today = _todayProvider();
        _month = new CalendarMonth(today.Year, today.Month);
        RebuildWeekdayHeaders();
    }

    public int CurrentYear => _month.FirstDay.Year;
    public int CurrentMonth => _month.FirstDay.Month;

    public bool SetDisplayOptions(CalendarWeekStart weekStart,
        bool colorWeekends)
    {
        bool weekStartChanged = _weekStart != weekStart;
        bool changed = weekStartChanged || _colorWeekends != colorWeekends;
        if (!changed) return false;
        _weekStart = weekStart;
        _colorWeekends = colorWeekends;
        if (weekStartChanged) RebuildWeekdayHeaders();
        return true;
    }

    public Task InitializeAsync() => RefreshAsync();
    public async Task PreviousMonthAsync() { _month = _month.Previous(); await RefreshAsync(); }
    public async Task NextMonthAsync() { _month = _month.Next(); await RefreshAsync(); }
    public Task SelectYearAsync(int year)
    {
        _month = new CalendarMonth(year, CurrentMonth);
        return RefreshAsync();
    }
    public Task SelectMonthAsync(int month)
    {
        _month = new CalendarMonth(CurrentYear, month);
        return RefreshAsync();
    }
    public Task GoToTodayAsync()
    {
        DateTime today = _todayProvider();
        _month = new CalendarMonth(today.Year, today.Month);
        return RefreshAsync();
    }

    public void SetSynchronizationAvailability(bool isLoggedIn)
    {
        if (!isLoggedIn) _lastSuccessfulSynchronization = null;
        IsSynchronizing = false;
        IsSynchronizationFailed = false;
        SynchronizationStatus = isLoggedIn
            ? FormatLastSuccess("동기화 대기 중")
            : "로컬 전용";
    }

    public void ApplySynchronizationState(CalendarSynchronizationState state)
    {
        IsSynchronizing =
            state.Status == CalendarSynchronizationStatus.InProgress;
        IsSynchronizationFailed =
            state.Status == CalendarSynchronizationStatus.Failed;
        if (state.Status == CalendarSynchronizationStatus.Succeeded)
            _lastSuccessfulSynchronization = state.OccurredAt;

        SynchronizationStatus = state.Status switch
        {
            CalendarSynchronizationStatus.InProgress => "동기화 중…",
            CalendarSynchronizationStatus.Succeeded =>
                FormatLastSuccess("동기화 완료"),
            CalendarSynchronizationStatus.Failed =>
                FormatLastSuccess("동기화 실패"),
            _ => SynchronizationStatus
        };
    }

    private string FormatLastSuccess(string fallback) =>
        _lastSuccessfulSynchronization is { } completed
            ? $"{fallback} · 마지막 성공 {completed.ToLocalTime():HH:mm}"
            : fallback;

    public void SetTaskRowCapacity(int capacity)
    {
        capacity = Math.Max(0, capacity);
        if (_taskRowCapacity == capacity) return;
        _taskRowCapacity = capacity;
        foreach (CalendarDayViewModel day in Days) day.SetCapacity(capacity);
    }

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            CalendarMonth month = _month;
            CalendarWeekStart weekStart = _weekStart;
            bool colorWeekends = _colorWeekends;
            var dates = month.GetDates(weekStart == CalendarWeekStart.Monday
                ? DayOfWeek.Monday : DayOfWeek.Sunday);
            IReadOnlyDictionary<DateOnly, string> holidayNames =
                KoreanHolidayCalculator.GetHolidayNames(dates[0], dates[^1]);
            Task<IReadOnlyList<CalendarOccurrence>> occurrenceTask =
                _repository.GetOccurrencesByRangeAsync(dates[0], dates[^1]);
            Task<IReadOnlyList<DateCellDecoration>> decorationTask =
                _repository.GetDecorationsByRangeAsync(dates[0], dates[^1]);
            await Task.WhenAll(occurrenceTask, decorationTask);
            IReadOnlyList<CalendarOccurrence> occurrences =
                await occurrenceTask;
            var backgrounds = (await decorationTask)
                .Where(item => item.Kind == DateCellDecorationKind.Highlight)
                .GroupBy(item => item.Date)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(item => item.UpdatedAt)
                        .First().Color);
            var snapshot = new System.Collections.Generic.List<CalendarDayViewModel>();
            foreach (DateOnly date in dates)
            {
                var dateItems = occurrences
                    .Where(occurrence => occurrence.Date == date)
                    .Select(occurrence => occurrence.Item)
                    .OrderByDescending(item => item.IsAnniversary)
                    .ThenBy(item => item.IsCompleted)
                    .ThenBy(item => item.StartTime)
                    .ThenBy(item => item.Title)
                    .ToArray();
                CalendarItem[] schedules = dateItems
                    .Where(item => !item.IsAnniversary).ToArray();
                int incompleteCount = schedules.Count(item => !item.IsCompleted);
                int completedCount = schedules.Length - incompleteCount;
                var taskRows = dateItems.Select(item =>
                    new CalendarTaskPreviewViewModel(
                        item.IsAnniversary ? string.Empty : item.StartTime is { } time
                            ? $"{time:HH:mm}" : string.Empty,
                        item.Title, item.IsCompleted, item.Color,
                        item.IsAnniversary)).ToArray();
                snapshot.Add(new CalendarDayViewModel(
                    date,
                    date.Month == month.FirstDay.Month,
                    incompleteCount,
                    completedCount,
                    taskRows,
                    _taskRowCapacity,
                    backgrounds.GetValueOrDefault(date),
                    holidayNames.GetValueOrDefault(date), colorWeekends));
            }
            await _updateUi(() =>
            {
                bool sameDates = Days.Count == snapshot.Count &&
                    Days.Select(day => day.Date)
                        .SequenceEqual(snapshot.Select(day => day.Date));
                if (!sameDates)
                {
                    Days.Clear();
                    foreach (CalendarDayViewModel day in snapshot) Days.Add(day);
                }
                else
                {
                    for (int index = 0; index < Days.Count; index++)
                    {
                        CalendarDayViewModel source = snapshot[index];
                        Days[index].Update(source.Date, source.IsCurrentMonth,
                            source.IncompleteCount, source.CompletedCount,
                            source.AllTasks,
                            backgrounds.GetValueOrDefault(source.Date),
                            holidayNames.GetValueOrDefault(source.Date),
                            colorWeekends);
                    }
                }
                DisplayMonth = month.DisplayName;
                DisplayYear = $"{month.FirstDay.Year}년";
                DisplayMonthName = $"{month.FirstDay.Month}월";
                OnPropertyChanged(nameof(CurrentYear));
                OnPropertyChanged(nameof(CurrentMonth));
            });
        }
        finally { _refreshGate.Release(); }
    }

    private void RebuildWeekdayHeaders()
    {
        DayOfWeek first = _weekStart == CalendarWeekStart.Monday
            ? DayOfWeek.Monday : DayOfWeek.Sunday;
        WeekdayHeaders.Clear();
        for (int offset = 0; offset < 7; offset++)
        {
            DayOfWeek day = (DayOfWeek)(((int)first + offset) % 7);
            WeekdayHeaders.Add(new CalendarWeekdayViewModel(day switch
            {
                DayOfWeek.Sunday => "일",
                DayOfWeek.Monday => "월",
                DayOfWeek.Tuesday => "화",
                DayOfWeek.Wednesday => "수",
                DayOfWeek.Thursday => "목",
                DayOfWeek.Friday => "금",
                DayOfWeek.Saturday => "토",
                _ => string.Empty
            }, day));
        }
    }

    private static async Task UpdateOnUiThreadAsync(Action update) =>
        await Dispatcher.UIThread.InvokeAsync(update);
}
