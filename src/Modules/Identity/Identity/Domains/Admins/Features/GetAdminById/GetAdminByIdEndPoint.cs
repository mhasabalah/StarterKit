using Identity.Contracts.Admins.Dtos;
using Identity.Contracts.Admins.Queries;
using Identity.Domains.Roles.Constants;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;

namespace Identity.Domains.Admins.Features.GetAdminById;

public class GetAdminByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/identity/admins/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var query = new GetAdminByIdQuery(id);
                var result = await sender.Send(query, cancellationToken);
                var response = ApiResponse.Success(result);
                return Results.Ok(response);
            })
            .RequireAuthorization(PolicyConstants.SuperAdminOnly)
            .WithName("GetAdminById")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .WithSummary("Get admin details by ID")
            .WithDescription(
                "Retrieve detailed information about a specific admin. Admin or SuperAdmin.")
            .Produces<ApiResponse<AdminDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
