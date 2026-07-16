using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HmDesktopCalendar.Calendar;

namespace HmDesktopCalendar.ViewModels;

public enum ScheduleOverviewRange
{
    CurrentMonth,
    Next30Days,
    Next90Days,
    Custom
}

public enum ScheduleOverviewStatus
{
    All,
    Scheduled,
    Completed,
    Anniversary
}

public sealed class ScheduleOverviewItemViewModel(CalendarOccurrence occurrence)
{
    public CalendarOccurrence Occurrence { get; } = occurrence;
    public CalendarItem Item => Occurrence.Item;
    public DateOnly Date => Occurrence.Date;
    public string DateText => $"{Date:yyyy. M. d. (ddd)}";
    public string TimeText => Item.IsAllDay || Item.StartTime is null
        ? "하루 종일"
        : $"{Item.StartTime:HH\\:mm}";
    public string Title => Item.Title;
    public string Notes => Item.Notes;
    public string Color => Item.Color;
    public bool IsCompleted => Item.IsCompleted;
    public bool IsAnniversary => Item.IsAnniversary;
    public string BadgeText => Item.IsAnniversary ? "기념일" :
        Item.IsCompleted ? "완료" : "예정";
    public string SeriesText => Item.HasSeriesScope ? Item.SeriesBadgeText :
        string.Empty;
    public bool HasSeries => Item.HasSeriesScope;
}

public sealed partial class ScheduleOverviewViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan RefreshDebounce =
        TimeSpan.FromMilliseconds(200);
    private readonly ICalendarRepository _repository;
    private readonly Func<DateTime> _todayProvider;
    private readonly Func<Action, Task> _updateUi;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _dispose = new();
    private CancellationTokenSource? _refresh;
    private CancellationTokenSource? _debounce;
    private long _request;
    private bool _disposed;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _rangeIndex = 2;
    [ObservableProperty] private int _statusIndex;
    [ObservableProperty] private int _sortIndex;
    [ObservableProperty] private DateTimeOffset _customStart;
    [ObservableProperty] private DateTimeOffset _customEnd;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private ObservableCollection<ScheduleOverviewItemViewModel>
        _items = [];

    public bool IsCustomRange => RangeIndex == (int)ScheduleOverviewRange.Custom;
    public bool HasItems => Items.Count > 0;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsEmpty => !IsLoading && !HasItems && string.IsNullOrEmpty(ErrorMessage);
    public string ResultSummary => $"{Items.Count:N0}개 일정";

    public ScheduleOverviewViewModel(ICalendarRepository repository) : this(
        repository, () => DateTime.Today, UpdateOnUiThreadAsync,
        Task.Delay)
    {
    }

    public ScheduleOverviewViewModel(ICalendarRepository repository,
        Func<DateTime> todayProvider, Func<Action, Task> updateUi,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _repository = repository;
        _todayProvider = todayProvider;
        _updateUi = updateUi;
        _delay = delay;
        DateTime today = _todayProvider().Date;
        _customStart = new DateTimeOffset(today);
        _customEnd = new DateTimeOffset(today.AddDays(89));
        _repository.Changed += OnRepositoryChanged;
    }

    partial void OnSearchTextChanged(string value) => QueueRefresh();
    partial void OnStatusIndexChanged(int value) => QueueRefresh();
    partial void OnSortIndexChanged(int value) => QueueRefresh();
    partial void OnRangeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCustomRange));
        QueueRefresh();
    }
    partial void OnCustomStartChanged(DateTimeOffset value)
    {
        if (IsCustomRange) QueueRefresh();
    }
    partial void OnCustomEndChanged(DateTimeOffset value)
    {
        if (IsCustomRange) QueueRefresh();
    }

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long request = Interlocked.Increment(ref _request);
        CancellationTokenSource current = CancellationTokenSource
            .CreateLinkedTokenSource(_dispose.Token);
        CancellationTokenSource? previous = Interlocked.Exchange(
            ref _refresh, current);
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken cancellationToken = current.Token;
        await _updateUi(() => IsLoading = true);
        try
        {
            (DateOnly from, DateOnly to) = GetRange();
            IReadOnlyList<CalendarOccurrence> occurrences = await _repository
                .GetOccurrencesByRangeAsync(from, to, cancellationToken);
            string search = SearchText.Trim();
            var status = Enum.IsDefined(typeof(ScheduleOverviewStatus), StatusIndex)
                ? (ScheduleOverviewStatus)StatusIndex
                : ScheduleOverviewStatus.All;
            IEnumerable<CalendarOccurrence> filtered = occurrences.Where(value =>
                MatchesSearch(value.Item, search) && MatchesStatus(value.Item, status));
            filtered = SortIndex == 1
                ? filtered.OrderByDescending(value => value.Date)
                    .ThenByDescending(value => value.Item.StartTime)
                    .ThenBy(value => value.Item.Title, StringComparer.CurrentCulture)
                : filtered.OrderBy(value => value.Date)
                    .ThenBy(value => value.Item.StartTime)
                    .ThenBy(value => value.Item.Title, StringComparer.CurrentCulture);
            ScheduleOverviewItemViewModel[] snapshot = filtered
                .Select(value => new ScheduleOverviewItemViewModel(value)).ToArray();
            if (request != Volatile.Read(ref _request)) return;
            await _updateUi(() =>
            {
                if (request != Volatile.Read(ref _request)) return;
                Items.Clear();
                foreach (ScheduleOverviewItemViewModel item in snapshot)
                    Items.Add(item);
                ErrorMessage = string.Empty;
                NotifyResultState();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (request == Volatile.Read(ref _request))
                await _updateUi(() =>
                {
                    ErrorMessage = $"일정을 불러오지 못했습니다: {exception.Message}";
                    NotifyResultState();
                });
        }
        finally
        {
            if (request == Volatile.Read(ref _request))
                await _updateUi(() =>
                {
                    IsLoading = false;
                    NotifyResultState();
                });
        }
    }

    private (DateOnly From, DateOnly To) GetRange()
    {
        DateOnly today = DateOnly.FromDateTime(_todayProvider());
        var range = Enum.IsDefined(typeof(ScheduleOverviewRange), RangeIndex)
            ? (ScheduleOverviewRange)RangeIndex
            : ScheduleOverviewRange.Next90Days;
        return range switch
        {
            ScheduleOverviewRange.CurrentMonth =>
                (new DateOnly(today.Year, today.Month, 1),
                 new DateOnly(today.Year, today.Month,
                     DateTime.DaysInMonth(today.Year, today.Month))),
            ScheduleOverviewRange.Next30Days => (today, today.AddDays(29)),
            ScheduleOverviewRange.Custom => NormalizeCustomRange(),
            _ => (today, today.AddDays(89))
        };
    }

    private (DateOnly From, DateOnly To) NormalizeCustomRange()
    {
        DateOnly first = DateOnly.FromDateTime(CustomStart.LocalDateTime);
        DateOnly second = DateOnly.FromDateTime(CustomEnd.LocalDateTime);
        return first <= second ? (first, second) : (second, first);
    }

    private static bool MatchesSearch(CalendarItem item, string search) =>
        search.Length == 0 || item.Title.Contains(search,
            StringComparison.CurrentCultureIgnoreCase) || item.Notes.Contains(search,
            StringComparison.CurrentCultureIgnoreCase);

    private static bool MatchesStatus(CalendarItem item,
        ScheduleOverviewStatus status) => status switch
        {
            ScheduleOverviewStatus.Scheduled =>
                !item.IsAnniversary && !item.IsCompleted,
            ScheduleOverviewStatus.Completed =>
                !item.IsAnniversary && item.IsCompleted,
            ScheduleOverviewStatus.Anniversary => item.IsAnniversary,
            _ => true
        };

    private void OnRepositoryChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        CancellationTokenSource next = CancellationTokenSource
            .CreateLinkedTokenSource(_dispose.Token);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _debounce, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshAfterDelayAsync(next.Token);
    }

    private async Task RefreshAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _delay(RefreshDebounce, cancellationToken);
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
    }

    private void QueueRefresh()
    {
        if (!_disposed) _ = RefreshSafelyAsync();
    }

    private async Task RefreshSafelyAsync()
    {
        try { await RefreshAsync(); }
        catch (OperationCanceledException) when (_dispose.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    private void NotifyResultState()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ResultSummary));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
        _dispose.Cancel();
        _refresh?.Cancel();
        _debounce?.Cancel();
        _refresh?.Dispose();
        _debounce?.Dispose();
        _dispose.Dispose();
    }

    private static async Task UpdateOnUiThreadAsync(Action update) =>
        await Dispatcher.UIThread.InvokeAsync(update);
}
