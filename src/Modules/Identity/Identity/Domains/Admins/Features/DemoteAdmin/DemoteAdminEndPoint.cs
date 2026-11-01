using Identity.Contracts.Admins.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.DemoteAdmin;

public class DemoteAdminEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/admins/{id:guid}/demote", async (
                [FromRoute] Guid id,
                [FromBody] DemoteAdminRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new DemoteAdminCommand
                {
                    AdminId = id,
                    Reason = request.Reason,
                    DemotedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequireAuthorization(PolicyConstants.SuperAdminOnly)
            .WithName("DemoteAdmin")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Demote an admin to regular user")
            .WithDescription(
                "Remove all admin privileges from an admin user. Admin or SuperAdmin. Admins cannot demote SuperAdmin accounts.")
            .Produces<ApiResponse<DemoteAdminResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}

public record DemoteAdminRequest
{
    public string Reason { get; init; } = default!;
}
