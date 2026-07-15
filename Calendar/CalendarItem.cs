using System;
using System.Collections.Generic;

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
}
