using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Todos;

public sealed class LocalTodoRepository : ITodoRepository, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    public event EventHandler? Changed;

    public LocalTodoRepository(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HmDesktopCalendar");
        _filePath = Path.Combine(directory, "todos.json");
    }

    public async Task<IReadOnlyList<TodoItem>> GetByDateAsync(DateOnly date,
        CancellationToken cancellationToken = default) =>
        (await ReadAllAsync(cancellationToken)).Where(x => x.Date == date && !x.IsDeleted)
            .OrderBy(x => x.IsCompleted).ThenBy(x => x.Time).ThenBy(x => x.Title)
            .ToArray();

    public async Task<IReadOnlyList<TodoItem>> GetByRangeAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default) =>
        (await ReadAllAsync(cancellationToken)).Where(x => x.Date >= from && x.Date <= to && !x.IsDeleted)
            .OrderBy(x => x.Date).ThenBy(x => x.IsCompleted).ThenBy(x => x.Time)
            .ToArray();

    public async Task UpsertAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Title);
        item.Title = item.Title.Trim();
        item.Revision = 0;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<TodoItem> items = await ReadUnlockedAsync(cancellationToken);
            int index = items.FindIndex(x => x.Id == item.Id);
            TodoItem copy = Clone(item);
            if (index < 0) items.Add(copy); else items[index] = copy;
            await WriteUnlockedAsync(items, cancellationToken);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<TodoItem> items = await ReadUnlockedAsync(cancellationToken);
            TodoItem? item = items.Find(x => x.Id == id);
            if (item is not null)
            {
                item.IsDeleted = true;
                item.Revision = 0;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                await WriteUnlockedAsync(items, cancellationToken);
            }
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<List<TodoItem>> GetAllAsync(CancellationToken cancellationToken = default) => ReadAllAsync(cancellationToken);
    public async Task ApplyServerAsync(IEnumerable<TodoItem> changes, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<TodoItem> items = await ReadUnlockedAsync(cancellationToken);
            foreach (TodoItem change in changes)
            {
                int index = items.FindIndex(x => x.Id == change.Id);
                if (index < 0) items.Add(Clone(change));
                else if (items[index].Revision != 0 &&
                         change.Revision >= items[index].Revision)
                    items[index] = Clone(change);
            }
            await WriteUnlockedAsync(items, cancellationToken);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkUploadedAsync(TodoItem serverItem,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<TodoItem> items = await ReadUnlockedAsync(cancellationToken);
            int index = items.FindIndex(item => item.Id == serverItem.Id);
            if (index < 0 || items[index].Revision != 0 ||
                items[index].UpdatedAt != expectedUpdatedAt)
                return;
            items[index] = Clone(serverItem);
            await WriteUnlockedAsync(items, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkDeletedSynchronizedAsync(Guid id,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<TodoItem> items = await ReadUnlockedAsync(cancellationToken);
            int index = items.FindIndex(item => item.Id == id);
            if (index < 0 || !items[index].IsDeleted ||
                items[index].Revision != 0 ||
                items[index].UpdatedAt != expectedUpdatedAt)
                return;
            items.RemoveAt(index);
            await WriteUnlockedAsync(items, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<TodoItem>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadUnlockedAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<List<TodoItem>> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return [];
        await using FileStream stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<TodoItem>>(stream, _jsonOptions,
            cancellationToken) ?? [];
    }

    private async Task WriteUnlockedAsync(List<TodoItem> items,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (directory is not null) Directory.CreateDirectory(directory);
        string temporaryPath = _filePath + ".tmp";
        await using (FileStream stream = new(temporaryPath, FileMode.Create,
            FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, items, _jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, _filePath, true);
    }

    private static TodoItem Clone(TodoItem item) => new()
    {
        Id = item.Id, Date = item.Date, Title = item.Title, Time = item.Time,
        Notes = item.Notes, IsCompleted = item.IsCompleted, IsDeleted = item.IsDeleted,
        Revision = item.Revision, Cursor = item.Cursor, UpdatedAt = item.UpdatedAt
    };

    public void Dispose() => _gate.Dispose();
}
