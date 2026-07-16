using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Authentication;

namespace HmDesktopCalendar.Calendar;

public sealed class SyncingCalendarRepository : ICalendarRepository,
    IDisposable, IAsyncDisposable
{
    private LocalCalendarRepository _local;
    private readonly RemoteCalendarRepository _remote;
    private readonly AuthSession _session;
    private readonly string _accountsDirectory;
    private readonly SemaphoreSlim _localGate = new(1, 1);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _syncSignal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _syncLoop;
    private string? _scope;
    private int _stopped;
    private bool _disposed;

    public event EventHandler? Changed;
    public event EventHandler<CalendarSynchronizationState>?
        SynchronizationStateChanged;

    public SyncingCalendarRepository(LocalCalendarRepository local,
        RemoteCalendarRepository remote, AuthSession session,
        string? accountsDirectory = null)
    {
        _local = local;
        _remote = remote;
        _session = session;
        _accountsDirectory = accountsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HmDesktopCalendar", "accounts");
        _syncLoop = Task.Run(ProcessSynchronizationRequestsAsync);
    }

    public async Task SwitchScopeAsync(Guid? userId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        string scope = userId?.ToString("N") ?? "anonymous";
        if (string.Equals(_scope, scope, StringComparison.Ordinal)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _stop.Token);
        await _syncGate.WaitAsync(linked.Token);
        try
        {
            await _localGate.WaitAsync(linked.Token);
            try
            {
                IReadOnlyList<CalendarItem> previousItems =
                    await _local.GetAllItemsAsync(linked.Token);
                IReadOnlyList<DateCellDecoration> previousDecorations =
                    await _local.GetAllDecorationsAsync(linked.Token);
                var next = new LocalCalendarRepository(
                    Path.Combine(_accountsDirectory, scope));
                try
                {
                    bool canSeed = _scope is null ||
                        (userId is not null && _scope == "anonymous");
                    if (canSeed &&
                        (await next.GetAllItemsAsync(linked.Token)).Count == 0 &&
                        (await next.GetAllDecorationsAsync(linked.Token)).Count == 0)
                    {
                        await next.SeedAsync(previousItems, previousDecorations,
                            linked.Token);
                    }
                }
                catch
                {
                    next.Dispose();
                    throw;
                }

                LocalCalendarRepository old = _local;
                _local = next;
                _scope = scope;
                old.Dispose();
            }
            finally { _localGate.Release(); }
        }
        finally { _syncGate.Release(); }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<CalendarItem>> GetItemsByRangeAsync(DateOnly from,
        DateOnly to, CancellationToken cancellationToken = default) =>
        WithLocalAsync(local => local.GetItemsByRangeAsync(from, to,
            cancellationToken), cancellationToken);

    public Task<IReadOnlyList<CalendarItem>> GetAllItemsAsync(
        CancellationToken cancellationToken = default) =>
        WithLocalAsync(local => local.GetAllItemsAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<CalendarOccurrence>> GetOccurrencesByRangeAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default) =>
        WithLocalAsync(local => local.GetOccurrencesByRangeAsync(from, to,
            cancellationToken), cancellationToken);

    public Task<IReadOnlyList<DateCellDecoration>> GetDecorationsByRangeAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default) =>
        WithLocalAsync(local => local.GetDecorationsByRangeAsync(from, to,
            cancellationToken), cancellationToken);

    public async Task UpsertItemAsync(CalendarItem item,
        CancellationToken cancellationToken = default)
    {
        await WithLocalAsync(local => local.UpsertItemAsync(item,
            cancellationToken), cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        RequestSynchronization();
    }

    public async Task DeleteItemAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        await WithLocalAsync(local => local.DeleteItemAsync(id,
            cancellationToken), cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        RequestSynchronization();
    }

    public async Task UpsertDecorationAsync(DateCellDecoration item,
        CancellationToken cancellationToken = default)
    {
        await WithLocalAsync(local => local.UpsertDecorationAsync(item,
            cancellationToken), cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        RequestSynchronization();
    }

    public async Task DeleteDecorationAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        await WithLocalAsync(local => local.DeleteDecorationAsync(id,
            cancellationToken), cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        RequestSynchronization();
    }

    public Task<SyncState> GetSyncStateAsync(
        CancellationToken cancellationToken = default) =>
        WithLocalAsync(local => local.GetSyncStateAsync(cancellationToken),
            cancellationToken);

    public Task SetSyncStateAsync(SyncState state,
        CancellationToken cancellationToken = default) =>
        WithLocalAsync(local => local.SetSyncStateAsync(state,
            cancellationToken), cancellationToken);

    public void RequestSynchronization()
    {
        if (!_session.IsLoggedIn || Volatile.Read(ref _stopped) != 0) return;
        if (_syncSignal.CurrentCount != 0) return;
        try { _syncSignal.Release(); }
        catch (ObjectDisposedException) { }
        catch (SemaphoreFullException) { }
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
                if (!_session.IsLoggedIn) continue;
                try { await SynchronizeCoreAsync(_stop.Token); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"캘린더 동기화 실패: {exception}");
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
            PublishSynchronizationState(CalendarSynchronizationStatus.InProgress);
            IReadOnlyList<CalendarItem> items;
            IReadOnlyList<DateCellDecoration> decorations;
            SyncState syncState;
            await _localGate.WaitAsync(cancellationToken);
            try
            {
                items = await _local.GetAllItemsAsync(cancellationToken);
                decorations = await _local.GetAllDecorationsAsync(cancellationToken);
                syncState = await _local.GetSyncStateAsync(cancellationToken);
            }
            finally { _localGate.Release(); }

            Exception? uploadFailure = null;
            foreach (CalendarItem item in items.Where(item => item.Revision == 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await UploadItemAsync(item, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    uploadFailure = exception;
                    Console.Error.WriteLine(
                        $"일정 전송 실패({item.Id}): {exception.Message}");
                }
            }
            foreach (DateCellDecoration item in decorations
                         .Where(item => item.Revision == 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await UploadDecorationAsync(item, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    uploadFailure = exception;
                    Console.Error.WriteLine(
                        $"날짜 장식 전송 실패({item.Id}): {exception.Message}");
                }
            }

            if (uploadFailure is not null)
            {
                PublishSynchronizationState(CalendarSynchronizationStatus.Failed,
                    uploadFailure.Message);
                return;
            }

            bool receivedChanges = false;
            long cursor = syncState.Cursor;
            CalendarSyncPage page;
            do
            {
                page = await _remote.PullAsync(cursor, cancellationToken);
                await _localGate.WaitAsync(cancellationToken);
                try
                {
                    if (page.Items.Count > 0 || page.Decorations.Count > 0)
                    {
                        await _local.ApplyServerAsync(page.Items,
                            page.Decorations, cancellationToken);
                        receivedChanges = true;
                    }
                    cursor = page.NextCursor;
                    await _local.SetSyncStateAsync(new SyncState(cursor,
                        syncState.LastSucceededAt), cancellationToken);
                }
                finally { _localGate.Release(); }
            } while (page.HasMore);

            DateTimeOffset completedAt = DateTimeOffset.Now;
            await _localGate.WaitAsync(cancellationToken);
            try
            {
                await _local.SetSyncStateAsync(new SyncState(cursor, completedAt),
                    cancellationToken);
            }
            finally { _localGate.Release(); }
            if (receivedChanges) Changed?.Invoke(this, EventArgs.Empty);
            PublishSynchronizationState(CalendarSynchronizationStatus.Succeeded,
                occurredAt: completedAt);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            PublishSynchronizationState(CalendarSynchronizationStatus.Failed,
                exception.Message);
            throw;
        }
        finally { _syncGate.Release(); }
    }

    private async Task UploadItemAsync(CalendarItem item,
        CancellationToken cancellationToken)
    {
        if (item.IsDeleted)
        {
            await _remote.DeleteItemAsync(item.Id, cancellationToken);
            await WithLocalAsync(local =>
                local.MarkItemDeletedSynchronizedAsync(item.Id,
                    item.UpdatedAt, cancellationToken), cancellationToken);
            return;
        }
        CalendarItem serverItem = await _remote.UpsertItemAsync(item,
            cancellationToken);
        await WithLocalAsync(local => local.MarkItemUploadedAsync(serverItem,
            item.UpdatedAt, cancellationToken), cancellationToken);
    }

    private async Task UploadDecorationAsync(DateCellDecoration item,
        CancellationToken cancellationToken)
    {
        if (item.IsDeleted)
        {
            await _remote.DeleteDecorationAsync(item.Id, cancellationToken);
            await WithLocalAsync(local =>
                local.MarkDecorationDeletedSynchronizedAsync(item.Id,
                    item.UpdatedAt, cancellationToken), cancellationToken);
            return;
        }
        DateCellDecoration serverItem = await _remote.UpsertDecorationAsync(item,
            cancellationToken);
        await WithLocalAsync(local => local.MarkDecorationUploadedAsync(
            serverItem, item.UpdatedAt, cancellationToken), cancellationToken);
    }

    private async Task<T> WithLocalAsync<T>(
        Func<LocalCalendarRepository, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ThrowIfStopped();
        await _localGate.WaitAsync(cancellationToken);
        try { return await action(_local); }
        finally { _localGate.Release(); }
    }

    private async Task WithLocalAsync(
        Func<LocalCalendarRepository, Task> action,
        CancellationToken cancellationToken)
    {
        ThrowIfStopped();
        await _localGate.WaitAsync(cancellationToken);
        try { await action(_local); }
        finally { _localGate.Release(); }
    }

    private void PublishSynchronizationState(
        CalendarSynchronizationStatus status, string? errorMessage = null,
        DateTimeOffset? occurredAt = null) =>
        SynchronizationStateChanged?.Invoke(this,
            new CalendarSynchronizationState(status,
                occurredAt ?? DateTimeOffset.Now, errorMessage));

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
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
            throw new ObjectDisposedException(nameof(SyncingCalendarRepository));
    }
}

public enum CalendarSynchronizationStatus
{
    InProgress,
    Succeeded,
    Failed
}

public sealed record CalendarSynchronizationState(
    CalendarSynchronizationStatus Status,
    DateTimeOffset OccurredAt,
    string? ErrorMessage = null);
