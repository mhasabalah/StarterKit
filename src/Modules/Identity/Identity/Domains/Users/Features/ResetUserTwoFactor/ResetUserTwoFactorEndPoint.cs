using Identity.Contracts.Users.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.ResetUserTwoFactor;

public class ResetUserTwoFactorEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/users/{id:guid}/2fa/reset", async (
                [FromRoute] Guid id,
                [FromBody] ResetUserTwoFactorRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var result = await sender.Send(new ResetUserTwoFactorCommand
                {
                    UserId = id,
                    Reason = request.Reason,
                    ResetByAdminId = adminId.Value
                }, cancellationToken);

                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            // admins.manage = SuperAdmin only (RolePermissionMap); the handler re-checks against the database.
            .RequirePermission(Permissions.AdminsManage)
            .DisallowApiKey()
            .WithName("ResetUserTwoFactor")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .WithSummary("Reset a user's two-factor authentication")
            .WithDescription(
                "SuperAdmin only. Removes the user's authenticator and recovery codes and signs out all of their " +
                "sessions. Audited.")
            .Produces<ApiResponse<ResetUserTwoFactorResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record ResetUserTwoFactorRequest
{
    public string Reason { get; init; } = default!;
}
