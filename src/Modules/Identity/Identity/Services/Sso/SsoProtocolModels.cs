using System.Net;
using Identity.Options;
using Microsoft.Extensions.Options;
using Shared.Core.Net;

namespace Identity.Services.Sso;

/// <summary>Server-side state of one SSO round trip, addressed by the opaque <c>state</c> / RelayState value.</summary>
public sealed record SsoLoginState(
    Guid ConnectionId,
    string ReturnUrl,
    string? Nonce = null,
    string? CodeVerifier = null,
    string? SamlRequestId = null,
    bool TestMode = false,
    string? BrowserBindingHash = null);

/// <summary>The verified identity an IdP asserted (protocol-neutral).</summary>
/// <param name="Subject">Stable, IdP-unique user id (OIDC <c>sub</c>, SAML NameID). The account link key.</param>
/// <param name="EmailVerified">False only when the IdP explicitly says the email is NOT verified.</param>
/// <param name="MultiFactor">The IdP asserted multi-factor authentication.</param>
public sealed record SsoIdentity(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    bool MultiFactor,
    IReadOnlyDictionary<string, string> Claims);

/// <summary>After the callback: who signed in, where, and how strongly. Stored behind the one-time exchange code.</summary>
public sealed record SsoLoginGrant(Guid UserId, Guid TenantId, Guid ConnectionId, bool MultiFactor);

/// <summary>Named HTTP client for every outbound call to a tenant-configured IdP (discovery, JWKS, token, metadata).</summary>
public static class SsoHttp
{
    public const string ClientName = "identity-sso";

    /// <summary>
    /// SSRF-guarded handler: DNS is resolved and checked at connect time (no private, loopback or metadata
    /// addresses unless <c>AllowInsecureIdentityProviders</c>), redirects are not followed, short timeout.
    /// </summary>
    public static IServiceCollection AddSsoHttpClient(this IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("StarterKit-SSO/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SsoOptions>>().Value;
                var connector = new GuardedConnector(options.AllowInsecureIdentityProviders);
                return new SocketsHttpHandler
                {
                    ConnectCallback = connector.ConnectAsync,
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    MaxResponseHeadersLength = 64
                };
            });
        return services;
    }

    /// <summary>HTTPS required for IdP URLs unless explicitly relaxed (development).</summary>
    public static bool IsAcceptableUrl(string? url, bool allowInsecure) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (allowInsecure && uri.Scheme == Uri.UriSchemeHttp))
        && string.IsNullOrEmpty(uri.UserInfo);
}
