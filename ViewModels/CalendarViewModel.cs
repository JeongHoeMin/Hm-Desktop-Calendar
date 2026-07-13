using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HmDesktopCalendar.Calendar;
using HmDesktopCalendar.Todos;

namespace HmDesktopCalendar.ViewModels;

public sealed partial class CalendarViewModel : ViewModelBase
{
    private readonly ITodoRepository _repository;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CalendarMonth _month;
    private int _taskRowCapacity = 3;

    [ObservableProperty] private string _displayMonth = string.Empty;
    [ObservableProperty] private ObservableCollection<CalendarDayViewModel> _days = [];

    public CalendarViewModel(ITodoRepository repository)
    {
        _repository = repository;
        DateTime today = DateTime.Today;
        _month = new CalendarMonth(today.Year, today.Month);
    }

    public Task InitializeAsync() => RefreshAsync();
    public async Task PreviousMonthAsync() { _month = _month.Previous(); await RefreshAsync(); }
    public async Task NextMonthAsync() { _month = _month.Next(); await RefreshAsync(); }

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
            var todos = await _repository.GetByRangeAsync(dates[0], dates[^1]);
            var snapshot = new System.Collections.Generic.List<CalendarDayViewModel>();
            foreach (DateOnly date in dates)
            {
                var dateTodos = todos.Where(item => item.Date == date)
                    .OrderBy(item => item.IsCompleted)
                    .ThenBy(item => item.Time)
                    .ThenBy(item => item.Title)
                    .ToArray();
                int incompleteCount = dateTodos.Count(item => !item.IsCompleted);
                int completedCount = dateTodos.Length - incompleteCount;
                var taskRows = dateTodos.Select(item =>
                    new CalendarTaskPreviewViewModel(
                        item.Time is { } time ? $"{time:HH:mm}" : string.Empty,
                        item.Title, item.IsCompleted)).ToArray();
                snapshot.Add(new CalendarDayViewModel(
                    date,
                    date.Month == month.FirstDay.Month,
                    incompleteCount,
                    completedCount,
                    taskRows,
                    _taskRowCapacity));
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
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
            });
        }
        finally { _refreshGate.Release(); }
    }
}
