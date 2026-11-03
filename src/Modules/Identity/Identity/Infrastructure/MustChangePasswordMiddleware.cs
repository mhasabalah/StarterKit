using Identity.Services.Token;

namespace Identity.Infrastructure;

/// <summary>
/// While a token carries <c>must_change_password=true</c> (e.g. the seeded SuperAdmin), only the endpoints needed to
/// change the password or end the session are reachable. Paths match the routes the Carter modules actually map.
/// </summary>
public class MustChangePasswordMiddleware(RequestDelegate next)
{
    public static readonly string[] AllowedPaths =
    [
        "/api/auth/change-password",
        "/api/auth/reset-password",
        "/api/auth/refresh",
        "/api/auth/logout"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(IdentityClaimTypes.MustChangePassword)?.Value == "true"
            && !IsAllowed(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "MUST_CHANGE_PASSWORD",
                message = "You must change your password before continuing."
            });
            return;
        }

        await next(context);
    }

    public static bool IsAllowed(PathString path) =>
        AllowedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
