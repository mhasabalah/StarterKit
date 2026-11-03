using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.Logout;

public class LogoutEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/logout", async (
            LogoutCommand command,
            ISender sender) =>
        {
            var result = await sender.Send(command);

            return Results.Ok(new ApiResponse<LogoutResult>(
                result.Success,
                StatusCodes.Status200OK,
                result,
                result.Message));
        })
        .WithName("Logout")
        .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
        .WithTags("Authentication")
        .Produces<ApiResponse<LogoutResult>>()
        .WithSummary("Logout and invalidate tokens")
        .WithDescription(@"Revokes the current access token and optionally the refresh token.
            Set logoutAllDevices=true to revoke all refresh tokens for this user.")
        .RequireAuthorization() // Must be authenticated to logout
        .HasApiVersion(1.0);
    }
}
