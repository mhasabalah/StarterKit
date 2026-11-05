using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Sso;
using Identity.Services.Sso;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Domains.Sso.Features.SsoLogin;

public record DiscoverSsoRequest(string Email);

public record ExchangeSsoCodeRequest(string Code);

/// <summary>
/// The public half of enterprise SSO. Browser-facing routes (start, callbacks) always answer with a redirect; only
/// the final <c>exchange</c> returns tokens, to the application's own JavaScript - tokens never travel in a URL.
/// </summary>
public class SsoLoginModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(SsoUrls.RoutePrefix)
            .WithTags("Enterprise SSO")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .HasApiVersion(1.0);

        group.MapPost("discover", async (DiscoverSsoRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new DiscoverSsoQuery(request.Email), ct))))
            .WithName("DiscoverSso")
            .WithSummary("Home-realm discovery")
            .WithDescription("Email-first sign-in: tells whether the email's domain signs in through an IdP (and " +
                             "whether that is mandatory). Reveals nothing about accounts.")
            .Produces<ApiResponse<SsoDiscoveryResult>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapGet("{connectionId:guid}/start", async (
                Guid connectionId, [FromQuery] string returnUrl, [FromQuery] string? loginHint, HttpContext http,
                ISender sender, CancellationToken ct) =>
            {
                var redirect = await sender.Send(new StartSsoLoginCommand(connectionId, returnUrl, loginHint), ct);
                SsoBrowserBinding.Set(http, redirect.BrowserBinding);
                return Results.Redirect(redirect.Url);
            })
            .WithName("StartSsoLogin")
            .WithSummary("Start SSO sign-in (browser navigation)")
            .WithDescription("302 to the IdP. returnUrl must be on an allowed application origin; after sign-in the " +
                             "browser returns there with sso_code (exchange it) or sso_error.")
            .Produces(StatusCodes.Status302Found)
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapGet("oidc/callback", async (
                [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error,
                [FromQuery(Name = "error_description")] string? errorDescription, HttpContext http, ISender sender,
                CancellationToken ct) =>
            {
                var result = await sender.Send(
                    new CompleteOidcLoginCommand(code, state, error, errorDescription, SsoBrowserBinding.Take(http)), ct);
                return Results.Redirect(result.RedirectUrl);
            })
            .WithName("CompleteOidcLogin")
            .WithSummary("OIDC redirect URI")
            .ExcludeFromDescription()
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("saml/{connectionId:guid}/acs", async (Guid connectionId, HttpContext http, ISender sender,
                CancellationToken ct) =>
            {
                var form = await http.Request.ReadFormAsync(ct);
                var result = await sender.Send(new CompleteSamlLoginCommand(
                    connectionId, form["SAMLResponse"].ToString(), form["RelayState"].ToString(),
                    SsoBrowserBinding.Take(http)), ct);
                return Results.Redirect(result.RedirectUrl);
            })
            .WithName("CompleteSamlLogin")
            .WithSummary("SAML Assertion Consumer Service (HTTP-POST)")
            .ExcludeFromDescription()
            .DisableAntiforgery() // cross-site POST from the IdP by design; the signed response is the proof
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapGet("saml/{connectionId:guid}/metadata", async (Guid connectionId, ISender sender, CancellationToken ct) =>
                Results.Content(await sender.Send(new GetSsoSamlMetadataQuery(connectionId), ct), "application/samlmetadata+xml"))
            .WithName("GetSsoSamlMetadata")
            .WithSummary("SAML service-provider metadata")
            .WithDescription("Upload to the IdP (or give it this URL) to configure the SAML app.")
            .Produces(StatusCodes.Status200OK, contentType: "application/samlmetadata+xml")
            .AllowAnonymous();

        group.MapPost("exchange", async (ExchangeSsoCodeRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ExchangeSsoCodeCommand(request.Code), ct);
                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            .WithName("ExchangeSsoCode")
            .WithSummary("Exchange the SSO code for a session")
            .WithDescription("Single use, short lived. Same result as /api/auth/login (tokens, or a 2FA challenge).")
            .Produces<ApiResponse<LoginResult>>()
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
    }
}

/// <summary>
/// The login-CSRF cookie: set by <c>start</c>, read (and cleared) by the callbacks, scoped to the SSO routes,
/// HttpOnly, 10 minutes. HTTPS: SameSite=None so the IdP cross-site SAML POST still carries it; plain HTTP (local
/// development only): SameSite=Lax, which covers the OIDC top-level GET redirect.
/// </summary>
internal static class SsoBrowserBinding
{
    public const string CookieName = "sk_sso_binding";

    public static void Set(HttpContext http, string? value)
    {
        if (value is null)
            return;

        http.Response.Cookies.Append(CookieName, value, Options(http, TimeSpan.FromMinutes(10)));
    }

    public static string? Take(HttpContext http)
    {
        var value = http.Request.Cookies[CookieName];
        if (value is not null)
            http.Response.Cookies.Delete(CookieName, Options(http, null));
        return value;
    }

    private static CookieOptions Options(HttpContext http, TimeSpan? lifetime) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = http.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
        Path = "/" + SsoUrls.RoutePrefix,
        MaxAge = lifetime,
        IsEssential = true
    };
}
