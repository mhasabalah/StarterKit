using Shared.Core.Net;
using System.Net;
using Microsoft.Extensions.Options;

namespace Webhooks.Security;

public enum WebhookUrlCheck
{
    Ok,
    Empty,
    TooLong,
    Invalid,
    HttpsRequired,
    HasCredentials,
    BlockedHost
}

public interface IWebhookUrlPolicy
{
    /// <summary>
    /// Static checks on a URL a tenant registers: absolute, http(s) (https only unless configured), no credentials, and
    /// no literal blocked IP / well-known internal host names. Names that merely RESOLVE to a private address are caught
    /// at connect time by <see cref="GuardedConnector"/> (the only check that survives DNS changes).
    /// </summary>
    WebhookUrlCheck Check(string? url);
}

public sealed class WebhookUrlPolicy(IOptions<WebhooksOptions> options) : IWebhookUrlPolicy
{
    private const int MaxLength = 2048;

    private static readonly string[] InternalSuffixes =
        [".localhost", ".local", ".internal", ".localdomain", ".home.arpa", ".lan", ".intranet", ".corp"];

    public WebhookUrlCheck Check(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return WebhookUrlCheck.Empty;
        if (url.Length > MaxLength)
            return WebhookUrlCheck.TooLong;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrEmpty(uri.Host))
        {
            return WebhookUrlCheck.Invalid;
        }

        var settings = options.Value;
        if (uri.Scheme == Uri.UriSchemeHttp && settings.RequireHttps)
            return WebhookUrlCheck.HttpsRequired;
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return WebhookUrlCheck.HasCredentials;

        if (!settings.AllowPrivateNetworks && IsBlockedHost(uri))
        {
            return WebhookUrlCheck.BlockedHost;
        }

        return WebhookUrlCheck.Ok;
    }

    private static bool IsBlockedHost(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Length == 0)
            return true;

        // [::1] arrives bracketed in Uri.Host but not in IdnHost; handle both.
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return NetworkAddressPolicy.IsBlocked(literal);
        }

        // Single-label names ("intranet", "localhost") only resolve inside a private network.
        if (!host.Contains('.'))
            return true;

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               InternalSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
