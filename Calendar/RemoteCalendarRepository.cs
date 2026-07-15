using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Authentication;

namespace HmDesktopCalendar.Calendar;

public sealed class RemoteCalendarRepository : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string?>> _accessTokenProvider;

    public RemoteCalendarRepository(AuthSession session,
        string baseUrl = "http://127.0.0.1:3000",
        HttpMessageHandler? handler = null)
        : this(session.GetAccessTokenAsync, baseUrl, handler)
    {
    }

    public RemoteCalendarRepository(
        Func<CancellationToken, Task<string?>> accessTokenProvider,
        string baseUrl = "http://127.0.0.1:3000",
        HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(accessTokenProvider);
        _accessTokenProvider = accessTokenProvider;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public async Task<CalendarItem> UpsertItemAsync(CalendarItem item,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(cancellationToken);
        object? recurrence = item.Recurrence is null ? null : new
        {
            frequency = FormatFrequency(item.Recurrence.Frequency),
            interval = item.Recurrence.Interval,
            daysOfWeek = item.Recurrence.DaysOfWeek?
                .Select(day => (int)day).ToArray() ?? [],
            until = item.Recurrence.Until?.ToString("yyyy-MM-dd"),
            count = item.Recurrence.Count
        };
        var body = new
        {
            kind = item.Kind == CalendarItemKind.Anniversary
                ? "anniversary" : "schedule",
            title = item.Title,
            notes = item.Notes,
            startDate = item.StartDate.ToString("yyyy-MM-dd"),
            endDate = item.EndDate.ToString("yyyy-MM-dd"),
            startTime = item.StartTime?.ToString("HH:mm"),
            endTime = item.EndTime?.ToString("HH:mm"),
            allDay = item.IsAllDay,
            completed = item.IsCompleted,
            color = item.Color,
            recurrence,
            reminders = item.Reminders.Select(reminder => new
            {
                minutesBefore = reminder.MinutesBefore,
                timeOfDay = reminder.TimeOfDay?.ToString("HH:mm")
            }).ToArray()
        };
        using HttpResponseMessage response = await _http.PutAsJsonAsync(
            $"v2/calendar-items/{item.Id}", body, JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        RemoteCalendarItem remote = await response.Content
            .ReadFromJsonAsync<RemoteCalendarItem>(JsonOptions,
                cancellationToken) ??
            throw new InvalidDataException("서버 일정 응답이 비어 있습니다.");
        return ToItem(remote, remote.Cursor);
    }

    public async Task DeleteItemAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(cancellationToken);
        using HttpResponseMessage response = await _http.DeleteAsync(
            $"v2/calendar-items/{id}", cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    public async Task<DateCellDecoration> UpsertDecorationAsync(
        DateCellDecoration item,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(cancellationToken);
        var body = new
        {
            date = item.Date.ToString("yyyy-MM-dd"),
            kind = item.Kind switch
            {
                DateCellDecorationKind.Highlight => "highlight",
                DateCellDecorationKind.ColorDot => "colorDot",
                DateCellDecorationKind.Label => "label",
                _ => throw new ArgumentOutOfRangeException(nameof(item))
            },
            color = item.Color,
            label = item.Label
        };
        using HttpResponseMessage response = await _http.PutAsJsonAsync(
            $"v2/date-cell-decorations/{item.Id}", body, JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        RemoteDecoration remote = await response.Content
            .ReadFromJsonAsync<RemoteDecoration>(JsonOptions,
                cancellationToken) ??
            throw new InvalidDataException("서버 날짜 장식 응답이 비어 있습니다.");
        return ToDecoration(remote, remote.Cursor);
    }

    public async Task DeleteDecorationAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(cancellationToken);
        using HttpResponseMessage response = await _http.DeleteAsync(
            $"v2/date-cell-decorations/{id}", cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    public async Task<CalendarSyncPage> PullAsync(long after,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(cancellationToken);
        RemoteSyncPage page = await _http.GetFromJsonAsync<RemoteSyncPage>(
            $"v2/sync?after={after}&limit=500", JsonOptions,
            cancellationToken) ??
            throw new InvalidDataException("서버 동기화 응답이 비어 있습니다.");
        var items = new List<CalendarItem>();
        var decorations = new List<DateCellDecoration>();
        foreach (RemoteSyncChange change in page.Changes)
        {
            switch (change.EntityType)
            {
                case "calendarItem":
                    items.Add(ToItem(change.Payload.Deserialize<RemoteCalendarItem>(
                        JsonOptions) ?? throw new InvalidDataException(
                        "서버 일정 변경이 비어 있습니다."), change.Cursor));
                    break;
                case "dateCellDecoration":
                    decorations.Add(ToDecoration(change.Payload
                        .Deserialize<RemoteDecoration>(JsonOptions) ??
                        throw new InvalidDataException(
                            "서버 날짜 장식 변경이 비어 있습니다."),
                        change.Cursor));
                    break;
                default:
                    throw new InvalidDataException(
                        $"지원하지 않는 동기화 엔터티입니다: {change.EntityType}");
            }
        }
        return new CalendarSyncPage(items, decorations, page.NextCursor,
            page.HasMore);
    }

    private async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        string token = await _accessTokenProvider(cancellationToken) ??
            throw new InvalidOperationException("로그인이 필요합니다.");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private static CalendarItem ToItem(RemoteCalendarItem item, long cursor) =>
        new()
        {
            Id = item.Id,
            Kind = ParseKind(item.Kind),
            Title = item.Title,
            Notes = item.Notes,
            StartDate = item.StartDate,
            EndDate = item.EndDate,
            StartTime = ParseTime(item.StartTime),
            EndTime = ParseTime(item.EndTime),
            IsAllDay = item.AllDay,
            IsCompleted = item.Completed,
            Color = item.Color,
            Recurrence = item.Recurrence is null ? null : new RecurrenceRule(
                ParseFrequency(item.Recurrence.Frequency),
                item.Recurrence.Interval,
                item.Recurrence.DaysOfWeek.Select(day =>
                    (DayOfWeek)day).ToArray(),
                item.Recurrence.Until, item.Recurrence.Count),
            Reminders = item.Reminders.Select(reminder =>
                new CalendarReminder(reminder.MinutesBefore,
                    ParseTime(reminder.TimeOfDay))).ToList(),
            IsDeleted = item.Deleted,
            Revision = item.Revision,
            Cursor = cursor,
            UpdatedAt = item.UpdatedAt
        };

    private static DateCellDecoration ToDecoration(RemoteDecoration item,
        long cursor) => new()
    {
        Id = item.Id,
        Date = item.Date,
        Kind = item.Kind switch
        {
            "highlight" => DateCellDecorationKind.Highlight,
            "colorDot" => DateCellDecorationKind.ColorDot,
            "label" => DateCellDecorationKind.Label,
            _ => throw new InvalidDataException(
                $"지원하지 않는 날짜 장식 종류입니다: {item.Kind}")
        },
        Color = item.Color,
        Label = item.Label,
        IsDeleted = item.Deleted,
        Revision = item.Revision,
        Cursor = cursor,
        UpdatedAt = item.UpdatedAt
    };

    private static TimeOnly? ParseTime(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : TimeOnly.ParseExact(value,
            "HH:mm", CultureInfo.InvariantCulture);

    private static string FormatFrequency(RecurrenceFrequency frequency) =>
        frequency switch
        {
            RecurrenceFrequency.Daily => "daily",
            RecurrenceFrequency.Weekly => "weekly",
            RecurrenceFrequency.Monthly => "monthly",
            RecurrenceFrequency.Yearly => "yearly",
            _ => throw new ArgumentOutOfRangeException(nameof(frequency))
        };

    private static CalendarItemKind ParseKind(string kind) => kind switch
    {
        "schedule" => CalendarItemKind.Schedule,
        "anniversary" => CalendarItemKind.Anniversary,
        _ => throw new InvalidDataException(
            $"지원하지 않는 일정 종류입니다: {kind}")
    };

    private static RecurrenceFrequency ParseFrequency(string frequency) =>
        frequency switch
        {
            "daily" => RecurrenceFrequency.Daily,
            "weekly" => RecurrenceFrequency.Weekly,
            "monthly" => RecurrenceFrequency.Monthly,
            "yearly" => RecurrenceFrequency.Yearly,
            _ => throw new InvalidDataException(
                $"지원하지 않는 반복 주기입니다: {frequency}")
        };

    public void Dispose() => _http.Dispose();

    private sealed record RemoteSyncPage(RemoteSyncChange[] Changes,
        long NextCursor, bool HasMore);
    private sealed record RemoteSyncChange(string EntityType,
        JsonElement Payload, long Cursor);
    private sealed record RemoteCalendarItem(Guid Id, string Kind,
        string Title, string Notes, DateOnly StartDate, DateOnly EndDate,
        string? StartTime, string? EndTime, bool AllDay, bool Completed,
        string Color, RemoteRecurrence? Recurrence,
        RemoteReminder[] Reminders, bool Deleted, long Revision, long Cursor,
        DateTimeOffset UpdatedAt);
    private sealed record RemoteRecurrence(string Frequency, int Interval,
        int[] DaysOfWeek, DateOnly? Until, int? Count);
    private sealed record RemoteReminder(int MinutesBefore, string? TimeOfDay);
    private sealed record RemoteDecoration(Guid Id, DateOnly Date, string Kind,
        string Color, string Label, bool Deleted, long Revision, long Cursor,
        DateTimeOffset UpdatedAt);
}

public sealed record CalendarSyncPage(
    IReadOnlyList<CalendarItem> Items,
    IReadOnlyList<DateCellDecoration> Decorations,
    long NextCursor,
    bool HasMore);
