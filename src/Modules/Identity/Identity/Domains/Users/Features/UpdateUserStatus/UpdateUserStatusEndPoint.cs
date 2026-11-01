using Identity.Contracts.Users.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.UpdateUserStatus;

public class UpdateUserStatusEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/identity/users/{id:guid}/status", async (
                [FromRoute] Guid id,
                [FromBody] UpdateUserStatusRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new UpdateUserStatusCommand
                {
                    UserId = id,
                    IsActive = request.IsActive,
                    Reason = request.Reason,
                    UpdatedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("UpdateUserStatus")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .WithSummary("Update user account status")
            .WithDescription("Activate or deactivate a user account. Admin only.")
            .Produces<ApiResponse<UpdateUserStatusResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}

public record UpdateUserStatusRequest
{
    public bool IsActive { get; init; }
    public string? Reason { get; init; }
}
