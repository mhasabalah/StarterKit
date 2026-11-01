using Identity.Contracts.Admins.Dtos;
using Identity.Contracts.Admins.Queries;
using Identity.Domains.Roles.Constants;
using Shared.Core.Authorization;

namespace Identity.Domains.Admins.Features.GetAdmins;

public class GetAdminsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/identity/admins",
                async ([AsParameters] GetAdminsQuery query, ISender sender) =>
                {
                    var result = await sender.Send(query);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.SuperAdminOnly)
            .WithName("GetAdmins")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .Produces<ApiResponse<PaginatedResult<AdminListDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithSummary("Get Admins")
            .WithDescription(
                "Retrieve paginated list of admins with filtering and sorting. Admin or SuperAdmin.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
