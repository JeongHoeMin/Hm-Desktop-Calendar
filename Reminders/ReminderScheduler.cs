using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Calendar;

namespace HmDesktopCalendar.Reminders;

public sealed record ReminderNotification(string Key, CalendarItem Item,
    DateOnly OccurrenceDate, DateTimeOffset ScheduledAt, bool IsRecovered,
    bool IsSnoozed);

public sealed class ReminderScheduler : IAsyncDisposable
{
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromHours(24);
    private readonly ICalendarRepository _repository;
    private readonly IReminderDeviceStateStore _stateStore;
    private readonly IReminderClock _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly Func<string> _scopeProvider;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _lifetime;
    private Task? _loop;

    public event EventHandler<ReminderNotification>? ReminderDue;

    public ReminderScheduler(ICalendarRepository repository,
        IReminderDeviceStateStore stateStore, IReminderClock clock,
        Func<string> scopeProvider, TimeZoneInfo? timeZone = null)
    {
        _repository = repository;
        _stateStore = stateStore;
        _clock = clock;
        _scopeProvider = scopeProvider;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _lifetime = new CancellationTokenSource();
        _repository.Changed += OnRepositoryChanged;
        _loop = RunAsync(_lifetime.Token);
    }

    public async Task StopAsync()
    {
        if (_loop is null) return;
        _repository.Changed -= OnRepositoryChanged;
        _lifetime!.Cancel();
        try { await _loop; }
        catch (OperationCanceledException) { }
        _loop = null;
        _lifetime.Dispose();
        _lifetime = null;
    }

    public async Task<IReadOnlyList<ReminderNotification>> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.Now;
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            now, _timeZone).Date);
        IReadOnlyList<CalendarOccurrence> occurrences = await _repository
            .GetOccurrencesByRangeAsync(today.AddDays(-1), today.AddDays(366),
                cancellationToken);
        IReadOnlyDictionary<string, ReminderDeviceState> states =
            await _stateStore.ReadAsync(cancellationToken);
        var candidates = new List<(ReminderNotification Notification,
            DateTimeOffset Due)>();

        foreach (CalendarOccurrence occurrence in occurrences)
        {
            CalendarItem item = occurrence.Item;
            if (item.IsDeleted || item.IsCompleted ||
                item.Recurrence is null && occurrence.Date != item.StartDate)
                continue;
            foreach (CalendarReminder reminder in item.Reminders)
            {
                TimeOnly? anchorTime = item.StartTime ?? reminder.TimeOfDay;
                if (anchorTime is null) continue;
                DateTimeOffset scheduled = ToInstant(occurrence.Date, anchorTime.Value)
                    .AddMinutes(-reminder.MinutesBefore);
                string key = CreateKey(_scopeProvider(), item.Id,
                    occurrence.Date, reminder);
                states.TryGetValue(key, out ReminderDeviceState? state);
                if (state?.AcknowledgedAt is not null) continue;
                DateTimeOffset due = state?.SnoozedUntil ?? scheduled;
                if (state is not null && state.SnoozedUntil is null) continue;
                if (due > now || now - due > RecoveryWindow) continue;
                candidates.Add((new ReminderNotification(key, item,
                    occurrence.Date, scheduled, scheduled < now,
                    state?.SnoozedUntil is not null), due));
            }
        }

        var result = new List<ReminderNotification>();
        foreach (var candidate in candidates.OrderBy(value => value.Due)
                     .ThenBy(value => value.Notification.Key,
                         StringComparer.Ordinal))
        {
            await _stateStore.WriteAsync(candidate.Notification.Key,
                new ReminderDeviceState(now), cancellationToken);
            result.Add(candidate.Notification);
        }
        return result;
    }

    public Task AcknowledgeAsync(ReminderNotification notification,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.Now;
        return _stateStore.WriteAsync(notification.Key,
            new ReminderDeviceState(now, now), cancellationToken);
    }

    public async Task SnoozeAsync(ReminderNotification notification, int minutes,
        CancellationToken cancellationToken = default)
    {
        if (minutes is not (5 or 10 or 30))
            throw new ArgumentOutOfRangeException(nameof(minutes));
        DateTimeOffset now = _clock.Now;
        await _stateStore.WriteAsync(notification.Key,
            new ReminderDeviceState(now, null, now.AddMinutes(minutes)),
            cancellationToken);
        Wake();
    }

    public static string CreateKey(string scope, Guid itemId,
        DateOnly occurrenceDate, CalendarReminder reminder) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{scope}|{itemId:N}|{occurrenceDate:yyyy-MM-dd}|{reminder.MinutesBefore}|{reminder.TimeOfDay:HH\\:mm}");

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<ReminderNotification> due =
                await ScanAsync(cancellationToken);
            foreach (ReminderNotification notification in due)
                ReminderDue?.Invoke(this, notification);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            Task delay = _clock.DelayAsync(TimeSpan.FromMinutes(1), wait.Token);
            Task wake = _wake.WaitAsync(wait.Token);
            await Task.WhenAny(delay, wake);
            wait.Cancel();
            try { await Task.WhenAll(delay, wake); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }

    private DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (_timeZone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, _timeZone.GetUtcOffset(local));
    }

    private void OnRepositoryChanged(object? sender, EventArgs eventArgs) => Wake();
    private void Wake() { if (_wake.CurrentCount == 0) _wake.Release(); }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _wake.Dispose();
    }
}
