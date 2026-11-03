using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetAdminPlans;

public class GetAdminPlansEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/plans",
                async ([AsParameters] GetAdminPlansQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.PlansManage)
            .RequireRateLimiting("search")
            .WithName("GetAdminPlans")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<PaginatedResult<AdminPlanDto>>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
