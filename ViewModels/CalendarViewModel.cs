using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HmDesktopCalendar.Calendar;

namespace HmDesktopCalendar.ViewModels;

public sealed partial class CalendarViewModel : ViewModelBase
{
    private readonly ICalendarRepository _repository;
    private readonly Func<DateTime> _todayProvider;
    private readonly Func<Action, Task> _updateUi;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CalendarMonth _month;
    private int _taskRowCapacity = 3;

    [ObservableProperty] private string _displayMonth = string.Empty;
    [ObservableProperty] private string _displayYear = string.Empty;
    [ObservableProperty] private string _displayMonthName = string.Empty;
    [ObservableProperty] private ObservableCollection<CalendarDayViewModel> _days = [];
    [ObservableProperty] private string _synchronizationStatus = "로컬 전용";
    [ObservableProperty] private bool _isSynchronizing;
    [ObservableProperty] private bool _isSynchronizationFailed;
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
    }

    public int CurrentYear => _month.FirstDay.Year;
    public int CurrentMonth => _month.FirstDay.Month;

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
            var dates = month.GetDates();
            var occurrences = await _repository.GetOccurrencesByRangeAsync(
                dates[0], dates[^1]);
            var snapshot = new System.Collections.Generic.List<CalendarDayViewModel>();
            foreach (DateOnly date in dates)
            {
                var dateItems = occurrences
                    .Where(occurrence => occurrence.Date == date &&
                        occurrence.Item.Kind == CalendarItemKind.Schedule)
                    .Select(occurrence => occurrence.Item)
                    .OrderBy(item => item.IsCompleted)
                    .ThenBy(item => item.StartTime)
                    .ThenBy(item => item.Title)
                    .ToArray();
                int incompleteCount = dateItems.Count(item => !item.IsCompleted);
                int completedCount = dateItems.Length - incompleteCount;
                var taskRows = dateItems.Select(item =>
                    new CalendarTaskPreviewViewModel(
                        item.StartTime is { } time
                            ? $"{time:HH:mm}" : string.Empty,
                        item.Title, item.IsCompleted, item.Color)).ToArray();
                snapshot.Add(new CalendarDayViewModel(
                    date,
                    date.Month == month.FirstDay.Month,
                    incompleteCount,
                    completedCount,
                    taskRows,
                    _taskRowCapacity));
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
                            source.AllTasks);
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

    private static async Task UpdateOnUiThreadAsync(Action update) =>
        await Dispatcher.UIThread.InvokeAsync(update);
}
