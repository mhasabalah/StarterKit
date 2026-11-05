using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Identity.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Identity.Services.Sso;

/// <summary>Encrypts OIDC client secrets at rest (Data Protection, purpose <see cref="Purpose"/>).</summary>
public interface ISsoSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

public sealed class SsoSecretProtector(IDataProtectionProvider provider) : ISsoSecretProtector
{
    public const string Purpose = "Identity.Sso.OidcClientSecret.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>
/// "Has this been used?" across all instances: Redis <c>SET NX</c> when Redis is on, otherwise a process-local gate
/// over the distributed cache (single instance / tests). Used for SSO state, exchange codes and SAML response ids.
/// </summary>
public interface IOneTimeUseGuard
{
    /// <summary>True the first time <paramref name="key"/> is seen within <paramref name="window"/>, false after.</summary>
    Task<bool> TryUseAsync(string key, TimeSpan window, CancellationToken ct = default);
}

public sealed class RedisOneTimeUseGuard(IConnectionMultiplexer redis, string keyPrefix) : IOneTimeUseGuard
{
    public async Task<bool> TryUseAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await redis.GetDatabase().StringSetAsync(keyPrefix + "identity:once:" + key, "1", window, When.NotExists);
    }
}

public sealed class DistributedCacheOneTimeUseGuard(IDistributedCache cache) : IOneTimeUseGuard
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<bool> TryUseAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var cacheKey = "identity:once:" + key;
        await Gate.WaitAsync(ct);
        try
        {
            if (await cache.GetAsync(cacheKey, ct) is not null)
                return false;

            await cache.SetAsync(cacheKey, [1],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = window }, ct);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>
/// Short-lived server-side records addressed by an unguessable id (SSO login state, exchange codes). Consuming is
/// atomic across instances through <see cref="IOneTimeUseGuard"/>: two concurrent callbacks with the same id cannot
/// both succeed.
/// </summary>
public sealed class SsoOneTimeStore(IDistributedCache cache, IOneTimeUseGuard guard)
{
    private const string Prefix = "identity:sso:";

    public static string NewId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public async Task<string> CreateAsync<T>(string kind, T value, TimeSpan lifetime, CancellationToken ct = default)
    {
        var id = NewId();
        await StoreAsync(kind, id, value, lifetime, ct);
        return id;
    }

    /// <summary>Stores under a caller-chosen id (from <see cref="NewId"/>) when the id is needed before the value.</summary>
    public Task StoreAsync<T>(string kind, string id, T value, TimeSpan lifetime, CancellationToken ct = default) =>
        cache.SetAsync(Prefix + kind + ":" + id, JsonSerializer.SerializeToUtf8Bytes(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, ct);

    public async Task<T?> ConsumeAsync<T>(string kind, string? id, TimeSpan lifetime, CancellationToken ct = default)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            return null;

        var key = Prefix + kind + ":" + id;
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null)
            return null;

        if (!await guard.TryUseAsync("sso:" + kind + ":" + id, lifetime, ct))
            return null;

        await cache.RemoveAsync(key, ct);
        return JsonSerializer.Deserialize<T>(bytes);
    }
}

/// <summary>
/// The public URLs of the service provider side (what the customer's IdP admin configures). Derived from
/// <c>Identity:Sso:PublicBaseUrl</c>; never from client input in production.
/// </summary>
public sealed class SsoUrls(
    IOptions<SsoOptions> options,
    IHttpContextAccessor httpContextAccessor,
    Microsoft.Extensions.Hosting.IHostEnvironment environment)
{
    public const string RoutePrefix = "api/auth/sso";

    public string BaseUrl
    {
        get
        {
            var configured = options.Value.PublicBaseUrl;
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            // The Host header is client input: deriving redirect/ACS URLs from it is only acceptable in development.
            if (!environment.IsDevelopmentLike())
                throw new InvalidOperationException("Identity:Sso:PublicBaseUrl must be configured outside development.");

            var request = httpContextAccessor.HttpContext?.Request
                          ?? throw new InvalidOperationException("Identity:Sso:PublicBaseUrl is not configured.");
            return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
        }
    }

    public string OidcRedirectUri => $"{BaseUrl}/{RoutePrefix}/oidc/callback";

    /// <summary>One SP entity id per connection, so two tenants can use separate apps in the same IdP.</summary>
    public string SamlEntityId(Guid connectionId) => $"{BaseUrl}/{RoutePrefix}/saml/{connectionId}";

    public string SamlAcsUrl(Guid connectionId) => $"{BaseUrl}/{RoutePrefix}/saml/{connectionId}/acs";

    public string SamlMetadataUrl(Guid connectionId) => $"{BaseUrl}/{RoutePrefix}/saml/{connectionId}/metadata";
}

/// <summary>
/// Validates where a sign-in may return to: absolute http(s) URL on an allowed frontend origin, no credentials, no
/// fragment. Blocks the classic SSO open redirect (attacker-supplied returnUrl receiving the one-time code).
/// </summary>
public sealed class SsoReturnUrlPolicy(IOptions<SsoOptions> options, IOptions<ApplicationOptions> application)
{
    public bool IsAllowed(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl.Length > 2048 ||
            !Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var origin = uri.GetLeftPart(UriPartial.Authority);
        return AllowedOrigins().Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Where errors go when the original return URL is unknown (state expired or forged).</summary>
    public string FallbackUrl => application.Value.FrontendBaseUrl.TrimEnd('/') + "/login";

    private IEnumerable<string> AllowedOrigins()
    {
        var configured = options.Value.AllowedReturnOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
        if (configured.Count == 0)
            configured.Add(application.Value.FrontendBaseUrl);

        return configured.Select(o => Uri.TryCreate(o.Trim(), UriKind.Absolute, out var u)
            ? u.GetLeftPart(UriPartial.Authority)
            : o.Trim().TrimEnd('/'));
    }

    public static string AppendQuery(string url, string name, string value)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }
}
