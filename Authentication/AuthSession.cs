using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Authentication;

public sealed class AuthSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly WindowsCredentialTokenStore _tokens = new();
    private readonly SemaphoreSlim _refreshGate = new(1,1);
    private DateTimeOffset _accessExpiresAt;
    public string? AccessToken { get; private set; }
    public UserInfo? User { get; private set; }
    public bool IsLoggedIn => User is not null;
    public event EventHandler? Changed;
    public AuthSession(string baseUrl = "http://127.0.0.1:3000") => _http = new HttpClient { BaseAddress = new Uri(baseUrl) };

    public Task LoginAsync(string email, string password, CancellationToken ct = default) => AuthenticateAsync("v1/auth/login", email, password, ct);
    public Task RegisterAsync(string email, string password, CancellationToken ct = default) => AuthenticateAsync("v1/auth/register", email, password, ct);
    private async Task AuthenticateAsync(string path, string email, string password, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(path, new { email, password, deviceName = Environment.MachineName }, ct);
        await ApplyResponseAsync(response, ct);
    }
    public async Task<bool> TryRestoreAsync(CancellationToken ct = default)
    {
        string? refresh = _tokens.Load(); if (refresh is null) return false;
        try { var response = await _http.PostAsJsonAsync("v1/auth/refresh", new { refreshToken = refresh, deviceName = Environment.MachineName }, ct); await ApplyResponseAsync(response, ct); return true; }
        catch { _tokens.Clear(); return false; }
    }
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct=default)
    {
        if(!IsLoggedIn||AccessToken is null)return null;
        if(_accessExpiresAt>DateTimeOffset.UtcNow.AddSeconds(30))return AccessToken;
        await _refreshGate.WaitAsync(ct);
        try
        {
            if(_accessExpiresAt>DateTimeOffset.UtcNow.AddSeconds(30))return AccessToken;
            string? refresh=_tokens.Load();if(refresh is null)return null;
            var response=await _http.PostAsJsonAsync("v1/auth/refresh",new{refreshToken=refresh,deviceName=Environment.MachineName},ct);
            await ApplyResponseAsync(response,ct);return AccessToken;
        }
        catch { _tokens.Clear();AccessToken=null;User=null;Changed?.Invoke(this,EventArgs.Empty);return null; }
        finally{_refreshGate.Release();}
    }
    private async Task ApplyResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException((await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken: ct))?.Message ?? "인증에 실패했습니다.");
        SessionResponse session = (await response.Content.ReadFromJsonAsync<SessionResponse>(cancellationToken: ct))!;
        Guid? previousUser=User?.Id;
        AccessToken = session.AccessToken; User = session.User; _accessExpiresAt=ReadExpiry(session.AccessToken);
        _tokens.Save(session.RefreshToken); if(previousUser!=User.Id)Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        string? refresh = _tokens.Load();
        try { if (refresh is not null) await _http.PostAsJsonAsync("v1/auth/logout", new { refreshToken = refresh, deviceName = Environment.MachineName }, ct); } catch { }
        _tokens.Clear(); AccessToken = null; User = null; Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() { _http.Dispose(); _refreshGate.Dispose(); }
    private static DateTimeOffset ReadExpiry(string token)
    {
        try { string value=token.Split('.')[1].Replace('-','+').Replace('_','/');value=value.PadRight((value.Length+3)/4*4,'=');
            using JsonDocument json=JsonDocument.Parse(Convert.FromBase64String(value));return DateTimeOffset.FromUnixTimeSeconds(json.RootElement.GetProperty("exp").GetInt64()); }
        catch{return DateTimeOffset.UtcNow.AddMinutes(10);}
    }
    private sealed record SessionResponse([property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("user")] UserInfo User);
    private sealed record ApiError([property: JsonPropertyName("message")] string Message);
}
public sealed record UserInfo([property: JsonPropertyName("id")] Guid Id, [property: JsonPropertyName("email")] string Email);
