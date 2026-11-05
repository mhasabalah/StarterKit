using Microsoft.AspNetCore.Http;

namespace Shared.Core.Web;

/// <summary>
/// Where real-time (SignalR) hubs are mapped. Browsers cannot send an <c>Authorization</c> header on a WebSocket (or
/// SSE) request, so the access token travels as <c>?access_token=</c> - accepted by the JWT scheme ONLY under this
/// prefix (never on ordinary API routes, where a token in a URL would end up in logs, history and referrers).
/// </summary>
public static class RealtimePaths
{
    /// <summary>Path prefix of every hub (<c>/hubs/notifications</c>, ...). Nginx proxies it with WebSocket upgrade.</summary>
    public const string HubPrefix = "/hubs";

    /// <summary>Query-string parameter the SignalR clients use for <c>accessTokenFactory</c>.</summary>
    public const string AccessTokenQueryParameter = "access_token";

    public static bool IsHubRequest(PathString path) =>
        path.StartsWithSegments(HubPrefix, StringComparison.OrdinalIgnoreCase);
}
