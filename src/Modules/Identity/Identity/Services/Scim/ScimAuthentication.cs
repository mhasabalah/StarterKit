using System.Text.Encodings.Web;
using Identity.Domains.Scim.Models;
using Microsoft.Extensions.Options;

namespace Identity.Services.Scim;

public static class ScimAuthentication
{
    public const string SchemeName = "Scim";
    public const string PolicyName = "scim";
    public const string DirectoryClaim = "scim_directory";
    public const string TenantClaim = "scim_tenant";
    public const string RoutePrefix = "scim/v2";

    /// <summary>
    /// Registers the scheme and a policy bound to it. Endpoints use <c>.RequireAuthorization(PolicyName)</c>; the
    /// policy authenticates with THIS scheme only, so a SCIM token never authenticates anything else and a JWT or API
    /// key never reaches SCIM.
    /// </summary>
    public static IServiceCollection AddScimAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ScimAuthenticationHandler>(SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy => policy
                .AddAuthenticationSchemes(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DirectoryClaim));
        return services;
    }

    public static Guid DirectoryId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(DirectoryClaim) ?? throw new UnauthorizedException("Not a SCIM client."));

    public static Guid TenantId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(TenantClaim) ?? throw new UnauthorizedException("Not a SCIM client."));
}

/// <summary>
/// <c>Authorization: Bearer scim_{prefix}_{secret}</c> → the enabled directory whose hash matches (constant time).
/// Last use is recorded at most once a minute (cheap single-row update, no tracking).
/// </summary>
public sealed class ScimAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ApplicationIdentityDbContext dbContext,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        var prefix = ScimDirectory.PrefixOf(token);
        if (prefix is null)
            return AuthenticateResult.NoResult();

        var directory = await dbContext.ScimDirectories.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TokenPrefix == prefix, Context.RequestAborted);
        if (directory is null || !directory.Matches(token) || !directory.Enabled)
            return AuthenticateResult.Fail("Invalid SCIM token.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (directory.LastRequestAt is null || now - directory.LastRequestAt > TimeSpan.FromMinutes(1))
        {
            await dbContext.ScimDirectories.Where(d => d.Id == directory.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastRequestAt, now), Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ScimAuthentication.DirectoryClaim, directory.Id.ToString()),
            new Claim(ScimAuthentication.TenantClaim, directory.TenantId.ToString())
        ], ScimAuthentication.SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ScimAuthentication.SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = Scim.ScimSchemas.ContentType;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"scim\"";
        return Response.WriteAsync(ScimJson.Error(401, "A valid SCIM bearer token is required.", null).ToJsonString());
    }
}
