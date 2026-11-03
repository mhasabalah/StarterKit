using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.ChangePassword;

public class ChangePasswordEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/change-password", async (
                ChangePasswordRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContextService.GetUserIdAsGuid();
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await sender.Send(new ChangePasswordCommand(
                    userId.Value, request.CurrentPassword, request.NewPassword, request.ConfirmPassword),
                    cancellationToken);

                return Results.Ok(new ApiResponse<ChangePasswordResult>(
                    true, StatusCodes.Status200OK, result, result.Message));
            })
            .WithName("ChangePassword")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .WithSummary("Change the signed-in user's password")
            .WithDescription(
                "Requires the current password. Signs out every other session and returns a new token pair.")
            .Produces<ApiResponse<ChangePasswordResult>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
