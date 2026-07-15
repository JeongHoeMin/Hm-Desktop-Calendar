using System;

namespace HmDesktopCalendar.Calendar;

public enum DateCellDecorationKind
{
    Highlight,
    ColorDot,
    Label
}

public sealed class DateCellDecoration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public DateCellDecorationKind Kind { get; set; }
    public string Color { get; set; } = "#3B82F6";
    public string Label { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public long Revision { get; set; }
    public long Cursor { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
