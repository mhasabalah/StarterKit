using Identity.Contracts.Admins.Commands;
using Identity.Contracts.Admins.Dtos;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;

namespace Identity.Domains.Admins.Features.UpdateAdmin;

public class UpdateAdminEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/identity/admins/{id:guid}", async (
                [FromRoute] Guid id,
                [FromBody] UpdateAdminRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdateAdminCommand
                {
                    AdminId = id,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    PhoneNumber = request.PhoneNumber
                };

                var result = await sender.Send(command, cancellationToken);
                return Results.Ok(ApiResponse.Success(result));
            })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("UpdateAdmin")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Update admin profile")
            .WithDescription(
                "Update an admin's profile details. Admin or SuperAdmin. Admins cannot edit SuperAdmin accounts.")
            .Produces<ApiResponse<AdminDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}

public record UpdateAdminRequest
{
    public string Email { get; init; } = default!;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
}
