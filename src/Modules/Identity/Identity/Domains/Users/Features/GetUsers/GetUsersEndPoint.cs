using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;
using Shared.Core.Authorization;

namespace Identity.Domains.Users.Features.GetUsers;

public class GetUsersEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/identity/users",
                async ([AsParameters] GetUsersQuery query, ISender sender) =>
                {
                    var result = await sender.Send(query);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetUsers")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .Produces<ApiResponse<PaginatedResult<UserListDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithSummary("Get Users")
            .WithDescription("Retrieve paginated list of users with filtering and sorting. Admin only.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
