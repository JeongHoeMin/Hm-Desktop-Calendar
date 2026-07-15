using System;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Reminders;

public interface IReminderClock
{
    DateTimeOffset Now { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemReminderClock : IReminderClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
