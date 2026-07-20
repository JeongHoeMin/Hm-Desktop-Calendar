using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Authentication;
using HmDesktopCalendar.Services;

namespace HmDesktopCalendar.Todos;

public sealed class RemoteTodoRepository : IDisposable
{
    private readonly HttpClient _http;
    private readonly AuthSession _session;
    public RemoteTodoRepository(AuthSession session) :
        this(session, ServerEndpoint.Default) { }
    public RemoteTodoRepository(AuthSession session, string baseUrl) :
        this(session, ServerEndpoint.FromHttpUrl(baseUrl)) { }
    public RemoteTodoRepository(AuthSession session, ServerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _session = session;
        _http = new HttpClient { BaseAddress = endpoint.HttpBaseUri };
    }
    private async Task AuthorizeAsync(CancellationToken ct) => _http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", await _session.GetAccessTokenAsync(ct) ?? throw new InvalidOperationException("로그인이 필요합니다."));
    public async Task<TodoItem> UpsertAsync(TodoItem item, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct); var response = await _http.PutAsJsonAsync($"v1/todos/{item.Id}", new { date = item.Date.ToString("yyyy-MM-dd"), title = item.Title,
            time = item.Time?.ToString("HH:mm"), notes = item.Notes, completed = item.IsCompleted }, ct);
        response.EnsureSuccessStatusCode(); return ToItem((await response.Content.ReadFromJsonAsync<RemoteTodo>(cancellationToken: ct))!);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default) { await AuthorizeAsync(ct); var response = await _http.DeleteAsync($"v1/todos/{id}", ct); if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode(); }
    public async Task<SyncPage> PullAsync(long after, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct); var page = (await _http.GetFromJsonAsync<RemoteSyncPage>($"v1/sync?after={after}&limit=500", ct))!;
        var items = new List<TodoItem>(); foreach (var todo in page.Changes) items.Add(ToItem(todo)); return new(items, page.NextCursor, page.HasMore);
    }
    private static TodoItem ToItem(RemoteTodo x) => new() { Id=x.Id, Date=DateOnly.ParseExact(x.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture),
        Title=x.Title, Time=string.IsNullOrEmpty(x.Time)?null:TimeOnly.Parse(x.Time), Notes=x.Notes, IsCompleted=x.Completed,
        IsDeleted=x.Deleted, Revision=x.Revision, Cursor=x.Cursor, UpdatedAt=x.UpdatedAt };
    public void Dispose() => _http.Dispose();
    private sealed record RemoteSyncPage([property:JsonPropertyName("changes")] RemoteTodo[] Changes,[property:JsonPropertyName("nextCursor")] long NextCursor,[property:JsonPropertyName("hasMore")] bool HasMore);
    private sealed record RemoteTodo(Guid Id,string Date,string Title,string? Time,string Notes,bool Completed,bool Deleted,long Revision,long Cursor,DateTimeOffset UpdatedAt);
}
public sealed record SyncPage(IReadOnlyList<TodoItem> Changes,long NextCursor,bool HasMore);
