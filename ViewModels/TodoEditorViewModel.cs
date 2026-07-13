using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HmDesktopCalendar.Todos;

namespace HmDesktopCalendar.ViewModels;

public sealed partial class TodoEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ITodoRepository _repository;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly CancellationTokenSource _dispose = new();
    private long _loadRequest;
    private int _localOperation;
    private bool _disposed;

    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private string _errorMessage = string.Empty;
    public string DateTitle => $"{Date:yyyy년 M월 d일} 할 일";
    public ObservableCollection<TodoItem> Items { get; } = [];

    public TodoEditorViewModel(DateOnly date, ITodoRepository repository)
    {
        _date = date;
        _repository = repository;
        _repository.Changed += OnRepositoryChanged;
    }

    partial void OnDateChanged(DateOnly value) =>
        OnPropertyChanged(nameof(DateTitle));

    public Task LoadAsync() => LoadDateAsync(Date);

    public async Task LoadDateAsync(DateOnly date,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long request = Interlocked.Increment(ref _loadRequest);
        await Dispatcher.UIThread.InvokeAsync(() => Date = date);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _dispose.Token);
        await _loadGate.WaitAsync(linked.Token);
        try
        {
            var items = await _repository.GetByDateAsync(date, linked.Token);
            if (request != Volatile.Read(ref _loadRequest)) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (request != Volatile.Read(ref _loadRequest)) return;
                Items.Clear();
                foreach (TodoItem item in items) Items.Add(item);
            });
        }
        finally { _loadGate.Release(); }
    }

    public Task<bool> AddAsync(string title, TimeOnly? time, string notes) =>
        ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("제목을 입력하세요.");
            await _repository.UpsertAsync(new TodoItem
            {
                Date = Date,
                Title = title.Trim(),
                Time = time,
                Notes = notes.Trim()
            });
        });

    public Task<bool> SaveAsync(TodoItem item) =>
        ExecuteAsync(() => _repository.UpsertAsync(item));

    public Task<bool> DeleteAsync(TodoItem item) =>
        ExecuteAsync(() => _repository.DeleteAsync(item.Id));

    private async Task<bool> ExecuteAsync(Func<Task> action)
    {
        Interlocked.Increment(ref _localOperation);
        try
        {
            await action();
            ErrorMessage = string.Empty;
            await LoadDateAsync(Date);
            return true;
        }
        catch (OperationCanceledException) when (_dispose.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"저장하지 못했습니다: {exception.Message}";
            return false;
        }
        finally { Interlocked.Decrement(ref _localOperation); }
    }

    private void OnRepositoryChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed || Volatile.Read(ref _localOperation) != 0) return;
        _ = ReloadSafelyAsync();
    }

    private async Task ReloadSafelyAsync()
    {
        try { await LoadDateAsync(Date, _dispose.Token); }
        catch (OperationCanceledException) when (_dispose.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_disposed) { }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ErrorMessage = $"할 일을 불러오지 못했습니다: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
        _dispose.Cancel();
        _dispose.Dispose();
    }
}
