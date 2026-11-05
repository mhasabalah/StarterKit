using Shared.Core.Authorization;
using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.EnableTwoFactor;

public class EnableTwoFactorEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/2fa/enable", async (
                EnableTwoFactorRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContextService.GetUserIdAsGuid();
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await sender.Send(new EnableTwoFactorCommand(userId.Value, request.Code), cancellationToken);
                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            .DisallowApiKey()
            .WithName("EnableTwoFactor")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Two-Factor Authentication")
            .WithSummary("Enable two-factor authentication")
            .WithDescription(
                "Verifies a code from the authenticator set up via /api/auth/2fa/setup, enables 2FA and returns " +
                "one-time recovery codes plus a new MFA session (all other sessions are signed out).")
            .Produces<ApiResponse<EnableTwoFactorResult>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(409)
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}

public record EnableTwoFactorRequest(string Code);
