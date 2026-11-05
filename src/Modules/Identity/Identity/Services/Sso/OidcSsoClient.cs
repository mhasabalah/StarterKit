using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Identity.Contracts.Sso;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Shared.Core.MultiTenancy.Caching;

namespace Identity.Services.Sso;

/// <summary>
/// Caches one discovery/JWKS <see cref="ConfigurationManager{T}"/> per authority in the process-wide
/// <see cref="IMemoryCache"/> (singleton). Keys rotate automatically (the manager refreshes on its interval and on
/// demand when a signature does not validate). <see cref="ForgetAsync"/> evicts locally AND broadcasts the eviction
/// (<see cref="ILocalCacheInvalidationBus"/>, Redis pub/sub) so every replica re-reads an edited connection at once.
/// </summary>
public sealed class OidcConfigurationCache(
    IHttpClientFactory httpClientFactory,
    IOptions<SsoOptions> options,
    IMemoryCache memoryCache,
    ILocalCacheInvalidationBus invalidationBus)
{
    private const string KeyPrefix = "identity:sso:oidc:";

    /// <summary>Unused managers leave the cache after a day; an active one is kept alive by sign-ins.</summary>
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(24);

    public static string CacheKey(string authority) => KeyPrefix + authority.TrimEnd('/').ToLowerInvariant();

    public Task<OpenIdConnectConfiguration> GetAsync(string authority, CancellationToken ct) =>
        Manager(authority).GetConfigurationAsync(ct);

    /// <summary>Forces the next call to re-download discovery + keys (unknown <c>kid</c>).</summary>
    public void Refresh(string authority) => Manager(authority).RequestRefresh();

    /// <summary>Drops the cached documents of the given authorities here and on every other replica.</summary>
    public async Task ForgetAsync(IEnumerable<string?> authorities, CancellationToken ct)
    {
        var keys = authorities
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => CacheKey(a!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0)
            return;

        foreach (var key in keys)
        {
            memoryCache.Remove(key);
        }

        await invalidationBus.PublishAsync(keys, ct);
    }

    private ConfigurationManager<OpenIdConnectConfiguration> Manager(string authority) =>
        memoryCache.GetOrCreate(CacheKey(authority), entry =>
        {
            entry.SlidingExpiration = SlidingExpiration;
            entry.Size = 1;
            var retriever = new HttpDocumentRetriever(httpClientFactory.CreateClient(SsoHttp.ClientName))
            {
                RequireHttps = !options.Value.AllowInsecureIdentityProviders
            };
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                authority.TrimEnd('/') + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(), retriever)
            {
                AutomaticRefreshInterval = TimeSpan.FromHours(12),
                RefreshInterval = TimeSpan.FromMinutes(1)
            };
        })!;
}

/// <summary>
/// OpenID Connect relying party for one tenant connection: authorization-code flow with PKCE (S256), state and
/// nonce, confidential client (<c>client_secret_basic</c>). The ID token is validated locally against the IdP's
/// published keys: issuer, audience, lifetime, signature algorithm allow-list and nonce.
/// </summary>
public sealed class OidcSsoClient(
    OidcConfigurationCache configurations,
    IHttpClientFactory httpClientFactory,
    ISsoSecretProtector secrets,
    SsoUrls urls,
    IOptions<SsoOptions> options,
    ILogger<OidcSsoClient> logger)
{
    private static readonly string[] AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512
    ];

    public static string NewNonce() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string NewCodeVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string CodeChallenge(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public async Task<SsoRedirect> BuildAuthorizationRedirectAsync(
        SsoConnection connection, string state, SsoLoginState login, string? loginHint, CancellationToken ct)
    {
        var configuration = await LoadConfigurationAsync(connection, ct);

        // Discovery is tenant-controlled: never let it turn our start URL into an open redirect to another host.
        if (!SsoHttp.IsAcceptableUrl(configuration.AuthorizationEndpoint, options.Value.AllowInsecureIdentityProviders) ||
            !SameHost(configuration.AuthorizationEndpoint, connection.OidcAuthority!))
            throw new SsoLoginFailedException("The IdP authorization endpoint is not on the authority host.");

        var scopes = "openid email profile";
        if (!string.IsNullOrWhiteSpace(connection.OidcExtraScopes))
            scopes += " " + connection.OidcExtraScopes;

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["response_mode"] = "query",
            ["client_id"] = connection.OidcClientId,
            ["redirect_uri"] = urls.OidcRedirectUri,
            ["scope"] = scopes,
            ["state"] = state,
            ["nonce"] = login.Nonce,
            ["code_challenge"] = CodeChallenge(login.CodeVerifier!),
            ["code_challenge_method"] = "S256",
            ["login_hint"] = string.IsNullOrWhiteSpace(loginHint) ? null : loginHint
        };

        var url = configuration.AuthorizationEndpoint + (configuration.AuthorizationEndpoint.Contains('?') ? "&" : "?") +
                  string.Join('&', query.Where(p => p.Value is not null)
                      .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));
        return new SsoRedirect(url);
    }

    public async Task<SsoIdentity> RedeemAsync(SsoConnection connection, string code, SsoLoginState login, CancellationToken ct)
    {
        var configuration = await LoadConfigurationAsync(connection, ct);
        if (!SsoHttp.IsAcceptableUrl(configuration.TokenEndpoint, options.Value.AllowInsecureIdentityProviders))
            throw new SsoLoginFailedException("The IdP token endpoint is not an acceptable URL.");

        var idToken = await RequestIdTokenAsync(connection, configuration.TokenEndpoint, code, login.CodeVerifier!, ct);

        var validation = await ValidateAsync(connection, configuration, idToken);
        if (!validation.IsValid && validation.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            // Key rollover at the IdP: refresh JWKS once and retry.
            configurations.Refresh(connection.OidcAuthority!);
            configuration = await configurations.GetAsync(connection.OidcAuthority!, ct);
            validation = await ValidateAsync(connection, configuration, idToken);
        }

        if (!validation.IsValid)
        {
            logger.LogWarning(validation.Exception, "OIDC id_token rejected for connection {ConnectionId}", connection.Id);
            throw new SsoLoginFailedException("id_token_invalid");
        }

        var claims = validation.Claims;
        if (!string.Equals(Claim(claims, "nonce"), login.Nonce, StringComparison.Ordinal))
            throw new SsoLoginFailedException("nonce_mismatch");

        var subject = Claim(claims, "sub") ?? throw new SsoLoginFailedException("missing_sub");
        var email = Claim(claims, connection.EmailClaim)
                    ?? Claim(claims, "email")
                    ?? EmailLike(Claim(claims, "upn"));
        var emailVerified = !string.Equals(Claim(claims, "email_verified"), "false", StringComparison.OrdinalIgnoreCase);

        return new SsoIdentity(
            subject,
            email,
            emailVerified,
            Claim(claims, connection.GivenNameClaim) ?? Claim(claims, "given_name"),
            Claim(claims, connection.FamilyNameClaim) ?? Claim(claims, "family_name"),
            AssertsMfa(claims),
            claims.Where(c => c.Value is string or bool or long or int)
                .ToDictionary(c => c.Key, c => Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
    }

    private async Task<OpenIdConnectConfiguration> LoadConfigurationAsync(SsoConnection connection, CancellationToken ct)
    {
        if (!SsoHttp.IsAcceptableUrl(connection.OidcAuthority, options.Value.AllowInsecureIdentityProviders))
            throw new SsoLoginFailedException("The OIDC authority is not an acceptable URL.");

        try
        {
            return await configurations.GetAsync(connection.OidcAuthority!, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException)
        {
            logger.LogWarning(ex, "OIDC discovery failed for connection {ConnectionId}", connection.Id);
            throw new SsoLoginFailedException("discovery_failed");
        }
    }

    private async Task<string> RequestIdTokenAsync(
        SsoConnection connection, string tokenEndpoint, string code, string codeVerifier, CancellationToken ct)
    {
        var secret = connection.OidcClientSecretProtected is null
            ? throw new SsoLoginFailedException("client_secret_missing")
            : secrets.Unprotect(connection.OidcClientSecretProtected);

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = urls.OidcRedirectUri,
            ["code_verifier"] = codeVerifier
        });

        // client_secret_basic (RFC 6749 §2.3.1: form-encode id and secret before Base64).
        var basic = $"{Uri.EscapeDataString(connection.OidcClientId!)}:{Uri.EscapeDataString(secret)}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basic)));
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClientFactory.CreateClient(SsoHttp.ClientName).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OIDC token request for connection {ConnectionId} failed with {Status}",
                connection.Id, (int)response.StatusCode);
            throw new SsoLoginFailedException("token_request_failed");
        }

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return body?.IdToken ?? throw new SsoLoginFailedException("missing_id_token");
    }

    private Task<TokenValidationResult> ValidateAsync(
        SsoConnection connection, OpenIdConnectConfiguration configuration, string idToken) =>
        new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = configuration.Issuer,
            ValidAudience = connection.OidcClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = AllowedAlgorithms,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Clamp(options.Value.ClockSkewSeconds, 0, 600))
        });

    private static bool AssertsMfa(IDictionary<string, object> claims)
    {
        var methods = claims.TryGetValue("amr", out var amr)
            ? amr switch
            {
                string s => [s],
                IEnumerable<object> list => list.Select(o => o.ToString() ?? string.Empty).ToArray(),
                _ => []
            }
            : Array.Empty<string>();

        // RFC 8176: "mfa" is explicit; hardware/software-bound keys (passkeys, FIDO) are phishing-resistant MFA.
        // A lone "otp" or "pwd" is ONE factor.
        if (methods.Any(m => m is "mfa" or "hwk" or "swk" or "fido"))
            return true;

        var acr = Claim(claims, "acr");
        return acr is not null && (acr.Contains("mfa", StringComparison.OrdinalIgnoreCase) ||
                                   acr.Contains("phr", StringComparison.OrdinalIgnoreCase));
    }

    private static string? Claim(IDictionary<string, object> claims, string? name) =>
        name is not null && claims.TryGetValue(name, out var value) && value is not null
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) is { Length: > 0 } s ? s : null
            : null;

    private static bool SameHost(string url, string authority) =>
        Uri.TryCreate(url, UriKind.Absolute, out var a) && Uri.TryCreate(authority, UriKind.Absolute, out var b) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    private static string? EmailLike(string? value) => value is not null && value.Contains('@') ? value : null;

    private sealed record TokenResponse([property: JsonPropertyName("id_token")] string? IdToken);
}
