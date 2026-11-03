using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetAllFeatures;

public class GetAllFeaturesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/subscriptions/{subscriberId:guid}/features",
                async (Guid subscriberId, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GetAllFeaturesQuery(subscriberId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("GetAllFeatures")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<Dictionary<string, string>>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
