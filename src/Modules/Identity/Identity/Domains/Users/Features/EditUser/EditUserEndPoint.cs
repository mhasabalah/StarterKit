using Identity.Contracts.Users.Commands;
using Identity.Contracts.Users.Dtos;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;

namespace Identity.Domains.Users.Features.EditUser;

public class EditUserEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/identity/users/{id:guid}", async (
                [FromRoute] Guid id,
                [FromBody] EditUserRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new EditUserCommand
                {
                    UserId = id,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    PhoneNumber = request.PhoneNumber
                };

                var result = await sender.Send(command, cancellationToken);
                var response = ApiResponse.Success(result, "User updated successfully");
                return Results.Ok(response);
            })
            .RequirePermission(Permissions.UsersManage)
            .WithName("EditUser")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .WithSummary("Edit user profile")
            .WithDescription("Edit a user's core profile fields (FirstName, LastName, Email, PhoneNumber). Admin only.")
            .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record EditUserRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string Email { get; init; } = default!;
    public string? PhoneNumber { get; init; }
}
