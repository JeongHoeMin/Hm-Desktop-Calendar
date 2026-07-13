using System;
using System.Collections.Generic;

namespace HmDesktopCalendar.Calendar;

public sealed class CalendarMonth
{
    public CalendarMonth(int year, int month)
    {
        FirstDay = new DateOnly(year, month, 1);
        int offset = (int)FirstDay.DayOfWeek;
        GridStart = FirstDay.AddDays(-offset);
    }

    public DateOnly FirstDay { get; }
    public DateOnly GridStart { get; }
    public string DisplayName => $"{FirstDay.Year}년 {FirstDay.Month}월";

    public IReadOnlyList<DateOnly> GetDates()
    {
        var dates = new DateOnly[42];
        for (int i = 0; i < dates.Length; i++) dates[i] = GridStart.AddDays(i);
        return dates;
    }

    public CalendarMonth Previous() => new(FirstDay.AddMonths(-1).Year,
        FirstDay.AddMonths(-1).Month);
    public CalendarMonth Next() => new(FirstDay.AddMonths(1).Year,
        FirstDay.AddMonths(1).Month);
}
