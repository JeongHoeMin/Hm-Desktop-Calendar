using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Authentication;

namespace HmDesktopCalendar.Todos;

public sealed class SyncingTodoRepository : ITodoRepository, IDisposable, IAsyncDisposable
{
    private LocalTodoRepository _local;
    private readonly RemoteTodoRepository _remote;
    private readonly AuthSession _session;
    private readonly SemaphoreSlim _localGate = new(1, 1);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _syncSignal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _syncLoop;
    private long _cursor;
    private int _stopped;
    private bool _disposed;

    public event EventHandler? Changed;
    public event EventHandler<TodoSynchronizationState>? SynchronizationStateChanged;

    public SyncingTodoRepository(LocalTodoRepository local,
        RemoteTodoRepository remote, AuthSession session)
    {
        _local = local;
        _remote = remote;
        _session = session;
        _syncLoop = Task.Run(ProcessSynchronizationRequestsAsync);
    }

    public async Task SwitchScopeAsync(Guid? userId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _stop.Token);
        await _syncGate.WaitAsync(linked.Token);
        try
        {
            await _localGate.WaitAsync(linked.Token);
            try
            {
                List<TodoItem> previous = await _local.GetAllAsync(linked.Token);
                string scope = userId?.ToString("N") ?? "anonymous";
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HmDesktopCalendar", "accounts", scope);
                var next = new LocalTodoRepository(directory);
                try
                {
                    if (userId is not null &&
                        (await next.GetAllAsync(linked.Token)).Count == 0)
                    {
                        await next.ApplyServerAsync(
                            previous.FindAll(item => !item.IsDeleted), linked.Token);
                    }
                }
                catch
                {
                    next.Dispose();
                    throw;
                }

                LocalTodoRepository old = _local;
                _local = next;
                _cursor = 0;
                old.Dispose();
            }
            finally { _localGate.Release(); }
        }
        finally { _syncGate.Release(); }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<TodoItem>> GetByDateAsync(DateOnly date,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        await _localGate.WaitAsync(cancellationToken);
        try { return await _local.GetByDateAsync(date, cancellationToken); }
        finally { _localGate.Release(); }
    }

    public async Task<IReadOnlyList<TodoItem>> GetByRangeAsync(DateOnly from,
        DateOnly to, CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        await _localGate.WaitAsync(cancellationToken);
        try { return await _local.GetByRangeAsync(from, to, cancellationToken); }
        finally { _localGate.Release(); }
    }

    public async Task UpsertAsync(TodoItem item,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        await _localGate.WaitAsync(cancellationToken);
        try { await _local.UpsertAsync(item, cancellationToken); }
        finally { _localGate.Release(); }

        Changed?.Invoke(this, EventArgs.Empty);
        RequestSynchronization();
    }

    public async Task DeleteAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        await _localGate.WaitAsync(cancellationToken);
        try { await _local.DeleteAsync(id, cancellationToken); }
        finally { _localGate.Release(); }

        Changed?.Invoke(this, EventArgs.Empty);
        RequestSynchronization();
    }

    public void RequestSynchronization()
    {
        if (!_session.IsLoggedIn || Volatile.Read(ref _stopped) != 0)
            return;
        if (_syncSignal.CurrentCount == 0)
        {
            try { _syncSignal.Release(); }
            catch (ObjectDisposedException) { }
            catch (SemaphoreFullException) { }
        }
    }

    public async Task SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _stop.Token);
        await SynchronizeCoreAsync(linked.Token);
    }

    private async Task ProcessSynchronizationRequestsAsync()
    {
        try
        {
            while (true)
            {
                await _syncSignal.WaitAsync(_stop.Token);
                if (_session.IsLoggedIn)
                {
                    try { await SynchronizeCoreAsync(_stop.Token); }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine($"할 일 동기화 실패: {exception}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task SynchronizeCoreAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsLoggedIn) return;
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            PublishSynchronizationState(TodoSynchronizationStatus.InProgress);
            List<TodoItem> snapshot;
            await _localGate.WaitAsync(cancellationToken);
            try { snapshot = await _local.GetAllAsync(cancellationToken); }
            finally { _localGate.Release(); }

            Exception? uploadFailure = null;
            foreach (TodoItem item in snapshot.FindAll(item => item.Revision == 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (item.IsDeleted)
                    {
                        await _remote.DeleteAsync(item.Id, cancellationToken);
                        await _localGate.WaitAsync(cancellationToken);
                        try
                        {
                            await _local.MarkDeletedSynchronizedAsync(item.Id,
                                item.UpdatedAt, cancellationToken);
                        }
                        finally { _localGate.Release(); }
                    }
                    else
                    {
                        TodoItem serverItem = await _remote.UpsertAsync(item,
                            cancellationToken);
                        await _localGate.WaitAsync(cancellationToken);
                        try
                        {
                            await _local.MarkUploadedAsync(serverItem,
                                item.UpdatedAt, cancellationToken);
                        }
                        finally { _localGate.Release(); }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    uploadFailure = exception;
                    Console.Error.WriteLine($"할 일 전송 실패({item.Id}): {exception.Message}");
                }
            }

            if (uploadFailure is not null)
            {
                PublishSynchronizationState(TodoSynchronizationStatus.Failed,
                    uploadFailure.Message);
                return;
            }

            bool receivedChanges = false;
            SyncPage page;
            do
            {
                page = await _remote.PullAsync(_cursor, cancellationToken);
                if (page.Changes.Count > 0)
                {
                    await _localGate.WaitAsync(cancellationToken);
                    try { await _local.ApplyServerAsync(page.Changes, cancellationToken); }
                    finally { _localGate.Release(); }
                    receivedChanges = true;
                }
                _cursor = page.NextCursor;
            } while (page.HasMore);

            if (receivedChanges)
                Changed?.Invoke(this, EventArgs.Empty);
            PublishSynchronizationState(TodoSynchronizationStatus.Succeeded);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            PublishSynchronizationState(TodoSynchronizationStatus.Failed,
                exception.Message);
            throw;
        }
        finally { _syncGate.Release(); }
    }

    private void PublishSynchronizationState(TodoSynchronizationStatus status,
        string? errorMessage = null) => SynchronizationStateChanged?.Invoke(
            this, new TodoSynchronizationState(status, DateTimeOffset.Now,
                errorMessage));

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _stop.Cancel();
        try { await _syncLoop; }
        catch (OperationCanceledException) { }

        await _syncGate.WaitAsync();
        _syncGate.Release();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        _disposed = true;

        await _localGate.WaitAsync();
        try { _local.Dispose(); }
        finally { _localGate.Release(); }
        _remote.Dispose();
        _stop.Dispose();
        _syncSignal.Dispose();
        _syncGate.Dispose();
        _localGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void ThrowIfStopped()
    {
        if (Volatile.Read(ref _stopped) != 0)
            throw new ObjectDisposedException(nameof(SyncingTodoRepository));
    }
}

public enum TodoSynchronizationStatus
{
    InProgress,
    Succeeded,
    Failed
}

public sealed record TodoSynchronizationState(
    TodoSynchronizationStatus Status,
    DateTimeOffset OccurredAt,
    string? ErrorMessage = null);
