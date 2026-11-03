using Identity.Contracts.Users.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.DeleteUser;

public class DeleteUserEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/identity/users/{id:guid}", async (
                [FromRoute] Guid id,
                [FromBody] DeleteUserRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new DeleteUserCommand
                {
                    UserId = id,
                    Reason = request.Reason,
                    DeletedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequireAuthorization(PolicyConstants.SuperAdminOnly) // Only super admins can delete users
            .WithName("DeleteUser")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .WithSummary("Delete a user account (soft delete)")
            .WithDescription("Permanently lock and deactivate a user account. Super admin only.")
            .Produces<ApiResponse<DeleteUserResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record DeleteUserRequest
{
    public string Reason { get; init; } = default!;
}
