using System;
using System.Collections.Generic;
using System.Linq;

namespace HmDesktopCalendar.Calendar;

public sealed record CalendarOccurrence(CalendarItem Item, DateOnly Date)
{
    public Guid SeriesId => Item.Id;
}

public static class CalendarOccurrenceEngine
{
    public static IReadOnlyList<CalendarOccurrence> GetOccurrences(
        CalendarItem item, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateRange(from, to);
        if (item.IsDeleted) return [];
        ValidateItem(item);

        return item.Recurrence is { } recurrence
            ? ExpandRecurrence(item, recurrence, from, to)
            : ExpandPeriod(item, from, to);
    }

    public static IReadOnlyList<CalendarOccurrence> GetOccurrences(
        IEnumerable<CalendarItem> items, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateRange(from, to);

        return items
            .SelectMany(item => GetOccurrences(item, from, to))
            .OrderBy(occurrence => occurrence.Date)
            .ThenBy(occurrence => occurrence.Item.IsCompleted)
            .ThenBy(occurrence => occurrence.Item.StartTime)
            .ThenBy(occurrence => occurrence.Item.Title,
                StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.SeriesId)
            .ToArray();
    }

    internal static void ValidateItem(CalendarItem item)
    {
        if (item.EndDate < item.StartDate)
            throw new ArgumentException(
                "종료 날짜는 시작 날짜보다 빠를 수 없습니다.", nameof(item));

        if (item.Kind == CalendarItemKind.Anniversary &&
            (item.IsCompleted || item.Recurrence is not
                { Frequency: RecurrenceFrequency.Yearly, Interval: 1,
                  Until: null, Count: null }))
            throw new ArgumentException(
                "기념일은 완료되지 않은 무기한 연간 반복 일정이어야 합니다.",
                nameof(item));

        if (item.Recurrence is not { } recurrence) return;
        if (item.EndDate != item.StartDate)
            throw new ArgumentException(
                "기간 일정과 반복 일정은 함께 사용할 수 없습니다.", nameof(item));
        if (recurrence.Interval < 1)
            throw new ArgumentException("반복 간격은 1 이상이어야 합니다.",
                nameof(item));
        if (!Enum.IsDefined(recurrence.Frequency))
            throw new ArgumentException("유효하지 않은 반복 주기입니다.",
                nameof(item));
        if (recurrence.Count is not null)
            throw new ArgumentException("반복 횟수는 지원하지 않습니다.",
                nameof(item));
        if (recurrence.Until is { } until && until < item.StartDate)
            throw new ArgumentException(
                "반복 종료일은 시작 날짜보다 빠를 수 없습니다.", nameof(item));
        if (recurrence.Frequency != RecurrenceFrequency.Weekly) return;
        if (recurrence.DaysOfWeek is not { Count: > 0 })
            throw new ArgumentException(
                "매주 반복은 한 개 이상의 요일이 필요합니다.", nameof(item));
        if (recurrence.DaysOfWeek.Any(day => !Enum.IsDefined(day)))
            throw new ArgumentException("유효하지 않은 반복 요일입니다.",
                nameof(item));
    }

    private static IReadOnlyList<CalendarOccurrence> ExpandPeriod(
        CalendarItem item, DateOnly from, DateOnly to)
    {
        DateOnly first = Max(item.StartDate, from);
        DateOnly last = Min(item.EndDate, to);
        return first > last ? [] : ExpandDates(item, first, last, _ => true);
    }

    private static IReadOnlyList<CalendarOccurrence> ExpandRecurrence(
        CalendarItem item, RecurrenceRule recurrence, DateOnly from,
        DateOnly to)
    {
        DateOnly first = Max(item.StartDate, from);
        DateOnly last = recurrence.Until is { } until
            ? Min(until, to)
            : to;
        if (first > last) return [];

        HashSet<DayOfWeek>? weeklyDays =
            recurrence.Frequency == RecurrenceFrequency.Weekly
                ? recurrence.DaysOfWeek!.ToHashSet()
                : null;
        return ExpandDates(item, first, last,
            date => Matches(item.StartDate, date, recurrence, weeklyDays));
    }

    private static IReadOnlyList<CalendarOccurrence> ExpandDates(
        CalendarItem item, DateOnly first, DateOnly last,
        Func<DateOnly, bool> include)
    {
        var occurrences = new List<CalendarOccurrence>();
        DateOnly date = first;
        while (true)
        {
            if (include(date)) occurrences.Add(new CalendarOccurrence(item, date));
            if (date == last) break;
            date = date.AddDays(1);
        }
        return occurrences;
    }

    private static bool Matches(DateOnly start, DateOnly candidate,
        RecurrenceRule recurrence, HashSet<DayOfWeek>? weeklyDays)
    {
        int interval = recurrence.Interval;
        return recurrence.Frequency switch
        {
            RecurrenceFrequency.Daily =>
                (candidate.DayNumber - start.DayNumber) % interval == 0,
            RecurrenceFrequency.Weekly =>
                weeklyDays!.Contains(candidate.DayOfWeek) &&
                WeeksBetween(start, candidate) % interval == 0,
            RecurrenceFrequency.Monthly =>
                candidate.Day == start.Day &&
                MonthsBetween(start, candidate) % interval == 0,
            RecurrenceFrequency.Yearly =>
                candidate.Month == start.Month &&
                candidate.Day == start.Day &&
                (candidate.Year - start.Year) % interval == 0,
            _ => throw new ArgumentOutOfRangeException(nameof(recurrence))
        };
    }

    private static int WeeksBetween(DateOnly start, DateOnly candidate)
    {
        int startWeek = start.DayNumber - (int)start.DayOfWeek;
        int candidateWeek = candidate.DayNumber - (int)candidate.DayOfWeek;
        return (candidateWeek - startWeek) / 7;
    }

    private static int MonthsBetween(DateOnly start, DateOnly candidate) =>
        (candidate.Year - start.Year) * 12 + candidate.Month - start.Month;

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
    }

    private static DateOnly Max(DateOnly first, DateOnly second) =>
        first >= second ? first : second;

    private static DateOnly Min(DateOnly first, DateOnly second) =>
        first <= second ? first : second;
}
