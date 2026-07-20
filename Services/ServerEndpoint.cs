using System;

namespace HmDesktopCalendar.Services;

public sealed class ServerEndpoint
{
    public const string EnvironmentVariableName = "HM_CALENDAR_SERVER_URL";
    public const string DefaultHttpUrl = "http://127.0.0.1:3000";

    private ServerEndpoint(Uri httpBaseUri, bool isEnvironmentOverride)
    {
        HttpBaseUri = httpBaseUri;
        IsEnvironmentOverride = isEnvironmentOverride;
        var realtime = new UriBuilder(httpBaseUri)
        {
            Scheme = httpBaseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Port = httpBaseUri.IsDefaultPort ? -1 : httpBaseUri.Port,
            Path = httpBaseUri.AbsolutePath.TrimEnd('/') + "/v1/realtime",
            Query = string.Empty,
            Fragment = string.Empty
        };
        RealtimeUri = realtime.Uri;
    }

    public Uri HttpBaseUri { get; }
    public Uri RealtimeUri { get; }
    public bool IsEnvironmentOverride { get; }
    public string HttpUrl => HttpBaseUri.AbsoluteUri.TrimEnd('/');

    public static ServerEndpoint Default { get; } =
        FromHttpUrl(DefaultHttpUrl);

    public static ServerEndpoint FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Resolve(settings.ServerUrl,
            Environment.GetEnvironmentVariable(EnvironmentVariableName));
    }

    public static ServerEndpoint Resolve(string? settingsUrl,
        string? environmentUrl)
    {
        if (!string.IsNullOrWhiteSpace(environmentUrl))
        {
            if (!TryNormalizeHttpUrl(environmentUrl, out string normalized,
                    out string error))
                throw new InvalidOperationException(
                    $"{EnvironmentVariableName} 값이 올바르지 않습니다: {error}");
            return new ServerEndpoint(CreateBaseUri(normalized), true);
        }

        string selected = NormalizeOrDefault(settingsUrl);
        return new ServerEndpoint(CreateBaseUri(selected), false);
    }

    public static ServerEndpoint FromHttpUrl(string url)
    {
        if (!TryNormalizeHttpUrl(url, out string normalized, out string error))
            throw new ArgumentException(error, nameof(url));
        return new ServerEndpoint(CreateBaseUri(normalized), false);
    }

    public static string NormalizeOrDefault(string? url) =>
        TryNormalizeHttpUrl(url, out string normalized, out _)
            ? normalized
            : DefaultHttpUrl;

    public static bool TryNormalizeHttpUrl(string? value,
        out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "서버 주소를 입력해 주세요.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "http:// 또는 https://로 시작하는 올바른 주소를 입력해 주세요.";
            return false;
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "서버 주소에 사용자 이름이나 비밀번호를 포함할 수 없습니다.";
            return false;
        }
        if (!string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "서버 주소에 쿼리 문자열이나 조각을 포함할 수 없습니다.";
            return false;
        }

        normalized = uri.AbsoluteUri.TrimEnd('/');
        return true;
    }

    private static Uri CreateBaseUri(string normalized) =>
        new(normalized.TrimEnd('/') + "/", UriKind.Absolute);
}
