using Identity.Contracts.Admins.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.AssignRole;

public class AssignRoleEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/users/{id:guid}/roles", async (
                [FromRoute] Guid id,
                [FromBody] AssignRoleRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new AssignRoleCommand
                {
                    UserId = id,
                    RoleName = request.RoleName,
                    Reason = request.Reason,
                    AssignedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequirePermission(Permissions.UsersManage)
            .WithName("AssignRole")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Assign a role to a user")
            .WithDescription(
                "Assign a role to a user. SuperAdmin required for Admin/SuperAdmin roles.")
            .Produces<ApiResponse<AssignRoleResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record AssignRoleRequest
{
    public string RoleName { get; init; } = default!;
    public string? Reason { get; init; }
}
