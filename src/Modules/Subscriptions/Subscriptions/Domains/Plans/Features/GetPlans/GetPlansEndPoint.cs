using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetPlans;

public class GetPlansEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/plans", async (ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetPlansQuery(), ct);
                return Results.Ok(ApiResponseExtensions.Success(result));
            })
            .RequireRateLimiting("search")
            .WithName("GetPlans")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<List<PlanDto>>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
