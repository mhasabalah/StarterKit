using Shared.Core.Authorization;
using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.RegenerateRecoveryCodes;

public class RegenerateRecoveryCodesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/2fa/recovery-codes/regenerate", async (
                RegenerateRecoveryCodesRequest request,
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
                    new RegenerateRecoveryCodesCommand(userId.Value, request.Code), cancellationToken);
                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            .DisallowApiKey()
            .WithName("RegenerateRecoveryCodes")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Two-Factor Authentication")
            .WithSummary("Regenerate two-factor recovery codes")
            .WithDescription("Requires a current authenticator code. Old recovery codes stop working.")
            .Produces<ApiResponse<RegenerateRecoveryCodesResult>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}

public record RegenerateRecoveryCodesRequest(string Code);
