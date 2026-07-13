using System;
using System.Text.Json.Serialization;

namespace HmDesktopCalendar.Todos;

public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public string Title { get; set; } = string.Empty;
    public TimeOnly? Time { get; set; }
    [JsonIgnore]
    public TimeSpan? TimeValue { get => Time?.ToTimeSpan(); set => Time = value is { } v ? TimeOnly.FromTimeSpan(v) : null; }
    public string Notes { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsDeleted { get; set; }
    public long Revision { get; set; }
    public long Cursor { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
