using Carter;
using Identity.Contracts.Users.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Api.Endpoints;

public class DashboardEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/dashboard/stats",
                async (ISender sender, CancellationToken cancellationToken) =>
                {
                    var userStats = await sender.Send(new GetUserStatsQuery(), cancellationToken);

                    var result = new DashboardStatsDto
                    {
                        TotalUsers = userStats.TotalUsers
                    };

                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetDashboardStats")
            .WithApiVersionSet(app.NewApiVersionSet("Dashboard").Build())
            .WithTags("Dashboard")
            .Produces<ApiResponse<DashboardStatsDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get Dashboard Stats")
            .WithDescription("Aggregate admin dashboard metrics. Extend DashboardStatsDto with stats from other modules.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}

public class DashboardStatsDto
{
    public int TotalUsers { get; init; }
}
