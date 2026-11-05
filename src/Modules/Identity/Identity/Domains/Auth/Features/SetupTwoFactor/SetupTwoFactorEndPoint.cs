using Shared.Core.Authorization;
using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.SetupTwoFactor;

public class SetupTwoFactorEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/2fa/setup", async (
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContextService.GetUserIdAsGuid();
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await sender.Send(new SetupTwoFactorCommand(userId.Value), cancellationToken);
                return Results.Ok(ApiResponse.Success(result,
                    "Scan the QR code (or enter the key) in your authenticator app, then call /api/auth/2fa/enable."));
            })
            .DisallowApiKey()
            .WithName("SetupTwoFactor")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Two-Factor Authentication")
            .WithSummary("Start two-factor enrollment")
            .WithDescription(
                "Creates a new pending authenticator (TOTP) key for the signed-in user and returns the shared key " +
                "and an otpauth:// URI. 2FA is not active until POST /api/auth/2fa/enable succeeds.")
            .Produces<ApiResponse<SetupTwoFactorResult>>()
            .ProducesProblem(401)
            .ProducesProblem(409)
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}
