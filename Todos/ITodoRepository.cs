using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Todos;

public interface ITodoRepository
{
    event EventHandler? Changed;
    Task<IReadOnlyList<TodoItem>> GetByDateAsync(DateOnly date,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodoItem>> GetByRangeAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);
    Task UpsertAsync(TodoItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
