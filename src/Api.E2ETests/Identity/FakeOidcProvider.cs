using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.E2ETests.Identity;

/// <summary>
/// A tiny but honest OpenID Connect provider on a loopback port: discovery, JWKS, and a token endpoint that checks
/// the confidential client credentials and the PKCE verifier before returning an RS256-signed id_token. The test plays
/// the browser + the user: it reads the authorize URL our API redirected to and mints the code itself
/// (<see cref="IssueCode"/>), which is what the IdP login page would do.
/// </summary>
internal sealed class FakeOidcProvider : IAsyncDisposable
{
    public const string ClientId = "starterkit-e2e";
    public const string ClientSecret = "e2e-client-secret";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();
    private WebApplication? _app;

    private FakeOidcProvider() => _key = new RsaSecurityKey(_rsa) { KeyId = "e2e-key-1" };

    public string Authority { get; private set; } = default!;

    /// <summary>Token requests that failed client authentication or PKCE (should stay 0).</summary>
    public int RejectedTokenRequests;

    public static async Task<FakeOidcProvider> StartAsync()
    {
        var provider = new FakeOidcProvider();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = provider.Authority,
            ["authorization_endpoint"] = provider.Authority + "/authorize",
            ["token_endpoint"] = provider.Authority + "/token",
            ["jwks_uri"] = provider.Authority + "/jwks",
            ["response_types_supported"] = new[] { "code" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" }
        }));

        app.MapGet("/jwks", () =>
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(provider._key);
            return Results.Json(new
            {
                keys = new[] { new { kty = jwk.Kty, use = "sig", kid = jwk.Kid, alg = "RS256", n = jwk.N, e = jwk.E } }
            });
        });

        app.MapPost("/token", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            if (!provider.ClientAuthenticated(request) || form["grant_type"] != "authorization_code" ||
                !provider._codes.TryRemove(form["code"].ToString(), out var pending) ||
                Challenge(form["code_verifier"].ToString()) != pending.CodeChallenge)
            {
                Interlocked.Increment(ref provider.RejectedTokenRequests);
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            return Results.Json(new Dictionary<string, object>
            {
                ["access_token"] = "opaque",
                ["token_type"] = "Bearer",
                ["id_token"] = provider.IdToken(pending)
            });
        });

        await app.StartAsync();
        provider._app = app;
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        provider.Authority = address.TrimEnd('/');
        return provider;
    }

    /// <summary>What the IdP does after the user logs in: remember the request and hand out a code.</summary>
    public string IssueCode(Uri authorizeUrl, string subject, string? email, bool mfa = false, string? nonceOverride = null)
    {
        var query = System.Web.HttpUtility.ParseQueryString(authorizeUrl.Query);
        query["client_id"].Should().Be(ClientId);
        query["code_challenge_method"].Should().Be("S256");

        var code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        _codes[code] = new PendingCode(subject, email, nonceOverride ?? query["nonce"]!, query["code_challenge"]!, mfa);
        return code;
    }

    private string IdToken(PendingCode pending)
    {
        var claims = new List<Claim> { new("sub", pending.Subject), new("nonce", pending.Nonce) };
        if (pending.Email is not null)
            claims.Add(new Claim("email", pending.Email));
        claims.Add(new Claim("given_name", "Ada"));
        claims.Add(new Claim("family_name", "Lovelace"));
        claims.Add(new Claim("amr", pending.Mfa ? "mfa" : "pwd"));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = ClientId,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256)
        });
    }

    private bool ClientAuthenticated(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.Ordinal))
            return false;

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
        return decoded == $"{Uri.EscapeDataString(ClientId)}:{Uri.EscapeDataString(ClientSecret)}";
    }

    private static string Challenge(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        _rsa.Dispose();
    }

    private sealed record PendingCode(string Subject, string? Email, string Nonce, string CodeChallenge, bool Mfa);
}
