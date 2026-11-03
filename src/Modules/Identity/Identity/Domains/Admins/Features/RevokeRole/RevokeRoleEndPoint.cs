using Identity.Contracts.Admins.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.RevokeRole;

public class RevokeRoleEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/identity/users/{id:guid}/roles/{roleName}", async (
                [FromRoute] Guid id,
                [FromRoute] string roleName,
                [FromBody] RevokeRoleRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new RevokeRoleCommand
                {
                    UserId = id,
                    RoleName = roleName,
                    Reason = request.Reason,
                    RevokedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequirePermission(Permissions.UsersManage)
            .WithName("RevokeRole")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Revoke a role from a user")
            .WithDescription(
                "Revoke a role from a user. SuperAdmin required for Admin/SuperAdmin roles.")
            .Produces<ApiResponse<RevokeRoleResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record RevokeRoleRequest
{
    public string? Reason { get; init; }
}
