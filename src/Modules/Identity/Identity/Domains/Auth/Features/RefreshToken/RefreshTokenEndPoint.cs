using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.RefreshToken;

public class RefreshTokenEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/refresh", async (
            RefreshTokenCommand command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);

            return Results.Ok(new ApiResponse<RefreshTokenResult>(
                true,
                StatusCodes.Status200OK,
                result,
                result.Message));
        })
        .WithName("RefreshToken")
        .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
        .WithTags("Authentication")
        .Produces<ApiResponse<RefreshTokenResult>>()
        .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
        .AllowAnonymous()
        .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
        .WithSummary("Refresh access token")
        .WithDescription("Exchange a refresh token for a new access token and refresh token (token rotation)")
        .RequireRateLimiting("auth")
        .HasApiVersion(1.0);
    }
}
