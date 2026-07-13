using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Authentication;

public sealed class RealtimeSyncClient : IDisposable, IAsyncDisposable
{
    private readonly AuthSession _session;
    private readonly Uri _uri;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lifetimeLock = new();
    private Task? _loop;
    private bool _disposed;

    public event EventHandler? SyncRequested;

    public RealtimeSyncClient(AuthSession session,
        string url = "ws://127.0.0.1:3000/v1/realtime")
    {
        _session = session;
        _uri = new Uri(url);
    }

    public void Start()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _loop ??= Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        int delay = 1;
        while (!_stop.IsCancellationRequested)
        {
            if (!_session.IsLoggedIn || string.IsNullOrEmpty(_session.AccessToken))
            {
                await Task.Delay(1000, _stop.Token);
                continue;
            }

            try
            {
                string? access = await _session.GetAccessTokenAsync(_stop.Token);
                if (access is null) continue;
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", $"Bearer {access}");
                await socket.ConnectAsync(_uri, _stop.Token);
                delay = 1;
                SyncRequested?.Invoke(this, EventArgs.Empty);
                var buffer = new byte[2048];
                while (socket.State == WebSocketState.Open &&
                       !_stop.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(
                        buffer, _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    SyncRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"실시간 연결 실패: {exception.Message}");
                await Task.Delay(TimeSpan.FromSeconds(delay), _stop.Token);
                delay = Math.Min(delay * 2, 30);
            }
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        lock (_lifetimeLock)
        {
            if (!_stop.IsCancellationRequested) _stop.Cancel();
            loop = _loop;
        }
        if (loop is null) return;
        try { await loop; }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        lock (_lifetimeLock) _disposed = true;
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
