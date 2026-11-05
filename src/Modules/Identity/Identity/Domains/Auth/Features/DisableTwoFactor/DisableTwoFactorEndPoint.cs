using Shared.Core.Authorization;
using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.DisableTwoFactor;

public class DisableTwoFactorEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/2fa/disable", async (
                DisableTwoFactorRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContextService.GetUserIdAsGuid();
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await sender.Send(
                    new DisableTwoFactorCommand(userId.Value, request.Password, request.Code), cancellationToken);
                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            .DisallowApiKey()
            .WithName("DisableTwoFactor")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Two-Factor Authentication")
            .WithSummary("Disable two-factor authentication")
            .WithDescription(
                "Requires the current password and an authenticator code (or a recovery code). Removes the " +
                "authenticator and recovery codes, signs out every other session and returns a new session.")
            .Produces<ApiResponse<DisableTwoFactorResult>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}

public record DisableTwoFactorRequest(string Password, string Code);
