using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HmDesktopCalendar.Calendar;

public sealed record KoreanHoliday(DateOnly Date, string Name,
    bool IsSubstitute)
{
    public string DisplayName => IsSubstitute
        ? $"대체공휴일({Name})"
        : Name;
}

public static class KoreanHolidayCalculator
{
    private const int MinimumYear = 1900;
    private const int MaximumYear = 2050;
    private static readonly ConcurrentDictionary<int,
        IReadOnlyList<KoreanHoliday>> Cache = new();

    public static IReadOnlyList<KoreanHoliday> GetHolidays(int year) =>
        year is < MinimumYear or > MaximumYear
            ? Array.Empty<KoreanHoliday>()
            : Cache.GetOrAdd(year, BuildHolidays);

    public static IReadOnlyDictionary<DateOnly, string> GetHolidayNames(
        DateOnly from, DateOnly to)
    {
        if (from > to) return new Dictionary<DateOnly, string>();

        return Enumerable.Range(from.Year, to.Year - from.Year + 1)
            .SelectMany(GetHolidays)
            .Where(holiday => holiday.Date >= from && holiday.Date <= to)
            .GroupBy(holiday => holiday.Date)
            .ToDictionary(group => group.Key, group => string.Join("·",
                group.Select(holiday => holiday.DisplayName).Distinct()));
    }

    private static IReadOnlyList<KoreanHoliday> BuildHolidays(int year)
    {
        var holidays = new List<KoreanHoliday>();
        var substituteEligible = new HashSet<string>
        {
            "삼일절", "노동절", "어린이날", "부처님오신날", "제헌절",
            "광복절", "개천절", "한글날", "성탄절"
        };

        AddFixed(holidays, year, 1, 1, "신정");
        AddFixed(holidays, year, 3, 1, "삼일절");
        AddFixed(holidays, year, 5, 1, "노동절");
        AddFixed(holidays, year, 5, 5, "어린이날");
        AddFixed(holidays, year, 6, 6, "현충일");
        AddFixed(holidays, year, 7, 17, "제헌절");
        AddFixed(holidays, year, 8, 15, "광복절");
        AddFixed(holidays, year, 10, 3, "개천절");
        AddFixed(holidays, year, 10, 9, "한글날");
        AddFixed(holidays, year, 12, 25, "성탄절");

        DateOnly lunarNewYear = ToSolarDate(year, 1, 1);
        DateOnly[] lunarNewYearBreak =
            [lunarNewYear.AddDays(-1), lunarNewYear, lunarNewYear.AddDays(1)];
        foreach (DateOnly date in lunarNewYearBreak)
            holidays.Add(new KoreanHoliday(date, "설날", false));

        DateOnly buddhasBirthday = ToSolarDate(year, 4, 8);
        holidays.Add(new KoreanHoliday(buddhasBirthday, "부처님오신날", false));

        DateOnly chuseok = ToSolarDate(year, 8, 15);
        DateOnly[] chuseokBreak =
            [chuseok.AddDays(-1), chuseok, chuseok.AddDays(1)];
        foreach (DateOnly date in chuseokBreak)
            holidays.Add(new KoreanHoliday(date, "추석", false));

        var originalCounts = holidays.GroupBy(holiday => holiday.Date)
            .ToDictionary(group => group.Key, group => group.Count());
        var candidates = new List<SubstituteCandidate>();
        AddBreakCandidate(candidates, lunarNewYearBreak, "설날",
            originalCounts);
        AddBreakCandidate(candidates, chuseokBreak, "추석", originalCounts);

        foreach (IGrouping<DateOnly, KoreanHoliday> group in holidays
                     .Where(holiday => substituteEligible.Contains(holiday.Name))
                     .GroupBy(holiday => holiday.Date))
        {
            if (IsWeekend(group.Key) || originalCounts[group.Key] > 1)
                candidates.Add(new SubstituteCandidate(group.Key,
                    group.Last().Name));
        }

        var occupiedDates = holidays.Select(holiday => holiday.Date).ToHashSet();
        foreach (SubstituteCandidate candidate in candidates
                     .OrderBy(candidate => candidate.AfterDate)
                     .ThenBy(candidate => candidate.Name))
        {
            DateOnly substituteDate = candidate.AfterDate.AddDays(1);
            while (IsWeekend(substituteDate) ||
                   occupiedDates.Contains(substituteDate))
                substituteDate = substituteDate.AddDays(1);
            occupiedDates.Add(substituteDate);
            holidays.Add(new KoreanHoliday(substituteDate, candidate.Name, true));
        }

        return holidays.OrderBy(holiday => holiday.Date).ToArray();
    }

    private static void AddBreakCandidate(
        ICollection<SubstituteCandidate> candidates,
        IReadOnlyList<DateOnly> breakDates, string name,
        IReadOnlyDictionary<DateOnly, int> originalCounts)
    {
        if (breakDates.Any(date => date.DayOfWeek == DayOfWeek.Sunday ||
                originalCounts[date] > 1))
            candidates.Add(new SubstituteCandidate(breakDates[^1], name));
    }

    private static DateOnly ToSolarDate(int lunarYear, int lunarMonth,
        int lunarDay)
    {
        var lunarCalendar = new KoreanLunisolarCalendar();
        int leapMonth = lunarCalendar.GetLeapMonth(lunarYear);
        int monthIndex = leapMonth != 0 && lunarMonth >= leapMonth
            ? lunarMonth + 1
            : lunarMonth;
        return DateOnly.FromDateTime(lunarCalendar.ToDateTime(lunarYear,
            monthIndex, lunarDay, 0, 0, 0, 0));
    }

    private static void AddFixed(ICollection<KoreanHoliday> holidays, int year,
        int month, int day, string name) => holidays.Add(
        new KoreanHoliday(new DateOnly(year, month, day), name, false));

    private static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private sealed record SubstituteCandidate(DateOnly AfterDate, string Name);
}
