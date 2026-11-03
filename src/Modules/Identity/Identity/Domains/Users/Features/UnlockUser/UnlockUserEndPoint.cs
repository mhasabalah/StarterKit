using Identity.Contracts.Users.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.UnlockUser;

public class UnlockUserEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/users/{id:guid}/unlock", async (
                [FromRoute] Guid id,
                [FromBody] UnlockUserRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new UnlockUserCommand
                {
                    UserId = id,
                    UnlockReason = request.UnlockReason,
                    UnlockedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequirePermission(Permissions.UsersManage)
            .WithName("UnlockUser")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .WithSummary("Unlock/reactivate a user account")
            .WithDescription("Remove lockout from a user account. Admin only.")
            .Produces<ApiResponse<UnlockUserResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record UnlockUserRequest
{
    public string? UnlockReason { get; init; }
}
