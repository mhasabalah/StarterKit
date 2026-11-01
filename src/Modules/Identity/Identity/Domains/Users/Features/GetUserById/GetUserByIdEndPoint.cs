using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Response;

namespace Identity.Domains.Users.Features.GetUserById;

public class GetUserByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/identity/users/{id:guid}", async (
            Guid id,
            [FromServices] ISender sender,
            CancellationToken cancellationToken) =>
            {
                var query = new GetUserByIdQuery(id);
                var result = await sender.Send(query, cancellationToken);
                var response = ApiResponse.Success(result);
                return Results.Ok(response);
            })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetUserById")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .WithSummary("Get user details by ID")
            .WithDescription("Retrieve detailed information about a specific user. Admin only.")
            .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
