using Identity.Contracts.Admins.Commands;
using Identity.Domains.Roles.Constants;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.ChangeAdminPassword;

public class ChangeAdminPasswordEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/identity/admins/{id:guid}/change-password", async (
                [FromRoute] Guid id,
                [FromBody] ChangeAdminPasswordRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var callerId = httpContextService.GetUserIdAsGuid();
                if (callerId is null)
                    return Results.Unauthorized();

                var command = new AdminChangePasswordCommand
                {
                    AdminId = id,
                    NewPassword = request.NewPassword,
                    ChangedByAdminId = callerId.Value
                };

                await sender.Send(command, cancellationToken);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.AdminsManage)
            .DisallowApiKey()
            .WithName("ChangeAdminPassword")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Change admin password")
            .WithDescription(
                "Reset an admin's password without requiring the old one. Admin or SuperAdmin. Admins cannot change a SuperAdmin's password.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record ChangeAdminPasswordRequest
{
    public string NewPassword { get; init; } = default!;
}
