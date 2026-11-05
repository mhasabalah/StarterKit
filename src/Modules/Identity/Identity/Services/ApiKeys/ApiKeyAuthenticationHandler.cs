using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;

namespace Identity.Services.ApiKeys;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>Where a request carries its API key: <c>X-Api-Key: &lt;key&gt;</c> or <c>Authorization: ApiKey &lt;key&gt;</c>.</summary>
public static class ApiKeyRequest
{
    public const string HeaderName = "X-Api-Key";
    public const string AuthorizationScheme = "ApiKey";

    public static string? Extract(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith(AuthorizationScheme + " ", StringComparison.OrdinalIgnoreCase))
        {
            var value = authorization[(AuthorizationScheme.Length + 1)..].Trim();
            return value.Length > 0 ? value : null;
        }

        var header = request.Headers[HeaderName].ToString();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }

    /// <summary>Policy-scheme selector: an explicit Bearer token wins, otherwise a key selects the ApiKey scheme.</summary>
    public static string? SelectScheme(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        return Extract(context.Request) is not null ? ApiKeyClaimTypes.SchemeName : null;
    }
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var rawKey = ApiKeyRequest.Extract(Request);
        if (rawKey is null) return AuthenticateResult.NoResult();

        var authenticator = Context.RequestServices.GetRequiredService<IApiKeyAuthenticator>();
        var result = await authenticator.AuthenticateAsync(rawKey, Context.RequestAborted);

        return result.Succeeded
            ? AuthenticateResult.Success(new AuthenticationTicket(result.Principal!, Scheme.Name))
            : AuthenticateResult.Fail(result.Failure ?? "Invalid API key.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.Append("WWW-Authenticate", ApiKeyRequest.AuthorizationScheme);
        return base.HandleChallengeAsync(properties);
    }
}
