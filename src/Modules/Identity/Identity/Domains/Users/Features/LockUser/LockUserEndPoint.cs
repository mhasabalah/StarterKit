using Identity.Contracts.Users.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.LockUser;

public class LockUserEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/users/{id:guid}/lock", async (
                [FromRoute] Guid id,
                [FromBody] LockUserRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new LockUserCommand
                {
                    UserId = id,
                    Reason = request.Reason,
                    Duration = request.Duration,
                    CustomDurationHours = request.CustomDurationHours,
                    LockedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, result.Message);
                return Results.Ok(response);
            })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("LockUser")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .WithSummary("Lock/suspend a user account")
            .WithDescription(
                "Lock a user account for a specified duration with a reason. Admin only.")
            .Produces<ApiResponse<LockUserResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}

public record LockUserRequest
{
    public string Reason { get; init; } = default!;
    public LockDuration Duration { get; init; } = LockDuration.Permanent;
    public int? CustomDurationHours { get; init; }
}
