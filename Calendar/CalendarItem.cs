using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HmDesktopCalendar.Calendar;

public enum CalendarItemKind
{
    Schedule,
    Anniversary
}

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}

public sealed record RecurrenceRule(
    RecurrenceFrequency Frequency,
    int Interval = 1,
    IReadOnlyList<DayOfWeek>? DaysOfWeek = null,
    DateOnly? Until = null,
    int? Count = null);

public sealed record CalendarReminder(int MinutesBefore);

public sealed class CalendarItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CalendarItemKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsCompleted { get; set; }
    public string Color { get; set; } = CalendarTextColor.DefaultColor;
    public RecurrenceRule? Recurrence { get; set; }
    public List<CalendarReminder> Reminders { get; set; } = [];
    public bool IsDeleted { get; set; }
    public long Revision { get; set; }
    public long Cursor { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsAnniversary => Kind == CalendarItemKind.Anniversary;

    [JsonIgnore]
    public bool HasSeriesScope => Recurrence is not null || EndDate > StartDate;

    [JsonIgnore]
    public bool HasScheduleSeriesScope => !IsAnniversary && HasSeriesScope;

    [JsonIgnore]
    public string SeriesBadgeText => IsAnniversary ? "매년" :
        Recurrence is { } recurrence ? recurrence.Frequency switch
        {
            RecurrenceFrequency.Daily => recurrence.Interval == 1
                ? "매일" : $"{recurrence.Interval}일마다",
            RecurrenceFrequency.Weekly => recurrence.Interval == 1
                ? "매주" : $"{recurrence.Interval}주마다",
            RecurrenceFrequency.Monthly => recurrence.Interval == 1
                ? "매월" : $"{recurrence.Interval}개월마다",
            RecurrenceFrequency.Yearly => recurrence.Interval == 1
                ? "매년" : $"{recurrence.Interval}년마다",
            _ => "반복"
        } : EndDate > StartDate
            ? $"{EndDate.DayNumber - StartDate.DayNumber + 1}일 기간"
            : string.Empty;
}
