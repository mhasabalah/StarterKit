using Identity.Contracts.Admins.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.PromoteToAdmin;

public class PromoteToAdminEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/users/{id:guid}/promote-admin", async (
                [FromRoute] Guid id,
                [FromBody] PromoteToAdminRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new PromoteToAdminCommand
                {
                    UserId = id,
                    MakeSuperAdmin = request.MakeSuperAdmin,
                    Reason = request.Reason,
                    PromotedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequirePermission(Permissions.AdminsManage)
            .WithName("PromoteToAdmin")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Promote a user to admin")
            .WithDescription(
                "Promote a regular user to Admin. Admin or SuperAdmin. Only SuperAdmin can set MakeSuperAdmin=true.")
            .Produces<ApiResponse<PromoteToAdminResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record PromoteToAdminRequest
{
    public bool MakeSuperAdmin { get; init; }
    public string Reason { get; init; } = default!;
}
