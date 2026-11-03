using Identity.Contracts.Admins.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.CreateAdmin;

public class CreateAdminEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/admins", async (
                CreateAdminRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var adminId = httpContextService.GetUserIdAsGuid();
                if (adminId is null)
                    return Results.Unauthorized();

                var command = new CreateAdminCommand
                {
                    UserName = request.UserName,
                    Email = request.Email,
                    Password = request.Password,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    PhoneNumber = request.PhoneNumber,
                    IsSuperAdmin = request.IsSuperAdmin,
                    CreatedByAdminId = adminId.Value
                };

                var result = await sender.Send(command, cancellationToken);

                if (!result.Success)
                {
                    return Results.BadRequest(ApiResponse.Success(result, result.Message));
                }

                var response = ApiResponse.Success(result, result.Message);
                return Results.Created($"api/identity/admins/{result.AdminId}", response);
            })
            .RequirePermission(Permissions.AdminsManage)
            .WithName("CreateAdmin")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Create a new admin account")
            .WithDescription(
                "Create a new admin account. SuperAdmin only.")
            .Produces<ApiResponse<CreateAdminResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .HasApiVersion(1.0);
    }
}

public record CreateAdminRequest
{
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string Password { get; init; } = default!;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsSuperAdmin { get; init; }
}
