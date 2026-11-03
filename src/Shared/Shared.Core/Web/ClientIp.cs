using System.Net;
using Microsoft.AspNetCore.Http;

namespace Shared.Core.Web;

/// <summary>
/// Single source of truth for "who is the client".
/// Reads <c>Connection.RemoteIpAddress</c> only: X-Forwarded-For is applied by the ForwardedHeaders
/// middleware (and only from trusted proxies), so raw forwarding headers are never read here.
/// </summary>
public static class ClientIp
{
    public static IPAddress? GetRemoteIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null) return null;
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    public static string? GetRemoteIpString(HttpContext context) => GetRemoteIp(context)?.ToString();
}
