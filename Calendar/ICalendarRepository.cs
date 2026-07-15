using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Calendar;

public sealed record SyncState(long Cursor, DateTimeOffset? LastSucceededAt = null);

public interface ICalendarRepository
{
    event EventHandler? Changed;

    Task<IReadOnlyList<CalendarItem>> GetItemsByRangeAsync(DateOnly from,
        DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CalendarOccurrence>> GetOccurrencesByRangeAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DateCellDecoration>> GetDecorationsByRangeAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);
    Task UpsertItemAsync(CalendarItem item,
        CancellationToken cancellationToken = default);
    Task DeleteItemAsync(Guid id,
        CancellationToken cancellationToken = default);
    Task UpsertDecorationAsync(DateCellDecoration decoration,
        CancellationToken cancellationToken = default);
    Task DeleteDecorationAsync(Guid id,
        CancellationToken cancellationToken = default);
    Task<SyncState> GetSyncStateAsync(
        CancellationToken cancellationToken = default);
    Task SetSyncStateAsync(SyncState state,
        CancellationToken cancellationToken = default);
}
