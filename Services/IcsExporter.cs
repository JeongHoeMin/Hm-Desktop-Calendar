using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Calendar;

namespace HmDesktopCalendar.Services;

public sealed class IcsExporter
{
    private const string TimeZoneId = "Asia/Seoul";
    private readonly Func<DateOnly> _todayProvider;

    public IcsExporter() : this(() => DateOnly.FromDateTime(DateTime.Today))
    {
    }

    internal IcsExporter(Func<DateOnly> todayProvider) =>
        _todayProvider = todayProvider;

    public string Export(IEnumerable<CalendarItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//HmDesktopCalendar//KO",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            $"X-WR-TIMEZONE:{TimeZoneId}",
            "BEGIN:VTIMEZONE",
            $"TZID:{TimeZoneId}",
            "BEGIN:STANDARD",
            "DTSTART:19700101T000000",
            "TZOFFSETFROM:+0900",
            "TZOFFSETTO:+0900",
            "TZNAME:KST",
            "END:STANDARD",
            "END:VTIMEZONE"
        };
        foreach (CalendarItem item in items.Where(item => !item.IsDeleted)
                     .OrderBy(item => item.StartDate).ThenBy(item => item.Id))
            AppendEvents(lines, item);
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.SelectMany(FoldLine)) + "\r\n";
    }

    public async Task ExportToFileAsync(IEnumerable<CalendarItem> items,
        string path, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, Export(items),
                new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void AppendEvents(List<string> lines, CalendarItem item)
    {
        if (item.Recurrence is null ||
            Enum.IsDefined(item.Recurrence.Frequency))
            CalendarOccurrenceEngine.ValidateItem(item);
        string? recurrenceRule = null;
        if (item.Recurrence is { } recurrence &&
            !TryFormatRecurrence(item, recurrence, out recurrenceRule))
        {
            DateOnly today = _todayProvider();
            DateOnly from = item.StartDate > today ? item.StartDate : today;
            DateOnly to = today.AddYears(2);
            if (from > to) return;
            foreach (CalendarOccurrence occurrence in CalendarOccurrenceEngine
                         .GetOccurrences(item, from, to))
                AppendEvent(lines, item, occurrence.Date, occurrence.Date,
                    null, $"-{occurrence.Date:yyyyMMdd}");
            return;
        }
        AppendEvent(lines, item, item.StartDate, item.EndDate,
            recurrenceRule, string.Empty);
    }

    private static void AppendEvent(List<string> lines, CalendarItem item,
        DateOnly startDate, DateOnly endDate, string? recurrenceRule,
        string uidSuffix)
    {
        lines.Add("BEGIN:VEVENT");
        lines.Add($"UID:{item.Id:N}{uidSuffix}@hm-desktop-calendar");
        lines.Add($"DTSTAMP:{item.UpdatedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
        if (item.IsAllDay || item.StartTime is null)
        {
            lines.Add($"DTSTART;VALUE=DATE:{startDate:yyyyMMdd}");
            lines.Add($"DTEND;VALUE=DATE:{endDate.AddDays(1):yyyyMMdd}");
        }
        else
        {
            lines.Add($"DTSTART;TZID={TimeZoneId}:" +
                $"{startDate:yyyyMMdd}T{item.StartTime:HHmmss}");
            if (item.EndTime is { } endTime)
                lines.Add($"DTEND;TZID={TimeZoneId}:" +
                    $"{endDate:yyyyMMdd}T{endTime:HHmmss}");
        }
        lines.Add($"SUMMARY:{EscapeText(item.Title)}");
        if (!string.IsNullOrWhiteSpace(item.Notes))
            lines.Add($"DESCRIPTION:{EscapeText(item.Notes)}");
        if (item.IsCompleted) lines.Add("STATUS:COMPLETED");
        if (recurrenceRule is not null)
            lines.Add($"RRULE:{recurrenceRule}");
        lines.Add("END:VEVENT");
    }

    private static bool TryFormatRecurrence(CalendarItem item,
        RecurrenceRule recurrence, out string? result)
    {
        string? frequency = recurrence.Frequency switch
        {
            RecurrenceFrequency.Daily => "DAILY",
            RecurrenceFrequency.Weekly => "WEEKLY",
            RecurrenceFrequency.Monthly => "MONTHLY",
            RecurrenceFrequency.Yearly => "YEARLY",
            _ => null
        };
        if (frequency is null)
        {
            result = null;
            return false;
        }
        var parts = new List<string> { $"FREQ={frequency}" };
        if (recurrence.Interval != 1)
            parts.Add($"INTERVAL={recurrence.Interval}");
        if (recurrence.Frequency == RecurrenceFrequency.Weekly)
            parts.Add("BYDAY=" + string.Join(',', recurrence.DaysOfWeek!
                .Distinct().OrderBy(day => (int)day).Select(FormatDay)));
        if (recurrence.Until is { } until)
            parts.Add(item.IsAllDay || item.StartTime is null
                ? $"UNTIL={until:yyyyMMdd}"
                : $"UNTIL={until:yyyyMMdd}T145959Z");
        result = string.Join(';', parts);
        return true;
    }

    private static string FormatDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "SU",
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => throw new ArgumentOutOfRangeException(nameof(day))
    };

    private static string EscapeText(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\n", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal);

    private static IEnumerable<string> FoldLine(string line)
    {
        string remaining = line;
        bool continuation = false;
        while (Encoding.UTF8.GetByteCount(remaining) >
               (continuation ? 74 : 75))
        {
            int byteLimit = continuation ? 74 : 75;
            int length = 0;
            int bytes = 0;
            foreach (Rune rune in remaining.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > byteLimit) break;
                bytes += rune.Utf8SequenceLength;
                length += rune.Utf16SequenceLength;
            }
            yield return (continuation ? " " : string.Empty) +
                remaining[..length];
            remaining = remaining[length..];
            continuation = true;
        }
        yield return (continuation ? " " : string.Empty) + remaining;
    }
}
