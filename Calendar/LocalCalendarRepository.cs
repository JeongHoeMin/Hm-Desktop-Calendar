using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Todos;

namespace HmDesktopCalendar.Calendar;

public sealed class LocalCalendarRepository : ICalendarRepository, IDisposable
{
    public const int CurrentDocumentVersion = 2;

    private readonly string _documentPath;
    private readonly string _legacyTodoPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public event EventHandler? Changed;

    public LocalCalendarRepository(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HmDesktopCalendar");
        _documentPath = Path.Combine(directory, "calendar-v2.json");
        _legacyTodoPath = Path.Combine(directory, "todos.json");
    }

    public async Task<IReadOnlyList<CalendarItem>> GetItemsByRangeAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
        CalendarDocument document = await ReadAsync(cancellationToken);
        return document.Items
            .Where(item => !item.IsDeleted && item.StartDate <= to &&
                (item.Recurrence is not null || item.EndDate >= from))
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.StartTime)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .Select(Clone)
            .ToArray();
    }

    public async Task<IReadOnlyList<CalendarOccurrence>>
        GetOccurrencesByRangeAsync(DateOnly from, DateOnly to,
            CancellationToken cancellationToken = default)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
        CalendarDocument document = await ReadAsync(cancellationToken);
        return CalendarOccurrenceEngine.GetOccurrences(document.Items, from, to);
    }

    public async Task<IReadOnlyList<CalendarItem>> GetAllItemsAsync(
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken)).Items.Select(Clone).ToArray();

    public async Task<IReadOnlyList<DateCellDecoration>> GetAllDecorationsAsync(
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken)).Decorations.Select(Clone).ToArray();

    public async Task<IReadOnlyList<DateCellDecoration>>
        GetDecorationsByRangeAsync(DateOnly from, DateOnly to,
            CancellationToken cancellationToken = default)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
        CalendarDocument document = await ReadAsync(cancellationToken);
        return document.Decorations
            .Where(item => !item.IsDeleted && item.Date >= from && item.Date <= to)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Label, StringComparer.CurrentCulture)
            .Select(Clone)
            .ToArray();
    }

    public Task UpsertItemAsync(CalendarItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Title);
        TextColorValidation color = CalendarTextColor.Validate(item.Color);
        if (!color.IsValid) throw new ArgumentException(color.Message,
            nameof(item));
        item.Color = color.NormalizedColor;
        CalendarOccurrenceEngine.ValidateItem(item);
        if (item.Reminders.Any(reminder => reminder.MinutesBefore < 0))
            throw new ArgumentException("알림 시간은 음수일 수 없습니다.", nameof(item));

        return MutateAsync(document =>
        {
            item.Title = item.Title.Trim();
            item.Revision = 0;
            item.Cursor = 0;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            int index = document.Items.FindIndex(current => current.Id == item.Id);
            if (index < 0) document.Items.Add(Clone(item));
            else document.Items[index] = Clone(item);
        }, cancellationToken);
    }

    public Task DeleteItemAsync(Guid id,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            CalendarItem? item = document.Items.Find(current => current.Id == id);
            if (item is null) return;
            item.IsDeleted = true;
            item.Revision = 0;
            item.Cursor = 0;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);

    public Task UpsertDecorationAsync(DateCellDecoration decoration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoration);
        return MutateAsync(document =>
        {
            decoration.Revision = 0;
            decoration.Cursor = 0;
            decoration.UpdatedAt = DateTimeOffset.UtcNow;
            int index = document.Decorations.FindIndex(
                current => current.Id == decoration.Id);
            if (index < 0) document.Decorations.Add(Clone(decoration));
            else document.Decorations[index] = Clone(decoration);
        }, cancellationToken);
    }

    public Task DeleteDecorationAsync(Guid id,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            DateCellDecoration? item = document.Decorations.Find(
                current => current.Id == id);
            if (item is null) return;
            item.IsDeleted = true;
            item.Revision = 0;
            item.Cursor = 0;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);

    public async Task<SyncState> GetSyncStateAsync(
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken)).SyncState;

    public Task SetSyncStateAsync(SyncState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state.Cursor);
        return MutateAsync(document => document.SyncState = state,
            cancellationToken);
    }

    public Task SeedAsync(IEnumerable<CalendarItem> items,
        IEnumerable<DateCellDecoration> decorations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(decorations);
        return MutateAsync(document =>
        {
            if (document.Items.Count == 0)
                document.Items.AddRange(items.Where(item => !item.IsDeleted)
                    .Select(CloneAsLocalChange));
            if (document.Decorations.Count == 0)
                document.Decorations.AddRange(decorations
                    .Where(item => !item.IsDeleted)
                    .Select(CloneAsLocalChange));
        }, cancellationToken);
    }

    public Task ApplyServerAsync(IEnumerable<CalendarItem> items,
        IEnumerable<DateCellDecoration> decorations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(decorations);
        return MutateAsync(document =>
        {
            foreach (CalendarItem change in items)
                MergeServerItem(document.Items, change);
            foreach (DateCellDecoration change in decorations)
                MergeServerDecoration(document.Decorations, change);
        }, cancellationToken);
    }

    public Task MarkItemUploadedAsync(CalendarItem serverItem,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            int index = document.Items.FindIndex(item => item.Id == serverItem.Id);
            if (index < 0 || document.Items[index].Revision != 0 ||
                document.Items[index].UpdatedAt != expectedUpdatedAt)
                return;
            document.Items[index] = Clone(serverItem);
        }, cancellationToken);

    public Task MarkItemDeletedSynchronizedAsync(Guid id,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document => document.Items.RemoveAll(item =>
            item.Id == id && item.IsDeleted && item.Revision == 0 &&
            item.UpdatedAt == expectedUpdatedAt), cancellationToken);

    public Task MarkDecorationUploadedAsync(DateCellDecoration serverItem,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            int index = document.Decorations.FindIndex(
                item => item.Id == serverItem.Id);
            if (index < 0 || document.Decorations[index].Revision != 0 ||
                document.Decorations[index].UpdatedAt != expectedUpdatedAt)
                return;
            document.Decorations[index] = Clone(serverItem);
        }, cancellationToken);

    public Task MarkDecorationDeletedSynchronizedAsync(Guid id,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document => document.Decorations.RemoveAll(item =>
            item.Id == id && item.IsDeleted && item.Revision == 0 &&
            item.UpdatedAt == expectedUpdatedAt), cancellationToken);

    private async Task<CalendarDocument> ReadAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return Clone(await LoadOrImportUnlockedAsync(cancellationToken)); }
        finally { _gate.Release(); }
    }

    private async Task MutateAsync(Action<CalendarDocument> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CalendarDocument document =
                await LoadOrImportUnlockedAsync(cancellationToken);
            mutation(document);
            await WriteUnlockedAsync(document, cancellationToken);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<CalendarDocument> LoadOrImportUnlockedAsync(
        CancellationToken cancellationToken)
    {
        if (File.Exists(_documentPath))
        {
            await using FileStream stream = File.OpenRead(_documentPath);
            CalendarDocument document =
                await JsonSerializer.DeserializeAsync<CalendarDocument>(stream,
                    _jsonOptions, cancellationToken) ??
                throw new InvalidDataException("캘린더 문서가 비어 있습니다.");
            if (document.Version != CurrentDocumentVersion)
                throw new InvalidDataException(
                    $"지원하지 않는 캘린더 문서 버전입니다: {document.Version}");
            foreach (CalendarItem item in document.Items)
                item.Color = CalendarTextColor.NormalizeLegacyDefault(item.Color);
            return document;
        }

        var imported = new CalendarDocument();
        if (File.Exists(_legacyTodoPath))
        {
            await using FileStream stream = File.OpenRead(_legacyTodoPath);
            List<TodoItem> todos =
                await JsonSerializer.DeserializeAsync<List<TodoItem>>(stream,
                    _jsonOptions, cancellationToken) ?? [];
            imported.Items.AddRange(todos.Select(Import));
        }

        await WriteUnlockedAsync(imported, cancellationToken);
        return imported;
    }

    private async Task WriteUnlockedAsync(CalendarDocument document,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_documentPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = _documentPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath,
                FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, document,
                    _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _documentPath, true);
        }
        catch
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            throw;
        }
    }

    private static CalendarItem Import(TodoItem todo) => new()
    {
        Id = todo.Id,
        Kind = CalendarItemKind.Schedule,
        Title = todo.Title,
        Notes = todo.Notes,
        StartDate = todo.Date,
        EndDate = todo.Date,
        StartTime = todo.Time,
        IsAllDay = todo.Time is null,
        IsCompleted = todo.IsCompleted,
        IsDeleted = todo.IsDeleted,
        // v1 서버의 revision/cursor는 v2 서버 공간에 존재하지 않으므로
        // 가져온 일정은 v2에 다시 올릴 로컬 변경으로 표시한다.
        Revision = 0,
        Cursor = 0,
        UpdatedAt = todo.UpdatedAt
    };

    private static CalendarDocument Clone(CalendarDocument document) => new()
    {
        Version = document.Version,
        Items = document.Items.Select(Clone).ToList(),
        Decorations = document.Decorations.Select(Clone).ToList(),
        SyncState = document.SyncState
    };

    private static CalendarItem Clone(CalendarItem item) => new()
    {
        Id = item.Id,
        Kind = item.Kind,
        Title = item.Title,
        Notes = item.Notes,
        StartDate = item.StartDate,
        EndDate = item.EndDate,
        StartTime = item.StartTime,
        EndTime = item.EndTime,
        IsAllDay = item.IsAllDay,
        IsCompleted = item.IsCompleted,
        Color = item.Color,
        Recurrence = item.Recurrence is null ? null : new RecurrenceRule(
            item.Recurrence.Frequency, item.Recurrence.Interval,
            item.Recurrence.DaysOfWeek?.ToArray(), item.Recurrence.Until,
            item.Recurrence.Count),
        Reminders = item.Reminders.ToList(),
        IsDeleted = item.IsDeleted,
        Revision = item.Revision,
        Cursor = item.Cursor,
        UpdatedAt = item.UpdatedAt
    };

    private static CalendarItem CloneAsLocalChange(CalendarItem item)
    {
        CalendarItem clone = Clone(item);
        clone.IsDeleted = false;
        clone.Revision = 0;
        clone.Cursor = 0;
        clone.UpdatedAt = DateTimeOffset.UtcNow;
        return clone;
    }

    private static DateCellDecoration CloneAsLocalChange(
        DateCellDecoration item)
    {
        DateCellDecoration clone = Clone(item);
        clone.IsDeleted = false;
        clone.Revision = 0;
        clone.Cursor = 0;
        clone.UpdatedAt = DateTimeOffset.UtcNow;
        return clone;
    }

    private static void MergeServerItem(List<CalendarItem> items,
        CalendarItem change)
    {
        int index = items.FindIndex(item => item.Id == change.Id);
        if (index < 0) items.Add(Clone(change));
        else if (items[index].Revision != 0 &&
                 change.Revision >= items[index].Revision)
            items[index] = Clone(change);
    }

    private static void MergeServerDecoration(List<DateCellDecoration> items,
        DateCellDecoration change)
    {
        int index = items.FindIndex(item => item.Id == change.Id);
        if (index < 0) items.Add(Clone(change));
        else if (items[index].Revision != 0 &&
                 change.Revision >= items[index].Revision)
            items[index] = Clone(change);
    }

    private static DateCellDecoration Clone(DateCellDecoration item) => new()
    {
        Id = item.Id,
        Date = item.Date,
        Kind = item.Kind,
        Color = item.Color,
        Label = item.Label,
        IsDeleted = item.IsDeleted,
        Revision = item.Revision,
        Cursor = item.Cursor,
        UpdatedAt = item.UpdatedAt
    };

    public void Dispose() => _gate.Dispose();
}

internal sealed class CalendarDocument
{
    public int Version { get; set; } = LocalCalendarRepository.CurrentDocumentVersion;
    public List<CalendarItem> Items { get; set; } = [];
    public List<DateCellDecoration> Decorations { get; set; } = [];
    public SyncState SyncState { get; set; } = new(0);
}
